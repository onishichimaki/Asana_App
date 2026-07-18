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

    $requestId = [string]$organized.taskRequestId
    $candidateId = [string]$candidate.id
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
    Stop-TaskCaptureApi $secondProcess
    $secondProcess = $null

    $query = @"
SET NOCOUNT ON;
SELECT CONCAT(
    (SELECT COUNT(*) FROM TaskRequests WHERE Id='$requestId'), '|',
    (SELECT COUNT(*) FROM TaskCandidates WHERE TaskRequestId='$requestId'), '|',
    (SELECT COUNT(*) FROM AsanaRegistrations r
        JOIN TaskCandidates c ON c.Id=r.TaskCandidateId
        WHERE c.TaskRequestId='$requestId' AND r.Succeeded=1 AND r.Provider='Mock'), '|',
    (SELECT COUNT(*) FROM AuditLogs WHERE EntityId IN ('$requestId','$candidateId')), '|',
    (SELECT COUNT(*) FROM ApplicationSettings), '|',
    (SELECT COUNT(*) FROM Users WHERE ClientKey='sql-server-smoke'));
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
    $countsAreValid = $countParts.Count -eq 6 -and
        $countParts[0] -eq 1 -and
        $countParts[1] -eq 1 -and
        $countParts[2] -eq 1 -and
        $countParts[3] -ge 2 -and
        $countParts[4] -ge 2 -and
        $countParts[5] -eq 1
    if (-not $countsAreValid) {
        throw "Unexpected SQL row counts: $counts"
    }

    Write-Output "SQL_SMOKE_REQUEST_ID=$requestId"
    Write-Output "SQL_SMOKE_CANDIDATE_ID=$candidateId"
    Write-Output "SQL_SMOKE_REGISTRATION_ID=$($registered.registrationId)"
    Write-Output "SQL_SMOKE_EXTERNAL_GID=$($registered.externalTaskGid)"
    Write-Output 'SQL_SMOKE_WEB_SPA=PASS'
    Write-Output 'SQL_SMOKE_RESTART_PERSISTENCE=PASS'
    Write-Output "SQL_COUNTS_REQUEST_CANDIDATE_REGISTRATION_AUDIT_SETTINGS_USER=$counts"
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
