import React from "react"
import { useTranslation } from "react-i18next"
import {
  NmxBadge,
  NmxButton,
  NmxCard,
  NmxCardBody,
  NmxCardFooter,
  NmxCardHeader,
  NmxIconFont,
  NmxIconFontSymbol,
  NmxMetaItem,
  NmxMetaList,
  type NmxSemanticColor,
} from "@namorix/ui"
import type { Camera, CameraRuntimeState } from "../../types/camera"
import type { TFunction } from "@namorix/core"

interface CameraManageCardProps {
  camera: Camera
  onInfo: () => void
  onEdit: () => void
  onDelete: () => void
  onShare: () => void
}

function renderStateBadge(state: CameraRuntimeState, t: TFunction) {
  let semantic: NmxSemanticColor = "fatal"
  let title = t("scout.cameras.list.error")

  if (state === "failed") {
    semantic = "error"
    title = t("scout.cameras.state.failed")
  } else if (state === "connecting") {
    semantic = "info"
    title = t("scout.cameras.state.connecting")
  } else if (state === "stopped") {
    semantic = "warning"
    title = t("scout.cameras.state.stopped")
  } else if (state === "streaming") {
    semantic = "success"
    title = t("scout.cameras.state.streaming")
  }

  return (
    <NmxBadge semantic={semantic} size="sm">
      {title}
    </NmxBadge>
  )
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
    <NmxCard spacing="md">
      <NmxCardHeader title={camera.name} />
      <NmxCardBody>
        <NmxMetaList alignItem="end">
          {camera.rtspUrl && (
            <NmxMetaItem
              label={t("scout.cameras.list.url")}
              value={camera.rtspUrl ?? "_"}
              useSelectEnabled
            />
          )}
          <NmxMetaItem
            label={t("scout.cameras.list.stream")}
            value={t(`scout.cameras.stream.${camera.streamType}`)}
          />
          <NmxMetaItem label={t("scout.cameras.list.active")}>
            <NmxBadge
              semantic={camera.enabled ? "success" : "default"}
              size="sm"
            >
              {camera.enabled
                ? t("scout.cameras.list.enabled")
                : t("scout.cameras.list.disabled")}
            </NmxBadge>
          </NmxMetaItem>
          <NmxMetaItem label={t("scout.cameras.list.state")}>
            {renderStateBadge(camera.state, t)}
          </NmxMetaItem>
          <NmxMetaItem
            shouldRender={camera.access !== "owner"}
            label={t("scout.cameras.list.share")}
          >
            <NmxBadge
              semantic={camera.access === "view" ? "info" : "success"}
              size="sm"
            >
              {camera.access === "view"
                ? t("scout.cameras.share.permission.view")
                : t("scout.cameras.share.permission.manage")}
            </NmxBadge>
          </NmxMetaItem>
          <NmxMetaItem label={t("scout.cameras.list.record")}>
            <NmxBadge
              semantic={camera.recordEnabled ? "success" : "error"}
              size="sm"
            >
              {camera.recordEnabled
                ? t("scout.cameras.list.recordOn")
                : t("scout.cameras.list.recordOff")}
            </NmxBadge>
          </NmxMetaItem>
          <NmxMetaItem
            label={t("scout.cameras.list.retention")}
            value={String(camera.retentionDays)}
          />
        </NmxMetaList>
      </NmxCardBody>

      <NmxCardFooter alignHorizontal="end" spacingBottom="md">
        <NmxButton
          variant="ghost"
          semantic="info"
          title={t("scout.cameras.list.info")}
          onClick={onInfo}
        >
          <NmxIconFont symbol={NmxIconFontSymbol.INFO} size="lg" />
        </NmxButton>
        {camera.access !== "view" && (
          <NmxButton
            variant="ghost"
            semantic="success"
            title={t("scout.cameras.list.edit")}
            onClick={onEdit}
          >
            <NmxIconFont symbol={NmxIconFontSymbol.EDIT} size="lg" />
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
              <NmxIconFont symbol={NmxIconFontSymbol.SHARE} size="lg" />
            </NmxButton>
            <NmxButton
              variant="ghost"
              semantic="error"
              title={t("scout.cameras.list.delete")}
              onClick={onDelete}
            >
              <NmxIconFont symbol={NmxIconFontSymbol.DELETE} size="lg" />
            </NmxButton>
          </>
        )}
      </NmxCardFooter>
    </NmxCard>
  )
}
