FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY . . 

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
ENV DOTNET_NOLOGO=true
ENV ASPNETCORE_ENVIRONMENT=Production

# 🔥 IMPORTANT FIX
RUN dotnet restore

# warnings ko fail na banne do
RUN dotnet publish -c Release -o /app/publish /p:WarningLevel=0 /p:TreatWarningsAsErrors=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "SmartStockERP.dll"]