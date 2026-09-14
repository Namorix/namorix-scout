import { streamsController, type StreamOffer } from "../controllers/streams.controller"

export type StreamState = "idle" | "connecting" | "live" | "offline"

export interface StreamStatus {
  state: StreamState
  reason: string | null
  stream: MediaStream | null
}

export interface RtcStreamCallbacks {
  onState: (state: StreamState, reason?: string | null) => void
  onStream: (stream: MediaStream | null) => void
}

const ICE_POLL_MS = 250
const CONNECT_TIMEOUT_MS = 20_000
const GATHER_TIMEOUT_MS = 5_000
const DISCONNECT_GRACE_MS = 8_000
const RECONNECT_MIN_MS = 1_000
const RECONNECT_MAX_MS = 5_000

function normalizeCandidate(value: string): string {
  let text = value.trim()
  const firstLine = text.split("\n").find((line) => line.includes("candidate:"))
  if (firstLine) text = firstLine.trim()
  if (text.startsWith("a=")) text = text.slice(2)
  return text
}

function waitIceGathering(pc: RTCPeerConnection): Promise<void> {
  if (pc.iceGatheringState === "complete") return Promise.resolve()
  return new Promise((resolve) => {
    const timer = window.setTimeout(finish, GATHER_TIMEOUT_MS)
    function finish() {
      window.clearTimeout(timer)
      pc.onicegatheringstatechange = null
      resolve()
    }
    pc.onicegatheringstatechange = () => {
      if (pc.iceGatheringState === "complete") finish()
    }
  })
}

export class RtcStreamClient {
  private readonly cameraId: string
  private readonly callbacks: RtcStreamCallbacks

  private peer: RTCPeerConnection | null = null
  private sessionId: string | null = null
  private pollTimer: ReturnType<typeof setInterval> | null = null
  private retryTimer: ReturnType<typeof setTimeout> | null = null
  private graceTimer: ReturnType<typeof setTimeout> | null = null
  private visibilityBound = false
  private attempt = 0
  private degraded = false
  private runId = 0
  private disposed = false

  constructor(cameraId: string, callbacks: RtcStreamCallbacks) {
    this.cameraId = cameraId
    this.callbacks = callbacks
  }

  start(): void {
    if (this.disposed) return
    const run = ++this.runId
    this.stopInternal()
    this.attempt = 0
    this.degraded = false
    this.callbacks.onState("connecting", null)
    void this.connect(run)
  }

  stop(): void {
    if (this.disposed) return
    this.runId += 1
    this.stopInternal()
    this.callbacks.onStream(null)
    this.callbacks.onState("idle", null)
  }

  dispose(): void {
    if (this.disposed) return
    this.disposed = true
    this.runId += 1
    this.stopInternal()
    this.disarmVisibility()
  }

  private isCurrent(run: number): boolean {
    return !this.disposed && run === this.runId
  }

  private async connect(run: number): Promise<void> {
    let offer: StreamOffer
    try {
      offer = await streamsController.offer(this.cameraId)
    } catch {
      if (this.isCurrent(run)) this.scheduleRetry(run, "stream-offline")
      return
    }
    if (!this.isCurrent(run)) return

    // The server session exists as soon as the offer is issued, so publish its id
    // now - our ICE candidates start firing during gathering and must not be dropped.
    this.sessionId = offer.sessionId

    const peer = new RTCPeerConnection({ iceServers: [] })
    this.peer = peer
    peer.addTransceiver("video", { direction: "recvonly" })

    peer.ontrack = (ev) => {
      if (ev.track.kind !== "video") return
      const stream = ev.streams[0] ?? new MediaStream([ev.track])
      if (this.isCurrent(run)) this.callbacks.onStream(stream)
    }

    peer.onicecandidate = (ev) => {
      const sessionId = this.sessionId
      if (ev.candidate && sessionId) {
        void streamsController.iceAdd(sessionId, ev.candidate.candidate).catch(() => undefined)
      }
    }

    peer.onconnectionstatechange = () => {
      if (!this.isCurrent(run) || this.peer !== peer) return
      const state = peer.connectionState
      if (state === "connected") {
        this.attempt = 0
        this.degraded = false
        this.disarmVisibility()
        this.clearPoll()
        this.clearGrace()
        this.callbacks.onState("live", null)
      } else if (state === "disconnected") {
        this.startGrace(run)
      } else if (state === "failed" || state === "closed") {
        this.scheduleRetry(run, "connection-lost")
      }
    }

    try {
      await peer.setRemoteDescription({ type: "offer", sdp: offer.sdp })
      const answer = await peer.createAnswer()
      await peer.setLocalDescription(answer)
      await waitIceGathering(peer)
      if (!this.isCurrent(run)) return

      const sdp = peer.localDescription?.sdp ?? ""
      await streamsController.answer(offer.sessionId, sdp)
    } catch {
      if (this.isCurrent(run)) this.scheduleRetry(run, "negotiation-failed")
      return
    }

    this.startPoll(run)
  }

  private startPoll(run: number): void {
    this.clearPoll()
    const deadline = Date.now() + CONNECT_TIMEOUT_MS
    this.pollTimer = setInterval(() => {
      const sessionId = this.sessionId
      const peer = this.peer
      if (!this.isCurrent(run) || !sessionId || !peer) {
        this.clearPoll()
        return
      }
      const connectionState = peer.connectionState
      const iceState = peer.iceConnectionState
      if (
        connectionState === "connected" ||
        iceState === "connected" ||
        iceState === "completed"
      ) {
        this.clearPoll()
        this.callbacks.onState("live", null)
        return
      }
      // A hidden tab throttles this interval, so the wall-clock deadline would
      // expire while nothing could actually be attempted - defer until visible.
      if (!document.hidden && Date.now() > deadline) {
        this.clearPoll()
        this.scheduleRetry(run, "timeout")
        return
      }
      void streamsController
        .iceList(sessionId)
        .then(({ candidates }) => {
          if (!this.isCurrent(run)) return
          for (const candidate of candidates) {
            const line = normalizeCandidate(candidate)
            if (!line) continue
            void peer.addIceCandidate({ candidate: line }).catch(() => undefined)
          }
        })
        .catch(() => undefined)
    }, ICE_POLL_MS)
  }

  private scheduleRetry(run: number, reason: string): void {
    if (!this.isCurrent(run)) return
    this.stopInternal()
    this.degraded = true
    this.callbacks.onStream(null)
    this.callbacks.onState("connecting", reason)

    const delay = Math.min(RECONNECT_MIN_MS * 2 ** this.attempt, RECONNECT_MAX_MS)
    this.attempt += 1
    this.retryTimer = window.setTimeout(() => {
      this.retryTimer = null
      this.disarmVisibility()
      if (!this.isCurrent(run)) return
      void this.connect(run)
    }, delay)
    this.armVisibility()
  }

  private startGrace(run: number): void {
    this.degraded = true
    this.armVisibility()
    if (this.graceTimer !== null) return
    this.graceTimer = window.setTimeout(() => {
      this.graceTimer = null
      this.scheduleRetry(run, "connection-lost")
    }, DISCONNECT_GRACE_MS)
  }

  private clearGrace(): void {
    if (this.graceTimer !== null) {
      clearTimeout(this.graceTimer)
      this.graceTimer = null
    }
  }

  private clearPoll(): void {
    if (this.pollTimer) {
      clearInterval(this.pollTimer)
      this.pollTimer = null
    }
  }

  private readonly onVisibility = (): void => {
    if (this.disposed || document.visibilityState !== "visible") return
    if (!this.degraded) return
    const run = this.runId
    if (!this.isCurrent(run)) return
    this.stopInternal()
    void this.connect(run)
  }

  private armVisibility(): void {
    if (this.visibilityBound) return
    this.visibilityBound = true
    document.addEventListener("visibilitychange", this.onVisibility)
  }

  private disarmVisibility(): void {
    if (!this.visibilityBound) return
    this.visibilityBound = false
    document.removeEventListener("visibilitychange", this.onVisibility)
  }

  private stopInternal(): void {
    this.clearPoll()
    this.clearGrace()
    this.disarmVisibility()
    if (this.retryTimer !== null) {
      clearTimeout(this.retryTimer)
      this.retryTimer = null
    }
    const sessionId = this.sessionId
    this.sessionId = null
    if (sessionId) {
      void streamsController.stop(sessionId).catch(() => undefined)
    }
    const peer = this.peer
    this.peer = null
    if (peer) {
      peer.ontrack = null
      peer.onicecandidate = null
      peer.onconnectionstatechange = null
      peer.close()
    }
  }
}
