# Build context is the repository root. Use scripts/build-image.sh, which names the image the way the extension
# expects it; the equivalent command is:
#   docker build -f src/StorageExplorer.Web/Dockerfile -t <registry>/<image>:<tag> .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/StorageExplorer.Web/StorageExplorer.Web.csproj src/StorageExplorer.Web/
RUN dotnet restore src/StorageExplorer.Web/StorageExplorer.Web.csproj

COPY src/StorageExplorer.Web/ src/StorageExplorer.Web/
RUN dotnet publish src/StorageExplorer.Web/StorageExplorer.Web.csproj \
    --configuration Release --no-restore --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "StorageExplorer.Web.dll"]
