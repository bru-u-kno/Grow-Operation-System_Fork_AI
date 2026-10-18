# Grow MCP Fork AI — Home Assistant Add-on

Gibt Claude auf deinem Rechner Zugriff auf deine Anlage. Nicht als fertigen
Textblock wie bei der **Berater-Mappe**, die Grow OS zum Herunterladen anbietet,
sondern als Werkzeuge: Claude fragt gezielt nach, was es gerade braucht — den
Lagebericht, den EC-Verlauf der letzten zwei Wochen, einen bestimmten Ablauf aus
dem Fachwissen.

Genau das kann die Mappe nicht. Sie hält den Stand von *jetzt* fest. „Wie hat
sich mein pH die letzten 14 Tage bewegt?" beantwortet nur dieses Add-on.

> **Fork AI.** Dieser MCP gehört zu **Grow OS Fork AI** und liest dessen Daten.
> Er läuft neben dem Grow MCP des Originals: eigener Port **5080** (das Original
> belegt 5079) und eigener Name beim Klienten, `grow-os-fork-ai`.

## Voraussetzungen

- **Grow OS Fork AI** ist installiert und läuft.
- Ein MCP-Klient auf einem Rechner in deinem Netz. Getestet mit **Claude Code**;
  alles, was MCP über HTTP spricht, funktioniert genauso.

## Installation

1. **Einstellungen → Add-ons → Add-on-Store**, dieses Repository ist bereits
   eingetragen, wenn du Grow OS von hier hast.
2. **Grow MCP Fork AI** installieren und starten.
3. Auf der Info-Seite **„Im Seitenleisten-Menü anzeigen"** einschalten.
4. Die Seite öffnen. Dort steht ein fertiger Befehl — kopieren und auf dem
   Rechner ausführen, auf dem Claude Code läuft. Fertig.

Der Befehl sieht so aus:

```
claude mcp add --transport http grow-os-fork-ai http://homeassistant.local:5080/mcp --header "Authorization: Bearer <dein-schlüssel>"
```

**Wenn du Home Assistant über eine Domain von aussen benutzt**, fragt die Seite
nach einer Adresse, statt eine vorzuschlagen. Das ist Absicht: Port 5080 ist nur
im eigenen Netz offen, über die Adresse von aussen kommt dort niemand an. Trag
die Adresse ein, unter der dein Server im Heimnetz erreichbar ist — meist eine
IP wie `192.168.1.50`, zu finden in Home Assistant unter *Einstellungen → System
→ Netzwerk*. Der Befehl auf der Seite schreibt sich beim Tippen mit.

## Was Claude damit kann

| Werkzeug | Wofür |
| --- | --- |
| `grows_auflisten` | Welche Grows laufen |
| `lagebericht` | Der ganze Stand eines Grows als Text |
| `messwert_verlauf` | pH, EC, Temperatur & Co. über Tage oder Wochen |
| `trends` | Was Grow OS selbst an Bewegung erkannt hat |
| `abweichungen` | Wo es vom Sollwert weg läuft, mit Vorschlägen |
| `alarme` | Offene Risiken und die eingestellten Grenzwerte |
| `dosierungen` | Pumpen am Zelt und Protokoll der letzten Dosen |
| `dosier_vorschlag` | Was Grow OS jetzt dosieren würde, und warum — oder warum nicht |
| `ablauf_fortschritt` | Welche Abläufe laufen und wie weit |
| `licht` | Eingestellter Zyklus gegen tatsächliche Schaltzeiten |
| `technik` | Geräte, anstehende Wartungen und Kalibrierungen |
| `anlage` | Volumen, Pumpen, Kühler, UV, Topfzahl |
| `sorte` | Blütewochen, Stretch, Düngerbedarf |
| `pflanzen` | Einzelne Pflanzen, dazu der Pheno Hunt |
| `journal` | Deine eigenen Einträge |
| `aufgaben` | Die Aufgaben eines Grows mit Id und Zustand |
| `wissen_liste`, `wissen_nachschlagen` | Abläufe, Behandlungen, Symptome, Erreger, Sollwerte |
| `suchen` | Volltextsuche, wenn das Kürzel noch fehlt |

## Eintragen und Schalten

Mit dem Schlüssel von der Einrichtungsseite liest Claude nur. Zum **Eintragen
und Schalten** braucht es einen Schlüssel aus Grow OS selbst:

1. In Grow OS **Einrichtung → KI-Assistent → Zugriff & Schlüssel** öffnen, den
   Hauptschalter einschalten und einen **neuen Schlüssel** anlegen. Dabei die
   Stufen anhaken, die der Assistent haben soll (siehe unten). Der Schlüssel
   beginnt mit `gok_` und wird nur einmal angezeigt.
2. Im Befehl von der Einrichtungsseite den MCP-Schlüssel durch diesen
   Schlüssel **ersetzen** — derselbe Connector, nur ein anderer Schlüssel:

   ```
   claude mcp add --transport http grow-os-fork-ai http://homeassistant.local:5080/mcp --header "Authorization: Bearer gok_…"
   ```

   Wer schon verbunden ist: `claude mcp remove grow-os-fork-ai`, dann neu
   hinzufügen.

Mit dem Fork-Schlüssel gehen weiter alle lesenden Werkzeuge. Der Grow MCP
reicht den Schlüssel bei **jeder** Anfrage an Grow OS durch und entscheidet
selbst nichts: welche Stufe frei ist, wie viele ml eine Dosis höchstens haben
darf, wie viele Schaltbefehle je Stunde gehen, ob der Schlüssel gesperrt ist —
das prüft Grow OS. Sagt Grow OS Nein, bekommt Claude den Grund als Satz, etwa
„Dafür fehlt die Freigabe für Stufe ‚Grow planen'". Jede schreibende Anfrage
steht in Grow OS im Prüfprotokoll.

### Die Stufen

| Stufe | Werkzeuge |
| --- | --- |
| — (nur ein gültiger Schlüssel) | `zugriff_pruefen`, `raum`, `ha_bereiche`, `ha_zustaende`, `ha_verlauf` |
| Dokumentieren | `messung_eintragen`, `messung_aendern`, `messung_loeschen`, `journal_eintragen`, `journal_aendern`, `journal_loeschen`, `aufgabe_anlegen`, `aufgabe_erledigen`, `wartung_eintragen`, `kalibrierung_eintragen` |
| Grow planen | `phase_bestaetigen` (Keimung, Bewurzelung, Veg, Blüte/Flip, Finish), `grenzwert_setzen`, `lichtplan_setzen` |
| Geräte schalten | `pumpe_dosieren`, `pumpe_stoppen`, `licht_schalten`, `ha_dienst` |
| Verwaltung | `raum_setzen`, `zelt_aufstellraum_setzen`, `sensor_zuordnen`, dazu `sensoren` (liest nur, aber die Zelt-Einstellungen liegen hinter Verwaltung); zusätzlich für `ha_dienst` auf Automationen, Skripte, Szenen und Helfer |

Manche Dienste in Home Assistant gehen über einen Schlüssel **nie**, egal mit
welcher Stufe — etwa Neustart (`homeassistant`), Add-ons (`hassio`),
Sicherungen, Schlösser, Alarmanlage, Benachrichtigungen, `update`, `mqtt`,
`downloader` und jedes `reload`. Die vollständige Liste mit Begründungen führt
Grow OS.

Claude ruft zuerst `zugriff_pruefen` auf: dort steht, welche Stufen frei sind,
**wobei es vorher nachfragen soll** und welche Höchstwerte gelten. Die
Rückfrage ist eine Bitte an den Assistenten — durchsetzen kann Grow OS nur die
Stufen.

### Zum Diktieren

- „Grow 4: pH 5,8, EC 1,2, ORP 450, Wasser 19,5 Grad, 3 Liter nachgefüllt."
  → `messung_eintragen` (ohne Phase gilt die aktuelle des Grows)
- „Die Messung von eben: der pH war 5,9." → `messung_aendern`, alle anderen
  Werte bleiben stehen
- „Ins Journal: 2 ml pH-Minus und 300 ml Pyrolyt gegeben." → `journal_eintragen`
- „Im Wasserwechsel-Eintrag fehlt noch: Purolyt 200 ml." → `journal`, dann
  `journal_aendern` mit `anhaengen` (der alte Text bleibt; `text` ersetzt ihn ganz)
- „Hak die Aufgabe Filter reinigen ab." → `aufgaben`, dann `aufgabe_erledigen`
- „Leg mir eine Aufgabe an: Filter am Sonntag reinigen." → `aufgabe_anlegen`
- „pH-Sonde kalibriert, 7,00 Referenz, vorher 7,12, nachher 7,01." → `technik`,
  dann `kalibrierung_eintragen`
- „Grow 4 ist auf 12/12 geflippt." → `phase_bestaetigen` mit `bluete`
- „Gib 2 ml pH-Minus." → `pumpe_dosieren`; Grow OS meldet, ob wirklich dosiert
  wurde
- „Licht auf Stufe 7." → `licht_schalten`
- „Nenne den Raum Keller und stell das Zelt hinein." → `raum`, dann `raum_setzen` und
  `zelt_aufstellraum_setzen` (ändert am Zelt nur die Raumzuordnung; alles andere bleibt, per Prüfsumme belegt)
- „Wie warm war es die letzte Nacht im Zelt?" → `ha_zustaende`, dann `ha_verlauf`
- „Ordne die Außenwerte vom Controller dem Zelt zu." → `sensoren`, dann `sensor_zuordnen` je Messgröße (etwa „Außen Temp."
  → `sensor.controller_temperatur`). Das Werkzeug prüft vorher, ob es die Entität in Home Assistant gibt (bei einem Tippfehler
  mit Vorschlag) und ob ihre Art passt; danach vergleicht es alle übrigen Zuordnungen vorher/nachher — ihre Ids bleiben gleich.
  Leer oder „keine" löst eine Zuordnung, die Zeile bleibt dann inaktiv stehen.
- „Lösch den Testeintrag im Journal." → `journal`, dann `journal_loeschen`; die Antwort enthält Datum und den ganzen Text,
  damit er sich wieder eintragen lässt
- „Die Messung von eben war Unsinn, weg damit." → `messung_loeschen`; die Antwort nennt jeden Wert (einzelne Werte
  korrigiert `messung_aendern`)
- „Melde erst ab 28 Grad." → `alarme`, dann `grenzwert_setzen` (Lufttemp., max, 28). Nur diese eine Grenze ändert sich,
  der Rest ist per Prüfsumme belegt; ob die Zahl physikalisch möglich ist, prüft Grow OS
- „Lichtplan auf 7 bis 19 Uhr." → `licht`, dann `lichtplan_setzen`. Das ändert den Lichtplan in Grow OS (Nachtabsenkung,
  Lichteinbruch-Wächter, Auto-Messungen, Nachtgrenzen), nicht den Zeitplan am Controller

## Kamera, Kosten, Pflanzen, Regelungen

| Werkzeug | Stufe | Wofür |
| --- | --- | --- |
| `kamera_bild` | keine | Das Livebild der Zelt-Kamera eines Grows als Bild; sagt, ob es frisch ist oder das letzte gültige |
| `steuerung` | keine | Zustand aller Regelungen in Klartext, mit `modul` Einstellungen, Live-Werte und Abweichungen; ein laufender Probelauf |
| `kosten` | keine | Kosten eines Grows kurz: Summe, Strom, offene Gebinde mit Reichweite, Artikel mit Id |
| `verbrauch_buchen` | Dokumentieren | Eine Gabe aus einem Artikel buchen („20 ml pH-Minus"); den CO₂-Artikel der CO₂-Steuerung lehnt es ab (sonst doppelt gebucht) |
| `nachfuellung_eintragen` | Dokumentieren | Neues Gebinde mit Preis; `vorherigeLeer` sagt ausdrücklich, ob die offene Füllung geschlossen wird |
| `anschaffung_eintragen` | Dokumentieren | Einen Kauf verbuchen, mit Gesamtbetrag |
| `pflanze_aendern` | Grow planen | Status, Sorte, Topf, Name oder Notiz einer Pflanze — nur das Genannte, der Rest per Prüfsumme belegt |
| `probelauf_abbrechen` | Geräte schalten | Not-Aus für einen Probelauf; scheitert nie am Höchstwert der Schaltbefehle |
| `tagesbilder` | keine | Die täglichen Kamerabilder eines Zelts (ab 12 Uhr, erste Kamera): Liste der Tage und Kameras, mit `datum` das Bild |
| `kamera_foto_speichern` | Dokumentieren | Das aktuelle Kamerabild als Foto in den Grow, mit Motiv und Bildunterschrift (nur laufende Grows, nur Kameras des Zelts) |

Artikel und Sorten gehen per Name; passen mehrere, kommt die Auswahl zurück und nichts wird gebucht. Beträge
über 500 € nennt die Antwort ausdrücklich, negative und Zeitpunkte in der Zukunft werden abgelehnt. Kosten
**löschen** braucht in Grow OS die Stufe Verwaltung (mit Sicherung vorher).

- „Neue CO₂-Flasche, 10 Kilo für 38 Euro, die alte ist leer." → `nachfuellung_eintragen` mit `vorherigeLeer=true`
- „Pflanze 2 ist ein Zwitter, die ist raus." → `pflanze_aendern` mit `status=raus`
- „Warum läuft der Entfeuchter gerade nicht?" → `steuerung` mit `modul=entfeuchter`
- „Wie sah das Zelt letzten Sonntag aus?" → `tagesbilder` (Liste), dann mit `datum`
- „Mach ein Foto fürs Tagebuch, Blätter oben werden hell." → `kamera_foto_speichern` mit `motiv=blatt`

## Alles im Fork steuern: `fork_funktionen` und `fork_ausfuehren`

Seit Grow OS Fork AI mit Funktionsverzeichnis lässt sich über den Grow MCP **jede** Funktion von Grow OS
erreichen, nicht nur die festen Werkzeuge oben. Grow OS erzeugt dafür selbst ein Verzeichnis aus seinem
Code (`GET /api/ki-zugriff/funktionen`, je Methode und Weg ein Eintrag): Weg, Art, was ein Schlüssel dafür braucht,
Sicherung vorher, Schaltbefehl, Beschreibung und erwartete Angaben. Neue Funktionen stehen automatisch
darin; die Einstufung kommt aus derselben Regel, mit der Grow OS jede Anfrage prüft.

| Werkzeug | Stufe | Wofür |
| --- | --- | --- |
| `fork_funktionen` | keine (geht auch mit dem MCP-Schlüssel) | Das Verzeichnis durchsuchen: `suche` („kosten nachfuellung"), `bereich`, `art`, `zugang`. Bei wenigen Treffern mit allen Feldern, sonst eine Zeile je Funktion oder die Bereiche. |
| `fork_ausfuehren` | je Funktion, Grow OS prüft | Eine Funktion aus dem Verzeichnis aufrufen: `methode`, `pfad` (etwa `api/plants/7`), `query`, `body` (JSON). |

Was `fork_ausfuehren` zusätzlich tut — es fügt keine Rechte hinzu, es verengt nur:

- **Nur, was im Verzeichnis steht**, und nur Wege unter `api/` — kein `..`, keine Adresse, kein anderer Host,
  keine kodierten Zeichen. Wege, die Grow OS als „nie über einen Schlüssel" führt, lehnt es ab, bevor es sie ruft.
- **Vorher und nachher lesen:** Kennt das Verzeichnis einen GET auf denselben Weg, meldet die Antwort, was
  sich geändert hat („einstellungen.hystereseProzent: 2 → 3; 27 Werte unverändert"), und nennt ausdrücklich,
  was sich verändert hat, ohne im Rumpf zu stehen. Live-Werte, die sich schon zwischen zwei Lesungen davor
  bewegen, vergleicht es nicht. Vor einem Löschen liest es den Eintrag und nennt ihn in der Antwort.
- **PUT ergänzen:** Grow OS ersetzt bei PUT meist das ganze Objekt. Fehlende Rumpffelder füllt das Werkzeug
  aus dem gelesenen Stand (aus dem Teil, in dem die meisten Rumpffelder stehen, etwa `einstellungen`), damit „nur
  die Hysterese ändern" nicht alles andere zurücksetzt; `ergaenzen=false` schickt nur den Rumpf. Fehlt im Rumpf eine
  **Liste** (Sensoren, Kacheln …), bricht es ab: was Grow OS mit einer fehlenden Liste tut, ist je Weg verschieden.
  Ist der Stand nicht lesbar oder passt er nicht zum Rumpf, geht kein halber PUT hinaus.
- **Keine Dateien:** kein Hochladen, keine Bilder oder Sicherungsdateien in der Antwort (dafür `foto_ansehen`).

**Was über einen Schlüssel nie geht** — egal mit welcher Stufe, auch nicht über `fork_ausfuehren`:
Schlüsselverwaltung, KI-Hauptschalter, Sicherung zurückspielen oder herunterladen, Home-Assistant-Adresse
und -Token ändern. Das bleibt dem Menschen in der Oberfläche von Grow OS. Maßgeblich ist, was Grow OS selbst
als „nie" führt: `fork_funktionen` mit `zugang=nie`.

Für Alltägliches bleiben die festen Werkzeuge der bessere Weg: sie prüfen mehr (Messfelder, Höchstwerte,
Prüfsummen) und antworten in Sätzen.

- „Stell beim Entfeuchter die Hysterese auf 3 Prozent." → `fork_funktionen` (suche „entfeuchter"), dann
  `fork_ausfuehren` mit `PUT`, `api/steuerung/entfeuchter`, `{"hystereseProzent":3}`

## Sicherheit

- **Nur dein Netz.** Der Port 5080 ist im Heimnetz offen, nicht im Internet. Für
  Claude im Browser auf claude.ai müsste dein Home Assistant öffentlich
  erreichbar sein — das will dieses Add-on ausdrücklich nicht.
- **Nur mit Schlüssel.** Beim ersten Start wird einer erzeugt und gespeichert.
  Ohne ihn antwortet die Schnittstelle mit 401.
- **Der Schlüssel steht nur auf der Ingress-Seite.** Über Port 5080 ist diese
  Seite nicht erreichbar — sonst könnte sich jeder im Netz den Schlüssel
  abholen.
- **Mit dem MCP-Schlüssel nur lesend.** Eintragen und Schalten gehen nur mit
  einem Schlüssel aus Grow OS (siehe oben), und nur so weit, wie du es dort
  angehakt hast. Mit dem MCP-Schlüssel antworten diese Werkzeuge nur mit dem
  Hinweis, wo es den richtigen Schlüssel gibt — Grow OS wird gar nicht gefragt.
- **Der Fork-Schlüssel wird nicht gespeichert.** Der Grow MCP liest ihn bei
  jeder Anfrage aus dem Kopf, reicht ihn an Grow OS weiter und vergisst ihn.
  Gesperrt oder gelöscht wird er in Grow OS; das wirkt sofort.
- **Neuen Schlüssel?** Datei `mcp-token` im Add-on-Speicher löschen und neu
  starten. Die alten Klienten müssen dann neu eingerichtet werden.

## Wenn Grow OS nicht gefunden wird

Normalerweise findet der Server Grow OS von selbst, solange beide aus diesem
Repository stammen. Sonst steht der Name in Home Assistant unter
**Grow OS → Info → Hostname**; unter `grow_os_adresse` eintragen, mit Port:

```
http://a1b2c3d4-grow-os-fork-ai:5076
```
