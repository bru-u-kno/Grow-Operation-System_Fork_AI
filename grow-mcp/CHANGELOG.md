# Änderungen — Grow MCP

## 0.1.9-forkai.1

**Der Grow MCP des Forks ist jetzt wirklich der des Forks.** Bis 0.1.8 zog er
das Image des Originals und suchte ein Grow OS mit dem Namen des Originals
(`grow_os`) — der Fork heißt `grow_os_fork_ai`. Installiert lief also das
Original, und es fand Grow OS Fork AI nicht.

- Eigenes Image (`ghcr.io/bru-u-kno/grow-operation-system_fork_ai-mcp`) und
  eigener Name: **Grow MCP Fork AI**.
- Findet **Grow OS Fork AI** im eigenen Store von selbst.
- **Port 5080** statt 5079, damit er neben dem Grow MCP des Originals starten
  kann. Der Befehl auf der Einrichtungsseite nennt den neuen Port.
- Beim Klienten heißt er `grow-os-fork-ai` statt `grow-os` — wer beide
  einrichtet, bekommt zwei getrennte Einträge.
- Die Einrichtungsseite mit dem Schlüssel antwortet nur dem Ingress-Proxy von
  Home Assistant (war im Fork schon so, kam aber wegen des fremden Images nie an).

## 0.1.8

**Die Einrichtungsseite nannte eine Adresse, unter der der Server nie erreichbar
war.** Wer Home Assistant von aussen ueber eine Domain benutzt, bekam
`http://meine-domain.de:5079/mcp` vorgeschlagen — und Claude Code blieb im
Verbindungsaufbau haengen, ohne Fehlermeldung. Direkt unter dem Befehl stand
schon damals der Satz „Erreichbar ist das nur in deinem eigenen Netz": die Seite
hat sich selbst widersprochen.

Ursache ist eine Vermischung von zwei Tueren. Die Seite laeuft ueber Ingress und
ist deshalb unter *jeder* Adresse offen, unter der Home Assistant offen ist —
auch einer aus dem Internet. Sie hat diese Adresse einfach uebernommen. Die
MCP-Tuer haengt aber an Port 5079, und der ist mit Absicht nur im eigenen Netz
offen; das ist die halbe Absicherung.

Jetzt prueft die Seite, ob die Adresse ueberhaupt ins Heimnetz zeigt (private
IPv4-Bereiche, Loopback, `.local`, `.fritz.box` und Verwandte). Tut sie es
nicht, sagt die Seite das — und fragt nach der Adresse im Heimnetz, statt eine
zu erfinden. Der Befehl darunter schreibt sich beim Tippen mit, und kopieren
laesst er sich erst, wenn eine Adresse drinsteht.

Automatisch herausfinden kann das Add-on die Adresse nicht: der Supervisor gibt
die Netzwerkangaben nur an Add-ons mit der Rolle `manager` heraus, und die
duerften jedes andere Add-on stoppen und deinstallieren. Diese Rolle ist hier
bewusst nicht gesetzt.

## 0.1.7

Zwei Auskuenfte, die in die Irre fuehrten — beide ohne Fehlermeldung.

- **`pflanzen`** antwortete fuer einen Grow, den es gar nicht gibt, mit einer
  leeren Liste. Das liest sich wie „dieser Grow hat keine Pflanzen" statt wie
  „diese Id gibt es nicht".
- **`sorte`** meldete „keine Sorte zugeordnet", waehrend `grows_auflisten` fuer
  denselben Grow „Purple Lemonade / FastBuds" lieferte. Jetzt nennt es den
  eingetragenen Namen und sagt, was wirklich fehlt: der Eintrag in der
  Sortenliste, und damit Bluetewochen, Stretch und Notizen.

## 0.1.6

Drei Werkzeuge haben eine andere Frage beantwortet als die gestellte. Keines
davon meldete einen Fehler — sie gaben eine plausible Antwort zurueck, und eine
KI kann den Unterschied nicht sehen.

- **`alarme` lieferte die offenen Risiken ALLER Grows.** Ursache in Grow OS: die
  Filter waren eine einzige if-else-Kette mit `openOnly` ganz vorne, die den
  Grow-Filter dahinter verschluckte. In der Weboberflaeche fiel es nie auf, weil
  sie beide Filter nie zusammen schickt — der MCP-Server tut genau das.
- **`technik` zeigte die Wartung und Kalibrierung fremder Zelte.** Beide
  Endpunkte kennen nur einen Filter nach Geraet; hier wurden sie ganz ohne
  Filter geholt. Jetzt werden die Termine je Geraet des Zeltes abgefragt.
- **`grows_auflisten(auchAbgeschlossene: true)` liess die laufenden Grows
  verschwinden.** Der Endpunkt schaltet zwischen zwei Listen um, statt sie
  zusammenzulegen — „auch abgeschlossene" hiess in Wirklichkeit „nur
  abgeschlossene".

Gelesen wird weiterhin ausschliesslich.

## 0.1.5

Zwei Werkzeuge dazu, und ein Fehler behoben, der das Wichtigste aus 0.1.4
unbrauchbar machte.

- **`foto_ansehen` gab nie ein Bild zurueck.** Der Pfad wurde doppelt
  zusammengesetzt (`uploads/uploads/4/x.jpg`), jede Anfrage lief in einen 404.
  Ein Test prueft jetzt den zusammengebauten Pfad. Danach an der laufenden App
  nachgemessen: der richtige Pfad liefert `image/png` mit echter PNG-Signatur.
- `aushaerten` — die Glaeser im Aushaerten mit Tag, Feuchte, Bewertung und dem
  naechsten Lueft-Termin. Ohne Grow-Id alle offenen Glaeser: nach der Ernte gilt
  ein Grow als beendet, das Aushaerten laeuft aber noch 30 bis 60 Tage.
- `symptom_bilder` — die eigenen Aufnahmen des Betreibers zu einem Symptom aus
  der Wissensbasis. Damit laesst sich eine neue Aufnahme mit frueheren Faellen
  aus derselben Anlage vergleichen, statt mit fremden Bildern aus dem Netz.

Zweiundzwanzig Werkzeuge. Gelesen wird weiterhin ausschliesslich.

## 0.1.4

Bilder. Bisher konnte die eigene KI alles lesen, was Grow OS in Zahlen weiss,
aber nichts davon sehen — dabei ist gerade die Pflanze das, was man ansieht,
bevor man misst.

- `fotos` — was für einen Grow fotografiert wurde: Motiv, Datum, Bildunterschrift
  und die Messung, an der das Bild hängt.
- `foto_ansehen` — das Bild selbst, dazu ein Satz, der sagt, was darauf zu sehen
  sein soll: „Grow 4 · Motiv: Root · vor 3 Tagen aufgenommen · Notiz des
  Betreibers: ‚Wurzeln wirken braun' · gehört zur Messung 12".

Dieser Satz ist kein Beiwerk. Braune Wurzeln nach einem Wasserwechsel bedeuten
etwas anderes als braune Wurzeln in Woche sieben, und ein Modell, das nur die
Bildpunkte bekommt, kann diesen Unterschied nicht kennen. Was Grow OS über eine
Aufnahme nicht weiss, steht auch nicht in dem Satz — lieber eine kurze Notiz als
eine erfundene.

Bilder über 6 MB werden abgelehnt, statt sie zu laden und dann zu verwerfen.
Gelesen wird weiterhin ausschliesslich.

## 0.1.3

Sieben Werkzeuge mehr. Bisher war nur ein Bruchteil dessen angeschlossen, was
Grow OS ohnehin weiss — nicht aus Vorsicht, sondern weil es beim Bauen
untergegangen ist. Gelesen wird weiterhin ausschliesslich.

- `alarme` — offene Risiko-Ereignisse und die eingestellten Grenzwerte.
- `dosierungen` — die Pumpen am Zelt und das Protokoll der letzten Dosen.
- `dosier_vorschlag` — was Grow OS für eine Pumpe jetzt dosieren würde, samt
  Begründung und den Sperren, die dabei greifen. Es wird nur gerechnet.
- `ablauf_fortschritt` — welche Abläufe laufen und wie weit sie sind.
- `pflanzen` — die einzelnen Pflanzen, dazu der Pheno Hunt, falls einer läuft.
- `licht` — der eingestellte Zyklus und die tatsächlich beobachteten
  Schaltzeitpunkte. Zeigt, ob die Lampe tut, was der Plan sagt.
- `technik` — Geräte mit Zustand, anstehende Wartungen und Kalibrierungen.

Fehlt ein Teil, weil es ihn für diesen Grow gar nicht gibt — etwa kein Pheno
Hunt —, steht dieser Teil auf `null` und der Rest bleibt lesbar. Vorher hätte das
die ganze Antwort mitgerissen.

## 0.1.2

- Behoben — **`wissen_liste` war keine Übersicht.** Bei Abläufen und
  Behandlungen kam jeder Eintrag vollständig zurück, mit allen Schritten,
  Materiallisten und Quellen: elf Abläufe auf einen Schlag, rund 15.000 Tokens.
  Genau der Papierstapel, den dieses Add-on gegenüber der Mappe vermeiden soll.
  Jetzt kommen nur die Kopfdaten; den ganzen Eintrag holt `wissen_nachschlagen`.
  Symptome, Erreger und Sollwerte bleiben vollständig — sie sind kurz und haben
  keinen Einzelabruf.
- Umlaute kommen als Umlaute zurück statt als `ä`.

## 0.1.1

- Behoben — **Grow OS wurde nicht gefunden.** Der Server leitete den Namen von
  Grow OS aus seinem eigenen ab, suchte dabei aber nach dem Namen des
  Berater-Add-ons. Kam Grow OS aus dem Store, ging die Suche darum immer leer
  aus.
- Behoben — **die Seite bot den Verbindungsbefehl an, obwohl nichts erreichbar
  war.** Wer ihn ausführte, verband sich erfolgreich mit einem Server, bei dem
  danach jede Frage ins Leere lief. Jetzt steht dort erst eine Prüfliste.
- Die Meldung nennt jetzt auch, dass Grow OS mindestens **2.0.0-beta.24** sein
  muss — ältere Fassungen lassen andere Add-ons nicht mitlesen.

## 0.1.0

Erste Fassung.

- Elf lesende Werkzeuge auf Grow OS: Grows, Lagebericht, Messwert-Verlauf,
  Trends, Abweichungen, Anlage, Sorte, Journal, Fachwissen und Volltextsuche.
- Der Verlauf ist der eigentliche Grund für dieses Add-on. Die Berater-Mappe zum
  Herunterladen hält den Stand von jetzt fest; hier fragt das Modell selbst nach,
  wie sich ein Wert über Tage bewegt hat.
- Einrichtungsseite in der Seitenleiste mit fertigem Verbindungsbefehl. Der
  Schlüssel wird beim ersten Start erzeugt und muss nicht abgeschrieben werden.
- Zwei getrennte Türen: die Seite mit dem Schlüssel hängt am Ingress, die
  Schnittstelle am Netz-Port. Über das Netz ist die Seite nicht erreichbar.
- Kein Werkzeug schreibt. Dosieren und Schalten bleiben in Grow OS.
