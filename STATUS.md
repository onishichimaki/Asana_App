# STATUS

最終更新: 2026-07-18

## 現在地

- フェーズ: MVP 実装・実SQL結合検証・GitHub公開・文書整備完了
- MVP 判定: Mock/RuleBased/SQL Server 経路を含め完成

## 完了

- React のレスポンシブ1画面（入力 → 整理 → 確認・修正 → 登録）
- 通常貼り付け、Clipboard API、Web Speech API 日本語音声入力と非対応フォールバック
- ASP.NET Core API、DataAnnotations、Problem Details、CORS、SPA 配信
- RuleBased organizer によるタイトル・内容・担当者・相対/絶対期限抽出
- Asana REST API / Mock adapter とサーバー側 PAT 管理
- 成功済み候補の二重登録防止
- EF Core の6テーブル、index/relationship、InitialCreate SQL Server migration
- Development/Test の InMemory provider 差し替え
- .NET User Secrets によるローカルSQL Server設定と環境変数による配備先差し替え
- 再実行可能な `scripts/Test-SqlServerIntegration.ps1`
- WinForms + WebView2 tray launcher、Ctrl+Shift+A、clipboard bridge、登録後自動非表示
- ランチャー通常起動時の入力画面表示、`--background` tray起動、`--clipboard` 起動
- README、AGENTS、REQUIREMENTS、ARCHITECTURE、IMPLEMENTATION_PLAN、STATUS、DECISIONS
- `docs/architecture.html`、`docs/architecture.json`、`docs/architecture_readme.md`
- GitHub `onishichimaki/Asana_App` の `main` へ PR #1 をマージ

## 検証結果

- API project build: 成功、警告0、エラー0
- Launcher project build: 成功、警告0、エラー0
- React lint: 成功
- React production build: 成功
- xUnit: 8件成功、失敗0
- 実 HTTP smoke: health、HTML、bundle、organize、Mock register が成功
- SQL Server `DESKTOP-RQ3T767/TaskCapture`: InitialCreate migration と必須6テーブルを確認
- 実SQL結合: 整理、Mock登録、API再起動後の履歴再取得、Users/履歴/候補/登録/設定/監査行を確認
- NuGet / npm dependency vulnerability scan: 既知脆弱性0
- `dotnet format --verify-no-changes`: 成功
- Launcher実機smoke: 通常起動で `Task Capture` ウィンドウhandle生成・応答を確認、trayプロセス維持

## 未完了 / 外部待ち

- Asana PAT、workspace/project GID が未提供のため、実 Asana 登録 smoke test は未実施。
- browser control の初期化競合により独立ブラウザーの自動スクリーンショットQAは未実施。SPA 200、TypeScript build、lint、responsive CSS静的確認、ランチャー実ウィンドウhandle確認で代替した。
- Windows tray/hotkey、iPhone/iPad 音声・clipboard は実端末で最終確認が必要。

## 次に必要な作業

1. Asana PAT と workspace/project GID をサーバーへ設定し、限定 project で1件登録する。
2. HTTPS の iPhone/iPad と Windows 実機で短い受入テストを実施する。
3. 外部公開する場合は組織認証、TLS、rate limit を追加する。
