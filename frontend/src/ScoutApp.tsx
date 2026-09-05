import React from "react"
import { Provider } from "react-redux"
import { useTranslation } from "react-i18next"
import {
  NmxRail,
  NmxRailList,
  NmxRailContent,
  NmxIconFontSymbol,
  NmxLoadingOverlay,
} from "@namorix/ui"
import type { NmxRailItemData } from "@namorix/ui"
import "./i18n"
import { useAddonMode, useIsStandalone, useSessionGuard } from "@namorix/core"
import { store } from "./store/store"

type ScoutTab = "dashboard" | "devices" | "settings"

const TABS: NmxRailItemData<ScoutTab>[] = [
  { key: "dashboard", icon: NmxIconFontSymbol.HOME, label: "Dashboard" },
  { key: "devices", icon: NmxIconFontSymbol.DEVICE, label: "Devices" },
  { key: "settings", icon: NmxIconFontSymbol.SETTING, label: "Settings" },
]

export const ScoutApp: React.FC = () => {
  const { t } = useTranslation()
  const isStandalone = useIsStandalone()
  const guard = useSessionGuard()
  const addonMode = useAddonMode()

  if (guard === "loading") return <NmxLoadingOverlay />
  if (guard === "unauthorized") return null

  return (
    <Provider store={store}>
      <NmxRail<ScoutTab> defaultTab="dashboard">
        <NmxRailList
          title={t("scout.title")}
          items={TABS}
          t={t}
          showToggle={isStandalone}
        />
        <NmxRailContent<ScoutTab> tabKey="dashboard">
          <h1>DashboardView: {addonMode}</h1>
        </NmxRailContent>
        <NmxRailContent<ScoutTab> tabKey="devices">
          <h1>DevicesView</h1>
        </NmxRailContent>
        <NmxRailContent<ScoutTab> tabKey="settings">
          <h1>SettingsView</h1>
        </NmxRailContent>
      </NmxRail>
    </Provider>
  )
}
