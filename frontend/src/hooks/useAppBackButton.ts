import { useEffect } from "react"
import { App } from "@capacitor/app"
import { Capacitor } from "@capacitor/core"
import { exitFullscreen } from "../utils/fullscreen"

// Registering a `backButton` listener makes the App plugin own the hardware back button.
// Without it the OS closes the app even mid-fullscreen: DOM fullscreen lives entirely
// inside the WebView, so there is no native fullscreen state for Android to unwind.
export function useAppBackButton() {
  useEffect(() => {
    if (!Capacitor.isNativePlatform()) return

    const handle = App.addListener("backButton", () => {
      console.log("Back")
      if (document.fullscreenElement) {
        void exitFullscreen()
        return
      }

      void App.exitApp()
    })

    return () => {
      void handle.then((listener) => listener.remove())
    }
  }, [])
}
