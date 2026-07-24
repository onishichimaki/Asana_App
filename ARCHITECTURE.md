# アーキテクチャ

## システム概要

Task Capture は、React の通常入力／WBS一括取込 UI、ASP.NET Core API、SQL Server、Windows WebView2 ランチャーからなる登録専用アプリである。ブラウザーとランチャーは同じ Web UI を使用し、秘密情報・外部 API 呼び出し・履歴保存はすべて API 側へ閉じ込める。

```mermaid
flowchart LR
    User["PC / iPhone / iPad 利用者"] --> Web["React 1画面 UI"]
    Image["画像 / カメラ"] --> Ocr["Tesseract.js 日本語OCR"] --> Web
    Minutes["TXT / MD / CSV 議事録"] --> Web
    Wbs["XLSX / CSV WBS"] --> Parse["ブラウザー内解析・自由列マッピング"] --> Web
    Tray["Windows tray + WebView2"] --> Web
    Web -->|HTTPS JSON| Api["ASP.NET Core API"]
    Api --> Workflow["Task workflow"]
    Api --> WbsWorkflow["WBS import workflow"]
    Workflow --> Organizer["ITaskOrganizer"]
    Workflow --> Asana["IAsanaTaskService"]
    Workflow --> Db["EF Core / SQL Server"]
    Organizer --> Rule["RuleBased organizer"]
    Organizer --> Gemini["Gemini organizer"]
    Gemini --> GeminiApi["Gemini API"]
    Gemini -. "未設定 / 失敗" .-> Rule
    Asana --> Mock["Mock Asana"]
    Asana --> Real["Asana REST API"]
```

## コンポーネント

| コンポーネント | 責務 |
|---|---|
| React UI | メイリオUI優先表示、Clipboard、議事録読込、Tesseract.js画像OCR、Web Speech API、親タスクとサブタスク候補、開始日・期限、登録先の編集、実際に解決された担当者と警告の表示 |
| WBS UI | 「ファイル → 読取確認 → 登録」の3ステップ、XLSX/CSVのブラウザー内解析、見出し行・列の自動推測、通常は閉じた自由マッピング、4階層方式、開始日・対象列、複数テンプレート、project/section選択、PC表／スマホカードの編集preview、最終確認、一括登録・エラーCSV |
| Task workflow | 状態遷移、親子候補のDB保存、監査、親→子の順序制御、部分失敗時の再開 |
| WBS import workflow | server dry-run、行単位検証、親優先登録、row hash重複防止、部分失敗からの再開、profile/batch/row監査 |
| RuleBased organizer | API キー不要の決定的なタイトル・担当者・期限抽出と、明示された箇条書きのサブタスク化 |
| Gemini organizer | GeminiのJSON Schema出力を親候補と0〜6件の実行可能なサブタスクへ変換し、未設定・失敗時はRuleBasedへフォールバック |
| Asana services | Mock と REST API の設定切り替え、workspaceユーザー名の安全なGID解決、project/section一覧取得、開始日を含む親タスク/サブタスク登録、候補/既定project/workspaceの登録先解決 |
| EF Core | SQL Server スキーマ、履歴、登録・監査データ |
| Launcher | tray、グローバルホットキー、WebView2、クリップボード橋渡し、自動クローズ |

## API

| Method | Path | 用途 |
|---|---|---|
| GET | `/api/health` | 起動状態、DB/AI/Asana モード確認（秘密情報なし） |
| POST | `/api/task-requests/organize` | 入力保存と候補生成 |
| PUT | `/api/task-candidates/{id}` | 確認・修正した候補の保存 |
| POST | `/api/task-candidates/{id}/register` | 親候補とサブタスクの保存、Asana/Mockへの親子登録 |
| GET | `/api/task-requests/recent` | 限定的な直近履歴確認 |
| GET | `/api/asana/projects` | workspaceの利用可能project一覧とサーバー既定値を取得 |
| GET | `/api/asana/projects/{projectGid}/sections` | 選択projectのsection一覧を取得 |
| GET/POST | `/api/wbs-imports/profiles` | WBS変換テンプレートの一覧・作成 |
| PUT/DELETE | `/api/wbs-imports/profiles/{id}` | テンプレートの更新・削除 |
| POST | `/api/wbs-imports/batches` | 正規化行のserver dry-runとbatch保存 |
| GET | `/api/wbs-imports/batches/{id}` | batch・行別結果の再取得 |
| POST | `/api/wbs-imports/batches/{id}/register` | 親優先のAsana/Mock一括登録と再開 |
| GET | `/api/wbs-imports/batches/{id}/errors.csv` | 未登録行と理由のCSV出力 |

## データモデル

```mermaid
erDiagram
    Users ||--o{ TaskRequests : creates
    TaskRequests ||--o{ TaskCandidates : produces
    TaskCandidates ||--o{ TaskCandidateSubtasks : contains
    TaskCandidates ||--o{ AsanaRegistrations : registers
    TaskCandidateSubtasks ||--o{ AsanaSubtaskRegistrations : registers
    Users ||--o{ AuditLogs : generates
    Users ||--o{ WbsImportProfiles : owns
    Users ||--o{ WbsImportBatches : imports
    WbsImportProfiles o|--o{ WbsImportBatches : configures
    WbsImportBatches ||--o{ WbsImportRows : contains
    WbsImportRows o|--o{ WbsImportRows : parent

    Users { guid Id PK string DisplayName string ClientKey }
    TaskRequests { guid Id PK guid UserId FK string RawText string Source string Status datetime CreatedAtUtc }
    TaskCandidates { guid Id PK guid TaskRequestId FK string Title string Description string Assignee date StartDate date DueDate string AdvancedSettingsJson }
    TaskCandidateSubtasks { guid Id PK guid TaskCandidateId FK string Title int SortOrder }
    AsanaRegistrations { guid Id PK guid TaskCandidateId FK bool Succeeded string ExternalTaskGid string ResolvedAssigneeGid string ResolvedAssigneeName string AssigneeResolutionStatus }
    AsanaSubtaskRegistrations { guid Id PK guid TaskCandidateSubtaskId FK bool Succeeded string ExternalTaskGid string ExternalTaskUrl string ErrorCode }
    ApplicationSettings { string Key PK string Value string Description }
    AuditLogs { guid Id PK guid UserId string EventType string EntityType string EntityId string Detail }
    WbsImportProfiles { guid Id PK guid UserId FK string Name string LayoutSignature string MappingJson }
    WbsImportBatches { guid Id PK guid UserId FK guid WbsImportProfileId FK string FileHash string Status int TotalRows }
    WbsImportRows { guid Id PK guid WbsImportBatchId FK guid ParentRowId FK string SourceKey date StartDate date DueDate string RowHash string Status string ExternalTaskGid }
```

テーブルには監査用の UTC 日時を持たせる。入力・候補・エラー詳細は秘密情報を含まない範囲で保持し、PAT や接続文字列は保持しない。

## 状態遷移

`Received → Organized → Edited（任意）→ Registering → Registered / PartiallyRegistered / Failed`

自由文の担当者は `GET /workspaces/{workspace_gid}/users` で取得した名前へ完全一致、次に一意な部分一致を行う。0件・複数件・取得失敗では誤割り当てせず、親タスクを未割り当てで登録して警告を返す。親タスクを先に登録してGIDとAsanaが返した担当者を保存し、その後に同じ担当者GIDでサブタスクを `POST /tasks/{task_gid}/subtasks` から順に登録する。部分失敗時は成功済みの親・子を再作成せず、失敗した子だけを次回再試行する。

## WBS取込の境界

可変レイアウトのExcel/CSV取込は通常の1件入力画面へ混在させず、同じSPA内の独立モードとして実装する。

```mermaid
flowchart LR
    File["XLSX / CSV"] --> Parse["ブラウザー内解析"]
    Parse --> Mapping["見出し行・列を自動推測し、利用者が確認"]
    Mapping --> Destination["project・sectionを名前で選択"]
    Destination --> Preview["親子プレビューと検証"]
    Preview --> Batch["確認済み正規化行だけをBatch APIへ送信"]
    Batch --> Db2["WbsImportProfiles / Batches / Rows"]
    Batch --> Asana2["親優先・行単位のAsana登録"]
    Mapping --> Profile["複数の名前付きImportProfile"]
```

階層は「なし」「識別キー・親キー」「階層レベル」「大項目・中項目等の階層列」から選ぶ。見出し名から開始日、期限、登録対象を含む列を初期推測し、割り当ての意味を画面内に表示する。利用者はproject/sectionを名前で選び、クライアントpreviewに加えてAPIが日付順、親不足・循環・重複等を再検証する。確認済み結果を `WbsImportProfiles`、`WbsImportBatches`、`WbsImportRows` へ保存し、親優先でAsanaへ登録する。row hashで別batchを含む重複を防ぎ、部分失敗時は成功済み行を飛ばして未完了行だけを再試行する。

WBS batch状態は `Ready / Invalid → Registering → Registered / PartiallyRegistered / Failed`、行状態は `Ready / Invalid / Excluded → Registered / Duplicate / Failed / Blocked` である。

## セキュリティ境界

- ブラウザーには PAT、AI キー、DB 接続文字列を返さない。
- Gemini APIキーはUser Secretsまたは配備先Secret Storeだけから読み、入力本文・キー・SDK設定オブジェクトをログへ出さない。
- 画像・議事録・WBSファイルはブラウザー内で文字化または正規化し、ファイル本体をAPI/DBへ送信・保存しない。
- API DTO の文字数・日付・JSON 形式を検証する。
- `HttpClient` の Authorization は Asana 専用クライアントでのみ設定する。
- ログは例外メッセージを必要最小限にし、認証ヘッダー/設定オブジェクトを出力しない。
- MVP は限定ネットワーク内での利用を前提とする。外部公開前に組織認証、TLS 終端、レート制限を追加する。

## 実装根拠と引き継ぎ

人向けのクリック可能な構成図は `docs/architecture.html`、機械可読の module/API/DB/integration/data-flow inventory は `docs/architecture.json`、更新手順は `docs/architecture_readme.md` にある。Gemini構造化整理、実 SQL Server、通常入力とWBSのAsana限定projectへの担当者付き親子登録はローカル限定環境で実通信まで検証済みである。未確認のHTTPS配備と実端末 QAは inventory のリスクまたは次アクションへ分離している。
