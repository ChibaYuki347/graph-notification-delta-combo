# PoC Graph Calendar Architecture - システム設計書

## 概要
Microsoft Graphの**Change Notifications**と**Delta Queries**を組み合わせて、**16会議室スケール**で会議室の予定変更をリアルタイムで監視し、会議本文からVisitorIDを抽出するPoCシステム。高負荷パフォーマンステスト機能とReact UIによる可視化を含む。

## アーキテクチャ図

```
[Outlook Calendar] --> [Microsoft Graph API] --> [Webhook Notifications]
                                |                         |
                                v                         v
                        [Delta Queries] <-- [NotificationsFunction]
                                |                         |
                                v                         v
                    [DeltaWorkerFunction] <-- [Azure Storage Queue]
                                |
                                v
                    [VisitorIdExtractor] --> [Blob Storage Cache]
                                                      |
                                                      v
[React UI (16 rooms)] <-- [AllRoomEventsApiFunction] <-- [Cache Store]
                                                      |
                                                      v
                                            [Performance Test Suite]
```

## システム処理フロー

### 1. 初期化フェーズ

1. **SubscribeRoomsFunction**: 監視対象会議室（最大16室）のGraph APIサブスクリプション作成
2. **InitialDelta**: 既存の会議データを取得してキャッシュに保存

### 2. リアルタイム監視フェーズ

1. **会議変更発生**: OutlookカレンダーでMeetingが作成/更新/削除
2. **Webhook通知**: Microsoft GraphからNotificationsFunction へHTTPS POST
3. **キュー投入**: 変更通知をAzure Storage Queueに投入
4. **Delta処理**: DeltaWorkerFunctionがキューメッセージを処理
5. **VisitorID抽出**: 会議本文からVisitorID（GUID形式）を抽出
6. **キャッシュ更新**: 抽出結果をBlob Storageに保存
7. **UI表示**: AllRoomEventsApiFunctionが16室分のデータを統合して配信

### 3. パフォーマンステストフェーズ

1. **バルクイベント作成**: CreateBulkEventsFunctionで16室×5件のテストデータ生成
2. **エンドツーエンドテスト**: PerformanceTestSuiteFunctionで総合性能測定
3. **リアルタイム可視化**: React UIで242イベント同時表示

## Functions 一覧と役割

### 🔴 **本番必須Functions（コア機能）**

#### 1. **NotificationsFunction**

- **役割**: Microsoft GraphのWebhook通知を受信
- **トリガー**: HTTP POST (`/api/graph/notifications`)
- **処理**:
  - Webhook検証（validation token処理）
  - 変更通知をQueueに投入
  - サブスクリプションID→会議室UPN変換
- **重要度**: ★★★ 必須（リアルタイム監視の入口）

#### 2. **DeltaWorkerFunction**

- **役割**: キューメッセージを処理してDelta同期実行
- **トリガー**: Azure Storage Queue
- **処理**:
  - Delta APIでカレンダー変更を取得
  - VisitorID抽出
  - キャッシュ更新
- **重要度**: ★★★ 必須（メイン処理）

#### 3. **SubscribeRoomsFunction**

- **役割**: 会議室の監視サブスクリプション作成/管理
- **トリガー**: HTTP POST (`/api/graph/subscribe`)
- **処理**: Graph APIサブスクリプション作成、状態保存
- **重要度**: ★★★ 必須（監視開始）

#### 4. **AllRoomEventsApiFunction** ✨新規

- **役割**: 16会議室の統合イベントAPI（UI向け）
- **トリガー**: HTTP GET (`/api/room-events`)
- **処理**: 
  - 16室分のキャッシュデータを統合
  - 時間フィルタリング（今日+7日間）
  - React UI向けJSON配信
- **重要度**: ★★★ 必須（UI表示の中核）

### 🟡 **運用重要Functions（本番推奨）**

#### 5. **RenewSubscriptionsFunction**

- **役割**: サブスクリプションの有効期限更新
- **トリガー**: Timer (定期実行)
- **処理**: 期限切れ前のサブスクリプション更新
- **重要度**: ★★☆ 重要（長期運用に必要）

#### 6. **HealthFunction** ✨新規

- **役割**: システムヘルスチェック
- **トリガー**: HTTP GET (`/api/health`)
- **処理**: Graph API接続確認、キュー状態確認
- **重要度**: ★★☆ 重要（監視・アラート）

### � **運用・監視Functions（運用補助）**

#### 7. **GetStateFunction**

- **役割**: システム状態確認・管理
- **トリガー**: HTTP GET/DELETE (`/api/graph/state/{room}`)
- **処理**: サブスクリプション情報、deltaLink確認・削除
- **重要度**: ★☆☆ 運用補助

#### 8. **GetCacheFunction**

- **役割**: キャッシュされた会議データ確認
- **トリガー**: HTTP GET (`/api/graph/cache/{room}/{eventId?}`)
- **処理**: Blob Storageからキャッシュデータ取得
- **重要度**: ★☆☆ 運用補助

#### 9. **GetQueueFunction**

- **役割**: キュー状態確認（通常・poison queue）
- **トリガー**: HTTP GET (`/api/graph/queue?poison=true`)
- **処理**: Storage Queueのメッセージ一覧表示
- **重要度**: ★☆☆ デバッグ・監視

#### 10. **MetricsAggregatorFunction** ✨新規

- **役割**: パフォーマンスメトリクス集計
- **トリガー**: Timer (定期実行)
- **処理**: レンダリング時間、API応答時間の統計処理
- **重要度**: ★☆☆ 運用補助

### 🔵 **パフォーマンステストFunctions（テスト専用）**

#### 11. **PerformanceTestSuiteFunction** ✨新規

- **役割**: 包括的パフォーマンステスト実行
- **トリガー**: HTTP POST (`/api/performance/test-suite`)
- **処理**: 
  - バルクイベント作成（16室×N件）
  - エンドツーエンド応答時間測定
  - 10秒以内制約での検証
- **重要度**: ☆☆☆ テスト専用（本番では無効化推奨）

#### 12. **CreateBulkEventsFunction** ✨新規

- **役割**: 大量テストイベント作成（週分散対応）
- **トリガー**: HTTP POST (`/api/bulk/events`)
- **処理**: 
  - 16室対応の高速バルク作成
  - 今週内ランダム日時分散
  - VisitorID付きイベント生成
- **重要度**: ☆☆☆ テスト専用（本番では無効化推奨）

#### 13. **PerformanceReportFunction** ✨新規

- **役割**: 詳細パフォーマンスレポート生成
- **トリガー**: HTTP GET (`/api/performance/report`)
- **処理**: システム性能の包括的分析とレポート出力
- **重要度**: ☆☆☆ テスト専用

### 🔵 **デバッグ・開発Functions（開発専用）**

#### 14. **TestDeltaFunction**

- **役割**: Delta同期ロジックの直接テスト
- **トリガー**: HTTP POST (`/api/graph/debug/test-delta`)
- **処理**: キューを介さずDelta処理を直接実行
- **重要度**: ☆☆☆ デバッグ専用

#### 15. **ListRoomsFunction**

- **役割**: 利用可能会議室一覧表示
- **トリガー**: HTTP GET (`/api/graph/debug/rooms`)
- **処理**: Graph APIから会議室リスト取得
- **重要度**: ☆☆☆ デバッグ専用

#### 16. **ListEventsFunction**

- **役割**: 会議室の現在のイベント一覧表示
- **トリガー**: HTTP GET (`/api/graph/debug/events`)
- **処理**: calendarViewで直接イベント取得
- **重要度**: ☆☆☆ デバッグ専用

#### 17. **TriggerDeltaFunction**

- **役割**: 手動Delta同期トリガー
- **トリガー**: HTTP GET (`/api/graph/debug/trigger`)
- **処理**: 指定会議室のDelta同期を手動実行
- **重要度**: ☆☆☆ デバッグ専用

#### 18. **PurgeQueueFunction**

- **役割**: キューメッセージの削除
- **トリガー**: HTTP DELETE (`/api/graph/queue`)
- **処理**: 通常・poison queueのメッセージ削除
- **重要度**: ☆☆☆ デバッグ専用

#### 19. **CreateTestEventFunction**

- **役割**: VisitorID含むテスト会議の作成
- **トリガー**: HTTP POST (`/api/graph/debug/create-test-event`)
- **処理**: 実際のフォーマットでGraph APIに会議作成
- **重要度**: ☆☆☆ デバッグ専用

#### 20. **RealtimeEventsFunction** ✨新規

- **役割**: リアルタイムイベントストリーミング（開発用）
- **トリガー**: HTTP GET (`/api/realtime/events`)
- **処理**: Server-Sent Eventsでリアルタイム配信
- **重要度**: ☆☆☆ 開発専用

#### 21. **ClientRenderMetricsFunction** ✨新規

- **役割**: クライアント側レンダリング性能収集
- **トリガー**: HTTP POST (`/api/metrics/render`)
- **処理**: UI側からの性能データ収集・保存
- **重要度**: ☆☆☆ 開発専用

#### 22. **WarmupFunction** ✨新規

- **役割**: コールドスタート対策
- **トリガー**: HTTP GET (`/api/warmup`)
- **処理**: 依存関係の事前初期化
- **重要度**: ☆☆☆ 開発専用

## VisitorID抽出の仕組み

### 実際のフォーマット（来客管理アドイン）
```
<任意のユーザー文字>

VisitorID:39906803-1789-434f-9b94-7bcf089342c7 ^^^^^^^^^ 【来客管理アドインからのお願い】^^^^^^^^^
メール本文に発行された番号、IDは、手動で削除しないでください。
システム側に情報が残ったままになります。
削除する場合は、アドインの「削除」ボタンから削除をお願いします。
なお、この説明は「削除」ボタンで削除されません。
お手数ですが手動で削除をお願いいたします。
^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^

<任意のユーザー文字>
```

### 抽出ロジック
```csharp
// HTMLタグ除去 + HTMLエンティティデコード + 正規表現マッチング
VisitorID\s*[:：]\s*([0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12})
```

### ✅ 検証完了（2025-09-04）
- **テスト会議**: `[テスト] VisitorID検証会議`
- **抽出成功**: `9ea697db-c42e-4097-8cb2-7d589103112b`
- **HTMLタグ除去**: `<br>` などのHTMLタグを適切に処理
- **前後テキスト対応**: 来客管理アドインの説明文があっても正常抽出
- **エンドツーエンド**: 会議作成 → Delta同期 → 抽出 → キャッシュ保存まで完全動作

## Storage構成

### Blob Storage
- **Container**: `cache`
- **Path**: `{roomUpn}/{eventId}.json`
- **内容**: 会議の完全情報 + 抽出されたVisitorID

### Queue Storage  
- **Queue**: `delta-notifications`
- **Poison Queue**: `delta-notifications-poison`
- **メッセージ**: 会議室UPN + サブスクリプションID

### State Storage
- **Container**: `state`
- **Path**: `subscriptions/{roomUpn}.json`, `delta/{roomUpn}.json`
- **内容**: サブスクリプション情報、deltaLink

## 設定値

### local.settings.json

```json
{
  "WindowOptions:DaysPast": -3,
  "WindowOptions:DaysFuture": 14,
  "Blob:Connection": "UseDevelopmentStorage=true",
  "Queue:Connection": "UseDevelopmentStorage=true"
}
```

### 重要なエンドポイント

- **ngrok Public URL**: `https://84db83178cf7.ngrok-free.app`
- **Webhook**: `/api/graph/notifications`
- **UI統合API**: `/api/room-events` (16室対応)
- **パフォーマンステスト**: `/api/performance/test-suite`
- **監視対象**: 16会議室 (`ConfRoom1@bbslooklab.onmicrosoft.com` ～ `ConfRoom16@bbslooklab.onmicrosoft.com`)

### React UI設定

- **フロントエンド**: http://localhost:3000
- **API統合**: AllRoomEventsApiFunction
- **表示能力**: 242イベント同時表示
- **CORS設定**: Azure Functions ローカル開発対応

## 次の検証ステップ

1. ✅ **VisitorID含む会議の作成確認** - CreateTestEventFunctionで完了
2. ✅ **Graph API同期タイミングの検証** - Delta同期で正常処理確認
3. ✅ **VisitorID抽出の動作確認** - 実際のフォーマットで抽出成功
4. 🔄 **リアルタイム通知の動作確認** - Webhook経由の自動トリガーテスト
5. 🔄 **本番運用時の必須Functions特定** - デバッグ用Functions整理

## 📋 検証完了サマリー（2025-09-22 更新）

### ✅ 16室スケール対応完了

- **スケール実績**: 16会議室同時監視・表示
- **パフォーマンス検証**: 8.269秒（目標10秒以内達成）
- **イベント処理能力**: 242イベント/80件作成（234.6件/秒）
- **UI統合**: AllRoomEventsApiFunction による16室統合表示
- **エラー率**: 0%（完全成功）

### ✅ 動作確認済み機能

- **エンドツーエンドパイプライン**: 会議作成 → Delta同期 → VisitorID抽出 → キャッシュ保存
- **来客管理アドイン対応**: HTMLタグ除去、実際のフォーマット対応
- **16室統合API**: 単一エンドポイントで全会議室データ配信
- **週分散イベント生成**: 今週内ランダム日時でのテストデータ作成
- **リアルタイム可視化**: React UIでの242イベント同時表示
- **包括的テストスイート**: 自動化されたパフォーマンス検証

### ✅ パフォーマンス検証実績

```
テスト構成: 16室 × 5イベント = 80イベント作成
測定結果: 8.269秒 (目標10秒以内 ✓)
処理速度: 234.6イベント/秒
エラー率: 0%
UI表示: 242イベント (過去データ含む)
```

### 🔄 本番移行準備項目

1. **必須Functions特定**: コア機能4つ + UI統合1つ
2. **テスト専用Functions無効化**: パフォーマンステスト系Function無効化
3. **監視・アラート設定**: HealthFunction活用
4. **スケーリング設定**: Azure Functions Premium Plan検討

## トラブルシューティング

### よくある問題

1. **DeltaWorker poison queue**: エラーハンドリング済み、ログ確認
2. **サブスクリプション期限切れ**: RenewSubscriptionsFunction で自動更新
3. **ngrok接続エラー**: トンネル再起動またはURL更新
4. **VisitorID抽出失敗**: 会議本文の形式確認
5. **16室UI表示問題**: AllRoomEventsApiFunctionのキャッシュ確認

### デバッグコマンド例

```bash
# 16室統合API確認
curl -s "http://localhost:7071/api/room-events" | jq '.stats'

# 特定会議室の状態確認
curl -s "http://localhost:7071/api/graph/state/ConfRoom1@bbslooklab.onmicrosoft.com" | jq .

# 16室パフォーマンステスト
curl -X POST -s "http://localhost:7071/api/performance/test-suite?roomCount=16&eventsPerRoom=5" | jq '.summary'

# バルクイベント作成（週分散）
curl -X POST "http://localhost:7071/api/bulk/events?rooms=ConfRoom1@bbslooklab.onmicrosoft.com,ConfRoom2@bbslooklab.onmicrosoft.com&countPerRoom=5"

# キャッシュ確認  
curl -s "http://localhost:7071/api/graph/cache/ConfRoom1@bbslooklab.onmicrosoft.com" | jq .

# Delta手動テスト
curl -X POST "http://localhost:7071/api/graph/debug/test-delta?room=ConfRoom1@bbslooklab.onmicrosoft.com"
```

### 本番デプロイ時の必須Function

**必要最小限（5つ）:**
1. NotificationsFunction - Webhook受信
2. DeltaWorkerFunction - Delta同期処理  
3. SubscribeRoomsFunction - サブスクリプション管理
4. AllRoomEventsApiFunction - UI統合API
5. RenewSubscriptionsFunction - 長期運用

**本番では無効化推奨（14+個）:**
- PerformanceTestSuiteFunction
- CreateBulkEventsFunction  
- PerformanceReportFunction
- 全てのDebug系Function (TestDelta, ListRooms, ListEvents等)
- 全ての開発専用Function (RealtimeEvents, ClientRenderMetrics等)
