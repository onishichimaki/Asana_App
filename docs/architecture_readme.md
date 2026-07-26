# アーキテクチャ資料の読み方

## ファイル

- `architecture.html`: 人向けの1ページ構成図。カードを選ぶと責務と根拠ソースを確認できる。
- `architecture.json`: AI・ツール向けの構造化インベントリ。未確認事項も省略せず `risks_or_unknowns` に記録する。
- ルートの `ARCHITECTURE.md`: 実装と運用で参照する簡潔な設計契約。

`architecture.html` は単体ファイルとしてブラウザーで開ける。`architecture.json` の各具体要素には、判断の根拠となる相対 `source_files` を持たせる。

## 更新手順

1. `rg --files -g '!**/bin/**' -g '!**/obj/**' -g '!**/node_modules/**'` で構成を確認する。
2. Controller、`Program.cs`、React entrypoint、EF entities/migrations、launcher entrypoint、Gemini/Azure OpenAI/Asana等の外部adapterを確認する。
3. API、テーブル、外部連携、data flowの追加・削除をJSONとHTMLへ反映する。
4. 確認できない推測は事実欄へ置かず、`risks_or_unknowns` へ根拠とともに置く。
5. 次の検証を実行する。

```powershell
python -m json.tool docs\architecture.json > $null
@'
from html.parser import HTMLParser
from pathlib import Path
class P(HTMLParser): pass
P().feed(Path("docs/architecture.html").read_text(encoding="utf-8"))
print("html_parse_ok")
'@ | python -
```

DBスキーマ変更時は EF Core migration、ルート `ARCHITECTURE.md`、この3ファイルを同じ変更で更新する。親子登録、担当者名解決、project/section一覧取得のように複数の外部要求をまたぐ処理では、実際に保存する外部GID・警告、部分成功状態、重複を防ぐ再試行を `registration_flow` とリスクにも記録する。AI provider、構造化出力、フォールバックを変更した場合は、`external_integrations`、`dependencies`、`organize_flow`、本番設定診断、リスクを同期する。WBSのparser、自動判定、3ステップ導線、PC表／スマホカード、検索・ページ表示、開始日、登録先、追加項目、担当者確認、前回との差、履歴、元に戻す処理、読み取り方・列名・取込履歴・行テーブルを変更した場合は、`wbs_import_ui`、`wbs_import_service`、`wbs_import_flow` とDB一覧を同期する。

認証・認可を変更した場合は、会社メールの事前登録、Asana OAuthのPKCE/state/照合Cookie、session、workspace所属、claims、所有者境界、管理者policy、ログアウト・利用停止時の失効、project許可を `asana_login_and_authorization`、`asana_oauth_login_flow` へ同期する。Asana credentialを変更した場合は、PAT/OAuth、会社ドメイン・事前登録・workspaceの条件、Data Protection token、refresh、provider解除とローカル消去を `asana_user_connection`、`asana_oauth_login_flow`、`external_integrations`、DB一覧、リスクへ同期する。開発用EmailCodeを変更した場合は `email_code_fallback_flow` も更新する。秘密値そのものはどの資料にも記載しない。
