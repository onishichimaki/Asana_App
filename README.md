# Task Capture for Asana

PC・iPhone・iPad から入力したメモを、親タスクのタイトル・内容・担当者・期限と実行可能なサブタスクへ整理し、確認後に Asana へ登録する MVP です。Asana をタスク管理の正本とし、このアプリは「入力 → 整理 → 確認・修正 → 登録」だけに集中します。

外部設定なしでも `InMemory + RuleBased + Mock Asana` で全フローを動かせます。本番相当では同じ API を SQL Server と Asana REST API へ切り替えます。

## できること

- メイリオUIを優先し、入力に必要な操作だけを初期表示するコンパクトなレスポンシブ1画面
- テキスト入力、貼り付け、Clipboard API、UTF-8/Shift_JIS の `.txt/.md/.csv` 議事録読込
- JPEG/PNG/WebP のファイル選択・カメラ撮影・画像貼り付けから日本語OCR
- 対応ブラウザーで `ja-JP` ライブ音声入力（Web Speech API）
- タイトル、内容、担当者、期限と0〜6件のサブタスク候補を作るGemini整理、ルールベース自動フォールバック
- 登録前の確認・修正、必要時だけ開く詳細設定
- サーバー側だけで Asana PAT を使用する親タスク・サブタスクの実 API / Mock 切り替え
- 「大西」のような担当者名をAsana workspaceユーザーへ安全に解決し、実際の割り当て結果または警告を表示
- レイアウトが異なる `.xlsx` / `.csv` WBSの自由列・階層マッピング、複数テンプレート、編集可能プレビュー、冪等な一括登録
- 通常入力8テーブルにWbsImportProfiles、WbsImportBatches、WbsImportRowsを加えた11テーブルの SQL Server migration
- Windows tray、`Ctrl+Shift+A`、clipboard 自動入力、登録後自動クローズ、標準の最小化・閉じるボタンを備えた WebView2 launcher

## 必要なもの

- .NET SDK 8
- Node.js 20 以降と npm
- Windows launcher: Windows 10/11 と Microsoft Edge WebView2 Runtime
- 実接続時のみ: SQL Server、Asana PAT、workspace または project GID、Gemini APIキー

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

## 入力方法

- 貼り付け: OS標準の貼り付け、または「貼り付け」ボタンで文字を取り込みます。
- 議事録: UTF-8またはShift_JISの `.txt`、`.md`、`.csv` を2MBまで読み込みます。
- 画像: JPEG、PNG、WebPを10MBまで選択できます。iPhone/iPadではカメラ撮影、PCでは画像の貼り付けにも対応します。
- 音声: Web Speech API対応ブラウザーでは日本語を連続入力できます。非対応時は端末キーボードのマイク入力を利用できます。

画像OCRは `Tesseract.js` をブラウザー内で実行し、画像ファイル自体をAPIやSQL Serverへ送信・保存しません。最初のOCR時だけ日本語言語モデルを取得するためインターネット接続が必要で、その後はブラウザーキャッシュを利用します。OCR結果は必ず入力欄と候補画面で確認・修正してください。

Geminiモードでは、たとえば `カレーを作る、大西` から親タスク「カレーを作る」、担当者「大西」と、レシピ決定・冷蔵庫確認・不足食材の買い物・調理などのサブタスク候補を作ります。サブタスクは1行1件の欄で登録前に追加・修正・削除できます。単純な依頼には不要なサブタスクを作らないよう指示しています。RuleBasedモードでは推測せず、入力に明示した箇条書きだけをサブタスクとして保持します。

## Excel / CSV のWBS取込について

上部の「WBS一括取込」を選ぶと、プロジェクトや作成者ごとに列・開始行・階層が異なるWBSを変換できます。一括誤登録を防ぐため通常入力とは独立した確認フローです。

1. `.xlsx` / `.csv` を選び、sheet、header行、data開始行を指定する。
2. 各列をタイトル、説明、担当者、期限、識別キー、親キー、階層レベル、階層列へ割り当てる。複数列を同じタイトルまたは説明へ割り当てることもできる。
3. 階層は「親子関係なし」「識別キー・親キー」「階層レベル」「大項目・中項目等の階層列」から選ぶ。
4. マッピングを名前付きテンプレートとして複数保存する。同じ列レイアウトは次回読込時に自動適用され、必要に応じ更新・削除できる。
5. 親子関係、担当者、日付、除外行、エラーをpreviewし、タイトル・担当者・期限・登録対象を修正してから確定する。
6. 行単位の成功・失敗を保存し、失敗行だけ再試行・エラーCSV出力する。

ファイル上限は10MB、データ行は5,000件です。XLSX/CSV本体はブラウザー内で解析し、確認済みの正規化行だけをAPIへ送ります。project/section GIDは「変換と登録先」で固定値として指定できます。開始日・依存関係・custom field・既存タスク更新はMVP対象外です。詳細な受入条件は [REQUIREMENTS.md](REQUIREMENTS.md)、設計判断は [DECISIONS.md](DECISIONS.md) にあります。

## Windows launcher

先に Web/API を起動するか、HTTPS で配置済みの URL を指定します。

```powershell
$env:TASK_CAPTURE_WEB_URL = "http://localhost:5080"
dotnet run --project src/TaskCapture.Launcher/TaskCapture.Launcher.csproj
```

- 通常起動: 入力画面を表示してtrayへ常駐
- `--background`: Windows自動起動向けに入力画面を出さずtrayへ常駐
- `--clipboard`: 起動直後にclipboardを読み込んだ入力画面を表示
- 入力画面を開いている間はタスクバーとAlt+Tabに表示し、最小化「－」を利用できる
- tray icon のダブルクリック: 空の入力画面を開く
- `Ctrl+Shift+A`: clipboard を読み込んで入力画面を開く
- tray menu: 空で開く / clipboard から開く / 終了
- ウィンドウ右上の閉じる「×」: launcher は終了せず tray へ戻る
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

実SQLで migration、通常入力とWBSのMock親子登録、API再起動後の履歴、11テーブルの行を一括確認できます。スクリプトは監査可能なスモーク履歴を1件残します。

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

## Gemini APIで整理する

`TaskOrganization:Mode=Gemini` のとき、ASP.NET Core APIだけがGemini APIを呼びます。ブラウザーとlauncherへAPIキーは渡しません。APIキー未設定、タイムアウト、Geminiエラーの場合は、既定でRuleBased organizerへ自動フォールバックします。

APIキーはチャット、GitHub、`appsettings.json`へ貼らず、Google AI Studioで発行した未露出のキーをUser Secretsへ入力してください。PowerShellの入力と成功メッセージへキー本体を表示しない設定例:

```powershell
$project = "src/TaskCapture.Api/TaskCapture.Api.csproj"
$secureKey = Read-Host "Gemini API key" -AsSecureString
$geminiKey = [System.Net.NetworkCredential]::new("", $secureKey).Password
dotnet user-secrets set "TaskOrganization:Gemini:ApiKey" $geminiKey --project $project | Out-Null
dotnet user-secrets set "TaskOrganization:Mode" "Gemini" --project $project | Out-Null
dotnet user-secrets set "TaskOrganization:Gemini:Model" "gemini-3.5-flash" --project $project | Out-Null
Remove-Variable secureKey,geminiKey
```

配備先では `TaskOrganization__Gemini__ApiKey` または `GEMINI_API_KEY` をSecret Storeから設定します。`TaskOrganization__FallbackToRuleBased=false` にした場合だけ、Gemini失敗を整理APIのエラーとして扱います。将来のAzure OpenAI移行では、UI・DB・workflowを変えずに `ITaskOrganizer` 実装を追加します。

## Asana API を使う

サーバー環境だけに次を設定します。ブラウザーや launcher へ PAT を渡す設定はありません。

```powershell
$env:Integration__Asana__Mode = "Api"
$env:Integration__Asana__PersonalAccessToken = "YOUR_PAT"
$env:Integration__Asana__DefaultWorkspaceGid = "YOUR_WORKSPACE_GID"
$env:Integration__Asana__DefaultProjectGid = "YOUR_PROJECT_GID"
```

候補の詳細設定で project GID を指定した場合はその値を優先し、未指定時は `DefaultProjectGid` を使用します。どちらも未設定の場合だけ workspace の個人タスクとして登録します。section GID は project GID と組み合わせた membership として送信します。

担当者は `me` と数値GIDを直接使用します。「大西」のような自由文は `DefaultWorkspaceGid` のユーザー一覧へ完全一致、次に空白を除いた一意な部分一致で検索します。1人に特定できた場合だけGIDを送信し、Asanaが実際に返した担当者名を完了画面とSQL履歴へ保存します。0人、複数人、workspace未設定、ユーザー取得失敗では勝手に割り当てず、タスクは未割り当てで登録して画面に警告します。

サブタスクがある場合は、親タスクを作成・保存した後、Asanaの `POST /tasks/{task_gid}/subtasks` で子を登録します。一部だけ失敗した場合は結果を `PartiallyRegistered` として保存し、再試行では親と成功済みの子を重複作成せず、未完了の子だけを登録します。

| 設定 | 既定 | 用途 |
|---|---|---|
| `Database__Provider` | `SqlServer`（Development は `InMemory`） | DB provider |
| `ConnectionStrings__TaskCapture` | LocalDB 例 | SQL Server 接続文字列 |
| `Database__ApplyMigrations` | `true` | 起動時 migration |
| `TaskOrganization__Mode` | `RuleBased` | `RuleBased` / `Gemini` |
| `TaskOrganization__FallbackToRuleBased` | `true` | AI失敗時のルール整理継続 |
| `TaskOrganization__Gemini__ApiKey` / `GEMINI_API_KEY` | 未設定 | サーバー側Gemini APIキー |
| `TaskOrganization__Gemini__Model` | `gemini-3.5-flash` | Gemini model |
| `TaskOrganization__Gemini__TimeoutSeconds` | `20` | 5〜120秒のAI timeout |
| `Integration__Asana__Mode` | `Mock` | `Mock` / `Api` |
| `Integration__Asana__PersonalAccessToken` | 未設定 | サーバー側 PAT |
| `Integration__Asana__DefaultWorkspaceGid` | 未設定 | project 未指定時の workspace |
| `Integration__Asana__DefaultProjectGid` | 未設定 | 候補で project 未指定時の登録先 project |
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

API テストは、整理ルール、Gemini構造化結果とサブタスク分解、AI失敗時フォールバック、入力元検証、候補修正、担当者名の一意/曖昧解決、通常・WBSのMock親子登録、DB履歴、部分失敗後の再開、成功後の二重登録防止、WBS profile削除後のbatch保持、設計時接続文字列の差し替えを確認します。WBSの画面確認には `tests/fixtures` の親キー・階層レベル・階層列CSVを使えます。Gemini実通信は有効なローカルSecretを設定した環境でのみ実施します。実SQLの任意確認には `scripts/Test-SqlServerIntegration.ps1` を使います。

## iPhone / iPad で使う

API と built SPA を端末から到達できる HTTPS URL に配置してください。マイクと Clipboard API はブラウザーの secure context・権限・対応状況に依存します。画像ボタンはカメラ撮影または写真選択を開きます。WBSは端末のファイル選択からXLSX/CSVを読み込めます。非対応または権限拒否時も、通常のテキスト入力、議事録ファイル、OS標準貼り付けを利用できます。

## セキュリティ上の前提

- Asana PAT、Gemini APIキー、DB 接続文字列などの秘密情報をクライアント・URL・ApplicationSettings・監査ログへ保存しません。
- API は DataAnnotations と追加検証で文字数、GID、日付、詳細 JSON を検証します。
- WBSファイル本体はAPIやSQL Serverへ保存せず、クライアントで正規化した行をサーバーでも親不足・循環・重複・入力長について再検証します。
- 現在の `X-TaskCapture-Client` は端末識別であり認証ではありません。MVP は限定ネットワーク利用を前提とします。
- インターネット公開前に組織認証、HTTPS/TLS、rate limit、運用ログ保護を追加してください。

## 現時点で未設定・未確認のもの

- iPhone / iPad のカメラOCR・音声・clipboard と Windows tray/hotkey の実端末 QA
- HTTPS配備先のSecret Store、組織認証、TLS、rate limit、運用監視

GitHubへの公開、ローカルSQL Server migration、通常入力とWBSのSQL永続化、Gemini構造化整理の実通信、Asana限定projectへの通常・WBS親子実登録と担当者名解決スモークは完了しています。Gemini APIキーとAsana PATはローカルUser Secretsだけに保存され、リポジトリには含まれません。認証情報がなくても、Mock/RuleBased経路でMVP全フローを利用できます。

## ドキュメント

- [要件](REQUIREMENTS.md)
- [アーキテクチャ](ARCHITECTURE.md)
- [実装計画](IMPLEMENTATION_PLAN.md)
- [設計判断](DECISIONS.md)
- [現在の状態](STATUS.md)
- [対話型アーキテクチャ資料](docs/architecture.html)
- [機械可読インベントリ](docs/architecture.json)
