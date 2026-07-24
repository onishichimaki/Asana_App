[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$ServerInstance,

    [ValidatePattern('^[A-Za-z0-9_-]+$')]
    [string]$Database = 'TaskCapture',

    [ValidateRange(1024, 65535)]
    [int]$Port = 5091
)

$ErrorActionPreference = 'Stop'
$project = 'src/TaskCapture.Api/TaskCapture.Api.csproj'
$baseUrl = "http://127.0.0.1:$Port"
$connectionString =
    "Server=$ServerInstance;Database=$Database;Integrated Security=True;TrustServerCertificate=True;MultipleActiveResultSets=True"
$existingListener = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
if ($existingListener) {
    throw "Port $Port is already in use. Stop the existing listener or choose another port."
}
$taskTemp = Join-Path ([System.IO.Path]::GetTempPath()) (
    'taskcapture-sql-smoke-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskTemp | Out-Null

$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Database__Provider = 'SqlServer'
$env:Database__ApplyMigrations = 'true'
$env:ConnectionStrings__TaskCapture = $connectionString
$env:Integration__Asana__Mode = 'Mock'

function Start-TaskCaptureApi([string]$Suffix) {
    $stdout = Join-Path $taskTemp "api-$Suffix.out.log"
    $stderr = Join-Path $taskTemp "api-$Suffix.err.log"
    $process = Start-Process -FilePath 'dotnet' `
        -ArgumentList @(
            'run',
            '--project', $project,
            '--no-build',
            '--no-launch-profile',
            '--urls', $baseUrl) `
        -WorkingDirectory (Get-Location).Path `
        -WindowStyle Hidden `
        -RedirectStandardOutput $stdout `
        -RedirectStandardError $stderr `
        -PassThru

    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        if ($process.HasExited) {
            $output = Get-Content -Raw $stdout -ErrorAction SilentlyContinue
            $errorOutput = Get-Content -Raw $stderr -ErrorAction SilentlyContinue
            throw "API exited during startup. stdout=$output stderr=$errorOutput"
        }

        try {
            $health = Invoke-RestMethod -Uri "$baseUrl/api/health" -TimeoutSec 2
            if ($health.status -eq 'ok' -and
                $health.database -eq 'SqlServer' -and
                $health.asana -eq 'Mock') {
                return $process
            }
        }
        catch {
            # The API may not be listening yet.
        }
        Start-Sleep -Milliseconds 500
    }

    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    throw 'Timed out while waiting for the API to start.'
}

function Stop-TaskCaptureApi($Process) {
    if ($Process -and -not $Process.HasExited) {
        Stop-Process -Id $Process.Id
        Wait-Process -Id $Process.Id -Timeout 10 -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 500
    }
}

$firstProcess = $null
$secondProcess = $null

try {
    $marker = 'SQL persistence smoke ' + (Get-Date -Format 'yyyyMMdd-HHmmss')
    $headers = @{ 'X-TaskCapture-Client' = 'sql-server-smoke' }

    $firstProcess = Start-TaskCaptureApi 'first'
    $spaResponse = Invoke-WebRequest -Uri "$baseUrl/"
    if ($spaResponse.StatusCode -ne 200 -or $spaResponse.Content -notmatch '<div id="root"></div>') {
        throw 'The React SPA was not served by the API.'
    }
    $organizeBody = @{
        rawText = "$marker`n担当：me`n期限：2026-08-31"
        source = 'text'
    } | ConvertTo-Json
    $organized = Invoke-RestMethod `
        -Method Post `
        -Uri "$baseUrl/api/task-requests/organize" `
        -Headers $headers `
        -ContentType 'application/json; charset=utf-8' `
        -Body $organizeBody

    $candidate = $organized.candidate
    $registerBody = @{
        title = $candidate.title
        description = $candidate.description
        assignee = 'me'
        dueDate = '2026-08-31'
        subtasks = @("SQL subtask smoke $marker")
        tags = @()
        customFields = @{}
        priority = 'normal'
    } | ConvertTo-Json -Depth 5
    $registered = Invoke-RestMethod `
        -Method Post `
        -Uri "$baseUrl/api/task-candidates/$($candidate.id)/register" `
        -Headers $headers `
        -ContentType 'application/json; charset=utf-8' `
        -Body $registerBody

    if (-not $registered.succeeded -or $registered.provider -ne 'Mock') {
        throw 'The Mock registration response was not successful.'
    }

    $wbsToken = [guid]::NewGuid().ToString('N')
    $profileBody = @{
        name = "SQL WBS smoke $wbsToken"
        layoutSignature = ('a' * 64)
        sheetName = 'WBS'
        headerRow = 1
        dataStartRow = 2
        mapping = @{
            hierarchyMode = 'parentKey'
            roles = @{ '0' = 'key'; '1' = 'parentKey'; '2' = 'title' }
            titleSeparator = ' '
            descriptionSeparator = "`n"
            dateFormat = 'auto'
        }
    } | ConvertTo-Json -Depth 8
    $wbsProfile = Invoke-RestMethod `
        -Method Post `
        -Uri "$baseUrl/api/wbs-imports/profiles" `
        -Headers $headers `
        -ContentType 'application/json; charset=utf-8' `
        -Body $profileBody
    $batchBody = @{
        fileName = 'sql-smoke.csv'
        fileHash = ($wbsToken + $wbsToken)
        sheetName = 'WBS'
        layoutSignature = ('a' * 64)
        profileId = $wbsProfile.id
        rows = @(
            @{
                sourceRowNumber = 2
                sourceKey = "P-$wbsToken"
                title = "SQL WBS parent $marker"
                assignee = 'me'
                sortOrder = 0
            },
            @{
                sourceRowNumber = 3
                sourceKey = "C-$wbsToken"
                parentSourceKey = "P-$wbsToken"
                title = "SQL WBS child $marker"
                sortOrder = 1
            }
        )
    } | ConvertTo-Json -Depth 8
    $wbsBatch = Invoke-RestMethod `
        -Method Post `
        -Uri "$baseUrl/api/wbs-imports/batches" `
        -Headers $headers `
        -ContentType 'application/json; charset=utf-8' `
        -Body $batchBody
    $wbsRegistered = Invoke-RestMethod `
        -Method Post `
        -Uri "$baseUrl/api/wbs-imports/batches/$($wbsBatch.id)/register" `
        -Headers $headers
    if ($wbsRegistered.status -ne 'Registered' -or $wbsRegistered.succeededRows -ne 2) {
        throw 'The WBS Mock registration response was not successful.'
    }

    $requestId = [string]$organized.taskRequestId
    $candidateId = [string]$candidate.id
    $wbsProfileId = [string]$wbsProfile.id
    $wbsBatchId = [string]$wbsBatch.id
    Stop-TaskCaptureApi $firstProcess
    $firstProcess = $null

    $secondProcess = Start-TaskCaptureApi 'second'
    $recentResponse = Invoke-WebRequest `
        -Uri "$baseUrl/api/task-requests/recent?take=20" `
        -Headers $headers
    $recent = @($recentResponse.Content | ConvertFrom-Json)
    $persisted = $recent |
        Where-Object { [string]$_.taskRequestId -eq $requestId } |
        Select-Object -First 1
    if (-not $persisted) {
        $availableIds = @($recent | ForEach-Object { [string]$_.taskRequestId }) -join ','
        throw "The persisted request was not returned after restarting the API. requested=$requestId available=$availableIds"
    }
    if ($persisted.status -ne 'Registered' -or -not $persisted.registration.succeeded) {
        throw 'The persisted registration did not have the expected state.'
    }
    $persistedWbs = Invoke-RestMethod `
        -Uri "$baseUrl/api/wbs-imports/batches/$wbsBatchId" `
        -Headers $headers
    if ($persistedWbs.status -ne 'Registered' -or $persistedWbs.succeededRows -ne 2) {
        throw 'The persisted WBS batch did not have the expected state.'
    }
    Stop-TaskCaptureApi $secondProcess
    $secondProcess = $null

    $query = @"
SET NOCOUNT ON;
SELECT CONCAT(
    (SELECT COUNT(*) FROM TaskRequests WHERE Id='$requestId'), '|',
    (SELECT COUNT(*) FROM TaskCandidates WHERE TaskRequestId='$requestId'), '|',
    (SELECT COUNT(*) FROM AsanaRegistrations r
        JOIN TaskCandidates c ON c.Id=r.TaskCandidateId
        WHERE c.TaskRequestId='$requestId' AND r.Succeeded=1 AND r.Provider='Mock'
          AND r.AssigneeResolutionStatus='Mock' AND r.ResolvedAssigneeName='me'), '|',
    (SELECT COUNT(*) FROM TaskCandidateSubtasks s
        JOIN TaskCandidates c ON c.Id=s.TaskCandidateId
        WHERE c.TaskRequestId='$requestId'), '|',
    (SELECT COUNT(*) FROM AsanaSubtaskRegistrations sr
        JOIN TaskCandidateSubtasks s ON s.Id=sr.TaskCandidateSubtaskId
        JOIN TaskCandidates c ON c.Id=s.TaskCandidateId
        WHERE c.TaskRequestId='$requestId' AND sr.Succeeded=1 AND sr.Provider='Mock'), '|',
    (SELECT COUNT(*) FROM AuditLogs WHERE EntityId IN ('$requestId','$candidateId')), '|',
    (SELECT COUNT(*) FROM ApplicationSettings), '|',
    (SELECT COUNT(*) FROM Users WHERE ClientKey='sql-server-smoke'), '|',
    (SELECT COUNT(*) FROM WbsImportProfiles WHERE Id='$wbsProfileId'), '|',
    (SELECT COUNT(*) FROM WbsImportBatches WHERE Id='$wbsBatchId' AND Status='Registered'), '|',
    (SELECT COUNT(*) FROM WbsImportRows
        WHERE WbsImportBatchId='$wbsBatchId' AND Status='Registered' AND ExternalTaskGid IS NOT NULL));
"@
    $countOutput = sqlcmd `
        -S $ServerInstance `
        -E `
        -C `
        -b `
        -d $Database `
        -h -1 `
        -W `
        -Q $query
    $counts = ($countOutput | Where-Object { $_.Trim() } | Select-Object -First 1).Trim()
    $countParts = @($counts -split '\|' | ForEach-Object { [int]$_ })
    $countsAreValid = $countParts.Count -eq 11 -and
        $countParts[0] -eq 1 -and
        $countParts[1] -eq 1 -and
        $countParts[2] -eq 1 -and
        $countParts[3] -eq 1 -and
        $countParts[4] -eq 1 -and
        $countParts[5] -ge 2 -and
        $countParts[6] -ge 2 -and
        $countParts[7] -eq 1 -and
        $countParts[8] -eq 1 -and
        $countParts[9] -eq 1 -and
        $countParts[10] -eq 2
    if (-not $countsAreValid) {
        throw "Unexpected SQL row counts: $counts"
    }

    Write-Output "SQL_SMOKE_REQUEST_ID=$requestId"
    Write-Output "SQL_SMOKE_CANDIDATE_ID=$candidateId"
    Write-Output "SQL_SMOKE_REGISTRATION_ID=$($registered.registrationId)"
    Write-Output "SQL_SMOKE_EXTERNAL_GID=$($registered.externalTaskGid)"
    Write-Output "SQL_SMOKE_WBS_PROFILE_ID=$wbsProfileId"
    Write-Output "SQL_SMOKE_WBS_BATCH_ID=$wbsBatchId"
    Write-Output 'SQL_SMOKE_WEB_SPA=PASS'
    Write-Output 'SQL_SMOKE_RESTART_PERSISTENCE=PASS'
    Write-Output "SQL_COUNTS_REQUEST_CANDIDATE_REGISTRATION_SUBTASK_SUBTASKREGISTRATION_AUDIT_SETTINGS_USER_WBSPROFILE_WBSBATCH_WBSROWS=$counts"
    Write-Output 'SQL_PERSISTENCE_INTEGRATION=PASS'
}
finally {
    Stop-TaskCaptureApi $firstProcess
    Stop-TaskCaptureApi $secondProcess

    $resolvedTaskTemp = [System.IO.Path]::GetFullPath($taskTemp)
    $resolvedSystemTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    $safeToDelete = $resolvedTaskTemp.StartsWith(
        $resolvedSystemTemp,
        [System.StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedTaskTemp) -like 'taskcapture-sql-smoke-*'
    if ($safeToDelete -and (Test-Path -LiteralPath $resolvedTaskTemp)) {
        Get-ChildItem -LiteralPath $resolvedTaskTemp -File | Remove-Item -Force
        Remove-Item -LiteralPath $resolvedTaskTemp -Force
    }
}
