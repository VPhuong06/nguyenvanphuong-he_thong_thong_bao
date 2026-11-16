# Build context: thư mục gốc repository (xem docker-compose.yml)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Notification.Shared/Notification.Shared.csproj src/Notification.Shared/
COPY src/Admin.Web/Admin.Web.csproj src/Admin.Web/
RUN dotnet restore src/Admin.Web/Admin.Web.csproj
COPY src/Notification.Shared/ src/Notification.Shared/
COPY src/Admin.Web/ src/Admin.Web/
RUN dotnet publish src/Admin.Web/Admin.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Admin.Web.dll"]
