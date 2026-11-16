# Build context: thư mục gốc repository (xem docker-compose.yml)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Notification.Shared/Notification.Shared.csproj src/Notification.Shared/
COPY src/Notification.Worker/Notification.Worker.csproj src/Notification.Worker/
RUN dotnet restore src/Notification.Worker/Notification.Worker.csproj
COPY src/Notification.Shared/ src/Notification.Shared/
COPY src/Notification.Worker/ src/Notification.Worker/
RUN dotnet publish src/Notification.Worker/Notification.Worker.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "Notification.Worker.dll"]
