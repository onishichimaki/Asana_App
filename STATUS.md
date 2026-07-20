# STATUS

最終更新: 2026-07-20

## 現在地

- フェーズ: MVP完成、Gemini/SQL Server/Asana実連携まで確認済み
- MVP 判定: Gemini/RuleBased、SQL Server、Asana API/Mockの主要経路が完成。

## 完了

- メイリオUI優先で説明・余白を抑えたReactレスポンシブ1画面（入力 → 整理 → 確認・修正 → 登録）
- 通常貼り付け、Clipboard API、Web Speech API 日本語音声入力と非対応フォールバック
- UTF-8/Shift_JISの `.txt/.md/.csv` 議事録読込
- JPEG/PNG/WebPの選択・撮影・貼り付けと、Tesseract.jsによるブラウザー内日本語OCR
- ASP.NET Core API、DataAnnotations、Problem Details、CORS、SPA 配信
- RuleBased organizer によるタイトル・内容・担当者・相対/絶対期限抽出
- Gemini公式.NET SDK、JSON Schema構造化整理、20秒timeout、RuleBased自動フォールバック
- `ITaskOrganizer`境界を維持し、将来のAzure OpenAI adapter追加でUI・DBを変更しない構成
- Asana REST API / Mock adapter とサーバー側 PAT 管理
- 候補未指定時のAsana既定project設定（`DefaultProjectGid`）
- 成功済み候補の二重登録防止
- EF Core の6テーブル、index/relationship、InitialCreate SQL Server migration
- Development/Test の InMemory provider 差し替え
- .NET User Secrets によるローカルSQL Server設定と環境変数による配備先差し替え
- 再実行可能な `scripts/Test-SqlServerIntegration.ps1`
- WinForms + WebView2 tray launcher、Ctrl+Shift+A、clipboard bridge、登録後自動非表示、標準の最小化・閉じるボタン
- ランチャー通常起動時の入力画面表示、`--background` tray起動、`--clipboard` 起動
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
- xUnit: 17件成功、失敗0
- 実 HTTP smoke: health、HTML、bundle、organize、Mock register が成功
- SQL Server `DESKTOP-RQ3T767/TaskCapture`: InitialCreate migration と必須6テーブルを確認
- 実SQL結合: 整理、Mock登録、API再起動後の履歴再取得、Users/履歴/候補/登録/設定/監査行を確認
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
- 通常起動: SQL Server / Gemini（fallback有効）/ Asana API、Windows launcher応答を確認
- API再疎通: SQL Server / Gemini / Asana APIをhealthで確認し、Gemini実通信でタイトル・担当者・期限を再抽出
- compact UI QA: launcher相当520px幅とiPhone相当390px幅で横スクロール・console警告なし
- Launcher実画面QA: 初期操作が1画面内に収まり、標準タイトルバーの最小化「－」と閉じる「×」を確認

## 未完了 / 外部待ち

- Windows tray/hotkey、iPhone/iPad カメラOCR・音声・clipboard は実端末で最終確認が必要。
- 画像OCRは初回にTesseract.js日本語言語モデルを取得するため、初回のみインターネット接続が必要。
- HTTPS配備先のSecret Store、組織認証、TLS、rate limit、運用監視は未設定。

## 次に必要な作業

1. HTTPS の iPhone/iPad と Windows 実機で、画像・議事録・音声を含む短い受入テストを実施する。
2. 外部公開する場合は配備先Secret Store、組織認証、TLS、rate limit、運用監視を追加する。
