# 設計判断記録

## D-001: .NET 8 LTS を使用

- 日付: 2026-07-18
- 状態: 採用
- 判断: ASP.NET Core、EF Core、WinForms はインストール済みの .NET 8 LTS を使用する。
- 理由: Web API と Windows ランチャーを同じ C# 世代で保守でき、MVP に十分なサポート期間がある。

## D-002: 開発既定値は外部依存なし

- 日付: 2026-07-18
- 状態: 採用
- 判断: Development は EF Core InMemory、RuleBased organizer、Mock Asana を既定とする。本番相当では SQL Server と Asana API を設定で選択する。
- 理由: 外部認証情報と SQL Server インスタンスが未提供でも、全フローの実装・テストを継続するため。SQL Server provider と migration は同じモデルから提供する。

## D-003: 1入力1候補

- 日付: 2026-07-18
- 状態: 採用
- 判断: MVP は1回の入力から1件の TaskCandidate を生成する。
- 理由: 確認操作を単純化し、最短登録を優先する。複数候補分割は対象外とする。

## D-004: AI の MVP 実装は差し替え可能なルールベース

- 日付: 2026-07-18
- 状態: 採用
- 判断: `ITaskOrganizer` として決定的な日本語対応ルール実装を提供し、AI API 実装は将来差し替える。
- 理由: API/モデル指定がなく、外部キーもない。要件にあるモックまたはルールベース継続を満たし、テストの再現性を高める。

## D-005: 音声はブラウザーの Web Speech API

- 日付: 2026-07-18
- 状態: 採用
- 判断: サーバー録音保存や音声 API は持たず、対応ブラウザーの SpeechRecognition を使用する。
- 理由: 1画面、低遅延、秘密情報不要という MVP に合う。非対応端末では明示してテキスト入力へフォールバックする。

## D-006: ランチャーは WinForms + WebView2

- 日付: 2026-07-18
- 状態: 採用
- 判断: tray と `RegisterHotKey` は WinForms、画面は同じ React UI を WebView2 で表示する。
- 理由: UI 重複を避け、クリップボード投入と登録後の自動クローズをネイティブ bridge だけで実現できる。

## D-007: GitHub 調査不能時の扱い

- 日付: 2026-07-18
- 状態: 採用
- 判断: 空の作業フォルダーを新規リポジトリとして初期化し、GitHub Issue/PR 調査不能を STATUS に記録して実装を継続する。
- 理由: GitHub コネクタは再認証要求、`gh` CLI は未導入で、対象リモートを特定できなかった。MVP 完成を優先し、リモート接続後に同期確認する。

## D-008: DB provider 選択は DI 解決時に行う

- 日付: 2026-07-18
- 状態: 採用
- 判断: `AddDbContext` の factory 内で現在の configuration を読み、SQL Server または InMemory を選ぶ。
- 理由: 開発・テスト・本番の設定差し替えを同じ起動経路で有効にし、WebApplicationFactory でも外部 SQL Server なしに結合テストできるようにする。

## D-009: 成功後の登録は冪等

- 日付: 2026-07-18
- 状態: 採用
- 判断: 同じ TaskCandidate に成功済み AsanaRegistration があれば外部 API を再実行せず、既存結果を `AlreadyRegistered=true` で返す。
- 理由: ダブルクリック、通信再送、launcher の再操作による Asana 二重登録を防ぐ。

## D-010: ローカルSQL接続はUser Secretsで差し替える

- 日付: 2026-07-18
- 状態: 採用
- 判断: リポジトリのDevelopment既定はInMemoryのまま維持し、本機の `DESKTOP-RQ3T767/TaskCapture` は .NET User Secrets でSQL Serverへ切り替える。設計時DbContextは `ConnectionStrings__TaskCapture` を優先する。
- 理由: マシン固有の接続先をソースへ埋め込まず、ローカル実SQLを常用しつつ、完成後はUser Secretsまたは配備先環境変数の接続文字列だけで付け替えられるようにするため。

## D-011: ランチャーの通常起動では入力画面を表示する

- 日付: 2026-07-18
- 状態: 採用
- 判断: ランチャーの通常起動は入力画面を1回表示し、`--background` 指定時だけtrayのみで起動する。`--clipboard` は起動直後にclipboard入力を使う。入力画面を開いている間はタスクバーにも表示する。
- 理由: 初回起動が完全に無表示だと起動成功を判断しにくいため。Windows自動起動では従来どおり静かに常駐でき、閉じた後はtrayへ戻る。

## D-012: Asanaの既定登録先projectをサーバー設定で固定できる

- 日付: 2026-07-18
- 状態: 採用
- 判断: 候補でproject GIDを指定しない通常操作では `Integration:Asana:DefaultProjectGid` を使用し、候補の明示値、既定project、既定workspaceの順に登録先を決定する。
- 理由: 1画面の高速入力で詳細設定を毎回開かず、限定利用先の「仕事リクエスト」projectへ確実に登録するため。登録先はクライアントへ秘密として保持せず、サーバー設定だけで配備時に差し替える。

## D-013: 画像はブラウザー内OCR、議事録はテキスト抽出後に既存フローへ渡す

- 日付: 2026-07-20
- 状態: 採用
- 判断: JPEG/PNG/WebPは `Tesseract.js` の日本語モデルでブラウザー内OCRし、議事録はUTF-8/Shift_JISの `.txt/.md/.csv` をブラウザーで読み込む。画像やファイル本体はAPI/DBへ送らず、最大10,000文字の抽出結果だけを既存の整理APIへ渡す。音声は引き続きWeb Speech APIを使う。
- 理由: DBスキーマと秘密情報境界を変えずにPC・iPhone・iPad共通の入力入口を追加できるため。画像内容をサーバーへ保管せず、外部OCRキーも不要になる。初回OCRの言語モデル取得とブラウザー差はREADME/STATUSへ明記する。

## D-014: Geminiを一時AI providerとし、RuleBasedへ自動フォールバックする

- 日付: 2026-07-20
- 状態: 採用
- 判断: `ITaskOrganizer`のGemini実装をGoogle Gen AI .NET SDKとJSON Schema構造化出力で追加する。`TaskOrganization:Mode=Gemini`で選択し、キー未設定、timeout、API/応答エラー時は既定でRuleBasedへフォールバックする。将来のAzure OpenAIは同じinterfaceへadapterを追加する。
- 理由: 現在のUI・workflow・DBを変更せずAI品質を試せ、外部サービス障害でも高速登録を継続できるため。provider固有の認証情報はサーバーSecretだけに置き、チャットやGitHubへ露出したキーは使用しない。

## D-015: 初期画面を短くし、launcherは標準Windowsタイトルバーを使う

- 日付: 2026-07-20
- 状態: 採用
- 判断: 常時表示していた手順説明、入力方法の補足、監査説明を省き、初期画面は入力方法・入力欄・整理ボタンへ集中する。launcher queryでは余白と入力欄をさらに縮める。WinFormsは標準のサイズ変更可能なタイトルバーを使い、最小化「－」と閉じる「×」を表示する。閉じる操作は従来どおりtray格納とする。
- 理由: 小さい常駐launcherでも主要操作をスクロールなしで表示し、Windows利用者が迷わず最小化・格納できるようにするため。Webとlauncherで機能実装は共有したまま、表示密度だけを用途に合わせる。
