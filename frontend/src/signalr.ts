// Mirrors the backend ScoutSignalRGroups / ScoutSignalREvents constants; keep the two in sync.

export const ScSignalRGroups = {
  Scout: "scout",
} as const

export const ScSignalREvent = {
  CameraChanged: ScSignalRGroups.Scout + ":camera-changed",
  CameraDeleted: ScSignalRGroups.Scout + ":camera-deleted",
} as const

export type ScSignalRGroup = (typeof ScSignalRGroups)[keyof typeof ScSignalRGroups]
export type ScSignalREvent = (typeof ScSignalREvent)[keyof typeof ScSignalREvent]
