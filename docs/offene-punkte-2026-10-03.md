# Offene Punkte — Stand 03.10.2026 (nach 2.0.0-forkai.160)

Gesammelt aus der Durchsicht vom 01.–03.10.2026 (`docs/pruefung-2026-10-01.md`),
den Berichten der Agenten und der Prüfer-Durchgänge. Installiert in der Anlage:
**Grow OS Fork AI 2.0.0-forkai.160**, **Grow MCP Fork AI 0.1.9-forkai.1**.
Arbeitsbranch: `claude/fork-ai-code-review-s94cvv`.

Jeder Punkt: was, wo, warum offen. Reihenfolge innerhalb eines Abschnitts =
Vorschlag für die Dringlichkeit.

---

## A. Für den Nutzer in der Anlage (kein Code)

1. **Verlaufs-Kachel anlegen.** Die eigene Anordnung der Live-Seite (Zelt 1,
   Bereiche „Klima" und „Hydroponik · Nährlösung") hat keine Diagramm-Kachel —
   das neue Verlaufsdiagramm ist dort erst nach „Anpassen → + Kachel → Verlauf →
   Fertig" zu sehen.
2. **Grow MCP Fork AI einrichten.** Einstellungen → Apps → Grow MCP Fork AI →
   „Web-UI öffnen"; steht dort der Befehl `claude mcp add … grow-os-fork-ai …:5080/mcp`,
   ihn auf dem Rechner mit Claude Code ausführen. Steht dort „Grow OS ist von hier
   aus nicht erreichbar", ist die Suche nach dem Fork (`GrowOsLocator`) in der
   echten Anlage nicht belegt — bisher nur per Test. Die Seite antwortet nur dem
   Ingress (403 für alles andere, gewollt), deshalb konnte Claude das nicht selbst sehen.
3. **Port 5080 nicht im Router freigeben.** Der MCP-Schlüssel geht über HTTP;
   für unterwegs VPN (Tailscale/WireGuard).
4. **Noch nicht in der Anlage getestet:** Lüfterstufe auf „Zelt (AC-Test)"
   umstellen (kein rotes 502), A/B-Dosierung mit echten Pumpen, Fingergesten des
   Verlaufsdiagramms am Telefon (nur per Touch-Emulation geprüft, Safari/iOS gar
   nicht).
5. **Plan prüfen:** Anzucht 2 Wochen (ab 12.06.), Vegi 9 Wochen (26.06.–23.08.) —
   stimmt das mit dem echten Grow überein? Sonst ist „14 Tage schon in der Phase"
   am Grow falsch eingetragen.

## B. Fehler und Lücken im Code

> **Stand 03.10.2026 nachmittags (Branch `offene-punkte-b`):**
> - **B1, B2 erledigt.** Neu `Services/Zahlenlesen.cs` (`Maschine` = `maschinenZahl`,
>   `Getippt` = `zahlOderNull`); alle 22 eigenen Fassungen im Backend laufen darüber,
>   NaN/Unendlich gelten nicht mehr als Zahl. Fälle beider Seiten in einer Tabelle
>   (`GrowDiary.React/src/zahlen-leseregeln.json`), Zählung gegen neue eigene Fassungen.
>   Verhaltensänderung: „7,0" aus HA gilt jetzt als 7 (vorher 70).
> - **B3 erledigt, ohne Code.** In der Anlage geprüft (104 automatische Messungen
>   seit 11.08.): keine 0, kein verzerrter Mittelwert. Die eine Auffällige (id 19,
>   20.08., pH 5,28) hat normale EC und Wassertemperatur — eine echte Messung.
> - **B4 bewusst nicht umgesetzt** (Entscheidung 03.10.2026). Ohne Gegensonde gibt es
>   keinen Widerspruch, der eine echte Messung ausschliesst — jede Regel wäre eine
>   Faustregel. Die Anlage hat beide Sonden. Wieder aufnehmen mit A-001 („Fork für
>   andere Benutzer").
> - **B5 erledigt.** Erst rechnen (aus den Rohwerten ohne Nullbild), dann löschen und
>   schreiben in einer Transaktion (`SensorReadingRepository.Bereinigen`). Scheitert
>   die Rechnung, bleiben die Nullbilder des Zelt-Tags für den nächsten Start stehen.
> - **B6 erledigt.** Test mit Nullbild um 01:00 Ortszeit; er verlangt eine Zeitzone
>   mit Versatz und sagt es laut. Jeder Lauf der Backend-Tests (Tor, Hook, lokal)
>   bekommt `TZ=Europe/Berlin` über `GrowDiary.Web.Tests/zeitzone.runsettings`.
>   (Nicht in `ci.yml`: das Token der Claude-Sitzungen darf keine Workflows ändern.)
> - **B7 erledigt.** Ein Sensorwert außerhalb von `PhysikalischeGrenzen` zählt für die
>   Dosierung wie keiner (dann Handwert oder gar nichts).
> - **B8 erledigt: Neustart.** Bestandsaufnahme: außer `GrowPlanRegister` halten
>   auch die Wissensbasis (wird zurückgespielt, aber nicht neu geladen), die
>   Wochenwert-Überlagerung, drei `SchemaSteht`-Merker (`GrowPlanRepository`,
>   `WochenwertRepository`, `SteuerungRepository`) und mehrere Start-Übernahmen den
>   alten Stand. Statt sie einzeln nachzuladen, startet Grow OS nach erfolgreichem
>   Zurückspielen 3 s später neu (`SupervisorNeustart`, `POST
>   http://supervisor/addons/self/restart` — ohne `hassio_api` erlaubt). Ausserhalb
>   des Add-ons sagt das Ergebnis, dass ein Neustart nötig ist. **In der Anlage noch
>   nicht ausgelöst** — Zurückspielen überschreibt Daten.
> - **B9 erledigt (Kalibrierung).** Unlesbare Kalibrierwerte werden genannt und nicht
>   eingetragen; Meldungen des Pflege-Formulars stehen jetzt IM Formular (oben auf
>   der Seite lagen sie ~400 px außerhalb des Bildes). `SetpointProfilesPage` ist
>   **toter Code** — keine Route, kein Import; Löschkandidat wie D5.
> - **B10 erledigt.** pH-Kalibrierpunkte gegen 0–14 (vorhandene Tabelle), EC gegen
>   0–120 mS/cm (`Kalibrierpunkte.EcKalibrierObergrenzeMsCm`; stärkste übliche Lösung
>   1 mol/l KCl, 110–112 mS/cm, Merck Certipur 101255). Die Meldung nennt bei „1413"
>   den gemeinten Wert in mS/cm. ORP und Sauerstoff bleiben offen (Sauerstoff oft in %).
>   Nicht gefangen: eine µS-Eingabe unter 120 („84" für 84 µS/cm gilt als 84 mS/cm —
>   die Zahl ist in beiden Einheiten eine mögliche Lösung).
> - **B11 erledigt.** Die Karte markiert nach Programm-ID; der Feldfehler bei leerem
>   Programm nennt die Programmkarten statt der API-Route.

1. **Zahlen aus Home Assistant mit `NumberStyles.Any` + InvariantCulture.**
   `HomeAssistantService.cs` (Zustand lesen, um Zeile 203), `AcSchreiber.cs:291`,
   `Demoschaltbrett.cs:58`, `PhenoRepository`: erlaubt Tausender-Kommas — ein
   Zustand „5,8" würde zu 58. Auf `NumberStyles.Float` umstellen, mit Test.
2. **Freitext `reservoirSize` im Backend:** `GrowWorkflowApiController.cs:739–746`
   liest per Regex mit `Replace(',', '.')` — „1.200 L" wird 1,2. An die deutsche
   Leseregel des Frontends (`zahlenfeld.ts`) angleichen.
3. **Automatische Messungen mit Nullbild.** Messungen der Quelle Home Assistant
   (`AutoMeasurementExecutionService.cs:218`), die vor forkai.160 aus Rohwerten
   mit pH 0 / EC 0 gebildet wurden, enthalten die 0 oder einen verzerrten
   Mittelwert; die Startbereinigung fasst nur Rohwerte und Tageswerte an. In der
   Anlage prüfen, ob es solche Messungen gibt; ggf. Bereinigung nach derselben
   Regel (`WassersondenNullbild`).
4. **Nullbild-Regel greift nur bei pH- UND EC-Sonde.** Ein Zelt mit nur einer
   der beiden Sonden ist nicht abgedeckt (steht im Code).
5. **Neuberechnung der Tageswerte nicht transaktional.** `WassersondenNullbild.GespeicherteEntfernen`:
   Rohwerte werden gelöscht, danach wird gerechnet. Scheitert die Rechnung, ist
   die Grundlage weg und das Minimum 0 bleibt. Selten, aber endgültig.
6. **Testlücke Zeitzone:** Kein Test mit einem Nullbild zwischen 00:00 und 02:00
   Ortszeit (lokaler vs. UTC-Kalendertag). Der Code ist richtig, die Prüfung
   beißt dort nicht (Prüfer, Commit fe8748e).
7. **Dosierung ohne Plausibilitätsprüfung:** `DosingContextBuilder.ReadingFor` →
   `GetNewestReading` nimmt den jüngsten Rohwert ungeprüft. Das Nullbild ist an
   der Quelle abgefangen, andere Ausreißer nicht. Gegen
   `MeasurementSanityService.PhysikalischeGrenzen` halten.
8. **Speicherzustand nach Restore.** Nach dem Zurückspielen einer Sicherung laufen
   die Schema-Ergänzungen jetzt sofort, aber `GrowPlanRegister` und andere Daten im
   Speicher halten den Stand der vorigen Datenbank bis zum Neustart.
9. **Unlesbare Eingaben verschwinden noch still** an zwei Stellen:
   `SetpointProfilesPage.tsx:180-181` (Feld wird übersprungen) und
   `features/hardware/kalibrierpunkte.ts:59-61` (wird `null`, keine Meldung in
   `HardwarePage`).
10. **Kalibrier-Sollwerte ohne Obergrenze** — bewusst (12,88 mS/cm ist eine
    übliche Lösung, über der EC-Grenze 10). Eigene Grenze für Kalibrierlösungen
    wäre sauberer als gar keine.
11. **Grow-Formular:** Die Programmkarte SKX ist beim Bearbeiten nicht als gewählt
    markiert, wenn `nutrients` leer ist (die Markierung vergleicht den Namen, nicht
    die Programm-Id). Der Feldfehler bei leerem Programm nennt die API-Route.

## C. Verlaufsdiagramm — Ausbaustufen

1. **Monat / ganzer Grow** aus den dauerhaften Tageswerten (Min/Median/Max,
   `resolution=daily`) — heute endet es bei 7 Tagen (so lange hält Grow OS die
   Rohwerte).
2. **Ein/Aus-Spuren weiterer Geräte** (Kühler, Entfeuchter, Lüfter, Pumpen) über
   den Verlauf von Home Assistant, wie in der AC-Infinity-App. Heute nur Licht
   nach Lichtplan.
3. **Lichtplanwechsel innerhalb der 7 Tage** werden nicht gezeigt — Dunkelphasen
   nutzen den heute gelernten Zyklus für alle Tage, obwohl die Flanken gespeichert
   sind (Kommentar an `lichtPhasen` in `verlauf-modell.ts`).
4. **Zielband auch in „Zusammen"** als Schalter (heute nur in „Einzeln"/Fokus).
5. Das Zeiger-Schild kann Punkte ganz am oberen Rand verdecken.
6. Auf echten Geräten nachmessen (Linux-Schriften im Tor sind geprüft, echte
   Telefone nicht).

## D. Demobestand und Prüfungen

> **Stand 03.10.2026 abends (Branch `demobestand-d`):**
> - **D1 erledigt — und größer als gedacht.** Nicht nur die Luftfeuchte: in der
>   echten Demo-App (mit Plänen) lagen vier Bereichsziele daneben — Zelt 1
>   Luftfeuchte 53 % (≤ 50) und EC 1,03 (1,5–1,7), Blütezelt 2 Luftfeuchte 53 %
>   (≤ 40). Ursache: beide Zelte lasen dieselbe Kurve aus einem älteren Profil,
>   ihre Grows laufen aber nach dem SKX-Plan in verschiedenen Wochen. Jetzt hat
>   der Demo-Verlauf eine Lage je Zelt (`Demoverlauf.Lage`: Blüte / Spätblüte,
>   `DemoData.LageFuer`). Gehalten von `e2e/demobestand-im-ziel.spec.ts` (alter
>   Stand rot mit genau den drei Fällen). Einzelwert-Ziele („23 °C") zählen
>   bewusst nicht — die Kachel zeigt dort die Abweichung als Zahl.
>   Nebenbefund: Backend-Tests sehen den Bestand OHNE Pläne (Program.cs legt
>   sie erst danach an) und damit Profil-Ziele — zwei Fassungen desselben Bestands.
> - **D5 teilweise anders als notiert:** `summariseYield` wird von der Ernte-Seite
>   benutzt — schrieb aber „Trockenausbeute 22.0 %"; jetzt deutsch. `toNullableInt`
>   ist in Ordnung (alle Aufrufer `type="number"`, ganze Zahlen).
>   `handleMeasurementSubmit` samt ungenutztem Messformular im Hook entfernt.
>   `SetpointProfilesPage` (ohne Route) — Löschen mit dem Nutzer klären.
> - **D2–D4 offen.**
> - Lokal fällt `verlaufsdiagramm.spec.ts` „in anderer Zeitzone" durch: der Fall
>   stellt den Browser auf Europe/Berlin — in der VM läuft die App in derselben
>   Zone, der Mengenwächter meldet das richtig. Im Tor (UTC) grün.

1. **Demobestand widerspricht sich:** Luftfeuchte-Ziel ≤ 50 %, die Werte liegen
   bei 53–55 % — die Kachel zeigt „daneben". Nach der Regel in CLAUDE.md ist der
   Bestand falsch (`DemobestandStimmigTests`).
2. Der Demobestand hat **keinen Steckling** mit angehängter Anzucht-Woche (nur
   Samen-Grows) — der „Bewurzelung N"-Fall ist nur per Unit-Test belegt.
3. Im Demobestand sind die **Licht-Rollen nicht zugeordnet** („Geräte 0/6") — die
   Steuerungs-Übersicht zeigt dort „Modus – · Stufe –"; der Weg mit echtem
   Lichtmodus ist am Bestand nie zu sehen.
4. Kein Steuerungsweg setzt im Demobestand einen seltenen Lichtmodus (Auto, Cycle …)
   — „Modus automatisch" nur per Unit-Test belegt.
5. Ungenutzter Code: `summariseYield` in `harvest-yield.ts` (nur von seinem Test
   benutzt, schreibt „22.0 %"), `handleMeasurementSubmit` in
   `useGrowDetailMutations` (kein Aufrufer), `toNullableInt` (`v1-utils`) mit
   `parseInt` (alle Aufrufer `type="number"`).

## E. Fachliche Entscheidungen, die noch beim Nutzer liegen

1. **Zwei Grows im selben Zelt:** zurückgestellt (Entscheidung 02.10.2026: ein
   Grow je Zelt). Offen bleibt, welcher Grow dann Sollwerte an Home Assistant gibt.
2. **Original-Grow-OS (beta.65)** läuft parallel und steht in der Seitenleiste;
   bleibt zum Vergleichen (Entscheidung 03.10.2026). Es schaltet und meldet laut
   Log nichts. Der Fork steht nicht in der Seitenleiste — „In der Seitenleiste
   anzeigen" auf der Info-Seite der App kann nur der Nutzer setzen.

## F. Arbeitsweise und Werkzeuge

1. **Commit-Hook:** In der Cloud-Sitzung waren Commits nach Sekunden durch — ob
   `.claude/hooks/vor-commit.sh` (volles Tor) dort überhaupt läuft, ist nicht
   belegt. Einmal mit eingebautem Fehler prüfen.
   **Befund 03.10.2026 (VM `ClaudeCode`):** er läuft dort NICHT — `vor-commit.sh` ruft
   `python` und `dotnet`, im PATH stehen nur `python3` und `~/.dotnet/dotnet`. Ohne
   `python` bleibt der Befehl leer, und der Hook endet still mit 0. Außerdem kennt
   das .NET-8-SDK dort `GrowDiary.slnx` nicht (MSB4068); die Testprojekte laufen nur
   einzeln. Das Tor wurde in dieser Sitzung von Hand gefahren.
   **Erledigt 03.10.2026 (Branch `werkzeug-hooks`).** Drei Ursachen:
   (a) Die Sitzungen starten in `/home/claude/projekte`; Claude Code liest Hooks nur
   aus `.claude/settings.json` des Startordners — die des Repos wurden nie geladen.
   Jetzt bindet `/home/claude/projekte/.claude/settings.json` (nicht im Git) die
   Skripte des Repos ein. (b) Die Skripte suchen `python3`/`python` und `dotnet`
   (auch `~/.dotnet`) selbst (`.claude/hooks/werkzeuge.sh`); fehlt eins, melden sie
   das laut (Exit 2) statt still durchzuwinken. Sie greifen nur bei Dateien und
   Commits dieses Repos. (c) In der VM liegt neben SDK 8 jetzt SDK 10 — damit lädt
   `GrowDiary.slnx`. Belegt: Compilerfehler per Edit → Hook rot; kaputter Code im
   Commit → abgelehnt; ohne Python/dotnet → laut abgelehnt; die alte Fassung meldete
   beim selben Compilerfehler Exit 0.
2. **Hooks und Worktrees:** Die Hooks arbeiten auf dem Haupt-Repo
   (`WURZEL` aus dem Skriptpfad); Agenten in Worktrees müssen das Tor selbst
   laufen lassen.
3. **Lokale Apps parallel:** Jeder Agent braucht einen eigenen Port; Prozesse
   nur per PID und Port beenden. Ein Beenden über alle `GrowDiary.Web.dll`-Prozesse
   hat am 02.10. die Apps der Agenten mitgerissen.
4. `docs/pruefung-2026-10-01.md` auf den Stand von forkai.160 bringen
   (Nullbild, Verlaufsdiagramm, EC-Grenze).
