# Cultivate Control · Was ich für die Screen-Entwürfe noch brauche

Hallo,

dein Handoff ist eingebaut — alle sechs Phasen. Die Patch-Schicht
`rc2-overrides.css` (2.745 Zeilen, 105 × `!important`) gibt es nicht mehr, die
Navigation steht auf vier Gruppen, Zelt und Grow werden einmal oben gewählt, und
das Theme lässt sich umschalten. Danke — die Analyse war präzise, besonders die
Regel gegen `minmax(0,1fr)` neben einer festen Spalte. Genau daran lagen die
verzogenen Boxen.

Eine Sache fehlt: **`Cultivate Control Redesign.dc.html` war nicht im ZIP.** Das ZIP enthält
`refactor/HANDOFF.md` und die 15 Code-Dateien, aber nicht den Entwurf, auf den die
Screen-Zuordnungstabelle am Ende des Handoffs verweist.

---

## 1. Was ich brauche

**Am liebsten: die Datei selbst.** `Cultivate Control Redesign.dc.html` mit den Ansichten,
die du in der Tabelle aufgeführt hast. Die Werte stehen bei dir inline im
Quelltext — daraus lese ich Abstände, Größen und Hierarchie direkt ab, ohne zu
raten.

**Falls die Datei nicht mehr existiert:** Screenshots pro Ansicht bei **1440 px
und 390 px** reichen auch, wenn dabei steht, welche Zahlen bewusst gesetzt sind
(Kartenbreiten, Abstände, Schriftgrößen). Ein Screenshot ohne diese Angaben
zwingt mich zum Nachmessen in Pixeln, und dann baue ich deine Absicht falsch nach.

**Die Ansichten, um die es geht** — nach Nutzen sortiert:

| # | Ansicht | Warum sie zuerst dran ist |
|---|---|---|
| 1 | **Live** (Score-Ring, Klima/Hydro-Sektionen, Kamera, Risiken, Timeline) | Die Startseite. Wird täglich mehrfach geöffnet. |
| 2 | **Messen** (Klima/Nährlösung getrennt, Live-Abweichungen, Foto) | Die häufigste Eingabe der App. |
| 3 | **Addback** (Messen → Ziel → Dosieren → Kontrolle, Protokoll) | Der Ablauf, an dem am meisten schiefgeht. |
| 4 | **Grow anlegen** (eine Seite, Timeline-Vorschau, Kollisionsprüfung) | Ist noch ein sechsschrittiger Wizard. |
| 5 | **Ernte erfassen** (pro Pflanze nass/trocken, Trocknung) | Schließt den Kreis; bisher ein Formular. |
| 6 | **Grow-Detail** (Tabs, Timeline, Diagnose, Messungen) | Nach dem Umbau nur noch Übersicht — siehe unten. |

---

## 2. Woran sich der Entwurf jetzt orientieren muss

Seit dem Handoff hat sich die Informationsarchitektur geändert. Wenn du gegen den
alten Stand entwirfst, entwirfst du Bildschirme, die es so nicht mehr gibt.

**Die Navigation hat 17 Ziele in vier Gruppen:**

- **Jetzt** — Live `/` · Messen `/messung` · Addback `/addback` · Aufgaben `/aufgaben`
- **Grow** — Grows `/grows` · Diagnose `/diagnose` · Journal & Fotos `/journal` · Sorten & Pheno `/sorten` · Ernte & Archiv `/archiv`
- **Anlage** — Zelte & Räume `/zelte` · Hydro-Systeme `/hydro` · Sensoren & Wartung `/sensoren` · Regeln & Automatik `/regeln` · Home Assistant `/home-assistant`
- **Wissen** — SOPs `/sops` · Erste Schritte `/start` · Bibliothek `/wissen`

**Vier Dinge, die dein Entwurf nicht mehr mitbringen muss:**

1. **Kein Seitenkopf pro Bildschirm.** Zelt- und Grow-Auswahl stehen einmal oben
   in der Kontextleiste, für die ganze App. Die einzelnen Seiten haben keinen
   eigenen Umschalter mehr.
2. **Grow-Detail ist nur noch Übersicht.** Messungen, Diagnose, Journal und SOPs
   sind eigene Top-Seiten mit Grow-Umschalter, keine Tabs mehr im Grow.
3. **Verwandtes liegt unter Tabs auf einer Seite.** `/regeln` trägt Automatik,
   Grenzwerte, Benachrichtigungen und KI-Assistent; `/sorten` trägt Sorten und
   Pheno-Hunt; `/archiv` trägt Archiv und Vergleich. Der aktive Tab steht in der
   Adresszeile (`?tab=grenzwerte`).
4. **Sensoren ist eine Tabelle**, keine vier Tabs — eine Zeile pro Gerät mit
   Zelt, Status, HA-Entity und nächster fälliger Pflege.

---

## 3. Was mir die Arbeit am meisten abnimmt

**Benutze die Tokens, die es schon gibt.** Sie stehen in
`src/styles/tokens.css` und sind beide Themes durchgerechnet:

- Flächen: `--bg` `--panel` `--panel-2` · Linien: `--hair` `--hair-2`
- Schrift: `--ink` `--muted` `--faint` · `--hint` ist **rein dekorativ**, nie Text
- Akzent: `--accent` füllt, `--accent-text` schreibt, `--accent-ink` ist Text auf
  der Akzentfläche · dasselbe Muster bei `--warn` und `--danger`
- Abstände `--s-1` bis `--s-6` · Radien `--r-sm` `--r` `--r-lg` `--r-pill`
- Höhen `--h-xs` 24 · `--h-sm` 32 · `--h-md` 38 · `--h-lg` 44

Eine neue Farbe ist völlig in Ordnung — sag mir dann bitte dazu, wie sie im
hellen Theme aussehen soll. Umgekehrt kostet mich ein Hex-Wert ohne Hell-Variante
eine Rückfrage.

**Benutze die Primitive, wo sie passen.** `.v1-page` `.v1-section` `.v1-card`
`.v1-stat` `.v1-field` `.v1-button` `.v1-badge` `.v1-list-row` `.v1-tabs`
`.v1-empty` `.v1-alert` `.v1-kpi-grid` `.v1-split`. Wenn eine Ansicht etwas
braucht, das es nicht gibt, ist das kein Problem — schreib nur dazu, ob es ein
neues Primitiv werden soll (dann taucht es überall auf) oder etwas, das nur auf
dieser Seite vorkommt.

**Schriften:** Archivo und JetBrains Mono sind gebündelt und liegen lokal. Die
App läuft offline im Heimnetz, ein Google-Fonts-Link geht also nicht. Wenn du
eine andere Schrift möchtest, brauche ich die Dateien dazu.

---

## 4. Vier Fragen, die ein Bild allein nicht beantwortet

Wenn du zu jeder Ansicht ein, zwei Sätze dazuschreibst, spare ich mir das Raten
und du bekommst, was du gemeint hast.

**a) 924 px.** Das ist der kritische Fall — Wandtablet und das
Home-Assistant-Ingress-Fenster liegen genau dort. Was passiert bei deinem Layout
zwischen Desktop und Telefon: bricht die Nebenspalte um, oder wird sie schmaler?

**b) Leer, lädt, kaputt.** Wie sieht die Ansicht aus, wenn noch keine Messung da
ist, wenn die Daten laden, wenn Home Assistant nicht antwortet? Das ist bei einer
frisch installierten App der erste Eindruck — und aktuell überall uneinheitlich
gelöst.

**c) Wenn es zu viel wird.** Ein Nutzer hat drei Kameras in einem Zelt, ein
anderer zwölf Sites im RDWC, ein dritter acht Sensoren. Was passiert mit deinem
Raster, wenn die Zahl über die schöne Anzahl hinausgeht?

**d) Was ist die eine Sache?** Sag mir zu jeder Ansicht, was das Wichtigste
darauf ist — der eine Wert oder die eine Handlung. Danach richte ich Hierarchie
und Abstände aus, wenn ich zwischen zwei Auslegungen deines Entwurfs wählen muss.

---

## 5. Zwei Punkte aus dem Umbau, die dich interessieren dürften

**Die 44-px-Untergrenze war unterlaufen.** Die Regel dafür stand in
`primitives.css`, aber einzelne Seiten hatten ihren Knöpfen eigene Höhen gegeben
(40 px bei den Zelt-Aktionen, 32 px bei Tabs und Selects). Die stehen außerhalb
von `@layer primitives`, und eine ungeschichtete Regel schlägt eine geschichtete
unabhängig von der Spezifität. Die Untergrenze steht jetzt als letzte Regel und
wird gegen 8 Routen getestet.

**Dein Hinweis auf `ascii-umlaut-visible` war goldrichtig, aber die Quelle lag
woanders.** In der Ernte-Seite stand „Blue⟨weiches Trennzeichen⟩tenstruktur" —
ein Content-Fehler, kein CSS. In der Navigation waren die Beschriftungen zusätzlich
als „Zelte & Raeume" geschrieben, damit die Suche ohne Umlaut-Tippen trifft. Das
saß am falschen Ende: die Beschriftung sieht man, den Vergleich nicht. Jetzt steht
„Räume" da, und die Suche faltet die Schreibweisen.

---

Wenn dir die `.dc.html` vorliegt, ist das der kürzeste Weg — dann brauche ich
keine der Fragen oben, weil die Antworten im Quelltext stehen.

Viele Grüße
