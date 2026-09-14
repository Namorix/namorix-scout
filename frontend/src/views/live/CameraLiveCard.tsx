import React, { useCallback, useEffect, useRef, useState } from "react"
import { useTranslation } from "react-i18next"
import {
  NmxBadge,
  NmxButton,
  NmxIconFont,
  NmxIconFontSymbol,
  NmxSpinner,
} from "@namorix/ui"
import type { StreamStatus } from "../../streaming/RtcStreamClient"
import type { Camera } from "../../types/camera"
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
  const connecting = status.state === "connecting"

  const disabled = !camera.enabled && status.state === "idle"

  const videoRef = useRef<HTMLVideoElement | null>(null)

  // <video> mất sạch khung hình khi track kết thúc, nên khi pause phải tự chụp
  // frame cuối lúc bấm Stop rồi dùng nó làm poster.
  const [poster, setPoster] = useState<string | null>(null)

  // Phiên sống suốt lúc pause, nên poster chỉ bỏ khi thật sự quay lại live.
  useEffect(() => {
    if (!paused && status.stream) setPoster(null)
  }, [paused, status.stream])

  const showPoster = paused && poster !== null
  // Chỉ hiện badge khi chưa có ảnh — pause thì giữ nguyên poster.
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

  // Fullscreen áp lên chính container để overlay điều khiển (nút thoát) vẫn nằm trong
  // phần tử được phóng to — fullscreen thẳng lên <video> sẽ che mất controls.
  const containerRef = useRef<HTMLDivElement | null>(null)
  const [isFullscreen, setIsFullscreen] = useState(false)

  useEffect(() => {
    const onChange = () => {
      const active = document.fullscreenElement === containerRef.current
      setIsFullscreen(active)
      if (active) {
        setControlsVisible(true)
        scheduleHide()
      }
    }
    document.addEventListener("fullscreenchange", onChange)
    return () => document.removeEventListener("fullscreenchange", onChange)
  }, [scheduleHide])

  const toggleFullscreen = () => {
    const el = containerRef.current
    if (!el) return
    if (document.fullscreenElement) void document.exitFullscreen()
    else void el.requestFullscreen()
  }

  // Khi chưa phát thì luôn hiện để user thấy nút Play; chỉ auto-hide lúc đang live.
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
      : status.state === "offline"
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
        onTouchStart={() => revealControls(true)}
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
