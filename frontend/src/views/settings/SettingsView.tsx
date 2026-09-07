import React from "react"
import { useTranslation } from "react-i18next"

export const SettingsView: React.FC = () => {
  const { t } = useTranslation()

  return <h1>{t("scout.nav.settings")}</h1>
}
