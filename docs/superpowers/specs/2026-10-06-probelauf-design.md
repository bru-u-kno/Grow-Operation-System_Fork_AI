# Probelauf für Steuerungen — Entwurf

Stand: 2026-10-06 · Branch `probelauf` · Status: Entwurf, von Bru im Gespräch bestätigt, Spezifikation wartet auf Durchsicht.

## Ziel
Bru will eine Steuerung oder ein Gerät gezielt für 10–30 Minuten abschalten, um real zu sehen, wie sich
Temperatur, Luftfeuchte, VPD und weitere Werte entwickeln. Aus den Zahlen sollen Rückschlüsse auf
Einstellungen und weitere Automatisierung entstehen. Ergebnisse gehen in den Wissensspeicher
(Vault), damit spätere Sitzungen darauf aufbauen.

Erfolg heißt:
1. Ein Lauf liefert belastbare Zahlen (vorher / während / nachher) zu allen Messwerten des Zelts.
2. Nach jedem Lauf — auch nach Abbruch, Fehler oder Neustart — ist alles wieder wie vorher.
3. Bru bekommt eine Empfehlung der KI und übernimmt sie nur nach Bestätigung.

## Festgelegt (Bru, 2026-10-06)
- Wirkung real am Zelt; akzeptiert, solange die Grenzen halten.
- Nach dem Lauf: Auswertung **plus** KI-Empfehlung, nichts wird ohne Bestätigung übernommen.
- Bei Grenzverletzung: **automatisch abbrechen** und zurückstellen; der Lauf wird bis dahin ausgewertet.
- **Alle** Geräte, die der Fork steuert, kommen in Betracht (siehe „Geräte").

## Ansatz
Eigener Dienst im Fork (nicht HA-Automation mit Timer): Rückstellung, Aufzeichnung und Stufenprüfung
liegen an einer Stelle, ein Lauf hat eine Historie.

## Geräte (Eingriffsarten)
Pro Modul wird die Art des Eingriffs festgelegt; die Liste kommt aus `SteuerungBauteile`/`SteuerungGeraeteRollen`,
nicht aus dem Kopf (im Plan gegen den Code zu belegen):

| Modul | Eingriff im Lauf | Rückstellung |
|---|---|---|
| entfeuchter, entfeuchter-zusatz | Regelung/Port aus | Regelung an, Port in Ausgangszustand |
| chiller | Regelung aus (`ChillerControlEnabled`), Schalter aus | Regelung an, Schalter wie vorher |
| zuluft | Regelung aus, Stufe auf Ausgangswert festhalten oder aus | Stufe + Regelung wie vorher |
| co2 | Dosierung pausieren | Dosierung an |
| licht | **nur auf Brus ausdrückliche Wahl**: Dimmen/Aus ändert die Lichtphase | Lichtbefehl wie vorher |

Der Ausgangszustand wird vor dem Eingriff gelesen und mit dem Lauf gespeichert.

## Ablauf eines Laufs
1. **Start** (UI oder KI-Schlüssel, Stufe „Geräte schalten"): Gerät, Dauer (Höchstwert, Voreinstellung 20 Min.), Grenzen.
2. **Vorher:** letzte 10 Min. aus dem HA-Verlauf als Basis (kein Warten); Ausgangszustand sichern.
3. **Eingriff** starten; Wecker für Ende und harte Obergrenze speichern (SQLite).
4. **Während:** alle paar Sekunden Grenzen prüfen. Verletzung → sofort Abbruch.
5. **Ende/Abbruch:** zurückstellen mit `Ausschalter`-Nachkontrolle bzw. Wiederholung, dann 10 Min. Nachlauf aufzeichnen.
6. **Auswertung** speichern.

## Sicherheit
- Grenzen vor dem Start, Voreinstellung aus den Pflanzenzielen (Luftfeuchte max., Temperatur max., VPD-Band).
- Wecker liegt in der Datenbank. Beim Add-on-Start prüft der Fork als Erstes auf offene Läufe und stellt zurück.
- Rückstellung mit Nachkontrolle (AC-Wolke verwirft Aufträge still).
- Nur **ein** Lauf gleichzeitig; Start abgelehnt, wenn eine andere Steuerung schon im Eingriff ist.
- Aufträge über KI-Schlüssel zählen gegen die Höchstwerte des Schlüssels.
- Lichtphase und Außenwetter (falls vorhanden) werden mit gespeichert, damit Läufe vergleichbar bleiben.

## Auswertung
- Je Messwert: Start, Spitze, Ende, Änderung je Minute, Erholungszeit nach Rückstellung.
- Eine Kurve mit den drei Abschnitten.
- Hinweis, wenn sich während des Laufs Lichtphase oder Außenwerte änderten („ein Lauf belegt wenig").

## KI-Empfehlung
Über das vorhandene KI-System; liefert Vorschläge zu Schwellen und Nachlaufzeiten. Anwenden nur nach Bestätigung durch Bru.

## Wissensspeicher
Der Fork hält die Läufe (Daten + Auswertung) vor. Claude trägt nach einem Lauf eine Notiz im Vault ein
(`Grow/…` bzw. Fork-Notiz mit Gerät, Dauer, Zahlen, Schluss) und verknüpft sie mit der Aufgabennotiz.

## Oberfläche
Eine schlanke Seite: Gerät, Dauer, Start; Grenzen vorbelegt mit „Empfohlen", „Erweitert" eingeklappt.
Liste vergangener Läufe zum Vergleichen. Auf der Seite sind keine Einzelwerte nötig, die Bru nicht versteht.

## Prüfplan (Abnahme, am laufenden Stand mit echten Daten)
1. Echter 10-Minuten-Lauf mit dem Zusatz-Entfeuchter: Zustand vorher/nachher, Zahlen der Auswertung.
2. Erzwungener Grenzabbruch (Grenze knapp unter dem Istwert): Abbruchzeit und Rückstellung belegen.
3. Neustart des Add-ons mitten im Lauf: Rückstellung beim Start belegen.
4. Zweiter Start während eines Laufs wird abgelehnt.
5. Prüfung, dass 2 und 3 **ohne** die Schutzlogik fehlschlagen würden (Prüfung beißt).
6. Nachprüfung nach ~24 h: Läufe stehen noch in der Liste, kein Gerät blieb im Eingriffszustand.
Nicht prüfbar: Wirkung bei Licht aus, falls der Lauf nur tagsüber stattfindet — ausdrücklich im Bericht nennen.

## Offen für den Plan
- Genaue Eingriffsart je Modul (Spalte oben) gegen den Code belegen; bei Zuluft/Licht entscheiden, ob „aus" oder „festhalten" sinnvoll ist.
- Ob Außenwerte (Branch `aussenwerte`, andere Sitzung) schon nutzbar sind; sonst später ergänzen.
- Aufgabennotiz im Vault anlegen (A-0xx mit Abschnitt „Abnahme").
