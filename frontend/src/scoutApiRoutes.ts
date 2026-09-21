import { API_BASE } from "@namorix/core"

export const CAMERAS_BASE = API_BASE + "/cameras"
export const USERS_BASE = API_BASE + "/users"

export const ScoutApiRoutes = {
  users: USERS_BASE,
  cameras: CAMERAS_BASE,
  cameraById: (id: string) => `${CAMERAS_BASE}/${id}`,
  cameraShares: (id: string) => `${CAMERAS_BASE}/${id}/shares`,
  cameraShareByUser: (id: string, userId: number) =>
    `${CAMERAS_BASE}/${id}/shares/${userId}`,
  cameraLive: (id: string) => `${CAMERAS_BASE}/${id}/live.m3u8`,
} as const
