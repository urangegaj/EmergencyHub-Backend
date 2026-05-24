FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY EmergencyHub.Backend.sln .
COPY src/Shared/Shared.csproj src/Shared/
COPY src/NotificationService/NotificationService.csproj src/NotificationService/
RUN dotnet restore src/NotificationService/NotificationService.csproj

COPY src/Shared/ src/Shared/
COPY src/AuthService/Protos/ src/AuthService/Protos/
COPY src/EmergencyService/Protos/ src/EmergencyService/Protos/
COPY src/NotificationService/ src/NotificationService/
RUN dotnet publish src/NotificationService/NotificationService.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "NotificationService.dll"]
