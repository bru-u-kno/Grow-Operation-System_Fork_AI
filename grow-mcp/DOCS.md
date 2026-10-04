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
| — (nur ein gültiger Schlüssel) | `zugriff_pruefen`, `ha_bereiche`, `ha_zustaende`, `ha_verlauf` |
| Dokumentieren | `messung_eintragen`, `messung_aendern`, `journal_eintragen`, `aufgabe_erledigen`, `wartung_eintragen`, `kalibrierung_eintragen` |
| Grow planen | `phase_bestaetigen` (Keimung, Bewurzelung, Veg, Blüte/Flip, Finish) |
| Geräte schalten | `pumpe_dosieren`, `pumpe_stoppen`, `licht_schalten`, `ha_dienst` |
| Verwaltung | zusätzlich für `ha_dienst` auf Automationen, Skripte, Szenen und Helfer |

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
- „Hak die Aufgabe Filter reinigen ab." → `aufgaben`, dann `aufgabe_erledigen`
- „pH-Sonde kalibriert, 7,00 Referenz, vorher 7,12, nachher 7,01." → `technik`,
  dann `kalibrierung_eintragen`
- „Grow 4 ist auf 12/12 geflippt." → `phase_bestaetigen` mit `bluete`
- „Gib 2 ml pH-Minus." → `pumpe_dosieren`; Grow OS meldet, ob wirklich dosiert
  wurde
- „Licht auf Stufe 7." → `licht_schalten`
- „Wie warm war es die letzte Nacht im Zelt?" → `ha_zustaende`, dann `ha_verlauf`

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
