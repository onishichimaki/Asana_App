# 実装計画

## MVP 実装順

- [x] 1. ローカル構成・必須文書・既存コードを確認
- [x] 2. 要件と対象外を整理
- [x] 3. API、UI、ランチャーのアーキテクチャを設計
- [x] 4. SQL Server の必須テーブルと関係を設計（現在16テーブル）
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

- [x] 17. XLSX/CSVのsheet・header行・data開始行を選べるブラウザー内parserを追加
- [x] 18. 自由列マッピングと4種類の階層方式の選択UIを追加
- [x] 19. 複数のImportProfileの保存・自動適用・更新・削除を実装
- [x] 20. 親子・担当者・日付・除外行・エラーのプレビューとserver dry-runを実装
- [x] 21. WbsImportProfiles / Batches / Rows migrationと一括登録APIを実装
- [x] 22. 行hashによる冪等性、部分失敗からの再開、エラーCSV出力を実装
- [x] 23. 親キー・階層レベル・階層列fixtureで結合テストと実Asana親子スモークを実施
- [x] 24. 見出し行・列の自動判定、説明付きマッピング、登録対象列を追加
- [x] 25. Asana project/section一覧APIと名前選択UI、登録直前の件数・登録先確認を追加
- [x] 26. 通常入力とWBSへ開始日を追加し、EF migration・日付順検証・実SQLスモークを完了
- [x] 27. WBS取込を初見向け3ステップへ整理し、専門設定の段階表示、読取サマリー、スマホ用タスクカードと戻る操作を追加

## Phase 3: 個別アカウント・運用

- [x] 28. 会社メール確認コード、HttpOnly Cookie、session失効、rate limitを実装
- [x] 29. APIを既定認証必須にし、通常入力・WBS・履歴の所有者確認を実装
- [x] 30. 利用者別Asana OAuth、Data Protection token暗号化、refresh、接続解除を実装
- [x] 31. Asanaプロジェクト検索・お気に入り、利用者ごとの登録先制限を実装
- [x] 32. 利用停止・管理者・許可プロジェクトの管理画面と監査APIを実装
- [x] 33. 通常タスク履歴の検索・状態絞り込みを実装
- [x] 34. Windows自動起動・単一起動、配布ZIP、SQLバックアップ、ready health、GitHub Actions CIを実装
- [x] 35. 認証・プロジェクト制限を含む自動テストとDESKTOP-RQ3T767実SQL統合を完了
- [ ] 36. 本番SMTP、Asana OAuthアプリ、HTTPS URL、永続Data Protection鍵を配備先で設定
- [ ] 37. iPhone/iPad実機と配備済みHTTPS URLで最終受入テスト

## Phase 4: 本番安全性とAzure OpenAI

- [x] 38. 会社メールとAsana profile emailの一致をOAuth callback・token利用時に強制し、旧connectionを再接続対象にする
- [x] 39. Azure OpenAI v1 strict JSON Schema adapterとRuleBasedフォールバックを追加する
- [x] 40. Production必須設定の安全側起動検証、ready診断、配布設定例、定期バックアップ登録、受入チェックリストを追加する

## 品質ゲート

1. `dotnet build TaskCapture.sln` が成功する。
2. `dotnet test TaskCapture.sln` の主要テストが成功する。
3. `npm run lint` と `npm run build` が成功する。
4. API を InMemory/RuleBased/Mock で起動し、整理から登録まで疎通する。
5. `scripts/Test-SqlServerIntegration.ps1` で migration、登録、再起動後のSQL永続化を確認する。
6. レスポンシブ UI と launcher bridge をブラウザーで確認し、PC幅・390px幅で横スクロールがないことを検証する。
7. 秘密情報がソースと生成 bundle に存在しないことを検索確認する。
8. 議事録ファイル読込と日本語画像OCRをブラウザーで実動作確認する。
9. GeminiとAzure OpenAIは構造化変換・要求形式・フォールバックテストを常時実行し、実通信は未露出のSecretがある場合だけ行う。
10. 親登録後に一部のサブタスク登録が失敗しても、再試行で親と成功済みサブタスクを重複作成しないことを確認する。
11. 担当者名は完全一致または一意な部分一致だけを採用し、曖昧な名前を勝手に割り当てないことを確認する。
12. WBSは親キー・階層レベル・階層列をfixtureで確認し、実SQLでprofile/batch/row永続化、実Asanaで親子登録、再送時の重複防止を確認する。
13. project/section一覧取得、開始日保存、`start_on`送信、開始日と期限の順序違反をAPIテストで確認する。
14. WBSの通常画面に専門語が露出せず、詳細設定が既定で閉じ、PCと390px幅で30件の確認・戻る操作に横スクロールがないことを確認する。
15. 未認証APIが401、会社外メールが400、session logout後が401、停止利用者が403になることを確認する。
16. プロジェクト制限を一覧・section/field・実登録のサーバー側で強制し、直接GID指定で回避できないことを確認する。
17. Asana OAuth tokenが平文でDB・ログ・クライアントへ残らず、Data Protection鍵を永続化できることを確認する。
18. CI、配布スクリプト、検証付きSQLバックアップを実行可能な状態に保つ。
19. Asana OAuthはログイン会社メールとprofile emailが一致しない場合にtokenを保存・使用しないことを確認する。
20. ProductionはSQL Server、SSL SMTP、利用者別Asana OAuth、Azure OpenAI、HTTPS callback、永続Data Protection鍵の不足を安全に拒否する。
