# TODO (PoC 検証と本番化に向けたタスク)

進捗はチェックボックスで管理してください。Done の項目は必要に応じて日付/ログを追記します。

## 0. リポジトリ整備

- [x] 初期コミット & GitHub へ push（main）
- [x] .gitignore 追加（bin/obj, local.settings.json, .azurite, performance-reports 等）
- [x] NuGet.config 追加（nuget.org のみ）
- [x] ビルド修正（Functions 拡張, Graph Delta, 文字列スライス）→ ローカル build PASS
- [x] パフォーマンステストスイート実装（PerformanceTestSuiteFunction）
- [x] パフォーマンスレポート生成機能実装（PerformanceReportFunction）
- [x] 116室対応の大量テスト実行スクリプト作成（run-performance-tests.sh）

## 1. ローカル実行セットアップ

- [ ] Azure Functions Core Tools v4 を導入（macOS）
- [ ] Azurite を導入・起動（別ターミナル常時）
- [ ] ngrok などで 7071 を公開（https URL を取得）

## 2. 設定投入（FunctionApp/local.settings.json）

- [ ] Graph__TenantId / Graph__ClientId / Graph__ClientSecret を設定（アプリ許可に管理者同意済み）
- [ ] Webhook__BaseUrl に公開 https URL を設定（ngrok or Azure Functions）
- [ ] Webhook__ClientState をランダム 128 文字で設定（KV 推奨）
- [ ] Rooms__Upns に対象会議室 UPN を設定（例: ConfRoom1..ConfRoom10）
- [ ] Window__DaysPast=1 / Window__DaysFuture=7 を確認
- [ ] Blob* のコンテナ名・接続先を確認（Azurite: UseDevelopmentStorage=true）

## 3. 関数起動と購読作成

- [ ] `func start` でローカル起動（7071）
- [ ] `POST /api/graph/subscribe` を実行（Function 権限が必要な場合は code 付与）
- [ ] Blob state に `sub/<room>.json` が作成されることを確認

## 4. エンドツーエンド検証（通知→Queue→Delta→Blob）

- [ ] Outlook/ResourceLook で会議室の予定を「作成/更新/削除」
- [ ] 本文に `VisitorID: <GUID>`（全/半角コロン許容）を含める
- [ ] NotificationsFunction が 202 で応答し、Queue に投入される
- [ ] DeltaWorkerFunction が /calendarView/delta を実行し、cache に JSON を作成
- [ ] `cache/<roomUpn>/<eventId>.json` に visitorId が格納されている

## 5. 回帰/エッジケース

- [ ] クライアントステート不一致は破棄される（ログで Warning を確認）
- [ ] 2 回目以降は deltaLink で差分のみ取得されること（state の .delta ファイル更新）
- [ ] 終日予定（All-day）での時刻/タイムゾーンの扱いを確認
- [ ] 繰り返し予定/キャンセル（IsCancelled）を確認
- [ ] HTML 本文でも Prefer ヘッダーで text 化されることを確認

## 6. サブスク運用（Timer）

- [ ] 一時的に `Renew__Cron` を短周期にして動作確認（<24h で更新）
- [ ] 元の 6 時間周期に戻す

## 7. スケール検証（PoC 範囲）

- [x] パフォーマンステストスイート実装（API: /api/performance/test-suite）
- [x] 116室対応の大量テスト機能（CreateBulkEventsFunction拡張活用）
- [x] エンドツーエンドレスポンス時間測定（10秒以内要件チェック）
- [x] レポート生成機能（HTML/Markdown/JSON形式）
- [x] 実行スクリプト作成（./scripts/run-performance-tests.sh）
- [ ] 実際の116室での実行テスト
- [ ] クライアント向けレポート提出

## 8. 監視・可観測性（任意/本番想定）

- [ ] Application Insights 追加（依存/要求/トレース）
- [ ] P50/P95（通知遅延・Delta 処理時間・429 率）をダッシュボード化

## 9. セキュリティ/運用

- [ ] local.settings.json の機密を Git に含めない（現行 .gitignore 済）
- [ ] `Webhook__ClientState`/Client Secret を Key Vault 管理（本番）
- [ ] アプリ許可の最小化（会議室スコープの絞り込み検討）
- [ ] シークレットローテーション手順を整備

## 10. 本番化に向けた差し替え

- [ ] Lifecycle 通知エンドポイント実装（reauthorizationRequired/missed/removed）
- [ ] `IEventCacheStore` の SQL 実装（ResourceLook: Rooms/ScheduleCache/Visitor 連携を設計）
- [ ] リトライ/バックオフ/一時的失敗のハンドリング強化

## 11. CI/CD（任意）

- [ ] GitHub Actions で build/テスト
- [ ] Azure へのデプロイ（ステージング/本番）
- [ ] IaC（Bicep/Terraform）雛形の導入

## 12. ドキュメント/ナレッジ

- [ ] README.ja の「ローカル実行」「購読作成」「トラブルシュート」章を充実
- [ ] 既知の制約（Graph Delta の不許可クエリなど）を明記

---
参考メモ:

- 実運用では公開 HTTPS と 10 秒以内応答が前提（Queue 連携で担保済み）
- Delta の初回は期間指定（-1〜+7日）、以降は deltaLink を保存・再利用
- 本文の VisitorID 抽出は全/半角コロン・空白に強い正規表現を使用
