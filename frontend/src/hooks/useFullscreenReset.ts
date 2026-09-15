import { useEffect } from "react"
import { Capacitor, SystemBarType, SystemBars } from "@capacitor/core"
import { ScreenOrientation } from "@capacitor/screen-orientation"

// Fullscreen mutates two pieces of native state that outlive a WebView reload — the
// Activity's requested orientation and the window's hidden system bars — while
// `document.fullscreenElement` does not. When Android recreates the app in the
// background, both stay applied with no fullscreen left to release them, so they are
// re-aligned with the DOM on every entry.
export function useFullscreenReset() {
  useEffect(() => {
    if (!Capacitor.isNativePlatform()) return
    if (document.fullscreenElement) return

    void ScreenOrientation.unlock().catch(() => {})
    SystemBars.show({ bar: SystemBarType.StatusBar }).catch((err) =>
      console.error("[Scout] SystemBars toggle failed", err),
    )
  }, [])
}
