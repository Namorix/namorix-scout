---
name: "Camera pipeline — Live + Recording (RTSP → WebRTC → fMP4)"
overview: "Roadmap toàn pipeline camera của Namorix Scout: ingest RTSP (.NET-native, SharpRTSP) → live WebRTC (SIPSorcery) → recording fMP4 (SharpMP4) → timeline/playback. Bóc tách theo phase nhỏ, mỗi phase kết thúc bằng thứ chạy được / nhìn thấy được. Thay thế tab Live/Settings placeholder."
todos: []
isProject: false
---

# Camera pipeline — Live + Recording

## Trạng thái

🟢 **Phase 1 ✅ XONG** (2026-09-05) — ingest spike chạy với camera thật; SharpRTSP giữ (G1 đóng). Backend có `RtspIngestService` (handshake + Digest auth, reconnect, log NAL stats). Frontend Live/Settings vẫn placeholder.

🟢 **Phase 2 ✅ IMPLEMENTED** (2026-09-07) — Camera domain + CRUD API (`ScCamera`, migration `20260907071530_AddCameras`, `CameraService` + DataProtection cred, `CamerasController` REST `/api/cameras`). G6 đóng: tách userinfo khỏi URL + mã hoá `RtspCredentials` ở cột riêng. Verify CRUD smoke do owner chốt.

🟢 **Phase 3 ✅ XONG** (2026-09-07) — Camera manager UI đặt trên tab **Live** (không phải Settings — user redirect). CRUD add/edit/delete camera qua form `NmxAlertDialog`, list grid card + delete confirm; Redux `cameraSlice` + hook `useCameras`; kết quả/lỗi hiện qua toast. Fix `nmxToast`: mount `<NmxToastProvider/>` trong `ScoutApp` (addon bundle `@namorix/core` riêng → host provider không nghe bus addon). Nâng cấp sau: creds nhập qua field **Username/Password** riêng (backend `CameraUpsertRequest` + `ScCameraDto.Username`, prefill khi edit, password không trả về) + fix SQLite `DateTimeOffset` ORDER BY. Verify: `pnpm dev` + `make run`, thêm/sửa/xoá camera, toast hiện. Bump 0.4.0 (batch gộp Phase 3+4+5) + memory bank đã cập nhật.

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
| **3** | Live tab: add/manage camera | frontend | CRUD camera qua UI trên tab Live (form dialog + list card + toast) | 0.3.0 |
| **4** | WebRTC live spike | backend | 1 camera hiện qua peer (Chrome) | — |
| **5** | Live view | frontend | tab Live hiện stream, đa camera grid | 0.4.0 ✅ XONG |
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

⚠ **Security (dev-local):** URL spike thật kèm pass đặt trong `backend/src/Properties/launchSettings.json` profile `http` (`RtspSpike__Url`) — **working tree, chưa commit**; `appsettings.json` giữ `RtspSpike:Url: ""`. Giữ dev-local, đừng commit. Kể từ Phase 3 camera vào DB qua UI — cred tách userinfo + mã hoá DataProtection (G6).

---

## Phase 2 — Camera domain + CRUD API — backend — 🟢 IMPLEMENTED (2026-09-07)

**Mục đích (đạt):** có entity `ScCamera` + CRUD REST để Phase 3 (Settings UI) và Phase 6 (recording nền) dựa vào.

**Files (`backend/src/`):**
- `Models/ScCamera.cs` + `Models/Enums.cs` — `Guid Id`, `Name`, `RtspUrl` (**sanitized**, bỏ userinfo), `RtspCredentials?` (user:pass mã hoá, không bao giờ trả về), `CameraStreamType` (Main/Sub, lưu string), `Enabled`, `RecordEnabled`, `RetentionDays` (mặc định 7), `DateTimeOffset CreatedAt`.
- `Services/ScoutSecretProtector.cs` — DataProtection purpose `Scout.CameraCredentials`; key ring `{DataDir}/keys`.
- `Services/CameraService.cs` — CRUD qua `IDbContextFactory`; parse `rtsp[s]://user:pass@host/path`, tách userinfo, `Protect` trước khi lưu; map DTO.
- `Controllers/CamerasController.cs` — `[RequireAuth]` + `[Route("api/cameras")]`: `GET /`, `GET /{id:guid}`, `POST`, `PUT/{id:guid}`, `DELETE/{id:guid}`. 400 `INVALID_CAMERA_INPUT`, 404 `CAMERA_NOT_FOUND`, wrap `ApiResponse.Ok/Fail`.
- `Persistence/ScoutDbContext.cs` — `DbSet<ScCamera>` + enum→string; migration **`20260907071530_AddCameras`** (đã chạy).
- `Constants/ScoutSignalR.cs` — `ScoutSignalREvents` (`scout:camera-changed`/`camera-deleted`) — contract Phase 3; `Constants/Error.cs` — `CAMERA_NOT_FOUND`, `INVALID_CAMERA_INPUT`.
- `Program.cs` — `AddDataProtection().PersistKeysToFileSystem({DataDir}/keys)` + DI `ScoutSecretProtector`/`CameraService`.

**G6 — ĐÃ ĐÓNG:** tách cred khỏi URL. Response chỉ có `rtspUrl` sanitized + `hasCredentials`; pass nằm cột `RtspCredentials` mã hoá DataProtection. (Key ring mới → lần chạy đầu có thể phải login lại desktop.)

**Verify còn lại (owner):** `make build` + smoke CRUD (POST/GET/PUT/DELETE camera giả qua swagger/curl) rồi xác nhận — chưa mark ✅ gate.

---

## Phase 3 — Live tab: add/manage camera — frontend — 🟢 XONG (2026-09-07)

**Mục đích (đạt):** user nhập/quản lý camera qua UI (thay vì sửa config). **Thay đổi so với roadmap:** camera manager đặt trên tab **Live**, không phải Settings — user redirect ("Add camera thì cho ở live view chứ để ở setting làm gì, Settings để tạm vậy thôi"); Settings giữ placeholder.

**Files (`frontend/src/`):**
- `types/camera.ts` — `Camera` (id, name, `rtspUrl` sanitized, `streamType` main/sub, enabled, recordEnabled, retentionDays, hasCredentials, `username`, createdAt) + `CameraUpsert` (kèm `username`/`password`).
- `scoutApiRoutes.ts` — `ScoutApiRoutes.cameras` / `cameraById(id)` (`API_BASE + "/cameras"`).
- `controllers/camera.controller.ts` — `list/get/create/update/remove` qua `coreConfig.http` (pattern weave `network.controller.ts`); non-success → `throw ApiError.fromResponse`.
- `store/slices/cameraSlice.ts` + `store/selectors/cameraSelectors.ts` — state normalized `byId`/`order` (Rule 5); actions `setCameras`/`upsertCamera`/`removeCamera`.
- `hooks/useCameras.ts` — load list khi mount + `refresh` (try/catch → `loadFailed`).
- `views/live/LiveView.tsx` — toolbar Add camera (`NmxButton` + ADD icon) + `NmxButtonRefresh`; grid `NmxCard` (badge enabled/disabled, url, stream, record, retention) + footer Edit/Delete; `renderEmpty()` (loadFailed → Retry); form + delete dialog.
- `views/live/CameraFormDialog.tsx` — add/edit form trong **`NmxAlertDialog`** (không dùng `NmxDialog` raw — user hỏi "có NmxAlertDialog sao lại dùng NmxDialog"): name + RTSP URL (chỉ host/path) + **Username** (prefill `camera.username` khi edit) + **Password** (`type=password`, luôn để trống) + `NmxSelect` stream + retention + toggle Enabled/Record; validate name required + retention 1..365 + password có mà username trống → toast lỗi; submit `create`/`update` → `onSaved` → toast.
- `views/live/CameraErrorCodes.ts` — map `CAMERA_NOT_FOUND` → i18n (`formatCustomError`).
- `views/settings/SettingsView.tsx` — placeholder `<h1>` (redirect về Live).
- `ScoutApp.tsx` — mount `<NmxToastProvider/>` + route Live → `LiveView`, Settings → `SettingsView`.
- `i18n/locales/en.json` — `scout.cameras.*` (list/form/delete/errors) + `scout.live.empty`; vi.json trống (fallback en).

**Toast fix (`nmxToast` không hiện):** `nmxToast` trong `@namorix/core` là **event bus** — chỉ emit, không tự render. `NmxToastProvider` trước chỉ mount ở desktop host `Root.tsx`; scout bundle `@namorix/core` riêng (MF `shared` chỉ react/i18next/react-dom/react-i18next) → bus addon không ai nghe ở cả standalone lẫn widget. Giải pháp: mount `<NmxToastProvider/>` ngay trong `ScoutApp` (bên trong `NmxAddonRoot`). Host và addon dùng bus khác instance nên không lo toast trùng.

**Creds tách field (backend + frontend):** người dùng không còn gõ `user:pass@` trong URL. `Dtos/CameraDtos.cs` — `CameraUpsertRequest` + `Username?`/`Password?`; `ScCameraDto` + `Username?` (giải mã creds qua `ScoutSecretProtector.Unprotect`, lấy phần trước `:` — password không bao giờ trả về, `ReadUsername` bọc try/catch). `CameraService.ResolveCredentials`: nhập user+pass → mã hoá creds `{user}:{pass}` mới; **password để trống → giữ creds cũ** (create = không auth); password có mà thiếu username → 400 `INVALID_CAMERA_INPUT`. Vẫn nhận URL dán kèm creds (fallback cũ). Giới hạn: chưa có cách **xoá** creds đã lưu.

**Bug fix (backend):** `GET /api/cameras` văng `NotSupportedException` — SQLite không translate `OrderBy(c.CreatedAt)` (DateTimeOffset). Fix: `ListAsync` lấy list rồi order client-side (`cameras.OrderBy(...).Select(ToDto)`).

**Security:** creds chỉ nằm field Username/Password → gửi lên backend, không log; API trả `rtspUrl` đã redact + `hasCredentials` + `username` (không bao giờ password).

**Verify (owner, đã xác nhận 2026-09-07):** `pnpm dev` + `make run` → tab Live thêm/sửa/xoá camera, list hiện; toast success/error hiện sau fix; edit camera có creds thấy username prefill.

---

## Phase 4 — WebRTC live spike — backend — ✅ XONG (2026-09-07)

**Mục đích:** chứng minh NAL → SIPSorcery passthrough với 1 camera thật — **đoạn ít tài liệu nhất**, smoke test cross-browser sớm (G5).

✅ **4a — DB-driven ingest (2026-09-07, xong):** `RtspIngestService` giờ là registry reconcile camera **enabled** từ `ScCamera` mỗi 5s (bỏ `RtspSpike:Url` khỏi `appsettings.json`/`launchSettings.json` — hết cred trong config; cred giải mã qua `ScoutSecretProtector.Unprotect(RtspCredentials)`). Bóc nested `RtspSession` → `CameraRtspClient.cs` (giữ nguyên handshake/Digest/reconnect/TEARDOWN, thêm connect timeout 5s + luôn strip userinfo khỏi request-uri). Thêm `H264Depacketizer.cs` — NAL reassembly chuẩn RFC 6184 (single/STAP-A/FU-A/FU-B) gom theo RTP marker bit → `VideoFrame` (list NAL + keyframe + timestamp) phát qua event `FrameReceived`; `H264CodecSnapshot` (SPS/PPS, `profile-level-id`, `sprop-parameter-sets` base64) cache để WebRTC dùng cho SDP fmtp. SIPSorcery **10.0.16** đã thêm vào csproj.

✅ **4b — WebRTC signaling + live-test page (2026-09-07, code + build xong):** backend là **offerer**, browser answer + trickle ICE qua REST. Files: `Streaming/H264RtpPacketizer.cs` (single-NAL khi ≤1200 bytes, còn lại FU-A RFC 6184; marker bit ở packet cuối), `Streaming/WebRtcRelayService.cs` (+ `RtcViewerSession`: `RTCPeerConnection`, `MediaStreamTrack`/`VideoFormat(H264)` send-only, parse payload type từ offer `a=rtpmap`, subscribe `FrameReceived` khi connection `connected`, gate send bằng `SendRtpRaw(video, payload, rtpTimestamp, marker, pt)`), `Controllers/StreamsController.cs` (`POST {camera}/offer` → `{sessionId,sdp,payloadType}`, `POST {session}/answer`, `POST {session}/ice`, `GET {session}/ice` drain, `DELETE {session}`), `Dtos/StreamDtos.cs`, `Constants/Error.cs` (+`CameraOffline/StreamNotFound/StreamOfferFailed/StreamAnswerFailed/InvalidStreamInput`), `Program.cs` DI (`AddSingleton<RtspIngestService>` + `AddHostedService(factory)` để relay inject cùng instance). SPS/PPS in-band mỗi GOP (fmtp sprop defer v1); viewer đợi ≤1 GOP cho keyframe đầu. Test page `frontend/public/live-test.html` (`?camera=<guid>`): offer → answer → trickle `pc.onicecandidate` lên `POST /ice` + poll `GET /ice`, log connection state → video khi có keyframe. **Build pass sau 2 round fix compile** — API SIPSorcery 10.0.16 xác nhận qua chính compiler: `createOffer()`/`createAnswer()` **sync** trả `RTCSessionDescriptionInit`; `setLocalDescription` **async**; `setRemoteDescription(RTCSessionDescriptionInit)` **sync trả `SetDescriptionResultEnum`** (không await được) → `ApplyAnswer`/`SetRemoteAnswer` sync; `addIceCandidate` trả **void**; `RTCPeerConnectionState.@new/connecting/closed` tồn tại. Còn lại sửa warning: `RtcOffer` record → class `init` (JSON đọc qua reflection nên record gây "positional property never accessed"), `CloseAsync` bỏ `async` (không await) trả `Task.CompletedTask`, 2 `catch{}` rỗng thêm log, `Find` hạ `internal` (không expose `RtcViewerSession` internal qua public). **Verify (owner, 2026-09-07):** video thật lên Chrome qua tab Live sau khi sửa `VideoFormat` payload type (xem Phase 5) — `live-test.html` cũng thành công. **Gate G5 chưa test Safari** (H.264 passthrough cross-browser — theo dõi khi test Safari).

**Files:**
- `backend/src/Namorix.Scout.csproj` — thêm **SIPSorcery**.
- `backend/src/Streaming/RtspIngestService.cs` (prototype Phase 1 → hoàn thiện nhẹ): đẩy NAL frame qua channel/handler thay vì chỉ log.
- `backend/src/Streaming/WebRtcRelayService.cs` — prototype: tạo `RTCPeerConnection`, packetize NAL → RTP H.264 (SPS/PPS handling: gửi qua in-band hoặc SDP `fmtp`/`sprop-parameter-sets`), ICE/DTLS/SRTP. **Viết glue NAL→RTP riêng** — không lib nào lo.
- Signaling: dùng REST đơn giản `POST /api/streams/{cameraId}/offer` (body = SDP offer từ browser) → trả SDP answer + ICE. Không dùng server signaling riêng (SIPSorcery in-process).
- `backend/src/Controllers/StreamsController.cs` — endpoint offer/answer + `POST /api/streams/{cameraId}/stop`.

**Gate (G5):** smoke Chrome trước; nếu H.264 passthrough qua SIPSorcery có vấn đề Safari → chốt chiến lược: ưu tiên sub-stream H.264 cho live/WebRTC; main H.265 chỉ dành recording; **HLS fallback** nếu cần.

**Verify (owner):** page test đơn giản hoặc curl SDP → thấy video trên Chrome.

---

## Phase 5 — Live view — frontend — ✅ XONG (2026-09-07)

**Mục đích (đạt):** tab Live hiện camera thật qua WebRTC, đa camera grid. **User redirect:** không làm manager ở Settings mà **giữ nguyên camera CRUD manager ngay trên tab Live** (toolbar Add/Refresh + card grid), Live/Settings placeholder vẫn tách — camera manager là chức năng của Live view. **User chốt layout:** lưới **thuần nhất** (bỏ khái niệm main-stage lớn + grid phụ ban đầu).

**Frontend self-derive stream state — KHÔNG dùng SignalR:** backend hiện không push `scout:camera-*` state event (chỉ định nghĩa constants) → Phase 5 suy live/connecting/offline/idle từ chính session stream, không có nguồn sự kiện backend.

**Files (`frontend/src/`):**
- `scoutApiRoutes.ts` — `ScoutApiRoutes.streams` (`offer/answer/ice/stop` theo `STREAMS_BASE = API_BASE + "/streams"`).
- `controllers/streams.controller.ts` — `StreamOffer`/`StreamIceList`; `offer/answer/iceAdd/iceList/stop` qua `coreConfig.http`; non-success → `throw ApiError.fromResponse`.
- `streaming/RtcStreamClient.ts` — **browser WebRTC glue** (tái sử dụng REST signaling của `live-test.html` 4b): `RtcStreamClient` class quản lý vòng đời session (`runId` + `disposed` → callback race-safe). Flow: `offer(cameraId)` → `RTCPeerConnection({iceServers:[]})` + `addTransceiver("video", recvonly)` → `setRemoteDescription(offer)` → `createAnswer` → `setLocalDescription` → chờ ICE gathering (5s) → `POST answer` → poll server ICE mỗi **250ms** (drain `GET /ice` → `addIceCandidate`; trickle candidate browser lên `POST /ice` qua `onicecandidate`) → connected/completed hoặc timeout **20s** → `fail(reason)`. `StreamStatus {state: idle|connecting|live|offline, reason, stream}`; `StreamState` map từ trạng thái session; `fail`/`stop` gọi `DELETE session` + đóng peer.
- `hooks/useLiveStreams.ts` — 1 session WebRTC per camera (`entriesRef` Map), **auto-play camera enabled** khi reconcile theo `signature` (danh sách enabled id), hủy session khi camera bị disable/xoá; `play(id)`/`stop(id)` manual (play thay session mới nếu có). Camera disabled không auto-phát nhưng bấm Play được bằng tay.
- `views/live/StreamVideo.tsx` — `<video muted autoPlay playsInline>` gắn `srcObject` từ `MediaStream`.
- `views/live/LiveView.tsx` — toolbar (Add camera + `NmxButtonRefresh`) + **`NmxGrid minColWidth={320}` auto-fit thuần nhất** (mọi camera là card bằng nhau — 1 camera stretch full-width, 2+ tự chia cột); card = `CameraLiveCard`; CRUD + delete-confirm + `renderEmpty()` giữ nguyên từ Phase 3.
- `views/live/CameraLiveCard.tsx` — overlay status-only (spinner khi connecting, text Live/Connecting/Offline/Disabled/Stopped khi không live); actions: **Stop** (semantic error) khi live, **Play** khi idle/disabled/offline (disable khi connecting), Edit, Delete.
- `views/live/LiveView.scss` — `.scout-live-card` flex column surface-low + 16/9 video `object-fit: contain`.
- `i18n/locales/en.json` — `scout.live.*` (`connecting/live/offline/idle/play/stop`).

**Bug fix runtime (STREAM_OFFER_FAILED 400) — SIPSorcery `VideoFormat` ctor:** `new(VideoCodecsEnum.H264, fmtID, clockRate, parameters)` — **formatID là payload type RTP (0–127), clockRate H264 = 90000**. Lỗi 1 "clock rate > 0" khi truyền clock 0; lỗi 2 "format ID exceeded 127" khi nhầm tham số giữa là clock. **Fix:** `new(VideoCodecsEnum.H264, 96, 90_000, "packetization-mode=1")` → offer SDP `a=rtpmap:96 H264/90000` (regex `_h264Rtpmap` vẫn bắt payload type 96 để gửi RTP).

**Verify (owner, đã xác nhận 2026-09-07):** `pnpm dev` + `make run` → tab Live video camera hiện qua WebRTC sau fix payload type; camera enabled auto-play, đa camera grid.

**Còn lại / follow-up:** reason lỗi (`stream-offline`/`connection-lost`/`timeout`) chưa hiển thị trên card (chưa có i18n map + UI line); SignalR push `scout:camera-state` (online/offline từ ingest) là follow-up backend — hiện self-derive phía client.

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
