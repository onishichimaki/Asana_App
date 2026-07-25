[CmdletBinding()]
param(
    [string]$Runtime = 'win-x64',
    [string]$Configuration = 'Release',
    [string]$OutputDirectory = 'dist'
)

$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path '.').Path
$outputRoot = [IO.Path]::GetFullPath((Join-Path $workspace $OutputDirectory))
if (-not $outputRoot.StartsWith($workspace + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputDirectory must be inside the repository: $outputRoot"
}

npm ci --prefix src/taskcapture-web
npm run build --prefix src/taskcapture-web
dotnet restore TaskCapture.sln
dotnet test TaskCapture.sln --configuration $Configuration

$apiOutput = Join-Path $outputRoot 'server'
$launcherOutput = Join-Path $outputRoot 'launcher'
dotnet publish src/TaskCapture.Api/TaskCapture.Api.csproj `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained false `
    --output $apiOutput
dotnet publish src/TaskCapture.Launcher/TaskCapture.Launcher.csproj `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained false `
    --output $launcherOutput

$exampleConfig = @'
Task Capture 配布物

1. server フォルダーの環境変数を設定し、TaskCapture.Api.exe を起動します。
2. launcher フォルダーを各Windows PCへ配布します。
3. TASK_CAPTURE_WEB_URL にサーバーURLを設定して TaskCapture.Launcher.exe を起動します。
4. タスクトレイの「Windowsログイン時に起動」で自動起動を設定できます。

秘密情報は配布ZIPへ入れないでください。詳細はリポジトリの README.md を参照してください。
'@
Set-Content -LiteralPath (Join-Path $outputRoot 'はじめに.txt') -Value $exampleConfig -Encoding utf8

$zipPath = Join-Path $outputRoot "TaskCapture-$Runtime.zip"
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -Path $apiOutput,$launcherOutput,(Join-Path $outputRoot 'はじめに.txt') -DestinationPath $zipPath
Write-Output "PUBLISH_ZIP=$zipPath"
