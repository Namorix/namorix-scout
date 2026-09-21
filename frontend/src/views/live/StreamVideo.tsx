import React, { useEffect, useRef } from "react"

interface StreamVideoProps {
  className?: string
  videoRef?: React.Ref<HTMLVideoElement>
}

// The element is handed to the stream client, which owns the MediaSource it plays - this
// component only renders the tag and keeps playback alive across tab switches.
export const StreamVideo: React.FC<StreamVideoProps> = ({ className, videoRef }) => {
  const ownRef = useRef<HTMLVideoElement | null>(null)
  const ref = videoRef ?? ownRef

  // Browsers pause a playing element when the tab is hidden; resume it on return. The
  // client feed is live, so there is nothing to seek - the video picks up at the edge.
  useEffect(() => {
    const resume = () => {
      if (document.visibilityState !== "visible") return
      const video = ref.current
      if (!video || !video.paused) return
      void video.play().catch(() => undefined)
    }

    document.addEventListener("visibilitychange", resume)
    window.addEventListener("focus", resume)
    return () => {
      document.removeEventListener("visibilitychange", resume)
      window.removeEventListener("focus", resume)
    }
  }, [ref])

  return (
    <video
      ref={ref}
      className={className}
      controls={false}
      muted
      autoPlay
      playsInline
    />
  )
}
