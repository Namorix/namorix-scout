import React, { useState } from "react"
import { useTranslation } from "react-i18next"
import { formatCustomError, nmxToast, type TFunction } from "@namorix/core"
import {
  NmxAlign,
  NmxAlertDialog,
  NmxBadge,
  NmxButton,
  NmxButtonRefresh,
  NmxDataTable,
  NmxIconFont,
  NmxIconFontSymbol,
  NmxMenuButton,
  NmxToolbar,
  NmxToolbarContainer,
  NmxToolbarContent,
  type NmxDataTableColumn,
  type NmxFallback,
  type NmxSemanticColor,
} from "@namorix/ui"
import { cameraController } from "../../controllers/camera.controller"
import { useCameras } from "../../hooks/useCameras"
import { useAppDispatch } from "../../store/hooks"
import { cameraActions } from "../../store/slices/cameraSlice"
import type { Camera, CameraRuntimeState } from "../../types/camera"
import { CameraErrorCodes } from "../live/CameraErrorCodes"
import { CameraFormDialog } from "./CameraFormDialog"
import { CameraInfoDialog } from "./CameraInfoDialog"
import { CameraShareDialog } from "./CameraShareDialog"
import "./CamerasView.scss"

type FormTarget = { mode: "create" } | { mode: "edit"; camera: Camera }
type CameraAction = "info" | "edit" | "share" | "delete"

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

function accessSemantic(access: Camera["access"]): NmxSemanticColor {
  if (access === "view") return "info"
  if (access === "manage") return "warning"
  return "success"
}

export const CamerasView: React.FC = () => {
  const { t } = useTranslation()
  const dispatch = useAppDispatch()
  const { cameras, loading, loadFailed, refresh } = useCameras()
  const [formTarget, setFormTarget] = useState<FormTarget | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<Camera | null>(null)
  const [infoTarget, setInfoTarget] = useState<Camera | null>(null)
  const [shareTarget, setShareTarget] = useState<Camera | null>(null)
  const [deleting, setDeleting] = useState(false)

  const editingCamera = formTarget?.mode === "edit" ? formTarget.camera : null

  const onSaved = (camera: Camera) => {
    dispatch(cameraActions.upsertCamera(camera))
    setFormTarget(null)
    setInfoTarget(null)
    nmxToast.success(t("scout.cameras.form.saved"))
  }

  const closeDelete = () => {
    if (!deleting) setDeleteTarget(null)
  }

  const submitDelete = () => {
    if (!deleteTarget || deleting) return
    setDeleting(true)
    cameraController
      .remove(deleteTarget.id)
      .then(() => {
        dispatch(cameraActions.removeCamera(deleteTarget.id))
        setDeleteTarget(null)
        setInfoTarget(null)
        nmxToast.success(t("scout.cameras.delete.success"))
      })
      .catch((err) =>
        nmxToast.error(formatCustomError(t, err, CameraErrorCodes)),
      )
      .finally(() => setDeleting(false))
  }

  const handleAction = (camera: Camera, action: CameraAction) => {
    if (action === "info") setInfoTarget(camera)
    else if (action === "edit") setFormTarget({ mode: "edit", camera })
    else if (action === "share") setShareTarget(camera)
    else setDeleteTarget(camera)
  }

  const columns: NmxDataTableColumn<Camera>[] = [
    {
      header: t("scout.cameras.list.name"),
      grow: 1.5,
      renderCell: (camera) => camera.name,
    },
    {
      header: t("scout.cameras.list.url"),
      grow: 2,
      renderCell: (camera) => camera.rtspUrl ?? t("scout.cameras.info.none"),
    },
    {
      header: t("scout.cameras.list.stream"),
      hideBelow: "md",
      renderCell: (camera) => t(`scout.cameras.stream.${camera.streamType}`),
      alignHeader: "center",
      alignCell: "center",
    },
    {
      header: t("scout.cameras.list.active"),
      renderCell: (camera) => (
        <NmxBadge semantic={camera.enabled ? "success" : "default"} size="sm">
          {camera.enabled
            ? t("scout.cameras.list.enabled")
            : t("scout.cameras.list.disabled")}
        </NmxBadge>
      ),
      alignHeader: "center",
      alignCell: "center",
    },
    {
      header: t("scout.cameras.list.state"),
      renderCell: (camera) => renderStateBadge(camera.state, t),
      hideBelow: "md",
      alignHeader: "center",
      alignCell: "center",
    },
    {
      header: t("scout.cameras.access.label"),
      hideBelow: "lg",
      renderCell: (camera) => (
        <NmxBadge semantic={accessSemantic(camera.access)} size="sm">
          {t(`scout.cameras.access.${camera.access}`)}
        </NmxBadge>
      ),
      alignHeader: "center",
      alignCell: "center",
    },
    {
      header: t("scout.cameras.list.record"),
      hideBelow: "lg",
      renderCell: (camera) => (
        <NmxBadge
          semantic={camera.recordEnabled ? "success" : "error"}
          size="sm"
        >
          {camera.recordEnabled
            ? t("scout.cameras.list.recordOn")
            : t("scout.cameras.list.recordOff")}
        </NmxBadge>
      ),
      alignHeader: "center",
      alignCell: "center",
    },
    {
      header: t("scout.cameras.list.retention"),
      hideBelow: "lg",
      renderCell: (camera) => String(camera.retentionDays),
      alignHeader: "center",
      alignCell: "center",
    },
    {
      header: "",
      btnIsMenu: true,
      alignCell: "end",
      renderCell: (camera) => (
        <NmxMenuButton<CameraAction>
          variant="ghost"
          semantic="trace"
          arrowDisabled
          options={[
            { value: "info", label: t("scout.cameras.list.info") },
            { value: "edit", label: t("scout.cameras.list.edit") },
            { value: "share", label: t("scout.cameras.list.share") },
            {
              value: "delete",
              label: t("scout.cameras.list.delete"),
              semantic: "error",
            },
          ]}
          filterItem={(option) =>
            option.value === "info" ||
            (option.value === "edit"
              ? camera.access !== "view"
              : camera.access === "owner")
          }
          dividerIndexes={
            camera.access === "owner"
              ? [{ value: "delete", position: "top" }]
              : undefined
          }
          onSelect={(action) => handleAction(camera, action)}
        >
          <NmxIconFont symbol={NmxIconFontSymbol.MENU_VERTICAL} />
        </NmxMenuButton>
      ),
    },
  ]

  // Polling flips `loading` every 10s; only take over the table when there is nothing to show,
  // otherwise the rows blink out and back on each refresh.
  const fallbackConditions: NmxFallback[] = [
    {
      state: "loading",
      condition: loading && cameras.length === 0,
      content: t("scout.cameras.list.loading"),
    },
    {
      state: "error",
      condition: !loading && loadFailed && cameras.length === 0,
      content: t("scout.cameras.errors.loadFailed"),
    },
    {
      state: "empty",
      condition: !loading && !loadFailed && cameras.length === 0,
      content: t("scout.cameras.empty"),
    },
  ]

  return (
    <NmxToolbar>
      <NmxToolbarContainer>
        <NmxToolbarContent spacing="md">
          <NmxAlign direction="row" justify="end">
            <NmxButton
              semantic="success"
              onClick={() => setFormTarget({ mode: "create" })}
            >
              <NmxIconFont symbol={NmxIconFontSymbol.ADD} />
              <span>{t("scout.cameras.add")}</span>
            </NmxButton>
            <NmxButtonRefresh
              title={t("scout.cameras.refresh")}
              onClick={() => void refresh()}
            />
          </NmxAlign>

          <NmxDataTable
            columns={columns}
            rows={cameras}
            clickableRows
            onRowClick={(camera) => setInfoTarget(camera)}
            fallbackConditions={fallbackConditions}
            radiusEnabled={true}
            className="scout-cameras-table"
          />

          <CameraFormDialog
            open={formTarget !== null}
            camera={editingCamera}
            onClose={() => setFormTarget(null)}
            onSaved={onSaved}
          />

          <CameraInfoDialog
            open={infoTarget !== null}
            camera={infoTarget}
            onClose={() => setInfoTarget(null)}
            onEdit={() => {
              if (infoTarget)
                setFormTarget({ mode: "edit", camera: infoTarget })
            }}
          />

          <CameraShareDialog
            open={shareTarget !== null}
            camera={shareTarget}
            onClose={() => setShareTarget(null)}
          />

          <NmxAlertDialog
            open={deleteTarget !== null}
            title={t("scout.cameras.delete.title")}
            size="sm"
            confirmLabel={t("scout.cameras.delete.confirm")}
            confirmSemantic="error"
            closeLabel={t("scout.cameras.form.cancel")}
            onClose={closeDelete}
            onConfirm={submitDelete}
            loading={deleting}
          >
            <p>
              {t("scout.cameras.delete.body", {
                name: deleteTarget?.name ?? "",
              })}
            </p>
          </NmxAlertDialog>
        </NmxToolbarContent>
      </NmxToolbarContainer>
    </NmxToolbar>
  )
}
