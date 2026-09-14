import React from "react"
import { useTranslation } from "react-i18next"
import { NmxBadge, NmxButton, NmxIconFont, NmxIconFontSymbol } from "@namorix/ui"
import type { Camera } from "../../types/camera"

interface CameraManageCardProps {
  camera: Camera
  onInfo: () => void
  onEdit: () => void
  onDelete: () => void
}

export const CameraManageCard: React.FC<CameraManageCardProps> = ({
  camera,
  onInfo,
  onEdit,
  onDelete,
}) => {
  const { t } = useTranslation()

  return (
    <div className="scout-camera-card">
      <div className="scout-camera-card__head">
        <span className="scout-camera-card__name" title={camera.name}>
          {camera.name}
        </span>
        <div className="scout-camera-card__badges">
          <NmxBadge
            semantic={camera.enabled ? "success" : "default"}
            size="sm"
          >
            {camera.enabled
              ? t("scout.cameras.list.enabled")
              : t("scout.cameras.list.disabled")}
          </NmxBadge>
          <NmxBadge
            semantic={camera.recordEnabled ? "info" : "default"}
            size="sm"
          >
            {camera.recordEnabled
              ? t("scout.cameras.list.recordOn")
              : t("scout.cameras.list.recordOff")}
          </NmxBadge>
        </div>
      </div>

      <div className="scout-camera-card__meta">
        <div className="scout-camera-card__row">
          <span className="scout-camera-card__label">
            {t("scout.cameras.list.url")}
          </span>
          <span className="scout-camera-card__value" title={camera.rtspUrl}>
            {camera.rtspUrl}
          </span>
        </div>
        <div className="scout-camera-card__row">
          <span className="scout-camera-card__label">
            {t("scout.cameras.list.stream")}
          </span>
          <span className="scout-camera-card__value">
            {t(`scout.cameras.stream.${camera.streamType}`)}
          </span>
        </div>
        <div className="scout-camera-card__row">
          <span className="scout-camera-card__label">
            {t("scout.cameras.list.retention")}
          </span>
          <span className="scout-camera-card__value">
            {camera.retentionDays}
          </span>
        </div>
      </div>

      <div className="scout-camera-card__actions">
        <NmxButton
          variant="outline"
          title={t("scout.cameras.list.info")}
          onClick={onInfo}
        >
          <NmxIconFont symbol={NmxIconFontSymbol.INFO} />
        </NmxButton>
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
  )
}
