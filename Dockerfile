FROM node:22-bookworm-slim AS web
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim AS build
WORKDIR /src
COPY server/StitchHelper.csproj server/
RUN dotnet restore server/StitchHelper.csproj
COPY server/ server/
COPY data/thread-catalog/ data/thread-catalog/
COPY --from=web /src/server/wwwroot/ server/wwwroot/
ARG APP_VERSION=0.2.0
RUN dotnet publish server/StitchHelper.csproj -c Release -o /app --no-restore /p:Version=$APP_VERSION

FROM mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim AS runtime
WORKDIR /app
COPY --from=build /app/ ./
ENV ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "StitchHelper.dll"]
