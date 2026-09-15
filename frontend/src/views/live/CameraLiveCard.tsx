import React, { useCallback, useEffect, useRef, useState } from "react"
import { useTranslation } from "react-i18next"
import { Capacitor, SystemBarType, SystemBars } from "@capacitor/core"
import { ScreenOrientation } from "@capacitor/screen-orientation"
import {
  NmxBadge,
  NmxButton,
  NmxIconFont,
  NmxIconFontSymbol,
  NmxSpinner,
} from "@namorix/ui"
import type { StreamStatus } from "../../streaming/RtcStreamClient"
import type { Camera } from "../../types/camera"
import { usePinchZoom } from "../../hooks/usePinchZoom"
import { exitFullscreen } from "../../utils/fullscreen"
import { StreamVideo } from "./StreamVideo"

interface CameraLiveCardProps {
  camera: Camera
  status: StreamStatus
  paused: boolean
  onPlay: () => void
  onStop: () => void
}

const CONTROLS_HIDE_MS = 3_000

export const CameraLiveCard: React.FC<CameraLiveCardProps> = ({
  camera,
  status,
  paused,
  onPlay,
  onStop,
}) => {
  const { t } = useTranslation()
  const streaming = status.state === "live"
  // The backend already knows the camera is dead and says so within one poll, so there
  // is no reason to sit on a spinner for the whole frame-stall window first.
  const cameraFailed = camera.state === "failed" && !streaming
  const connecting = status.state === "connecting" && !cameraFailed

  const disabled = !camera.enabled && status.state === "idle"

  const videoRef = useRef<HTMLVideoElement | null>(null)

  // <video> clears its frame once the track ends, so on pause we capture the last
  // frame at Stop and reuse it as the poster.
  const [poster, setPoster] = useState<string | null>(null)

  // The session stays alive while paused, so only drop the poster once we are live again.
  useEffect(() => {
    if (!paused && status.stream) setPoster(null)
  }, [paused, status.stream])

  const showPoster = paused && poster !== null
  // Only show the badge when there is no captured image — keep the poster while paused.
  const showOverlay = !showPoster && !streaming

  const [controlsVisible, setControlsVisible] = useState(false)
  const hideTimer = useRef<ReturnType<typeof setTimeout> | null>(null)

  const clearHideTimer = useCallback(() => {
    if (hideTimer.current !== null) {
      clearTimeout(hideTimer.current)
      hideTimer.current = null
    }
  }, [])

  const scheduleHide = useCallback(() => {
    clearHideTimer()
    hideTimer.current = setTimeout(() => {
      hideTimer.current = null
      setControlsVisible(false)
    }, CONTROLS_HIDE_MS)
  }, [clearHideTimer])

  useEffect(() => () => clearHideTimer(), [clearHideTimer])

  // Fullscreen targets the container so the control overlay (the exit button) stays
  // inside the enlarged element — fullscreening the <video> directly hides the controls.
  const containerRef = useRef<HTMLDivElement | null>(null)
  const [isFullscreen, setIsFullscreen] = useState(false)

  usePinchZoom(videoRef, isFullscreen)

  useEffect(() => {
    const onChange = () => {
      const active = document.fullscreenElement === containerRef.current
      setIsFullscreen(active)
      if (active) {
        setControlsVisible(true)
        scheduleHide()
      }
      // Fullscreen API only scales the web content; the system bars live in the native
      // window layer, so they stay visible unless hidden separately.
      // Orientation is deliberately NOT unlocked here: this event also fires spuriously
      // around the orientation change itself, which would snap the device back to portrait.
      if (Capacitor.isNativePlatform()) {
        const systemBars = active
          ? SystemBars.hide({ bar: SystemBarType.StatusBar })
          : SystemBars.show({ bar: SystemBarType.StatusBar })
        systemBars.catch((err) =>
          console.error("[Scout] SystemBars toggle failed", err),
        )
      }
    }
    document.addEventListener("fullscreenchange", onChange)
    return () => document.removeEventListener("fullscreenchange", onChange)
  }, [scheduleHide])

  const toggleFullscreen = async () => {
    const el = containerRef.current
    if (!el) return

    if (document.fullscreenElement) {
      await exitFullscreen()
      return
    }

    await el.requestFullscreen()
    // Rotation is a separate API from fullscreen, and the spec only allows locking
    // once the document is in fullscreen — hence the await above.
    if (Capacitor.isNativePlatform()) {
      await ScreenOrientation.lock({ orientation: "landscape" }).catch(() => {})
    } else {
      await screen.orientation?.lock("landscape").catch(() => {})
    }
  }

  // Always visible when not playing so the user can reach Play; auto-hide only while live.
  const controlsShown = paused || !streaming || controlsVisible

  const revealControls = (withTimer: boolean) => {
    if (paused || !streaming) return
    setControlsVisible(true)
    if (withTimer) scheduleHide()
  }

  const hideControls = () => {
    clearHideTimer()
    setControlsVisible(false)
  }

  const capturePoster = () => {
    const video = videoRef.current
    if (!video || video.videoWidth === 0) return

    const canvas = document.createElement("canvas")
    canvas.width = video.videoWidth
    canvas.height = video.videoHeight
    const ctx = canvas.getContext("2d")
    if (!ctx) return

    ctx.drawImage(video, 0, 0, canvas.width, canvas.height)
    setPoster(canvas.toDataURL("image/jpeg", 0.8))
  }

  const handleStop = () => {
    capturePoster()
    onStop()
  }

  const stateLabel = streaming
    ? { text: t("scout.live.live"), semantic: "success" as const }
    : connecting
      ? { text: t("scout.live.connecting"), semantic: "info" as const }
      : cameraFailed || status.state === "offline"
        ? { text: t("scout.live.offline"), semantic: "error" as const }
        : disabled
          ? {
              text: t("scout.cameras.list.disabled"),
              semantic: "default" as const,
            }
          : { text: t("scout.live.idle"), semantic: "warning" as const }

  return (
    <div className="scout-live-card">
      <div
        ref={containerRef}
        className="scout-live-card__video"
        onMouseEnter={() => revealControls(false)}
        onMouseMove={() => revealControls(true)}
        onMouseLeave={hideControls}
        onTouchStart={(event) => {
          // Two fingers is the start of a pinch — leave the controls alone.
          if (event.touches.length === 1) revealControls(true)
        }}
      >
        <StreamVideo stream={status.stream} videoRef={videoRef} />

        {showPoster && (
          <img className="scout-live-card__poster" src={poster} alt="" />
        )}

        {showOverlay && (
          <div className="scout-live-card__overlay">
            {connecting ? (
              <NmxSpinner size="md" />
            ) : (
              <NmxBadge semantic={stateLabel.semantic} size="sm">
                {stateLabel.text}
              </NmxBadge>
            )}
          </div>
        )}

        <div
          className={
            controlsShown
              ? "scout-live-card__controls scout-live-card__controls--visible"
              : "scout-live-card__controls"
          }
        >
          <div className="scout-live-card__top">
            <span className="scout-live-card__name" title={camera.name}>
              {camera.name}
            </span>
          </div>

          <div className="scout-live-card__bottom">
            <div className="scout-live-card__bottom-start">
              {streaming && !paused ? (
                <NmxButton
                  variant="ghost"
                  semantic="default"
                  title={t("scout.live.stop")}
                  onClick={handleStop}
                  className="scout-live-card__button"
                >
                  <NmxIconFont symbol={NmxIconFontSymbol.STOP} />
                </NmxButton>
              ) : (
                <NmxButton
                  variant="ghost"
                  semantic="default"
                  disabled={connecting && !paused}
                  title={t("scout.live.play")}
                  onClick={onPlay}
                  className="scout-live-card__button"
                >
                  <NmxIconFont symbol={NmxIconFontSymbol.PLAY} />
                </NmxButton>
              )}
            </div>
            <div className="scout-live-card__bottom-end">
              <NmxButton
                variant="ghost"
                semantic="default"
                disabled={connecting}
                title={t(
                  isFullscreen
                    ? "scout.live.exitFullscreen"
                    : "scout.live.fullscreen",
                )}
                onClick={toggleFullscreen}
                className="scout-live-card__button"
              >
                <NmxIconFont
                  symbol={
                    isFullscreen
                      ? NmxIconFontSymbol.FULLSCREEN_EXIT
                      : NmxIconFontSymbol.FULLSCREEN
                  }
                />
              </NmxButton>
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}
