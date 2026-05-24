FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY EmergencyHub.Backend.sln .
COPY src/Shared/Shared.csproj src/Shared/
COPY src/Auth.Blacklist/Auth.Blacklist.csproj src/Auth.Blacklist/
COPY src/AuthService/AuthService.csproj src/AuthService/
RUN dotnet restore src/AuthService/AuthService.csproj

COPY src/Shared/ src/Shared/
COPY src/Auth.Blacklist/ src/Auth.Blacklist/
COPY src/AuthService/ src/AuthService/
RUN dotnet publish src/AuthService/AuthService.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 5001 5002
ENTRYPOINT ["dotnet", "AuthService.dll"]
