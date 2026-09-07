import React, { useState } from "react"
import { useTranslation } from "react-i18next"
import { formatCustomError, nmxToast } from "@namorix/core"
import {
  NmxAlign,
  NmxAlertDialog,
  NmxButton,
  NmxButtonRefresh,
  NmxCard,
  NmxCardBody,
  NmxGrid,
  NmxIconFont,
  NmxIconFontSymbol,
  NmxToolbar,
  NmxToolbarContainer,
  NmxToolbarContent,
} from "@namorix/ui"
import { cameraController } from "../../controllers/camera.controller"
import { useCameras } from "../../hooks/useCameras"
import { IDLE, useLiveStreams } from "../../hooks/useLiveStreams"
import { useAppDispatch } from "../../store/hooks"
import { cameraActions } from "../../store/slices/cameraSlice"
import type { Camera } from "../../types/camera"
import { CameraErrorCodes } from "./CameraErrorCodes"
import { CameraFormDialog } from "./CameraFormDialog"
import { CameraLiveCard } from "./CameraLiveCard"
import "./LiveView.scss"

type FormTarget = { mode: "create" } | { mode: "edit"; camera: Camera }

export const LiveView: React.FC = () => {
  const { t } = useTranslation()
  const dispatch = useAppDispatch()
  const { cameras, loading, loadFailed, refresh } = useCameras()
  const { statuses, play, stop } = useLiveStreams(cameras)
  const [formTarget, setFormTarget] = useState<FormTarget | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<Camera | null>(null)
  const [deleting, setDeleting] = useState(false)

  const statusOf = (id: string) => statuses[id] ?? IDLE
  const renderCard = (camera: Camera) => (
    <CameraLiveCard
      camera={camera}
      status={statusOf(camera.id)}
      onPlay={() => play(camera.id)}
      onStop={() => stop(camera.id)}
      onEdit={() => setFormTarget({ mode: "edit", camera })}
      onDelete={() => setDeleteTarget(camera)}
    />
  )

  const editingCamera = formTarget?.mode === "edit" ? formTarget.camera : null
  const formOpen = formTarget !== null
  const closeForm = () => setFormTarget(null)

  const onSaved = (camera: Camera) => {
    dispatch(cameraActions.upsertCamera(camera))
    setFormTarget(null)
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
        nmxToast.success(t("scout.cameras.delete.success"))
      })
      .catch((err) =>
        nmxToast.error(formatCustomError(t, err, CameraErrorCodes)),
      )
      .finally(() => setDeleting(false))
  }

  const renderEmpty = () => (
    <NmxCard>
      <NmxCardBody isEmpty>
        {loadFailed ? (
          <NmxAlign direction="column" gap="md">
            <span>{t("scout.cameras.errors.loadFailed")}</span>
            <NmxButton
              variant="outline"
              label={t("scout.cameras.retry")}
              onClick={() => void refresh()}
            />
          </NmxAlign>
        ) : (
          <span>{t("scout.live.empty")}</span>
        )}
      </NmxCardBody>
    </NmxCard>
  )

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

          {cameras.length === 0 ? (
            loading ? null : (
              renderEmpty()
            )
          ) : (
            <NmxGrid minColWidth={320} gap="md">
              {cameras.map((camera) => (
                <div key={camera.id}>{renderCard(camera)}</div>
              ))}
            </NmxGrid>
          )}

          <CameraFormDialog
            open={formOpen}
            camera={editingCamera}
            onClose={closeForm}
            onSaved={onSaved}
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
