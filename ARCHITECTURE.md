# PoC Graph Calendar Architecture - システム設計書

## 概要
Microsoft Graphの**Change Notifications**と**Delta Queries**を組み合わせて、会議室の予定変更をリアルタイムで監視し、会議本文からVisitorIDを抽出するPoCシステム。

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
```

## システム処理フロー

### 1. 初期化フェーズ
1. **SubscribeRoomsFunction**: 監視対象会議室のGraph APIサブスクリプション作成
2. **InitialDelta**: 既存の会議データを取得してキャッシュに保存

### 2. リアルタイム監視フェーズ
1. **会議変更発生**: OutlookカレンダーでMeetingが作成/更新/削除
2. **Webhook通知**: Microsoft GraphからNotificationsFunction へHTTPS POST
3. **キュー投入**: 変更通知をAzure Storage Queueに投入
4. **Delta処理**: DeltaWorkerFunctionがキューメッセージを処理
5. **VisitorID抽出**: 会議本文からVisitorID（GUID形式）を抽出
6. **キャッシュ更新**: 抽出結果をBlob Storageに保存

## Functions 一覧と役割

### 🔴 本番必須Functions（最低限必要）

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

#### 4. **RenewSubscriptionsFunction**
- **役割**: サブスクリプションの有効期限更新
- **トリガー**: Timer (定期実行)
- **処理**: 期限切れ前のサブスクリプション更新
- **重要度**: ★★☆ 重要（長期運用に必要）

### 🟡 運用・監視Functions

#### 5. **GetStateFunction**
- **役割**: システム状態確認・管理
- **トリガー**: HTTP GET/DELETE (`/api/graph/state/{room}`)
- **処理**: サブスクリプション情報、deltaLink確認・削除
- **重要度**: ★☆☆ 運用補助

#### 6. **GetCacheFunction**
- **役割**: キャッシュされた会議データ確認
- **トリガー**: HTTP GET (`/api/graph/cache/{room}/{eventId?}`)
- **処理**: Blob Storageからキャッシュデータ取得
- **重要度**: ★☆☆ 運用補助

#### 7. **GetQueueFunction**
- **役割**: キュー状態確認（通常・poison queue）
- **トリガー**: HTTP GET (`/api/graph/queue?poison=true`)
- **処理**: Storage Queueのメッセージ一覧表示
- **重要度**: ★☆☆ デバッグ・監視

### 🔵 デバッグ・テストFunctions

#### 8. **TestDeltaFunction** 
- **役割**: Delta同期ロジックの直接テスト
- **トリガー**: HTTP POST (`/api/graph/debug/test-delta`)
- **処理**: キューを介さずDelta処理を直接実行
- **重要度**: ☆☆☆ デバッグ専用

#### 9. **ListRoomsFunction**
- **役割**: 利用可能会議室一覧表示
- **トリガー**: HTTP GET (`/api/graph/debug/rooms`)
- **処理**: Graph APIから会議室リスト取得
- **重要度**: ☆☆☆ デバッグ専用

#### 10. **ListEventsFunction**
- **役割**: 会議室の現在のイベント一覧表示  
- **トリガー**: HTTP GET (`/api/graph/debug/events`)
- **処理**: calendarViewで直接イベント取得
- **重要度**: ☆☆☆ デバッグ専用

#### 11. **TriggerDeltaFunction**
- **役割**: 手動Delta同期トリガー
- **トリガー**: HTTP GET (`/api/graph/debug/trigger`)
- **処理**: 指定会議室のDelta同期を手動実行
- **重要度**: ☆☆☆ デバッグ専用

#### 12. **PurgeQueueFunction**
- **役割**: キューメッセージの削除
- **トリガー**: HTTP DELETE (`/api/graph/queue`)
- **処理**: 通常・poison queueのメッセージ削除
- **重要度**: ☆☆☆ デバッグ専用

#### 13. **CreateTestEventFunction** 
- **役割**: VisitorID含むテスト会議の作成
- **トリガー**: HTTP POST (`/api/graph/debug/create-test-event`)
- **処理**: 実際のフォーマットでGraph APIに会議作成
- **重要度**: ☆☆☆ デバッグ専用

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
- **監視対象**: `ConfRoom1@bbslooklab.onmicrosoft.com`

## 次の検証ステップ

1. ✅ **VisitorID含む会議の作成確認** - CreateTestEventFunctionで完了
2. ✅ **Graph API同期タイミングの検証** - Delta同期で正常処理確認
3. ✅ **VisitorID抽出の動作確認** - 実際のフォーマットで抽出成功
4. 🔄 **リアルタイム通知の動作確認** - Webhook経由の自動トリガーテスト
5. 🔄 **本番運用時の必須Functions特定** - デバッグ用Functions整理

## 📋 検証完了サマリー（2025-09-04）

### ✅ 動作確認済み機能
- **エンドツーエンドパイプライン**: 会議作成 → Delta同期 → VisitorID抽出 → キャッシュ保存
- **来客管理アドイン対応**: HTMLタグ除去、実際のフォーマット対応
- **デバッグシステム**: 包括的な監視・テスト機能群
- **サブスクリプション管理**: Graph API通知設定・更新

### 🔄 次のマイルストーン
- **Webhook自動トリガー**: リアルタイム通知処理の検証
- **本番運用準備**: 不要なデバッグFunctions削除計画

## トラブルシューティング

### よくある問題
1. **DeltaWorker poison queue**: エラーハンドリング済み、ログ確認
2. **サブスクリプション期限切れ**: RenewSubscriptionsFunction で自動更新
3. **ngrok接続エラー**: トンネル再起動またはURL更新
4. **VisitorID抽出失敗**: 会議本文の形式確認

### デバッグコマンド例
```bash
# システム状態確認
curl -s "http://localhost:7071/api/graph/state/ConfRoom1@bbslooklab.onmicrosoft.com" | jq .

# キャッシュ確認  
curl -s "http://localhost:7071/api/graph/cache/ConfRoom1@bbslooklab.onmicrosoft.com" | jq .

# Delta手動テスト
curl -X POST "http://localhost:7071/api/graph/debug/test-delta?room=ConfRoom1@bbslooklab.onmicrosoft.com"
```
