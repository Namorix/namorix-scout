import { ApiError } from "@namorix/core"
import { coreConfig } from "../config/coreConfig"
import { ScoutApiRoutes } from "../scoutApiRoutes"
import type { ScoutUser } from "../types/camera"

// Backed by the desktop's user table through the addon channel. One page, narrowed in the
// browser: the picker shows every name at once rather than requiring a substring to guess.
async function list(): Promise<ScoutUser[]> {
  const data = await coreConfig.http
    .url(`${coreConfig.getApiBaseUrl()}${ScoutApiRoutes.users}`)
    .get()
    .json<ScoutUser[]>()
  if (!data.success) throw ApiError.fromResponse(data)
  return data.data
}

export const userController = {
  list,
}
