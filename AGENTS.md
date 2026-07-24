# AGENTS.md

## プロジェクト方針

このリポジトリは、入力を短時間で整理して Asana に登録する Task Capture MVP の正本です。

- MVP の中心は「入力 → 整理 → 確認・修正 → 登録」の1画面フローです。
- Asana をタスク管理の正本とし、本アプリにはタスク管理機能を追加しません。
- 秘密情報はクライアントへ渡さず、API の環境変数または Secret Manager で管理します。
- 外部サービスなしでも開発・テスト可能なモックを維持します。
- 新しい設計判断は `DECISIONS.md`、進捗は `STATUS.md` に追記します。

## 構成

- `src/TaskCapture.Api`: ASP.NET Core Web API、EF Core、AI/Asana 連携
- `src/taskcapture-web`: React + TypeScript の通常入力・可変レイアウトWBS取込UI
- `src/TaskCapture.Launcher`: WinForms + WebView2 の Windows 常駐ランチャー
- `tests/TaskCapture.Api.Tests`: API・ルール整理・WBS一括登録・永続化の主要テスト
- `tests/fixtures`: 親キー・階層レベル・階層列のWBS取込fixture
- `docs`: 人間・AI 引き継ぎ用のアーキテクチャ資料

## 標準確認

```powershell
dotnet restore TaskCapture.sln
dotnet build TaskCapture.sln --no-restore
dotnet test TaskCapture.sln --no-build
npm ci --prefix src/taskcapture-web
npm run lint --prefix src/taskcapture-web
npm run build --prefix src/taskcapture-web
```

変更後は関連テストを先に実行し、最終確認で上記一式を実行してください。生成済み `wwwroot`、`bin`、`obj`、`node_modules` はコミットしません。

SQL Server関連を変更した場合は、利用可能な検証用インスタンスを指定して実SQLスモークも実行してください。スクリプトは履歴確認用のデータを1件残します。

```powershell
& ./scripts/Test-SqlServerIntegration.ps1 -ServerInstance YOUR_SERVER -Database TaskCapture
```

## 実装上の注意

- API の DTO へ DataAnnotations を設定し、自由入力の最大長を維持します。
- Asana PAT、AI API キー、接続文字列をログへ出しません。
- `Integration:Asana:Mode=Mock` と `TaskOrganization:Mode=RuleBased` を常に動作可能に保ち、Gemini失敗時のフォールバックを壊しません。
- Gemini APIキーはUser Secrets、`TaskOrganization__Gemini__ApiKey`、または `GEMINI_API_KEY` から読み、テスト・ログ・例外詳細・文書へ実値を残しません。
- DB スキーマ変更時は EF Core migration、`ARCHITECTURE.md`、`docs/architecture.json` を同期します。
- ランチャーのグローバルホットキー変更時は README と tray tooltip を同期します。
- WBSファイル本体はブラウザー内で解析し、APIへは確認済みの正規化行だけを送ります。10MB・5,000行上限、server dry-run、行hashの重複防止、失敗行だけの再試行を維持します。
