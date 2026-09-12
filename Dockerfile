# ---------- Stage 1: Frontend (SPA) build ----------
FROM --platform=$BUILDPLATFORM node:22-alpine AS frontend
RUN corepack enable && corepack prepare pnpm@latest --activate

WORKDIR /repo
COPY frontend/ namorix-scout/frontend/

# Namorix shared packages + tsconfig base come from the sibling repo via the
# `namorix` named build context (Makefile: --build-context namorix=../namorix).
COPY --from=namorix frontend/tsconfig.base.json namorix/frontend/tsconfig.base.json
COPY --from=namorix frontend/packages/core namorix/frontend/packages/core/
COPY --from=namorix frontend/packages/styles namorix/frontend/packages/styles/
COPY --from=namorix frontend/packages/ui namorix/frontend/packages/ui/

# Install shared packages as one workspace so cross-package deps resolve when the
# addon bundles their TS/SCSS source (@namorix/ui imports @namorix/core + @floating-ui/react).
WORKDIR /repo/namorix/frontend
RUN printf 'packages:\n  - "packages/*"\nallowBuilds:\n  esbuild: true\n  "@parcel/watcher": true\n' > pnpm-workspace.yaml \
    && printf '{\n  "name": "namorix-frontend-packages",\n  "private": true,\n  "version": "0.0.0"\n}\n' > package.json \
    && pnpm install

WORKDIR /repo/namorix-scout/frontend
RUN printf 'allowBuilds:\n  esbuild: true\n  "@parcel/watcher": true\n  sharp: true\n' > pnpm-workspace.yaml
RUN pnpm install
RUN ADDON_FRONTEND_PORT=5300 pnpm build

# ---------- Stage 2: Backend publish ----------
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /repo
COPY backend/ namorix-scout/backend/
# Namorix.Core (ProjectReference ../../../namorix/...) + central package versions
# must sit beside the scout backend exactly like the sibling repos on disk.
COPY --from=namorix backend/src/Namorix.Core namorix/backend/src/Namorix.Core/
COPY --from=namorix backend/src/Directory.Build.props namorix/backend/src/Directory.Build.props
COPY --from=namorix backend/src/Directory.Packages.props namorix/backend/src/Directory.Packages.props
RUN find namorix/backend/src/Namorix.Core -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
RUN dotnet publish namorix-scout/backend/src/Namorix.Scout.csproj \
    -c Release -o /publish \
    -a $TARGETARCH \
    --self-contained false

# ---------- Stage 3: Runtime — single origin: SPA + /api + /hubs on the addon entry port ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:5300 \
    NMX_DATA_DIR=/data
COPY --from=build /publish .
COPY --from=frontend /repo/namorix-scout/frontend/dist ./public
EXPOSE 5300
ENTRYPOINT ["dotnet", "Namorix.Scout.dll"]
