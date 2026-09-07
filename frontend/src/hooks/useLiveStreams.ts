import { useCallback, useEffect, useRef, useState, type Dispatch, type SetStateAction } from "react"
import { RtcStreamClient, type StreamStatus } from "../streaming/RtcStreamClient"
import type { Camera } from "../types/camera"

export const IDLE: StreamStatus = { state: "idle", reason: null, stream: null }

interface SessionEntry {
  client: RtcStreamClient
  auto: boolean
}

export interface UseLiveStreamsResult {
  statuses: Record<string, StreamStatus>
  play: (id: string) => void
  stop: (id: string) => void
}

function createClient(id: string, setStatuses: Dispatch<SetStateAction<Record<string, StreamStatus>>>) {
  return new RtcStreamClient(id, {
    onState: (state, reason = null) =>
      setStatuses((prev) => ({ ...prev, [id]: { ...(prev[id] ?? IDLE), state, reason } })),
    onStream: (stream) =>
      setStatuses((prev) => ({ ...prev, [id]: { ...(prev[id] ?? IDLE), stream } })),
  })
}

export function useLiveStreams(cameras: Camera[]): UseLiveStreamsResult {
  const entriesRef = useRef<Map<string, SessionEntry>>(new Map())
  const camerasRef = useRef(cameras)
  camerasRef.current = cameras

  const [statuses, setStatuses] = useState<Record<string, StreamStatus>>({})

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
      }
    }
    for (const camera of camerasRef.current) {
      if (!camera.enabled || entriesRef.current.has(camera.id)) continue
      const client = createClient(camera.id, setStatuses)
      entriesRef.current.set(camera.id, { client, auto: true })
      client.start()
    }
  }, [signature])

  useEffect(
    () => () => {
      for (const entry of entriesRef.current.values()) entry.client.dispose()
      entriesRef.current.clear()
    },
    [],
  )

  const play = useCallback((id: string) => {
    const existing = entriesRef.current.get(id)
    if (existing) {
      existing.client.dispose()
      entriesRef.current.delete(id)
    }
    const client = createClient(id, setStatuses)
    entriesRef.current.set(id, { client, auto: false })
    client.start()
  }, [])

  const stop = useCallback((id: string) => {
    const existing = entriesRef.current.get(id)
    if (existing) {
      existing.client.dispose()
      entriesRef.current.delete(id)
    }
    setStatuses((prev) => ({ ...prev, [id]: { state: "idle", reason: null, stream: null } }))
  }, [])

  return { statuses, play, stop }
}
