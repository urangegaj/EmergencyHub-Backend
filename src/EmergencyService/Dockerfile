FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY EmergencyHub.Backend.sln .
COPY src/Shared/Shared.csproj src/Shared/
COPY src/EmergencyService/EmergencyService.csproj src/EmergencyService/
RUN dotnet restore src/EmergencyService/EmergencyService.csproj

COPY src/Shared/ src/Shared/
COPY src/EmergencyService/ src/EmergencyService/
RUN dotnet publish src/EmergencyService/EmergencyService.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 5005
ENTRYPOINT ["dotnet", "EmergencyService.dll"]
