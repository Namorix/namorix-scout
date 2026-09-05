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
} from "@namorix/ui"
import type { NmxBottomNavigationBarItemData } from "@namorix/ui"
import "./i18n"
import { useAddonMode, useSessionGuard } from "@namorix/core"
import { store } from "./store/store"

type ScoutTab = "live" | "settings"

const TABS: NmxBottomNavigationBarItemData[] = [
  { key: "live", icon: NmxIconFontSymbol.CAMERA, label: "scout.nav.live" },
  { key: "settings", icon: NmxIconFontSymbol.SETTING, label: "scout.nav.settings" },
]

export const ScoutApp: React.FC = () => {
  const { t } = useTranslation()
  const addonMode = useAddonMode()
  const guard = useSessionGuard()

  if (guard === "loading") return <NmxLoadingOverlay />
  if (guard === "unauthorized") return null

  return (
    <Provider store={store}>
      <NmxAddonRoot>
        <NmxTabProvider defaultTab="live">
          <NmxBottomNavigationContent<ScoutTab>
            tabKey="live"
            spacingHorizontalDisabled
            spacingVerticalDisabled
          >
            <h1>LiveView · {addonMode}</h1>
          </NmxBottomNavigationContent>
          <NmxBottomNavigationContent<ScoutTab> tabKey="settings">
            <h1>SettingsView · {addonMode}</h1>
          </NmxBottomNavigationContent>
          <NmxBottomNavigationBar items={TABS} t={t} />
        </NmxTabProvider>
      </NmxAddonRoot>
    </Provider>
  )
}
