# 会社環境への安全な取込み・導入手順

## この手順書の目的

個人GitHubで作成したTask Captureの完成版ソースを、会社のルールに従って確認し、会社管理の非公開GitHubへ登録して、オンプレミスのアプリサーバーへ導入するための手順です。

推奨する流れは次のとおりです。

```text
個人GitHubの固定Release
  ↓ 会社が許可したブラウザーでZIPを1回だけ取得
ハッシュ確認・ウイルス検査・ソース検査
  ↓
会社GitHubの非公開リポジトリへ初回登録
  ↓
会社のCodexとCIでビルド・テスト・セキュリティ確認
  ↓
会社のオンプレミス環境へ配備
  ↓
Asana・Azure OpenAI・SQL Server・HTTPSを設定
  ↓
受入テスト後に利用開始
```

会社リポジトリから個人リポジトリを継続的に`pull`する構成にはしません。最初の取込みだけを行い、その後の正本は会社GitHubに切り替えます。

## 1. 先に会社へ確認すること

次の項目を情報システム部門またはセキュリティ担当者へ確認します。

- 公開GitHubからソースZIPを取得してよいか
- 取得ファイルを保存してよいフォルダーや申請済みの受渡し方法
- 会社GitHubで非公開リポジトリを作成できる組織名と管理者
- 利用必須のウイルス検査、秘密情報検査、OSSライセンス検査、脆弱性検査
- ソースコードの著作権・会社への持込み・社内利用・改変に関する承認
- オンプレミスのアプリサーバー、SQL Server、DNS名、HTTPS証明書の担当者
- 外部通信を許可する宛先と申請方法
- Asana管理者とAzure OpenAI管理者
- ブラウザーの音声入力および画像OCRに関する社内方針

個人のGitHubアカウント、個人のAsana認証情報、個人環境のAI APIキーを会社環境へ持ち込みません。

このリポジトリを公開していること自体は、会社での利用承認や第三者依存パッケージのライセンス確認を省略できるという意味ではありません。会社の法務・知財・OSS利用ルールに従って確認します。

## 2. GitHub Releaseから取得する

1. 会社が許可したブラウザーで次のRelease画面を開きます。
   `https://github.com/onishichimaki/Asana_App/releases`
2. 対象バージョンの`Assets`を開きます。
3. `Asana_App-＜バージョン＞-source.zip`をダウンロードします。
4. 同じAssetsにある`SHA256SUMS.txt`もダウンロードします。
5. ZIPを開く前に、会社指定のウイルス検査を実行します。

GitHubの`Code`から`Download ZIP`を選んでも取得できますが、導入する版を後から説明できるように、バージョンが固定されたRelease Assetを推奨します。

### SHA-256を確認する

PowerShellを開き、ダウンロードフォルダーで実行します。

```powershell
Get-FileHash .\Asana_App-＜バージョン＞-source.zip -Algorithm SHA256
Get-Content .\SHA256SUMS.txt
```

表示されたハッシュが`SHA256SUMS.txt`の値と完全に一致することを確認します。一致しない場合は解凍せず、ファイルを削除して取得元と社内ネットワークを確認します。

### ZIPに含まれるもの・含まれないもの

含まれるもの:

- ASP.NET Core Web API、React Web UI、Windowsランチャーのソース
- SQL Server用EF Core migration
- 自動テスト、サンプルWBS、ビルド・バックアップ・稼働確認スクリプト
- README、設計、要件、判断記録、受入テスト

含まれないもの:

- Asana Client secret、OAuth token、PAT
- GeminiまたはAzure OpenAIのAPIキー
- SQL Serverのパスワードと本番接続文字列
- 利用者データ、入力履歴、実データベース、Data Protection鍵
- `bin`、`obj`、`node_modules`などの個人PCで生成した実行物
- 元の個人GitHubの`.git`履歴

秘密情報と個人PCの生成物を含めず、会社環境で再ビルドすることがセキュリティ上の重要なポイントです。

## 3. 会社GitHubへ登録する

### 推奨設定

会社GitHubで空のリポジトリを作成します。

| 項目 | 推奨値 |
|---|---|
| リポジトリ名 | `Asana_App`または社内命名規則に従う名前 |
| 公開範囲 | `Private` |
| 所有者 | 会社のOrganization |
| READMEの自動作成 | しない |
| `.gitignore`の自動作成 | しない |
| Licenseの自動作成 | しない |

初回登録後は、`main`への直接pushを禁止し、Pull Request、レビュー、CI成功を必須にします。可能であればSecret scanning、依存関係検査、Dependabotまたは会社指定の同等機能を有効にします。

### GitHub Desktopを使う場合

1. ZIPを会社が許可した作業フォルダーへ解凍します。
2. GitHub Desktopへ会社アカウントでサインインします。
3. `File` → `Add local repository`を選び、解凍したフォルダーを指定します。
4. Gitリポジトリではないという案内が出たら、そのフォルダーにリポジトリを作成します。
5. 変更一覧にAPIキー、`.env`、本番設定値、データベースファイルがないことを確認します。
6. Summaryを`Import Task Capture MVP ＜バージョン＞`として最初のcommitを作成します。
7. `Publish repository`を選びます。
8. 会社のOrganizationを選択し、`Keep this code private`が有効であることを確認して公開します。
9. ブラウザーで会社GitHubを開き、表示が`Private`になっていることを再確認します。
10. Repository URLと`git remote -v`が会社GitHubだけを指し、個人GitHubがremoteに登録されていないことを確認します。

GitHub Desktopの画面名はバージョンにより少し異なる場合があります。`Public`へ公開する操作は行いません。

### Gitコマンドを使う場合

会社GitHubに空の非公開リポジトリを先に作成し、解凍したフォルダーで実行します。

```powershell
git init
git add .
git status
git commit -m "Import Task Capture MVP ＜バージョン＞"
git branch -M main
git remote add origin https://＜会社GitHub＞/＜会社組織＞/Asana_App.git
git push -u origin main
```

`git add .`の後、`git status`で秘密情報や不要な生成物がないことを必ず確認してからcommitします。会社のルールがPull Request必須の場合は、最初から取込み用ブランチを作り、管理者にmainへの取込みを依頼します。

## 4. 会社のCodexへ最初に指示する内容

会社GitHubへ登録した後、会社のCodexでリポジトリを開きます。最初は変更を依頼せず、検査だけを依頼します。

### 最初の確認用プロンプト

```text
このリポジトリは個人GitHubの固定Releaseから会社環境へ取り込んだTask Capture MVPです。
最初にAGENTS.md、README.md、STATUS.md、IMPLEMENTATION_PLAN.md、DECISIONS.md、
docs/COMPANY_ADOPTION_GUIDE.mdとリポジトリ全体を確認してください。

まだコード変更、commit、push、外部サービスへの接続はしないでください。
次を実行して結果を報告してください。
1. 秘密情報、個人情報、実データ、不要な生成物が含まれていないか
2. 依存パッケージの脆弱性とOSSライセンス
3. dotnetとReactのrestore、build、test、lint
4. 外部通信先と会社で許可が必要な項目
5. オンプレミス配備に必要な情報と不足事項
```

### 会社向け設定を実装するときのプロンプト

```text
検査結果を確認しました。
codex/company-deploymentブランチを作成し、会社環境に必要な設定例と運用文書を更新してください。
秘密値はリポジトリへ書かず、環境変数名または会社のSecret Store上の名前だけを記載してください。
Asana OAuth、Azure OpenAI、SQL Server、HTTPS、Data Protection鍵、バックアップ、監視を対象にします。
変更後はAGENTS.md記載の全ビルド・テストと本番ready確認を実施し、Pull Requestを作成してください。
mainへ直接pushしないでください。
```

CodexにAPIキーやパスワードをチャットへ貼り付けません。会社のSecret Storeまたはサーバー管理者が許可した安全な入力方法を使います。

## 5. 会社環境で最初にビルドする

必要な開発ツール:

- GitまたはGitHub Desktop
- .NET SDK 8
- Node.js 20以降とnpm
- SQL Serverへの接続に必要な会社指定ツール
- Windowsランチャー確認用のMicrosoft Edge WebView2 Runtime

バージョン確認:

```powershell
git --version
dotnet --info
node --version
npm --version
```

外部サービスを接続せず、最初にMock構成で確認します。

```powershell
dotnet tool restore
dotnet restore TaskCapture.sln
dotnet build TaskCapture.sln --no-restore
dotnet test TaskCapture.sln --no-build
npm ci --prefix src/taskcapture-web
npm run lint --prefix src/taskcapture-web
npm run build --prefix src/taskcapture-web
dotnet run --project src/TaskCapture.Api/TaskCapture.Api.csproj --launch-profile http
```

ブラウザーで`http://localhost:5080`を開きます。開発時はInMemory DB、RuleBased整理、Mock Asanaで動作確認できます。

会社指定の検査ツールに加え、利用可能であれば依存関係も確認します。

```powershell
dotnet list TaskCapture.sln package --vulnerable --include-transitive
npm audit --prefix src/taskcapture-web
```

脆弱性が検出された場合は、内容と実際の利用箇所を確認し、会社の基準に従って更新、緩和策、例外承認のいずれかを記録します。ライセンス一覧は会社指定のSCA/OSS管理ツールで生成・保管します。

## 6. 本番導入で会社が用意するもの

| 分類 | 会社側で用意・決定するもの | 主な担当 |
|---|---|---|
| アプリサーバー | Windows Server、配置先、実行アカウント、サービス監視 | インフラ担当 |
| URL | 社内DNS名、HTTPS証明書、443番通信 | インフラ・ネットワーク担当 |
| SQL Server | サーバー名、DB名、接続方式、実行アカウント、バックアップ先 | DB担当 |
| Asana | OAuthアプリ、会社workspace、Client ID/secret、callback URL、配布許可 | Asana管理者 |
| Azure OpenAI | endpoint、deployment名、APIキー、利用量・ログ方針 | Azure管理者 |
| 利用者 | 初期管理者メール、許可会社ドメイン、利用者登録・停止手順 | アプリ管理者 |
| 暗号鍵 | Data Protection鍵フォルダー、権限、バックアップ | インフラ担当 |
| 端末 | WebView2 Runtime、ランチャー配布先、iPhone/iPadの社内URL到達 | 端末管理者 |
| 運用 | 監視通知、SQL・暗号鍵バックアップ、障害・退職時手順 | 運用担当 |

アプリの実装はAsana OAuth、Azure OpenAI、SQL Serverへの切替に対応済みです。ただし、会社の実際のURL、証明書、アカウント、APIキー、ネットワーク許可はソースだけでは決められないため、各管理者の対応が必要です。

## 7. 外部サービスごとの設定

### Asana

1. Asana Developer Consoleで会社用OAuthアプリを作成します。
2. callback URLを`https://＜会社のアプリURL＞/api/auth/asana/callback`にします。
3. 配布設定で会社のAsana workspaceだけを許可します。
4. Client ID、Client secret、workspace GIDをサーバーのSecret Storeへ登録します。
5. 初期管理者メールと会社メールドメインを設定します。
6. 初期管理者がログインし、管理画面で利用者メールを事前登録します。

利用者はAsanaでログインし、自分がAsana上で見られるプロジェクトだけを選択できます。Asanaパスワードを本アプリへ入力・保存することはありません。

### Azure OpenAI

1. 会社のAzure環境にAzure OpenAI resourceとモデルdeploymentを用意します。
2. endpoint、deployment名、APIキーをサーバーのSecret Storeへ登録します。
3. `TaskOrganization__Mode`を`AzureOpenAI`にします。
4. 入力データの取扱い、ログ、リージョン、利用上限を会社ルールに合わせます。

現在の実装はAzure OpenAIのAPIキー方式に対応しています。Managed Identityを必須とする会社では追加実装とセキュリティレビューが必要です。本番では個人環境のGeminiキーを再利用せず、不要なキーは失効させます。

### SQL Server

1. 専用DBとアプリ実行アカウントを用意します。
2. SQL認証またはWindows統合認証を会社方針に合わせます。
3. 接続文字列をサーバーのSecret Storeへ登録します。
4. 初回起動でEF Core migrationを適用します。
5. 定期バックアップと`RESTORE VERIFYONLY`を設定します。

## 8. サーバー設定名

実値はファイルへ記載せず、サーバーの環境変数または会社のSecret Storeで設定します。

```text
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__TaskCapture=＜Secret Store＞
Database__Provider=SqlServer
Database__ApplyMigrations=true

Access__Mode=AsanaOAuth
Access__AllowedEmailDomains__0=＜会社ドメイン＞
Access__AdminEmails__0=＜初期管理者メール＞

Integration__Asana__Mode=Api
Integration__Asana__CredentialMode=PerUserOAuth
Integration__Asana__DefaultWorkspaceGid=＜会社workspace GID＞
Integration__Asana__OAuth__ClientId=＜Secret Store＞
Integration__Asana__OAuth__ClientSecret=＜Secret Store＞
Integration__Asana__OAuth__RedirectUri=https://＜会社URL＞/api/auth/asana/callback

TaskOrganization__Mode=AzureOpenAI
TaskOrganization__FallbackToRuleBased=true
TaskOrganization__AzureOpenAI__Endpoint=https://＜会社resource＞.openai.azure.com
TaskOrganization__AzureOpenAI__DeploymentName=＜deployment名＞
AZURE_OPENAI_API_KEY=＜Secret Store＞

DataProtection__KeysPath=C:\ProgramData\TaskCapture\keys
AllowedHosts=＜会社ホスト名＞
```

値の一覧は`src/TaskCapture.Api/appsettings.Production.example.json`でも確認できます。この例ファイルへ実値を書き込んでcommitしないでください。

## 9. 配布物を会社環境で作る

会社のCIまたは承認済みビルドPCで実行します。

```powershell
& .\scripts\Publish-TaskCapture.ps1
```

`dist/TaskCapture-win-x64.zip`が生成されます。個人GitHubから取得したバイナリをそのまま本番へ置かず、会社環境でソースから再ビルドしたものを使用します。

- `server`: オンプレミスのアプリサーバーへ配置
- `launcher`: 利用者のWindows PCへ配布
- iPhone・iPad: インストールせず、社内HTTPS URLをSafariで開く

サーバープロセスの常駐方式は、IIS、会社標準のサービス管理、監視製品などからインフラ担当者が決定します。現在の配布スクリプトは実行物を生成しますが、会社固有のIIS設定、Windows Service登録、証明書配布は自動化しません。

## 10. 通信許可で確認する宛先

- 会社GitHubまたはGitHub Enterprise
- NuGetとnpmの会社承認済みパッケージ取得先
- `https://app.asana.com`のOAuthとAsana API
- 会社のAzure OpenAI endpoint
- 画像OCRで使用するTesseract.js関連データの取得先、または社内配布先
- ブラウザーのWeb Speech APIが利用する音声認識サービス

Web Speech APIの通信先・音声データ取扱いは利用ブラウザーと会社ポリシーに依存します。許可されない場合は音声ボタンを無効化するか、会社承認済みの音声認識サービスへ置き換えます。画像ファイルそのものは現在の実装ではブラウザー内でOCRし、アプリAPIやDBへ保存しません。

## 11. 本番前の確認

```powershell
& .\scripts\Test-TaskCaptureReadiness.ps1 -BaseUrl "https://＜会社のアプリURL＞"
```

`TASK_CAPTURE_READY=true`を確認し、`docs/ACCEPTANCE_TEST.md`をPC、iPhone、iPadで実施します。

最低限、次を確認します。

- 未登録者、停止者、会社外アカウントがログインできない
- 登録済み利用者がAsanaでログインできる
- 自分が見られるAsanaプロジェクトだけが表示される
- テキスト、貼り付け、画像、マイク音声、Excel/CSVが動作する
- 親タスク、サブタスク、担当者、開始日、期限がAsanaへ登録される
- 履歴とエラーがSQL Serverへ保存される
- APIキー、OAuth token、入力本文が不要なログへ出ない
- SQL ServerとData Protection鍵をバックアップ・復旧できる
- 障害時の監視通知と連絡先が決まっている

## 12. 会社側でなければ完了できない作業

次の作業はソースやCodexだけでは完了できません。

- 公開GitHubからの持込み許可
- 会社GitHubの非公開リポジトリ作成と権限設定
- オンプレミスサーバー、DNS、HTTPS証明書、ファイアウォールの準備
- SQL ServerのDB・アカウント・バックアップ先の準備
- Asana OAuthアプリの会社workspace配布許可
- Azure OpenAI resource、deployment、APIキー、利用予算の準備
- 本番Secret Storeへの値登録
- 本番端末での受入テストと利用開始承認

これらの情報がそろえば、アプリ側は設定差し替えと会社環境での再ビルド・受入テストを行って本番利用へ進められます。

## 13. セキュリティ上の禁止事項

- APIキー、Client secret、接続文字列をGitHub、チャット、メール、Excelへ貼り付けない
- 個人GitHubを会社本番の継続的な配布元にしない
- 個人PCで作った`bin`や`node_modules`を会社本番へコピーしない
- 非公開設定を確認せず会社GitHubへPublishしない
- セキュリティ検査やCIが失敗した状態でmainへ統合しない
- 本番SQL ServerとData Protection鍵をバックアップなしで運用しない
- AsanaやAzureの本番secretを開発PCの共有フォルダーへ保存しない

## 14. 完了の判断

以下がすべて完了したら、会社への取込みと本番準備完了です。

- [ ] Release Assetのハッシュ確認と会社指定検査が完了した
- [ ] 会社GitHubの非公開リポジトリが正本になった
- [ ] 会社CIでビルド・テスト・脆弱性検査が成功した
- [ ] 会社環境で作った配布物を使用している
- [ ] SQL Server、HTTPS、Asana OAuth、Azure OpenAI、暗号鍵を設定した
- [ ] ready診断と全端末の受入テストが成功した
- [ ] バックアップ、監視、利用者追加・停止の運用担当者が決まった
