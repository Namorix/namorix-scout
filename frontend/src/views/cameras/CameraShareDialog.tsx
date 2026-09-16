import React, { useEffect, useState } from "react"
import { useTranslation } from "react-i18next"
import { ApiError, formatCustomError, nmxToast } from "@namorix/core"
import {
  NmxAlertDialog,
  NmxButton,
  NmxForm,
  NmxFormField,
  NmxIconFont,
  NmxIconFontSymbol,
  NmxSelect,
  type NmxSelectData,
} from "@namorix/ui"
import { cameraController } from "../../controllers/camera.controller"
import { userController } from "../../controllers/user.controller"
import type {
  Camera,
  CameraShare,
  CameraSharePermission,
  ScoutUser,
} from "../../types/camera"
import { CameraErrorCodes } from "../live/CameraErrorCodes"

interface CameraShareDialogProps {
  open: boolean
  camera: Camera | null
  onClose: () => void
}

export const CameraShareDialog: React.FC<CameraShareDialogProps> = ({
  open,
  camera,
  onClose,
}) => {
  const { t } = useTranslation()
  const [shares, setShares] = useState<CameraShare[]>([])
  const [loadFailed, setLoadFailed] = useState(false)
  const [busy, setBusy] = useState(false)
  const [users, setUsers] = useState<ScoutUser[]>([])
  const [usersFailed, setUsersFailed] = useState(false)
  // NmxSelect is generic over strings, so the id is carried as one and parsed when it is sent.
  const [selectedId, setSelectedId] = useState("")
  const [permission, setPermission] = useState<CameraSharePermission>("view")

  // Keyed on the id rather than the camera object: the store hands back a new object on every
  // refresh, and re-running this would wipe what the owner is halfway through typing.
  const cameraId = camera?.id ?? null

  useEffect(() => {
    if (!open || cameraId === null) return

    setSelectedId("")
    setPermission("view")
    setBusy(false)
    setLoadFailed(false)
    setUsersFailed(false)

    cameraController
      .listShares(cameraId)
      .then(setShares)
      .catch(() => setLoadFailed(true))

    // Fetched once per open, not per keystroke: the list is narrowed in the browser, so there
    // is nothing to debounce and no request to race.
    userController
      .list()
      .then(setUsers)
      .catch(() => {
        setUsers([])
        setUsersFailed(true)
      })
  }, [open, cameraId])

  const add = async () => {
    if (!camera || busy || selectedId === "") return
    setBusy(true)

    try {
      const share = await cameraController.addShare(camera.id, {
        userId: Number(selectedId),
        permission,
      })
      // Add is an upsert on (camera, user), so an existing row is replaced rather than doubled.
      setShares((prev) => [
        ...prev.filter((s) => s.userId !== share.userId),
        share,
      ])
      setSelectedId("")
      nmxToast.success(t("scout.cameras.share.added"))
    } catch (err) {
      report(err)
    } finally {
      setBusy(false)
    }
  }

  const revoke = async (targetUserId: number) => {
    if (!camera || busy) return
    setBusy(true)

    try {
      await cameraController.removeShare(camera.id, targetUserId)
      setShares((prev) => prev.filter((s) => s.userId !== targetUserId))
      nmxToast.success(t("scout.cameras.share.revoked"))
    } catch (err) {
      report(err)
    } finally {
      setBusy(false)
    }
  }

  // formatCustomError hands back the ApiError itself when no translation matches, so the
  // server's own sentence ("The owner already has access…") still reaches the toast.
  const report = (err: unknown) => {
    nmxToast.error(
      err instanceof ApiError
        ? formatCustomError(t, err, CameraErrorCodes)
        : err instanceof Error
          ? err.message
          : String(err),
    )
  }

  const describeUser = (user: {
    userId: number
    username?: string | null
    name?: string | null
  }) =>
    user.name ||
    user.username ||
    t("scout.cameras.share.user", { id: user.userId })

  const permissionOptions: NmxSelectData<CameraSharePermission>[] = [
    { value: "view", label: t("scout.cameras.share.permission.view") },
    { value: "manage", label: t("scout.cameras.share.permission.manage") },
  ]

  const personOptions: NmxSelectData[] = users.map((user) => ({
    value: String(user.userId),
    label: user.username,
    description: describeUser(user),
  }))

  return (
    <NmxAlertDialog
      open={open}
      title={t("scout.cameras.share.title", { name: camera?.name ?? "" })}
      confirmLabel={t("scout.cameras.share.add")}
      confirmDisabled={selectedId === ""}
      closeLabel={t("scout.cameras.form.cancel")}
      onClose={onClose}
      onConfirm={() => void add()}
      loading={busy}
    >
      <NmxForm>
        <NmxFormField label={t("scout.cameras.share.personLabel")}>
          <NmxSelect<string>
            value={selectedId}
            options={personOptions}
            placeholder={t("scout.cameras.share.personPlaceholder")}
            onChange={setSelectedId}
            disabled={busy || personOptions.length === 0}
          />
        </NmxFormField>

        <NmxFormField label={t("scout.cameras.share.permissionLabel")}>
          <NmxSelect<CameraSharePermission>
            value={permission}
            options={permissionOptions}
            onChange={setPermission}
            disabled={busy}
          />
        </NmxFormField>

        {usersFailed && (
          <span className="scout-share-list__empty">
            {t("scout.cameras.share.usersLoadFailed")}
          </span>
        )}
      </NmxForm>

      <div className="scout-share-list">
        <span className="scout-share-list__title">
          {t("scout.cameras.share.current")}
        </span>
        {shares.length === 0 ? (
          <span className="scout-share-list__empty">
            {loadFailed
              ? t("scout.cameras.share.loadFailed")
              : t("scout.cameras.share.empty")}
          </span>
        ) : (
          shares.map((share) => (
            <div className="scout-share-row" key={share.userId}>
              <div className="scout-share-row__who">
                <span className="scout-share-row__user">{share.username}</span>
                <span className="scout-share-row__permission">
                  {t(`scout.cameras.share.permission.${share.permission}`)}
                </span>
              </div>
              <NmxButton
                variant="ghost"
                semantic="error"
                title={t("scout.cameras.share.revoke")}
                onClick={() => void revoke(share.userId)}
                disabled={busy}
              >
                <NmxIconFont symbol={NmxIconFontSymbol.DELETE} size="md" />
              </NmxButton>
            </div>
          ))
        )}
      </div>
    </NmxAlertDialog>
  )
}
