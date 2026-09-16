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

**Alarmgrenzen können dem Wochenplan folgen.** Je Regel umschaltbar zwischen
festen Zahlen (Vorgabe, unverändertes Verhalten) und dem Zielband der laufenden
Woche ± Toleranz. Damit wandern pH und EC mit der Blütewoche mit, statt ab Woche 4
dauerhaft zu melden.

**Klimawerte je Woche statt nur je Phase.** Die Feed-Chart-Spalte trägt zusätzlich
Wassertemperatur Tag/Nacht, VPD, CO₂, PPFD, RH-Obergrenze und Lufttemperatur. Eine
Wochenplan-Seite zeigt die laufende Woche mit allen Plan-Werten, den Ankern
(Vegi-Start, Flip, Erntefenster) und dem Hinweis, wenn eine gestreckte Phase die
letzte Spalte hält. Die Wochenwerte lassen sich dort auch bearbeiten — gespeichert
wird nur die Abweichung vom Plan, die Programmdatei bleibt unberührt.

**Übergabe an Home Assistant.** Die Wochenwerte gehen beim Wochenwechsel und
täglich um 06:00 in HA-Helfer, nach denen dort die Automationen regeln. Von Hand
verstellte Helfer erkennt der Abgleich, überschreibt sie nicht und bietet sie zum
Freigeben an. Jeder Helfer hat dabei genau eine schreibende Stelle im Fork: was
der Wochenplan führt, lassen CO₂- und Kühler-Seite aus; das CO₂-Ziel schreibt
allein die CO₂-Steuerung als Staffel aus dem Wochenwert. Leitgedanke: Grow OS
plant, Home Assistant regelt.

**Tag/Nacht-Zielwerte.** Die Zelt-Grenzwerte tragen ein zweites Band für die
Dunkelphase, das überall gleich gilt; die Kachel zeigt beide nebeneinander, das
geltende hell. Ohne Nachtwerte verhält sich alles wie bisher.

**Steuerung.** Ein Leitstand mit Detailseiten für CO₂-Begasung, Licht, Zuluft und
Water Chiller: Sollwerte und Betriebsarten liegen im Fork, geregelt wird weiter in
Home Assistant, und die passenden Automationen liegen als Vorlagen bei. Nur die
Licht-Seite schreibt direkt am AC-Infinity-Port — nur Abweichendes, mit Abstand,
Prüfung und Wiederholung, weil die Cloud parallele Schreibvorgänge verwirft.

**Geräte und Rollen statt Entity-IDs.** Die Steuerungen sprechen Rollen an, die
über eine Seite mit Suchfeld, HA-Vorschlägen und Livewert zugeordnet werden — kein
Gerätename steht mehr im Code. Dazu eine Übersicht aller benutzten Entitäten nach
Gerät, gelesen aus dem HA-Geräteregister und von Hand korrigierbar.

**Kosten.** Strom aus HA-Zählerständen je Grow und Phase, Verbrauchsartikel mit
Füllungen und Gaben, Anschaffungen, Kosten je Tag und Pflanze, Laufzeit- und
Ernteprognose. Ein Artikel kann lagerneutral geführt werden, damit ein Kanister,
der drei Läufe hält, nicht den Lauf verzerrt, in dem er gekauft wurde.

**Bedienung am Telefon.** Eigene Titelzeile, Icon-Leiste mit anpassbarer
Reihenfolge, ein Erfassen-Blatt für Messung, Addback, Wasserwechsel, Notiz und
Kosten, dazu Auswahllisten als Blatt und Blätter, die sich nach unten wegziehen
lassen. Gaben lassen sich direkt beim Messen mitbuchen statt als Freitext in der
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
- **Reiterzeile am Telefon.** Auf schmalen Bildschirmen brach die Reiterzeile um
  und halbierte die Bewertungsscheibe darunter. Im Fork gelöst, aber durch einen
  eigenen Rahmen — als Vorlage taugt das eher zum Nachbauen als zum Übernehmen.

## Berührte Original-Dateien

Eigene Funktionen liegen möglichst in eigenen Dateien und Tabellen (`Fork*`).
Angefasst ist der Bestandscode dort, wo es nicht anders ging: Alarmauswertung und
Zielband, Kachel-Modell und Live-Payload, Wissens-Schema und Mischplan (Klimawerte
je Woche), Trendwächter und Grow-Workflow (Sollwerte über das Zielband),
Navigation und App-Shell, Wissens-Loader (ein Haken nach dem Laden für
eigene Wochenwerte), Zelt-Einstellungen (Blatt-Offset an den
Controller), Live-Kopfzeile, Sollwert-Profile, Grow-Formular und Addback. Welche
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
