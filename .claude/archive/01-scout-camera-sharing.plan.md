---
name: "Scout — chia sẻ camera giữa nhiều user (1 record, nhiều người xem)"
overview: "Sau Phase 7B addon phục vụ nhiều user, hai user thêm cùng một URL RTSP tạo hai row `ScCamera` → `RtspIngestService` và `WebRtcRelayService` đều khoá theo `camera.Id` nên thành hai phiên RTSP độc lập tới cùng một camera. Plan này thay hướng 'dedupe theo URL' bằng **share**: camera chỉ có một record với một chủ giữ creds, thêm bảng `CameraShare` cấp quyền cho user khác; nhờ vậy `camera.Id` vẫn là key duy nhất của ingest/relay nên tài nguyên không nhân đôi, và câu hỏi 'hai bộ creds khác nhau trỏ cùng nguồn thì dùng bộ nào' biến mất. Ràng buộc lớn nhất phát hiện từ code: addon **không có đường nào biết user nào tồn tại** — user chốt 2026-09-16 đi hướng thêm RPC mới (`SearchUsers`) vào kênh addon. ✅ Phase 1–6 đã code + build xong cả hai repo (namorix: 2 RPC + glyph share + bump 4 package; scout: bảng share, cổng quyền, UI, thu hồi ngắt phiên live). ⏸️ DG-S2 (đổi tên `UserId` → `OwnerId`) hoãn có chủ ý. 🔜 DG-S5 (luồng xin share) và DG-S6 (cảnh báo trùng URL có nêu tên chủ) vẫn chưa chốt — chúng chỉ ảnh hưởng cảnh báo lúc thêm camera, không chặn tính năng share. **Nợ duy nhất: migration `AddCameraOwner` + `AddCameraShare` chưa apply, chưa test runtime.**"
todos: []
isProject: false
---

# Scout — chia sẻ camera giữa nhiều user

> **Tiến độ:** **DG-S1 ✅ chốt 2026-09-16 — đi hướng B (thêm RPC `SearchUsers`), đã code + build xong**, xem mục 8. · **Phase 1 ✅** (`CameraShare` + migration `AddCameraShare`) · **Phase 2 ✅** (cổng quyền + DTO tách) · **Phase 3 ✅** (2 RPC tra danh tính) · **Phase 4 ✅** (frontend, DG-S8 = A) · **Phase 5 ✅ (DG-S4 = A — thu hồi share ngắt phiên live ngay)** · **Phase 6 ✅ (docs + version, 2 repo)**. **Còn nợ duy nhất: apply migration `AddCameraOwner` + `AddCameraShare` (`make db-update`) và test runtime.** Toàn bộ mục "Điều tra được từ code" dưới đây là đọc thật, có file:line.
>
> **Quan hệ với việc đang treo:** Phase 7B (`ScCamera.UserId` + filter theo chủ) code + build xong nhưng migration `AddCameraOwner` **chưa apply** và chưa test runtime nhiều user. Plan này **phụ thuộc** 7B — share là bước kế tiếp, không thay thế nó.

---

## 1. Vấn đề

Sau 7B, mỗi row `ScCamera` có chủ riêng và mọi query lọc theo `UserId`. Nếu hai user cùng thêm một URL RTSP:

| Tầng | Hành vi hiện tại | Nguồn |
|------|------------------|-------|
| DB | 2 row `Cameras` độc lập, cùng `RtspUrl` | `Models/ScCamera.cs` |
| Ingest | 2 `CameraRtspClient` — `_clients` khoá theo **`camera.Id`**, không theo URL | `RtspIngestService.cs:22`, `:64`, `:99` |
| Relay | 2 phiên WebRTC — `CreateAsync(cameraId)` | `WebRtcRelayService.cs:21` |
| Trạng thái | 2 `GetStatus(cameraId)` đọc `_lastError` của **từng client** | `RtspIngestService.cs:34-42` |

Ba hệ quả:

1. **Nhân đôi phiên tới camera.** Camera IP thường chỉ cho 4–6 main-stream đồng thời; 3 user add chung một cam là ăn 3 suất. Đây là rủi ro thật, không phải CPU.
2. **Trạng thái lệch nhau.** Cùng một camera vật lý mà card của user A báo OFFLINE còn card của user B báo live — đúng về code, sai về cảm nhận.
3. **Không có gì báo trùng.** UI không nói camera này đã tồn tại, nên user B không biết mình vừa tạo phiên thứ hai.

**Hướng chốt: share, không phải dedupe theo URL.** Hai lý do:

- Dedupe bị chặn bởi một câu hỏi không trả lời được: hai row cùng URL nhưng **creds khác nhau** thì client dùng bộ nào, và `Enabled = false` của A **không được** tắt luồng của B — refcount + chọn creds là phần khó, không phải cái dictionary.
- Share tự động giải bài toán tài nguyên mà **không cần thêm gì ở tầng stream**: chỉ còn một record nên `camera.Id` vẫn là key duy nhất của ingest lẫn relay. Không cần registry theo `host:port:path`.

---

## 2. Điều tra được từ code (ràng buộc thật)

| # | Phát hiện | Nguồn |
|---|-----------|-------|
| C1 | **DTO đang rò dữ liệu chủ.** `ScCameraDto` trả `RtspUrl`, `Username`, `HasCredentials` → share mà giữ nguyên DTO là đưa URL + username RTSP của chủ cho người được share | `Dtos/CameraDtos.cs:3-17` |
| C2 | **Cổng quyền chỉ nằm một chỗ.** `Offer` gọi `cameras.GetAsync(cameraId, CurrentUserId)`; `answer`/`ice`/`stop` không kiểm gì (dựa vào `sessionId` là Guid ngẫu nhiên) | `Controllers/StreamsController.cs:24`, `:42-90` |
| C3 | **Chỉ 2 file chạm `db.Cameras`.** Mọi thay đổi query đọng lại ở `CameraService` (6 chỗ) + một chỗ ở ingest | `Services/CameraService.cs:24,33,49,59,71,74`; `RtspIngestService.cs:54` |
| C4 | **Ingest không lọc theo user** (chỉ `.Where(c => c.Enabled)`) → share không ảnh hưởng ingest | `RtspIngestService.cs:54-57` |
| C5 | **Addon không biết user nào tồn tại** (trước batch này). Kênh addon chỉ có 5 RPC; danh tính duy nhất nhận được là `user_id` (int64). Desktop **không có** endpoint liệt kê/tìm user nào (`UserController` chỉ profile/password/settings, `UserService` chỉ UpdateProfile/ChangePassword, mọi chỗ đọc `appDbContext.Users` đều là existence check) | `Namorix.Core/Protos/addon_channel.proto`; `Namorix.Server/Controllers/UserController.cs`; `Namorix.Server/Services/UserService.cs` |
| C6 | **Chưa có khái niệm role ngoài chủ.** `RequireAuth` chỉ kiểm `IsAuthenticated`; middleware set `NameIdentifier`/`Name`/`client_id`/`session_id` — không có claim vai trò nào để phân biệt "người xem" và "chủ" | `Namorix.Core/Middleware/AddonSessionMiddleware.cs` |
| C7 | **Frontend khoá theo `camera.id`** ở mọi tầng: `RtcStreamClient` nhận `cameraId`, `useLiveStreams` auto-start mọi `camera.enabled`, store `cameraSlice` phẳng | `streaming/RtcStreamClient.ts:71`, `hooks/useLiveStreams.ts:29`, `store/slices/cameraSlice.ts` |
| C8 | **`RecordEnabled`/`RetentionDays` là thuộc tính của row**, chưa có chỗ nào ghi hình thật (Phase 6 chưa làm) | `Models/ScCamera.cs:12-13` |

**Đọc C5 mà ra:** một tính năng share **không thể** hoàn chỉnh nếu không thêm đường tra cứu danh tính — addon chỉ có `user_id` trần, không tên, không email, nên "share cho ai?" không có câu trả lời. Đây là lý do DG-S1 là quyết định chặn, và nó **đã được chốt + code** (mục 8).

---

## 3. Model dữ liệu (đề xuất)

```
ScCamera
  Id            Guid
  OwnerId       int      ← đổi tên từ UserId (xem DG-S2); chỉ owner giữ creds
  Name, RtspUrl, RtspCredentials, StreamType, Enabled, RecordEnabled,
  RetentionDays, CreatedAt, LastUpdatedAt

CameraShare            (mới)
  CameraId      Guid     FK → Cameras.Id, cascade delete
  UserId        int      user được cấp quyền
  Permission    string   "view" | "manage"   (xem DG-S3)
  CreatedAt     DateTimeOffset
  ── unique index (CameraId, UserId)
```

Truy vấn "camera của tôi" đổi từ `c.UserId == userId` thành:

```csharp
c.OwnerId == userId || db.CameraShares.Any(s => s.CameraId == c.Id && s.UserId == userId)
```

Bảng chỉ có một chỗ đọc/ghi (`CameraService` — C3), nên bề mặt thay đổi nhỏ.

---

## 4. Câu hỏi cần chốt (DG)

| Mã | Câu hỏi | Lựa chọn | Trạng thái |
|----|---------|----------|------------|
| **DG-S1** | Owner chọn người để share **bằng cách nào**? (C5: addon không biết user nào tồn tại) | **(A)** Nhập tay `user_id` — không cần API mới, UX xấu, ship được ngay. **(B)** Thêm RPC `SearchUsers` vào kênh addon — SDK + desktop đều phải sửa, bump `Namorix.Core` + `Namorix.Server` MINOR khi phát hành. **(C)** Share bằng email rồi desktop phân giải — cũng là RPC mới | ✅ **chốt B (2026-09-16)** — đã code + build, xem mục 8 |
| **DG-S2** | Giữ tên `ScCamera.UserId` hay đổi thành `OwnerId`? | `UserId` sau khi có share là mập mờ (chủ hay người xem?). Đổi tên tốn 1 migration `RenameColumn` + ~10 call site | ⏸️ **hoãn có chủ ý ở Phase 1** — xem mục 9 |
| **DG-S3** | Permission mấy mức? | **(A)** `view` / `manage`. **(B)** Thêm chiều "live only" vs "xem cả playback/recording" — nhưng ghi hình **chưa tồn tại** (C8) nên nhánh này hiện không có gì để chặn | ✅ **Phase 1 làm theo (A)**: `CameraSharePermission { View, Manage }`, lưu dạng chuỗi |
| **DG-S4** | Owner revoke thì người đang xem có bị **ngắt phiên live ngay** không? | **(A)** Ngắt ngay: xoá share → đóng mọi `RtcViewerSession` của camera đó thuộc user bị revoke — nhưng `WebRtcRelayService` **không lưu userId trong session** (C2), nên phải thêm field vào `RtcViewerSession`. **(B)** Chờ phiên tự hết (peer close / 15s grace) — không phải sửa gì, nhưng người bị revoke vẫn xem được tới khi tắt tab | ✅ **A (2026-09-16)** — làm ở Phase 5 |
| **DG-S5** | Có cần luồng "**xin** được share" (user B add trùng URL → gửi yêu cầu cho chủ) không? | Nếu có thì cần thêm bảng `CameraShareRequest` + nơi hiển thị + thông báo. Nếu không thì chỉ cần **cảnh báo trùng URL** lúc add và dừng ở đó | 🔜 chưa chốt |
| **DG-S6** | Cảnh báo trùng URL có **nói tên chủ** không? | Nói tên ⇒ ai đoán được URL camera là biết camera tồn tại và thuộc về ai (rò thông tin). Không nói ⇒ an toàn hơn nhưng user B không biết cầu cứu ai | 🔜 chưa chốt |
| **DG-S7** | Người được share thấy trạng thái/`LastError` thật của camera không? | Có ⇒ lộ chi tiết hạ tầng của chủ (message lỗi RTSP có thể chứa host). Không ⇒ card của họ luôn "không rõ" | 🔜 chưa chốt |

**Chuẩn hoá URL cho cảnh báo trùng (nếu DG-S5 cần):** so khớp theo `(host, port, path)` sau khi hạ chữ thường host và bỏ port mặc định 554. Giới hạn phải nói trước: **cùng một camera qua IP và qua DDNS sẽ KHÔNG khớp**, và URL có query khác nhau cũng không — tức đây là lưới bắt ca rõ ràng, không phải bảo đảm.

---

## 5. Phase

### Phase 0 — Chốt DG-S1…DG-S7
Không code. DG-S1 chặn mọi thứ phía sau (không có nó thì UI share không có gì để chọn).

### Phase 1 — Model + migration ✅ **xong 2026-09-16**

| File | Thay đổi |
|------|----------|
| `Models/CameraShare.cs` (NEW) | `CameraId`, `UserId`, `Permission`, `CreatedAt` — khoá chính là `(CameraId, UserId)` chứ không phải surrogate + unique index: "mỗi user một grant mỗi camera" trở thành hình dạng bảng không thể vi phạm, thay vì luật phải nhớ |
| `Models/Enums.cs` | +`CameraSharePermission { View = 0, Manage = 1 }` |
| `Persistence/ScoutDbContext.cs` | +`DbSet<CameraShare>`; cấu hình composite PK, `Permission` lưu dạng chuỗi (theo đúng khuôn `StreamType`), FK → `Cameras` **cascade** |
| `Migrations/20260916083344_AddCameraShare.cs` + `.Designer.cs` (NEW) | `CreateTable CameraShares` + FK cascade; `Down` là `DropTable`. Sinh bằng `make db-migrate name=AddCameraShare` |
| `Migrations/ScoutDbContextModelSnapshot.cs` | cập nhật theo |

- **Không sửa `AddCameraOwner`** đã sinh, dù nó chưa apply — sửa migration đã có là bẫy cho instance khác đã apply.
- **Chưa đổi query**: `CameraService` vẫn lọc `UserId` như cũ, nên **hành vi không đổi một chút nào** và bảng mới hiện chưa ai đọc. Build 3 project: 0 error / 0 warning.
- **Chưa `make db-update`.** Khi apply phải apply **sau** `AddCameraOwner` (thứ tự migration là tuyến tính, `db-update` lo cả hai).
- **Chưa bump version** — chờ tới Phase 6, vì phase này không đổi hành vi nào.

### Phase 2 — Cổng quyền + DTO tách ✅ **code + build xong 2026-09-16**

| File | Thay đổi |
|------|----------|
| `Models/Enums.cs` | +`CameraAccess { Owner, Manage, View }` — không persist, chỉ là câu trả lời cho "người gọi chạm tới camera này bằng đường nào". `Owner` không phải một mức share, nó là *không có* share |
| `Services/CameraService.cs` | `ListAsync` lọc `UserId == userId OR CameraShares.Any(...)`; `GetAsync` chủ-hoặc-được-share; `UpdateAsync` chủ-hoặc-`manage`; `DeleteAsync` vẫn chủ |
| `Dtos/CameraDtos.cs` | `ScCameraDto.RtspUrl` → `string?`, +`Access`; +`CameraShareDto`, `CameraShareRequest` |
| `Controllers/CamerasController.cs` | +`GET/POST /api/cameras/{id}/shares`, +`DELETE /api/cameras/{id}/shares/{userId}` (chỉ chủ) |
| `Controllers/StreamsController.cs` | chỉ đổi comment — cổng `Offer` **không phải sửa code**, vì nó gọi `GetAsync` và `GetAsync` giờ đã trả về camera được share |

- **Không tách thành 2 record DTO.** Danh sách trả về trộn camera của mình và camera được share, nên 2 record sẽ buộc phía gọi phải gộp 2 hình dạng payload. Thay vào đó các field của chủ để `null`, và serializer (`WhenWritingNull`) **bỏ hẳn key** — payload của người xem không có `rtspUrl`/`username`/`hasCredentials`/`lastError`.
- **`LastError` bị giữ lại với người xem** (DG-S7): chuỗi lỗi RTSP thường chứa host. Chọn bỏ. Nếu cần chẩn đoán thì phải là thông báo đã lọc, không phải chuỗi thô.
- **`StreamType` bị xếp vào nhóm "connection", không phải "settings"**: đổi main↔sub là đổi luồng bị kéo về, cùng loại với đổi địa chỉ. Nên `manage` **không** được đổi `RtspUrl`/`Username`/`Password`/`StreamType`; gửi field đó bị 400 (`INVALID_CAMERA_INPUT`), không im lặng bỏ qua.
- **`manage` vẫn làm được**: `Name`, `Enabled`, `RecordEnabled`, `RetentionDays`. Đây là chỗ dễ sai — nếu để `manage` PUT nguyên form của chủ thì nó re-point được stream sang server khác.
- **Share là chuyện của chủ, không phải của `manage`**: `ListShares`/`AddShare`/`RemoveShare` đều đòi `Cameras.UserId == userId`. Người xem được camera không được quyết ai khác xem được nữa.
- **`AddShare` là upsert**: gửi lại cùng `(cameraId, userId)` với permission khác là đổi mức, không cần DELETE trước. `RemoveShare` idempotent — xoá share không tồn tại vẫn 200 nếu camera là của mình.
- **`ListAsync` gộp quyền vào 1 query** (project `Permission` nullable song song với camera) thay vì 2 query + `Contains` trên list có thể rỗng.

**Đã biết / còn nợ của phase này:**

- **Share trả `userId` trần, không có tên.** Addon không có bảng user. Chủ share xong chỉ thấy `UserId` — muốn hiện tên thì phải thêm RPC tra theo *danh sách id* (`SearchUsers` không tra được bằng id), việc của Phase 4.
- **Không validate `request.UserId` có tồn tại bên desktop không.** Share với id rác tạo ra row chết, không khớp ai, chủ xoá được. Muốn chặn thì cũng cần RPC tra theo id.
- **Frontend chưa biết `access`** — `types/camera.ts` còn khai `rtspUrl: string` (giờ có thể vắng), và form Edit của `manage` còn gửi `rtspUrl: ""` → 400. Đúng phạm vi Phase 4, nhưng **từ giờ tới Phase 4 thì UI share sẽ vỡ**.
- **`answer`/`ice`/`stop` vẫn không kiểm tra chủ phiên** (`relay.Find(sessionId)`) — như cũ, session id là GUID khó đoán. Blast radius to hơn sau phase này (nhiều user hơn), ghi lại để nhớ.

### Phase 3 — Đường tra cứu danh tính ✅ **code + build xong 2026-09-16** (DG-S1 = B)
Chi tiết mặt API ở mục 8. Việc còn lại của phase này:

- **Bump version** `Namorix.Core` MINOR + `Namorix.Server` MINOR — nhưng gộp với Phase 4 vì `GetUsers` cũng nằm trong proto. Thêm RPC là additive, không vỡ addon nào (chỉ có **một** file `.proto` và **một** class kế thừa `AddonChannelBase` trong repo này).
- **Rate limit / audit**: vẫn chưa có, nhưng **mức nghiêm trọng đổi hẳn** sau khi chốt DG-S8 = A: liệt kê danh bạ giờ là *tính năng*, không còn là lỗ hổng cần bịt. Cái còn thiếu chỉ là trần theo thời gian và dấu vết ai gọi gì — chấp nhận được vì caller vốn giữ machine token. Xem mục 6.
- **Chưa test runtime** — chưa có addon nào gọi RPC này (chỉ mới `dotnet build` sạch).

### Phase 4 — Frontend ✅ **code + build xong 2026-09-16** (DG-S8 = A: tra tên theo id)

| File | Thay đổi |
|------|----------|
| `types/camera.ts` | +`CameraAccess`, `CameraSharePermission`, `CameraShare`, `CameraShareRequest`; `Camera.rtspUrl` → `string \| null`, +`Camera.access`; nhóm `rtspUrl`/`username`/`password`/`streamType` trong `CameraUpsert` thành optional |
| `scoutApiRoutes.ts` | +`cameraShares(id)`, `cameraShareByUser(id, userId)` |
| `controllers/camera.controller.ts` | +`listShares`, `addShare`, `removeShare` |
| `CameraManageCard.tsx` | +`onShare`; badge mức truy cập khi không phải chủ; **ẩn hàng URL** khi `rtspUrl === null`; ẩn Edit khi `view`, ẩn Share/Delete khi không phải chủ |
| `CameraFormDialog.tsx` | `isOwner`; ẩn cả nhóm connection với `manage` và **không gửi** nhóm đó (gửi kèm là bị server từ chối, kể cả chuỗi rỗng); thay bằng `NmxInlineAlert` giải thích |
| `CameraInfoDialog.tsx` | hàng URL/username/credentials chỉ hiện với chủ (`shouldRender`/omit, **không** hiện "—" giả); nút Edit ẩn với `view` qua `confirmShouldRender` |
| `CameraShareDialog.tsx` (NEW) | danh sách người được share + thu hồi + thêm mới; `useEffect` khoá theo `camera.id` chứ không theo object camera |
| `views/live/CameraErrorCodes.ts` | +`INVALID_CAMERA_INPUT` |
| `i18n/locales/en.json` | +`access.*`, `list.share`, `share.*`, `form.connectionOwnerOnly`, `errors.invalidInput` |
| `CamerasView.scss` | +`.scout-share-list`, `.scout-share-row` |

- **Tầng stream không phải sửa** (C7): store và `useLiveStreams` khoá theo `camera.id`, không đụng `rtspUrl`. Camera được share tự xuất hiện ở tab Live.
- Không tách DTO người xem thành record riêng ở FE; chỉ một interface `Camera` với field nullable, khớp với payload thật.
- **`vi.json` là file rỗng `{}`** từ trước cả phase này — chỉ thêm key vào `en.json`, không tự ý dịch.
- **Chưa test UI trên browser** — chỉ `tsc` + `vite build` sạch (403 modules). Chưa `make db-update` nên chưa chạy được thật.

**DG-S8 = A — tra tên theo id, và picker *liệt kê* chứ không *tìm*.**

Chốt lại giữa chừng, qua 3 bước: bản đầu là picker **tìm** (search debounce, query ≥ 2 ký tự) → chuyển thành **liệt kê + lọc tại browser** (bỏ debounce) → chốt cuối là **`NmxSelect` thuần, không có ô nhập nào**. Yêu cầu thật là *thấy danh sách để chọn*, nên mọi cơ chế gõ chữ đều là thừa.

| File (namorix) | Thay đổi |
|------|----------|
| `Namorix.Core/Protos/addon_channel.proto` | +`rpc GetUsers` + 2 message; `SearchUsersRequest` +`offset`, `limit` trần `[1, 200]`; **bỏ luật query ≥ 2 ký tự**, query rỗng = liệt kê. Sửa luôn 2 comment cũ đã thành sai: comment `SearchUsers` ("query is required and bounded in length") và comment `GetUsers` ("no enumeration is possible here" — id tuần tự thì quét được) |
| `Namorix.Server/Services/UserService.cs` | +`GetByIdsAsync(ids)`; `SearchAsync` +`offset`, nhánh `needle.Length > 0` nên list không lọc không mang 3 phép `ToLower()` vô nghĩa vào SQL |
| `Namorix.Server/Services/Grpc/AddonChannelService.cs` | +`GetUsers` override (`Take(UserLookupMaxIds = 50)`); `SearchUsers` bỏ guard độ dài, kẹp `limit`/`offset`; `UserSearchMaxLimit` 25 → **200**, `UserSearchDefaultLimit` 10 → 50 |
| `Namorix.Core/Grpc/AddonChannelClient.cs` | +`GetUsersAsync(IEnumerable<int>)`; `SearchUsersAsync` +`offset` |
| scout `Controllers/UsersController.cs` (NEW) | `GET /api/users` — liệt kê, không tham số; xin 1 page 200 (desktop kẹp lại), desktop chết → 503 `DESKTOP_UNREACHABLE` |
| scout `Dtos/UserDtos.cs` (NEW) | `ScoutUserDto(UserId, Username, Name)` — **không có email** |
| scout `Dtos/CameraDtos.cs` | +`CameraShareDto` có `Username`/`Name` (nullable) |
| scout `Services/CameraService.cs` | +`ResolveNamesAsync` gọi `channel.GetUsersAsync`, **best-effort**: lỗi → warning + tên `null`, không làm vỡ danh sách share (grant là row của addon, sống ngoài việc desktop có đang chạy hay không) |
| scout `frontend/user.controller.ts` (NEW) | `list()` |
| scout `CameraShareDialog.tsx` | nạp danh sách **1 lần lúc mở dialog**; chọn người bằng `NmxSelect` với `label` = tên, `description` = `@username`. Không ô nhập, không filter, không request theo phím |
| scout `CamerasView.scss` | xoá `.scout-share-results*` (4 rule) — chết sau khi bỏ ô nhập |

**Ràng buộc của `NmxSelect` phải nhớ:** generic là `T extends string` (`NmxSelect.tsx:37`) nên `userId` number phải đi qua `String(...)` rồi `Number(...)` lại lúc gửi; và nó **không có type-ahead** (listbox floating-ui + `useListNavigation`, chỉ mũi tên + `loop`), khác native `<select>`. Danh sách vài chục người thì không sao, vài trăm thì mũi tên rất mệt — mà addon đang xin tới 200. Chưa gặp ca đó, ghi lại để nhớ.

**Mặt đã mở, ghi rõ để không tự lừa mình:** giờ **mọi addon đã cài đều lấy được toàn bộ danh bạ** (username + tên hiển thị) — kể cả qua `SearchUsers` với query rỗng lẫn qua `GetUsers` quét id. Trước đây việc này bị chặn **có chủ ý** (proto ghi rõ). Chấp nhận, vì caller vốn đã giữ machine token; nhưng rate limit / audit vẫn **không có** (xem mục 6).

**Hệ quả version:** scout gọi `GetUsersAsync` nên `addon.json` `minCoreVersion` (đang `0.70.0`) **phải nâng** ở Phase 6 — Core cũ không có RPC này. Đây là chỗ dễ sót vì nó không nằm trong `package.json`.

**Người đang gọi bị loại khỏi danh sách** — `UsersController` lọc `u.UserId != CurrentUserId` trước khi trả. Không cần thêm `ownerId` vào DTO: chỉ chủ mới mở được dialog share, nên "người đang gọi" chính là chủ. Chỗ này thuộc **addon**, không phải desktop: desktop vẫn trả lời trung thực "ai tồn tại", còn phán đoán "ai đáng đưa vào picker" là việc của nơi tiêu thụ.

### Phase 5 — Ngữ nghĩa revoke ✅ **code + build xong 2026-09-16** (DG-S4 = A: ngắt ngay)

| File | Thay đổi |
|------|----------|
| `Streaming/WebRtcRelayService.cs` | `RtcViewerSession` +`UserId` +`CameraId` (forward từ `CameraRtspClient`) — relay vốn chỉ định danh phiên bằng `sessionId` nên **không có cách nào** biết phiên nào của ai; `CreateAsync(cameraId, userId, ct)`; +`CloseViewerSessionsAsync(cameraId, userId)` đóng đúng những phiên khớp cặp đó |
| `Controllers/StreamsController.cs` | `Offer` truyền `CurrentUserId` vào `CreateAsync` |
| `Services/CameraService.cs` | +`WebRtcRelayService relay` (cả hai singleton → không vòng DI); `RemoveShareAsync` gọi `CloseViewerSessionsAsync` **sau** `SaveChangesAsync` |

- **Không đẩy tín hiệu xuống client.** Đóng phiên ở server là đủ: row share đã biến mất nên lần `offer` kế tiếp của browser (nó tự reconnect theo backoff) bị `GetAsync` trả `null` → 404. Kill switch nằm ở chỗ quyết định quyền, không phải ở tầng stream.
- **Hạ mức `manage` → `view` cố ý KHÔNG ngắt phiên:** `view` vẫn được phép xem live, ngắt lúc đó là sai. Chỉ `RemoveShareAsync` mới đóng phiên.
- **`RemoveShareAsync` idempotent vẫn giữ nguyên** (share không tồn tại → `return true` sớm, không gọi relay): không có grant thì không có phiên hợp lệ nào để ngắt.
- Chưa test runtime.

### Phase 6 — Docs + version ✅ **xong 2026-09-16**
- namorix MINOR: `Namorix.Core` `0.70.0 → 0.71.0`, `Namorix.Server` `0.87.0 → 0.88.0` (proto có thêm `SearchUsers` + `GetUsers`). Kèm `@namorix/ui` `0.54.0 → 0.55.0` + `@namorix/styles` `0.62.1 → 0.63.0` cho glyph `ic-share`. `frontend` không bump (không file `frontend/src/` nào đổi), `@namorix/core` không đổi.
- scout MINOR: `0.10.0 → 0.11.0`, và **bắt buộc** nâng `addon.json` `minCoreVersion` `0.70.0 → 0.71.0` + `minServerVersion` `0.87.0 → 0.88.0` — scout gọi `GetUsersAsync`, Core cũ không có.
- Cập nhật `progress.md` cả hai repo ✅, `FLOW.md` mục addon ✅, `backend/README.md` ✅ (mục OAuth — đoạn `SearchUsers`/`GetUsers`). Scout **không** có `backend/README.md` (repo không có README nào), nên phần docs của scout nằm ở `.claude/memory/` — mà thư mục đó **bị `.gitignore`** (`memory/`), tức docs của scout không vào commit.

---

## 6. Rủi ro

| Rủi ro | Vì sao | Xử lý |
|--------|--------|-------|
| **Rò creds qua DTO** | `ScCameraDto` đang mang `RtspUrl` + `Username` (C1); share mà không tách DTO là mặc định rò | Tách DTO là việc **bắt buộc** của Phase 2, không phải phần "nếu còn thời gian" |
| **Làm share trước khi 7B chạy thật** | 7B chưa test runtime; share xếp lên một nền chưa verify thì bug khó quy trách nhiệm | Test runtime nhiều user của 7B trước, hoặc chấp nhận và ghi rõ |
| **Migration `AddCameraOwner` chưa apply** | Phase 1 của plan này sinh migration **nối tiếp** nó; thứ tự apply phải đúng | `make db-update` cho `AddCameraOwner` trước khi apply migration mới |
| **Cảnh báo trùng URL không đáng tin** | IP vs DDNS không khớp, query khác không khớp | Nói rõ giới hạn trong UI; coi là lưới bắt ca rõ ràng, không phải bảo đảm |
| **Share + `Enabled`** | Chủ tắt camera là mọi người xem mất — đúng ý định, nhưng phải nói được trên UI để người xem không tưởng app hỏng | Nhãn rõ trên card của người xem |
| **DG-S1 chọn B/C kéo theo hai repo** | RPC mới = sửa proto + desktop + SDK + bump 2 package + addon cũ phải cập nhật | ✅ Đã đi hướng B; thay đổi **additive** nên không addon nào vỡ |
| **RPC trả về danh bạ user cho mọi addon** | Caller là code bên thứ ba, và sau DG-S8 = A thì **cả danh bạ** (username + `Name`) lấy được trong 1 lần gọi `SearchUsers` query rỗng, hoặc quét id qua `GetUsers`. Không rate limit, không audit. Không còn là lỗ hổng mà là **thiết kế đã chốt** — nhưng là thiết kế *không thể thu hẹp lại* mà không bump proto | Chấp nhận; ghi lại vì nếu desktop về sau có cơ chế cấp quyền cho addon thì đây là chỗ áp vào |
| ~~Picker liệt kê cả chính chủ~~ | ~~Chọn mình rồi Add ăn 400 (`CameraService.cs:136-137`)~~ | ✅ Đã lọc ở `UsersController` bằng `CurrentUserId` |

---

## 7. Không nằm trong phạm vi

- **Ghi hình / playback** (Phase 6 của scout) — `RecordEnabled`/`RetentionDays` hiện chưa có chỗ tiêu thụ (C8), nên mọi mức permission liên quan playback là thiết kế cho thứ chưa tồn tại.
- **Dedupe theo URL ở tầng stream** — đã bị loại ở mục 1 vì không trả lời được câu hỏi creds.
- **Chuyển quyền sở hữu** (đổi `OwnerId` sang user khác) — không nằm trong đề xuất gốc.
- **Group/team** thay vì share từng người.

---

## 8. DG-S1 đã làm gì (2026-09-16) — RPC `SearchUsers`

**Quyết định của user:** đi hướng B — thêm RPC để lấy danh tính từ bảng user của desktop.

### Mặt API (công khai — đổi sau là break, cân nhắc kỹ)

```proto
rpc SearchUsers(SearchUsersRequest) returns (SearchUsersResponse);

message SearchUsersRequest {
  string query = 1;   // khớp username, tên hiển thị, email; RỖNG = liệt kê hết
  int32  limit = 2;   // desktop kẹp vào [1, 200]; ≤ 0 nghĩa là lấy mặc định (50)
  int32  offset = 3;  // số dòng bỏ qua, để với tới trang sau trần
}

message SearchUsersResponse { repeated AddonUser users = 1; }

message GetUsersRequest  { repeated int64 user_ids = 1; }   // kẹp 50 id/lần
message GetUsersResponse { repeated AddonUser users = 1; }  // id không có thì vắng mặt

message AddonUser {
  int64  user_id  = 1;   // đúng bằng OAuthTokenResult.user_id
  string username = 2;
  string name     = 3;
}
```

**Ba lựa chọn thiết kế mình tự quyết, cần user xác nhận lại:**

| Lựa chọn | Lý do | Trạng thái |
|----------|-------|----------------|
| ~~Buộc query ≥ 2 ký tự~~ | Bản đầu đặt ra để chặn `SearchUsers("")` = dump toàn danh bạ | **Đã bỏ** — DG-S8 chốt là picker phải thấy danh sách, nên liệt kê trở thành tính năng |
| **Không trả `email`** — email chỉ là khoá để khớp | Trả email là PII ra ngoài SDK; không trả thì addon vẫn tìm được người theo email, nhưng không xác nhận được một địa chỉ bằng cách dò | Giữ |
| **Không trả `Role`** | Không cần cho UX chọn người, mà lộ ai là admin | Giữ |

### Mã đã viết (build sạch 3 project, 0 error / 0 warning)

| File | Thay đổi |
|------|----------|
| `Namorix.Core/Protos/addon_channel.proto` | +`SearchUsers` + 3 message |
| `Namorix.Server/Services/UserService.cs` | +`UserSummary(int Id, string Username, string Name)` (record) và `SearchAsync(query, limit, ct)` — `AsNoTracking`, `ToLower()` cả hai vế vì `LIKE` của SQLite chỉ không phân biệt hoa thường với ASCII còn `instr()` thì phân biệt hẳn, `Select` chiếu thẳng ra `UserSummary` nên password hash không bao giờ rời DB dưới dạng entity |
| `Namorix.Server/Services/Grpc/AddonChannelService.cs` | +`SearchUsers` override: `RequireAddonClientIdAsync` như mọi RPC khác, kẹp limit, +`UserService users` vào primary constructor |
| `Namorix.Core/Grpc/AddonChannelClient.cs` | +`SearchUsersAsync(query, limit, ct)` — theo đúng khuôn `GetJwksAsync`, trả thẳng proto response |

### Chưa làm / đã biết

- **Chưa bump version.** Đề xuất: `Namorix.Core` 0.70.0 → 0.71.0, `Namorix.Server` 0.87.0 → 0.88.0 (MINOR — thêm method vào proto là additive). Cố ý để riêng vì chưa addon nào dùng.
- **Chưa test runtime.** Chưa có caller thật nào; muốn thử thì phải viết một addon gọi `SearchUsersAsync` hoặc một console project ngoài repo như cách đã smoke-test các phần Core khác.
- **Chưa có rate limit / audit** — xem bảng rủi ro mục 6.
- **`AddonChannelClient.EnsureStarted()` chỉ kiểm `_channel != null`**, không kiểm cổng `IsConnected`; caller phải tự gate như `AddonSessionMiddleware` đang làm. Scout gọi RPC này thì phải theo đúng luật đó.
- **Không sửa `UserController`/HTTP API** — RPC này chỉ dành cho kênh addon, desktop UI chưa có màn hình quản lý user nào để nối vào.

---

## 9. DG-S2 (đổi tên `UserId` → `OwnerId`) — hoãn có chủ ý

**Quyết định ở Phase 1: KHÔNG đổi tên.** `ScCamera.UserId` giữ nguyên.

**Vì sao hoãn:**

- Đổi tên là thao tác **không hoàn tác rẻ** trên DB đang sống (EF phải rebuild bảng trên SQLite), trong khi lợi ích chỉ là đọc dễ hiểu hơn. Batch này đang thuần **additive** — thêm bảng, không đụng cột nào — và giữ nó như vậy là lựa chọn an toàn hơn cho một migration sắp apply lên DB thật.
- Nó **rẻ để làm sau**: thêm một migration `RenameColumn` + sửa ~10 call site trong `CameraService`/`CamerasController`/`StreamsController`. Không có dữ liệu nào phải chuyển.
- User **chưa chốt** DG-S2, nên đây là việc không được phép tự quyết thay.

**Cái giá phải trả, nói rõ:** từ Phase 2 trở đi, `ScCamera.UserId` nghĩa là **chủ**, còn `CameraShare.UserId` nghĩa là **người được cấp quyền**. Hai cột cùng tên, hai nghĩa, trong cùng một tính năng — đây đúng là loại bẫy sinh bug. Phase 2 phải viết điều kiện truy cập sao cho không ai đọc nhầm, và nếu thấy khó đọc thì lúc đó chốt đổi tên.

**Nếu chốt đổi tên sau:** làm thành một migration riêng (`RenameCameraUserIdToOwner`), **không** gộp vào `AddCameraOwner` hay `AddCameraShare` đã sinh.
