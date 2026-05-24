FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY EmergencyHub.Backend.sln .
COPY src/Shared/Shared.csproj src/Shared/
COPY src/MedicalService/MedicalService.csproj src/MedicalService/
RUN dotnet restore src/MedicalService/MedicalService.csproj

COPY src/Shared/ src/Shared/
COPY src/MedicalService/ src/MedicalService/
RUN dotnet publish src/MedicalService/MedicalService.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 5006
ENTRYPOINT ["dotnet", "MedicalService.dll"]
