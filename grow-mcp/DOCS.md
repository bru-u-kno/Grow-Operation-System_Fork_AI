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
| `wissen_liste`, `wissen_nachschlagen` | Abläufe, Behandlungen, Symptome, Erreger, Sollwerte |
| `suchen` | Volltextsuche, wenn das Kürzel noch fehlt |

## Sicherheit

- **Nur dein Netz.** Der Port 5080 ist im Heimnetz offen, nicht im Internet. Für
  Claude im Browser auf claude.ai müsste dein Home Assistant öffentlich
  erreichbar sein — das will dieses Add-on ausdrücklich nicht.
- **Nur mit Schlüssel.** Beim ersten Start wird einer erzeugt und gespeichert.
  Ohne ihn antwortet die Schnittstelle mit 401.
- **Der Schlüssel steht nur auf der Ingress-Seite.** Über Port 5080 ist diese
  Seite nicht erreichbar — sonst könnte sich jeder im Netz den Schlüssel
  abholen.
- **Nur lesend.** Es gibt kein Werkzeug zum Dosieren, Schalten oder Ändern.
  Claude kann dir sagen, was zu tun wäre; tun musst du es in Grow OS.
- **Neuen Schlüssel?** Datei `mcp-token` im Add-on-Speicher löschen und neu
  starten. Die alten Klienten müssen dann neu eingerichtet werden.

## Wenn Grow OS nicht gefunden wird

Normalerweise findet der Server Grow OS von selbst, solange beide aus diesem
Repository stammen. Sonst steht der Name in Home Assistant unter
**Grow OS → Info → Hostname**; unter `grow_os_adresse` eintragen, mit Port:

```
http://a1b2c3d4-grow-os-fork-ai:5076
```
