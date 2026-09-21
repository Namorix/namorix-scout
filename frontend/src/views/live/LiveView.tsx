import React from "react"
import { useTranslation } from "react-i18next"
import {
  NmxAlign,
  NmxButton,
  NmxCard,
  NmxCardBody,
  NmxGrid,
  NmxToolbar,
  NmxToolbarContainer,
  NmxToolbarContent,
} from "@namorix/ui"
import { useCameras } from "../../hooks/useCameras"
import { IDLE, useLiveStreams } from "../../hooks/useLiveStreams"
import { CameraLiveCard } from "./CameraLiveCard"
import "./LiveView.scss"

export const LiveView: React.FC = () => {
  const { t } = useTranslation()
  const { cameras, loading, loadFailed, refresh } = useCameras()
  const { statuses, paused, play, stop, attachVideo } = useLiveStreams(cameras)

  const statusOf = (id: string) => statuses[id] ?? IDLE
  const pausedOf = (id: string) => paused[id] ?? false

  return (
    <NmxToolbar>
      <NmxToolbarContainer className="scout-live__container">
        <NmxToolbarContent spacing="md" className="scout-live__content">
          {cameras.length === 0 ? (
            loading ? null : (
              <NmxCard>
                <NmxCardBody isEmpty>
                  {loadFailed ? (
                    <NmxAlign direction="column" gap="lg">
                      <span>{t("scout.cameras.errors.loadFailed")}</span>
                      <NmxButton
                        variant="ghost"
                        uppercase={true}
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
          ) : (
            <NmxGrid minColWidth={320} gap="md">
              {cameras.map((camera) => (
                <div key={camera.id}>
                  <CameraLiveCard
                    camera={camera}
                    status={statusOf(camera.id)}
                    paused={pausedOf(camera.id)}
                    onPlay={() => play(camera.id)}
                    onStop={() => stop(camera.id)}
                    onAttachVideo={(video) => attachVideo(camera.id, video)}
                  />
                </div>
              ))}
            </NmxGrid>
          )}
        </NmxToolbarContent>
      </NmxToolbarContainer>
    </NmxToolbar>
  )
}
