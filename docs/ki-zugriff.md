# Zugriff für KI-Assistenten (A-003)

Stand: 03.10.2026 — Bauplan für forkai.163. Wird nach dem Bau zur Beschreibung.

## Worum es geht

Der Betreiber diktiert seinem KI-Assistenten (Claude) per Sprache, was passiert
ist — „ORP 450, 2 ml pH-Minus, 300 ml Pyrolyt" — und der Assistent trägt es
selbst ein. Bisher scheitert jedes Schreiben eines anderen Add-ons mit 403
`admin_access_required`: aus dem internen Add-on-Netz ist nur GET erlaubt
(`AdminAccessPolicy.IsInternalAddonRead`). Das bleibt so. Neu ist ein
**Schlüssel**, den der Betreiber ausdrücklich anlegt und der genau die Stufen
öffnet, die er anhakt.

Gedacht ist der Fork auch für andere Betreiber. Deshalb: **ab Werk aus**,
vorsichtige Vorbelegung, alles in der Oberfläche einstellbar.

## Stufen (`KiStufe`, Bits)

| Stufe | Wert | Beispiele |
|---|---|---|
| Dokumentieren | 1 | Messung anlegen/ändern, Journal, Beobachtung, Aufgabe abhaken, Wartung/Kalibrierung festhalten, Kosten, Einkaufsliste, Meldung quittieren |
| GrowPlanen | 2 | Phase wechseln, Zielwerte, Mischplan, Lichtplan, Wochenplan, Pflanzen/Sorten/Setups bearbeiten |
| GeraeteSchalten | 4 | Licht-Befehl, AC-Stufe, Dosierpumpe auslösen/stoppen, Probeschaltung, Steuerungs-Einstellungen, die sofort schalten |
| Verwaltung | 8 | Einstellungen, Sicherung anlegen, Import/Export, Löschen von Stammdaten, HA-Automationen/Helfer anlegen, Pumpen einrichten und kalibrieren. Sicherung zurückspielen/herunterladen: nie über einen Schlüssel (Prüfer 03.10.2026) |

Stufen bauen **nicht** aufeinander auf; jede wird einzeln angehakt.
Vorbelegung eines neuen Schlüssels: nur Dokumentieren.

**Einstufen** (`GrowDiary.Web/Infrastructure/KiZugriff/KiStufe.cs`):
- `[KiStufe(KiStufe.X)]` am Controller gilt für alle schreibenden Aktionen; an der Aktion gewinnt es.
- `[KeinKiZugriff("Grund")]` — nie über einen Schlüssel (Schlüsselverwaltung, Fehlerbehandler …).
- `[KiSicherungVorher]` — vor der Ausführung über einen Schlüssel eine Sicherung anlegen
  (Import, alles mit DELETE). Zurückspielen trägt `[KeinKiZugriff]`; beim Zurückspielen von Hand
  bleibt der Zugriffs-Zustand wie vorher (`KiZustandBeimZurueckspielen`).
- Lesen (GET) braucht keine Stufe — das darf das Add-on-Netz schon heute. Ausnahme:
  GET auf Verwaltungswege (`AdminAccessPolicy.IsAdminPath`) braucht Verwaltung.
- Eine schreibende Aktion **ohne** Einstufung ist über einen Schlüssel gesperrt
  (fail closed). `KiStufenVollstaendigTests` zählt über alle Controller-Aktionen
  per Reflexion: jede schreibende hat `KiStufe` oder `KeinKiZugriff` mit Grund.
  Mit Selbsttest (Grundmenge ≥ 150 schreibende Aktionen).
- Im Zweifel die **höhere** Stufe. Was eine HA-Automation oder einen Helfer
  anlegt/überschreibt: Verwaltung. Was ein Gerät sofort schaltet: GeraeteSchalten.

## Weg einer Anfrage

Kopfzeile: `Authorization: Bearer gok_<43 Zeichen base64url>` (32 Zufallsbytes).

1. **Vor dem Routing** (bestehende Sperre in `Program.cs`, `AdminAccessPolicy`):
   Darf die Anfrage ohnehin (Loopback, echter Ingress, lesend aus dem Add-on-Netz
   ohne Schlüssel-Kopf), bleibt alles wie heute. Trägt sie einen `gok_`-Schlüssel
   und kommt aus dem Add-on-Netz (`AddonNetworks`) oder Loopback:
   `KiZugriffDienst.Pruefen(klartext, ip)`. Gültig, Hauptschalter an, Schlüssel
   nicht gesperrt → `context.Items[KiZugriffKontext.ItemKey] = kontext` und weiter.
   Sonst 401 `ki_schluessel_ungueltig` (Fehlversuch zählt) bzw. 403
   `ki_zugriff_aus`. Ein Schlüssel von ausserhalb des Add-on-Netzes wird nie geprüft (403 wie heute).
2. **Nach `UseRouting`** (neue Middleware direkt danach): Liegt ein Kontext in
   `Items`, Endpunkt-Metadaten lesen. GET auf Nicht-Verwaltungsweg → weiter.
   Sonst: `KeinKiZugriff` → 403 `ki_kein_zugriff`; keine Einstufung → 403
   `ki_nicht_eingestuft`; Stufe nicht freigegeben → 403 `ki_stufe_fehlt`
   („Dafür fehlt die Freigabe für Stufe …"). `KiSicherungVorher` → Sicherung
   anlegen, schlägt sie fehl → 503, nichts ausführen. Danach weiter.
   Kein Endpunkt (404) → weiter, die App antwortet 404.
3. **Danach:** jede schreibende Anfrage über einen Schlüssel ins Prüfprotokoll
   (`SystemAuditRepository`, Source `ki-zugriff`, Summary
   „über KI-Assistent ‚<Name>': <METHOD> <Pfad> → <Status>"), `ZuletztGenutztAm` setzen.

Fehlversuche: je IP im Speicher; ab 10 Fehlversuchen in 10 Minuten wird diese IP
15 Minuten lang sofort abgewiesen (429 `ki_zu_viele_versuche`), ohne zu prüfen.

Die Schlüsselverwaltung (`/api/settings/ki-zugriff…`) liegt unter
`/api/settings` (Verwaltungsweg) **und** trägt `[KeinKiZugriff]` — ein Schlüssel
mit Verwaltung darf Einstellungen ändern, aber nie Schlüssel.

## Speicher

Tabelle `ForkKiSchluessel` (eigenes Schema wie `SteuerungRepository`):
`Id INTEGER PK, Name TEXT, Praefix TEXT (die ersten 8 Zeichen nach gok_), Hash TEXT
(SHA-256 hex des ganzen Klartexts), Stufen INTEGER, ErstelltAmUtc TEXT,
ZuletztGenutztAmUtc TEXT NULL, GesperrtAmUtc TEXT NULL`.
SHA-256 genügt: der Schlüssel hat 256 Bit Zufall, ein langsamer Hash schützt nur
schwache Passwörter. Vergleich mit `CryptographicOperations.FixedTimeEquals`.
Gelöschte Schlüssel verschwinden ganz; gesperrte bleiben sichtbar.

Einstellungen in `AppSettings` (Schlüssel `ki-zugriff.aktiv`,
`ki-zugriff.rueckfrage-ab-stufe`, `ki-zugriff.max-dosis-ml`,
`ki-zugriff.max-schaltbefehle-je-stunde`). Vorbelegung: aus, Rückfrage ab
GrowPlanen, 10 ml, 20 Befehle/h.

## Schnittstellen (Verträge: `Api/Contracts/KiZugriffContracts.cs`)

| Weg | Wer | Was |
|---|---|---|
| `GET /api/settings/ki-zugriff` | nur Oberfläche | `KiZugriffSeiteDto` |
| `PUT /api/settings/ki-zugriff` | nur Oberfläche | `KiZugriffSpeichernRequest` → `KiZugriffSeiteDto` |
| `POST /api/settings/ki-zugriff/schluessel` | nur Oberfläche | `KiSchluesselRequest` → 201 `KiSchluesselAngelegtDto` |
| `PUT /api/settings/ki-zugriff/schluessel/{id}` | nur Oberfläche | `KiSchluesselRequest` → `KiSchluesselDto` |
| `POST /api/settings/ki-zugriff/schluessel/{id}/sperren` | nur Oberfläche | → `KiSchluesselDto` |
| `DELETE /api/settings/ki-zugriff/schluessel/{id}` | nur Oberfläche | 204 |
| `GET /api/ki-zugriff/ich` | nur mit Schlüssel | `KiZugriffIchDto` (ohne Schlüssel: 401) |

Stufen gehen als Namen über die Leitung: `"Dokumentieren"`, `"GrowPlanen"`,
`"GeraeteSchalten"`, `"Verwaltung"`. Unbekannter Name → 400 mit Feldfehler.

## Höchstwerte

Nur für Anfragen über einen Schlüssel, zusätzlich zu den Grenzen der Geräte:
- Dosierbefehl (`POST /api/dosing/pumps/{id}/dose`): `Ml > MaxDosisMlJeBefehl` → 422
  `ki_hoechstwert`, nichts läuft; das Abweisen wird wie ein `DosingGuard`-Nein protokolliert.
- Schalt- und Dosierbefehle (alles mit `KiStufe.GeraeteSchalten`) zählen über alle
  Schlüssel in einem gleitenden Stundenfenster; darüber → 429 `ki_hoechstwert`.

## Oberfläche

Einstellungen → neuer Abschnitt **„Zugriff für KI-Assistenten"**: Hauptschalter,
Rückfrage-Regel (Auswahl: nie / ab Stufe …), Höchstwerte, Liste der Schlüssel
(Name, Präfix, Stufen, zuletzt genutzt, Sperren, Löschen), „Neuer Schlüssel"
(Name, Stufen-Häkchen; GeraeteSchalten und Verwaltung nur nach Bestätigung eines
Warnhinweises). Nach dem Anlegen wird der Klartext **einmal** gezeigt, mit
Kopieren-Knopf und dem Satz, dass er nicht wieder angezeigt wird.

## Für den Assistenten

`GET /api/ki-zugriff/ich` zuerst: welche Stufen frei sind, ab wann er nachfragen
soll, welche Höchstwerte gelten. Die Rückfrage-Regel ist eine Bitte an den
Assistenten — durchsetzen kann der Fork nur die Stufen.

## Home Assistant über den Fork

Fork AI (A-003 Etappe B, 03.10.2026). Ein Assistent bedient mit **einem**
Connector (Grow MCP Fork AI) den Fork und Home Assistant — ohne eigenen HA-MCP.
Der Weg führt durch den Fork, damit dieselben Stufen, Höchstwerte und dasselbe
Prüfprotokoll gelten. Controller: `Api/Controllers/KiHomeAssistantApiController.cs`,
Verträge: `Api/Contracts/KiHomeAssistantContracts.cs`, HA-Zugang:
`HomeAssistantService` (dieselbe Verbindung wie überall).

**Jeder Weg verlangt einen Schlüssel**, auch die lesenden. Sonst könnte jedes
Nachbar-Add-on über den lesenden Weg alle Zustände von Home Assistant abgreifen —
mit dem Token des Forks. Ohne Schlüssel: 401 `ki_schluessel_fehlt` (aus der
Oberfläche wie aus dem Add-on-Netz). Ein POST aus dem Add-on-Netz ohne Schlüssel
endet wie überall schon vor dem Routing mit 403 `admin_access_required`.

| Weg | Antwort | Stufe |
|---|---|---|
| `GET /api/ki-ha/bereiche` | `[{ id, name }]` | jeder gültige Schlüssel |
| `GET /api/ki-ha/zustaende?bereich=&domain=&suche=&anzahl=` | `[{ entityId, name, zustand, einheit, bereich, geaendertAmUtc }]` | jeder gültige Schlüssel |
| `GET /api/ki-ha/verlauf?entityId=&stunden=` | `{ entityId, punkte: [{ zeitUtc, zustand }] }` | jeder gültige Schlüssel |
| `POST /api/ki-ha/dienst` | Body `{ domain, dienst, entityId?, daten? }` → `{ erfolg, meldung }` | Geräte schalten, je Domain mehr (unten) |

- **Zustände:** alle Filter wahlweise. `bereich` = Kennung oder Name des Bereichs,
  `suche` durchsucht Entity-ID und Namen ohne Rücksicht auf Groß-/Kleinschreibung.
  `anzahl` Vorgabe 100, erlaubt 1–500 (sonst 400). Sortiert nach Entity-ID.
- **Bereiche** holt der Fork über `POST /api/template` (`areas()`, `area_name()`,
  `area_entities()` — Letzteres nimmt die Entitäten der Geräte im Bereich mit). Die
  REST-Schnittstelle kennt keine Bereiche, und die Vorlage geht über dieselbe
  Verbindung wie alles andere.
- **Verlauf** über `GET /api/history/period` mit `minimal_response` und
  `no_attributes`. `stunden` 1–168, Vorgabe 24 (sonst 400).
- **Fehler:** 503 `ha_nicht_eingerichtet` (keine Verbindung eingerichtet), 502
  `ha_nicht_erreichbar` (Home Assistant antwortet nicht).
- **Dienst:** `domain` und `dienst` nur aus Kleinbuchstaben, Ziffern, Unterstrich
  (sonst 400 — die Namen landen im Pfad `api/services/{domain}/{dienst}`).
  `entityId` ist genau **eine** Entität, und ihr Präfix muss die Domain sein
  (`light.turn_on` mit `switch.x` → 400). Ziele in `daten` (`entity_id`,
  `device_id`, `area_id`, `floor_id`, `label_id`, `target`) → 400: das Ziel geht nur
  über `entityId`, sonst liefe die Präfix-Prüfung ins Leere. Antwortet Home
  Assistant, ist die Antwort 200; `erfolg` sagt, ob es angenommen hat (bei
  ausbleibender Antwort `false` mit dem Hinweis, den Zustand nachzusehen).

### Einstufung je Domain

Die Aktion trägt `[KiStufe(KiStufe.GeraeteSchalten)]`: die Sperre prüft das vor
dem Controller **und zählt jeden Aufruf ins Stundenfenster der Schaltbefehle**
(429 `ki_hoechstwert`). Auch ein Aufruf, den der Controller danach abweist,
zählt — die Sperre zählt, bevor die Domain bekannt ist. Darauf legt der Controller
je Domain eine Tabelle (`Infrastructure/KiZugriff/KiHaEinstufung.cs`, jede Domain
genau einmal, mit Grund):

- **Verwaltung zusätzlich** (sonst 403 `ki_stufe_fehlt`, Meldung wie in der Sperre):
  `automation`, `script`, `scene`, `input_boolean`, `input_number`,
  `input_select`, `input_text`, `input_datetime`, `input_button`, `timer`,
  `counter`, `schedule` — sie ändern die Logik von Home Assistant, nicht ein Gerät.
- **Nie** (403 `ki_kein_zugriff`, auch mit allen Stufen):

| Domain / Dienst | Grund |
|---|---|
| `homeassistant` | Neustart, Stopp, Konfiguration neu laden, generisches Schalten jeder Domain — umginge die Tabelle |
| `hassio` | Supervisor: Add-ons, Host, Neustart, Sicherungen |
| `backup` | Sicherungen anlegen/zurückspielen — auch in Grow OS nie über einen Schlüssel |
| `recorder` | Löscht oder sperrt die Geschichte — nicht zurückzudrehen |
| `system_log` | Leert/schreibt das Systemprotokoll, die Spur des Geschehenen |
| `logger` | Ändert, was protokolliert wird |
| `shell_command` | Befehle auf dem Host |
| `python_script`, `pyscript` | Beliebiger Code in Home Assistant |
| `rest_command` | Anfragen an beliebige Adressen |
| `notify` | Nachrichten an Menschen gehen von Grow OS aus, nicht vom Assistenten |
| `persistent_notification` | Meldungen in HA — dem Betreiber nichts unterschieben |
| `tts` | Sprachausgabe an Lautsprecher im Haus |
| `conversation` | Der Sprachassistent führt beliebige Absichten aus — umginge die Tabelle |
| `lock` | Haus- und Zelttüren öffnen ist kein Grow-Betrieb |
| `alarm_control_panel` | Alarmanlage scharf/unscharf |
| `update` | Installiert Firmware und Updates (über den Auftrag hinaus ergänzt) |
| `mqtt` | `mqtt.publish` erreicht jedes Gerät — umginge die Tabelle (ergänzt) |
| `downloader` | Lädt Dateien auf den Host (ergänzt) |
| jeder Dienst `reload` | Konfiguration neu laden wirft Zustände weg und schaltet kaputte Konfiguration scharf |

- **Alles andere** (light, switch, fan, climate, humidifier, cover, number, select,
  button, valve, water_heater, vacuum, media_player …) genügt mit Geräte schalten.

Gehalten von `GrowDiary.Web.Tests/KiZugriff/KiHaSchnittstelleTests.cs` (an der
echten App mit nachgestelltem Home Assistant) und den Ausnahmen in
`JedeRouteHatEinenAufruferTests` (gerufen vom Grow MCP Fork AI, nicht aus der
Oberfläche).
