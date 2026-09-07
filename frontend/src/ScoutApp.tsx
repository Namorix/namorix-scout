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
import { LiveView } from "./views/live/LiveView"
import { SettingsView } from "./views/settings/SettingsView"

type ScoutTab = "live" | "settings"

const TABS: NmxBottomNavigationBarItemData[] = [
  { key: "live", icon: NmxIconFontSymbol.CAMERA, label: "scout.nav.live" },
  { key: "settings", icon: NmxIconFontSymbol.SETTING, label: "scout.nav.settings" },
]

export const ScoutApp: React.FC = () => {
  const { t } = useTranslation()
  const guard = useSessionGuard()

  if (guard === "loading") return <NmxLoadingOverlay />
  if (guard === "unauthorized") return null

  return (
    <Provider store={store}>
      <NmxAddonRoot>
        <NmxToastProvider />
        <NmxTabProvider defaultTab="live">
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
