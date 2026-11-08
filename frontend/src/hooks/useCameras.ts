import { useCallback, useEffect, useRef, useState } from "react"

import { coreConfig } from "../config/coreConfig"
import { cameraController } from "../controllers/camera.controller"
import { useAppDispatch, useAppSelector } from "../store/hooks"
import { selectCameras } from "../store/selectors/cameraSelectors"
import { cameraActions } from "../store/slices/cameraSlice"
import { ScSignalREvent, ScSignalRGroups } from "../signalr"

const { useSignalR, useSignalRGroup, useSignalREvent, useSignalRStatus } =
  coreConfig.signalRHooks

export interface UseCamerasResult {
  cameras: ReturnType<typeof selectCameras>
  loading: boolean
  loadFailed: boolean
  refresh: () => Promise<void>
}

export function useCameras(): UseCamerasResult {
  const dispatch = useAppDispatch()
  const cameras = useAppSelector(selectCameras)
  const [loading, setLoading] = useState(false)
  const [loadFailed, setLoadFailed] = useState(false)

  const refresh = useCallback(async () => {
    setLoading(true)
    setLoadFailed(false)
    try {
      const list = await cameraController.list()
      dispatch(cameraActions.setCameras(list))
    } catch {
      setLoadFailed(true)
    } finally {
      setLoading(false)
    }
  }, [dispatch])

  useEffect(() => {
    void refresh()
  }, [refresh])

  // Camera health lives in the ingest process, not in the row, so the backend pushes a
  // change event whenever a stream enters or leaves a state; there is no list poll.
  useSignalR(true)
  useSignalRGroup(ScSignalRGroups.Scout, true)
  useSignalREvent(ScSignalREvent.CameraChanged, () => void refresh())
  useSignalREvent<string>(ScSignalREvent.CameraDeleted, (cameraId) =>
    dispatch(cameraActions.removeCamera(cameraId)),
  )

  // A reconnect may have missed pushes; the first connect is covered by the mount fetch.
  const status = useSignalRStatus()
  const wasConnected = useRef(false)
  useEffect(() => {
    if (status === "connected" && wasConnected.current) void refresh()
    wasConnected.current = status === "connected"
  }, [status, refresh])

  return { cameras, loading, loadFailed, refresh }
}
