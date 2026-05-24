FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY EmergencyHub.Backend.sln .
COPY src/Shared/Shared.csproj src/Shared/
COPY src/FireService/FireService.csproj src/FireService/
RUN dotnet restore src/FireService/FireService.csproj

COPY src/Shared/ src/Shared/
COPY src/FireService/ src/FireService/
RUN dotnet publish src/FireService/FireService.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 5003
ENTRYPOINT ["dotnet", "FireService.dll"]
