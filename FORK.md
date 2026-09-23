# Grow OS Fork AI — Unterschiede zum Original

Dieses Repository ist ein Ableger von [Grow OS](https://github.com/Nerdstreak/Grow-Operation-System)
(Nerdstreak, MIT-Lizenz). Ziel: Weiterentwicklungen ausprobieren, die dem
Original-Projekt später als Pull Request angeboten werden können.

**Basis:** Grow OS 2.0.0-beta.65 · **Add-on:** „Grow OS Fork AI" (`grow_os_fork_ai`),
läuft parallel zum Original, eigene Datenbank, eigenes Image.

Diese Seite ist die Kurzfassung. Wer es Version für Version braucht:
[docs/fork-historie.md](docs/fork-historie.md).

## Neue Funktionen

**Zielwerte an einer Stelle.** Eine Seite zeigt je Messgröße das gerade geltende
Band und woher es kommt — Sollwertprofil, Feed-Chart-Woche oder feste Regel —
samt dem Ort, an dem man es ändert. Dahinter liegt eine gemeinsame Lesart, damit
Kachel, Score und Alarm dieselbe Zahl nennen.

**Jeder Grow hat seinen eigenen Plan.** Beim Anlegen wird das gewählte
Düngeprogramm als Kopie mit dem Grow gespeichert (Startstand unveränderlich,
Arbeitsstand bearbeitbar, Änderungsbuch). Alle Leser der Wochenziele nehmen diesen
Plan statt des Bibliotheksprogramms — ein geändertes Programm schreibt damit keine
laufenden oder abgeschlossenen Grows mehr um. Lücken eines Programms werden beim
Anlegen einmalig aus dem mitgelieferten Standard gefüllt; Programme ohne Wochen
bekommen ein Raster. Auf der Zielwerte-Seite öffnet jede Karte ein Blatt, in dem
Wochenziel, Alarmgrenze, Meldepause und Scharf-Schalter zusammen bearbeitet werden
Im Reiter „Plan“ lassen sich alle Wochen samt Dosierung bearbeiten; beim Speichern
wählt man „nur dieser Grow“ oder „auch ins Programm“ — mitgelieferte Programme
bleiben unverändert, es entsteht ein eigenes Programm als Datei im Wissensordner.
Jede Änderung steht im Änderungsbuch. Der Reiter „Meldungen“ zeigt alle Absender an einer Stelle — auch die
Wächter, die Home Assistant selbst schickt. Das Programm eines laufenden Grows lässt sich wechseln; eigene Änderungen
werden auf Wunsch übernommen. Sollwertprofile tauchen in der Oberfläche nicht
mehr auf. Beim Abschluss wird der Plan mit dem Grow eingefroren; die Grow-Seite wertet je
Woche Plan und Messung aus, und der Endstand lässt sich als Programm für den
nächsten Grow speichern. Die laufende Woche steht als Zeile über den Reitern; ein Tipp darauf öffnet
alle Wochen. (Umbau „Ziele & Meldungen“, forkai.116–125, abgeschlossen.) Seit forkai.133 sind es drei
Menüpunkte — **Plan** (Pflanzen), **Grenzwerte** (Betrieb) und **Handy** (Einrichtung, Push und App
einrichten) —, verbunden über die Kette „1 · Plan › 2 · Grenzwerte › 3 · Handy“; Zielwerte ändert man nur
noch im Plan.

**Live-Kacheln: Ziel und Grenze getrennt** (forkai.146). Jede Kachel nennt unter „ZIEL" den Planwert
(Luft/Luftfeuchte Tag und Nacht in einer Zeile); die Grenzwerte stehen als gelbe Striche auf dem Band.
Nennt der Plan nur einen Wert, zeigt die Kachel die Abweichung. Rot nur jenseits der Grenzwerte — wie die Meldung.

**Alarmgrenzen können dem Wochenplan folgen.** Je Regel umschaltbar zwischen
festen Zahlen (Vorgabe, unverändertes Verhalten) und dem Zielband der laufenden
Woche ± Toleranz. Damit wandern pH und EC mit der Blütewoche mit, statt ab Woche 4
dauerhaft zu melden.

**Klimawerte je Woche statt nur je Phase.** Die Feed-Chart-Spalte trägt zusätzlich
Wassertemperatur Tag/Nacht, VPD, CO₂, PPFD, RH-Obergrenze und Lufttemperatur. Eine
Zeile oben auf Plan und Grenzwerte zeigt die laufende Woche mit den
Ankern (Vegi-Start, Flip, Erntefenster) und dem Hinweis, wenn eine gestreckte
Phase die letzte Spalte hält; das Wochen-Blatt dahinter führt zu jeder Woche im
Plan. Bearbeitet wird im Plan des Grows — gespeichert wird nur die
Abweichung, die Programmdatei bleibt unberührt.

**Übergabe an Home Assistant.** Die Wochenwerte gehen beim Wochenwechsel und
täglich um 06:00 in HA-Helfer, nach denen dort die Automationen regeln. Von Hand
verstellte Helfer erkennt der Abgleich, überschreibt sie nicht und bietet sie zum
Freigeben an (im Reiter „Werte“). Jeder Helfer hat dabei genau eine schreibende
Stelle im Fork: was der Plan führt, lassen CO₂- und Kühler-Seite aus; das CO₂-Ziel schreibt
allein die CO₂-Steuerung als Staffel aus dem Wochenwert. Leitgedanke: Grow OS
plant, Home Assistant regelt.

**Tag/Nacht-Zielwerte.** Die Zelt-Grenzwerte tragen ein zweites Band für die
Dunkelphase, das überall gleich gilt; die Kachel zeigt beide nebeneinander, das
geltende hell. Ohne Nachtwerte verhält sich alles wie bisher.

**Steuerung.** Ein Leitstand mit Detailseiten für CO₂-Begasung, Licht, Zuluft,
Entfeuchter und Water Chiller: Sollwerte und Betriebsarten liegen im Fork, geregelt wird weiter in
Home Assistant, und die passenden Automationen liegen als Vorlagen bei. Nur die
Licht-Seite schreibt direkt am AC-Infinity-Port — nur Abweichendes, mit Abstand,
Prüfung und Wiederholung, weil die Cloud parallele Schreibvorgänge verwirft.
Der Water Chiller kennt seit forkai.135 zwei Ansteuerungen, die sich aus den
Rollen ergeben: Steckdose (HA schaltet mit einstellbarer Hysterese) oder ein
Kühler mit eigenem Thermostat (climate/number — HA schreibt nur das Tag- oder
Nachtziel ins Gerät); mit beiden ist die Steckdose Not-Aus. Berührt:
`Vorlagen/chiller/*`, `SteuerungAutomationService` (neu: `wennNicht`),
`SteuerungGeraeteService` (optionale Rolle bewusst leer), `HomeAssistantState`/
`HomeAssistantService` (Attribut `temperature`). Seit forkai.137 zeigt die
Chiller-Seite die Ansteuerung und blendet Nicht-Zutreffendes aus (Mockup-Variante B:
keine eigene Wahl, die Rollen entscheiden); optionale Rollen lassen sich im
Rollen-Reiter mit „keins" leeren (`RollenReiter.tsx`).

**Crop Steering stillgelegt (seit forkai.136).** Die Seite des Entwicklers
(Nachtabsenkung und Kühler-Regler im Add-on) ist im Fork abgeschaltet: Die
Wassertemperatur führt der Grow-Plan, geregelt wird unter Steuerung → Chiller —
sonst gäbe es zwei Regler für denselben Kühler und zwei Stellen für dasselbe Ziel.
Ein Schalter an einer Stelle (`Infrastructure/ForkAiSchalter.CropSteeringAktiv`);
die Original-Dateien bleiben unverändert liegen. Alte Adressen leiten zum Chiller,
die Grow-Karte „Nachtabsenkung", der Menü-/Sucheintrag und die Übersichtszeile
entfallen. Einmalig werden Einträge aus Crop Steering (Steckdose am Zelt,
climate-/number-Zielgerät) als Chiller-Rollen übernommen. Der Kühler-Wächter
wertet einen ausgeschalteten Kühler als Absicht, wenn die HA-Regelung keinen
Kühlbedarf meldet. Berührt (Original): `Program.cs`, `HomeAssistantSnapshotWorker`,
`TentsController`, `PumpWatchNotifier`, `App.tsx`, `navigation.ts`,
`GrowDetailPage.tsx`, E2E-Listen.

**Keine Werksvorgaben für Rollen (seit forkai.138).** Die Vorgaben der Rollen waren die
Entitäten einer einzelnen Anlage. `GeraeteRolle.Vorgabe` ist jetzt leer; die alten Werte stehen
in `BisherigeVorgabe` und werden einmalig von `RollenVorgabenUebernahme` als feste Zuordnung
gespeichert — nur, wo die Entität in HA existiert. Bis dahin gelten sie als Rückfall weiter.

**Grenzwerte → Bluelab-Gerät (seit forkai.141).** `BluelabGrenzenService` + Worker übertragen die
wirksamen Grenzwerte (pH, EC, Wassertemperatur; bei Nachtband die weitere Spanne) per
`script.edenic_set_alarm` an den Guardian. Rollen-Modul `bluelab`. Berührt (Original):
`AlertEvaluationService` (neu: `WirksameRegeln`), `pages/collections.tsx` (Zeile unter Grenzwerte).

**Geräte und Rollen statt Entity-IDs.** Die Steuerungen sprechen Rollen an, die
mit Suchfeld, HA-Vorschlägen und Livewert zugeordnet werden — kein Gerätename
steht mehr im Code. Alles an einer Stelle, *Geräte & Entitäten*: Reiter Geräte
(alle benutzten Entitäten nach Gerät, aus dem HA-Geräteregister, von Hand
korrigierbar), Rollen (seit forkai.134, vorher eine eigene Seite unter
Steuerung), Messgrößen und Wartung.

**Kosten.** Strom aus HA-Zählerständen je Grow und Phase, Verbrauchsartikel mit
Füllungen und Gaben, Anschaffungen, Kosten je Tag und Pflanze, Laufzeit- und
Ernteprognose. Ein Artikel kann lagerneutral geführt werden, damit ein Kanister,
der drei Läufe hält, nicht den Lauf verzerrt, in dem er gekauft wurde.

**Bedienung am Telefon.** Eigene Titelzeile, Icon-Leiste mit anpassbarer
Reihenfolge, ein Erfassen-Blatt für Messung, Addback, Wasserwechsel, Notiz und
Kosten, dazu Auswahllisten als Blatt und Blätter, die sich nach unten wegziehen
lassen. Zugaben lassen sich direkt beim Messen mitbuchen statt als Freitext in der
Notiz zu landen.

**Düngeprogramm SKX Canna Aqua.** R/DWC-Growplan mit 14 Wochenspalten (Root,
Vega 1–4, Flores 1–8, Flush) inklusive Ziel-EC/pH und Klimawerten, auf der
Wissensseite als Wochentabelle lesbar.

## Im Original aufgefallen

Drei Dinge, die nicht am Fork lagen — falls sie von Interesse sind:

- **Programm-Zuordnung über Teilstring.** Die Addback-Seite suchte das
  Düngeprogramm über einen Teilstring-Treffer im Freitext statt über die am Grow
  gespeicherte Id. Ein zweites Programm, dessen Name den eines anderen enthält
  („SKX Canna Aqua" neben „Canna Aqua"), wird dadurch falsch aufgelöst. Behoben in
  `AddbackPage.tsx` und in `CultivationKnowledgeService.MatchProgram` (exakter Name
  bzw. längster Treffer) — das ist der Punkt, der sich am saubersten
  herausschneiden lässt.
- **Kachel und Alarm nannten unterschiedliche Zahlen.** Die Live-Kachel las das
  Zielband der laufenden Woche, die Alarmauswertung kannte es nicht und prüfte nur
  gegen die eingetragenen Regeln. Für dieselbe Messgröße standen so zwei Bänder
  nebeneinander. Im Fork gehen beide durch dieselbe Lesart.
- **Sollwerte an zwei Stellen abgeholt.** Die meisten Leser gehen über
  `Zielband.FuerGrow` (Profil → Phase → Wochenspalte → eigene Grenzen). Der
  Trendwächter (`TrendWatchRunner`) und der Addback-Vorschlag
  (`GrowWorkflowApiController`) riefen dagegen `TargetValueService.GetTargets`
  direkt auf und übergingen damit die Wochenspalte — im Original ohne Folgen,
  weil dort nur EC und pH je Woche stehen, im Fork mit Klimawerten je Woche
  aber spürbar. Behoben in forkai.107; beide Stellen brauchen dafür die
  Wissensbasis als Abhängigkeit.
- **Erster Reiterwechsel springt nach oben.** `AppShell.tsx` rollt beim
  Seitenwechsel nach oben und hing dafür an `[location.pathname, wechselArt]`.
  Reiter, die in der Adresse stehen (`setParams(…, { replace: true })`), ändern
  beim ersten Wechsel die Navigationsart von PUSH auf REPLACE — der Effekt läuft,
  obwohl die Seite dieselbe ist. Im Fork (forkai.151) merkt sich der Effekt den
  zuletzt gerollten Pfad; ein Einzeiler, gut übernehmbar.
- **Reiterzeile am Telefon.** Auf schmalen Bildschirmen brach die Reiterzeile um
  und halbierte die Bewertungsscheibe darunter. Im Fork gelöst, aber durch einen
  eigenen Rahmen — als Vorlage taugt das eher zum Nachbauen als zum Übernehmen.

## Berührte Original-Dateien

Eigene Funktionen liegen möglichst in eigenen Dateien und Tabellen (`Fork*`).
Angefasst ist der Bestandscode dort, wo es nicht anders ging: Alarmauswertung und
Zielband, Kachel-Modell und Live-Payload, Wissens-Schema und Mischplan (Klimawerte
je Woche), Trendwächter und Grow-Workflow (Sollwerte über das Zielband),
Navigation und App-Shell (inkl. `V1Tabs` mit optionalem `insBild`), Wissens-Loader (ein Haken nach dem Laden für
eigene Wochenwerte), Zelt-Einstellungen (Blatt-Offset an den
Controller), Live-Kopfzeile, Sollwert-Profile, Grow-Formular und Addback, Benachrichtigungen, Hydro-Editor, Wochenplan, Sammelseite mit Reitern, Wissens-Vertrag, Grow-Seite, Ernte, Workflow-Controller, Grow-Controller
(Plan beim Anlegen) sowie `Program.cs` (Plan-Übernahme beim Start). Welche
Datei zu welcher Änderung gehört, steht in
[docs/fork-historie.md](docs/fork-historie.md).
## Geplant

Die beiden ursprünglich hier notierten Punkte sind erledigt: Wochenwerte im
Sollwertprofil mit forkai.46, der Export der aktiven Wochenwerte nach Home
Assistant mit forkai.53. Neue Vorhaben kommen hierher.

## Empfohlen: ohne die Kopfleiste von Home Assistant

Das Add-on läuft im Ingress von Home Assistant, also in einem iframe unterhalb
der HA-Kopfleiste mit dem Menü-Zeichen. **Ein Add-on kann diese Leiste nicht
entfernen** — sie gehört Home Assistant, nicht uns. Am Telefon kostet sie
zusammen mit der eigenen Titelzeile spürbar Platz.

Wer sie loswerden will, installiert die HACS-Integration
[**Ingress**](https://github.com/lovelylain/hass_ingress) (`lovelylain/hass_ingress`)
und trägt in `configuration.yaml` ein:

```yaml
ingress:
  growos:
    work_mode: hassio
    url: d48160c2_grow_os_fork_ai   # eigener Add-on-Slug, siehe Add-on-Seite in HA
    ui_mode: normal                 # 'normal' = ohne HA-Kopfleiste
    title: Grow OS
    icon: mdi:sprout
```

Danach Home Assistant neu starten. In der Seitenleiste erscheint ein neuer
Eintrag, der Grow OS bildschirmfüllend öffnet; der alte Add-on-Eintrag bleibt
daneben bestehen.

**Das ist freiwillig.** Ohne die Integration funktioniert alles genauso — die
Titelzeile erkennt selbst, ob die HA-Kopfleiste über ihr liegt, und lässt dann
den Namenszug weg, damit er nicht zweimal untereinander steht
(`useHomeAssistantFrame.ts`). Das „⌂"-Zeichen springt in beiden Fällen zurück;
wohin, steht unter Einstellungen → Darstellung (Voreinstellung `/lovelace/0`).

Nicht gangbar sind zwei naheliegende Wege, beide ausprobiert: eine
`webpage`-Karte auf die Ingress-Adresse verliert nach kurzer Zeit ihr
Sitzungs-Cookie, und `kiosk-mode` blendet nur Kopfzeilen von
Lovelace-Dashboards aus — ein Ingress-Panel ist keines.

## Nur für den Fork geändert (nicht zur Übernahme gedacht)

- `grow-os/config.yaml`: Name, Slug, Image, Version `2.0.0-forkai.N`
- `repository.yaml`: Add-on-Repository des Forks

## Abgleich mit dem Original

Änderungen des Originals kommen nicht automatisch. Vorgehen: auf GitHub „Sync fork"
(holt `main` des Originals herein), Konflikte in `grow-os/config.yaml` und
`grow-os/CHANGELOG.md` von Hand lösen, Version `forkai.N+1` setzen, Changelog,
Build, Release. Eigene Funktionen liegen möglichst in eigenen Dateien, damit der
Abgleich konfliktarm bleibt.
