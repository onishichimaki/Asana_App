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
- [x] 13. メイリオUI優先の画面、議事録読込、クライアント内画像OCRを追加

## 品質ゲート

1. `dotnet build TaskCapture.sln` が成功する。
2. `dotnet test TaskCapture.sln` の主要テストが成功する。
3. `npm run lint` と `npm run build` が成功する。
4. API を InMemory/RuleBased/Mock で起動し、整理から登録まで疎通する。
5. `scripts/Test-SqlServerIntegration.ps1` で migration、登録、再起動後のSQL永続化を確認する。
6. レスポンシブ UI と launcher bridge をブラウザーで確認し、PC幅・390px幅で横スクロールがないことを検証する。
7. 秘密情報がソースと生成 bundle に存在しないことを検索確認する。
8. 議事録ファイル読込と日本語画像OCRをブラウザーで実動作確認する。
