import React from "react"
import { Provider } from "react-redux"
import { useTranslation } from "react-i18next"
import {
  NmxAddonRoot,
  NmxBottomNavigationBar,
  NmxBottomNavigationContent,
  NmxIconFontSymbol,
  NmxLoadingOverlay,
  NmxTabProvider,
  NmxToastProvider,
} from "@namorix/ui"
import type { NmxBottomNavigationBarItemData } from "@namorix/ui"
import "./i18n"
import { useSessionGuard } from "@namorix/core"
import { store } from "./store/store"
import { CamerasView } from "./views/cameras/CamerasView"
import { LiveView } from "./views/live/LiveView"
import { SettingsView } from "./views/settings/SettingsView"

type ScoutTab = "device" | "live" | "settings"

const TABS: NmxBottomNavigationBarItemData[] = [
  {
    key: "device",
    icon: NmxIconFontSymbol.DEVICE,
    label: "scout.nav.device",
  },
  {
    key: "live",
    icon: NmxIconFontSymbol.CAMERA,
    label: "scout.nav.live",
  },
  {
    key: "settings",
    icon: NmxIconFontSymbol.SETTING,
    label: "scout.nav.settings",
  },
]

export const ScoutApp: React.FC = () => {
  const { t } = useTranslation()
  const guard = useSessionGuard()

  if (guard.state === "loading") return <NmxLoadingOverlay />
  if (guard.state === "unauthorized") return null

  return (
    <Provider store={store}>
      <NmxAddonRoot>
        <NmxToastProvider />
        <NmxTabProvider defaultTab="live">
          <NmxBottomNavigationContent<ScoutTab>
            tabKey="device"
            spacingHorizontalDisabled
            spacingVerticalDisabled
          >
            <CamerasView />
          </NmxBottomNavigationContent>
          <NmxBottomNavigationContent<ScoutTab>
            tabKey="live"
            spacingHorizontalDisabled
            spacingVerticalDisabled
          >
            <LiveView />
          </NmxBottomNavigationContent>
          <NmxBottomNavigationContent<ScoutTab> tabKey="settings">
            <SettingsView />
          </NmxBottomNavigationContent>
          <NmxBottomNavigationBar items={TABS} t={t} />
        </NmxTabProvider>
      </NmxAddonRoot>
    </Provider>
  )
}
