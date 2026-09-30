# Regeln für die Arbeit an Grow Operation System

## DIE ERGEBNIS-REGEL

**Prüfe nie die Änderung. Prüfe immer das Ergebnis am laufenden Stand.**

Diese Regel steht hier, weil an einem einzigen Tag (19.08.2026) vier Fehler
ausgeliefert wurden, die alle dieselbe Form hatten: geprüft wurde das, was ich
geschrieben hatte — nicht das, was der Nutzer sieht.

| geprüft | hätte geprüft gehört |
|---|---|
| der Menü-Link funktioniert | was auf der Zielseite steht |
| die CSS-Regel wirkt (per `addStyleTag` eingespielt) | ob die **gebaute** Datei gewinnt |
| der Test läuft durch | ob sein Treffer ein **Kommentar** war |
| die Ausnahmeliste steht im Code | ob die Kennungen darin **echt** sind |

Ergebnis: eine Seite mit einem zweiten, verkrüppelten Messformular kam ins
Menü, eine Regel lag wochenlang wirkungslos im Stylesheet, ein Test war grün
ohne zu prüfen, und drei Ausnahmen griffen nie.

### Die fünf Prüfungen, die vor jedem „fertig" laufen

1. **Ansehen, nicht nur messen.** Bei allem, was der Nutzer sieht: die Seite
   rendern und den Inhalt **lesen**. Zahlen erheben ist keine Prüfung —
   `−500 ppm` rendert tadellos, ein doppeltes Formular auch.

2. **Nur gegen den gebauten Stand.** `npm run build`, dann an der laufenden App
   messen. **Niemals** eine Regel per `addStyleTag`/`evaluate` einspielen und
   das als Beleg nehmen: so etwas hängt am Dokumentende und gewinnt immer. Es
   belegt, dass der Gedanke stimmt — nicht, dass er ankommt.

3. **Bezeichner nie aus dem Kopf.** Klassennamen, Enum-Werte, Datei-Kennungen,
   Routen: immer aus dem laufenden Baum oder der Datei holen. In diesem Projekt
   ist das über 30-mal schiefgegangen.

4. **Eine Erwähnung ist keine Verwendung.** Ein Kommentar, der einen Namen
   nennt, ist kein Import. Eine XML-Doku, die einen Enum-Wert nennt, ist kein
   Erzeuger. Ein Link irgendwo im Quelltext ist kein Weg im Menü. Jede Prüfung,
   die per Textsuche arbeitet, muss Kommentare und Doku ausschließen.

5. **Zeigen, dass die Prüfung beißt.** Eine Prüfung, von der niemand
   nachgewiesen hat, dass sie den Fehler findet, ist kein Beleg. Fehler
   wiedereinbauen, Prüfung laufen lassen, muss rot werden.

### Die Reparatur einmal WIEDERHOLEN

Am 24.08.2026 hat der Tester denselben Knopf zweimal gemeldet. Der erste Bericht
war „Bearbeiten reagiert nicht"; behoben, geprüft, ausgeliefert. Der zweite
Bericht war derselbe Knopf — beim **zweiten** Klick. Der Fix hing an einer
Zustandsflanke (`formOpen`), und die gibt es nur einmal.

Meine Prüfung klickte einmal. Sie war grün und hat nichts gesehen.

**Nach jeder Reparatur einer Bedienung: dieselbe Handlung ein zweites Mal
ausführen, und zwar unter erschwerten Umständen** — anderer Datensatz,
zwischendurch weggescrollt, Seite nicht neu geladen. Fast jede
Zustandsverwaltung hat einen Fall „schon offen", den der erste Durchgang nicht
berührt.

Dasselbe gilt für Formulare: speichern, dann **nochmal** speichern. Und für
Listen: den ersten Eintrag, dann den letzten.

### Der Testbestand ist eine Miniatur — das verdeckt Fehler

Der Nutzer hat sieben Geräte, der Testbestand hatte zwei. Zweimal an einem Tag
war ein Fehler dadurch unsichtbar: das Formular lag bei zwei Zeilen ohnehin im
Bild, und die neue Prüfung bestand **auch ohne den Fix**.

Wer eine Prüfung schreibt, die von der Menge abhängt, stellt die Menge her —
entweder im Bestand oder über ein kurzes Fenster (`setViewportSize`). Und wer
einen Fehler „behoben" meldet, hat vorher gezeigt, dass die Prüfung **ohne** den
Fix rot wird. Ohne diesen Nachweis ist nichts belegt.

### Erst belegen, dass der laufende Stand der gebaute ist

In der Nacht auf den 25.08.2026 habe ich eine halbe Stunde gegen eine App
gemessen, die noch aus einer früheren Sitzung lief. Der Port war belegt, mein
Start meldete trotzdem `Now listening on: http://0.0.0.0:5076`, und die alte
Instanz beantwortete jede Anfrage. Aufgefallen ist es nur, weil ein **neuer**
Endpunkt 404 gab — sonst wären alle Messungen still auf den falschen Stand
gegangen.

`GET /api/system/backend-health` liefert deshalb `bauKennung` — den
`TimeDateStamp` aus dem PE-Kopf der laufenden Assembly. **Kein Datum**: bei
deterministischem Build steht dort ein Hash, verglichen wird auf Gleichheit.

Vor jeder Messung an der laufenden App: die Kennung gegen die eben gebaute
`GrowDiary.Web.dll` halten. Stimmt sie nicht, misst man etwas anderes.

**Und: ein Bau schlägt fehl, solange die App läuft** — die `.exe` ist gesperrt.
Dann läuft der Test gar nicht, meldet aber auch nichts. Zweimal in einer Nacht
hätte ich so einen „Bissnachweis" gemeldet, der nie stattgefunden hat. Nach
jedem Testlauf gehört der Blick auf `Bestanden!` oder `Fehler!` — nicht auf das
Ausbleiben einer Meldung.

**Und der Bissnachweis endet nicht beim Zurücksetzen.** Am 02.09.2026 habe ich
die kaputte Fassung mit `cp` beiseitegelegt und danach mit `mv` zurückgeholt.
`mv` bringt die alte **Änderungszeit** mit — MSBuild hielt den Bau für aktuell,
baute nicht neu, und der Test blieb rot. Zehn Minuten Fehlersuche in einer
Reparatur, die längst stimmte; ohne die Diagnose hätte ich sie „zurückgenommen".

Also: nach jedem Zurücksetzen die Datei **anfassen** (`touch`) oder mit `cp`
zurückschreiben, und das Grün genauso belegen wie vorher das Rot.

### Der Testbestand ist Produktionscode

Er ist die Grundlage, gegen die **alles** geprüft wird: jede
Oberflächen-Messung, jeder E2E-Lauf, jeder Blick auf die laufende App. Ein
Fehler *im Bestand* verdeckt Fehler *in der App*. Belegte Fälle aus einer
einzigen Nacht:

| was der Bestand erzählte | was daran falsch war |
|---|---|
| „Licht an" bei PPFD 0 | Schalter rechnete in UTC, Kurven in Ortszeit |
| 18/6 in der Blüte | die App selbst nennt das „verhindert die Blüte" |
| Wasserwechsel „vor 73 Tagen" | keine Messung trug `SolutionChange` |
| „Umwälzpumpe zieht 0 W" | das Risiko hing an der pH-Sonde |
| ein Gerät | der Nutzer hat sieben |
| 0 von 17 Sensoren zugeordnet | der zugeordnete Weg lief nie |
| zwei pH-Sonden | dem einen Gerät fehlte die Messgröße |
| Schalten meldete Erfolg | und veränderte nichts |

Deshalb: `GrowDiary.Web.Tests/DemobestandStimmigTests.cs` prüft den Bestand
gegen die **eigenen Regeln der App** — wo Grow OS eine Warnung ausgeben würde,
ist der Bestand falsch, nicht die Warnung.

### Mein Rechner ist nicht die Anlage

Das Add-on läuft in einem **Linux-Container**. Dort sind die Schriften breiter
als auf diesem Windows-Rechner — ein paar Prozent, aber genug. Zwei Befunde am
25.08.2026 waren lokal grün und im Tor rot:

| Stelle | hier | im Tor |
|---|---|---|
| „Nährstoffprogramme." auf `/berater` bei 390 px | passt in 134 px | braucht 139 px |
| Überschrift „Verlauf" auf `/zelte/1` bei 360 px | 59 px in 58 px | 61 px in 58 px |

Beide sind **echt** — der Nutzer sieht die Linux-Fassung, nicht meine. Ein
lokaler Lauf ist also eine Vorprüfung, keine Freigabe: **was die Oberfläche
in Pixeln misst, gilt erst, wenn das Tor es bestätigt hat.**

Umgekehrt heisst das auch: eine Messung, die hier knapp durchgeht (ein, zwei
Pixel Luft), ist ein Befund und kein Erfolg.

### Prüfe die Schicht, in der der Fehler entsteht

Drei Prüfungen liefen jahrelang an ihrem eigenen Gegenstand vorbei:

- `DeutscheZahlenTests.cs` prüft Texte aus dem **Backend**. Eine Zahl, die im
  Browser aus einer JavaScript-Zahl entsteht, kommt dort nie vorbei — an den
  Diagramm-Achsen stand deshalb „5.80". Jetzt zusätzlich
  `e2e/deutsche-zahlen.spec.ts`.
- `handy-zuschnitt.spec.ts` misst, was über den **Seitenrand** ragt. Ein
  Zusammenstoß *innerhalb* eines Wischbereichs ist dort unsichtbar — im Archiv
  stand „21.05.202688 T". Jetzt zusätzlich `e2e/zellen-kollision.spec.ts`.
- `deutsche-woerter.node.test.ts` prüft, dass die **Übersetzungstabelle**
  vollständig ist — nicht, dass eine Seite sie benutzt. Das Formular „Grow
  starten" bot „Feminized / Seed / Planning" an. Jetzt zusätzlich
  `e2e/rohe-enums.spec.ts`.

Die Frage vor jeder neuen Prüfung: **in welcher Schicht entsteht der Fehler,
den ich fangen will — und misst meine Prüfung dort?**

### Zusätzlich bei bestimmten Änderungen

- **Etwas auffindbar machen** (Menü, Link, Suchbegriff): die Zielseite vorher
  **ganz** lesen. Erreichbar zu machen ist die Behauptung, dass es das wert ist.
  Führt die Seite dieselbe Hauptaktion wie eine andere, ist das ein Befund und
  kein Feature.
- **Layout**: nicht nur bei Scrollstand 0 messen. Klebende Elemente wandern erst
  beim Scrollen über ihre Nachbarn
  (`GrowDiary.React/zz-plausibel/ueberlappung-beim-scrollen.mjs`).
- **Text**: nicht den Element-Kasten messen, sondern den Textinhalt
  (`Range.getClientRects()`, siehe `zz-schrift/text-gegenprobe.mjs`). Ein
  `nowrap`-Element bleibt mit seinem Kasten brav in der Spalte, während die
  Buchstaben abgeschnitten werden.
- **CSS-Regel greift nicht?** Erst Spezifität und Ladereihenfolge prüfen. Bei
  gleicher Spezifität gewinnt die später geladene Datei (Reihenfolge in
  `src/App.tsx`). Zwei Klassen im Selektor gewinnen unabhängig davon.

### Prüfungen, die in diesem Projekt nichts prüfen

Belegte Fälle. Wer eine davon benutzt und „grün" meldet, hat nichts gemessen:

- **`tsc --noEmit` prüft NULL Dateien.** `tsconfig.json` hat `"files": []` und
  nur `references`. Richtig ist **`tsc -b`** (oder `npm run typecheck`). Am
  19.08.2026 mehrfach als „Typen ok" gemeldet, ohne eine Datei anzusehen.
- **Ein übersprungener Test ist kein bestandener.** Die E2E-Mappe fährt im Tor
  ohne Backend; von 34 Fällen aus vier Dateien sind dort 31 übersprungen. Genau
  die Prüfung, die das leere Archiv gefunden hätte, lief nie mit.
- **Eine Zählung ohne Mengenwächter** läuft bei leerer Grundmenge null Mal
  durch und ist grün. Jede braucht `Assert.True(menge.Count >= n)`.
- **Eine Textsuche darf die Datei nicht mitlesen, die sie prüft.**
  `routes-reachable` sucht Links im ganzen Quelltext einschließlich `App.tsx`,
  wo die Routen stehen — die Route belegt sich selbst.

### Drei Regeln, die aus belegten Fehlern folgen

**Ein übersprungener Test ist kein bestandener.** Von 34 Fällen aus vier
E2E-Dateien liefen im Tor drei — der Rest übersprang sich, weil kein Backend
lief. Genau die Prüfung, die das leere Archiv gefunden hätte, war darunter.
Gehalten wird das von `e2e/pflicht.ts` (`darfUeberspringen`) und
`e2e/keine-stillen-uebersprunge.spec.ts`; im Tor läuft `E2E_STRENG=1`.

**Eine Textsuche darf die Datei nicht mitlesen, die sie prüft.**
`routes-reachable` sucht Links im ganzen Quelltext einschließlich `App.tsx`, wo
die Routen stehen — eine erfundene Route belegt sich dadurch selbst. Dieselbe
Falle ist der neuen Übersprung-Zählung im ersten Lauf passiert: ihr eigener
Suchtext stand in ihr.

**Ein Formular gilt erst als geprüft, wenn jemand es ausgefüllt, abgeschickt
und den Wert nach dem Neuladen wiedergefunden hat.** Vorher gab es in der
ganzen E2E-Mappe zwei `fill()` und keinen einzigen Absende-Klick — und zwei
Fehler dieser Klasse hat der Tester gefunden: einen toten Speichern-Knopf und
einen stillen Datenverlust bei 21 Zahlenfeldern.
`e2e/formular-rundweg.spec.ts` zählt über alle `<form onSubmit>`; wer keinen
Rundweg hat, braucht einen ausgeschriebenen Grund.

### Der Demobestand

`GrowDiary.Web/Services/Demobestand.cs` legt beim Start einen vollständigen
Datensatz an — aber **nur in eine Datenbank ganz ohne Grows**. Wer eigene Daten
hat, behält sie.

```bash
GROW_OS_DEMO=1 dotnet run --project GrowDiary.Web
```

Gegen die laufende App prüfen:

```bash
GROW_OS_URL=http://localhost:5076 E2E_STRENG=1 npx playwright test
```

Ohne ihn prüft die halbe Oberflächen-Sammlung nichts. Zwei echte Fehler sind
sofort aufgefallen, als es ihn gab: eine Karte mit Kontrast 1,05 im hellen
Thema und vier Bedienelemente unter der Tippgröße — beide auf Seiten, die
vorher leer standen.

### Die Hooks nehmen mir zwei Prüfungen ab

Eingerichtet in `.claude/settings.json`, die Skripte in `.claude/hooks/`:

| Wann | Was | Blockt? |
|---|---|---|
| nach jeder `.cs`/`.ts`/`.tsx`-Änderung | `dotnet build` bzw. `tsc -b` | ja |
| vor `git commit` | das volle Tor (Backend, Typen, Lint, Vitest) | ja |

Beide sind nachgewiesen: Fehler eingebaut, Hook wurde rot. **Sie ersetzen die
fünf Prüfungen nicht** — sie fangen Tippfehler und rotes CI, nicht ein
doppeltes Formular oder eine Seite, die niemand liest.

### Vor „fertig": den Prüfer laufen lassen

`.claude/agents/pruefer.md` — ein Agent in eigenem Kontext, der die Änderung
gegen die fünf Regeln ansieht. Er hat sie nicht gebaut und keinen Grund, sie zu
mögen. Aufrufen **bevor** „fertig" gesagt wird.

Für ein Release: `/release` — die Reihenfolge steht dort, nicht im Kopf.

## ZÄHLUNGEN STATT LISTEN

Eine handgeschriebene Liste kann nur an dem scheitern, was schon draufsteht.
Deshalb prüft dieses Projekt über die **Grundmenge** (Reflexion, Enum,
Verzeichnis) und verlangt für jeden Fall entweder eine Behandlung oder eine
Ausnahme **mit ausgeschriebenem Grund**.

Jede Zählung braucht einen **Selbsttest**: sieht sie ihre Grundmenge überhaupt?
Ohne den läuft sie bei einer leeren Menge null Mal durch und ist grün.

Vorhandene Zählungen (Muster zum Abschauen):
- `GrowDiary.Web.Tests/MessfelderVollstaendigTests.cs` — jedes Zahlenfeld hat
  eine Sperre
- `GrowDiary.Web.Tests/AutoFelderVollstaendigTests.cs` — kein Auto-Band ist
  weiter als die Physik
- `GrowDiary.Web.Tests/RisikoTypenVollstaendigTests.cs` — kein Empfehler
  verzweigt auf einen Typ, den niemand erzeugt
- `GrowDiary.Web.Tests/SollwertKetteVollstaendigTests.cs` — niemand umgeht die
  Profil-Kette
- `GrowDiary.Web.Tests/WissenErreichbarkeitTests.cs` — jeder Ablauf wird von
  etwas vorgeschlagen
- `GrowDiary.React/src/menue-vollstaendig.node.test.ts` — jede eigenständige
  Seite steht im Menü
- `GrowDiary.React/src/deutsche-woerter.node.test.ts` — kein Enum-Wert steht
  roh auf dem Schirm

## EINE WAHRHEIT JE ZAHL

Steht dieselbe Zahl an zwei Stellen, laufen sie auseinander — das ist kein
Risiko, sondern eine Frage der Zeit. Belegte Fälle: das EC-Ziel (Diagnose
0,6–0,8 gegen Kachel 0,9–1,1 für denselben Grow), die physikalischen Grenzen
(drei Tabellen, sieben Widersprüche), die Sauerstoff-Schwelle (viermal 6,5).

Wo eine Zahl gebraucht wird, die es schon gibt: **verweisen, nicht abtippen.**

## FACHLICHE REGELN

- **Keine KI in der App.** Nie vorschlagen.
- **Faustregeln nur mit Etikett**, Empfehlungen nur mit Quelle. Eine Zahl, die
  niemand nachprüfen kann, ist schlechter als „zu wenig Daten".
- **Alles auf Deutsch** — Kommentare, Bezeichner, Oberflächentexte **und die
  Release Notes**. Der Changelog war bis beta.58 englisch; der Nutzer hat das
  am 28.08.2026 widerrufen, weil Home Assistant genau diesen Text beim Update
  zeigt. Gehalten von `src/release-notes-deutsch.node.test.ts`.
- **Beide Themen prüfen.** Das helle wird beim Bauen regelmäßig vergessen; die
  Falle ist dreimal zugeschnappt.
- **Das Telefon ist sauber** (320–768 px, null Überlauf über zwölf Breiten).
  Jede Änderung wird dagegen gemessen.
- **Athena-PDFs gehören nicht ins Repository.**

## RELEASE

Ein Release ist ein Tag: `git tag -a vX -m X && git push origin vX`. Dann
erzwingt `.github/workflows/release.yml` die Reihenfolge: CI (`ci.yml`) und
Image laufen gleichzeitig; **erst wenn beides grün ist**, wird das Manifest
anonym geprüft (HTTP 200, drei Architekturen), `config.yaml` hochgezählt und
nach `main` vorgespult. Der Docker-Build führt keine Tests aus — deshalb zählt
nie ein Mensch von Hand hoch. Ablauf: `/release`.
