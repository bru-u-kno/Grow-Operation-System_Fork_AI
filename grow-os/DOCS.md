# Grow OS — Home Assistant Add-on

Runs [Grow OS](https://github.com/Nerdstreak/Grow-Operation-System) directly inside
Home Assistant. Because Grow OS relies on Home Assistant for all sensor data, this
is the simplest way to install it: one click, no separate server, no manual token.

## Installation

1. In Home Assistant, go to **Settings → Add-ons → Add-on Store**.
2. Top-right **⋮ → Repositories**, add:
   `https://github.com/Nerdstreak/Grow-Operation-System`
3. **Grow OS** appears in the store — click **Install**, then **Start**.
4. On this add-on's **Info** page, enable **"Show in sidebar"** so Grow OS appears in
   the Home Assistant sidebar (🌱 Grow OS).
5. Open it from the sidebar.

## What "native integration" means here

- **No manual connection.** As an add-on, Grow OS receives a Supervisor token and
  reaches Home Assistant at `http://supervisor/core` automatically. You never paste
  a URL or a long-lived access token.
- **Pick sensors from a dropdown.** Grow OS reads your Home Assistant entities and
  lets you choose them from a searchable list (filtered by device class), instead
  of typing entity IDs.
- **Automatic backups.** The Grow OS database lives on the add-on's `/data` volume,
  which is included in Home Assistant snapshots.

## Data & persistence

All Grow OS data (SQLite database, uploads, snapshots) is stored under `/data` and
survives restarts and updates. Uninstalling the add-on removes this data — take a
Home Assistant backup first if you want to keep it.

## Notes

- Install and updates pull a prebuilt image (no on-device build), so they are quick
  and updates never require a reinstall — your data on `/data` is preserved.
- Requires a Home Assistant OS or Supervised installation (add-ons are not
  available on Home Assistant Container or Core installs).

## Zugriff für KI-Assistenten

Ein KI-Assistent (etwa Claude über das Home-Assistant-MCP oder den Grow MCP Fork AI) kann
Einträge selbst vornehmen, wenn du es erlaubst. Ab Werk ist das aus.

1. **Einstellungen → Zugriff für KI-Assistenten:** „Zugriff erlauben" anhaken, speichern.
2. **Neuer Schlüssel:** Namen vergeben, Stufen anhaken, anlegen. Den angezeigten Schlüssel
   (`gok_…`) sofort kopieren — er wird nur dieses eine Mal gezeigt.
3. Den Schlüssel deinem Assistenten geben. Er schickt ihn bei jeder Anfrage als
   `Authorization: Bearer gok_…` mit; `GET /api/ki-zugriff/ich` sagt ihm, welche Stufen frei
   sind, ab wann er nachfragen soll und welche Höchstwerte gelten.

| Stufe | Was der Assistent damit darf |
|---|---|
| Dokumentieren | Messungen, Journal, Beobachtungen, Aufgaben abhaken, Wartung, Kosten, Einkaufsliste, Meldungen quittieren |
| Grow planen | Phase wechseln, Zielwerte, Misch-, Licht- und Wochenplan, Pflanzen und Sorten |
| Geräte schalten | Licht, Klima und Dosierpumpen sofort auslösen |
| Verwaltung | Einstellungen, Sicherung anlegen, Import und Export, Stammdaten löschen — damit lassen sich auch Dienste in Home Assistant auslösen. Sicherungen zurückspielen oder herunterladen geht nur von Hand. |

Ein Schlüssel wird nur aus dem internen Add-on-Netz angenommen. Geht er verloren: sperren oder
löschen und einen neuen anlegen. Was über einen Schlüssel geändert wurde, steht im
Prüfprotokoll.
