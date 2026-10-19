# syntax=docker/dockerfile:1

# Build stages pinned to the native build platform: the frontend is static JS
# and `dotnet publish /p:UseAppHost=false` emits CPU-agnostic IL, so we compile
# once on the runner and only the final runtime layer is per-target-arch.
# This keeps multi-arch (arm64/arm/v7/amd64) builds fast (no QEMU for compiles).
FROM --platform=$BUILDPLATFORM node:20-bookworm-slim AS frontend-build
WORKDIR /src/GrowDiary.React

COPY GrowDiary.React/package*.json ./
RUN npm ci

COPY GrowDiary.React/ ./
RUN mkdir -p ../GrowDiary.Web/wwwroot && npm run build

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS publish
WORKDIR /src

COPY GrowDiary.Web/GrowDiary.Web.csproj GrowDiary.Web/
RUN dotnet restore GrowDiary.Web/GrowDiary.Web.csproj

COPY GrowDiary.Web/ GrowDiary.Web/
COPY --from=frontend-build /src/GrowDiary.Web/wwwroot/ GrowDiary.Web/wwwroot/
RUN dotnet publish GrowDiary.Web/GrowDiary.Web.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://0.0.0.0:5076
# All mutable data (db, uploads, photos, snapshots, keys) lives under the persistent
# /data volume so it survives updates and is captured by Home Assistant backups.
ENV GROWDIARY_DATA_PATH=/data
ENV GROWDIARY_DB_PATH=/data/grow-diary.db

EXPOSE 5076
VOLUME ["/data"]

COPY --from=publish /app/publish ./
COPY LICENSE THIRD-PARTY-NOTICES.md /licenses/
COPY LICENSES/ /licenses/LICENSES/
ENTRYPOINT ["dotnet", "GrowDiary.Web.dll"]
