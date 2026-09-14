import React, { useEffect, useState } from "react"
import { useTranslation } from "react-i18next"
import { ApiError, formatApiError, nmxToast } from "@namorix/core"
import {
  NmxAlertDialog,
  NmxFormField,
  NmxFormInput,
  NmxSelect,
  NmxToggle,
  type NmxSelectData,
  NmxForm,
} from "@namorix/ui"
import { cameraController } from "../../controllers/camera.controller"
import type { Camera, CameraStreamType, CameraUpsert } from "../../types/camera"

interface CameraFormDialogProps {
  open: boolean
  camera: Camera | null
  onClose: () => void
  onSaved: (camera: Camera) => void
}

interface CameraFormState {
  name: string
  rtspUrl: string
  username: string
  password: string
  streamType: CameraStreamType
  enabled: boolean
  recordEnabled: boolean
  retentionDays: string
}

const initialForm = (camera: Camera | null): CameraFormState => ({
  name: camera?.name ?? "",
  rtspUrl: camera?.rtspUrl ?? "",
  username: camera?.username ?? "",
  password: "",
  streamType: camera?.streamType ?? "main",
  enabled: camera?.enabled ?? true,
  recordEnabled: camera?.recordEnabled ?? false,
  retentionDays: camera ? String(camera.retentionDays) : "7",
})

export const CameraFormDialog: React.FC<CameraFormDialogProps> = ({
  open,
  camera,
  onClose,
  onSaved,
}) => {
  const { t } = useTranslation()
  const [form, setForm] = useState<CameraFormState>(() => initialForm(camera))
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    if (!open) return
    setForm(initialForm(camera))
    setBusy(false)
  }, [open, camera])

  const streamOptions: NmxSelectData<CameraStreamType>[] = [
    { value: "main", label: t("scout.cameras.stream.main") },
    { value: "sub", label: t("scout.cameras.stream.sub") },
  ]

  const submit = async () => {
    if (busy) return

    const name = form.name.trim()
    if (!name) {
      nmxToast.error(t("scout.cameras.errors.nameRequired"))
      return
    }

    const retentionDays = Number.parseInt(form.retentionDays, 10)
    if (
      Number.isNaN(retentionDays) ||
      retentionDays < 1 ||
      retentionDays > 365
    ) {
      nmxToast.error(t("scout.cameras.errors.retentionRange"))
      return
    }

    const username = form.username.trim()
    if (form.password && !username) {
      nmxToast.error(t("scout.cameras.errors.passwordNeedsUser"))
      return
    }

    const request: CameraUpsert = {
      name,
      rtspUrl: form.rtspUrl.trim(),
      username,
      password: form.password,
      streamType: form.streamType,
      enabled: form.enabled,
      recordEnabled: form.recordEnabled,
      retentionDays,
    }

    setBusy(true)

    try {
      const saved = camera
        ? await cameraController.update(camera.id, request)
        : await cameraController.create(request)
      setBusy(false)
      onSaved(saved)
    } catch (err) {
      setBusy(false)
      nmxToast.error(
        err instanceof ApiError
          ? (formatApiError(t, err) ?? err.message)
          : err instanceof Error
            ? err.message
            : String(err),
      )
    }
  }

  return (
    <NmxAlertDialog
      open={open}
      title={
        camera
          ? t("scout.cameras.form.editTitle")
          : t("scout.cameras.form.addTitle")
      }
      confirmLabel={t("scout.cameras.form.save")}
      closeLabel={t("scout.cameras.form.cancel")}
      onClose={onClose}
      onConfirm={() => void submit()}
      loading={busy}
    >
      <NmxForm>
        <NmxFormField
          label={t("scout.cameras.form.nameLabel")}
          controlId="camera-name"
          required
        >
          <NmxFormInput
            id="camera-name"
            value={form.name}
            placeholder={t("scout.cameras.form.namePlaceholder")}
            onValueChange={(value) =>
              setForm((prev) => ({ ...prev, name: value }))
            }
            disabled={busy}
          />
        </NmxFormField>

        <NmxFormField
          label={t("scout.cameras.form.urlLabel")}
          controlId="camera-rtsp-url"
          required
        >
          <NmxFormInput
            id="camera-rtsp-url"
            value={form.rtspUrl}
            placeholder={t("scout.cameras.form.urlPlaceholder")}
            onValueChange={(value) =>
              setForm((prev) => ({ ...prev, rtspUrl: value }))
            }
            disabled={busy}
          />
        </NmxFormField>

        <NmxFormField
          label={t("scout.cameras.form.usernameLabel")}
          controlId="camera-username"
        >
          <NmxFormInput
            id="camera-username"
            value={form.username}
            placeholder={t("scout.cameras.form.usernamePlaceholder")}
            onValueChange={(value) =>
              setForm((prev) => ({ ...prev, username: value }))
            }
            disabled={busy}
          />
        </NmxFormField>

        <NmxFormField
          label={t("scout.cameras.form.passwordLabel")}
          controlId="camera-password"
          helper={
            camera?.hasCredentials
              ? t("scout.cameras.form.passwordHelper")
              : undefined
          }
        >
          <NmxFormInput
            id="camera-password"
            type="password"
            value={form.password}
            placeholder={t("scout.cameras.form.passwordPlaceholder")}
            onValueChange={(value) =>
              setForm((prev) => ({ ...prev, password: value }))
            }
            disabled={busy}
          />
        </NmxFormField>

        <NmxFormField label={t("scout.cameras.form.streamLabel")}>
          <NmxSelect<CameraStreamType>
            value={form.streamType}
            options={streamOptions}
            onChange={(value) =>
              setForm((prev) => ({ ...prev, streamType: value }))
            }
            disabled={busy}
          />
        </NmxFormField>

        <NmxFormField
          label={t("scout.cameras.form.retentionLabel")}
          controlId="camera-retention"
        >
          <NmxFormInput
            id="camera-retention"
            type="number"
            value={form.retentionDays}
            placeholder={t("scout.cameras.form.retentionPlaceholder")}
            onValueChange={(value) =>
              setForm((prev) => ({ ...prev, retentionDays: value }))
            }
            disabled={busy}
          />
        </NmxFormField>

        <NmxToggle
          label={t("scout.cameras.form.enabledLabel")}
          checked={form.enabled}
          onCheckedChanged={(checked) =>
            setForm((prev) => ({ ...prev, enabled: checked }))
          }
          disabled={busy}
        />
        <NmxToggle
          label={t("scout.cameras.form.recordLabel")}
          checked={form.recordEnabled}
          onCheckedChanged={(checked) =>
            setForm((prev) => ({ ...prev, recordEnabled: checked }))
          }
          disabled={busy}
        />
      </NmxForm>
    </NmxAlertDialog>
  )
}
