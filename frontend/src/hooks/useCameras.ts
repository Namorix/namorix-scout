import { useCallback, useEffect, useState } from "react"

import { cameraController } from "../controllers/camera.controller"
import { useAppDispatch, useAppSelector } from "../store/hooks"
import { selectCameras } from "../store/selectors/cameraSelectors"
import { cameraActions } from "../store/slices/cameraSlice"

// Camera health lives in the ingest process, not in the row, so the list has to be re-read
// for a failing camera to show up — and for the badge to clear once it recovers.
const POLL_MS = 10_000

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

  useEffect(() => {
    // A hidden tab throttles timers anyway; skipping keeps dead requests off the wire.
    const timer = setInterval(() => {
      if (document.hidden) return
      void refresh()
    }, POLL_MS)
    return () => clearInterval(timer)
  }, [refresh])

  return { cameras, loading, loadFailed, refresh }
}
