import React from "react"
import { useTranslation } from "react-i18next"
import {
  NmxBadge,
  NmxButton,
  NmxIconFont,
  NmxIconFontSymbol,
} from "@namorix/ui"
import type { Camera } from "../../types/camera"

interface CameraManageCardProps {
  camera: Camera
  onInfo: () => void
  onEdit: () => void
  onDelete: () => void
  onShare: () => void
}

export const CameraManageCard: React.FC<CameraManageCardProps> = ({
  camera,
  onInfo,
  onEdit,
  onDelete,
  onShare,
}) => {
  const { t } = useTranslation()
  const isOwner = camera.access === "owner"

  return (
    <div className="scout-camera-card">
      <div className="scout-camera-card__head">
        <span className="scout-camera-card__name" title={camera.name}>
          {camera.name}
        </span>
        <div className="scout-camera-card__badges">
          {camera.access !== "owner" && (
            <NmxBadge semantic="info" size="sm">
              {t(`scout.cameras.access.${camera.access}`)}
            </NmxBadge>
          )}
          <NmxBadge semantic={camera.enabled ? "success" : "default"} size="sm">
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
          {camera.state === "failed" && (
            // NmxBadge takes no HTML props, so the reason rides on a wrapping span.
            <span title={camera.lastError ?? undefined}>
              <NmxBadge semantic="error" size="sm">
                {t("scout.cameras.list.error")}
              </NmxBadge>
            </span>
          )}
        </div>
      </div>

      <div className="scout-camera-card__meta">
        {camera.rtspUrl !== null && (
          <div className="scout-camera-card__row">
            <span className="scout-camera-card__label">
              {t("scout.cameras.list.url")}
            </span>
            <span className="scout-camera-card__value" title={camera.rtspUrl}>
              {camera.rtspUrl}
            </span>
          </div>
        )}
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
          variant="ghost"
          semantic="info"
          title={t("scout.cameras.list.info")}
          onClick={onInfo}
        >
          <NmxIconFont symbol={NmxIconFontSymbol.INFO} size="md" />
        </NmxButton>
        {camera.access !== "view" && (
          <NmxButton
            variant="ghost"
            semantic="success"
            title={t("scout.cameras.list.edit")}
            onClick={onEdit}
          >
            <NmxIconFont symbol={NmxIconFontSymbol.EDIT} size="md" />
          </NmxButton>
        )}
        {isOwner && (
          <>
            <NmxButton
              variant="ghost"
              semantic="warning"
              title={t("scout.cameras.list.share")}
              onClick={onShare}
            >
              <NmxIconFont symbol={NmxIconFontSymbol.SHARE} size="md" />
            </NmxButton>
            <NmxButton
              variant="ghost"
              semantic="error"
              title={t("scout.cameras.list.delete")}
              onClick={onDelete}
            >
              <NmxIconFont symbol={NmxIconFontSymbol.DELETE} size="md" />
            </NmxButton>
          </>
        )}
      </div>
    </div>
  )
}
