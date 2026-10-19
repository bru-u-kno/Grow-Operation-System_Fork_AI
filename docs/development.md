# Entwicklung

## Arbeitsregeln

- Nicht direkt auf `main` arbeiten.
- Pro Ticket eine klar benannte Branch verwenden.
- Nur den explizit genannten Scope bearbeiten.
- Keine breiten Refactors ohne vorherige Abstimmung.
- Keine API-, DTO-, Model- oder Schema-Aenderungen nebenbei.
- Keine Secrets, lokalen Datenbanken, Uploads oder `App_Data` committen.
- Build- und Teststatus am Ende dokumentieren.

## Standardbefehle

Backend Build:

```powershell
dotnet build GrowDiary.Web/GrowDiary.Web.csproj -v:minimal
```

Backend Tests:

```powershell
dotnet test GrowDiary.Web.Tests/GrowDiary.Web.Tests.csproj -v:minimal
```

Frontend Build, wenn Frontend betroffen ist:

```powershell
cd GrowDiary.React
npm run build
```

PowerShell-Fallback fuer npm:

```powershell
& 'C:\Program Files\nodejs\npm.cmd' run build
```

## Pull Requests

Ein guter PR enthaelt:

- kurze Beschreibung der Aenderung
- betroffene Bereiche
- ausgefuehrte Builds und Tests
- Hinweis auf Datenbank-/Schema-Aenderungen, falls vorhanden
- Screenshots oder visuelle Pruefung, wenn UI betroffen ist
- Security-/Runtime-Hinweise, wenn `App_Data`, Home Assistant, Uploads oder Remote-Zugriff betroffen sind

## Merge-Checkliste

Vor Merge:

- Branch ist nicht `main`.
- `git status` enthaelt nur erwartete Dateien.
- Backend Build ist gruen.
- Backend Tests sind gruen.
- Frontend Build ist gruen, falls Frontend betroffen ist.
- Keine lokalen Runtime-Daten oder Secrets sind enthalten.
- Keine generierten Artefakte wurden versehentlich committed.

## Security und lokale Daten

Nicht committen:

- `GrowDiary.Web/App_Data`
- `GrowDiary.Web/App_Data/ha-config.json`
- SQLite-Dateien, WAL und SHM
- DataProtectionKeys
- Snapshots, Fotos und Uploads
- echte Logs mit privaten Daten

Cultivate Control laeuft als Home-Assistant-Add-on hinter dem Ingress-Proxy; Home Assistant uebernimmt Authentifizierung und Remote-Zugriff (Web/App). Der Add-on-Port ist ingress-only und wird nicht ins Netzwerk veroeffentlicht.

## Offene Punkte

- formaler Security-Policy-Prozess
- Issue Templates
- Release-Checkliste fuer reproduzierbare ZIP-/Docker-/systemd-Pruefung
