FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY EmergencyHub.Backend.sln .
COPY src/Shared/Shared.csproj src/Shared/
COPY src/Auth.Blacklist/Auth.Blacklist.csproj src/Auth.Blacklist/
COPY src/Gateway/Gateway.csproj src/Gateway/
RUN dotnet restore src/Gateway/Gateway.csproj

COPY src/Shared/ src/Shared/
COPY src/Auth.Blacklist/ src/Auth.Blacklist/
COPY src/AuthService/Protos/ src/AuthService/Protos/
COPY src/EmergencyService/Protos/ src/EmergencyService/Protos/
COPY src/PoliceService/Protos/ src/PoliceService/Protos/
COPY src/FireService/Protos/ src/FireService/Protos/
COPY src/MedicalService/Protos/ src/MedicalService/Protos/
COPY src/AssessmentService/Protos/ src/AssessmentService/Protos/
COPY src/Gateway/ src/Gateway/
RUN dotnet publish src/Gateway/Gateway.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "Gateway.dll"]
