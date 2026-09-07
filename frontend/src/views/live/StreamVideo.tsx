import React, { useEffect, useRef } from "react"

interface StreamVideoProps {
  stream: MediaStream | null
  className?: string
}

export const StreamVideo: React.FC<StreamVideoProps> = ({ stream, className }) => {
  const videoRef = useRef<HTMLVideoElement | null>(null)

  useEffect(() => {
    const video = videoRef.current
    if (!video) return
    video.srcObject = stream
  }, [stream])

  return <video ref={videoRef} className={className} muted autoPlay playsInline />
}
