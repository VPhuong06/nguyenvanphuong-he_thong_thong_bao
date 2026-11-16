# Build context: thư mục gốc repository (xem docker-compose.yml)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Notification.Shared/Notification.Shared.csproj src/Notification.Shared/
COPY src/Campaign.Api/Campaign.Api.csproj src/Campaign.Api/
RUN dotnet restore src/Campaign.Api/Campaign.Api.csproj
COPY src/Notification.Shared/ src/Notification.Shared/
COPY src/Campaign.Api/ src/Campaign.Api/
RUN dotnet publish src/Campaign.Api/Campaign.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Campaign.Api.dll"]
