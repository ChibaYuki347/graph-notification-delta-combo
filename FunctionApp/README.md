
# Graph Calendar PoC – Webhook + Delta (Azure Functions, .NET 8)

> Sample PoC architecture for 300 room mailboxes, **Webhook (change notifications) + Delta Query**.
> UI reads from cache; backend updates cache on notifications.
>
> **Window:** yesterday (1) + next 7 days (configurable). **Body:** `Prefer: outlook.body-content-type="text"`. **TZ:** `Tokyo Standard Time`.

## Components
- `NotificationsFunction` – HTTP GET (validationToken echo), POST (receive notifications, verify `clientState`, enqueue)
- `DeltaWorkerFunction` – Queue-trigger worker; runs `/calendarView/delta` (initial window or via `deltaLink`), extracts `VisitorID` by regex, upserts cache
- `SubscribeRoomsFunction` – HTTP Function to create subscriptions for comma-separated room UPNs
- `RenewSubscriptionsFunction` – Timer to renew subscriptions before expiration
- `BlobStateStore` – stores `subscription` and `deltaLink` per room
- `BlobEventCacheStore` – stores cached events as JSON (PoC). Replace with SQL in production.

## Quick start
1. `func start` (AzFunc Core Tools) after adding **app settings** in `local.settings.json`
2. `POST /api/graph/subscribe` to create subscriptions for rooms in `Rooms__Upns`
3. On change, notifications will flow into the queue; worker will delta-sync and update blob cache

## Settings
See `local.settings.json`. Typical values:
- `Webhook__ClientState`: random high-entropy secret (≤128 chars)
- `Rooms__Upns`: `room1@contoso.com,room2@contoso.com`
- `Window__DaysPast`: `1` ; `Window__DaysFuture`: `7`
- `Blob__Connection`: `UseDevelopmentStorage=true` for Azurite
- `Renew__Cron`: `0 0 */6 * * *`

## Notes
- Delta on `calendarView` **does not support** `$select/$filter/$orderby/$search` by design.
- Prefer headers used:
  - `Prefer: outlook.body-content-type="text"`
  - `Prefer: outlook.timezone="Tokyo Standard Time"`
- Regex for VisitorID (half/full-width colon supported):
  `VisitorID\s*[:：]\s*([0-9a-fA-F\-]{36})`

## Replace Blob with SQL
Implement `IEventCacheStore` using your SQL schema (room/event keyed cache).
