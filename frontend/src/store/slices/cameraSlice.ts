import { createSlice, type PayloadAction } from "@reduxjs/toolkit"
import type { Camera } from "../../types/camera"

interface NormalizedCameras {
  byId: Record<string, Camera>
  order: string[]
}

function toNormalizedCameras(cameras: readonly Camera[]): NormalizedCameras {
  const byId: Record<string, Camera> = {}
  const order: string[] = []

  for (const camera of cameras) {
    byId[camera.id] = camera
    order.push(camera.id)
  }

  return { byId, order }
}

const initialState: NormalizedCameras = {
  byId: {},
  order: [],
}

const slice = createSlice({
  name: "cameras",
  initialState,
  reducers: {
    setCameras(state, action: PayloadAction<Camera[]>) {
      return toNormalizedCameras(action.payload)
    },
    upsertCamera(state, action: PayloadAction<Camera>) {
      const camera = action.payload
      state.byId[camera.id] = camera
      if (!state.order.includes(camera.id)) state.order.push(camera.id)
    },
    removeCamera(state, action: PayloadAction<string>) {
      delete state.byId[action.payload]
      state.order = state.order.filter((id) => id !== action.payload)
    },
  },
})

export const cameraActions = slice.actions
export const cameraReducer = slice.reducer
