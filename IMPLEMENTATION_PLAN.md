# 実装計画

## MVP 実装順

- [x] 1. ローカル構成・必須文書・既存コードを確認
- [x] 2. 要件と対象外を整理
- [x] 3. API、UI、ランチャーのアーキテクチャを設計
- [x] 4. SQL Server の6テーブルと関係を設計
- [x] 5. ASP.NET Core API、EF Core、migration を実装
- [x] 6. React の1画面 UI を実装
- [x] 7. Asana REST/Mock 切り替えを実装
- [x] 8. ルールベース AI 整理を実装
- [x] 9. Web Speech API 音声入力を実装
- [x] 10. WebView2 常駐ランチャーを実装
- [x] 11. 単体・API 結合・UI ビルドを確認
- [x] 12. 必須文書とアーキテクチャ資料を最終更新

## 品質ゲート

1. `dotnet build TaskCapture.sln` が成功する。
2. `dotnet test TaskCapture.sln` の主要テストが成功する。
3. `npm run lint` と `npm run build` が成功する。
4. API を InMemory/RuleBased/Mock で起動し、整理から登録まで疎通する。
5. レスポンシブ UI と launcher bridge をブラウザーで確認する（自動ブラウザー環境の初期化競合により、build/static/HTTP確認まで。実端末目視は `STATUS.md` の次作業）。
6. 秘密情報がソースと生成 bundle に存在しないことを検索確認する。
