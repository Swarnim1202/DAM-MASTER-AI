# ─── Stage 1: Build React / Vite frontend ────────────────────────────────────
FROM node:20-slim AS frontend-build
WORKDIR /app/frontend

COPY frontend/package*.json ./
RUN npm ci

COPY frontend/ ./
# Override the outDir from vite.config.ts so the build lands in a known Docker path
RUN npx vite build --outDir /tmp/wwwroot --emptyOutDir

# ─── Stage 2: Build ASP.NET Core backend ─────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS backend-build
WORKDIR /app

COPY backend/DamMaster.sln ./
COPY backend/DamMaster.Api/ ./DamMaster.Api/

RUN dotnet restore DamMaster.Api/DamMaster.Api.csproj
RUN dotnet publish DamMaster.Api/DamMaster.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Copy the frontend build into wwwroot of the published app
COPY --from=frontend-build /tmp/wwwroot /app/publish/wwwroot

# Copy database migration scripts so a startup script can run them
COPY database/ /app/publish/database/

# ─── Stage 3: Runtime image ───────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Install mysql-client for the entrypoint migration runner
RUN apt-get update && apt-get install -y --no-install-recommends default-mysql-client && \
    rm -rf /var/lib/apt/lists/*

COPY --from=backend-build /app/publish ./

# Entrypoint script that runs DB migrations then starts the API
COPY entrypoint.sh ./
RUN chmod +x entrypoint.sh

# Render injects PORT env var; ASP.NET Core reads ASPNETCORE_URLS
ENV ASPNETCORE_URLS=http://+:10000
EXPOSE 10000

ENTRYPOINT ["./entrypoint.sh"]
