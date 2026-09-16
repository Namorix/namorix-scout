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
import { useAppDispatch } from "../../store/hooks"
import { cameraActions } from "../../store/slices/cameraSlice"
import type { Camera } from "../../types/camera"
import { CameraErrorCodes } from "../live/CameraErrorCodes"
import { CameraFormDialog } from "./CameraFormDialog"
import { CameraInfoDialog } from "./CameraInfoDialog"
import { CameraManageCard } from "./CameraManageCard"
import { CameraShareDialog } from "./CameraShareDialog"
import "./CamerasView.scss"

type FormTarget = { mode: "create" } | { mode: "edit"; camera: Camera }

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
                    <span>{t("scout.cameras.empty")}</span>
                  )}
                </NmxCardBody>
              </NmxCard>
            )
          ) : (
            <NmxGrid minColWidth={320} gap="md" cols={2}>
              {cameras.map((camera) => (
                <div key={camera.id}>
                  <CameraManageCard
                    camera={camera}
                    onInfo={() => setInfoTarget(camera)}
                    onEdit={() => setFormTarget({ mode: "edit", camera })}
                    onDelete={() => setDeleteTarget(camera)}
                    onShare={() => setShareTarget(camera)}
                  />
                </div>
              ))}
            </NmxGrid>
          )}

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
