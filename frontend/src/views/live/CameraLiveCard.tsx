import React from "react"
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
  onPlay: () => void
  onStop: () => void
  onEdit: () => void
  onDelete: () => void
}

export const CameraLiveCard: React.FC<CameraLiveCardProps> = ({
  camera,
  status,
  onPlay,
  onStop,
  onEdit,
  onDelete,
}) => {
  const { t } = useTranslation()
  const streaming = status.state === "live"
  const connecting = status.state === "connecting"
  const showOverlay = !streaming

  const disabled = !camera.enabled && status.state === "idle"

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
      <div className="scout-live-card__video">
        <StreamVideo stream={streaming ? status.stream : null} />
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
      </div>
      <div className="scout-live-card__head">
        <span className="scout-live-card__name" title={camera.name}>
          {camera.name}
        </span>
        <div className="scout-live-card__actions">
          {streaming ? (
            <NmxButton
              semantic="error"
              title={t("scout.live.stop")}
              onClick={onStop}
            >
              <NmxIconFont symbol={NmxIconFontSymbol.STOP} />
            </NmxButton>
          ) : (
            <NmxButton
              semantic="success"
              disabled={connecting}
              title={t("scout.live.play")}
              onClick={onPlay}
            >
              <NmxIconFont symbol={NmxIconFontSymbol.PLAY} />
            </NmxButton>
          )}
          <NmxButton
            variant="outline"
            title={t("scout.cameras.list.edit")}
            onClick={onEdit}
          >
            <NmxIconFont symbol={NmxIconFontSymbol.EDIT} />
          </NmxButton>
          <NmxButton
            variant="outline"
            semantic="error"
            title={t("scout.cameras.list.delete")}
            onClick={onDelete}
          >
            <NmxIconFont symbol={NmxIconFontSymbol.DELETE} />
          </NmxButton>
        </div>
      </div>
    </div>
  )
}
