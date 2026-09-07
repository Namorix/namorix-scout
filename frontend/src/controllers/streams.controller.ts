import { ApiError } from "@namorix/core"
import { coreConfig } from "../config/coreConfig"
import { ScoutApiRoutes } from "../scoutApiRoutes"

export interface StreamOffer {
  sessionId: string
  sdp: string
  payloadType: number
}

export interface StreamIceList {
  candidates: string[]
}

const base = () => coreConfig.getApiBaseUrl()

async function offer(cameraId: string): Promise<StreamOffer> {
  const data = await coreConfig.http
    .url(`${base()}${ScoutApiRoutes.streams.offer(cameraId)}`)
    .post()
    .json<StreamOffer>()
  if (!data.success) throw ApiError.fromResponse(data)
  return data.data
}

async function answer(sessionId: string, sdp: string): Promise<void> {
  const data = await coreConfig.http
    .url(`${base()}${ScoutApiRoutes.streams.answer(sessionId)}`)
    .post({ sdp })
    .json<null>()
  if (!data.success) throw ApiError.fromResponse(data)
}

async function iceAdd(sessionId: string, candidate: string): Promise<void> {
  const data = await coreConfig.http
    .url(`${base()}${ScoutApiRoutes.streams.ice(sessionId)}`)
    .post({ candidate })
    .json<null>()
  if (!data.success) throw ApiError.fromResponse(data)
}

async function iceList(sessionId: string): Promise<StreamIceList> {
  const data = await coreConfig.http
    .url(`${base()}${ScoutApiRoutes.streams.ice(sessionId)}`)
    .get()
    .json<StreamIceList>()
  if (!data.success) throw ApiError.fromResponse(data)
  return data.data
}

async function stop(sessionId: string): Promise<void> {
  const data = await coreConfig.http
    .url(`${base()}${ScoutApiRoutes.streams.stop(sessionId)}`)
    .delete()
    .json<null>()
  if (!data.success) throw ApiError.fromResponse(data)
}

export const streamsController = {
  offer,
  answer,
  iceAdd,
  iceList,
  stop,
}
