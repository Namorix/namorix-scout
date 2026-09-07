export type CameraStreamType = "main" | "sub"

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
