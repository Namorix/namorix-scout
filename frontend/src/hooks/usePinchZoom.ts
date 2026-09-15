import { useEffect, type RefObject } from "react"

const MIN_SCALE = 1
const MAX_SCALE = 4
const DOUBLE_TAP_MS = 300

interface Point {
  x: number
  y: number
}

interface PinchStart {
  distance: number
  scale: number
  mid: Point
  offset: Point
}

function spread(touches: TouchList): number | null {
  const a = touches[0]
  const b = touches[1]
  if (!a || !b) return null
  return Math.hypot(a.clientX - b.clientX, a.clientY - b.clientY)
}

function centre(touches: TouchList): Point | null {
  const a = touches[0]
  const b = touches[1]
  if (!a || !b) return null
  return { x: (a.clientX + b.clientX) / 2, y: (a.clientY + b.clientY) / 2 }
}

// Pinch-to-zoom, two-finger pan and double-tap reset for one element. Enabled only while
// it is worth having — the caller turns it on for fullscreen and the transform is cleared
// again on the way out, so the next session never starts zoomed.
export function usePinchZoom(
  targetRef: RefObject<HTMLElement | null>,
  enabled: boolean,
) {
  useEffect(() => {
    const el = targetRef.current
    if (!el || !enabled) return

    let scale = MIN_SCALE
    let offset: Point = { x: 0, y: 0 }
    let pinch: PinchStart | null = null
    let pan: { from: Point; offset: Point } | null = null
    let lastTapAt = 0

    // The gesture runs per frame, so it feeds CSS variables rather than React state —
    // re-rendering the tree that often would stutter the video.
    const paint = () => {
      el.style.setProperty("--scout-zoom-scale", String(scale))
      el.style.setProperty("--scout-zoom-x", `${offset.x}px`)
      el.style.setProperty("--scout-zoom-y", `${offset.y}px`)
    }

    // Scaling spills half of the extra size past each edge; that is as far as it can pan.
    const clamp = () => {
      const maxX = (el.offsetWidth * (scale - 1)) / 2
      const maxY = (el.offsetHeight * (scale - 1)) / 2
      offset = {
        x: Math.min(maxX, Math.max(-maxX, offset.x)),
        y: Math.min(maxY, Math.max(-maxY, offset.y)),
      }
    }

    const onTouchStart = (event: TouchEvent) => {
      const first = event.touches[0]
      if (!first) return

      if (event.touches.length >= 2) {
        const distance = spread(event.touches)
        const mid = centre(event.touches)
        if (distance === null || mid === null) return
        pinch = { distance, scale, mid, offset }
        pan = null
        return
      }

      // A tap landing inside the window is the second half of a double tap, not a pan.
      if (event.timeStamp - lastTapAt < DOUBLE_TAP_MS) {
        lastTapAt = 0
        scale = MIN_SCALE
        offset = { x: 0, y: 0 }
        paint()
        return
      }

      lastTapAt = event.timeStamp
      pan = { from: { x: first.clientX, y: first.clientY }, offset }
    }

    const onTouchMove = (event: TouchEvent) => {
      const first = event.touches[0]
      if (!first) return

      if (pinch && event.touches.length >= 2) {
        const distance = spread(event.touches)
        const mid = centre(event.touches)
        if (distance === null || mid === null) return
        scale = Math.min(
          MAX_SCALE,
          Math.max(MIN_SCALE, (pinch.scale * distance) / pinch.distance),
        )
        // The midpoint drifts as fingers move, which is what makes pinch-and-drag work.
        offset = {
          x: pinch.offset.x + (mid.x - pinch.mid.x),
          y: pinch.offset.y + (mid.y - pinch.mid.y),
        }
        clamp()
        paint()
        event.preventDefault()
        return
      }

      if (pan && scale > MIN_SCALE) {
        offset = {
          x: pan.offset.x + (first.clientX - pan.from.x),
          y: pan.offset.y + (first.clientY - pan.from.y),
        }
        clamp()
        paint()
        event.preventDefault()
      }
    }

    const onTouchEnd = (event: TouchEvent) => {
      pinch = null
      const first = event.touches[0]
      // One finger left after a pinch — carry on panning from where it sits now.
      pan = first
        ? { from: { x: first.clientX, y: first.clientY }, offset }
        : null
    }

    el.addEventListener("touchstart", onTouchStart, { passive: true })
    el.addEventListener("touchmove", onTouchMove, { passive: false })
    el.addEventListener("touchend", onTouchEnd)
    el.addEventListener("touchcancel", onTouchEnd)

    return () => {
      el.removeEventListener("touchstart", onTouchStart)
      el.removeEventListener("touchmove", onTouchMove)
      el.removeEventListener("touchend", onTouchEnd)
      el.removeEventListener("touchcancel", onTouchEnd)
      el.style.removeProperty("--scout-zoom-scale")
      el.style.removeProperty("--scout-zoom-x")
      el.style.removeProperty("--scout-zoom-y")
    }
  }, [targetRef, enabled])
}
