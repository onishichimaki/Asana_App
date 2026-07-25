using System.Globalization;
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

    public async Task<IReadOnlyList<WbsColumnAliasResponse>> GetColumnAliasesAsync(
        string clientKey,
        CancellationToken cancellationToken)
    {
        var user = await GetOrCreateUserAsync(clientKey, cancellationToken);
        return await db.WbsColumnAliases.AsNoTracking()
            .Where(alias => alias.UserId == user.Id)
            .OrderByDescending(alias => alias.UpdatedAtUtc)
            .Select(alias => new WbsColumnAliasResponse(alias.ColumnName, alias.Role))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WbsImportBatchSummaryResponse>> GetHistoryAsync(
        string clientKey,
        int take,
        CancellationToken cancellationToken)
    {
        var user = await GetOrCreateUserAsync(clientKey, cancellationToken);
        var batches = await db.WbsImportBatches.AsNoTracking()
            .Include(batch => batch.Rows)
            .Where(batch => batch.UserId == user.Id)
            .OrderByDescending(batch => batch.CreatedAtUtc)
            .Take(Math.Clamp(take, 1, 50))
            .ToListAsync(cancellationToken);
        return batches.Select(ToSummaryResponse).ToArray();
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
                ?? throw new KeyNotFoundException("保存した読み取り方が見つかりません。");
        }

        var duplicateName = await db.WbsImportProfiles.AnyAsync(
            item => item.UserId == user.Id &&
                item.Name == input.Name.Trim() &&
                item.Id != profile.Id,
            cancellationToken);
        if (duplicateName)
        {
            throw new InvalidOperationException("同じ名前の読み取り方が既にあります。");
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

        foreach (var pair in input.ColumnNames)
        {
            if (!input.Mapping.Roles.TryGetValue(pair.Key, out var role) || role == "ignore")
            {
                continue;
            }
            var columnName = pair.Value.Trim();
            var normalizedName = NormalizeColumnName(columnName);
            if (normalizedName.Length == 0) continue;
            var alias = await db.WbsColumnAliases.SingleOrDefaultAsync(
                item => item.UserId == user.Id && item.NormalizedColumnName == normalizedName,
                cancellationToken);
            if (alias is null)
            {
                alias = new WbsColumnAlias
                {
                    UserId = user.Id,
                    ColumnName = columnName,
                    NormalizedColumnName = normalizedName,
                    Role = role,
                    CreatedAtUtc = now
                };
                db.WbsColumnAliases.Add(alias);
            }
            else
            {
                alias.ColumnName = columnName;
                alias.Role = role;
            }
            alias.UpdatedAtUtc = now;
        }

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
            ?? throw new KeyNotFoundException("保存した読み取り方が見つかりません。");
        var referencedBatches = await db.WbsImportBatches
            .Where(batch => batch.WbsImportProfileId == profile.Id)
            .ToListAsync(cancellationToken);
        foreach (var batch in referencedBatches)
        {
            batch.WbsImportProfileId = null;
        }

        db.WbsImportProfiles.Remove(profile);
        AddAudit(
            user.Id,
            "WbsProfileDeleted",
            "WbsImportProfile",
            profile.Id,
            profile.Name,
            correlationId,
            timeProvider.GetUtcNow());
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
                throw new KeyNotFoundException("保存した読み取り方が見つかりません。");
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
            CustomFieldTargetsJson = JsonSerializer.Serialize(input.CustomFieldTargets, JsonOptions),
            PutUnmatchedExtraFieldsInDescription = input.PutUnmatchedExtraFieldsInDescription,
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
                    errors[key].Add($"親の行「{parentKey}」が見つかりません。");
                }
                else
                {
                    depth = ResolveDepth(parentKey) + 1;
                    if (row.Included && !parent.Included)
                    {
                        errors[key].Add("登録する子タスクの親が、登録しない設定になっています。");
                    }
                }
            }
            visiting.Remove(key);
            if (depth > 20)
            {
                errors[key].Add("親子の深さは20段以下にしてください。");
            }
            depths[key] = Math.Min(depth, 20);
            return depths[key];
        }

        foreach (var pair in inputByKey)
        {
            var row = pair.Value;
            if (row.Included && string.IsNullOrWhiteSpace(row.Title))
            {
                errors[pair.Key].Add("タスク名がありません。");
            }
            ResolveDepth(pair.Key);
        }

        var profileScope = input.ProfileId?.ToString("N") ?? input.LayoutSignature.ToLowerInvariant();
        var entitiesByKey = new Dictionary<string, WbsImportRow>(StringComparer.Ordinal);
        foreach (var inputRow in input.Rows.OrderBy(row => row.SortOrder))
        {
            var key = inputRow.SourceKey.Trim();
            var rowErrors = errors[key].Distinct(StringComparer.Ordinal).ToArray();
            var identity = inputRow.IsGeneratedKey
                ? $"{user.Id:N}|{batch.ProjectGid}|{profileScope}|{batch.FileName}|{batch.SheetName}|{inputRow.SourceRowNumber}"
                : $"{user.Id:N}|{batch.ProjectGid}|{profileScope}|{key}";
            var entity = new WbsImportRow
            {
                WbsImportBatchId = batch.Id,
                SourceRowNumber = inputRow.SourceRowNumber,
                SourceKey = key,
                IsGeneratedKey = inputRow.IsGeneratedKey,
                RowHash = Sha256(identity),
                Depth = depths[key],
                SortOrder = inputRow.SortOrder,
                Included = inputRow.Included,
                Title = inputRow.Title.Trim(),
                Description = inputRow.Description.Trim(),
                Assignee = NullIfWhiteSpace(inputRow.Assignee),
                StartDate = inputRow.StartDate,
                DueDate = inputRow.DueDate,
                Progress = NullIfWhiteSpace(inputRow.Progress),
                Priority = NullIfWhiteSpace(inputRow.Priority),
                EstimatedHours = inputRow.EstimatedHours,
                EstimatedCost = inputRow.EstimatedCost,
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
                var child = entitiesByKey[inputRow.SourceKey.Trim()];
                child.ParentRowId = parent.Id;
                child.ParentRow = parent;
            }
        }

        RollUpParentDates(batch.Rows);
        foreach (var row in batch.Rows)
        {
            row.ContentHash = ContentHash(row, batch);
        }

        await ClassifyChangesAsync(batch, cancellationToken);
        await ResolveAssigneesAsync(batch.Rows, cancellationToken);
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
        WbsRegisterBatchRequest input,
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
            throw new InvalidOperationException("この取り込みは別の処理で登録中です。");
        }

        batch.Status = "Registering";
        batch.UpdatedAtUtc = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        var rowsById = batch.Rows.ToDictionary(row => row.Id);
        foreach (var row in actionableRows.OrderBy(item => item.Depth).ThenBy(item => item.SortOrder))
        {
            if (IsSuccessful(row)) continue;
            if (row.ChangeType == "Unchanged" && row.PreviousExternalTaskGid is not null)
            {
                CopyPreviousTask(row, "Duplicate", "前回と同じ内容のため、登録は行いませんでした。");
                await db.SaveChangesAsync(cancellationToken);
                continue;
            }
            if (row.ChangeType == "Changed" &&
                row.PreviousExternalTaskGid is not null &&
                !input.UpdateExistingTasks)
            {
                CopyPreviousTask(row, "Skipped", "変更された内容を今回は上書きしませんでした。");
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
                    row.ErrorMessage = "親タスクを登録できていないため、このタスクは登録していません。";
                    row.UpdatedAtUtc = timeProvider.GetUtcNow();
                    await db.SaveChangesAsync(cancellationToken);
                    continue;
                }
                parentTaskGid = parent.ExternalTaskGid;
            }

            var importTask = BuildImportTask(row, batch);
            var isUpdate = row.ChangeType == "Changed" && row.PreviousExternalTaskGid is not null;
            var result = isUpdate
                ? await UpdateTaskSafelyAsync(importTask, row.PreviousExternalTaskGid!, cancellationToken)
                : await CreateTaskSafelyAsync(importTask, parentTaskGid, cancellationToken);
            row.Status = result.Succeeded ? isUpdate ? "Updated" : "Registered" : "Failed";
            row.Provider = result.Provider;
            row.ExternalTaskGid = result.ExternalTaskGid;
            row.ExternalTaskUrl = result.ExternalTaskUrl ?? (isUpdate ? row.PreviousExternalTaskUrl : null);
            row.ErrorCode = result.ErrorCode;
            row.ErrorMessage = SafeMessage(result.ErrorMessage);
            row.AssigneeResolutionStatus = result.AssigneeResolutionStatus;
            row.ResolvedAssigneeGid = result.ResolvedAssigneeGid;
            row.ResolvedAssigneeName = result.ResolvedAssigneeName;
            row.WarningMessage = SafeWarning(result.WarningMessage);
            row.WasCreatedInBatch = result.Succeeded && !isUpdate;
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

    public async Task<WbsImportBatchResponse> UndoBatchAsync(
        Guid batchId,
        string clientKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var user = await GetOrCreateUserAsync(clientKey, cancellationToken);
        var batch = await LoadBatchAsync(batchId, user.Id, cancellationToken);
        var targets = batch.Rows
            .Where(row =>
                row.WasCreatedInBatch &&
                row.Status == "Registered" &&
                row.ExternalTaskGid != null)
            .OrderByDescending(row => row.Depth)
            .ThenByDescending(row => row.SortOrder)
            .ToArray();
        if (targets.Length == 0)
        {
            throw new InvalidOperationException("この取り込みで新しく作った、元に戻せるタスクはありません。");
        }

        foreach (var row in targets)
        {
            var result = await DeleteTaskSafelyAsync(row.ExternalTaskGid!, cancellationToken);
            row.Provider = result.Provider;
            row.ErrorCode = result.ErrorCode;
            row.ErrorMessage = SafeMessage(result.ErrorMessage);
            row.Status = result.Succeeded ? "Reverted" : "RevertFailed";
            row.RevertedAtUtc = result.Succeeded ? timeProvider.GetUtcNow() : null;
            row.UpdatedAtUtc = timeProvider.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
        }

        UpdateBatchCounts(batch);
        batch.Status = targets.All(row => row.Status == "Reverted") ? "Reverted" : "PartiallyReverted";
        batch.UpdatedAtUtc = timeProvider.GetUtcNow();
        AddAudit(
            user.Id,
            "WbsBatchReverted",
            "WbsImportBatch",
            batch.Id,
            $"Reverted={targets.Count(row => row.Status == "Reverted")}; Failed={targets.Count(row => row.Status == "RevertFailed")}",
            correlationId,
            batch.UpdatedAtUtc,
            batch.Status == "Reverted" ? "Information" : "Error");
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
            "元の行,行の番号,タスク名,状態,エラー"
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

    private async Task ClassifyChangesAsync(WbsImportBatch batch, CancellationToken cancellationToken)
    {
        var hashes = batch.Rows
            .Where(row => row.Included && row.Status != "Invalid")
            .Select(row => row.RowHash)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var previousByHash = new Dictionary<string, WbsImportRow>(StringComparer.Ordinal);
        foreach (var chunk in hashes.Chunk(500))
        {
            var previous = await db.WbsImportRows.AsNoTracking()
                .Where(row =>
                    chunk.Contains(row.RowHash) &&
                    (row.Status == "Registered" ||
                     row.Status == "Updated" ||
                     row.Status == "Duplicate" ||
                     row.Status == "Skipped") &&
                    row.ExternalTaskGid != null)
                .OrderByDescending(row => row.UpdatedAtUtc)
                .ToListAsync(cancellationToken);
            foreach (var row in previous)
            {
                previousByHash.TryAdd(row.RowHash, row);
            }
        }

        foreach (var row in batch.Rows)
        {
            if (!row.Included || row.Status == "Invalid")
            {
                row.ChangeType = "NotApplicable";
                continue;
            }
            if (!previousByHash.TryGetValue(row.RowHash, out var previous))
            {
                row.ChangeType = "New";
                continue;
            }
            row.PreviousExternalTaskGid = previous.ExternalTaskGid;
            row.PreviousExternalTaskUrl = previous.ExternalTaskUrl;
            row.ChangeType = row.ContentHash == previous.ContentHash ? "Unchanged" : "Changed";
        }
    }

    private async Task ResolveAssigneesAsync(
        IReadOnlyList<WbsImportRow> rows,
        CancellationToken cancellationToken)
    {
        var requestedNames = rows
            .Where(row => row.Included && row.Status != "Invalid" && row.Assignee is not null)
            .Select(row => row.Assignee!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (requestedNames.Length == 0) return;

        IReadOnlyList<AsanaAssigneeResolution> resolutions;
        try
        {
            resolutions = await asanaTaskService.ResolveAssigneesAsync(requestedNames, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            foreach (var row in rows.Where(row => row.Assignee is not null))
            {
                row.AssigneeResolutionStatus = "Unavailable";
                row.WarningMessage = "担当者をAsanaで確認できませんでした。登録前に接続設定を確認してください。";
            }
            return;
        }

        var byName = resolutions.ToDictionary(item => item.RequestedName, StringComparer.Ordinal);
        foreach (var row in rows.Where(row => row.Assignee is not null))
        {
            if (!byName.TryGetValue(row.Assignee!, out var resolution)) continue;
            row.AssigneeResolutionStatus = resolution.Status;
            row.ResolvedAssigneeGid = resolution.ResolvedGid;
            row.ResolvedAssigneeName = resolution.ResolvedName;
            row.WarningMessage = SafeWarning(resolution.Message);
        }
    }

    private static void RollUpParentDates(IReadOnlyList<WbsImportRow> rows)
    {
        var childrenByParent = rows
            .Where(row => row.ParentRowId is not null && row.Included && row.Status != "Invalid")
            .GroupBy(row => row.ParentRowId!.Value)
            .ToDictionary(group => group.Key, group => group.ToArray());
        foreach (var parent in rows.OrderByDescending(row => row.Depth))
        {
            if (!childrenByParent.TryGetValue(parent.Id, out var children)) continue;
            var starts = children.Where(child => child.StartDate is not null).Select(child => child.StartDate!.Value).ToArray();
            var dues = children.Where(child => child.DueDate is not null).Select(child => child.DueDate!.Value).ToArray();
            if (starts.Length > 0) parent.StartDate = starts.Min();
            if (dues.Length > 0) parent.DueDate = dues.Max();
        }
    }

    private static AsanaImportTask BuildImportTask(WbsImportRow row, WbsImportBatch batch)
    {
        var targets = JsonSerializer.Deserialize<Dictionary<string, string>>(
            batch.CustomFieldTargetsJson,
            JsonOptions) ?? [];
        var values = new (string Key, string Label, string? Value)[]
        {
            ("progress", "進み具合", row.Progress),
            ("priority", "優先度", row.Priority),
            ("estimatedHours", "予定時間", row.EstimatedHours?.ToString(CultureInfo.InvariantCulture)),
            ("estimatedCost", "予定費用", row.EstimatedCost?.ToString(CultureInfo.InvariantCulture))
        };
        var extraFields = values
            .Where(value => value.Value is not null && targets.ContainsKey(value.Key))
            .Select(value => new AsanaCustomFieldInput(targets[value.Key], value.Label, value.Value!))
            .ToArray();
        var description = row.Description;
        if (batch.PutUnmatchedExtraFieldsInDescription)
        {
            var additions = values
                .Where(value => value.Value is not null && !targets.ContainsKey(value.Key))
                .Select(value => $"{value.Label}: {value.Value}")
                .ToArray();
            if (additions.Length > 0)
            {
                description = string.Join(
                    "\n\n",
                    new[] { NullIfWhiteSpace(description), string.Join("\n", additions) }
                        .Where(value => value is not null));
            }
        }

        return new AsanaImportTask(
            row.Title,
            description,
            row.Assignee,
            row.StartDate,
            row.DueDate,
            batch.ProjectGid,
            batch.SectionGid,
            extraFields);
    }

    private static string ContentHash(WbsImportRow row, WbsImportBatch batch)
    {
        var parentSourceKey = row.ParentRow?.SourceKey;
        var content = string.Join(
            "\n",
            row.Title,
            row.Description,
            row.Assignee,
            row.StartDate?.ToString("yyyy-MM-dd"),
            row.DueDate?.ToString("yyyy-MM-dd"),
            row.Progress,
            row.Priority,
            row.EstimatedHours?.ToString(CultureInfo.InvariantCulture),
            row.EstimatedCost?.ToString(CultureInfo.InvariantCulture),
            parentSourceKey,
            batch.CustomFieldTargetsJson,
            batch.PutUnmatchedExtraFieldsInDescription);
        return Sha256(content);
    }

    private static void CopyPreviousTask(WbsImportRow row, string status, string warning)
    {
        row.Status = status;
        row.ExternalTaskGid = row.PreviousExternalTaskGid;
        row.ExternalTaskUrl = row.PreviousExternalTaskUrl;
        row.WarningMessage = warning;
        row.ErrorCode = null;
        row.ErrorMessage = null;
        row.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private async Task<WbsImportBatch> LoadBatchAsync(
        Guid batchId,
        Guid userId,
        CancellationToken cancellationToken) =>
        await db.WbsImportBatches
            .Include(batch => batch.Rows)
            .SingleOrDefaultAsync(batch => batch.Id == batchId && batch.UserId == userId, cancellationToken)
            ?? throw new KeyNotFoundException("取り込み履歴が見つかりません。");

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
            return IntegrationError(ex);
        }
    }

    private async Task<AsanaRegistrationResult> UpdateTaskSafelyAsync(
        AsanaImportTask task,
        string taskGid,
        CancellationToken cancellationToken)
    {
        try
        {
            return await asanaTaskService.UpdateImportTaskAsync(task, taskGid, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return IntegrationError(ex);
        }
    }

    private async Task<AsanaActionResult> DeleteTaskSafelyAsync(
        string taskGid,
        CancellationToken cancellationToken)
    {
        try
        {
            return await asanaTaskService.DeleteTaskAsync(taskGid, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new AsanaActionResult(false, "Configuration", "INTEGRATION_ERROR", SafeMessage(ex.Message));
        }
    }

    private static AsanaRegistrationResult IntegrationError(Exception exception) =>
        new(
            false,
            "Configuration",
            null,
            null,
            "INTEGRATION_ERROR",
            SafeMessage(exception.Message));

    private static void UpdateBatchCounts(WbsImportBatch batch)
    {
        batch.TotalRows = batch.Rows.Count;
        batch.ValidRows = batch.Rows.Count(row => row.Included && row.Status != "Invalid");
        batch.SucceededRows = batch.Rows.Count(IsSuccessful);
        batch.FailedRows = batch.Rows.Count(row =>
            row.Included &&
            !IsSuccessful(row) &&
            row.Status is not "Ready" and not "Reverted");
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

    private static WbsImportBatchSummaryResponse ToSummaryResponse(WbsImportBatch batch)
    {
        var (newRows, changedRows, unchangedRows) = ChangeCounts(batch);
        return new WbsImportBatchSummaryResponse(
            batch.Id,
            batch.FileName,
            batch.SheetName,
            batch.Status,
            batch.TotalRows,
            batch.SucceededRows,
            batch.FailedRows,
            newRows,
            changedRows,
            unchangedRows,
            CanUndo(batch),
            batch.CreatedAtUtc,
            batch.UpdatedAtUtc);
    }

    private static WbsImportBatchResponse ToResponse(WbsImportBatch batch, bool alreadyRegistered)
    {
        var parentKeys = batch.Rows.ToDictionary(row => row.Id, row => row.SourceKey);
        var (newRows, changedRows, unchangedRows) = ChangeCounts(batch);
        return new WbsImportBatchResponse(
            batch.Id,
            batch.FileName,
            batch.SheetName,
            batch.ProjectGid,
            batch.SectionGid,
            batch.Status,
            alreadyRegistered,
            batch.TotalRows,
            batch.ValidRows,
            batch.SucceededRows,
            batch.FailedRows,
            newRows,
            changedRows,
            unchangedRows,
            CanUndo(batch),
            batch.CreatedAtUtc,
            batch.UpdatedAtUtc,
            batch.Rows.OrderBy(row => row.SortOrder).Select(row => new WbsImportRowResponse(
                row.Id,
                row.ParentRowId,
                row.ParentRowId is not null && parentKeys.TryGetValue(row.ParentRowId.Value, out var key) ? key : null,
                row.SourceRowNumber,
                row.SourceKey,
                row.Depth,
                row.SortOrder,
                row.Included,
                row.Title,
                row.Description,
                row.Assignee,
                row.StartDate,
                row.DueDate,
                row.Progress,
                row.Priority,
                row.EstimatedHours,
                row.EstimatedCost,
                row.Status,
                row.ChangeType,
                DeserializeErrors(row.ValidationErrorsJson),
                row.Provider,
                row.ExternalTaskGid,
                row.ExternalTaskUrl,
                row.ErrorMessage,
                row.AssigneeResolutionStatus,
                row.ResolvedAssigneeName,
                row.WarningMessage,
                row.PreviousExternalTaskUrl,
                row.WasCreatedInBatch,
                row.RevertedAtUtc)).ToArray());
    }

    private static (int NewRows, int ChangedRows, int UnchangedRows) ChangeCounts(WbsImportBatch batch) =>
        (
            batch.Rows.Count(row => row.Included && row.ChangeType == "New"),
            batch.Rows.Count(row => row.Included && row.ChangeType == "Changed"),
            batch.Rows.Count(row => row.Included && row.ChangeType == "Unchanged")
        );

    private static bool CanUndo(WbsImportBatch batch) =>
        batch.Rows.Any(row => row.WasCreatedInBatch && row.Status == "Registered");

    private static IReadOnlyList<string> DeserializeErrors(string value) =>
        JsonSerializer.Deserialize<string[]>(value, JsonOptions) ?? [];

    private static bool IsSuccessful(WbsImportRow row) =>
        row.Status is "Registered" or "Updated" or "Duplicate" or "Skipped";

    private static string NormalizeColumnName(string value) =>
        string.Concat(value.Normalize(NormalizationForm.FormKC).Where(char.IsLetterOrDigit)).ToUpperInvariant();

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
