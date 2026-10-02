# syntax=docker/dockerfile:1

# Wie beim Hauptprogramm: uebersetzt wird einmal auf dem Bau-Rechner
# (`UseAppHost=false` liefert CPU-unabhaengiges IL), nur die Laufzeitschicht ist
# je Zielarchitektur. Das haelt den Mehrfach-Bau schnell, ohne QEMU.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS publish
WORKDIR /src

COPY GrowOsAccess/GrowOsAccess.csproj GrowOsAccess/
COPY GrowMcp/GrowMcp.csproj GrowMcp/
RUN dotnet restore GrowMcp/GrowMcp.csproj

COPY GrowOsAccess/ GrowOsAccess/
COPY GrowMcp/ GrowMcp/
RUN dotnet publish GrowMcp/GrowMcp.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=publish /app/publish .

# Die Ports stehen fest im Programm (siehe GrowMcp/Tueren.cs) — welcher Port was
# darf, ist eine Sicherheitsfrage und keine Umgebungsvariable.
ENV DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 5078 5080
ENTRYPOINT ["dotnet", "GrowMcp.dll"]
