import {
  createNmxCore,
  createHttpClient,
  createSignalrService,
  createSignalRHooks,
  ApiOauthRoutes,
  type AuthRefreshService,
} from "@namorix/core"

const config = createNmxCore({
  apiBaseUrl: import.meta.env.VITE_API_URL ?? window.location.origin,
  hubsPath: "/hubs/scout",
  isShellDesktop: false,
})

// Scout is an addon guest — its session is the addon OAuth cookie, silently
// refreshed by the backend middleware. The desktop /api/auth/refresh route
// does not exist here: the YARP dev catch-all would forward it back through
// Vite into an infinite proxy loop (Header overflow). Never call it. Instead
// probe /api/oauth/status; on a dead session redirect into the addon OAuth
// flow. Returning "expired" unconditionally would also kill SignalR reconnect
// on transient drops (signalr.service.ts), so only report it when real.
const onUnauthorized: Array<() => void> = []

const authRefresh: AuthRefreshService = {
  async refreshAccessToken() {
    try {
      const res = await fetch(config.getApiBaseUrl() + ApiOauthRoutes.status, {
        credentials: "include",
      })
      if (res.ok) return "success"
      onUnauthorized.forEach((handler) => handler())
      window.location.assign(ApiOauthRoutes.login)
      return "expired"
    } catch {
      return "network"
    }
  },
  setOnUnauthorized(handler) {
    onUnauthorized.push(handler)
  },
}

const http = createHttpClient(authRefresh)
const signalr = createSignalrService({ core: config, authRefresh })
const signalRHooks = createSignalRHooks(signalr)

export const coreConfig = {
  ...config,
  http,
  authRefresh,
  signalr,
  signalRHooks,
}
