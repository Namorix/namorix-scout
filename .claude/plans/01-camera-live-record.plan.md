---
name: "Camera pipeline — Live + Recording (RTSP → WebRTC → fMP4)"
overview: "Roadmap toàn pipeline camera của Namorix Scout: ingest RTSP (.NET-native, SharpRTSP) → live WebRTC (SIPSorcery) → recording fMP4 (SharpMP4) → timeline/playback. Bóc tách theo phase nhỏ, mỗi phase kết thúc bằng thứ chạy được / nhìn thấy được. Thay thế tab Live/Settings placeholder."
todos: []
isProject: false
---

# Camera pipeline — Live + Recording

## Trạng thái

🟢 **Phase 1 ✅ XONG** (2026-09-05) — ingest spike chạy với camera thật; SharpRTSP giữ (G1 đóng). Backend có `RtspIngestService` (handshake + Digest auth, reconnect, log NAL stats). Frontend Live/Settings vẫn placeholder — **Phase 2 (Camera CRUD) làm tiếp**.

## Mục tiêu

Biến Scout thành Surveillance-Station-style: xem trực tiếp camera IP (RTSP) và ghi hình cục bộ, tất cả bằng C# trong addon — **no MediaMTX / sidecar** (hard decision, `systemPatterns.md` §7). Browser không mở RTSP trực tiếp được nên bắt buộc relay server-side.

## Kiến trúc đích (tóm tắt)

```
IP camera (RTSP :554)
      │ SharpRTSP pull → H.264 NAL units   (RtspClientSharp fallback nếu API quá thấp)
      ▼
Streaming/RtspIngestService.cs  ──per-camera──►  NAL frames
      │
      ├─► WebRtcRelayService.cs  (SIPSorcery)  NAL→RTP passthrough → Live tab (<video>)
      │        signaling SDP qua REST (/api/streams) — không cần server signaling riêng
      └─► RecordingWriterService.cs (SharpMP4 FragmentedMp4Builder)
               fragment 2s, rotate 5–10 min, FileStream.Flush(true) per fragment
               → recordings/{cameraId}/{yyyy-MM-dd}/{start}.mp4
               → ScRecordingSegment row (EF Core) → timeline API → playback HTTP Range
```

Module layout tương lai (`backend/src/`):
```
Streaming/
  RtspIngestService.cs        // SharpRTSP per camera → NAL frames
  WebRtcRelayService.cs       // SIPSorcery RTCPeerConnection, NAL → RTP
  RecordingWriterService.cs   // SharpMP4 fragment rotation + ScRecordingSegment rows
  RetentionWorker.cs          // PeriodicTimer — xoá file quá hạn giữ
Models/ScCamera.cs · ScRecordingSegment.cs   // (ScMotionEvent sau nếu làm motion)
Controllers/CamerasController.cs · TimelineController.cs (or Streaming/PlaybackController.cs)
```

## Cổng quyết định (decision gates) — phải chốt đúng thời điểm

| # | Gate | Khi nào | Ảnh hưởng |
|---|------|---------|-----------|
| G1 | SharpRTSP **vs** RtspClientSharp | sau Phase 1 (ingest spike) | chọn thư viện ingest; bỏ lib kia |
| G2 | Audio từ camera? (AAC/G711) | trước Phase 6 (recording) | có → phải xử lý audio mux; không → bỏ hẳn nhánh audio |
| G3 | Motion: ONVIF event **vs** frame-diff | trước Phase 6 | frame-diff kéo dependency decoder vào — nếu dùng ONVIF thì là Phase sau (post-record), không chặn recording |
| G4 | Concurrency đa camera: worker-per-camera (`IHostedService` per `ScCamera`) **vs** pooled | trước Phase 6 | thiết kế registry + worker nền |
| G5 | Live leg: H.264 passthrough có chạy cross-browser không (Safari) | sau Phase 4 smoke test | không → đổi sub-stream H.264; hẹn HLS fallback |
| G6 | Bảo mật RTSP URL/cred | từ Phase 1 | lưu cred mã hoá (DataProtection) như weave `WeaveSecretProtector`? hay user nhập URL kèm user:pass mỗi lần? |

---

## Tổng quan các phase

| Phase | Tên | Scope | Kết thúc bằng | Version |
|-------|-----|-------|---------------|---------|
| **1** | Ingest prototype (spike) | backend | log NAL units từ camera thật | — (không bump) |
| **2** | Camera domain + CRUD API | backend | `GET/POST/PUT/DELETE /api/cameras` + migration | — |
| **3** | Settings tab: add/manage camera | frontend | nhập name + RTSP URL, lưu được | 0.3.0 |
| **4** | WebRTC live spike | backend | 1 camera hiện qua peer (Chrome) | — |
| **5** | Live view | frontend | tab Live hiện stream, đa camera grid | 0.4.0 |
| **6** | Recording writer (fMP4) | backend | ghi segment, crash-safe flush, retention | — |
| **7** | Recording index + timeline + playback API | backend | `/timeline` + range-request MP4 | 0.5.0 |
| **8** | Playback + timeline UI | frontend | xem lại bản ghi theo ngày | 0.6.0 |
| *(9)* | *(Motion detection — ONVIF)* | *(post)* | *(tuỳ G3)* | *tuỳ* |

Bump theo rule skill update-docs-and-versions: **chỉ bump khi behavior Desktop-visible đổi** (Phase có marker version). Phase backend-thuần có `—` → ghi progress.md, không bump.

---

## Phase 1 — Ingest prototype (spike) — backend — ✅ DONE (2026-09-05)

**Mục đích:** validate SharpRTSP với camera thật của owner. **Throwaway prototype** — viết gọn, không làm domain model, không commit cred thật. Đây là chân khó nhất/ít tiền lệ nhất nên làm trước.

**Files:**
- `backend/src/Namorix.Scout.csproj` — thêm `PackageReference` **SharpRTSP** (`ngraziano/SharpRTSP`). Nếu API quá low-level để lấy NAL sạch → thử **RtspClientSharp** (G1).
- `backend/src/Streaming/RtspIngestService.cs` — prototype:
  - `BackgroundService` đọc 1 camera từ config dev-only (KHÔNG commit cred — xem Security).
  - Connect RTSP → receive RTP → tách payload → log **NAL-unit stats**: count từng loại (SPS/PPS/IDR/non-IDR), có thấy SPS/PPS không (in-band hay chỉ trong SDP), STAP-A có không, fps ước lượng, kích thước frame.
  - Handle reconnect/backoff + đừng bao giờ log URL kèm password.
- `backend/src/appsettings.Development.json` / `launchSettings.json` — camera URL dev-local với `rtsp://USER:PASS@...` placeholder (owner điền thật cục bộ, không commit).

**Gate — ĐÃ ĐÓNG:** chạy với camera thật, log NAL đều + SPS/PPS → **SharpRTSP giữ** (không cần chuyển RtspClientSharp). Code spike giữ lại làm nền Phase 6.

**Kết quả (owner chạy 2026-09-05):** Hikvision `192.168.31.161` (subtype=0, H.264 High L4.0 qua `profile-level-id=4D4028`) — Digest auth chạy, SETUP/PLAY OK; RTP ~2 MB/5s ≈ 3900–4500 kbps, ~25 fps, SPS≈PPS≈2–3 mỗi window (bằng nhau → đếm STAP-A đúng). Sau code review sửa 3 điểm: (1) **STAP-A** giờ loop qua tất cả NAL gộp, không chỉ lấy NAL đầu (camera hay gộp SPS+PPS trong 1 gói lúc đầu GOP); (2) gửi **TEARDOWN best-effort** trong `finally` trước khi đóng socket — tránh camera giữ session kẹt khi reconnect; (3) **Authorization gắn sẵn trước khi gửi** khi `_auth` đã có — chỉ OPTIONS bị 401 một lần, DESCRIBE/SETUP/PLAY sau đi thẳng, hết double round-trip. **Defer có chủ đích:** parse SPS để log resolution width×height (item #4), xử lý FU-B NAL 29 (#5 — không dùng thực tế).

⚠ **Open (security):** URL thật kèm password đang nằm trong `backend/src/appsettings.json` — file **tracked**. Trước khi commit Phase 2: bỏ URL khỏi đó, set qua env `RtspSpike__Url`; đồng thời xoá key `RtspSpike__Url: ""` cũ trong `launchSettings.json` profile `http` (đang ghi đè appsettings thành rỗng khi `dotnet run`).

---

## Phase 2 — Camera domain + CRUD API — backend

**Mục đích:** có entity `ScCamera` để Phase 3 (Settings) và Phase 6 (recording nền) dựa vào.

**Files:**
- `backend/src/Models/ScCamera.cs` — theo Rule 11: class `PascalCase`. Fields đề xuất:
  - `Guid Id`, `string Name`, `string RtspUrl` (hoặc tách `RtspUrl` + cred riêng — chốt ở G6), `CameraStreamType StreamType` (main/sub enum), `bool Enabled`, `bool RecordEnabled`, `int RetentionDays` (mặc định 7), `DateTimeOffset CreatedAt`.
- `backend/src/Persistence/ScoutDbContext.cs` — thêm `DbSet<ScCamera>`; migration mới qua `make db-init-create` + `db-update`.
- `backend/src/Controllers/CamerasController.cs` — REST CRUD theo convention namorix (mutation = REST, SignalR chỉ push):
  - `GET /api/cameras` (list), `GET /api/cameras/{id}`, `POST /api/cameras`, `PUT /api/cameras/{id}`, `DELETE /api/cameras/{id}`.
  - Auth: controller chạy sau `UseAddonSessionAuth` pipeline → tự được bảo vệ. **Không bao giờ trả RTSP cred về response** (redact url → chỉ `rtsp://host:port/path` hoặc flag `hasCredentials`).
- `backend/src/Constants/ScoutSignalR.cs` — thêm hằng số event camera: `camera:updated`, `camera:deleted`, `camera:state` (online/offline) — dùng `SignalRPath` prefix như `/hubs/scout`.

**Verify (owner chạy):** `make build`; smoke qua swagger/curl CRUD camera giả.

---

## Phase 3 — Settings tab: add/manage camera — frontend

**Mục đích:** user nhập camera qua UI (thay vì sửa config). Live tab giữ placeholder.

**Files:**
- `frontend/src/controllers/cameras.controller.ts` — gọi REST qua `coreConfig.http` (pattern weave `network.controller.ts`): `list/create/update/delete`.
- `frontend/src/views/SettingsView.tsx` — form thêm/sửa camera (name + RTSP URL + toggle Enabled/Record + retention), danh sách camera có nút edit/delete/xoá; dùng `@namorix/ui` primitives, `formatApiError` cho lỗi (Rule 7).
- `frontend/src/views/LiveView.tsx` — vẫn placeholder `<h1>` phase này (chỉ tách file sẵn sàng).
- `frontend/src/store/slices/camerasSlice.ts` + selectors — state `byId`/`order` (Rule 5 store pattern); hook `useCameras.ts`.
- `frontend/src/i18n/locales/en.json` — `scout.cameras.*`, `scout.settings.*`, errors; vi.json bỏ trống (fallback en).
- `ScoutApp.tsx` — route Live → `LiveView`, Settings → `SettingsView`.

**Security:** URL nhập có `rtsp://user:pass@` — chỉ gửi lên backend, không log; display đã redact.

**Verify (owner):** `pnpm dev` + `make run` → tab Settings thêm camera, list hiện, không log cred.

---

## Phase 4 — WebRTC live spike — backend

**Mục đích:** chứng minh NAL → SIPSorcery passthrough với 1 camera thật — **đoạn ít tài liệu nhất**, smoke test cross-browser sớm (G5).

**Files:**
- `backend/src/Namorix.Scout.csproj` — thêm **SIPSorcery**.
- `backend/src/Streaming/RtspIngestService.cs` (prototype Phase 1 → hoàn thiện nhẹ): đẩy NAL frame qua channel/handler thay vì chỉ log.
- `backend/src/Streaming/WebRtcRelayService.cs` — prototype: tạo `RTCPeerConnection`, packetize NAL → RTP H.264 (SPS/PPS handling: gửi qua in-band hoặc SDP `fmtp`/`sprop-parameter-sets`), ICE/DTLS/SRTP. **Viết glue NAL→RTP riêng** — không lib nào lo.
- Signaling: dùng REST đơn giản `POST /api/streams/{cameraId}/offer` (body = SDP offer từ browser) → trả SDP answer + ICE. Không dùng server signaling riêng (SIPSorcery in-process).
- `backend/src/Controllers/StreamsController.cs` — endpoint offer/answer + `POST /api/streams/{cameraId}/stop`.

**Gate (G5):** smoke Chrome trước; nếu H.264 passthrough qua SIPSorcery có vấn đề Safari → chốt chiến lược: ưu tiên sub-stream H.264 cho live/WebRTC; main H.265 chỉ dành recording; **HLS fallback** nếu cần.

**Verify (owner):** page test đơn giản hoặc curl SDP → thấy video trên Chrome.

---

## Phase 5 — Live view — frontend

**Mục đích:** tab Live hiện camera thật. Full-bleed (đã setup `spacing*Disabled`), nhiều camera = grid.

**Files:**
- `frontend/src/views/LiveView.tsx` — grid camera; mỗi card = `<video>` + play/stop + state (live/connecting/offline); 1 camera chính lớn + các camera phụ nhỏ (Surveillance-style).
- `frontend/src/signalr/` + `hooks/useCameraStream.ts` — nhận SDP qua REST, gắn stream vào `video.srcObject`; nghe `camera:state` online/offline.
- `frontend/src/controllers/streams.controller.ts` — offer/stop.
- SignalR camera events constants phía frontend (mirror backend `ScoutSignalR`).
- **Lưu ý kiến trúc:** `NmxBottomNavigationContent` giữ tab hidden **mounted** → stream WebRTC không restart khi chuyển tab (lý do đã chọn primitives này).
- i18n `scout.live.*`.

**Verify (owner):** tab Live xem được camera; chuyển Settings↔Live stream không ngắt.

---

## Phase 6 — Recording writer (fMP4) — backend

**Trước khi làm phải chốt:** G2 (audio?), G3 (motion — nếu frame-diff thì kéo decoder), G4 (concurrency worker-per-camera vs pooled).

**Files:**
- `backend/src/Namorix.Scout.csproj` — thêm **SharpMP4** (`jimm98y/SharpMP4`).
- `backend/src/Streaming/RecordingWriterService.cs`:
  - `FragmentedMp4Builder(output, 2000)` → fragment 2s; **mỗi fragment xong gọi `FileStream.Flush(true)`** (fsync) — crash mất tối đa fragment đang viết (~1–3s). KHÔNG plain MP4 (index `moov` ghi 1 lần lúc close → mất cả segment nếu mất điện).
  - Rotation: đóng file mỗi 5–10 phút → `recordings/{cameraId}/{yyyy-MM-dd}/{start-unix}.mp4`.
  - Chỉ ghi khi `ScCamera.RecordEnabled`; chọn main/sub stream theo cấu hình.
  - Multi-camera: worker-per-camera nếu G4 chốt vậy.
- (nếu G2 = có audio) nhánh audio AAC/G711 → mux vào fMP4; không → bỏ.

**Verify (owner):** bật record, để 1–2 phút, thấy file `.mp4` trong `data/recordings/`, đọc được bằng player (ffprobe/VLC).

---

## Phase 7 — Recording index + timeline + playback API — backend

**Files:**
- `backend/src/Models/ScRecordingSegment.cs` — `CameraId, StartTime, EndTime, FilePath, SizeBytes`; `ScoutDbContext` thêm `DbSet`; migration.
- `Streaming/RecordingWriterService.cs` — sau khi close 1 segment → insert row (crash-safe: chỉ index segment đã flush xong).
- `backend/src/Streaming/RetentionWorker.cs` — `PeriodicTimer` (vd mỗi 1h) xoá file + row quá `ScCamera.RetentionDays`.
- `Controllers/TimelineController.cs` — `GET /api/cameras/{id}/timeline?date=yyyy-MM-dd` → mảng recorded ranges cho timeline bar.
- `Controllers/PlaybackController.cs` — `GET /api/cameras/{id}/playback?start=&end=` (resolve segment, stitch qua boundary) serve MP4 **HTTP Range** (`Content-Range`/206) — phải đúng thứ tự `moof/mdat` để seek ổn.

**Verify (owner):** curl `Range: bytes=0-1023` → 206 + partial content; timeline trả đúng khoảng đã ghi.

---

## Phase 8 — Playback + timeline UI — frontend

**Files:**
- `frontend/src/views/LiveView.tsx` (hoặc `RecordingsView`) — chọn ngày → timeline bar highlight các khoảng đã record → click seek.
- `<video>` playback dùng URL API (server range-request) với `srcObject`/`src`; tua = set `currentTime` (backed by Range).
- i18n `scout.playback.*`, `scout.timeline.*`.
- (tuỳ) card camera thêm nút "Recordings".

**Verify (owner):** tua timeline xem lại bản ghi cũ liền mạch.

---

## Phase 9 — Motion detection (sau, tuỳ G3)

- Nếu chọn ONVIF motion events (khuyên dùng): backend poll `camera/motionDetection` events → `ScMotionEvent` + push SignalR `camera:motion`. Không kéo dependency decode.
- frame-diff: cần decoder (flag cho owner — thêm dependency nặng), không khuyến khích.

---

## Security checklist (xuyên các phase)

- **Không bao giờ commit RTSP URL chứa password thật** (activeContext nhắc `NMX_REGISTRATION_TOKEN` cũng chỉ dev-local). URL camera thật để trong `appsettings.Development.json`/`launchSettings` gitignored — dùng placeholder `rtsp://USER:PASS@` trong code mẫu.
- Không log URL/cred; log chỉ host:port hoặc id camera.
- API redact cred khi trả về frontend.
- Recording path theo `cameraId` — không lộ cred.
- Auth: controllers đứng sau session-auth pipeline; mutation qua REST (namorix convention) — SignalR chỉ push.

## Những file KHÔNG đụng tới

- `frontend/vite.config.ts`, Dockerfiles (không đổi cho feature này — chỉ khi thêm lib cần native, không có: cả 3 lib đều pure C#/JS, MIT).
- `addon.json` ports (5300/5302 giữ nguyên; WebRTC trong-process không cần port mới — dùng REST+SignalR sẵn có).
- `Namorix.Core`, `@namorix/ui`, `@namorix/styles` (sibling) — không sửa trừ khi cần primitive UI mới (đưa lên `namorix` repo).

## Ghi chú triển khai

- Plan này là roadmap tổng của milestone camera. Khi bắt tay từng phase, theo convention weave có thể bóc thành plan riêng (số `NN-...`) và archive dần — mục đích là để owner review toàn cảnh + thứ tự.
- Mỗi phase = branch `feature/{short-desc}` (Rule 8), commit `{type}({scope}): ...`.
- Sau mỗi phase có bump version → chạy skill `update-docs-and-versions` + cập nhật memory bank.
