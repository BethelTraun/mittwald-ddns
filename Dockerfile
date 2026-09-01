FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

ENV DOTNET_EnableDiagnostics=0 \
    MITTWALD_CONFIG_PATH=/config/mittwald-ddns.json

RUN mkdir -p /config && chown -R "$APP_UID" /app /config
USER $APP_UID

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["MittwaldDdns/MittwaldDdns.csproj", "MittwaldDdns/"]
RUN dotnet restore "MittwaldDdns/MittwaldDdns.csproj"

COPY ["MittwaldDdns/", "MittwaldDdns/"]
WORKDIR /src/MittwaldDdns
RUN dotnet publish "MittwaldDdns.csproj" \
    --configuration "$BUILD_CONFIGURATION" \
    --output /app/publish

FROM runtime AS final
COPY --from=build /app/publish .
VOLUME ["/config"]
ENTRYPOINT ["./mittwald-ddns"]
