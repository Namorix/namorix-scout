import React from "react"
import { useTranslation } from "react-i18next"
import { NmxAlertDialog, NmxMetaItem, NmxMetaList } from "@namorix/ui"
import type { Camera } from "../../types/camera"

interface CameraInfoDialogProps {
  open: boolean
  camera: Camera | null
  onClose: () => void
  onEdit: () => void
}

export const CameraInfoDialog: React.FC<CameraInfoDialogProps> = ({
  open,
  camera,
  onClose,
  onEdit,
}) => {
  const { t } = useTranslation()

  if (!camera) return null

  const yesNo = (value: boolean) =>
    value ? t("scout.cameras.info.yes") : t("scout.cameras.info.no")

  return (
    <NmxAlertDialog
      open={open}
      size="lg"
      title={camera.name}
      closeLabel={t("scout.cameras.info.close")}
      confirmLabel={t("scout.cameras.list.edit")}
      onClose={onClose}
      onConfirm={onEdit}
    >
      <NmxMetaList alignItem="end">
        <NmxMetaItem
          label={t("scout.cameras.list.url")}
          value={camera.rtspUrl}
          useSelectEnabled
        />
        <NmxMetaItem
          label={t("scout.cameras.info.username")}
          value={camera.username ?? t("scout.cameras.info.none")}
        />
        <NmxMetaItem
          label={t("scout.cameras.info.credentials")}
          value={t(
            camera.hasCredentials
              ? "scout.cameras.info.credentialsSet"
              : "scout.cameras.info.credentialsNone",
          )}
        />
        <NmxMetaItem
          label={t("scout.cameras.list.stream")}
          value={t(`scout.cameras.stream.${camera.streamType}`)}
        />
        <NmxMetaItem
          label={t("scout.cameras.list.enabled")}
          value={yesNo(camera.enabled)}
        />
        <NmxMetaItem
          label={t("scout.cameras.list.record")}
          value={yesNo(camera.recordEnabled)}
        />
        <NmxMetaItem
          label={t("scout.cameras.list.retention")}
          value={String(camera.retentionDays)}
        />
        <NmxMetaItem
          label={t("scout.cameras.info.createdAt")}
          value={new Date(camera.createdAt).toLocaleString()}
        />
      </NmxMetaList>
    </NmxAlertDialog>
  )
}
