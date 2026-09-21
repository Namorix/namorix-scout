---
name: "Camera remote view — HLS qua Frontgate (bỏ WebRTC cho đường ngoài LAN)"
overview: "Xem camera từ ngoài LAN bị kẹt ở 'connecting' vì **cả hai đầu WebRTC đều `new RTCPeerConnection` không có ICE server** → chỉ gather được host candidate, chạy trong LAN, chết trên 5G. Owner chốt **bỏ hẳn WebRTC** (không dựng STUN/TURN: TURN tốn băng thông VPS và đi ngược tinh thần self-hosted) và đẩy stream qua chính HTTPS 443 của Frontgate — đường HTTP vốn đã xuyên 5G vì APK Android load cả app từ `scout.namorix.online`. ✅ **Phase 1–6 code + build xong (2026-09-21, scout 0.11.0 → 0.12.0)**: `HlsPackager`/`HlsPackagerRegistry`/`HlsController` (fMP4 2s, ring buffer RAM, playlist cửa sổ trượt) + frontend `HlsStreamClient` (hls.js 1.7.3 nạp động, chờ `#EXTINF`, watchdog `no-frames`); xoá `WebRtcRelayService`/`H264RtpPacketizer`/`StreamsController`/`StreamDtos` + dep SIPSorcery + `RtcStreamClient`/`streams.controller`. Đổi lại: latency đồng nhất ~3–4s ở mọi nơi, kể cả trong LAN. **Nợ: chưa lần nào chạy camera Hikvision thật, chưa mở live view trên máy thật, chưa `curl` playlist bằng cookie phiên, chưa kiểm 4 cờ rule Frontgate** (mục 9). Batch `quality=sub` (DG-H6) **cố ý tách ra**, không nằm trong plan này."
todos: []
isProject: false
---

# Camera remote view — HLS qua Frontgate

## Trạng thái

🟡 **Phase 1–6 xong** (2026-09-21) — build sạch (0 error, 0 warning). Phase 2 + 3 đã verify offline bằng `ffprobe`/`ffmpeg` với stream H.264 tổng hợp; Phase 5 verify bằng `tsc`/`pnpm build`. **Chưa chạy với camera thật lần nào, và chưa mở live view trên máy thật lần nào.** 🟡 vì lý do đó, không phải vì còn code.

**Phase 4 đã rà soát xong** cùng ngày: rule `scout.namorix.online` **đã tồn tại sẵn** (APK Android trỏ vào), không phải tạo mới — nhưng phát hiện **3 chỗ bản plan đầu nói sai** (xem Phase 4).

**Phase 5 đã chốt lại hướng (owner quyết cùng ngày): bỏ hẳn WebRTC, không giữ làm đường LAN.** Nghĩa là plan này không còn "fallback" — HLS là đường duy nhất. Đường WebRTC đã bị xoá khỏi code (4 file backend + 2 file frontend, xem Phase 5). Hệ quả: **latency trong LAN cũng thành ~3–4s** như ngoài 5G; đổi lại chỉ còn một đường ống để bảo trì và không còn nhánh ICE nào quay vòng được nữa.

**Phase 6 xong** cùng ngày: scout `0.11.0 → 0.12.0` (`addon.json` + `frontend/package.json`), memory bank scout cập nhật, và tàn dư WebRTC trong docs đã dọn (kể cả một comment treo trong `HlsPackager.cs`). `minCoreVersion`/`minServerVersion` không đổi; namorix không đụng gì.

**⇒ Không còn phase nào trong plan này.** Việc còn lại chỉ là **owner chạy thật** (camera Hikvision + `curl` + 4 cờ Frontgate) và một batch **cố ý tách ra** `quality=sub` (DG-H6) — chi tiết ở **mục 9**.

Tiền đề: `01-camera-live-record.plan.md` (Phase 1–5 xong — lưu ý Phase 1–5 của plan đó mô tả đường live WebRTC LAN **đã bị xoá bởi plan này**), và tính năng chia sẻ camera nhiều người (đã archive ở `.claude/archive/01-scout-camera-sharing.plan.md`, scout 0.11.0).

---

## 1. Vấn đề — đã xác minh từ code, không phải phỏng đoán

⚠ Đọc mục này như **hồ sơ lịch sử**: các file `WebRtcRelayService.cs` / `RtcStreamClient.ts` được trích ở đây **đã bị xoá** ở Phase 5. Giữ nguyên mục này để thấy vì sao plan sinh ra, và để không ai dựng lại đường ICE mà không có STUN/TURN.

**Triệu chứng:** xem camera trong LAN (WiFi) thì chạy; ra 5G thì badge kẹt ở "connecting", ICE quay vòng, và endpoint ICE trả `{"success":true,"data":{"candidates":[]}}`.

**Nguyên nhân gốc — không có ICE server ở cả hai đầu:**

| Đầu | Vị trí | Nội dung |
|-----|--------|----------|
| Server | `backend/src/Streaming/WebRtcRelayService.cs:123` | `new RTCPeerConnection(null)` — không cấu hình gì |
| Client | `frontend/src/streaming/RtcStreamClient.ts:139` | `new RTCPeerConnection({ iceServers: [] })` |

Grep cả repo: `RtcStreamClient.ts:139` là chỗ **duy nhất** có `iceServers`, và nó rỗng. Không có `stun:`/`turn:` ở đâu.

Hệ quả: chỉ có host candidate (địa chỉ private của hai máy). Trong LAN thì định tuyến được; ra 5G thì một bên sau CGNAT nhà mạng, bên kia sau NAT nhà — không tồn tại cặp candidate nào chạy được, nên ICE `checking` mãi.

**Vì sao nó quay vòng vô hạn:** `RtcStreamClient.ts:212-215` hết `CONNECT_TIMEOUT_MS = 20_000` (`:17`) → `scheduleRetry` → backoff 1s→5s (`:289-305`) → `connect()` lại. Không có giới hạn số lần thử và không có nhánh thoát nào khác.

**`candidates: []` KHÔNG phải bằng chứng server không gather được candidate.** `DrainIce` **rút hàng** (`WebRtcRelayService.cs:273-279` — `TryDequeue`, one-shot), còn client poll mỗi 250 ms (`RtcStreamClient.ts:16`, `:228`). Poll đầu lấy hết host candidate, mọi poll sau trả rỗng. Trong LAN loop thoát sau 1–2 poll nên không ai thấy; trên 5G nó poll suốt 20 giây nên gần như mọi response nhìn thấy đều rỗng.

**Điểm mấu chốt — đường HTTP đã xuyên 5G rồi.** `frontend/capacitor.config.ts` đặt `server.url: "https://scout.namorix.online"`: app Android tải **toàn bộ UI** qua Frontgate trên 5G. Nghĩa là TLS/443 + domain + access policy chạy tốt ở ngoài mạng; chỉ đường media UDP là không có đường đi. Không cần mở port, không cần NAT config, không cần VPS.

---

## 2. Quyết định đã chốt (2026-09-21)

**Đẩy stream qua chính đường HTTPS 443 của Frontgate, dạng fMP4 segment + playlist `.m3u8`, phát bằng hls.js.**

**Cập nhật Phase 5 (2026-09-21) — owner chốt: bỏ hẳn WebRTC, không giữ làm đường LAN.** Quyết định đầu tiên ("giữ WebRTC ưu tiên cho LAN, hạ xuống fallback") đã bị thay. Lý do: giữ hai đường ống nghĩa là hai bộ phân quyền, hai đường lỗi, và một nhánh chuyển tiếp chỉ chạy được nếu có ai test nó — trong khi đường WebRTC **chưa bao giờ chạy được ngoài LAN** và trong LAN thì nó chỉ nhanh hơn. Đổi lại: latency đồng nhất ~3–4s ở mọi nơi. Code WebRTC đã **bị xoá sạch** (xem Phase 5), không phải để đó mà không dùng.

Đã cân nhắc và loại:
- **STUN/TURN (coturn trên VPS):** TURN relay toàn bộ media → tốn băng thông VPS, và thêm một VPS đi ngược tinh thần self-hosted của Namorix. Chỉ hợp lý nếu sau này cần latency <1s ngoài LAN.
- **Server tự quảng cáo public IP + port-forward UDP:** phụ thuộc port-forward của ISP, mà nhiều ISP VN đặt thuê bao gia đình sau CGNAT — rủi ro chết giữa đường. Và vẫn phải mở UDP ra Internet.

---

## 3. Sửa lại một giả định ở bước trước (ghi để không lặp lại)

Trong tin nhắn bàn phương án tôi đã loại fMP4/MSE với lý do "iPhone Safari không có `MediaSource`". **Sai cho repo này.**

Client mobile là **Capacitor Android** — có `frontend/android/`, có `@capacitor/android` trong `frontend/package.json`, và **không có `frontend/ios/`**. WebView Android là Chrome, nên **có** MSE. Cả hls.js lẫn fMP4-over-MSE đều chạy.

Điều này không đổi kết luận (vẫn chọn HLS vì có player sẵn thay vì tự viết plumbing MSE), nhưng **đổi lý do**: lý do đúng là "hls.js lo sẵn buffer/reconnect/playlist, không phải tự viết", không phải "MSE không có".

⚠ Nếu sau này có client iOS (Safari thật hoặc Capacitor iOS) thì câu chuyện MSE phải xét lại — nhưng đó là việc của lúc đó, không phải bây giờ.

---

## 4. Kiến trúc đích (tóm tắt)

```
IP camera (RTSP)  ──►  CameraRtspClient  ──►  H.264 NAL (FrameReceived)
                            │
                            └─► HlsPackager                        ← đường duy nhất, mọi mạng
                                     │  SharpMP4 FragmentedMp4Builder
                                     │  segment ~2s, giữ trong RAM (ring buffer)
                                     ▼
                              GET /api/.../live.m3u8  +  /seg{n}.m4s
                                     │
                                     ▼
                        Frontgate (YARP, 443, Http2, cache OFF)
                                     │
                                     ▼
                    scout.namorix.online → hls.js trong WebView
```

Module layout dự kiến (`backend/src/`):
```
Streaming/
  HlsPackager.cs              // per-camera, subscribe FrameReceived → fMP4 segment trong RAM
  HlsPackagerRegistry.cs      // 1 packager / camera, dùng chung cho mọi viewer
Controllers/
  HlsController.cs            // live.m3u8 + segment, phân quyền qua CameraService.GetAsync
```

Frontend: `streaming/HlsStreamClient.ts` (thay `RtcStreamClient.ts` đã xoá) + `hooks/useLiveStreams.ts` giữ map id → client.

---

## 5. Cổng quyết định (decision gates)

| # | Gate | Khi nào | Ảnh hưởng |
|---|------|---------|-----------|
| DG-H1 | LL-HLS đầy đủ (partial segment + blocking playlist reload) **vs** fMP4 + playlist trượt | trước Phase 2 | ✅ **Chốt: playlist trượt** — đã làm ở Phase 3. LL-HLS để sau nếu 3–4s là không đủ |
| DG-H2 | Phân quyền segment: cookie phiên same-origin **vs** signed URL | trước Phase 3 | ✅ **Chốt: cookie phiên** — `[RequireAuth]` trên `HlsController`, chưa test bằng cookie thật (việc của owner ở Phase 3/5). Chỉ phải quay lại nếu hoá ra cookie addon không same-origin |
| DG-H3 | Mô hình quyền khi xem | trước Phase 3 | ✅ **Chốt: giữ nguyên** — mọi request gọi `cameras.GetAsync(id, CurrentUserId, ct)`, không tự viết lại logic quyền |
| DG-H4 | Audio từ camera (AAC/G711) | trước Phase 2 | Vẫn là G2 treo từ plan recording. Chưa có nhánh audio ở đâu cả → **video-only trước** (Phase 2–3 làm video-only) |
| DG-H5 | Packager dùng chung **vs** per-viewer | trước Phase 2 | ✅ **Chốt: dùng chung** — `HlsPackagerRegistry` giữ đúng một `HlsPackager` / camera, N viewer đọc cùng buffer. Từ Phase 5 `FrameReceived` **chỉ còn một subscriber** (WebRTC đã xoá), nên gate này hết chỗ để chọn sai |
| DG-H6 | Main stream **vs** sub stream cho đường remote | trước Phase 4 | Spike Phase 1 của plan recording đo **~3900–4500 kbps** ở main. Đi 5G tốn. `StreamType` hiện chỉ là metadata, không đủ. **Đã chốt hướng:** thêm field `Camera.SubStreamRtspUrl` (optional), request chọn qua tham số `quality=main|sub`, registry key `(CameraId, StreamType)` — không thêm Camera row mới. ⚠ **Chưa làm** — tách thành batch riêng, xem mục 9 |

### DG-H6 — hệ quả kéo theo (không phải tuỳ chọn)

🔴 **Chưa làm.** Phase 3 đã xong nhưng `HlsPackagerRegistry` khoá theo **camera id đơn**, đúng như thiết kế hiện tại của ingest. Khi làm `quality=sub` thì đây là việc của **một batch riêng**, không phải một tham số thêm vào controller — nó đụng vào đường ingest đang chạy và cần một migration, nên không gộp vào batch HLS đã verify offline được.

`quality=main|sub` không chỉ là một tham số request. Ingest hiện **chỉ pull một URL mỗi camera**, nên sub-stream không tự xuất hiện — 4 chỗ phải sửa cùng lúc:

| # | Chỗ phải sửa | Vì sao |
|---|--------------|--------|
| 1 | `RtspIngestService.cs:22` — `_clients` là `Dictionary<Guid, IngestEntry>`, khoá theo camera | Sub-stream cần **2 client / camera** → đổi khoá thành `(cameraId, streamType)` |
| 2 | `RtspIngestService.cs:143-144` — `Signature` chỉ gồm `RtspUrl` + `RtspCredentials` | Không thêm URL sub vào signature thì **đổi URL sub không kích hoạt reload** — client giữ stream cũ tới lúc restart. Kiểu bug im lặng, không log, không lỗi |
| 3 | `RtspIngestService.cs:24-30` — `GetActiveClient(cameraId)` trả về **một** client | ✏️ **Nhẹ hơn bản đầu:** chỗ gọi thứ hai (`WebRtcRelayService.cs:23`) **đã bị xoá ở Phase 5**, nên từ giờ chỉ còn `HlsPackagerRegistry` gọi hàm này — sửa một chỗ là đủ, không còn nguy cơ lấy nhầm client sub |
| 4 | `CameraService.cs:277-296` — `SplitRtspUrl` tách userinfo khỏi `RtspUrl` | `SubStreamRtspUrl` phải đi qua đúng hàm này, nếu không userinfo trong URL sub **rò ra DTO** (`CameraService.cs:335` trả `owner ? camera.RtspUrl : null`) |

Kéo theo cả CRUD (`CameraUpsertRequest`, `ScCameraDto`) và một migration cho cột mới.

**Cập nhật 2026-09-21:** dự tính ban đầu là làm chung lượt với Phase 3, nhưng Phase 3 đã xong mà **không** gộp — vì cả 4 chỗ trên đều nằm trên đường ingest đang chạy và cần migration, không verify được nếu không có camera thật. Chuyển thành **batch riêng**, xem mục 9.

---

## 6. Tổng quan các phase

| Phase | Tên | Scope | Kết thúc bằng | Version | Trạng thái |
|-------|-----|-------|---------------|---------|-----------|
| **1** | Giới hạn retry + thông báo rõ | frontend | hết quay vô hạn, hiện lý do thật | — | ✅ xong, build sạch |
| **2** | HLS packager spike | backend | đọc lại được segment bằng ffprobe/VLC | — | ✅ xong, verify offline; camera thật còn nợ |
| **3** | HLS endpoints + phân quyền | backend | `curl` lấy được playlist + segment | — | ✅ xong, playlist verify offline; `curl` còn nợ |
| **4** | Frontgate rule cho `scout.namorix.online` | config | xem được từ 5G bằng `curl`/browser | — | 🟡 đã rà soát xong; rule **đã tồn tại sẵn** (APK đang trỏ vào) — còn owner kiểm 4 cờ |
| **5** | Frontend hls.js, xoá đường WebRTC | frontend + backend | xem được trên app Android qua 5G | 0.12.0 | ✅ xong, build sạch; **chưa mở trên máy thật** |
| **6** | Docs + version | — | memory bank + bump | 0.12.0 | ✅ xong |

Bump theo rule của skill update-docs-and-versions: chỉ bump khi hành vi nhìn thấy được đổi. Phase 1 + 5 nằm cùng một batch chưa phát hành → **một bump duy nhất 0.11.0 → 0.12.0**, không bump hai lần.

---

## Phase 1 — Giới hạn retry + thông báo rõ — frontend

**Mục đích:** chặn triệu chứng ngay, độc lập hoàn toàn với HLS. Hiện tại người dùng ngoài LAN nhìn thấy badge "connecting" quay mãi và không có manh mối nào.

**Files:**
- `frontend/src/streaming/RtcStreamClient.ts`
  - `scheduleRetry` (`:289-305`) đếm số lần đã thử; quá N lần (đề xuất 3) → dừng hẳn, `onState("offline", "unreachable")` thay vì hẹn retry tiếp.
  - Hiện `attempt` đã có sẵn và đã dùng cho backoff (`:296`) nhưng **không bao giờ được dùng để dừng** — chỉ cần thêm ngưỡng.
- `frontend/src/streaming/*` + i18n: thêm nhánh dịch cho reason `"unreachable"`, nội dung kiểu "Không kết nối được từ ngoài mạng nội bộ" — nói đúng bản chất, không nói "lỗi mạng chung chung".
- `LiveView` / chỗ render badge: cho người dùng bấm thử lại thay vì tự quay.

**Đã làm (2026-09-21):**
- `RtcStreamClient.ts` — thêm `MAX_ATTEMPTS = 3`; `scheduleRetry` dừng hẳn khi `attempt >= MAX_ATTEMPTS` và báo `onState("offline", reason)`.
- ⚠ **Khác plan một chỗ:** plan viết một reason chung `"unreachable"`, code dùng **5 reason cụ thể** đã có sẵn trong `RtcStreamClient` (`stream-offline`, `connection-lost`, `negotiation-failed`, `timeout`, `no-frames`). Lý do: `scheduleRetry` vốn đã nhận `reason` từ chỗ gọi, gộp lại thành một câu chung là ném đi thông tin đã có — "không mở được đường video" và "mất kết nối giữa chừng" là hai chuyện khác nhau với người đang xem.
- `CameraLiveCard.tsx` — bảng `REASON_KEYS` map reason → khoá i18n, render trong overlay cùng badge; chỉ hiện khi **không** còn `connecting` (lúc đó spinner đã là toàn bộ thông điệp).
- `en.json` — thêm nhánh `scout.live.reason.*` (5 khoá). `vi.json` là `{}` nên không thêm gì.
- `LiveView.scss` — `.scout-live-card__notice` + `__reason`.
- Nút Play đã là retry thủ công sẵn (`useLiveStreams.play()` dispose rồi tạo client mới khi không phải paused) → **không phải sửa gì**.
- Verify: `tsc --noEmit` sạch (chỉ còn TS5101 `baseUrl` có từ trước), `pnpm build` chạy được. **Chưa test trên trình duyệt/điện thoại.**

**Verify (owner):** trong LAN vẫn chạy như cũ; tắt WiFi dùng 4G/5G → sau ~3 lần thử hiện thông báo rõ và **dừng**, không quay nữa.

---

## Phase 2 — HLS packager spike — backend

**Mục đích:** chứng minh ghi được fMP4 segment từ NAL có sẵn, trước khi đụng tới HTTP/Frontgate. Đây là chân ít tiền lệ nhất.

**Files:**
- `backend/src/Namorix.Scout.csproj` — thêm **SharpMP4**. Hiện csproj chỉ có SharpRTSP 1.11.1, SIPSorcery 10.0.16, EF Core Sqlite 10.0.9, JWT 8.19.1 — **chưa có SharpMP4**.
- `backend/src/Streaming/HlsPackager.cs`:
  - Subscribe `camera.FrameReceived` — đúng chỗ `RtcViewerSession` đang dùng (`WebRtcRelayService.cs:158` subscribe, `:360` unsubscribe). Đây là điểm vào duy nhất, không cần đụng ingest.
  - Dùng `FragmentedMp4Builder` giống plan recording (`.claude/plans/01-camera-live-record.plan.md:201`). Plan ban đầu định **fragment ~1s** cho live; **đã làm ở 2s** — vì SharpMP4 cắt theo thời gian chứ không theo keyframe (xem ghi chú dưới), nên con số này phải bằng I-frame interval của camera chứ không chọn tuỳ ý.
  - Segment ghi vào **RAM** (ring buffer), **không đụng đĩa**: tránh mòn SSD khi chạy 24/7, và tách hẳn khỏi đường recording.
  - Bám `VideoFrame { Nals, RtpTimestamp, IsKeyFrame }` (`Streaming/VideoFrames.cs`). Segment phải **bắt đầu bằng keyframe** — nếu không mỗi segment là một đoạn hỏng.
- Codec: pipeline ingest **chỉ có H.264** (`CameraRtspClient.cs:252` dựng `H264Depacketizer`, `:614`/`:617` bắt NAL 7/8 SPS/PPS, `:110` trả `H264CodecSnapshot`), và camera thật (Hikvision) đã cho H.264 High L4.0 ở spike Phase 1. **Không cần chạy `ffprobe`** — đường WebRTC trong LAN đang chạy được chính là bằng chứng camera ra H.264 mà pipeline này parse được.

**Đã làm (2026-09-21):**
- `Namorix.Scout.csproj` — thêm `SharpMP4` **0.3.1** (kéo theo SharpH264/SharpISOBMFF/SharpMP4Common cùng bản). csproj giờ có 6 PackageReference.
- `backend/src/Streaming/HlsPackager.cs` — `PushFrame(VideoFrame)` + `InitSegment` + `Segments`, ring buffer 8 segment trong RAM. Packager **không tự subscribe**; chủ sở hữu quyết định vòng đời, đúng kiểu `WebRtcRelayService` (`:158`).

**API SharpMP4 — đọc từ source ở đúng commit NuGet (`jimm98y/SharpMP4@49340e1`), không đoán:**
- Gọi `ProcessTrackSample(trackID, nal, -1)` **từng NAL một**. `ProcessRawSample` là cho sample AVCC đã mux, không phải input của mình.
- NAL đưa vào **không được có start code Annex-B** — track ném `ArgumentException("NAL unit must not have Annex-B prefix!")`. `CameraRtspClient` đã trả NAL trần nên khớp sẵn.
- Track tự gom NAL thành access unit bằng slice header, nên SPS/PPS đi riêng một frame vẫn mở AU đúng.
- `FragmentedMp4Builder(output, maxFragmentLengthInMs, durationInMs = 0, appendMovieFragmentRandomAccessBox = true)` — README ghi 2 tham số là nhờ default; live phải truyền `durationInMs: 0, append: false`.
- `FragmentedBlobOutput.OnFragmentReady` bắn `{ SequenceNumber, Data }`, **sequence 0 chính là init segment** (`moov`), segment media bắt đầu từ 1.
- `new H264Track()` **không tham số** tự đọc frame rate từ VUI của SPS (`Timescale` / `DefaultSampleDuration`). Không hardcode 90000/6000 như harness đầu tiên — làm vậy là sai với camera 25fps.

⚠ **Cắt fragment theo thời gian, không theo keyframe.** SharpMP4 cắt ở mốc `maxFragmentLengthInMs`, không quan tâm IDR nằm đâu. Hệ quả: `SegmentMilliseconds` **phải bằng I-frame interval của camera**. Khi lệch, ffmpeg **không báo lỗi** — nó lặng lẽ bỏ các frame đầu không có reference, nên nhìn qua tưởng vẫn chạy. Vì vậy packager có cảnh báo một lần `HLS segment started on a non-keyframe` khi boundary rơi vào frame không phải keyframe; đã test: bắn đúng khi đặt 1500ms với GOP 1s, im khi đặt 2000ms.

**Verify đã làm (offline, không phải camera thật):** harness ngoài repo (`/tmp/hlsharness`, `ProjectReference` vào `Namorix.Scout.csproj`) nạp stream H.264 do ffmpeg sinh:
- 10s → `init.mp4` 663 B + 4 segment; `cat init.mp4 segN.m4s | ffprobe` giải mã **độc lập từng segment, frame đầu là `I`**, 30 frame @ 2.000s, pts liền mạch 0/2/4/6.
- 40s → 19 fragment sinh ra, chỉ giữ lại 8 (seq 12–19) → ring buffer đúng.
- **Gate SPS/PPS in-band: ĐẠT.** Stream test có `AUD,SPS,PPS,SEI,IDR` trước mỗi IDR — đúng hình dạng Hikvision — và SharpMP4 ghép được, không phải inject gì thêm.

**Còn nợ (owner):** chạy với camera thật, xem log có cảnh báo non-keyframe không, và đo I-frame interval thật. Nếu SPS của camera **không có VUI timing** thì track rơi về fallback 24000/1001 (≈23.976fps) → sai thời lượng segment, cũng lộ ra qua chính cảnh báo đó.

**Verify (owner):** chạy vài giây, ghi segment ra file tạm, `ffprobe` / mở bằng VLC thấy hình đúng. Đo thêm I-frame interval thật của camera (`ffprobe -show_frames` xem khoảng cách giữa các `pict_type=I`) — con số này cộng vào độ trễ cold-start ở Phase 5.

---

## Phase 3 — HLS endpoints + phân quyền — backend

**Files:**
- `backend/src/Streaming/HlsPackagerRegistry.cs` — một packager / camera, tham chiếu đếm; ngừng khi hết viewer **và** không cần cho recording.
- ⚠ Nếu làm `quality=sub`: đọc mục **"DG-H6 — hệ quả kéo theo"** (mục 5) trước. Phải sửa `RtspIngestService` (khoá đôi + signature) và thêm cột + `SplitRtspUrl` cho `SubStreamRtspUrl` — không chỉ là thêm tham số request.
- `backend/src/Controllers/HlsController.cs`:
  - `GET /api/cameras/{id}/live.m3u8` — playlist cửa sổ trượt.
  - `GET /api/cameras/{id}/seg{n}.m4s` — segment (kèm `init.mp4` nếu cần).
  - Phân quyền **y như** `StreamsController.cs:25`: `cameras.GetAsync(cameraId, CurrentUserId, ct)` → `null` thì 404. Không tự viết lại logic quyền.
  - Header quan trọng: `.m3u8` phải `Cache-Control: no-store`; segment `immutable` được vì tên đã đánh số (xem DG-H6/rủi ro cache bên dưới).

**Đã làm (2026-09-21):**
- `backend/src/Streaming/HlsPackagerRegistry.cs` — một `HlsPackager` / camera, tạo khi có request đầu tiên, gắn vào client hiện tại của `RtspIngestService` qua `FrameReceived += packager.PushFrame`.
- `backend/src/Controllers/HlsController.cs` — `[RequireAuth]`, route `api/cameras`: `{id}/live.m3u8`, `{id}/init.mp4`, `{id}/seg{n}.m4s`.
- `Program.cs` — `AddSingleton<HlsPackagerRegistry>()`.

**Khác plan ba chỗ (có chủ đích):**
1. **Không đếm tham chiếu** — dùng mốc `LastUsed` + quét dọn sau 60s không ai đụng. Với HTTP polling thì hai cách cho cùng kết quả (mỗi lần lấy playlist/segment đều "đụng"), mà bỏ được sổ sách acquire/release. Viewer đóng app → packager còn sống thêm 60s.
2. **Segment để `no-store`, không `immutable`** như plan. Số thứ tự **đếm lại từ đầu** mỗi khi packager được dựng lại (camera đổi cấu hình, kết nối ingest dựng lại), nên `seg12.m4s` hôm nay và mai có thể là hai đoạn video khác nhau — URI đánh số ở đây **không** bất biến.
3. `EXT-X-TARGETDURATION` = `SegmentMilliseconds/1000 + 1` = 3. Camera 29.97fps không cắt được đúng mốc 2.000s, mà spec bắt target phải phủ segment dài nhất; đổi lại nhịp reload của hls.js thành 1.5s thay vì 1s.

Thêm: registry **dựng lại packager khi `GetActiveClient` trả về object khác** — timeline của packager cũ thuộc về stream đã kết thúc, giữ lại là ghép frame mới vào một fragment đã phát đi. Thu hồi quyền share **không cần làm gì** ở đây: request playlist kế tiếp 404, khác WebRTC (phải đóng peer đang chạy).

**Verify đã làm (offline):** playlist sinh bằng chính `HlsController.BuildPlaylist` (gọi qua reflection, không chép lại code) + segment thật → `ffprobe -f hls` đọc ra `h264 640x360`, **240 frame qua 8 segment (16.0s)**, tức playlist + `EXT-X-MAP` + segment ghép thành stream phát được. Lưu ý: playlist live không có `#EXT-X-ENDLIST` nên ffprobe/ffmpeg không tự kết thúc — muốn đo hết thì phải thêm `#EXT-X-PLAYLIST-TYPE:VOD` + `#EXT-X-ENDLIST` vào bản copy.

⚠ **Chưa chắc:** request **đầu tiên** trả playlist rỗng (0 dòng `#EXTINF`) vì chính nó là cái khởi động packager. ffmpeg chấp nhận; hls.js có coi playlist live rỗng là lỗi fatal hay không thì Phase 5 mới biết. Nếu có thì phải xử ở đó.

**Verify (owner):** `curl` playlist + segment với cookie phiên; thử với user không có quyền → 404. Nhớ lần gọi đầu tiên trả playlist rỗng, đợi ~4s rồi gọi lại mới thấy segment.

---

## Phase 4 — Frontgate rule cho `scout.namorix.online` — config — ✅ đã rà soát (2026-09-21)

**Không phải code, và cũng không phải "tạo rule mới".** `frontend/capacitor.config.ts:8` trỏ `server.url: "https://scout.namorix.online"` → APK Android đang load cả app từ host đó, tức **rule này đã tồn tại và đang chạy**. Việc của Phase 4 là **soi lại cờ của rule đang chạy**, không phải dựng lại từ đầu.

Rule nằm trong DB (`FgReverseProxyRules`, `AppDbContext.cs:29`), sửa qua UI Frontgate hoặc `POST/PUT /api/frontgate/reverse-proxy` (`ReverseProxyController.cs`, `[RequireAdmin]`). Không có default/seed/appsettings nào chứa rule → **trong code không có gì để sửa**.

| Cờ | Giá trị đúng | Vì sao |
|----|--------------|--------|
| `Source` | `scout.namorix.online` | khớp `capacitor.config.ts:8` |
| `DestinationScheme` / `Host` / `Port` | `http` / addon / **5300** | `addon.json:15-22` khai entry port 5300 |
| `Status` | `Active` | `FrontgateProxyConfigProvider.cs:39-43` chỉ nạp rule `Active` |
| `Access` | **`Public`** | ⚠ Mặc định của entity là `Private` (`FgReverseProxyRule.cs:33`), và `Private` = chỉ loopback/RFC1918 (`FrontgateAccessService.cs:29-30`, `:68-69`) → đi 5G ăn **403**. Đúng kiểu hỏng mà plan này sinh ra để sửa |
| `CacheAssets` | **TẮT** (mặc định đã tắt, `:38`) | transform set `Cache-Control: public, max-age=86400` (`FrontgateProxyConfigProvider.cs:86-93`) |
| `WebSocketsSupport` | **BẬT** (mặc định đã bật, `:37`) | ✏️ **Sửa lại so với bản plan đầu** — xem bên dưới |
| `Http2Support` | bật cũng được, nhưng **không phải thứ lo song song segment** | ✏️ **Sửa lại** — xem bên dưới |
| `ForceSsl` / `CertificateId` | giữ nguyên cấu hình đang chạy | APK đang dùng `https://` → cert đã có và đang hoạt động |

### ✏️ Ba chỗ bản plan đầu nói sai

**1. `CacheAssets` — `no-store` của addon KHÔNG cứu được.** Transform `ResponseHeader` + `Set` là **ghi đè** header trên đường response về, không phải chỉ điền khi thiếu. Nên `Cache-Control: no-store` mà `HlsController` đặt ở Phase 3 **bị ghi đè** thành `public, max-age=86400` nếu cờ này bật. Kết luận: Phase 4 **bắt buộc**, không phải "phòng xa" như mục 7 viết.

**2. `WebSocketsSupport` — cần, không phải "chưa cần".** Bản đầu kết luận "HLS thuần HTTP nên không đụng" — đúng cho HLS nhưng **sai cho cả host**: scout có SignalR hub ở `/hubs/scout` (`Constants/ScoutSignalR.cs:6`, `Program.cs:20-24`), frontend nối bằng `withUrl(...)` **không set transport** (`packages/core/src/signalr/signalr.service.ts:82-83`) → mặc định thử WebSocket trước. Tắt cờ là `BlockWebSocketMiddleware` trả **426**, client rơi xuống SSE/long-polling — vẫn chạy nhưng chậm hơn và giữ connection lâu hơn, đúng thứ không nên làm trên 5G.

**3. `Http2Support` không phải thứ gỡ nghẽn segment.** Cờ này chỉ đặt `ForwarderRequestConfig.Version` — tức hop **proxy → addon** (`FrontgateProxyConfigProvider.cs:62-66`), không phải hop **browser → proxy**. HTTP/2 phía browser đã bật sẵn ở tầng Kestrel cho port HTTPS: `Program.cs:69-71` dùng `HttpProtocols.Http1AndHttp2` + ALPN `h2`. Thêm nữa destination ở đây là `http://` nên `VersionPolicy = RequestVersionOrLower` khó có tác dụng (h2c cleartext không thương lượng qua ALPN). Cứ bật cho khớp, nhưng đừng trông vào nó.

**Verify (owner):**

```bash
# 1. Xem cờ hiện tại của rule (cookie phiên admin)
curl -s 'https://<desktop-api>/api/frontgate/reverse-proxy?page=1&size=50' -b cookie.txt \
  | jq '.data[] | select(.source=="scout.namorix.online")
        | {source,access,status,cacheAssets,http2Support,webSocketsSupport,forceSsl}'
# mong đợi: access="public", status="active", cacheAssets=false, webSocketsSupport=true

# 2. Playlist KHÔNG bị cache — phải KHÔNG thấy max-age=86400
curl -sI 'https://scout.namorix.online/api/cameras/<id>/live.m3u8' -b cookie.txt | grep -i cache-control

# 3. WebSocket cho SignalR không bị chặn — 426 là hỏng, khác 426 là đi được
curl -si -o /dev/null -w '%{http_code}\n' 'https://scout.namorix.online/hubs/scout?id=probe' \
  -H 'Connection: Upgrade' -H 'Upgrade: websocket' \
  -H 'Sec-WebSocket-Version: 13' -H 'Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ=='
```

Ghi chú về destination: addon chạy `NetworkMode = "host"` (`DockerService.cs:83`), nên chỉ reachable ở `127.0.0.1:5300` **khi desktop cũng chạy trên host**. Dưới `docker-compose.yml` desktop nằm trong bridge network → `127.0.0.1` bên trong container là loopback của chính nó, phải dùng IP của docker host. Rule hiện tại đang chạy được nên destination đã đúng; chỉ cần lưu ý nếu dựng lại.

---

## Phase 5 — Frontend hls.js, xoá đường WebRTC — frontend + backend — ✅ xong (2026-09-21)

**Files (dự kiến ban đầu — 3/4 gạch đầu dòng còn đúng, gạch thứ 3 đã bỏ):**
- `frontend/package.json` — thêm `hls.js`. (WebView Android không có HLS native; hls.js chạy qua MSE.) ✅
- `frontend/src/streaming/` — bật `xhrSetup` set `withCredentials = true`; thiếu dòng này thì mọi request playlist/segment 404 dù backend phân quyền đúng. ✅
- ~~thêm lớp điều phối: thử WebRTC trước, ICE fail → chuyển sang HLS~~ ❌ **không làm** — WebRTC đã bị xoá, không còn gì để điều phối.
- `LiveView` — hiện badge nguồn đang phát ❌ **không làm** — chỉ còn một nguồn thì badge vô nghĩa.

### Đã làm

**Frontend**
- `frontend/package.json` — thêm `hls.js` **1.7.3** (exact).
- **`streaming/HlsStreamClient.ts` (mới, thay `RtcStreamClient.ts`)** — vòng đời `attach/detach/start/stop/dispose`, báo state `idle|connecting|live|offline` + `reason`.
  - hls.js nạp bằng `await import("hls.js")` **trong** `createPlayer`, không import tĩnh ở đầu file. Lý do: thư viện ~576 kB, và `HlsStreamClient` nằm trong chunk chính của addon — import tĩnh làm chunk chính phình từ ~193 kB lên ~769 kB. Tách ra thì hls.js chỉ tải khi thực sự mở live view.
  - Vì vậy các import ở đầu file là **type-only** (`import type Hls from "hls.js"`), còn `Events`/`ErrorTypes` lấy từ object module lúc runtime.
  - `xhrSetup` đặt `withCredentials = true` để cookie phiên đi cùng request segment.
- **`streaming/HlsStreamClient.ts` — chờ playlist có segment trước khi giao cho hls.js.** Request playlist đầu tiên **chính là cái dựng packager** (Phase 3), nên nó trả playlist rỗng — và hls.js coi playlist live rỗng là `LEVEL_EMPTY_ERROR`, chỉ thử lại ~2 lần (~3s) rồi **fatal**. Thay vì trông vào retry của thư viện, client tự `fetch` playlist theo nhịp 1s tới khi thấy dòng `#EXTINF` (tối đa 20s) rồi mới `loadSource`. Response **không** OK thì fail ngay (`stream-offline`) — camera hỏng thật phải báo liền, không ngồi chờ 20s.
- **`streaming/HlsStreamClient.ts` — watchdog đứng hình.** `FRAG_LOADED` bắn liên tục kể cả khi camera đã ngừng đẩy frame (hls.js vẫn tải segment cũ), nên "có kết nối" không bằng "có hình". Cứ 2s so `video.currentTime`; không nhích trong 12s → `offline`/`no-frames`. Giữ đúng hành vi mà bản WebRTC cũ đã có.
- **`hooks/useLiveStreams.ts`** — thay `RtcStreamClient` bằng `HlsStreamClient`; thêm `videosRef` (map `cameraId → <video>`) vì card đăng ký thẻ `<video>` trong cùng commit chạy effect của hook này. `play()` giờ chỉ gọi `client.start()` trên client đang có, **không** dispose-rồi-tạo-lại (cách cũ làm mất thẻ `<video>` đã gắn).
- **`views/live/StreamVideo.tsx`** — bỏ prop `stream`/`srcObject`, nhận `videoRef` từ cha; effect resume chỉ còn kiểm `document.visibilityState`.
- **`views/live/CameraLiveCard.tsx`** — thêm `onAttachVideo` (callback ref, để hook kịp giữ thẻ `<video>`); `REASON_KEYS` map 5 reason mới → khoá i18n.
- **`i18n/locales/en.json`** — thêm `scout.live.reason.{streamOffline,timeout,hlsNetwork,hlsMedia,noFrames}`. `vi.json` là `{}` nên không thêm gì.
- **`scoutApiRoutes.ts`** — bỏ `STREAMS_BASE` + nhóm `streams{}`; thêm `cameraLive(id)`.

**Backend — xoá hẳn đường WebRTC (owner chốt, không để lại mà không dùng)**
- Xoá: `Streaming/WebRtcRelayService.cs`, `Streaming/H264RtpPacketizer.cs`, `Controllers/StreamsController.cs`, `Dtos/StreamDtos.cs`.
- `Namorix.Scout.csproj` — bỏ `SIPSorcery 10.0.16` (không còn ai dùng).
- `Program.cs` — bỏ `AddSingleton<WebRtcRelayService>()`. `AddSingleton<HlsPackagerRegistry>()` giữ nguyên.
- `Services/CameraService.cs` — bỏ tham số `WebRtcRelayService relay` và lời gọi `CloseViewerSessionsAsync` khi thu hồi share. Từ Phase 3 việc này đã **không cần**: request playlist kế tiếp không tìm thấy share nên 404, khác WebRTC (phải chủ động đóng peer đang chạy).
- `Streaming/CameraRtspClient.cs` — bỏ `_codec`, vòng quét SPS(7)/PPS(8), class `CodecState` và `GetCodec()`; chỉ còn `FrameReceived` cho `HlsPackager`.
- `Constants/Error.cs` — bỏ `StreamOfferFailed`, `StreamAnswerFailed`, `InvalidStreamInput`. Giữ `StreamNotFound` (còn dùng ở `HlsController.cs:77`).

**Verify đã làm:** `tsc --noEmit` sạch (chỉ còn TS5101 `baseUrl` có từ trước), `pnpm build` xong với chunk tách đúng ý (chunk chính 193 kB + `hls-*.js` 576 kB riêng), `dotnet build` 0 error 0 warning, và `grep` toàn repo không còn `WebRtc|SIPSorcery|RTCPeerConnection` trong source (chỉ còn trong file docs — xem mục 9).

**Verify (owner):** mở live view trên app Android qua 5G → thấy hình sau [chờ playlist ~3–4s] + [chờ keyframe đầu, đo ở Phase 2] cho lần mở đầu; các lần mở sau (packager đã chạy sẵn) chỉ còn ~3–4s. Trong LAN giờ **cũng** ~3–4s (không còn đường nhanh). Rút mạng camera giữa lúc xem → sau ~12s hiện lý do `no-frames`, không đứng im.


---

## Phase 6 — Docs + version — ✅ xong (2026-09-21)

- `progress.md` + `activeContext.md` của scout (lưu ý `.claude/memory/` **bị gitignore** — `memory/` dòng 12 — nên docs scout không vào commit). ✅
- ⚠ **Dọn tàn dư WebRTC trong docs** — `.claude/CLAUDE.md` và `.claude/memory/{projectbrief,techContext,systemPatterns}.md` đã sửa; các mục **lịch sử** trong `progress.md`/`activeContext.md` (v0.4.0, v0.5.0, v0.8.0, v0.11.0) **cố ý giữ nguyên** vì chúng mô tả đúng thứ có thật lúc viết. Kèm một comment treo `HlsPackager.cs:66` trỏ `WebRtcRelayService` đã xoá. ✅
- `backend/README.md` **của namorix** — **không đụng**: không thêm RPC nào, và scout không có README. ✅
- Bump scout `0.11.0 → 0.12.0` (`addon.json` + `frontend/package.json`). `minCoreVersion` / `minServerVersion` **không đổi** — không dùng API Core mới. ✅

---

## 7. Rủi ro

| Rủi ro | Vì sao | Xử lý |
|--------|--------|-------|
| **Cache đánh lừa** | `CacheAssets` bật sẽ cache `.m3u8` 24h — nhìn như "stream đứng" | ⚠ **`no-store` của addon không cứu được:** transform của YARP chạy sau và **ghi đè** header, xem Phase 4. Nên việc tắt `CacheAssets` là **bắt buộc**, không phải phòng xa |
| **Băng thông 5G** | Main stream đo được 3900–4500 kbps | DG-H6; nếu main quá nặng thì dùng sub-stream — thêm field `SubStreamRtspUrl` optional trên `Camera` (không tạo Camera row mới), Stream registry chọn URL theo tham số `quality` của request. **Chưa làm** |
| **Mòn CPU NUC** | Mỗi camera mux fMP4 liên tục kể cả khi không ai xem | Packager chỉ chạy khi có viewer. **Đã làm khác plan:** không refcount mà dùng `LastUsed` + sweep 60s (`HlsPackagerRegistry.cs`) — refcount sai khi player reload playlist mỗi ~2s mà không có tín hiệu "ngừng xem" rõ ràng, còn hết hạn theo thời gian thì không cần tín hiệu đó. Hệ quả: packager sống thêm tối đa 60s sau khi viewer cuối rời đi |
| **Trễ 3–4s** | Bản chất playlist trượt | Chấp nhận cho "ngó xem nhà"; LL-HLS là bước nâng cấp sau (DG-H1) |
| **Mất đường latency thấp trong LAN** | ~~WebRTC vẫn ưu tiên, chỉ fallback khi ICE fail~~ — Phase 5 đã **xoá WebRTC**, nên ở nhà cũng ~3–4s thay vì <500ms | Đã chấp nhận có chủ đích (mục 2). Nếu sau này cần lại: dựng lại đường WebRTC **kèm STUN/TURN**, đừng dựng lại bản không ICE server — đó đúng là thứ plan này sinh ra để bỏ |
| **Chưa chạy với camera thật** | Phase 1–5 đã xong và build sạch, nhưng **chưa lần nào chạy với camera Hikvision thật**, và Phase 5 **chưa mở live view trên máy thật lần nào** — Phase 2–3 mới chỉ verify offline bằng stream H.264 tổng hợp (ffmpeg) đẩy qua chính code đã ship, Phase 5 chỉ `tsc` + `pnpm build` | Việc còn nợ của owner, xem mục 9 |
| **Docs còn mô tả WebRTC** | Xoá code mà không xoá docs thì lần sau có người dựng lại đường ICE tưởng nó vẫn tồn tại | ✅ **Đã xử ở Phase 6.** Các mục *lịch sử* trong `progress.md`/`activeContext.md` vẫn nhắc WebRTC — cố ý, chúng là bản ghi theo ngày |

---

## 8. Không nằm trong plan này

- STUN/TURN, mở UDP, coturn — đã loại có chủ đích ở mục 2.
- Recording / timeline / playback — thuộc `01-camera-live-record.plan.md` Phase 6–8. Plan này **chỉ chia sẻ** thư viện SharpMP4, không chia sẻ vòng đời (recording chạy nền, HLS chỉ khi có viewer).
- Audio — treo ở DG-H4, giống G2 của plan recording.

---

## 9. Việc còn nợ

### Owner phải chạy (không verify offline được)

| Phase | Việc | Cách kiểm |
|-------|------|-----------|
| 2 | Xác nhận không có cảnh báo cắt segment giữa GOP với camera thật | Chạy với camera Hikvision, grep log `HLS segment started on a non-keyframe`. **Nếu có cảnh báo này thì phải sửa `SegmentMilliseconds` cho bằng đúng chu kỳ I-frame của camera** |
| 2 | Đo chu kỳ I-frame thật | ffprobe trên stream, hoặc xem cấu hình camera. Giá trị này là `HlsPackager.SegmentMilliseconds` (đang để 2000) |
| 2 | Xác nhận SPS có VUI timing | Nếu SPS thiếu timing, `H264Track` rơi về `24000/1001` và độ dài segment sai lệch. Kiểm bằng log/ffprobe `r_frame_rate` của `init.mp4` |
| 3 | Playlist + segment qua HTTP thật | `curl` với cookie phiên; user không có quyền → 404. Lần gọi đầu trả playlist rỗng, đợi ~4s gọi lại |
| 5 | Live view chạy end-to-end trên app Android | **Chưa mở lần nào.** Mở qua 5G → thấy hình; cũng thử trong LAN. Ghi lại thời gian từ lúc bấm Play tới frame đầu — đây là con số duy nhất biết được độ trễ thật |
| 5 | Watchdog `no-frames` bắn đúng | Rút mạng camera giữa lúc đang xem → sau ~12s phải hiện lý do, không đứng im |

### Batch tách ra — DG-H6 (`quality=sub`)

Chưa làm, **cố ý**. Lý do: cần thêm `SubStreamRtspUrl` + migration + CRUD + `SplitRtspUrl`, đổi `RtspIngestService._clients` sang khoá kép, và sửa `Signature` — tức đụng vào đường ingest đang chạy, **không verify được nếu không có camera thật**. Làm thành batch riêng sau Phase 6.

✏️ Nhẹ hơn bản đầu một chút: chỗ gọi `GetActiveClient` thứ hai nằm trong `WebRtcRelayService` đã bị xoá ở Phase 5, nên từ đây chỉ còn `HlsPackagerRegistry` dùng hàm đó.

### Phase 4 — owner kiểm cờ

Rule `scout.namorix.online` **đã tồn tại và đang chạy** (APK Android trỏ vào nó). Không cần tạo mới. Owner chỉ cần mở UI Frontgate → sửa rule → kiểm 4 cờ và chạy 3 lệnh `curl` ở Phase 4:

| Cờ | Phải là | Nếu sai thì |
|----|---------|-------------|
| `Access` | `Public` | 5G ăn 403 |
| `CacheAssets` | tắt | `.m3u8` bị cache 24h, ghi đè cả `no-store` của addon |
| `WebSocketsSupport` | bật | SignalR (`/hubs/scout`) bị 426, rơi xuống long-polling |
| `Status` | `Active` | rule không được nạp |

### Phase 6 — docs + bump — ✅ xong

Đã bump scout `0.11.0 → 0.12.0`, cập nhật `.claude/memory/` (bị gitignore, không vào commit), và dọn tàn dư WebRTC trong `.claude/CLAUDE.md` + `.claude/memory/{projectbrief,techContext,systemPatterns}.md`. `minCoreVersion` / `minServerVersion` không đổi. Namorix không đụng gì.
