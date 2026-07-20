# 要件定義

## 目的

Windows PC、iPhone、iPad から入力した内容をタスク候補へ整理し、利用者の確認後に Asana へ登録する。Asana をタスク管理の正本とし、本アプリの責務は登録と監査可能な履歴保存に限定する。

## MVP ユースケース

1. 利用者がテキスト、貼り付け、クリップボード、議事録ファイル、画像OCR、または音声で内容を入力する。
2. 「AIで整理」を押し、タイトル、内容、担当者、期限を含む候補を得る。
3. 同じ画面で候補を修正する。通常は高度な Asana 項目を表示しない。
4. 「Asanaへ登録」を押す。
5. 成功時は Asana タスク URL またはモック登録 ID を表示する。

## 機能要件

| ID | 要件 | MVP 受入条件 |
|---|---|---|
| F-01 | テキスト入力 | 1〜10,000文字を入力できる |
| F-02 | 貼り付け | OS 標準貼り付けと Clipboard API 読み込みができる |
| F-03 | 音声入力 | 対応ブラウザーで Web Speech API により日本語を追記できる |
| F-04 | タスク整理 | Geminiまたはルールベースでタイトル、内容、担当者、期限の候補を返す |
| F-05 | 候補確認・修正 | 登録前に全基本項目を編集できる |
| F-06 | 詳細設定 | プロジェクト、セクション、タグ、カスタムフィールド、優先度を折りたためる |
| F-07 | Asana 登録 | サーバーだけが PAT を使用し、成功・失敗を保存する |
| F-08 | モック継続 | AI/Asana/SQL Server 未設定でも開発モードで一連の操作が成功する |
| F-09 | 履歴 | 入力、候補、登録結果、エラー/監査、設定メタデータを永続化できる |
| F-10 | Windows ランチャー | tray 常駐、Ctrl+Shift+A、クリップボード入力、登録後自動クローズに対応する |
| F-11 | 議事録読込 | UTF-8/Shift_JISの `.txt/.md/.csv` を2MBまで読み込み、最大10,000文字を入力へ追加できる |
| F-12 | 画像OCR | JPEG/PNG/WebPを10MBまで選択・撮影・貼り付けでき、日本語OCR結果だけを入力へ追加する |

## 非機能要件

- レスポンシブ: 360px 幅以上で主要操作を横スクロールなしに行える。
- 可読性: WindowsではメイリオUIを優先し、iPhone/iPadでは端末標準の日本語UIフォントへフォールバックする。
- 速度: 外部 API を除く整理・モック登録は一般的な開発 PC で体感待ちを生じさせない。
- セキュリティ: 秘密情報をクライアント、URL、監査ログへ含めない。入力長と形式をサーバーで検証する。画像OCRはブラウザー内で行い、画像ファイルをAPI/DBへ送信・保存しない。
- 可用性: Gemini未設定・失敗時はルールベースへフォールバックし、外部サービス未設定時もモック構成で起動できる。
- 保守性: AI と Asana はインターフェース越しに差し替え、DB は EF Core migration で管理する。

## 対象外

- Asana タスクの一覧、編集、完了、検索
- 本格的な認証・権限管理、マルチテナント
- 複数候補への自動分割、Asanaへの添付ファイル登録、オフライン同期
- Asana AI チームメイトが行う登録後の粒度調整

## 外部設定

- SQL Server 接続文字列: `ConnectionStrings__TaskCapture`
- Asana: `Integration__Asana__Mode=Api`、`Integration__Asana__PersonalAccessToken`、必要に応じ `DefaultWorkspaceGid`
- Gemini: `TaskOrganization__Mode=Gemini`、`TaskOrganization__Gemini__ApiKey`または`GEMINI_API_KEY`、必要に応じmodel/timeout。秘密情報はサーバー設定だけに保持する。
- Web URL（ランチャー）: `TASK_CAPTURE_WEB_URL`
- AIは `ITaskOrganizer` でRuleBased/Geminiを差し替える。将来のAzure OpenAIも同じ境界へ追加し、UI・DBを変更しない。
- 画像OCRはブラウザーの `Tesseract.js`。初回の日本語言語モデル取得にはインターネット接続が必要。
