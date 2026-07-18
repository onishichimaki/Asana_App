# Task Capture for Asana

PC・iPhone・iPad から入力したメモを、タイトル・内容・担当者・期限へ整理し、確認後に Asana へ登録する MVP です。Asana をタスク管理の正本とし、このアプリは「入力 → 整理 → 確認・修正 → 登録」だけに集中します。

外部設定なしでも `InMemory + RuleBased + Mock Asana` で全フローを動かせます。本番相当では同じ API を SQL Server と Asana REST API へ切り替えます。

## できること

- レスポンシブな1画面でテキスト入力、貼り付け、Clipboard API 読み込み
- 対応ブラウザーで `ja-JP` 音声入力（Web Speech API）
- タイトル、内容、担当者、期限のルールベース整理
- 登録前の確認・修正、必要時だけ開く詳細設定
- サーバー側だけで Asana PAT を使用する実 API / Mock 切り替え
- Users、TaskRequests、TaskCandidates、AsanaRegistrations、ApplicationSettings、AuditLogs の SQL Server migration
- Windows tray、`Ctrl+Shift+A`、clipboard 自動入力、登録後自動クローズの WebView2 launcher

## 必要なもの

- .NET SDK 8
- Node.js 20 以降と npm
- Windows launcher: Windows 10/11 と Microsoft Edge WebView2 Runtime
- 実接続時のみ: SQL Server、Asana PAT、workspace または project GID

## 最短起動（外部サービス不要）

PowerShell でリポジトリルートから実行します。

```powershell
dotnet tool restore
dotnet restore TaskCapture.sln
npm ci --prefix src/taskcapture-web
npm run build --prefix src/taskcapture-web
dotnet run --project src/TaskCapture.Api/TaskCapture.Api.csproj --launch-profile http
```

[http://localhost:5080](http://localhost:5080) を開きます。User Secrets を未設定の環境では、launch profile は `Development` のため DB は InMemory、整理は RuleBased、登録先は Mock です。production bundle は API の `wwwroot` へ生成され、同じ origin から配信されます。SQL Server 用 User Secrets を設定済みの場合は、同じ起動コマンドで SQL Server を使用します。

開発中に React の hot reload を使う場合は、API を起動したまま別の PowerShell で実行します。

```powershell
npm run dev --prefix src/taskcapture-web
```

[http://localhost:5173](http://localhost:5173) を開きます。Vite が `/api` を `http://localhost:5080` へ proxy します。

## Windows launcher

先に Web/API を起動するか、HTTPS で配置済みの URL を指定します。

```powershell
$env:TASK_CAPTURE_WEB_URL = "http://localhost:5080"
dotnet run --project src/TaskCapture.Launcher/TaskCapture.Launcher.csproj
```

- 通常起動: 入力画面を表示してtrayへ常駐
- `--background`: Windows自動起動向けに入力画面を出さずtrayへ常駐
- `--clipboard`: 起動直後にclipboardを読み込んだ入力画面を表示
- 入力画面を開いている間はタスクバーとAlt+Tabに表示し、閉じるとtrayへ戻る
- tray icon のダブルクリック: 空の入力画面を開く
- `Ctrl+Shift+A`: clipboard を読み込んで入力画面を開く
- tray menu: 空で開く / clipboard から開く / 終了
- ウィンドウの閉じるボタン: launcher は終了せず tray へ戻る
- 登録成功: WebView2 から完了通知を受けて自動的に画面を閉じる

ショートカットが他アプリと競合した場合は tray 通知を表示し、tray menu からは利用できます。WebView2 開発者ツールが必要な場合だけ `TASK_CAPTURE_LAUNCHER_DEVTOOLS=1` を設定します。

## SQL Server を使う

接続文字列はソースへ書かず、ローカル開発では .NET User Secrets、配備先では環境変数または Secret Store を使います。本機の既定インスタンス `DESKTOP-RQ3T767` を使う設定例:

```powershell
$project = "src/TaskCapture.Api/TaskCapture.Api.csproj"
dotnet user-secrets set "Database:Provider" "SqlServer" --project $project
dotnet user-secrets set "Database:ApplyMigrations" "true" --project $project
dotnet user-secrets set "ConnectionStrings:TaskCapture" "Server=DESKTOP-RQ3T767;Database=TaskCapture;Integrated Security=True;TrustServerCertificate=True;MultipleActiveResultSets=True" --project $project
dotnet user-secrets set "Integration:Asana:Mode" "Mock" --project $project
dotnet run --project $project --launch-profile http
```

User Secrets はユーザープロファイル側に保存され、Git の対象になりません。別SQL Serverへ付け替える場合は `ConnectionStrings:TaskCapture` の値だけを新しい接続文字列へ変更します。

実SQLで migration、整理、Mock登録、API再起動後の履歴、6テーブルの行を一括確認できます。スクリプトは監査可能なスモーク履歴を1件残します。

```powershell
& ./scripts/Test-SqlServerIntegration.ps1 `
  -ServerInstance DESKTOP-RQ3T767 `
  -Database TaskCapture
```

配備環境では次のように環境変数へ差し替えます。

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:Database__Provider = "SqlServer"
$env:Database__ApplyMigrations = "true"
$env:ConnectionStrings__TaskCapture = "Server=YOUR_SERVER;Database=TaskCapture;Integrated Security=True;TrustServerCertificate=True"
dotnet run --project src/TaskCapture.Api/TaskCapture.Api.csproj --no-launch-profile --urls http://localhost:5080
```

起動時に未適用の migration を適用します。設計時DbContextも `ConnectionStrings__TaskCapture` を優先するため、手動適用先を明示できます。

```powershell
$env:ConnectionStrings__TaskCapture = "Server=YOUR_SERVER;Database=TaskCapture;Integrated Security=True;TrustServerCertificate=True"
dotnet tool run dotnet-ef database update `
  --project src/TaskCapture.Api/TaskCapture.Api.csproj `
  --startup-project src/TaskCapture.Api/TaskCapture.Api.csproj
```

migration SQL を接続せず確認する場合:

```powershell
dotnet tool run dotnet-ef migrations script --idempotent `
  --project src/TaskCapture.Api/TaskCapture.Api.csproj `
  --startup-project src/TaskCapture.Api/TaskCapture.Api.csproj
```

## Asana API を使う

サーバー環境だけに次を設定します。ブラウザーや launcher へ PAT を渡す設定はありません。

```powershell
$env:Integration__Asana__Mode = "Api"
$env:Integration__Asana__PersonalAccessToken = "YOUR_PAT"
$env:Integration__Asana__DefaultWorkspaceGid = "YOUR_WORKSPACE_GID"
```

詳細設定で project GID を指定した場合は project を使用し、section GID は project GID と組み合わせた membership として送信します。担当者は `me` または数値 GID の場合だけ Asana の assignee に設定します。自由文の担当者名は原文・候補として履歴に残り、誤った assignee API 値にはしません。

| 設定 | 既定 | 用途 |
|---|---|---|
| `Database__Provider` | `SqlServer`（Development は `InMemory`） | DB provider |
| `ConnectionStrings__TaskCapture` | LocalDB 例 | SQL Server 接続文字列 |
| `Database__ApplyMigrations` | `true` | 起動時 migration |
| `TaskOrganization__Mode` | `RuleBased` | 現在の organizer mode |
| `Integration__Asana__Mode` | `Mock` | `Mock` / `Api` |
| `Integration__Asana__PersonalAccessToken` | 未設定 | サーバー側 PAT |
| `Integration__Asana__DefaultWorkspaceGid` | 未設定 | project 未指定時の workspace |
| `TASK_CAPTURE_WEB_URL` | `http://localhost:5080` | launcher が開く URL |

## テストと品質確認

```powershell
dotnet restore TaskCapture.sln
dotnet build TaskCapture.sln --no-restore
dotnet test TaskCapture.sln --no-build
npm ci --prefix src/taskcapture-web
npm run lint --prefix src/taskcapture-web
npm run build --prefix src/taskcapture-web
```

API テストは、整理ルール、入力検証、候補修正、Mock 登録、DB 履歴、成功後の二重登録防止、設計時接続文字列の差し替えを確認します。実SQLの任意確認には `scripts/Test-SqlServerIntegration.ps1` を使います。

## iPhone / iPad で使う

API と built SPA を端末から到達できる HTTPS URL に配置してください。マイクと Clipboard API はブラウザーの secure context・権限・対応状況に依存します。非対応または権限拒否時も、通常のテキスト入力と貼り付けは利用できます。

## セキュリティ上の前提

- PAT、DB 接続文字列などの秘密情報をクライアント・URL・ApplicationSettings・監査ログへ保存しません。
- API は DataAnnotations と追加検証で文字数、GID、日付、詳細 JSON を検証します。
- 現在の `X-TaskCapture-Client` は端末識別であり認証ではありません。MVP は限定ネットワーク利用を前提とします。
- インターネット公開前に組織認証、HTTPS/TLS、rate limit、運用ログ保護を追加してください。

## 現時点で未設定・未確認のもの

- 実 Asana PAT を使った限定 project への登録 smoke test
- iPhone / iPad の音声・clipboard と Windows tray/hotkey の実端末 QA
- ブラウザー自動操作環境での目視レイアウトQA

GitHubへの公開、ローカルSQL Server migration、SQL永続化スモークは完了しています。Asana認証情報がなくても、実装済みの Mock/RuleBased/SQL Server 経路でMVP全フローを利用できます。

## ドキュメント

- [要件](REQUIREMENTS.md)
- [アーキテクチャ](ARCHITECTURE.md)
- [実装計画](IMPLEMENTATION_PLAN.md)
- [設計判断](DECISIONS.md)
- [現在の状態](STATUS.md)
- [対話型アーキテクチャ資料](docs/architecture.html)
- [機械可読インベントリ](docs/architecture.json)
