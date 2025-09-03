# 概要

こちらでは **Graph APIアーキテクチャ案（変更通知＋Delta）** と、会議室300までを見据えた要件（**昨日＋1週間＝8日間**のキャッシュ、**UIはなる早≒P95≦10秒目安**、本文先頭に **VisitorID** を埋め込む）を前提に、**PoC用の最小アーキテクチャとサンプル実装**をまとめました。すぐ動かせる **Azure Functions(.NET 8, Isolated)** のプロジェクト雛形を用意しています。

---

## PoCアーキテクチャ（最小構成）

**目的**：UIは常にDB/キャッシュを参照→**通知着弾→差分取り込み**で“なる早”反映。本文中の **VisitorID** を抽出し、会議室と予定にひもづけて保持。
**期間**：**昨日（-1日）〜＋7日** をキャッシュ。
**対象**：最大300会議室を想定（先行PoCは10室程度から）

**構成（PoCで簡素化）**

* **Azure Functions（HTTP）**

  * `NotificationsFunction`：Graph **URL検証**（GET, `validationToken` エコー）／**通知受信**（POST、`clientState` 照合→Queueへ即投入、**202**応答）
* **Azure Queue**

  * `graph-notifications`：通知→差分実行の非同期化（P95応答≦10秒を守りやすく）
* **Azure Functions（Queue Trigger）**

  * `DeltaWorkerFunction`：**/calendarView/delta**（初回は期間指定、以降は`deltaLink`）で最小差分のみ取得
  * **ヘッダ**：`Prefer: outlook.body-content-type="text"`, `Prefer: outlook.timezone="Tokyo Standard Time"`
  * **VisitorID抽出**：`VisitorID\s*[:：]\s*([0-9a-fA-F\-]{36})`（全/半角コロン、前後空白や前置メッセージに強い）
* **State（PoCはBlob）**

  * `deltaLink`／`subscriptionId`／期限の永続化（本番ではSQLでもOK）
* **Cache（PoCはBlob JSON）**

  * 予定とVisitorIDのスナップショット格納（本番ではSQL Databaseへ）
* **Azure Functions（Timer）**

  * `RenewSubscriptionsFunction`：**期限前更新**（Outlook系は最大**7日**。6日で更新）
* **Azure Functions（HTTP）**

  * `SubscribeRoomsFunction`：**/users/{room}/events** に **created,updated,deleted** の通知サブスクを一括作成（room UPNカンマ区切り）

> これらはスライドの「A案：通知＋差分」「PoC計画」「運用（更新・再認可）」に対応しています。&#x20;

---

## 同梱コードの見どころ

> プロジェクト：`src/FunctionApp`（.NET 8, Functions v4/Isolated）

### 1) 通知受信（URL検証/通知→Queue）

**`Functions/NotificationsFunction.cs`**

* **GET**：`validationToken` を **text/plain** で即返却（URL検証）
* **POST**：`clientState` を**完全一致**で検証→**Queue**へ投入（**202**で即応答）
* **resource** から **room UPN** をパースしてメッセージ化
* 設定：`Webhook__ClientState`（128文字以内）, `Webhook__NotificationQueue`

### 2) 差分取り込み（Delta）

**`Functions/DeltaWorkerFunction.cs`**

* 既に `deltaLink` があれば **WithUrl(deltaLink)**、無ければ \*\*期間指定（-1〜+7日）\*\*で初回Delta
* `Prefer` ヘッダで**本文テキスト化**／**TZ固定**
* ページング：`OdataNextLink` がある限りループ→終了時に `OdataDeltaLink` を**保存**
* **VisitorID抽出**：`Utils/VisitorIdExtractor.cs`（正規表現は全/半角コロン対応）
* **キャッシュ書き込み**：`Services/IEventCacheStore`（PoCはBlob実装、SQL差し替え容易）

> Deltaのクエリ制約（`$select/$filter/$orderby/$search`不可）を前提に、本文は軽量化せず取得しています。

### 3) サブスク作成/更新

**`Functions/SubscribeRoomsFunction.cs`**

* `Rooms__Upns` のカンマ区切りで **/users/{room}/events** へ**まとめて作成**
* `LifecycleNotificationUrl` も付与可能（PoCでは未実装エンドポイント、将来用）
* `Services/BlobStateStore` に `subscriptionId`／期限／`deltaLink` を保存

**`Functions/RenewSubscriptionsFunction.cs`**

* 6時間ごとに起動。**期限-24h** を下回ったサブスクのみ期限延長（**6日**先へ）

---

## 設定（`local.settings.json` 抜粋）

```json
{
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "Graph__TenantId": "<tenant-id>",
    "Graph__ClientId": "<client-id>",
    "Graph__ClientSecret": "<client-secret>",

    "Webhook__ClientState": "<random-128-chars>",
    "Webhook__NotificationQueue": "graph-notifications",
    "Webhook__BaseUrl": "https://<your-functions-app>.azurewebsites.net",

    "Rooms__Upns": "room1@contoso.com,room2@contoso.com",
    "Window__DaysPast": "1",
    "Window__DaysFuture": "7",

    "Blob__Connection": "UseDevelopmentStorage=true",
    "Blob__StateContainer": "state",
    "Blob__CacheContainer": "cache",

    "Renew__Cron": "0 0 */6 * * *"
  }
}
```

> **clientState** は**Key Vault**で安全管理を推奨。**公開HTTPS**、**10秒以内応答**といった受け口要件は、実装/運用上の要点としてスライドに記載済み。

---

## 使い方（PoCの流れ）

1. **関数アプリの起動**（ローカル：Azurite＋Core Tools推奨）
2. **購読作成**：`POST /api/graph/subscribe`（`Rooms__Upns` の会議室を対象に一括作成）
3. **Outlook操作**（アドインからの登録/更新/削除）→Graphが**通知**→Queue→**Delta**が実行
4. **キャッシュ確認**：`cache/<roomUpn>/<eventId>.json` にVisitorID含むスナップショットが生成
5. **期限更新**：Timerが**自動更新**（6日→+6日）

> UIはDB/キャッシュから**直近予定**を即時描画、通知→差分反映で“なる早”。この設計は提案スライドのA案と一致します。

---

## 実装ディテール（要件反映点）

* **VisitorIDの頑健抽出**：ユーザーが本文上下にメッセージを差し込んでも、テキスト全体から**最初の一致**を抽出（全/半角コロン対応）。
* **期間ウィンドウ**：**当日必須**＋**前後1〜2日**の現状から、理想の**昨日＋1週間**へ拡張可能（`Window__DaysPast/Future`）。
* **300室のスケール**：本PoCはQueueで非同期化とバックオフを前提にしており、将来は

  * **並列度制御**（Functionsの`WEBSITE_MAX_DYNAMIC_APPLICATION_SCALE_OUT`、アプリ側`SemaphoreSlim`等）
  * **\$batch** の導入（同じ部屋の**GET**はDeltaで十分のため、主に初回同期の負荷分散で検討）
    を追加すれば拡張しやすい構成です。

---

## 本番移行に向けた差し替えポイント（メモ）

* **Blob→SQL**：`IEventCacheStore` をSQL実装に差し替え（既存の**SQL Database**キャッシュ運用に寄せる）。
* **セキュリティ**：**RBAC for Applications** 等で“会議室のみ”へスコープを絞るのは**任意**（必要であれば適用）。
* **Lifecycle通知**：`lifecycleNotificationUrl` を有効化し、**reauthorizationRequired／missed／removed** 対応を追加。
* **監視**：App Insightsで **通知遅延／429率／Delta処理時間（P50/P95）** を可視化。
