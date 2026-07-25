[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ServerInstance,
    [ValidatePattern('^[A-Za-z0-9_-]+$')]
    [string]$Database = 'TaskCapture',
    [string]$BackupDirectory = 'C:\TaskCaptureBackups',
    [ValidateRange(1, 3650)]
    [int]$KeepDays = 30
)

$ErrorActionPreference = 'Stop'
$resolvedBackupDirectory = [IO.Path]::GetFullPath($BackupDirectory)
New-Item -ItemType Directory -Path $resolvedBackupDirectory -Force | Out-Null
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupPath = Join-Path $resolvedBackupDirectory "$Database-$timestamp.bak"
$escapedBackupPath = $backupPath.Replace("'", "''")
$query = "BACKUP DATABASE [$Database] TO DISK=N'$escapedBackupPath' WITH COPY_ONLY, CHECKSUM, COMPRESSION, INIT; RESTORE VERIFYONLY FROM DISK=N'$escapedBackupPath' WITH CHECKSUM;"

sqlcmd -S $ServerInstance -E -C -b -Q $query
if (-not (Test-Path -LiteralPath $backupPath)) {
    throw "SQL Server reported success but the backup file was not found: $backupPath"
}

$cutoff = (Get-Date).AddDays(-$KeepDays)
Get-ChildItem -LiteralPath $resolvedBackupDirectory -File -Filter "$Database-*.bak" |
    Where-Object { $_.LastWriteTime -lt $cutoff } |
    Remove-Item -Force
Write-Output "BACKUP_VERIFIED=$backupPath"
