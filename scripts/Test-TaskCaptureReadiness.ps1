[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BaseUrl,
    [ValidateRange(1, 120)]
    [int]$TimeoutSeconds = 15
)

$ErrorActionPreference = 'Stop'
$parsedUrl = $null
if (-not [Uri]::TryCreate($BaseUrl, [UriKind]::Absolute, [ref]$parsedUrl)) {
    throw 'BaseUrl must be an absolute URL.'
}
$isLocal = $parsedUrl.Host -in @('localhost', '127.0.0.1', '::1')
if ($parsedUrl.Scheme -ne 'https' -and -not $isLocal) {
    throw 'Production readiness checks require HTTPS. HTTP is allowed only for localhost.'
}

$readyUrl = [Uri]::new($parsedUrl, '/api/health/ready')
try {
    $response = Invoke-RestMethod -Uri $readyUrl -TimeoutSec $TimeoutSeconds
} catch {
    $body = $_.ErrorDetails.Message
    if ($body) {
        try {
            $response = $body | ConvertFrom-Json
        } catch {
            throw "Readiness endpoint failed: $($_.Exception.Message)"
        }
    } else {
        throw "Readiness endpoint failed: $($_.Exception.Message)"
    }
}

$response | ConvertTo-Json -Depth 6
if ($response.status -ne 'ready') {
    $issues = @($response.configurationIssues) -join ' '
    throw "Task Capture is not ready. $issues"
}
Write-Output 'TASK_CAPTURE_READY=true'
