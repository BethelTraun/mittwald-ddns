FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["mittwald-ddns/mittwald-ddns.csproj", "mittwald-ddns/"]
RUN dotnet restore "mittwald-ddns/mittwald-ddns.csproj"
COPY . .
WORKDIR "/src/mittwald-ddns"
RUN dotnet build "./mittwald-ddns.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./mittwald-ddns.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "mittwald-ddns.dll"]
