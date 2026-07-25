# Task Capture for Asana

PC・iPhone・iPadから、文章・貼り付け・音声・画像・議事録・Excel/CSVを取り込み、AIで整理して、確認後にAsanaへ登録するアプリです。Asanaをタスク管理の正本とし、このアプリは「入力 → 整理 → 確認・修正 → 登録」に集中します。

## 主な機能

- 会社メールへ届く6桁コードでログイン（パスワード保存なし）
- 利用者ごとの履歴分離、利用停止、管理者設定、プロジェクト制限、監査ログ
- テキスト、貼り付け、クリップボード、Web音声入力、日本語画像OCR、TXT/MD/CSV議事録
- GeminiまたはAzure OpenAIによるタイトル・説明・担当者・開始日・期限・サブタスク整理
- 外部AI未設定・失敗時のルールベース処理
- 登録前の確認・修正、Asanaプロジェクト検索・お気に入り・セクション選択
- Asana PATまたは利用者別OAuth、会社メールとの一致確認、担当者名のAsanaユーザー解決
- レイアウトが異なるExcel/CSV WBSの列・開始行・階層マッピング
- WBSマッピングの保存、プレビュー、重複防止、差分更新、失敗行再試行、作成分の取消
- SQL Serverへの入力・候補・登録・認証・Asana接続・設定・監査履歴保存
- Windowsタスクトレイ、`Ctrl+Shift+A`、クリップボード取込、登録後自動非表示、自動起動

UIは「Meiryo UI」を優先し、Windows PC、iPhone、iPad向けにレスポンシブ表示します。

## 最短起動（外部サービス不要）

必要なものは.NET SDK 8、Node.js 20以降、npmです。

```powershell
dotnet tool restore
dotnet restore TaskCapture.sln
npm ci --prefix src/taskcapture-web
npm run build --prefix src/taskcapture-web
dotnet run --project src/TaskCapture.Api/TaskCapture.Api.csproj --launch-profile http
```

[http://localhost:5080](http://localhost:5080) を開きます。Development環境の初期値はInMemory DB、開発用自動ログイン、RuleBased整理、Mock Asanaです。外部APIや秘密情報がなくても全画面を確認できます。

Reactのホットリロードを使う場合は、APIを起動したまま別のPowerShellで実行します。

```powershell
npm run dev --prefix src/taskcapture-web
```

## SQL Server

このPCの既定インスタンス `DESKTOP-RQ3T767` を使う例です。

```powershell
dotnet user-secrets set "Database:Provider" "SqlServer" --project src/TaskCapture.Api
dotnet user-secrets set "Database:ApplyMigrations" "true" --project src/TaskCapture.Api
dotnet user-secrets set "ConnectionStrings:TaskCapture" "Server=DESKTOP-RQ3T767;Database=TaskCapture;Integrated Security=True;TrustServerCertificate=True;MultipleActiveResultSets=True" --project src/TaskCapture.Api
```

起動時にEF Core migrationを適用します。別サーバーへ付け替える場合は接続文字列だけを変更します。

実SQL統合テスト:

```powershell
& ./scripts/Test-SqlServerIntegration.ps1 -ServerInstance "DESKTOP-RQ3T767" -Database TaskCapture
```

検証付きバックアップ:

```powershell
& ./scripts/Backup-TaskCapture.ps1 -ServerInstance "DESKTOP-RQ3T767" -Database TaskCapture -BackupDirectory "C:\TaskCaptureBackups"
```

毎日2時のバックアップをWindowsタスクへ登録する例（ログイン中の利用者で実行）:

```powershell
& ./scripts/Install-TaskCaptureBackupTask.ps1 -ServerInstance "DESKTOP-RQ3T767" -Database TaskCapture -DailyAt "02:00"
```

## 会社メールの個別アカウント

Entra IDは不要です。管理者が事前登録した会社メールだけに確認コードを送り、メールアドレスごとに個別アカウントを作ります。パスワードは保持しません。

本番環境の主な設定:

```text
Access__Mode=EmailCode
Access__AllowedEmailDomains__0=example.co.jp
Access__AdminEmails__0=admin@example.co.jp
Access__EmailCode__Delivery__Mode=Smtp
Access__EmailCode__Delivery__Host=smtp.example.co.jp
Access__EmailCode__Delivery__Port=587
Access__EmailCode__Delivery__EnableSsl=true
Access__EmailCode__Delivery__Username=...
Access__EmailCode__Delivery__Password=...
Access__EmailCode__Delivery__FromAddress=taskcapture@example.co.jp
```

ProductionでMockメール送信やDevelopment認証を指定すると起動を拒否します。SMTPパスワードは環境変数または安全なシークレット管理へ置き、Gitへ保存しません。

初回だけ `Access__AdminEmails` に設定した管理者メールを起点にします。このメールでログインし、「接続・利用設定」→「利用者を事前登録」から社員の会社メールを追加します。事前登録されていないメールには確認コードを送らず、ログインできません。`AdminEmails` は初期管理者のサーバー側許可リストなので、運用開始後も必要最小限にします。

退職・異動時は管理画面で「このアプリを利用できる」を外して保存します。その時点で発行済みログインをすべて失効し、Asana OAuthを解除してローカルの暗号化tokenも消去します。再び有効にした場合はAsanaへの再接続が必要です。

## Gemini

APIキーはUser Secretsへ保存します。

```powershell
$project = "src\TaskCapture.Api\TaskCapture.Api.csproj"
$secureKey = Read-Host "Gemini APIキー" -AsSecureString
$key = [System.Net.NetworkCredential]::new("", $secureKey).Password
dotnet user-secrets set "TaskOrganization:Gemini:ApiKey" $key --project $project
dotnet user-secrets set "TaskOrganization:Mode" "Gemini" --project $project
Remove-Variable secureKey,key
```

環境変数 `GEMINI_API_KEY` または `TaskOrganization__Gemini__ApiKey` でも設定できます。失敗時は既定でRuleBasedへ切り替わります。

## Azure OpenAI

本番ではAzure OpenAI v1 APIを利用できます。deployment名はAzure側で作成したモデルdeploymentを指定します。

```text
TaskOrganization__Mode=AzureOpenAI
TaskOrganization__FallbackToRuleBased=true
TaskOrganization__AzureOpenAI__Endpoint=https://YOUR-RESOURCE.openai.azure.com
TaskOrganization__AzureOpenAI__DeploymentName=YOUR-DEPLOYMENT
AZURE_OPENAI_API_KEY=...
```

APIキーはSecret Storeまたは環境変数だけに置きます。Azure OpenAIはstrict JSON Schemaで候補を返し、Geminiと同じ検証・RuleBasedフォールバックを通ります。

## Asana接続

### 管理者のPATを共有する現在のローカル構成

```powershell
dotnet user-secrets set "Integration:Asana:Mode" "Api" --project src/TaskCapture.Api
dotnet user-secrets set "Integration:Asana:CredentialMode" "PersonalAccessToken" --project src/TaskCapture.Api
dotnet user-secrets set "Integration:Asana:PersonalAccessToken" "新しいPAT" --project src/TaskCapture.Api
dotnet user-secrets set "Integration:Asana:DefaultWorkspaceGid" "ワークスペース番号" --project src/TaskCapture.Api
```

PATの権限で見えるプロジェクトだけが一覧に出ます。

### 利用者が自分のAsanaを接続する本番推奨構成

Asana Developer ConsoleでOAuthアプリを1件作成し、callback URLを `https://アプリURL/api/asana/connection/callback` にします。

```text
Integration__Asana__Mode=Api
Integration__Asana__CredentialMode=PerUserOAuth
Integration__Asana__OAuth__ClientId=...
Integration__Asana__OAuth__ClientSecret=...
Integration__Asana__OAuth__RedirectUri=https://アプリURL/api/asana/connection/callback
Integration__Asana__OAuth__RequireMatchingEmail=true
DataProtection__KeysPath=C:\TaskCapture\DataProtectionKeys
```

利用者は初回ログイン後、「接続・利用設定」からログイン中の会社メールと同じメールのAsanaを接続します。接続が完了するまでは通常登録、WBS、履歴を利用できません。異なるメール、emailを取得できないアカウント、`DefaultWorkspaceGid` の社内ワークスペースに所属しないアカウントは拒否し、tokenを保存しません。アクセストークンと更新トークンはASP.NET Core Data Protectionで暗号化してSQL Serverへ保存し、ブラウザーへ保持しません。各利用者には、そのAsanaアカウントで参照できるプロジェクトだけが表示されます。管理者はさらにアプリ側で登録先を限定できます。

全社展開時の利用開始順は「管理者が会社メールを事前登録 → 利用者がメール確認コードでログイン → 同じメールのAsanaをOAuth接続 → 利用可能なプロジェクトから登録」です。社内Asana workspaceは `DefaultWorkspaceGid` で1つに固定します。

## Excel / CSV WBS取込

「Excel・CSVをまとめて登録」を開きます。

1. `.xlsx` または `.csv` を選ぶ。
2. アプリが見出し行・開始行・各列の意味を推測する。
3. 「タスク名」「説明」「担当者」「開始日」「期限」と親子関係を画面で確認する。
4. 必要なら読み方を名前付きで保存する。
5. 登録先プロジェクトを選び、プレビューで対象行とエラーを確認する。
6. 件数確認ダイアログの後にAsanaへ登録する。

親子関係は、親番号方式、階層レベル方式、大項目・中項目の複数列方式に対応します。ファイルはブラウザー内で解析し、本体をAPIへ保存しません。上限は10MB・5,000行です。

テスト用ファイル:

- [給食スパイスカレー](samples/給食スパイスカレー_WBS_テスト.xlsx)
- [金沢旅行・一般形式](samples/柴犬ちまきと行く金沢旅行_WBS_一般形式.xlsx)
- [金沢旅行・階層レベル形式](samples/柴犬ちまきと行く金沢旅行_WBS_階層レベル型.xlsx)

## Windowsランチャー

```powershell
$env:TASK_CAPTURE_WEB_URL = "http://localhost:5080"
dotnet run --project src/TaskCapture.Launcher/TaskCapture.Launcher.csproj
```

- タスクトレイに常駐
- 起動時はタスクバーを避けて画面右下へ表示
- `Ctrl+Shift+A` でクリップボードを取り込んで表示
- 通常入力の初期操作はスクロールせず1画面内に表示
- ウィンドウの最小化・最大化・閉じるボタン
- 登録成功後に自動で非表示
- タスクトレイの「Windowsログイン時に起動」で自動起動

WebView2のログインCookieは利用者のWindowsプロファイル内に保持されます。

## ビルド・テスト

```powershell
dotnet restore TaskCapture.sln
dotnet build TaskCapture.sln --no-restore
dotnet test TaskCapture.sln --no-build
npm ci --prefix src/taskcapture-web
npm run lint --prefix src/taskcapture-web
npm run build --prefix src/taskcapture-web
```

GitHub Actionsでも同じビルド・テストを実行します。

Windows配布ZIP:

```powershell
& ./scripts/Publish-TaskCapture.ps1
```

`dist/TaskCapture-win-x64.zip` を生成します。秘密情報はZIPへ含めません。

本番設定の項目一覧は `src/TaskCapture.Api/appsettings.Production.example.json` にあります。実値、APIキー、パスワード、接続文字列はこのファイルへ書かず、環境変数またはSecret Storeへ設定します。

## 運用確認

- 稼働確認: `GET /api/health`
- 起動準備確認: `GET /api/health/ready`（DB、SMTP、Asana、Azure OpenAI、暗号鍵。秘密値は返さない）
- 配備先確認: `& ./scripts/Test-TaskCaptureReadiness.ps1 -BaseUrl "https://アプリURL"`
- 管理画面: 利用者の有効/停止、管理者、プロジェクト限定
- 管理API: `GET /api/admin/audit`
- バックアップ: `scripts/Backup-TaskCapture.ps1`、定期登録: `scripts/Install-TaskCaptureBackupTask.ps1`
- OAuth/Data Protection鍵とSQL Serverバックアップは別の安全な場所へ保管
- 実端末受入項目: `docs/ACCEPTANCE_TEST.md`

## 本番前に必要な外部設定

| 項目 | 開発時 | 本番前に必要 |
|---|---|---|
| SQL Server | InMemory可 | 接続文字列、バックアップ先 |
| メール送信 | Mock可 | SMTPホスト・送信元・資格情報 |
| Asana | Mock/PAT可 | OAuthアプリのClient ID/Secret/Callback URL |
| AI | RuleBased/Gemini可 | Azure OpenAI endpoint・deployment・APIキー |
| HTTPS | localhost HTTP可 | TLS証明書と公開URL |
| Data Protection | Windowsプロファイル | 永続化・バックアップ対象の鍵フォルダー |

秘密情報はクライアント、ログ、リポジトリ、配布ZIPへ入れません。

Productionでは、SQL Server、SSL SMTP、利用者別Asana OAuthと同一メール確認、Azure OpenAI、HTTPS callback、永続Data Protection鍵が揃っていない場合は安全のため起動を拒否します。

## 文書

- [要件](REQUIREMENTS.md)
- [アーキテクチャ](ARCHITECTURE.md)
- [実装計画](IMPLEMENTATION_PLAN.md)
- [進捗](STATUS.md)
- [設計判断](DECISIONS.md)
- [開発エージェント向け規約](AGENTS.md)
- [本番受入テスト](docs/ACCEPTANCE_TEST.md)
