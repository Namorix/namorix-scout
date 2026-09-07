import { useCallback, useEffect, useState } from "react"

import { cameraController } from "../controllers/camera.controller"
import { useAppDispatch, useAppSelector } from "../store/hooks"
import { selectCameras } from "../store/selectors/cameraSelectors"
import { cameraActions } from "../store/slices/cameraSlice"

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

  return { cameras, loading, loadFailed, refresh }
}
