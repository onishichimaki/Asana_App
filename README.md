# Task Capture for Asana

PC・iPhone・iPadから、文章・貼り付け・音声・画像・議事録・Excel/CSVを取り込み、AIで整理して、確認後にAsanaへ登録するアプリです。Asanaをタスク管理の正本とし、このアプリは「入力 → 整理 → 確認・修正 → 登録」に集中します。

## 主な機能

- 管理者が事前登録した会社メールのAsanaアカウントで1回ログイン
- 利用者ごとの履歴分離、利用停止、管理者設定、プロジェクト制限、監査ログ
- テキスト、貼り付け、クリップボード、マイクのWeb音声入力、日本語画像OCR、TXT/MD/CSV議事録
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

Entra IDとSMTPは不要です。本番では「Asanaでログイン」の1回で本人確認とAsana API連携を完了します。アプリはAsanaパスワードを受け取りません。

1. 初期管理者のメールを `Access__AdminEmails__0` に設定する。
2. 初期管理者がAsanaでログインする。
3. 「アカウント・利用設定」で社員の会社メールを事前登録する。
4. 社員が同じ会社メールのAsanaでログインする。

未登録、利用停止中、許可ドメイン外、指定した社内Asanaワークスペースに所属しないアカウントはログインできません。退職・異動時は管理画面で「このアプリを利用できる」を外すと、発行済みsessionとAsana OAuthを失効し、ローカルの暗号化tokenも消去します。

PC・iPhone・iPadへ同時にログインできます。ログアウトするとその端末のsessionを終了し、ほかにログイン中の端末がなければAsana連携も解除します。管理者による利用停止は全端末を即時ログアウトさせます。

メール確認コード方式は開発・切替用の `Access__Mode=EmailCode` として残していますが、Productionは `AsanaOAuth` 以外では起動しません。

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

### Asanaログインを使う本番構成

Asana Developer ConsoleでOAuthアプリを1件作成し、callback URLを `https://アプリURL/api/auth/asana/callback` にします。Asana側では対象の社内ワークスペースにアプリを公開します。

```text
Integration__Asana__Mode=Api
Integration__Asana__CredentialMode=PerUserOAuth
Integration__Asana__DefaultWorkspaceGid=社内ワークスペース番号
Integration__Asana__OAuth__ClientId=...
Integration__Asana__OAuth__ClientSecret=...
Integration__Asana__OAuth__RedirectUri=https://アプリURL/api/auth/asana/callback
Integration__Asana__OAuth__RequireMatchingEmail=true
Access__Mode=AsanaOAuth
Access__AllowedEmailDomains__0=example.co.jp
Access__AdminEmails__0=admin@example.co.jp
DataProtection__KeysPath=C:\TaskCapture\DataProtectionKeys
```

ログイン開始とcallbackはPKCE（S256）、改ざん防止state、HttpOnlyの短時間照合Cookieで保護します。アクセストークンと更新トークンはASP.NET Core Data Protectionで暗号化してSQL Serverへ保存し、ブラウザーへ保持しません。各利用者にはそのAsanaアカウントで参照できるプロジェクトだけが表示され、管理者はアプリ側でさらに登録先を限定できます。

OAuth scopeは `projects:read sections:read tasks:read tasks:write tasks:delete users:read workspaces:read` を設定します。

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

音声はマイクからのリアルタイム入力に対応します。MP3やWAVなどの録音済み音声ファイル取込は対象外です。

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
- 起動準備確認: `GET /api/health/ready`（DB、Asanaログイン、Azure OpenAI、暗号鍵。秘密値は返さない）
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
| Asana | Mock/PAT可 | OAuthアプリのClient ID/Secret/Callback URL |
| AI | RuleBased/Gemini可 | Azure OpenAI endpoint・deployment・APIキー |
| HTTPS | localhost HTTP可 | TLS証明書と公開URL |
| Data Protection | Windowsプロファイル | 永続化・バックアップ対象の鍵フォルダー |

秘密情報はクライアント、ログ、リポジトリ、配布ZIPへ入れません。

Productionでは、SQL Server、Asanaログインと社内workspace、Azure OpenAI、HTTPS callback、永続Data Protection鍵が揃っていない場合は安全のため起動を拒否します。

## 文書

- [要件](REQUIREMENTS.md)
- [アーキテクチャ](ARCHITECTURE.md)
- [実装計画](IMPLEMENTATION_PLAN.md)
- [進捗](STATUS.md)
- [設計判断](DECISIONS.md)
- [開発エージェント向け規約](AGENTS.md)
- [本番受入テスト](docs/ACCEPTANCE_TEST.md)
