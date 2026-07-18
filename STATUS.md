# STATUS

最終更新: 2026-07-18

## 現在地

- フェーズ: MVP 実装・自動検証・文書整備完了
- MVP 判定: Mock/RuleBased/InMemory 経路は完成

## 完了

- React のレスポンシブ1画面（入力 → 整理 → 確認・修正 → 登録）
- 通常貼り付け、Clipboard API、Web Speech API 日本語音声入力と非対応フォールバック
- ASP.NET Core API、DataAnnotations、Problem Details、CORS、SPA 配信
- RuleBased organizer によるタイトル・内容・担当者・相対/絶対期限抽出
- Asana REST API / Mock adapter とサーバー側 PAT 管理
- 成功済み候補の二重登録防止
- EF Core の6テーブル、index/relationship、InitialCreate SQL Server migration
- Development/Test の InMemory provider 差し替え
- WinForms + WebView2 tray launcher、Ctrl+Shift+A、clipboard bridge、登録後自動非表示
- README、AGENTS、REQUIREMENTS、ARCHITECTURE、IMPLEMENTATION_PLAN、STATUS、DECISIONS
- `docs/architecture.html`、`docs/architecture.json`、`docs/architecture_readme.md`

## 検証結果

- API project build: 成功、警告0、エラー0
- Launcher project build: 成功、警告0、エラー0
- React lint: 成功
- React production build: 成功
- xUnit: 7件成功、失敗0
- 実 HTTP smoke: health、HTML、bundle、organize、Mock register が成功
- SQL Server migration: InitialCreate と冪等 SQL script の生成成功（実 DB 接続は未実施）
- NuGet / npm dependency vulnerability scan: 既知脆弱性0
- `dotnet format --verify-no-changes`: 成功
- Launcher process smoke: 起動後2秒以上維持、早期終了なし

## 未完了 / 外部待ち

- GitHub connector は再認証が必要。開始時フォルダーは空で remote がなく、Issue/PR を確認できなかった。`gh` CLI も未導入。
- SQL Server 接続先が未提供のため、実 DB への migration 適用 smoke test は未実施。
- Asana PAT、workspace/project GID が未提供のため、実 Asana 登録 smoke test は未実施。
- browser control の初期化競合により自動画面目視 QA は未実施。HTML/JS 200、TypeScript build、lint、responsive CSS 静的確認で代替した。
- Windows tray/hotkey、iPhone/iPad 音声・clipboard は実端末で最終確認が必要。

## 次に必要な作業

1. GitHub connector を再認証し、remote / Issue / PR / default branch と同期する。
2. 限定 SQL Server へ接続して migration と履歴行を確認する。
3. 限定 Asana project で1件登録し、assignee/due/project/sectionを確認する。
4. HTTPS の iPhone/iPad と Windows 実機で短い受入テストを実施する。
5. 外部公開する場合は組織認証、TLS、rate limit を追加する。
