# Grow OS Dokumentation

Zwei Hälften: **was die App kann** (die Referenz-Mappe) und **wie man daran
arbeitet** (bauen, ausliefern, Entscheidungen).

## Was die App kann

→ **[referenz/](referenz/README.md)** — zehn Nachschlagseiten, eine je Bereich.
Jede sagt: wo es in der App steht, was es tut, **jede Zahl mit Quelle**, was
bewusst *nicht* passiert, welche Dateien man anfassen würde, und was hier schon
einmal schiefgegangen ist. Die Einstiegsseite dort beantwortet „wo schlage ich
WAS nach" über eine Liste echter Fragen.

Live & Messen · Dosierung & Addback · Crop Steering · Sollwerte & Wissen ·
Diagnose & Risiken · Grows, Sorten, Pflanzen · Ernte, Trocknen, Aushärten ·
Zelte, Hydro, Wasser · Home Assistant & Automatik · Aufgaben, Journal, MCP

Chronologisch statt nach Bereich: `grow-os/CHANGELOG.md` und
`grow-mcp/CHANGELOG.md` — jede Änderung mit ihrem Grund, auf Englisch.

## Projektüberblick

Grow OS ist ein **Home-Assistant-Add-on** für RDWC/DWC-Anbau. Es kombiniert:

- Sensor- und Statusdaten aus Home Assistant (Verbindung automatisch über den Supervisor)
- Grows, Messungen, Journal, Fotos und Aufgaben
- Zelte, Hydro-Systeme, Geräte und Wartung
- Dosierpumpen, Addback- und Wasserwechsel-Protokolle
- Crop Steering über die Wassertemperatur (Nachtabsenkung, Kühler-Regler)
- Ernte, Trocknen, Aushärten und Archiv mit Kostenrechnung
- SOPs, Diagnose und Risiko-Ereignisse aus einer mitgelieferten Wissensbasis
- Bedienung am Telefon direkt in der Home-Assistant-App

**Keine KI in der App.** Wer eine eigene benutzen will, verbindet sie unter
**Einrichtung → KI-Assistent**: über das eigene Konto bei Claude oder ChatGPT,
mit einem Schlüssel aus Grow OS und nur so weit, wie er freigegeben ist — oder
ohne Verbindung als ZIP („Mappe"). Grow OS ruft selbst keine KI auf.

## Wie man daran arbeitet

| Dokument | Wofür | Stand |
|---|---|---|
| [install.md](install.md) | Add-on in Home Assistant installieren — für Nutzer | 2026-07-20 |
| [pi-setup.md](pi-setup.md) | von der leeren SD-Karte bis zur laufenden App | 2026-07-21 |
| [setup.md](setup.md) | Quellcode bauen und lokal starten | 2026-06-14 |
| [development.md](development.md) | Branch-Regeln, Build/Test-Pflicht, Review-Checkliste | 2026-07-21 |
| [release.md](release.md) | die Reihenfolge beim Ausliefern (CI → Image → GHCR → Version) | 2026-07-26 |
| [architecture.md](architecture.md) | Backend, Frontend, SQLite, Repository-Struktur | 2026-05-18 · **teilweise veraltet** |
| [grow-domain-notes.md](grow-domain-notes.md) | fachliche Notizen zu Zelten, Setups, Messungen, HA | 2026-07-21 |
| [decisions/adr-0001-local-first-pwa.md](decisions/adr-0001-local-first-pwa.md) | lokale Web-App statt nativer App — Status im Dokument: „teilweise abgelöst" | 2026-07-20 |
| [decisions/adr-0002-repository-refactor.md](decisions/adr-0002-repository-refactor.md) | `GrowRepository` als Facade | 2026-05-18 |

**`release.md` gilt weiter** und deckt sich mit `CLAUDE.md`; ausführbar liegt
derselbe Ablauf als `/release` in `.claude/commands/release.md`.

**`setup.md` und `development.md` kennen nur `npm run build`.** Das Tor von
heute steht in `CLAUDE.md`: `tsc -b` bzw. `npm run typecheck` (`tsc --noEmit`
prüft in diesem Repository **null** Dateien), Vitest, ESLint, und Playwright
gegen die *laufende* App mit `GROW_OS_DEMO=1`, `GROW_OS_URL` und `E2E_STRENG=1`.

### Was an `architecture.md` nicht mehr stimmt

- Es nennt `/live`, `/action` und `/analyse` als Oberflächen. Das sind heute nur
  noch Weiterleitungen auf `/`, `/aufgaben` und `/archiv` — `/action` und
  `/analyse` über `legacyRedirects` in `GrowDiary.React/src/navigation.ts`,
  `/live` als eigene `Navigate`-Route in `App.tsx`.
- Die Oberflächen-Liste endet bei Settings — Dosierung, Crop Steering, Sollwert-
  Profile, Diagnose, Sorten, Aushärten, Wasser und Einkaufsliste fehlen ganz.
- Abschnitt „Knowledge Base": das Wissen werde „beim ersten Start" nach
  `App_Data/knowledge/` kopiert. Genau das war der Fehler bis 1.6.1; seither
  gleicht `KnowledgeBaseLoader.EnsureKnowledgeDirectory` bei **jedem** Start
  gegen das Manifest `.shipped-defaults.json` ab.

Repository-Struktur, Datenbank-Abschnitt und die Aussage „kein ORM, kein
Migrations-Framework" stimmen weiter.

## Momentaufnahmen — nicht als Stand lesen

Vier Befundlisten von einem Stichtag. Sie erklären gut, *warum* etwas so gebaut
wurde; was heute offen ist, steht nicht darin.

| Dokument | Was | Datum |
|---|---|---|
| [code-analyse.md](code-analyse.md) | Tiefenanalyse über 673 Dateien, Commit `6b4b34b` | 2026-07-25 |
| [code-analyse-2.0.md](code-analyse-2.0.md) | neun Befunde über die 21 Commits des UI-Umbaus | 2026-07-26 |
| [sop-algorithmen.md](sop-algorithmen.md) | Abgleich SOP-Quelldokumente ↔ App; das Dokument nennt selbst „Stand: 2026-07-25" | 2026-07-26 |
| [designer-brief.md](designer-brief.md) | Rückfrage an den Designer nach einer fehlenden Entwurfsdatei; das Redesign ist seit 2.0.0-beta.1 ausgeliefert | 2026-07-26 |
