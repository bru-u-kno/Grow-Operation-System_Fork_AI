# KI-Assistent

> Fork AI (04.10.2026). Die eine Stelle für alles, was mit einer eigenen KI zu
> tun hat: verbinden, Zugriff und Schlüssel, Mappe. In Grow OS steckt weiterhin
> keine KI — die Seite verbindet den Assistenten des Bedieners, sie bringt
> keinen mit.

## Wo in der App

| Was | Wo |
|---|---|
| Menü „Einrichtung" → „KI-Assistent" | `/ki` → `KiAssistentSeite` (`features/ki-assistent/`) |
| Reiter „Verbinden": die Wege zum eigenen Assistenten | `/ki?tab=verbinden` → `KiVerbinden.tsx` |
| Reiter „Zugriff & Schlüssel": Hauptschalter, Höchstwerte, Schlüssel, Protokoll | `/ki?tab=zugriff` → `KiZugriffAbschnitt` (bis forkai.164 unten in `/settings`) |
| Reiter „Mappe": ZIP mit Lagebericht und Fachwissen | `/ki?tab=mappe` → `MappeReiter.tsx` (alt: `/berater`, leitet um) |
| Einstellungen: nur noch ein Wegweiser | `/settings`, Abschnitt „KI-Assistent" |
| Grow-Seite: Reiter „Mappe für eigene KI" | `/grows/<id>` → `/ki?growId=<id>&tab=mappe` |

## Was es tut

Vorher lag das an drei Orten: die Mappe unter Wissen, der Zugriff für
KI-Assistenten unten in den Einstellungen, die Einrichtung des Grow MCP in der
Seitenleiste von Home Assistant — und wie man einen Assistenten überhaupt
verbindet, stand nirgends in Grow OS (Bru: „irreführend").

**Verbinden** beschreibt vier Wege, alle über das **eigene Konto** des
Bedieners — die meisten haben den kostenlosen Plan oder ein Abo, keinen
API-Schlüssel (Bru, 04.10.2026):

| Weg | Plan | Wie |
|---|---|---|
| Claude-App (Windows, Mac, Android, iPhone, Browser) | kostenlos oder Abo | Connector → Add-on Home Assistant MCP Server mit Webhook Proxy → `ha_manage_app` im Proxy-Modus auf Grow OS, mit `gok_`-Schlüssel. Die Seite liefert die Projektanweisung mit dem echten Add-on-Namen (aus `/api/system/mobile-access`). **Geht an Grow OS vorbei:** der HA MCP Server hat selbst vollen Zugriff auf Home Assistant; Stufen, Höchstwerte und Protokoll gelten nur für Anfragen über Grow OS. Die Seite sagt das und empfiehlt OAuth im Webhook Proxy. |
| Claude Code im Heimnetz | Abo | Add-on Grow MCP Fork AI (Port 5080), im Befehl der `gok_`-Schlüssel statt des MCP-Schlüssels |
| ChatGPT | Plus oder Pro | Entwicklermodus, derselbe Weg wie die Claude-App — ausdrücklich als nicht erprobt gekennzeichnet |
| Mappe | jeder Plan | ohne Verbindung, Stand von jetzt |

**Zugriff & Schlüssel** und **Mappe** sind unverändert umgezogen; was sie tun,
steht in `docs/ki-zugriff.md` bzw. in `aufgaben-journal-mcp.md`.

## Die Zahlen und woher sie kommen

Keine eigenen. Port 5080 steht in `grow-mcp/config.yaml`, der Add-on-Name kommt
vom Supervisor (`SupervisorInfoService`), Stufen und Höchstwerte aus
`/api/settings/ki-zugriff`.

## Was es bewusst NICHT tut

- **Keine Zugangsdaten zu Claude oder ChatGPT.** Ein Abo lässt sich in fremder
  Software nicht verwenden, und Grow OS ruft selbst keine KI auf. Der Weg geht
  umgekehrt: der Assistent bekommt einen Schlüssel von hier.
- **Keinen Weg versprechen, den es noch nicht gibt.** Ein direkter Connector zu
  Grow OS (ohne Home Assistant MCP dazwischen) ist geplant, steht aber erst auf
  der Seite, wenn er funktioniert.
- **Keine Grenze versprechen, die nicht greift.** „Was ein Grow-OS-Schlüssel nie kann"
  gilt für Wege durch Grow OS (auch den Grow MCP Fork AI), nicht für einen Connector,
  der selbst an Home Assistant geht — die Seite sagt das ausdrücklich (Prüfer 04.10.2026).
- **Den Schlüssel nicht in die Anleitung schreiben.** Die Texte zum Kopieren
  tragen `gok_…` als Platzhalter; der Klartext erscheint nur einmal beim Anlegen.

## Im Code

- `GrowDiary.React/src/features/ki-assistent/` — Seite, Verbinden, Mappe
- `GrowDiary.React/src/features/ki-zugriff/` — Schlüssel, Stufen, Protokoll
- `GrowDiary.React/src/navigation.ts` — Menüpunkt `/ki`, Umleitung `/berater`
- `GrowDiary.React/e2e/ki-assistent.spec.ts`, `e2e/ki-zugriff-rundweg.spec.ts`
