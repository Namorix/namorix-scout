// Types only: the library itself is imported inside createPlayer so it stays out of the
// addon's first load. hls.js is most of a megabyte and nothing else here needs it.
import type Hls from "hls.js"
import type { ErrorData } from "hls.js"
import { coreConfig } from "../config/coreConfig"
import { ScoutApiRoutes } from "../scoutApiRoutes"

export type StreamState = "idle" | "connecting" | "live" | "offline"

export interface StreamStatus {
  state: StreamState
  reason: string | null
}

export interface HlsStreamCallbacks {
  onState: (state: StreamState, reason?: string | null) => void
}

const PLAYLIST_POLL_MS = 1_000
const PLAYLIST_WAIT_MS = 20_000
const STALL_POLL_MS = 2_000
const STALL_GRACE_MS = 12_000
const EXTINF = "#EXTINF"
// Every failure here is worth another attempt: the ingest of a camera that is reconnecting is
// briefly gone, so the 409 and the empty playlist are the same story told at different stages.
// Backing off matters more than the cap - the warmup already parks a card for up to 20s, and a
// fixed short retry would put a camera that is down back on the wire every few seconds.
const RETRY_BASE_MS = 3_000
const RETRY_MAX_MS = 30_000

// The remote-viewing path. Everything the browser needs is a plain HTTPS GET of the playlist
// and its segments, so there is no ICE, no UDP and nothing for a carrier NAT to drop - the
// trade is a few seconds of latency behind the camera, which is the whole reason the WebRTC
// path existed.
export class HlsStreamClient {
  private readonly cameraId: string
  private readonly callbacks: HlsStreamCallbacks

  private hls: Hls | null = null
  private video: HTMLVideoElement | null = null
  private warmupAbort: AbortController | null = null
  private stallTimer: ReturnType<typeof setInterval> | null = null
  private lastTime = 0
  private lastProgressAt = 0
  private runId = 0
  private live = false
  private disposed = false
  private retryTimer: ReturnType<typeof setTimeout> | null = null
  private retryAttempt = 0

  constructor(cameraId: string, callbacks: HlsStreamCallbacks) {
    this.cameraId = cameraId
    this.callbacks = callbacks
  }

  // The element arrives after the client is built (React attaches refs during commit,
  // before the effect that creates this object), so attaching is always optional and
  // the player picks up whatever is registered by the time the playlist is ready.
  attach(video: HTMLVideoElement): void {
    this.video = video
    if (this.hls && this.hls.media !== video) this.hls.attachMedia(video)
  }

  detach(): void {
    this.video = null
  }

  // Called by the card (auto-start on mount, and the play button). A deliberate attempt starts
  // the backoff over, so a viewer watching a flapping camera is not made to wait out the
  // interval the last run had grown into.
  start(): void {
    this.retryAttempt = 0
    this.restart()
  }

  private restart(): void {
    if (this.disposed) return
    const run = ++this.runId
    this.stopInternal()
    this.callbacks.onState("connecting", null)
    void this.open(run)
  }

  stop(): void {
    if (this.disposed) return
    this.runId += 1
    this.stopInternal()
    this.callbacks.onState("idle", null)
  }

  dispose(): void {
    if (this.disposed) return
    this.disposed = true
    this.runId += 1
    this.stopInternal()
  }

  private isCurrent(run: number): boolean {
    return !this.disposed && run === this.runId
  }

  private playlistUrl(): string {
    return `${coreConfig.getApiBaseUrl()}${ScoutApiRoutes.cameraLive(this.cameraId)}`
  }

  private async open(run: number): Promise<void> {
    const outcome = await this.warmUp(run)
    if (!this.isCurrent(run)) return
    if (outcome !== "ready") {
      this.fail(run, outcome === "rejected" ? "stream-offline" : "timeout")
      return
    }
    await this.createPlayer(run)
  }

  // The playlist request is what starts the packager, so the first answers legitimately
  // carry no segments - handing one of those to hls.js is how the stream looks broken for
  // the first few seconds and never recovers. Wait for something playable instead, and
  // keep the two failure modes apart: a camera the backend cannot reach answers with an
  // error status, while a slow one answers with an empty playlist.
  private async warmUp(run: number): Promise<"ready" | "rejected" | "timeout"> {
    const deadline = Date.now() + PLAYLIST_WAIT_MS

    while (this.isCurrent(run)) {
      const abort = new AbortController()
      this.warmupAbort = abort
      try {
        const response = await fetch(this.playlistUrl(), { signal: abort.signal })
        if (!response.ok) return "rejected"
        if ((await response.text()).includes(EXTINF)) return "ready"
      } catch {
        // A poll that never landed is not an answer: the packager may be starting, the
        // radio may have blinked. Only the deadline decides.
      } finally {
        this.warmupAbort = null
      }

      if (Date.now() > deadline) return "timeout"
      // Deliberately not cleared on stop: the loop exits on the next runId check anyway,
      // and a cancelled timer would leave this await pending for the client's lifetime.
      await new Promise((resolve) => window.setTimeout(resolve, PLAYLIST_POLL_MS))
    }

    return "timeout"
  }

  private async createPlayer(run: number): Promise<void> {
    let module: typeof import("hls.js")
    try {
      module = await import("hls.js")
    } catch {
      // The chunk is fetched from the same origin as the playlist, so a failure here means
      // the network is already gone - the same story the playlist never arriving tells.
      this.fail(run, "stream-offline")
      return
    }
    if (!this.isCurrent(run)) return

    const { default: Hls, Events, ErrorTypes } = module
    const hls = new Hls({
      // The session cookie is HttpOnly and the playlist is same-origin, so the browser
      // sends it unprompted - this only covers an API base moved off-origin by VITE_API_URL.
      xhrSetup: (xhr) => {
        xhr.withCredentials = true
      },
    })
    this.hls = hls
    this.lastTime = 0
    this.lastProgressAt = Date.now()

    // A parsed manifest only says the playlist was readable. A fragment in hand is the
    // first honest proof that video is arriving.
    hls.on(Events.FRAG_LOADED, () => this.noteLive(run))
    hls.on(Events.MANIFEST_PARSED, () => {
      void this.video?.play().catch(() => undefined)
    })
    hls.on(Events.ERROR, (_event, data: ErrorData) => {
      if (!data.fatal || !this.isCurrent(run)) return
      this.fail(run, data.type === ErrorTypes.MEDIA_ERROR ? "hls-media" : "hls-network")
    })

    const video = this.video
    if (video) hls.attachMedia(video)
    hls.loadSource(this.playlistUrl())
  }

  private noteLive(run: number): void {
    if (!this.isCurrent(run)) return
    if (!this.live) {
      this.live = true
      this.callbacks.onState("live", null)
    }
    this.startStallWatch(run)
  }

  // hls.js keeps fetching happily while the camera behind the relay has gone quiet, so a
  // frozen picture would otherwise keep reporting Live. Watch the only thing the viewer
  // can see: the clock on the element.
  private startStallWatch(run: number): void {
    if (this.stallTimer !== null) return
    this.lastTime = 0
    this.lastProgressAt = Date.now()
    this.stallTimer = window.setInterval(() => {
      const video = this.video
      if (!this.isCurrent(run) || !video) return

      if (video.currentTime !== this.lastTime) {
        this.lastTime = video.currentTime
        this.lastProgressAt = Date.now()
        return
      }
      if (Date.now() - this.lastProgressAt >= STALL_GRACE_MS) this.fail(run, "no-frames")
    }, STALL_POLL_MS)
  }

  // Offline is what the card shows, not where the client stops: it stays off the wire for the
  // backoff interval, during which the play button is still live and cancels the wait.
  private fail(run: number, reason: string): void {
    if (!this.isCurrent(run)) return
    this.stopInternal()
    this.callbacks.onState("offline", reason)

    const delay = Math.min(RETRY_BASE_MS * 2 ** this.retryAttempt, RETRY_MAX_MS)
    this.retryAttempt += 1
    this.retryTimer = window.setTimeout(() => {
      this.retryTimer = null
      if (this.isCurrent(run)) this.restart()
    }, delay)
  }

  private stopInternal(): void {
    this.warmupAbort?.abort()
    this.warmupAbort = null

    if (this.retryTimer !== null) {
      clearTimeout(this.retryTimer)
      this.retryTimer = null
    }

    if (this.stallTimer !== null) {
      clearInterval(this.stallTimer)
      this.stallTimer = null
    }

    const hls = this.hls
    this.hls = null
    this.live = false
    // destroy() also detaches the media, which is what clears the frame the poster replaces.
    hls?.destroy()
  }
}
