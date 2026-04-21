# BackEndGame

## Database

- The API now uses PostgreSQL through Entity Framework Core.
- Configure the connection string in `appsettings.json` or `appsettings.Development.json` under `ConnectionStrings:DefaultConnection`.
- On startup, the app calls `Database.EnsureCreated()` so the initial schema is created automatically for local development.

## Realtime presence

- SignalR hub: `/hubs/presence?deviceId={deviceId}`
- When the connection succeeds, the server emits `connection:ready`.
- When the client disconnects, the server removes the connection from the in-memory tracker.
- Presence check API: `GET /api/realtime/presence?deviceId={deviceId}`

---

## SignalRNetworkService

### Class này làm gì?

Đây là lớp **triển khai cụ thể** của `INetworkService`, dùng **SignalR WebSocket** để giao tiếp giữa server và Unity client. Mọi thứ liên quan đến việc gửi/nhận packet trong game đều đi qua đây.

---

### Các biến lưu trạng thái

```csharp
private readonly IHubContext<GameHub> _hubContext;
```
> `IHubContext` cho phép gửi message đến client **từ bên ngoài Hub** (ví dụ từ MatchManager, GameLoopService). Nếu không có nó, chỉ có thể gửi message từ bên trong `GameHub`.

---

```csharp
private Action<Guid, InputPacket>? _inputHandler;
```
> Lưu một callback function. Khi Unity gửi input lên, callback này sẽ được gọi để chuyển packet vào đúng match.

---

```csharp
// deviceId ↔ connectionId
_deviceToConnection   // "player123" → "abc-connection-id"
_connectionToDevice   // "abc-connection-id" → "player123"
```
> Hai chiều tra cứu. Khi Unity kết nối vào `/hubs/game`, server lưu cặp này lại. Dùng để biết **connectionId của một player cụ thể** khi cần gửi message riêng cho họ.

---

```csharp
// matchId → tập hợp connectionId
_matchConnections     // match-A → { conn1, conn2, ..., conn10 }

// connectionId → matchId
_connectionToMatch    // conn1 → match-A
```
> Dùng để biết player đang ở match nào (khi disconnect), và để xóa player khỏi group khi match kết thúc.

---

### Các method

#### `BroadcastGameStateAsync` — Gửi game state mỗi tick
```csharp
_hubContext.Clients.Group("match:{matchId}").SendAsync("game:state", packet)
```
> Gửi `GameStatePacket` (vị trí, HP của tất cả 10 người) đến **toàn bộ group** của match đó. Được gọi **64 lần/giây** — đây là method chạy nhiều nhất trong toàn hệ thống.

---

#### `SendGameStateToPlayerAsync` — Gửi riêng cho 1 người
```csharp
_hubContext.Clients.Client(connectionId).SendAsync("game:state", packet)
```
> Dùng khi cần đồng bộ cho 1 player cụ thể (ví dụ: player vào trận muộn, cần sync trạng thái ngay lập tức).

---

#### `OnInputReceived` — Đăng ký callback nhận input
```csharp
_inputHandler = handler;
```
> Ai muốn nhận input từ Unity thì gọi method này để đăng ký. Hiện tại `MatchManager` dùng cơ chế này. Khi Unity gọi `SendInput`, `GameHub` sẽ gọi callback đã đăng ký.

---

#### `NotifyMatchFoundAsync` — Tín hiệu 1: Home → Loading screen
```csharp
SendAsync("match:found", { matchId, teamId })
```
> Gửi **riêng từng người** (chưa có group). Mỗi người nhận được `teamId` của mình (0 hoặc 1) để màn hình loading hiển thị đúng team. Dùng `Task.WhenAll` để gửi song song cho cả 10 người cùng lúc.

---

#### `NotifyMatchStartedAsync` — Tín hiệu 2: Loading → Game screen
```csharp
Groups.AddToGroupAsync(connectionId, "match:{matchId}")
SendAsync("match:started", matchId)
```
> Làm **2 việc cùng lúc** cho mỗi player:
> 1. Thêm vào SignalR group → từ đây mỗi tick `BroadcastGameStateAsync` sẽ tự động đến họ
> 2. Gửi `"match:started"` → Unity chuyển sang màn hình game

Lock `_matchConnectionsLock` ở đây để tránh race condition khi nhiều player join group cùng lúc.

---

#### `NotifyMatchEndedAsync` — Kết thúc match
```csharp
SendAsync("match:ended", "Team0Win")           // gửi kết quả
RemoveFromGroupAsync(connectionId, group)       // dọn dẹp group
```
> Gửi kết quả (`Team0Win` / `Team1Win` / `Timeout`) đến cả group, sau đó **xóa tất cả connectionId khỏi group** để dọn dẹp bộ nhớ SignalR.

---

#### `RegisterGameHubConnection` / `UnregisterGameHubConnection`
> Gọi từ `GameHub.OnConnectedAsync` và `OnDisconnectedAsync`. Duy trì bảng tra cứu deviceId ↔ connectionId.

---

#### `GetMatchIdForConnection`
> Tra cứu player đang ở match nào — dùng trong `GameHub.OnDisconnectedAsync` để biết match nào cần xử lý khi player thoát.

---

### Tóm tắt luồng dữ liệu

```
Unity connects    → RegisterGameHubConnection(deviceId, connectionId)
Match found       → NotifyMatchFoundAsync    → "match:found"   → Loading screen
Match starts      → NotifyMatchStartedAsync  → join group
                                             → "match:started" → Game screen
Every 15.6ms      → BroadcastGameStateAsync  → "game:state"    → render
Match ends        → NotifyMatchEndedAsync    → "match:ended"   → End screen
                                             → leave group
Unity disconnects → UnregisterGameHubConnection
```
