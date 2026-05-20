# Lịch sử làm việc

## Phiên 2026-05-19

### 1. Khởi động server
- Chạy server bằng `dotnet run --launch-profile http`
- Server lắng nghe tại `http://localhost:5039`
- GameLoopService khởi động thành công, 0 active matches

### 2. Kiểm tra log flow
Xác nhận các bước flow hoạt động bình thường:
- `[GameHub] FindGame called` ✅
- `[GameHub] Starting match` ✅
- `Match created — Team0: 1` ✅
- `[SignalR] NotifyMatchStarted — adding ...` ✅
- Không có `SendInput DROPPED` ✅
- Không có `deviceId not found` ✅

### 3. Thêm log debug cho SendInput
**Vấn đề:** `SendInput` success path không có log → không biết client có gửi input đến server không.

**Sửa:** Thêm `LogDebug` vào `GameHub.SendInput` (success path):
```csharp
_logger.LogDebug("[GameHub] SendInput OK — matchId={MatchId} tick={Tick}", matchId, input.Tick);
```

**File:** `src/Features/Network/GameHub.cs`

### 4. Bật Debug log level cho GameHub
**Vấn đề:** `LogDebug` không hiện mặc định vì level mặc định là `Information`.

**Sửa:** Thêm vào `appsettings.Development.json`:
```json
"BackEndGame.Features.Network.GameHub": "Debug"
```

### 5. Phát hiện tick=0 trên server
Sau khi restart, log hiện `SendInput OK` liên tục nhưng `tick=0` mãi không tăng, dù client xác nhận tick đang tăng.

### 6. Sửa lỗi deserialization InputPacket
**Nguyên nhân:** `InputPacket` dùng **public fields** thay vì **properties**. `System.Text.Json` mặc định không serialize/deserialize fields đúng cách.

**Sửa 1:** Bật `IncludeFields` trong `Program.cs`:
```csharp
builder.Services.AddSignalR()
    .AddJsonProtocol(options => options.PayloadSerializerOptions.IncludeFields = true);
```

**Sửa 2:** Chuyển `InputPacket` và `GameStatePacket` từ struct fields sang class properties:

`src/Domain/Packets/InputPacket.cs` — đổi `struct` → `class`, fields → properties với `{ get; set; }`

`src/Domain/Packets/GameStatePacket.cs` — đổi `struct` → `class`, fields → properties với `{ get; set; }`

### 7. Giải thích tick client vs tick server
- `InputPacket.Tick` = tick counter của client, dùng cho reconciliation
- `GameStatePacket.Tick` = tick counter của server (`_tick++` mỗi 15ms trong `Match.Tick()`)
- Hai tick độc lập nhau, không cần bằng nhau

### Các file đã thay đổi
| File | Thay đổi |
|---|---|
| `src/Features/Network/GameHub.cs` | Thêm log debug SendInput OK |
| `src/Features/Network/SignalRNetworkService.cs` | (không đổi) |
| `src/Domain/Packets/InputPacket.cs` | struct fields → class properties |
| `src/Domain/Packets/GameStatePacket.cs` | struct fields → class properties |
| `src/Program.cs` | AddJsonProtocol IncludeFields = true |
| `appsettings.Development.json` | Bật Debug log cho GameHub |