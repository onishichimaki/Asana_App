# アーキテクチャ資料の読み方

## ファイル

- `architecture.html`: 人向けの1ページ構成図。カードを選ぶと責務と根拠ソースを確認できる。
- `architecture.json`: AI・ツール向けの構造化インベントリ。未確認事項も省略せず `risks_or_unknowns` に記録する。
- ルートの `ARCHITECTURE.md`: 実装と運用で参照する簡潔な設計契約。

`architecture.html` は単体ファイルとしてブラウザーで開ける。`architecture.json` の各具体要素には、判断の根拠となる相対 `source_files` を持たせる。

## 更新手順

1. `rg --files -g '!**/bin/**' -g '!**/obj/**' -g '!**/node_modules/**'` で構成を確認する。
2. Controller、`Program.cs`、React entrypoint、EF entities/migrations、launcher entrypoint、Gemini/Asana等の外部adapterを確認する。
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

DBスキーマ変更時は EF Core migration、ルート `ARCHITECTURE.md`、この3ファイルを同じ変更で更新する。AI providerまたはフォールバックを変更した場合も、`external_integrations`、`dependencies`、`organize_flow`、リスクを同期する。
