FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY EmergencyHub.Backend.sln .
COPY src/Shared/Shared.csproj src/Shared/
COPY src/PoliceService/PoliceService.csproj src/PoliceService/
RUN dotnet restore src/PoliceService/PoliceService.csproj

COPY src/Shared/ src/Shared/
COPY src/PoliceService/ src/PoliceService/
RUN dotnet publish src/PoliceService/PoliceService.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 5004
ENTRYPOINT ["dotnet", "PoliceService.dll"]
