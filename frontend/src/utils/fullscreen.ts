import { Capacitor } from "@capacitor/core"
import { ScreenOrientation } from "@capacitor/screen-orientation"

// Fullscreen and rotation are separate APIs, so leaving fullscreen always has to undo
// both — everywhere, not just via the on-screen button.
export async function exitFullscreen() {
  if (!document.fullscreenElement) return

  await document.exitFullscreen()

  if (Capacitor.isNativePlatform()) {
    await ScreenOrientation.unlock().catch(() => {})
  } else {
    screen.orientation?.unlock()
  }
}
