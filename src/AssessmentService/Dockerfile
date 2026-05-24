FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY EmergencyHub.Backend.sln .
COPY src/Shared/Shared.csproj src/Shared/
COPY src/AssessmentService/AssessmentService.csproj src/AssessmentService/
RUN dotnet restore src/AssessmentService/AssessmentService.csproj

COPY src/Shared/ src/Shared/
COPY src/EmergencyService/Protos/ src/EmergencyService/Protos/
COPY src/PoliceService/Protos/ src/PoliceService/Protos/
COPY src/FireService/Protos/ src/FireService/Protos/
COPY src/MedicalService/Protos/ src/MedicalService/Protos/
COPY src/AssessmentService/ src/AssessmentService/
RUN dotnet publish src/AssessmentService/AssessmentService.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 5007
ENTRYPOINT ["dotnet", "AssessmentService.dll"]
