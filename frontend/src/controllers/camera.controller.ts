import { ApiError } from "@namorix/core"
import { coreConfig } from "../config/coreConfig"
import { ScoutApiRoutes } from "../scoutApiRoutes"
import type { Camera, CameraUpsert } from "../types/camera"

async function list(): Promise<Camera[]> {
  const data = await coreConfig.http
    .url(`${coreConfig.getApiBaseUrl()}${ScoutApiRoutes.cameras}`)
    .get()
    .json<Camera[]>()
  if (!data.success) throw ApiError.fromResponse(data)
  return data.data
}

async function get(id: string): Promise<Camera> {
  const data = await coreConfig.http
    .url(`${coreConfig.getApiBaseUrl()}${ScoutApiRoutes.cameraById(id)}`)
    .get()
    .json<Camera>()
  if (!data.success) throw ApiError.fromResponse(data)
  return data.data
}

async function create(request: CameraUpsert): Promise<Camera> {
  const data = await coreConfig.http
    .url(`${coreConfig.getApiBaseUrl()}${ScoutApiRoutes.cameras}`)
    .post(request)
    .json<Camera>()
  if (!data.success) throw ApiError.fromResponse(data)
  return data.data
}

async function update(id: string, request: CameraUpsert): Promise<Camera> {
  const data = await coreConfig.http
    .url(`${coreConfig.getApiBaseUrl()}${ScoutApiRoutes.cameraById(id)}`)
    .put(request)
    .json<Camera>()
  if (!data.success) throw ApiError.fromResponse(data)
  return data.data
}

async function remove(id: string): Promise<void> {
  const data = await coreConfig.http
    .url(`${coreConfig.getApiBaseUrl()}${ScoutApiRoutes.cameraById(id)}`)
    .delete()
    .json<null>()
  if (!data.success) throw ApiError.fromResponse(data)
}

export const cameraController = {
  list,
  get,
  create,
  update,
  remove,
}
