FROM node:22-alpine AS build
RUN corepack enable && corepack prepare pnpm@latest --activate

ARG ADDON_FRONTEND_PORT=5302
ENV ADDON_FRONTEND_PORT=${ADDON_FRONTEND_PORT}
ARG ADDON_ID=namorix-scout
ENV ADDON_ID=${ADDON_ID}

WORKDIR /app
COPY . /app/${ADDON_ID}/frontend/

COPY --from=namorix frontend/packages/core /app/namorix/frontend/packages/core/
COPY --from=namorix frontend/tsconfig.base.json /app/namorix/frontend/tsconfig.base.json
COPY --from=namorix frontend/packages/styles /app/namorix/frontend/packages/styles/
COPY --from=namorix frontend/packages/ui /app/namorix/frontend/packages/ui/

# Install all Namorix shared frontend packages as one workspace so their
# cross-package deps resolve when the addon bundles their TS source
# (@namorix/ui imports @namorix/core and @floating-ui/react).
WORKDIR /app/namorix/frontend
RUN printf 'packages:\n  - "packages/*"\nallowBuilds:\n  esbuild: true\n  "@parcel/watcher": true\n' > pnpm-workspace.yaml \
    && printf '{\n  "name": "namorix-frontend-packages",\n  "private": true,\n  "version": "0.0.0"\n}\n' > package.json \
    && CI=true pnpm install

WORKDIR /app/${ADDON_ID}/frontend
RUN printf 'allowBuilds:\n  esbuild: true\n  "@parcel/watcher": true\n' > pnpm-workspace.yaml
RUN CI=true pnpm install
RUN pnpm build

FROM node:22-alpine

ARG ADDON_FRONTEND_PORT=5302
ENV ADDON_FRONTEND_PORT=${ADDON_FRONTEND_PORT}
ARG ADDON_ID=namorix-scout
ENV ADDON_ID=${ADDON_ID}

WORKDIR /app
COPY --from=build /app/${ADDON_ID}/frontend/dist ./dist
RUN npm install -g serve

EXPOSE ${ADDON_FRONTEND_PORT}
CMD serve dist -l ${ADDON_FRONTEND_PORT} --cors
