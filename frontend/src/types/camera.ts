export type CameraStreamType = "main" | "sub"

export type CameraRuntimeState =
  | "stopped"
  | "connecting"
  | "streaming"
  | "failed"

// How the caller reaches a camera. "owner" is not a share level — it is the absence of one —
// but the API reports all three on one field so the UI branches on a single value.
export type CameraAccess = "owner" | "manage" | "view"

export type CameraSharePermission = "view" | "manage"

export interface Camera {
  id: string
  name: string
  // Absent unless the camera is the caller's own: the address and the account it is dialled
  // with belong to the owner, and a share grants watching rather than a copy of the connection.
  rtspUrl: string | null
  streamType: CameraStreamType
  enabled: boolean
  recordEnabled: boolean
  retentionDays: number
  hasCredentials: boolean
  username: string | null
  createdAt: string
  lastUpdatedAt: string
  state: CameraRuntimeState
  lastError: string | null
  lastFrameAt: string | null
  access: CameraAccess
}

export interface CameraUpsert {
  name: string
  enabled: boolean
  recordEnabled: boolean
  retentionDays: number
  // Owner-only group. A non-owner who sends any of these is refused rather than ignored, so a
  // manage share has to leave them out entirely instead of echoing back what it was not told.
  rtspUrl?: string
  username?: string
  password?: string
  streamType?: CameraStreamType
}

export interface CameraShare {
  userId: number
  // Filled in by the addon from the desktop's user table. Null when the desktop could not be
  // reached — the grant still exists, so the row still renders, just without a name.
  username: string | null
  name: string | null
  permission: CameraSharePermission
  createdAt: string
}

export interface ScoutUser {
  userId: number
  username: string
  name: string
}

export interface CameraShareRequest {
  userId: number
  permission: CameraSharePermission
}
