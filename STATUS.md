# STATUS

最終更新: 2026-07-25

## 現在地

- フェーズ: 個別アカウント・運用機能を含むMVP実装完了、本番外部設定待ち
- MVP 判定: 通常入力、Gemini/RuleBased、可変WBS、会社メール個別アカウント、利用者別Asana OAuth、権限管理、SQL Server、Windowsランチャー、配布・バックアップ・CIのコードと主要自動テストが完成。

## 完了

- メイリオUI優先で説明・余白を抑えたReactレスポンシブ1画面（入力 → 整理 → 確認・修正 → 登録）
- 会社メール確認コード、PBKDF2ハッシュ、HttpOnly Cookie session、ログアウト、送信・試行回数制限
- 認証必須API、利用者ごとの通常/WBS履歴分離、停止利用者の拒否、管理者policy
- 利用者別Asana OAuth、Data Protection token暗号化、refresh、接続状態・解除UI
- 利用者管理画面（利用停止、管理者、登録先project限定）と監査閲覧API
- Asanaプロジェクトの名前検索・お気に入り、通常登録・WBS共通のサーバー側project認可
- 通常タスク履歴100件のタスク名・内容検索と状態絞り込み
- 通常貼り付け、Clipboard API、Web Speech API 日本語音声入力と非対応フォールバック
- UTF-8/Shift_JISの `.txt/.md/.csv` 議事録読込
- JPEG/PNG/WebPの選択・撮影・貼り付けと、Tesseract.jsによるブラウザー内日本語OCR
- ASP.NET Core API、DataAnnotations、Problem Details、CORS、SPA 配信
- RuleBased organizer によるタイトル・内容・担当者・相対/絶対期限抽出と、明示された箇条書きのサブタスク保持
- Gemini公式.NET SDK、JSON Schemaによる親タスクと0〜6件の実行可能なサブタスク整理、20秒timeout、RuleBased自動フォールバック
- 登録前に最大10件のサブタスクを1行1件で追加・修正・削除できるUI
- `ITaskOrganizer`境界を維持し、将来のAzure OpenAI adapter追加でUI・DBを変更しない構成
- Asana REST API / Mock adapter とサーバー側 PAT 管理、親作成後の `POST /tasks/{task_gid}/subtasks`
- workspaceユーザー一覧による担当者名の完全一致・一意な部分一致と、未解決時の安全な未割り当て・警告
- Asanaが実際に返した担当者GID・表示名・解決状態・警告のSQL監査と完了画面表示
- 候補未指定時のAsana既定project設定（`DefaultProjectGid`）
- 親・成功済みサブタスクの二重登録防止と、`PartiallyRegistered` から失敗した子だけを再試行する処理
- `.xlsx` / UTF-8・Shift_JIS `.csv` のブラウザー内解析、sheet・header行・data開始行の選択
- 複数列をタイトル・説明・担当者・期限等へ割り当てる自由列マッピングと見出し名からの初期推測
- 先頭20行からの見出し行自動判定、説明付き列マッピング、割り当て済み列の強調表示と再自動設定
- WBSの「ファイルを選ぶ → 読み取りを確認 → Asanaへ登録」3ステップ表示、件数・読取項目サマリー、専門設定の段階表示
- Asana workspaceのproject/section一覧APIと、通常入力・WBSでの名前選択（GID直接入力もフォールバックとして維持）
- 通常入力・サブタスク・WBSの開始日編集、SQL履歴、Asana `start_on` 送信、期限との前後関係検証
- WBSの登録対象列（はい/いいえ、○/×、1/0）、開始日列、登録前の件数・登録先確認ダイアログ
- 親子関係なし、識別キー・親キー、階層レベル、大項目・中項目等の階層列による親子変換
- 複数の名前付きWBSテンプレート保存・更新・削除、レイアウト署名一致時の自動適用
- 最大5,000行の編集可能プレビュー、対象外指定、日付・親不足・重複キー・循環参照の事前検証
- PCの一覧表とスマートフォンの1タスク1カードを切り替えるレスポンシブ確認画面、読取結果へ戻る操作
- WBS server dry-run、一括登録、行hash重複防止、部分失敗からの再開、エラーCSV
- EF Core の16テーブルと8 migration（認証session、暗号化Asana接続、project設定、WBS履歴を含む）
- Development/Test の InMemory provider 差し替え
- .NET User Secrets によるローカルSQL Server設定と環境変数による配備先差し替え
- 再実行可能な `scripts/Test-SqlServerIntegration.ps1`
- WinForms + WebView2 tray launcher、Ctrl+Shift+A、clipboard bridge、登録後自動非表示、標準の最小化・閉じるボタン
- ランチャー通常起動時の入力画面表示、`--background` tray起動、`--clipboard` 起動
- Windowsランチャーから1件登録とExcel・CSVまとめ登録を切り替え、まとめ登録時は確認しやすい大きさへ自動拡大
- launcherの単一起動と、trayから切替可能なWindowsログイン時自動起動
- GitHub Actions CI、API/launcher配布ZIPスクリプト、SQL backup/VERIFYONLYスクリプト、ready health
- README、AGENTS、REQUIREMENTS、ARCHITECTURE、IMPLEMENTATION_PLAN、STATUS、DECISIONS
- `docs/architecture.html`、`docs/architecture.json`、`docs/architecture_readme.md`
- GitHub `onishichimaki/Asana_App` の `main` へ PR #1・#2 をマージし、実連携検証更新を PR #3 で公開
- Asana PATをローカルUser Secretsへ保存し、既定workspace/project GIDを設定
- 「仕事リクエスト」projectへ実API登録し、SQL履歴と二重登録防止を確認

## 検証結果

- API project build: 成功、警告0、エラー0
- Launcher project build: 成功、警告0、エラー0
- React lint: 成功
- React production build: 成功
- xUnit: 46件成功、失敗0
- 実 HTTP smoke: health、HTML、bundle、organize、Mock register が成功
- SQL Server `DESKTOP-RQ3T767/TaskCapture`: 8 migration と必須16テーブルを確認
- 実SQL結合: 通常候補とWBS行の開始日・期限、親子整理、通常Mock親子登録、WBSテンプレート・親子行・Mock登録、API再起動後の履歴再取得を確認（`1|1|1|1|1|2|4|1|1|1|2`）
- 認証API: 未認証401、会社外メール拒否、誤コード拒否、session作成、logout後401、監査保存、管理画面で付与した管理者権限の再ログイン後維持を確認
- project認可: お気に入り保存、許可一覧絞り込み、未許可section APIの403を確認
- NuGet / npm dependency vulnerability scan: 既知脆弱性0
- `dotnet format --verify-no-changes`: 成功
- Launcher実機smoke: 通常起動で `Task Capture` ウィンドウhandle生成・応答を確認、trayプロセス維持
- Asana実登録: `AsanaApi`成功、Task GID `1216673939374366`、project `1216674009964669`
- 実登録後のSQL履歴: `Registered` / `AsanaApi` / 同一Task GIDを確認
- 同一候補の再登録: `AlreadyRegistered=true`、Asana重複作成なし
- browser UI QA: PC幅と390px幅で横スクロールなし、入力・候補確認画面を目視確認
- 議事録実読込: `.md` から807文字を入力欄へ反映
- 画像OCR実動作: 日本語画面画像から476文字を抽出し、画像ファイル非送信を確認
- Gemini organizer: 構造化JSON変換、JST基準日、欠損値処理、RuleBasedフォールバックをSDKモックで確認
- Gemini mode / APIキー未設定の実HTTP smoke: healthはGemini modelとfallback有効を返し、候補はRuleBasedで正常生成
- Gemini実通信: fallback無効の独立InMemory APIで `gemini-3.5-flash` がタイトル・担当者・期限を構造化し、HTTP 200を確認
- Geminiサブタスク実通信: `カレーを作る、大西` から親タイトル、担当者と「レシピを決める」「冷蔵庫の食材を確認する」「不足している食材を買う」「カレーを調理する」を生成
- Mock親子登録: 親1件とサブタスク4件を登録し、成功後の再送で親GIDが変わらないことを確認
- 部分失敗テスト: 2件目の子だけ初回失敗させ、再試行で親と成功済み子を作り直さず失敗した子だけを登録
- 担当者名テスト: 「大西」→「大西 千茉季」の一意な部分一致と、同姓2名時に未割り当て・警告となることを確認
- 実Asana親子登録: 親GID `1216837143593172`、子GID `1216837009090537` / `1216837008878522` を作成
- 実Asana担当者解決: 「大西」を「大西努」/ GID `1216675064179055` へ解決し、親子へ割り当て、SQLへ `Resolved` として保存
- 実通信で検出した新規サブタスクのレスポンス二重表示を修正し、0件から2件追加する再発テストを追加
- 通常起動: SQL Server / Gemini（fallback有効）/ Asana API、Windows launcher応答を確認
- API再疎通: SQL Server / Gemini / Asana APIをhealthで確認し、Gemini実通信でタイトル・担当者・期限を再抽出
- compact UI QA: launcher相当520px幅とiPhone相当390px幅で横スクロール・console警告なし
- Launcher実画面QA: 初期操作が1画面内に収まり、標準タイトルバーの最小化「－」と閉じる「×」を確認
- Launcher Excel・CSV QA: Windowsアプリ内で「Excel・CSVをまとめて登録」を選択し、ファイル選択画面への切替と自動拡大を実画面で確認
- サブタスクUI QA: launcher相当520px幅とiPhone相当390px幅で4件の編集欄を確認し、横スクロール・console警告なし
- アーキテクチャ資料: JSON構文、HTML構文、全 `source_files` の存在確認に成功
- WBS UI QA: 親キー・階層レベル・階層列fixtureをそれぞれ4件へ変換し、深度 `0→1→2→1`、エラー0、横スクロール・console警告なしを確認
- WBS Excel QA: 2 sheetの実 `.xlsx` を読み込み、sheet切替、列自動推測、Excel日付変換、深度 `0→1→2→1` を確認
- WBSテンプレートQA: 保存後に同一レイアウトを再読込し、列・階層設定の自動適用を確認
- WBS Mock登録: 親子4件を登録し、全件成功を画面で確認
- WBS実Asana登録: 親GID `1216841537461362`、子GID `1216857439579470` を作成し、「大西」を両方とも「大西努」へ解決
- WBS実Asana再送: 同一batchで `AlreadyRegistered=true`、親子の重複作成なし
- WBS操作性QA: 給食スパイスカレー30行Excelで見出し4行目・13列中9項目を自動判定し、開始日・期限・登録対象・親子キーを正しく割り当て
- WBS登録先QA: 実Asana APIから「仕事リクエスト」projectを取得し、名前選択、30件・エラー0件preview、登録直前の件数・登録先確認を表示
- WBS初見性QA: 30行Excelで通常画面を7つの平易な読取項目へ要約し、「識別キー」「親キー」「階層レベル」「階層列」は詳細設定を開くまで表示されないことを確認
- WBSレスポンシブQA: PC幅は30件を横スクロールなしの一覧表、iPhone相当390pxは1タスク1カードで表示し、ページ全体・previewとも横スクロールなし、console警告なし
- WBS戻る操作QA: 確認画面から読取結果へ戻るとstep 2へ復帰し、再度30件・エラー0件の確認画面を生成できることを確認
- WBS一般形式QA: 専用の取込対象・階層レベル列を持たない金沢旅行WBSで、見出し6行目と `大項目 / 中項目 / 小項目` を自動判定。30作業行から親1件・中項目7件・小項目30件の38件を生成し、server dry-runでエラー0件、横スクロール・console警告なしを確認
- WBS再取込: 行ごとに「新しく作る・変更あり・前回と同じ」を判定し、変更行の上書き有無を登録前に選択可能
- WBS安全な取り消し: 今回新しく作ったタスクだけを子から順に元へ戻し、既存・上書きタスクを残す
- WBS担当者確認: 登録前チェックで担当者の解決結果と未解決警告を表示
- WBS日程: 子タスクの最も早い開始日と最も遅い期限を親へ自動反映
- WBS追加項目: 進み具合・優先度・予定時間・予定費用をAsanaの追加項目または説明へ送信
- WBS大量行UI: 検索、表示条件、親子開閉、100件ごとの表示、表示中の一括選択
- WBS学習・履歴: 保存した会社独自列名の再利用と、直近20件の取込履歴表示
- SQL実結合再確認: migration適用、React配信、通常入力、WBS親子登録、API再起動後の履歴再取得が成功

## 未完了 / 外部設定待ち

- Windows tray/hotkey、iPhone/iPad カメラOCR・音声・clipboard は実端末で最終確認が必要。
- 画像OCRは初回にTesseract.js日本語言語モデルを取得するため、初回のみインターネット接続が必要。
- 本番SMTP、Asana OAuthアプリのClient ID/Secret/Callback URL、HTTPS公開URL、永続Data Protection鍵は配備先未設定。
- 本番の定期バックアップ実行主体・監視通知先は運用環境決定後に設定が必要。

## 次に必要な作業

1. 本番SMTP、Asana OAuthアプリ、HTTPS、Secret Store、Data Protection鍵フォルダーを設定する。
2. HTTPS の iPhone/iPad と配布版Windows launcherで、ログイン・画像・音声・WBS・Asana実登録の受入テストを実施する。
3. SQL backupスクリプトをタスクスケジューラ等へ登録し、監視通知先を決める。
