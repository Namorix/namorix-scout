import React, { useEffect, useRef } from "react"

interface StreamVideoProps {
  stream: MediaStream | null
  className?: string
  videoRef?: React.RefObject<HTMLVideoElement | null>
}

export const StreamVideo: React.FC<StreamVideoProps> = ({
  stream,
  className,
  videoRef,
}) => {
  const ownRef = useRef<HTMLVideoElement | null>(null)
  const ref = videoRef ?? ownRef

  useEffect(() => {
    const video = ref.current
    if (!video) return
    video.srcObject = stream
    if (stream) void video.play().catch(() => undefined)
  }, [stream, ref])

  // Browsers pause a playing element when the tab is hidden; resume it on return.
  useEffect(() => {
    const resume = () => {
      if (document.visibilityState !== "visible") return
      const video = ref.current
      if (!video || !video.srcObject || !video.paused) return
      void video.play().catch(() => undefined)
    }

    document.addEventListener("visibilitychange", resume)
    window.addEventListener("focus", resume)
    return () => {
      document.removeEventListener("visibilitychange", resume)
      window.removeEventListener("focus", resume)
    }
  }, [ref])

  return <video ref={ref} className={className} muted autoPlay playsInline />
}
