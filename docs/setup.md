# Setup

## Voraussetzungen

- .NET SDK 8
- Node.js und npm
- Git
- moderner Browser
- optional, aber fuer den Zielbetrieb empfohlen: Home Assistant

SQLite laeuft eingebettet ueber `Microsoft.Data.Sqlite`. Es ist kein separater SQLite-Server noetig.

## Repository vorbereiten

```powershell
git clone https://github.com/Nerdstreak/Grow-Operation-System.git
cd Grow-Operation-System
```

Wenn bereits eine lokale Arbeitskopie existiert, reicht der Wechsel in den Repo-Root.

## Backend starten

```powershell
dotnet run --project GrowDiary.Web/GrowDiary.Web.csproj
```

Standard-URLs:

- `http://localhost:5076`
- im Heimnetz je nach Firewall: `http://<server-ip>:5076`

## Frontend im Entwicklungsmodus

In einem zweiten Terminal:

```powershell
cd GrowDiary.React
npm install
npm run dev
```

Vite laeuft standardmaessig unter `http://127.0.0.1:5173` und proxyt `/api` an das Backend auf Port `5076`.

Falls PowerShell `npm.ps1` blockiert:

```powershell
& 'C:\Program Files\nodejs\npm.cmd' run dev
```

## Lokaler Production-Start

Frontend bauen:

```powershell
cd GrowDiary.React
npm install
npm run build
```

Danach im Repo-Root:

```powershell
dotnet run --project GrowDiary.Web/GrowDiary.Web.csproj
```

Der Vite-Build schreibt nach `GrowDiary.Web/wwwroot`; das Backend liefert die SPA direkt aus.

## Tests und Builds

Backend Build:

```powershell
dotnet build GrowDiary.Web/GrowDiary.Web.csproj -v:minimal
```

Backend Tests:

```powershell
dotnet test GrowDiary.Web.Tests/GrowDiary.Web.Tests.csproj -v:minimal
```

Frontend Build:

```powershell
cd GrowDiary.React
npm run build
```

## Datenbank und lokale Daten

Standardpfad:

```text
GrowDiary.Web/App_Data/grow-diary.db
```

Optional kann ein eigener Datenbankpfad gesetzt werden:

```powershell
$env:GROWDIARY_DB_PATH="D:\GrowOSData\grow-diary.db"
```

`GrowDiary.Web/App_Data` enthaelt lokale Runtime-Daten und sollte nicht committed werden.

## Erster Start

Beim ersten Start legt die App lokale Runtime-Daten an. Danach:

1. Einstellungen unter `/settings` oeffnen.
2. Home Assistant Base URL und Long-Lived Access Token eintragen, falls HA genutzt wird.
3. Zelte anlegen.
4. Sensor-Mapping pro Zelt konfigurieren.
5. Hydro-Setups und Grows anlegen.

Alternativ kann `GrowDiary.Web/App_Data/ha-config.example.json` als Vorlage fuer eine lokale `ha-config.json` genutzt werden.

## Home Assistant

Cultivate Control kann ohne konfigurierte Home-Assistant-Verbindung starten, der volle Nutzen entsteht aber mit HA. Wichtig ist, dass der Grow-OS-Server Home Assistant erreichen kann, nicht nur das Handy oder der Browser.

Typische Base URLs:

- `http://homeassistant.local:8123`
- `http://192.168.x.x:8123`

Tokens, lokale `ha-config.json`, Datenbanken, Snapshots und Uploads nicht committen.
