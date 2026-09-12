import type { CapacitorConfig } from "@capacitor/cli"

const config: CapacitorConfig = {
  appId: "izerocs.namorix.scout",
  appName: "Namorix Scout",
  webDir: "dist",
  server: {
    url: "https://scout.namorix.online",
    androidScheme: "https",
    cleartext: true,
    allowNavigation: ["namorix.online", "*.namorix.online"],
  },
}

export default config
