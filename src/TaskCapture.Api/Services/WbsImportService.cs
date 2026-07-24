using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TaskCapture.Api.Contracts;
using TaskCapture.Api.Data;

namespace TaskCapture.Api.Services;

public sealed class WbsImportService(
    TaskCaptureDbContext db,
    IAsanaTaskService asanaTaskService,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<WbsImportProfileResponse>> GetProfilesAsync(
        string clientKey,
        CancellationToken cancellationToken)
    {
        var user = await GetOrCreateUserAsync(clientKey, cancellationToken);
        var profiles = await db.WbsImportProfiles.AsNoTracking()
            .Where(profile => profile.UserId == user.Id)
            .OrderBy(profile => profile.Name)
            .ToListAsync(cancellationToken);
        return profiles.Select(ToResponse).ToArray();
    }

    public async Task<WbsImportProfileResponse> SaveProfileAsync(
        Guid? profileId,
        WbsImportProfileRequest input,
        string clientKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var user = await GetOrCreateUserAsync(clientKey, cancellationToken);
        var now = timeProvider.GetUtcNow();
        WbsImportProfile profile;
        if (profileId is null)
        {
            profile = new WbsImportProfile
            {
                UserId = user.Id,
                CreatedAtUtc = now
            };
            db.WbsImportProfiles.Add(profile);
        }
        else
        {
            profile = await db.WbsImportProfiles
                .SingleOrDefaultAsync(item => item.Id == profileId && item.UserId == user.Id, cancellationToken)
                ?? throw new KeyNotFoundException("WBS import profile was not found.");
        }

        var duplicateName = await db.WbsImportProfiles.AnyAsync(
            item => item.UserId == user.Id &&
                item.Name == input.Name.Trim() &&
                item.Id != profile.Id,
            cancellationToken);
        if (duplicateName)
        {
            throw new InvalidOperationException("同じ名前のWBSテンプレートが既にあります。");
        }

        profile.Name = input.Name.Trim();
        profile.LayoutSignature = input.LayoutSignature.ToLowerInvariant();
        profile.SheetName = input.SheetName.Trim();
        profile.HeaderRow = input.HeaderRow;
        profile.DataStartRow = input.DataStartRow;
        profile.MappingJson = JsonSerializer.Serialize(input.Mapping, JsonOptions);
        profile.ProjectGid = NullIfWhiteSpace(input.ProjectGid);
        profile.SectionGid = NullIfWhiteSpace(input.SectionGid);
        profile.UpdatedAtUtc = now;
        AddAudit(user.Id, "WbsProfileSaved", "WbsImportProfile", profile.Id, profile.Name, correlationId, now);
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(profile);
    }

    public async Task DeleteProfileAsync(
        Guid profileId,
        string clientKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var user = await GetOrCreateUserAsync(clientKey, cancellationToken);
        var profile = await db.WbsImportProfiles
            .SingleOrDefaultAsync(item => item.Id == profileId && item.UserId == user.Id, cancellationToken)
            ?? throw new KeyNotFoundException("WBS import profile was not found.");
        var referencedBatches = await db.WbsImportBatches
            .Where(batch => batch.WbsImportProfileId == profile.Id)
            .ToListAsync(cancellationToken);
        foreach (var batch in referencedBatches)
        {
            batch.WbsImportProfileId = null;
        }

        db.WbsImportProfiles.Remove(profile);
        AddAudit(user.Id, "WbsProfileDeleted", "WbsImportProfile", profile.Id, profile.Name, correlationId, timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<WbsImportBatchResponse> CreateBatchAsync(
        WbsImportBatchRequest input,
        string clientKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var user = await GetOrCreateUserAsync(clientKey, cancellationToken);
        if (input.ProfileId is not null)
        {
            var profileExists = await db.WbsImportProfiles.AnyAsync(
                profile => profile.Id == input.ProfileId && profile.UserId == user.Id,
                cancellationToken);
            if (!profileExists)
            {
                throw new KeyNotFoundException("WBS import profile was not found.");
            }
        }

        var now = timeProvider.GetUtcNow();
        var batch = new WbsImportBatch
        {
            UserId = user.Id,
            WbsImportProfileId = input.ProfileId,
            FileName = input.FileName.Trim(),
            FileHash = input.FileHash.ToLowerInvariant(),
            SheetName = input.SheetName.Trim(),
            LayoutSignature = input.LayoutSignature.ToLowerInvariant(),
            ProjectGid = NullIfWhiteSpace(input.ProjectGid),
            SectionGid = NullIfWhiteSpace(input.SectionGid),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        var inputByKey = input.Rows.ToDictionary(row => row.SourceKey.Trim(), StringComparer.Ordinal);
        var errors = inputByKey.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ValidationErrors
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim()[..Math.Min(value.Trim().Length, 200)])
                .Take(20)
                .ToList(),
            StringComparer.Ordinal);
        var depths = new Dictionary<string, int>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        int ResolveDepth(string key)
        {
            if (depths.TryGetValue(key, out var resolved)) return resolved;
            if (!visiting.Add(key))
            {
                errors[key].Add("親子関係が循環しています。");
                return 0;
            }

            var row = inputByKey[key];
            var parentKey = NullIfWhiteSpace(row.ParentSourceKey);
            var depth = 0;
            if (parentKey is not null)
            {
                if (!inputByKey.TryGetValue(parentKey, out var parent))
                {
                    errors[key].Add($"親キー「{parentKey}」が見つかりません。");
                }
                else
                {
                    depth = ResolveDepth(parentKey) + 1;
                    if (row.Included && !parent.Included)
                    {
                        errors[key].Add("登録対象の子タスクに、除外された親タスクが指定されています。");
                    }
                }
            }
            visiting.Remove(key);
            if (depth > 20)
            {
                errors[key].Add("階層は20段以下にしてください。");
            }
            depths[key] = Math.Min(depth, 20);
            return depths[key];
        }

        foreach (var pair in inputByKey)
        {
            var row = pair.Value;
            if (row.Included && string.IsNullOrWhiteSpace(row.Title))
            {
                errors[pair.Key].Add("タスクタイトルがありません。");
            }
            ResolveDepth(pair.Key);
        }

        var profileScope = input.ProfileId?.ToString("N") ?? input.LayoutSignature.ToLowerInvariant();
        var entitiesByKey = new Dictionary<string, WbsImportRow>(StringComparer.Ordinal);
        foreach (var inputRow in input.Rows.OrderBy(row => row.SortOrder))
        {
            var key = inputRow.SourceKey.Trim();
            var rowErrors = errors[key].Distinct(StringComparer.Ordinal).ToArray();
            var title = inputRow.Title.Trim();
            var description = inputRow.Description.Trim();
            var assignee = NullIfWhiteSpace(inputRow.Assignee);
            var identity = inputRow.IsGeneratedKey
                ? $"{user.Id:N}|{batch.ProjectGid}|{profileScope}|{batch.FileHash}|{batch.SheetName}|{inputRow.SourceRowNumber}"
                : $"{user.Id:N}|{batch.ProjectGid}|{profileScope}|{key}";
            var content = $"{title}\n{description}\n{assignee}\n{inputRow.DueDate:yyyy-MM-dd}";
            var entity = new WbsImportRow
            {
                WbsImportBatchId = batch.Id,
                SourceRowNumber = inputRow.SourceRowNumber,
                SourceKey = key,
                IsGeneratedKey = inputRow.IsGeneratedKey,
                RowHash = Sha256(identity),
                ContentHash = Sha256(content),
                Depth = depths[key],
                SortOrder = inputRow.SortOrder,
                Included = inputRow.Included,
                Title = title,
                Description = description,
                Assignee = assignee,
                DueDate = inputRow.DueDate,
                Status = !inputRow.Included ? "Excluded" : rowErrors.Length > 0 ? "Invalid" : "Ready",
                ValidationErrorsJson = JsonSerializer.Serialize(rowErrors, JsonOptions),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            batch.Rows.Add(entity);
            entitiesByKey[key] = entity;
        }

        foreach (var inputRow in input.Rows)
        {
            var parentKey = NullIfWhiteSpace(inputRow.ParentSourceKey);
            if (parentKey is not null && entitiesByKey.TryGetValue(parentKey, out var parent))
            {
                entitiesByKey[inputRow.SourceKey.Trim()].ParentRowId = parent.Id;
            }
        }

        UpdateBatchCounts(batch);
        batch.Status = batch.FailedRows > 0 ? "Invalid" : "Ready";
        db.WbsImportBatches.Add(batch);
        AddAudit(
            user.Id,
            "WbsBatchPreviewed",
            "WbsImportBatch",
            batch.Id,
            $"Rows={batch.TotalRows}; Valid={batch.ValidRows}; Invalid={batch.FailedRows}",
            correlationId,
            now);
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(batch, false);
    }

    public async Task<WbsImportBatchResponse> GetBatchAsync(
        Guid batchId,
        string clientKey,
        CancellationToken cancellationToken)
    {
        var user = await GetOrCreateUserAsync(clientKey, cancellationToken);
        var batch = await LoadBatchAsync(batchId, user.Id, cancellationToken);
        return ToResponse(batch, batch.Status == "Registered");
    }

    public async Task<WbsImportBatchResponse> RegisterBatchAsync(
        Guid batchId,
        string clientKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var user = await GetOrCreateUserAsync(clientKey, cancellationToken);
        var batch = await LoadBatchAsync(batchId, user.Id, cancellationToken);
        var actionableRows = batch.Rows.Where(row => row.Included && row.Status != "Invalid").ToArray();
        if (actionableRows.Length > 0 && actionableRows.All(IsSuccessful))
        {
            return ToResponse(batch, true);
        }
        if (batch.Status == "Registering")
        {
            throw new InvalidOperationException("このWBSは別の処理で登録中です。");
        }

        batch.Status = "Registering";
        batch.UpdatedAtUtc = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        var successfulByHash = new Dictionary<string, WbsImportRow>(StringComparer.Ordinal);
        var hashes = actionableRows.Select(row => row.RowHash).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var chunk in hashes.Chunk(500))
        {
            var previous = await db.WbsImportRows.AsNoTracking()
                .Where(row => row.WbsImportBatchId != batch.Id &&
                    chunk.Contains(row.RowHash) &&
                    (row.Status == "Registered" || row.Status == "Duplicate") &&
                    row.ExternalTaskGid != null)
                .OrderByDescending(row => row.UpdatedAtUtc)
                .ToListAsync(cancellationToken);
            foreach (var row in previous)
            {
                successfulByHash.TryAdd(row.RowHash, row);
            }
        }

        var rowsById = batch.Rows.ToDictionary(row => row.Id);
        foreach (var row in actionableRows.OrderBy(item => item.Depth).ThenBy(item => item.SortOrder))
        {
            if (IsSuccessful(row)) continue;
            if (successfulByHash.TryGetValue(row.RowHash, out var previous))
            {
                row.Status = "Duplicate";
                row.Provider = previous.Provider;
                row.ExternalTaskGid = previous.ExternalTaskGid;
                row.ExternalTaskUrl = previous.ExternalTaskUrl;
                row.WarningMessage = "同じ識別キーのタスクは登録済みのためスキップしました。";
                row.ErrorCode = null;
                row.ErrorMessage = null;
                row.UpdatedAtUtc = timeProvider.GetUtcNow();
                await db.SaveChangesAsync(cancellationToken);
                continue;
            }

            string? parentTaskGid = null;
            if (row.ParentRowId is not null)
            {
                var parent = rowsById[row.ParentRowId.Value];
                if (!IsSuccessful(parent) || string.IsNullOrWhiteSpace(parent.ExternalTaskGid))
                {
                    row.Status = "Blocked";
                    row.ErrorCode = "PARENT_NOT_REGISTERED";
                    row.ErrorMessage = "親タスクが登録できていないため、この行は登録していません。";
                    row.UpdatedAtUtc = timeProvider.GetUtcNow();
                    await db.SaveChangesAsync(cancellationToken);
                    continue;
                }
                parentTaskGid = parent.ExternalTaskGid;
            }

            var result = await CreateTaskSafelyAsync(
                new AsanaImportTask(
                    row.Title,
                    row.Description,
                    row.Assignee,
                    row.DueDate,
                    batch.ProjectGid,
                    batch.SectionGid),
                parentTaskGid,
                cancellationToken);
            row.Status = result.Succeeded ? "Registered" : "Failed";
            row.Provider = result.Provider;
            row.ExternalTaskGid = result.ExternalTaskGid;
            row.ExternalTaskUrl = result.ExternalTaskUrl;
            row.ErrorCode = result.ErrorCode;
            row.ErrorMessage = SafeMessage(result.ErrorMessage);
            row.AssigneeResolutionStatus = result.AssigneeResolutionStatus;
            row.ResolvedAssigneeGid = result.ResolvedAssigneeGid;
            row.ResolvedAssigneeName = result.ResolvedAssigneeName;
            row.WarningMessage = SafeWarning(result.WarningMessage);
            row.UpdatedAtUtc = timeProvider.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
        }

        UpdateBatchCounts(batch);
        var included = batch.Rows.Where(row => row.Included).ToArray();
        batch.Status = included.Length > 0 && included.All(IsSuccessful)
            ? "Registered"
            : included.Any(IsSuccessful) ? "PartiallyRegistered" : "Failed";
        batch.UpdatedAtUtc = timeProvider.GetUtcNow();
        AddAudit(
            user.Id,
            batch.Status == "Registered" ? "WbsBatchRegistered" : "WbsBatchPartiallyRegistered",
            "WbsImportBatch",
            batch.Id,
            $"Status={batch.Status}; Succeeded={batch.SucceededRows}; Failed={batch.FailedRows}",
            correlationId,
            batch.UpdatedAtUtc,
            batch.Status == "Registered" ? "Information" : "Error");
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(batch, false);
    }

    public async Task<byte[]> GetErrorCsvAsync(
        Guid batchId,
        string clientKey,
        CancellationToken cancellationToken)
    {
        var user = await GetOrCreateUserAsync(clientKey, cancellationToken);
        var batch = await LoadBatchAsync(batchId, user.Id, cancellationToken);
        var lines = new List<string>
        {
            "source_row,source_key,title,status,error"
        };
        lines.AddRange(batch.Rows
            .Where(row => row.Included && !IsSuccessful(row))
            .OrderBy(row => row.SortOrder)
            .Select(row => string.Join(
                ",",
                row.SourceRowNumber,
                Csv(row.SourceKey),
                Csv(row.Title),
                Csv(row.Status),
                Csv(row.ErrorMessage ?? string.Join(" / ", DeserializeErrors(row.ValidationErrorsJson))))));
        var content = string.Join("\r\n", lines) + "\r\n";
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(content)];
    }

    private async Task<WbsImportBatch> LoadBatchAsync(Guid batchId, Guid userId, CancellationToken cancellationToken) =>
        await db.WbsImportBatches
            .Include(batch => batch.Rows)
            .SingleOrDefaultAsync(batch => batch.Id == batchId && batch.UserId == userId, cancellationToken)
            ?? throw new KeyNotFoundException("WBS import batch was not found.");

    private async Task<User> GetOrCreateUserAsync(string clientKey, CancellationToken cancellationToken)
    {
        var trimmed = string.IsNullOrWhiteSpace(clientKey) ? "local-device" : clientKey.Trim();
        var normalized = trimmed[..Math.Min(trimmed.Length, 128)];
        var user = await db.Users.SingleOrDefaultAsync(item => item.ClientKey == normalized, cancellationToken);
        if (user is not null) return user;
        user = new User
        {
            ClientKey = normalized,
            DisplayName = "Limited user",
            CreatedAtUtc = timeProvider.GetUtcNow()
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        return user;
    }

    private async Task<AsanaRegistrationResult> CreateTaskSafelyAsync(
        AsanaImportTask task,
        string? parentTaskGid,
        CancellationToken cancellationToken)
    {
        try
        {
            return await asanaTaskService.CreateImportTaskAsync(task, parentTaskGid, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new AsanaRegistrationResult(
                false,
                "Configuration",
                null,
                null,
                "INTEGRATION_ERROR",
                SafeMessage(ex.Message));
        }
    }

    private static void UpdateBatchCounts(WbsImportBatch batch)
    {
        batch.TotalRows = batch.Rows.Count;
        batch.ValidRows = batch.Rows.Count(row => row.Included && row.Status != "Invalid");
        batch.SucceededRows = batch.Rows.Count(IsSuccessful);
        batch.FailedRows = batch.Rows.Count(row => row.Included && !IsSuccessful(row) && row.Status != "Ready");
    }

    private void AddAudit(
        Guid userId,
        string eventType,
        string entityType,
        Guid entityId,
        string? detail,
        string correlationId,
        DateTimeOffset now,
        string level = "Information") =>
        db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            EventType = eventType,
            EntityType = entityType,
            EntityId = entityId.ToString(),
            Level = level,
            Detail = SafeMessage(detail),
            CorrelationId = correlationId,
            CreatedAtUtc = now
        });

    private static WbsImportProfileResponse ToResponse(WbsImportProfile profile) => new(
        profile.Id,
        profile.Name,
        profile.LayoutSignature,
        profile.SheetName,
        profile.HeaderRow,
        profile.DataStartRow,
        JsonSerializer.Deserialize<WbsMappingRequest>(profile.MappingJson, JsonOptions) ?? new WbsMappingRequest(),
        profile.ProjectGid,
        profile.SectionGid,
        profile.UpdatedAtUtc);

    private static WbsImportBatchResponse ToResponse(WbsImportBatch batch, bool alreadyRegistered) => new(
        batch.Id,
        batch.Status,
        alreadyRegistered,
        batch.TotalRows,
        batch.ValidRows,
        batch.SucceededRows,
        batch.FailedRows,
        batch.Rows.OrderBy(row => row.SortOrder).Select(row => new WbsImportRowResponse(
            row.Id,
            row.ParentRowId,
            row.SourceRowNumber,
            row.SourceKey,
            row.Depth,
            row.SortOrder,
            row.Included,
            row.Title,
            row.Description,
            row.Assignee,
            row.DueDate,
            row.Status,
            DeserializeErrors(row.ValidationErrorsJson),
            row.Provider,
            row.ExternalTaskGid,
            row.ExternalTaskUrl,
            row.ErrorMessage,
            row.AssigneeResolutionStatus,
            row.ResolvedAssigneeName,
            row.WarningMessage)).ToArray());

    private static IReadOnlyList<string> DeserializeErrors(string value) =>
        JsonSerializer.Deserialize<string[]>(value, JsonOptions) ?? [];

    private static bool IsSuccessful(WbsImportRow row) =>
        row.Status is "Registered" or "Duplicate";

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string Csv(string? value) =>
        $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SafeMessage(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value[..Math.Min(value.Length, 1_000)];

    private static string? SafeWarning(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value[..Math.Min(value.Length, 500)];
}
