# 実装計画

## MVP 実装順

- [x] 1. ローカル構成・必須文書・既存コードを確認
- [x] 2. 要件と対象外を整理
- [x] 3. API、UI、ランチャーのアーキテクチャを設計
- [x] 4. SQL Server の8テーブルと関係を設計
- [x] 5. ASP.NET Core API、EF Core、migration を実装
- [x] 6. React の1画面 UI を実装
- [x] 7. Asana REST/Mock 切り替えを実装
- [x] 8. ルールベース AI 整理を実装
- [x] 9. Web Speech API 音声入力を実装
- [x] 10. WebView2 常駐ランチャーを実装
- [x] 11. 単体・API 結合・UI ビルドを確認
- [x] 12. 必須文書とアーキテクチャ資料を最終更新
- [x] 13. メイリオUI優先の画面、議事録読込、クライアント内画像OCRを追加
- [x] 14. Gemini構造化整理、RuleBasedフォールバック、将来のAzure OpenAI差し替え境界を追加
- [x] 15. AIサブタスク分解、親子候補の編集・永続化、Asana親子登録、部分失敗時の再試行を追加
- [x] 16. 自由文担当者名のAsanaユーザー解決、結果監査、実Asana親子登録を確認

## Phase 2: 可変レイアウトWBS取込

- [ ] 17. XLSX/CSVのsheet・header行・data開始行を選べるブラウザー内parserを追加
- [ ] 18. 自由列マッピングと「親ID列 / 階層レベル列」の選択UIを追加
- [ ] 19. プロジェクト別ImportProfileの保存・読込を実装
- [ ] 20. 親子・担当者・日付・除外行・エラーのプレビューとdry-runを実装
- [ ] 21. ImportBatches / ImportRows migrationと一括登録APIを実装
- [ ] 22. 行hashによる冪等性、部分失敗からの再開、エラーCSV出力を実装
- [ ] 23. 複数レイアウトのfixtureで結合テストと実Asana少量スモークを実施

## 品質ゲート

1. `dotnet build TaskCapture.sln` が成功する。
2. `dotnet test TaskCapture.sln` の主要テストが成功する。
3. `npm run lint` と `npm run build` が成功する。
4. API を InMemory/RuleBased/Mock で起動し、整理から登録まで疎通する。
5. `scripts/Test-SqlServerIntegration.ps1` で migration、登録、再起動後のSQL永続化を確認する。
6. レスポンシブ UI と launcher bridge をブラウザーで確認し、PC幅・390px幅で横スクロールがないことを検証する。
7. 秘密情報がソースと生成 bundle に存在しないことを検索確認する。
8. 議事録ファイル読込と日本語画像OCRをブラウザーで実動作確認する。
9. GeminiはSDKをモック化した構造化変換・フォールバックテストを常時実行し、実通信は未露出のローカルSecretがある場合だけ行う。
10. 親登録後に一部のサブタスク登録が失敗しても、再試行で親と成功済みサブタスクを重複作成しないことを確認する。
11. 担当者名は完全一致または一意な部分一致だけを採用し、曖昧な名前を勝手に割り当てないことを確認する。
