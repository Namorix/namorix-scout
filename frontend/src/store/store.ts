import { configureStore } from "@reduxjs/toolkit"
import { cameraReducer } from "./slices/cameraSlice"

export const store = configureStore({
  reducer: {
    cameras: cameraReducer,
  },
})

export type RootState = ReturnType<typeof store.getState>
export type AppDispatch = typeof store.dispatch
