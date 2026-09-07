import { API_BASE } from "@namorix/core"

export const CAMERAS_BASE = API_BASE + "/cameras"
export const STREAMS_BASE = API_BASE + "/streams"

export const ScoutApiRoutes = {
  cameras: CAMERAS_BASE,
  cameraById: (id: string) => `${CAMERAS_BASE}/${id}`,
  streams: {
    offer: (cameraId: string) => `${STREAMS_BASE}/${cameraId}/offer`,
    answer: (sessionId: string) => `${STREAMS_BASE}/${sessionId}/answer`,
    ice: (sessionId: string) => `${STREAMS_BASE}/${sessionId}/ice`,
    stop: (sessionId: string) => `${STREAMS_BASE}/${sessionId}`,
  },
} as const
