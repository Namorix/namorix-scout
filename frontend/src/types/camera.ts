export type CameraStreamType = "main" | "sub"

export type CameraRuntimeState =
  | "stopped"
  | "connecting"
  | "streaming"
  | "failed"

export interface Camera {
  id: string
  name: string
  rtspUrl: string
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
}

export interface CameraUpsert {
  name: string
  rtspUrl: string
  username: string
  password: string
  streamType: CameraStreamType
  enabled: boolean
  recordEnabled: boolean
  retentionDays: number
}
