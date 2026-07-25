[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_.\\-]+$')]
    [string]$ServerInstance,
    [ValidatePattern('^[A-Za-z0-9_-]+$')]
    [string]$Database = 'TaskCapture',
    [ValidateScript({ -not $_.Contains('"') })]
    [string]$BackupDirectory = 'C:\TaskCaptureBackups',
    [ValidatePattern('^(?:[01]\d|2[0-3]):[0-5]\d$')]
    [string]$DailyAt = '02:00',
    [ValidateRange(1, 3650)]
    [int]$KeepDays = 30,
    [string]$TaskName = 'Task Capture SQL Backup'
)

$ErrorActionPreference = 'Stop'
$backupScript = (Resolve-Path (Join-Path $PSScriptRoot 'Backup-TaskCapture.ps1')).Path
$powerShell = (Get-Process -Id $PID).Path
$arguments = @(
    '-NoProfile'
    '-NonInteractive'
    '-ExecutionPolicy Bypass'
    "-File `"$backupScript`""
    "-ServerInstance `"$ServerInstance`""
    "-Database `"$Database`""
    "-BackupDirectory `"$BackupDirectory`""
    "-KeepDays $KeepDays"
) -join ' '

$action = New-ScheduledTaskAction -Execute $powerShell -Argument $arguments
$trigger = New-ScheduledTaskTrigger -Daily -At $DailyAt
$settings = New-ScheduledTaskSettingsSet `
    -StartWhenAvailable `
    -ExecutionTimeLimit (New-TimeSpan -Hours 2) `
    -RestartCount 2 `
    -RestartInterval (New-TimeSpan -Minutes 10)
$principal = New-ScheduledTaskPrincipal `
    -UserId ([System.Security.Principal.WindowsIdentity]::GetCurrent().Name) `
    -LogonType Interactive `
    -RunLevel Highest

Register-ScheduledTask `
    -TaskName $TaskName `
    -Action $action `
    -Trigger $trigger `
    -Settings $settings `
    -Principal $principal `
    -Description 'Task Capture SQL Server backup with CHECKSUM and RESTORE VERIFYONLY.' `
    -Force | Out-Null

Write-Output "BACKUP_TASK_INSTALLED=$TaskName"
Write-Output 'The task runs while this Windows user is signed in. Use a managed service account for unattended servers.'
