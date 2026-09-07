import { createSelector } from "@reduxjs/toolkit"
import type { Camera } from "../../types/camera"
import type { RootState } from "../store"

const selectCameraState = (state: RootState) => state.cameras

export const selectCameras = createSelector(selectCameraState, (state) =>
  state.order
    .map((id) => state.byId[id])
    .filter((camera): camera is Camera => camera != null),
)

export const selectCameraById = (id: string) =>
  createSelector(selectCameraState, (state) => state.byId[id])
