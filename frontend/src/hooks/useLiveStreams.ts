import { useCallback, useEffect, useRef, useState, type Dispatch, type SetStateAction } from "react"
import { HlsStreamClient, type StreamStatus } from "../streaming/HlsStreamClient"
import type { Camera } from "../types/camera"

export const IDLE: StreamStatus = { state: "idle", reason: null }

interface SessionEntry {
  client: HlsStreamClient
  auto: boolean
  paused: boolean
}

export interface UseLiveStreamsResult {
  statuses: Record<string, StreamStatus>
  paused: Record<string, boolean>
  play: (id: string) => void
  stop: (id: string) => void
  attachVideo: (id: string, video: HTMLVideoElement | null) => void
}

function createClient(id: string, setStatuses: Dispatch<SetStateAction<Record<string, StreamStatus>>>) {
  return new HlsStreamClient(id, {
    onState: (state, reason = null) =>
      setStatuses((prev) => ({ ...prev, [id]: { state, reason } })),
  })
}

export function useLiveStreams(cameras: Camera[]): UseLiveStreamsResult {
  const entriesRef = useRef<Map<string, SessionEntry>>(new Map())
  // A card registers its <video> during the same commit that runs this hook's effect, and
  // refs are attached first - so the elements are kept here and handed to a client whenever
  // one is built, rather than assuming a client already exists.
  const videosRef = useRef<Map<string, HTMLVideoElement>>(new Map())
  const camerasRef = useRef(cameras)
  camerasRef.current = cameras

  const [statuses, setStatuses] = useState<Record<string, StreamStatus>>({})
  const [paused, setPaused] = useState<Record<string, boolean>>({})

  // Read by play() to tell a resume apart from a retry without making the callback depend
  // on the state it writes to.
  const statusesRef = useRef(statuses)
  statusesRef.current = statuses

  const signature = cameras
    .filter((camera) => camera.enabled)
    .map((camera) => camera.id)
    .join(",")

  useEffect(() => {
    const camerasById = new Map(camerasRef.current.map((camera) => [camera.id, camera]))
    for (const [id, entry] of entriesRef.current) {
      const camera = camerasById.get(id)
      if (!camera || (entry.auto && !camera.enabled)) {
        entry.client.dispose()
        entriesRef.current.delete(id)
        videosRef.current.delete(id)
        setPaused((prev) => {
          if (!(id in prev)) return prev
          const next = { ...prev }
          delete next[id]
          return next
        })
      }
    }
    for (const camera of camerasRef.current) {
      if (!camera.enabled || entriesRef.current.has(camera.id)) continue
      const client = createClient(camera.id, setStatuses)
      const video = videosRef.current.get(camera.id)
      if (video) client.attach(video)
      entriesRef.current.set(camera.id, { client, auto: true, paused: false })
      client.start()
    }
  }, [signature])

  useEffect(
    () => () => {
      for (const entry of entriesRef.current.values()) entry.client.dispose()
      entriesRef.current.clear()
      videosRef.current.clear()
    },
    [],
  )

  const attachVideo = useCallback((id: string, video: HTMLVideoElement | null) => {
    if (!video) {
      videosRef.current.delete(id)
      entriesRef.current.get(id)?.client.detach()
      return
    }
    videosRef.current.set(id, video)
    entriesRef.current.get(id)?.client.attach(video)
  }, [])

  // Pausing keeps the player and its position intact and only overlays a poster, so
  // resuming costs nothing. Anything else means the button is being used as a retry, and
  // the running client has already given up - calling start() re-runs the whole open
  // sequence, where the old code threw the client away and lost the attached element.
  const play = useCallback((id: string) => {
    const existing = entriesRef.current.get(id)
    const auto = existing?.auto ?? false

    if (existing) {
      existing.paused = false
      setPaused((prev) => (prev[id] ? { ...prev, [id]: false } : prev))
      if (statusesRef.current[id]?.state !== "live") existing.client.start()
      return
    }

    const client = createClient(id, setStatuses)
    const video = videosRef.current.get(id)
    if (video) client.attach(video)
    entriesRef.current.set(id, { client, auto, paused: false })
    setPaused((prev) => (prev[id] ? { ...prev, [id]: false } : prev))
    client.start()
  }, [])

  const stop = useCallback((id: string) => {
    const existing = entriesRef.current.get(id)
    if (!existing) return
    existing.paused = true
    setPaused((prev) => (prev[id] ? prev : { ...prev, [id]: true }))
  }, [])

  return { statuses, paused, play, stop, attachVideo }
}
