# Changelog

> **Hinweis.** Ab 2.0.0-beta.58 stehen die Release Notes auf Deutsch — das ist
> die Sprache dieses Projekts, und wer ein Update einspielt, soll lesen können,
> was sich ändert. Die älteren Einträge darunter sind noch englisch; sie sind
> Geschichte und werden nicht nachübersetzt.

## 2.0.0-forkai.143

**Fork AI.** Grenzwerte: die Wert-Karten sind klarer.

- Geändert — Der Balken bleibt schmal, zeigt das Ziel aber kräftig grün, die Meldegrenzen als kleine
  gelbe Striche und die Zahlen direkt darunter. Der Messwert ist ein Zeiger in der Farbe der Lage:
  grün im Ziel, gelb außerhalb des Ziels, rot wenn gemeldet wird.
- Geändert — Statt „21 – 27 °C" und „Grenzwerte 21 – 27" gibt es zwei klare Zeilen: **Ziel** (mit
  FEST/PLAN und Herkunft darunter) und **Meldet** — mit den echten Grenzen als „unter …" / „über …",
  auch dort, wo bisher nur „Plan ±0,2" stand. Sind Ziel und Grenzen gleich, steht „außerhalb des Ziels".
- Geändert — „Luft" heißt jetzt **„Lufttemperatur"**.

## 2.0.0-forkai.142

**Fork AI.** Grenzwerte: die Übergabe aus dem Plan ist aufgeräumt.

- Geändert — Der Abschnitt heißt **„Übergabe aus dem Plan"** und ist nach Ziel gegliedert:
  *An Home Assistant* (Helfer der Regelungen), *An die Grenzwerte* (Meldungen im Fork) und
  *Ans Bluelab-Gerät*. Die bisherige Überschrift „Übergabe an Home Assistant" stimmte für die
  Grenzwert-Zeilen nicht.
- Geändert — Alle Werte mit Einheit; kurze Namen, Zusätze wie Tag/Nacht klein darunter. Der Zustand
  („folgt dem Plan") steht immer in eigener Zeile und bricht nicht mehr um.
- Geändert — CO₂-Ziel: „folgt dem Plan · gestaffelt" mit den drei Stufen je Canopy-Temperatur;
  die gerade geltende ist hervorgehoben.
- Geändert — Die Bluelab-Zeile unter der Seite ist in die Gruppe *Ans Bluelab-Gerät* gewandert
  (pH, EC, Wasser mit Zustand). Die Fußzeilen sagen, wann übergeben wird.

## 2.0.0-forkai.141

**Fork AI.** Die Grenzwerte gehen auch ans Bluelab-Gerät.

- Neu — Die Grenzwerte für pH, EC und Wassertemperatur werden automatisch an die Alarmgrenzen des
  **Bluelab Guardian** übertragen (über das Skript „Edenic Alarmgrenze setzen"). Gepflegt wird nur
  noch unter *Grenzwerte* — dem Plan folgend oder fest. Hat eine Grenze ein Nachtband, bekommt das
  Gerät die weitere Spanne aus Tag und Nacht, damit es nachts nicht falsch alarmiert.
- Neu — Abgleich alle fünf Minuten; geschrieben wird nur, was am Gerät abweicht, nacheinander und
  denselben Wert höchstens alle 30 Minuten.
- Neu — Unter *Grenzwerte* zeigt eine Zeile, ob die Gerätegrenzen übernommen sind.
- Neu — Rollen-Modul **„Bluelab · Gerätealarm"** unter *Geräte & Entitäten → Rollen* (Skript und die
  sechs Grenz-Entitäten, alles optional). Ohne Zuordnung passiert nichts.

## 2.0.0-forkai.140

**Fork AI.** Water Chiller pendelt um das Ziel.

- Geändert — Die Hysterese des Water Chillers wirkt jetzt nach beiden Seiten: ein ab Ziel plus
  Abstand, aus ab Ziel minus Abstand. Das Feld heißt **„Abstand zum Ziel ±"**. Gleiche Bandbreite,
  gleich viele Kompressorstarts — aber das Wasser liegt im Mittel auf dem Ziel statt darüber.
- Behoben — „Schaltpunkte" brach am Handy mitten im Wort um; der Wert steht jetzt darunter.

## 2.0.0-forkai.139

**Fork AI.** Chiller, Zuluft und Entfeuchter führen ab dem ersten Öffnen.

- Geändert — Beim ersten Aufruf übernimmt der Fork die Werte aus den Home-Assistant-Helfern
  sofort als eigenen Stand. Gespeichert wird nur im Fork, nach Home Assistant wird dabei
  nichts geschrieben — die Anlage läuft unverändert weiter.
- Entfernt — Der gelbe Hinweis „Werte aus Home Assistant übernommen" auf den drei Seiten.

## 2.0.0-forkai.138

**Fork AI.** Rollen ohne fremde Werksvorgaben, Steuerungs-Zeilen brechen am Handy nicht mehr um.

- Geändert — Rollen haben keine **Werksvorgabe** mehr. Die bisherigen Vorgaben waren die Geräte
  einer einzelnen Anlage; sie werden beim ersten Start einmalig als feste Zuordnung übernommen,
  aber nur, wenn es die Entität in Home Assistant gibt. Wer sie nicht hat, sieht die Rolle leer
  statt einer fremden Kennung. Die Option „— wie ab Werk —" und der Knopf „Auf Vorgabe zurück"
  entfallen; jede Zeile zeigt das Gerät, das wirklich dahintersteht.
- Behoben — Auf den Steuerungs-Seiten brach die rechte Spalte am Handy um („Rolle/n ›",
  „Rollen bearbeiten / ›", Schaltpunkte). Links und Werte bleiben jetzt auf einer Zeile.
- Geändert — Die Schaltpunkte des Water Chillers stehen als „ein 20,6 · aus 20,0 °C" da, immer mit
  einer Nachkommastelle.

## 2.0.0-forkai.137

**Fork AI.** Water Chiller: die Seite zeigt, wie der Kühler angesteuert wird.

- Neu — Im Reiter *Betrieb* steht die **Ansteuerung** (Steckdose, regelbarer Kühler oder beides),
  wie sie sich aus den Rollen ergibt, mit Sprung zu *Rollen*. Die Seite blendet aus, was für die
  Ansteuerung nicht gilt: bei einem Kühler mit eigenem Thermostat gibt es statt Schaltsperre,
  Schaltpunkten und Mindestzeiten die Kachel **„Soll im Gerät"** und das zugeordnete Gerät.
- Neu — Ist weder Steckdose noch Sollwert-Gerät zugeordnet, sagt die Seite das oben.
- Neu — Unter *Geräte & Entitäten → Rollen* lässt sich eine optionale Rolle mit **„— keins —"**
  ausdrücklich leeren. „Wie ab Werk" setzt die Vorgabe.
- Behoben — Die Geräte-Zählung zeigte „5 / 6 zugeordnet", obwohl nichts fehlte: Steckdose und
  Sollwert-Gerät sind Alternativen. Gezählt werden jetzt Pflichtrollen und belegte optionale Rollen.
- Entfernt — Die Warnung „Zwei Stellen schalten dieselbe Steckdose" — sie betraf nur Crop Steering.

## 2.0.0-forkai.136

**Fork AI.** Crop Steering ist stillgelegt — die Wassertemperatur hat nur noch eine Stelle.

- Entfernt — Die Seite **Crop Steering**, ihr Menü- und Sucheintrag, die Zeile in der
  Steuerung-Übersicht und die Karte „Nachtabsenkung" am Grow. Alte Adressen führen zu
  *Steuerung → Chiller*. Das Ziel kommt aus dem Plan, geregelt wird über die Chiller-Steuerung.
- Entfernt — Der Kühler-Regler und die Absenkrampe im Add-on laufen nicht mehr. Sie hätten
  neben der Chiller-Steuerung einen zweiten Weg zum selben Kühler geöffnet.
- Neu — Wer in Crop Steering eine Steckdose oder ein Zielgerät (climate/number) eingetragen hatte,
  findet sie einmalig als Rolle der Chiller-Steuerung wieder.
- Behoben — Der Kühler-Wächter hielt einen ausgeschalteten Kühler nur dann für Absicht, wenn
  Crop Steering ihn abgeschaltet hatte. Jetzt zählt auch die Chiller-Steuerung: meldet sie
  keinen Kühlbedarf, ist „aus" kein Ausfall.

## 2.0.0-forkai.135

**Fork AI.** Water Chiller: auch Kühler mit eigenem Thermostat, Hysterese wirklich einstellbar.

- Neu — Rolle **„Kühler · Sollwert"** unter *Geräte & Entitäten → Rollen → Water Chiller* für Kühler
  mit eigenem Thermostat (climate- oder number-Gerät, z. B. per WLAN). Home Assistant schreibt dann
  das Tag- oder Nachtziel aus dem Plan direkt ins Gerät; die Vorlage „Water Chiller Sollwert" liegt
  bei. Wie der Kühler angesteuert wird, ergibt sich aus den Rollen: nur Steckdose, nur Sollwert-Gerät
  oder beides — dann ist die Steckdose nur noch Not-Aus für den Wächter.
- Geändert — „Kühler · schalten" ist keine Pflicht-Rolle mehr. Eine optionale Rolle lässt sich jetzt
  bewusst leeren; bisher fiel sie still auf die Werksvorgabe zurück.
- Geändert — Der Wächter schaltet bei stummem Wasserfühler ab, was zugeordnet ist: Steckdose,
  Kühler oder beides.
- Behoben — Das Feld „Totband" im Reiter Schutz wirkte nicht: Home Assistant schaltete mit einem
  fest eingebauten Abstand, und die angezeigten Schaltpunkte stimmten nicht. Es heißt jetzt
  **„Einschalten ab Ziel +"** und schreibt in den neuen Helfer `input_number.chiller_hysterese`;
  ausgeschaltet wird beim Ziel. Ein älterer gespeicherter Wert wird nicht übernommen — es gilt, was
  in Home Assistant steht.

## 2.0.0-forkai.134

**Fork AI.** „Geräte & Entitäten" gibt es nur noch einmal.

- Geändert — Die Rollen der Steuerungen (welcher Fühler, welche Steckdose zu welcher Regelung gehört)
  stehen jetzt als Reiter **Rollen** auf der Seite *Geräte & Entitäten* — neben Geräte, Messgrößen
  und Wartung. Bisher lagen sie auf einer zweiten Seite gleichen Namens unter *Steuerung*. Die
  Zuordnungen selbst bleiben, wie sie sind.
- Geändert — In jeder Steuerung heißt der Weg dorthin **„Rollen bearbeiten ›"** und öffnet gleich die
  richtige Regelung. Die alte Adresse `/steuerung/geraete` leitet auf den neuen Reiter.
- Neu — Unter einem Gerät führen die Marken einer Rolle („Steuerung CHILLER · Kühler · schalten ›")
  und „Öffnen" im Block „Wo dieses Gerät vorkommt" direkt zur passenden Regelung.
- Behoben — „Geräte & Entitäten ›" aus Water Chiller, Licht, Zuluft oder Entfeuchter öffnete immer
  die CO₂-Begasung.
- Behoben — In der Zählerleiste der Geräteseite brach „korrigiert" am Handy mitten im Wort um; im
  Reiter Rollen klebte das nächste Feld am Hinweis des vorigen.

## 2.0.0-forkai.133

**Fork AI.** „Ziele & Meldungen" ist in drei Menüpunkte zerlegt: **Plan**, **Grenzwerte**, **Handy**.

- Neu — **Plan** (Menü *Pflanzen*, nach Grows): die Ziele deines Grows Woche für Woche — die einzige
  Stelle, an der Zielwerte geändert werden. Unter der Woche: „Ab wann wird diese Woche gemeldet? ·
  Grenzwerte ›".
- Neu — **Grenzwerte** (Menü *Betrieb*, statt „Ziele & Meldungen"): ab wann je Messgröße gemeldet wird,
  tags und nachts. Oben der Push-Stand („Push für Grenzwerte ist an · Gerät · Handy ›"), unten
  „Ziele dieser Woche ändern · Plan ›". Im Blatt je Messgröße steht der Planwert nur noch zum Lesen mit
  „im Plan ändern ›" (springt zur Woche); „Grenzwerte" statt „Alarm", **„Überwachen"** statt
  „Alarm scharf", „höchstens alle … Minuten erinnern", „Ob das aufs Handy kommt: Handy ›".
  Karten: „● außerhalb" / „● überwacht".
- Neu — **Handy** (Menü *Einrichtung*, statt „Aufs Handy holen"): Reiter **Push** (was aufs Handy kommt)
  und **App einrichten** (QR-Code). Bei „Grenzwerte gerade" ein Sprung zurück zu den Grenzwerten.
- Neu — Oben auf allen drei Seiten die Kette **„1 · Plan › 2 · Grenzwerte › 3 · Handy"**.
- Geändert — Alte Adressen und Lesezeichen leiten weiter (`/zielwerte` je nach Reiter, `/wochenplan`,
  `/sollwerte`, `/alarme`, `/benachrichtigungen`). Übergabe-Namen „Grenzwert Luft unten …" statt
  „Alarmgrenze …".
- Behoben — Auswertungs-Chips auf der Grow-Seite (EC, pH, Luft …) hatten nur 32 px Tippfläche, jetzt 44 px.
- Behoben — Handy › Push: „Alles wach" / „gerade eben" im hellen Schema zu blass (Kontrast 3,6), jetzt Text-Farben.
- Testdaten-App: der laufende Demo-Grow bekommt ein Düngeprogramm und sofort seinen Plan (sonst war die Seite „Plan" leer).

## 2.0.0-forkai.132

**Fork AI.** Einheitliche Wochennamen.

- Geändert — Im Plan des Grows und überall, wo die laufende Woche steht (Live, Ziele &
  Meldungen, Plan, Entfeuchter, Mischplan, Meldungen), heißen die Wochen jetzt
  **„Bewurzelung", „Vegiwoche 1–4", „Blütewoche 1–8", „Flush"** — egal, wie das Düngeprogramm
  sie nennt (SKX: „Vega/Flores · Woche N", Athena: „Veg/Blüte · Woche N").
- Unverändert — Die Herstellertabelle unter Wissen behält die Begriffe des Düngers.
  Schritte ohne Wochennummer (Athena „Klon · Vorweichen/Anfüttern") bleiben unterscheidbar.
  Frühere Einträge im Änderungsbuch behalten ihren Text.

### Technik
- `GrowPlanBauer.Wochenname()` beim Anlegen eines Plans (auch Programmwechsel);
  `WochennamenAngleichen()` im Nachtrag laufender Grows beim Start.

## 2.0.0-forkai.131

**Fork AI.** Klarere Beschriftung der Nachtwerte je Woche (Reiter Plan).

- Geändert — Statt „Nacht wie Standard | eigene Nachtwerte" sagen die Knöpfe jetzt, was nachts
  gilt: **„Nachts eigene Werte | Nachts wie tags"**. Darunter klein **„wie im ganzen Plan"**
  oder **„nur diese Woche · zurück zum Plan"**.
- Geändert — Schalter-Hinweis: „Für den ganzen Plan (Luft und Luftfeuchte) · einzelne Wochen
  können abweichen."
- Geändert — Herkunft von „Luft Nacht" heißt „aus Tag − 4 K" (Plan) bzw. „vorbefüllt: Tag − 4 K"
  (Werte-Blatt) statt „aus dem Standard".

## 2.0.0-forkai.130

**Fork AI.** Tag- und Nachtgrenzen selbst einstellen, „Nachts gelten die Tageswerte".

- Neu — **Ziele & Meldungen › Luft (und Luftfeuchte):** Alarm mit eigenen Zeilen **Tag** und
  **Nacht** („melden unter / über"); die gerade gültige ist markiert. Je Zeile „✓ folgt dem
  Plan" oder „● eigener Wert" mit **„Zurück zum Plan"**, dazu kurz „Planwert 24 °C ± 3 K" bzw.
  „Plan wäre 17–23". Eigene Werte bleiben jederzeit möglich.
- Neu — Feld **„Erlaubte Abweichung"**: so weit darf die Luft vom Planwert abweichen, bevor
  gemeldet wird (vorher fest ± 3 K im Code).
- Neu — Reiter **Plan:** **„Luft Nacht"** als eigener Planwert je Woche (einmalig mit Tag − 4 K
  vorbefüllt, danach frei) und optional **„RH max Nacht"** (leer = wie tags). Schalter
  **„Nachts gelten die Tageswerte"** als Standard für alle Wochen, je Woche abweichend
  einstellbar; abweichende Wochen haben im Punktband einen blauen Ring.
- Neu — Rückfrage vor dem Speichern, wenn ein Band kaum Abweichung zulässt (z. B. 20–21 °C).
- Behoben — **„Freigeben" bzw. „Zurück zum Plan" wirkt sofort**, nicht erst beim Plan-Lauf um
  06:00.
- Geändert — Im Werte-Blatt heißt die Woche „Blütewoche 5" statt „Flores · Woche 5".

### Technik
- `FeedChartColumn.RhMaxNight`, Planfelder `airTempNightC`/`rhMaxNight` (`Feld.Optional`),
  `GrowPlanInhalt.NachtWieTag`/`NachtWieTagJeWoche`/`NachtWerte()`, `POST /api/grows/{id}/plan/nacht`.
- Wochenplan-Sync: Tag/Nacht aus dem Grow-Plan ± Toleranz der festen Zelt-Regel; neue Rolle
  `feuchte-nacht-oben` (ohne Nachtfeuchte = Tageswert, nie lockerer).
- Feste Zelt-Regeln speichern `toleranz` jetzt mit (Erlaubte Abweichung).
- Bestehende Grows ändern sich nicht: Standard „nachts wie tags" ist aus, Nacht-Luft wird mit
  Tag − 4 K vorbefüllt, Abweichung 3 K.

## 2.0.0-forkai.129

**Fork AI.** Neue Detailseite **Steuerung › Entfeuchter**.

- Neu — Oben ein **Schwellen-Band**: Feuchte jetzt, AUS, EIN und die Plan-Feuchte auf
  einer Skala. Es zeigt nur den Ausschnitt, auf den es ankommt, damit dicht
  beieinanderliegende Werte getrennt bleiben.
- Neu — Reiter **Regel**: VPD-Band, Luftfeuchte max. und Blatt-Offset kommen aus dem Plan
  bzw. vom Zelt und stehen hier nur zum Lesen („Ziele & Meldungen ›"). Einstellbar sind
  „Nach VPD regeln" und **„Wie ruhig soll er schalten?"** (knapp 2 % · normal 4 % · ruhig
  6 % · eigener Wert) mit der Folge in Klartext. Die festen Tag/Nacht-Schwellen bleiben
  als eingeklappte Rückfallebene.
- Neu — Reiter **Schutz**: **Temperatur max.** je Tag und Nacht wahlweise **„Plan +"
  Abstand** (wandert mit der Planwoche, z. B. 24 + 5 = 29 °C) oder **„Fest"**; Hinweis,
  wenn der Wert über der CO₂-Grenze liegt. Dazu Mindestlaufzeit und Tagbetrieb.
- Neu — Reiter **Betrieb**: Automatik, **Einschaltverzögerung** und **„Außenluft
  zuerst"** als einstellbare Wartezeiten (vorher fest 10/25 min in der Automation) —
  mit Anzeige, welche gerade gilt.
- Neu — Der Plan übergibt das **VPD-Band der Woche** und den **Blatt-Offset** des Zelts an
  den Entfeuchter in Home Assistant. Ab Blüte W7 zieht das Band also von selbst auf
  1,4–1,6 nach. Handverstellungen werden wie gewohnt als „von dir gesetzt" erkannt.

### Technik
- Modul `entfeuchter`: `EntfeuchterEinstellungen`, `EntfeuchterSteuerungService`,
  `GET/PUT /api/steuerung/entfeuchter`, 7 Rollen, 13 Bauteile. Temperatur max. „Plan +"
  zieht der Wochenplan-Worker nach (Wochenwechsel, täglich 06:00), ohne Plan gilt der
  feste Wert.
- Wochenplan-Sync: neue Rollen `vpd-unten`, `vpd-oben`, `blatt-offset`;
  `WochenplanSyncService.PlanLuft()`.
- Solange nichts gespeichert ist, zeigt die Seite die Werte aus Home Assistant.
- Noch ohne Vorlagen, mit denen ein anderes System die Entfeuchter-Regelung neu anlegen
  kann (Rechenwerte, Automation) — folgt.

## 2.0.0-forkai.128

**Fork AI.** Zuluft: Pause, wenn das Zelt zu kalt wird.

- Neu — Reiter **Regel** hat das Feld **„Zelttemperatur min.“** (Helfer
  `input_number.zuluft_zelttemperatur_min`). Fällt das Zelt darunter, pausiert
  die Zuluft; sie läuft wieder ab diesem Wert + 1 °C. Oben erscheint dann
  „Pausiert — Zelt zu kalt“, die Übersicht zeigt „pausiert · Zelt zu kalt“, und
  der Rechenweg hat ein Glied „Zelt“.
- Geändert — Die Außentemperatur ist nur noch **Frostschutz** (Vorgabe 0 statt
  5 °C). Vor dem Auskühlen schützt die Zelttemperatur; die Außentemperatur
  sperrte die Zuluft gerade in kalten Nächten, in denen die Außenluft am
  trockensten ist.
- Behoben — Ein gespeicherter Stand von vor dieser Version gilt wie „nie
  gespeichert“: die Seite zeigt wieder die Werte aus Home Assistant. Sonst hätte
  das nächste Speichern Werte zurückgeschrieben, die inzwischen in Home Assistant
  geändert wurden.

### Technik
- Neue Geräte-Rolle `zelt_temp` („Zeltfühler · Temperatur“, Vorgabe
  `sensor.big_probe_sensor_sonden_temperatur`), neues Bauteil
  `input_number.zuluft_zelttemperatur_min`; die Bedarf-Vorlage prüft die
  Zelttemperatur (fehlt der Wert kurz, zählt er nicht). Vorhandene Helfer werden
  wie immer nicht überschrieben.
- `ZuluftEinstellungen.ZeltTemperaturMinC` (null = Stand vor forkai.128),
  `ZuluftLive.ZeltTempC` / `PauseZeltKalt`; das Livebild nimmt die geglättete
  Differenz, wenn es sie gibt.

## 2.0.0-forkai.127

**Fork AI.** Messseite: Zugaben-Block einspaltig.

- Behoben — Ohne Zugabe stand der Hinweis „Nichts zugegeben“ am Handy in einer
  halben Spalte und wurde zum schmalen, langen Turm. Der Block ist jetzt immer
  einspaltig, der Hinweistext kürzer.

## 2.0.0-forkai.126

**Fork AI.** Messseite: „Zugaben“ statt „Gaben“, und die Zeile passt aufs Handy.

- Behoben — Am Handy saß eine Zugabe-Zeile in nur einer Spalte des Rasters:
  das Produkt-Feld schrumpfte auf ein leeres Kästchen, „Artikel“ brach um und
  „Entfernen“ lief Buchstabe für Buchstabe untereinander. Jetzt nimmt die Zeile
  die volle Breite — am Handy das Produkt oben, darunter Menge und ✕.
- Geändert — Der Abschnitt heißt **„Zugaben“**, der Knopf **„+ Zugabe“**, das
  Feld **„Produkt“**. Die Einheit steht im Feldnamen („Menge (ml)“) statt unter
  dem Feld, entfernt wird über **✕**.
- Behoben — Die Kamera-Auswahl beim Snapshot zeigt den Namen aus Home Assistant
  statt der Entitäts-Id („Rdwc overview standardauflosung“).

### Technik

- `pages/ManualMeasurementPage.tsx`: Zeile als `.ms-zugabe-zeile`; Kameranamen
  aus `/api/home-assistant/entities`. Stile in `features/measurement/measurement.css`
  (nur Tokens). Buchung und `data-audit`-Namen unverändert.

## 2.0.0-forkai.125

**Fork AI.** Der Wochenplan ist in „Ziele & Meldungen“ aufgegangen.

- Entfernt — Der Menüpunkt **„Wochenplan“**. Alte Links und Lesezeichen auf
  `/wochenplan` landen im Reiter „Plan“.
- Neu — Über den Reitern von „Ziele & Meldungen“ steht die **laufende Woche**
  mit Sorte und den Ankern (Vegi-Start, Flip, Erntefenster). Ein Tipp darauf
  öffnet das **Wochen-Blatt** mit allen Wochen; eine Woche darin öffnet den
  Reiter „Plan“ genau dort.
- Geändert — **„von dir gesetzt — freigeben“** steht jetzt in der Übergabe an
  Home Assistant im Reiter „Werte“, wo die Übergabe ohnehin schon stand.
- Geändert — Wo in der Oberfläche „Wochenplan“ stand, steht jetzt „Plan“ —
  auch der Verweis auf der CO₂-Steuerung, der sonst ins Leere gezeigt hätte.

### Technik

- Neu: `features/zielwerte/WochenZeile.tsx`, `wochen-zeile.ts(+.test.ts)`;
  `TabbedCollectionPage` bekommt einen optionalen Kopf über den Reitern;
  `PlanReiter` übernimmt `?woche=` und nimmt den Wunsch danach aus der Adresse.
- Entfernt: `pages/WochenplanPage.tsx` und die damit ungenutzten Stile.

## 2.0.0-forkai.124

**Fork AI.** Abschluss des Umbaus „Ziele & Meldungen“.

- Behoben — **Grow-Seiten ohne Plan meldeten einen Konsolenfehler.** Die
  Auswertung antwortet dort jetzt „kein Plan gespeichert“, ohne Fehler.
- Behoben — **Bewurzelter Steckling:** Beginnt die Vegi am Starttag, hat die
  Bewurzelung in der Auswertung keinen Zeitraum mehr (F-019).
- Geändert — Der **„Grow UV-C – Verbindungs-Wächter“** steht nicht mehr unter
  „Meldungen“: er lädt nur die Eheim-Integration neu und schickt nichts.
- Entfernt — Der alte Wochenwert-Editor im Wochenplan (seit 121 ungenutzt).
- Technik — Die strengen E2E-Tests sind wieder grün (F-018): der Fall
  „Profil bearbeiten“ entfällt, die Sticky-Prüfung nutzt „Ziele & Meldungen“.

## 2.0.0-forkai.123

**Fork AI.** Nachbesserung zur Auswertung aus 122.

- Behoben — **Wochen der Auswertung lagen falsch** (F-019). Ohne eingetragenen
  Vegi-Beginn lagen Bewurzelung und Vegi-Woche 1 auf demselben Zeitraum; jetzt
  folgt die Vegi der Bewurzelungswoche. Eine verlängerte Vegi zählt bis zum Flip
  zur letzten Vegi-Woche — so wie Mischplan und Wochenplan diese Woche halten.
  Vorher fielen die Messungen dazwischen aus der Auswertung.
- Technik — E2E-Rauchtests auf die neuen Adressen von „Ziele & Meldungen“
  nachgezogen (F-018, CI war seit 121 rot).

## 2.0.0-forkai.122

**Fork AI.** Umbau „Ziele & Meldungen“, Schritt 6: Abschluss und Auswertung.

- Neu — **Der Plan wird mit dem Grow eingefroren.** Ernte eintragen,
  „Archivieren“ oder Status auf Geerntet/Abgebrochen — der Plan wird als
  Endstand gespeichert und lässt sich danach nicht mehr ändern. Wird der Grow
  wieder geöffnet, ist auch der Plan wieder bearbeitbar. Beides steht im
  Änderungsbuch; beim Start gleicht der Fork alle Pläne mit dem Status ab.
- Neu — **„Plan · Auswertung“ auf der Grow-Seite.** Je Messgröße (EC, pH,
  Wasser, Feuchte, Luft, ORP) und für die Dosierung: Start, Ende bzw. aktueller
  Stand und der gemessene Mittelwert je Woche; geänderte Wochen und Werte
  außerhalb des Ziels sind markiert. Darunter die Zeitleiste aller Änderungen,
  Programmwechsel und des Einfrierens. Sondenaussetzer zählen nicht mit.
  Grows, die vor den Plänen abgeschlossen wurden, sagen das.
- Neu — **Endstand als Programm speichern** — beim Eintragen der Ernte als
  Schalter mit Namen oder später auf der Grow-Seite. Das Programm steht beim
  nächsten Grow unter „Eigene Programme“.

### Technik

- `GET /api/grows/{id}/plan/auswertung`, `POST /api/grows/{id}/plan/als-programm`;
  `GrowPlanService.Abgleichen/AlleAbgleichen/AlsProgrammSpeichern`,
  `PlanAuswertung` (Zeiträume je Woche, Plausibilitätsfilter aus
  `MeasurementSanityService`).
- Neu: `features/zielwerte/PlanAuswertung.tsx`, `plan-auswertung.ts(+.test.ts)`.

## 2.0.0-forkai.121

**Fork AI.** Umbau „Ziele & Meldungen“, Schritt 5: aufgeräumt und
Programmwechsel.

- Geändert — **Menü:** „Zielwerte“ heißt jetzt **„Ziele & Meldungen“** (Reiter
  Werte · Plan · Meldungen), „Regeln & Automatik“ heißt **„Auto-Messungen“**.
  Die Reiter „Profile“ und „Grenzwerte“ sind weg — Grenzen stellt man an der
  Werte-Karte ein, Ziele im Plan. Alte Adressen leiten weiter.
- Neu — **Programm am laufenden Grow wechseln.** Hat der Plan eigene
  Änderungen, fragt das Formular: „Änderungen übernehmen“ (in die gleichnamigen
  Wochen des neuen Programms) oder „Änderungen verwerfen“. Der Startstand bleibt,
  der Wechsel steht im Änderungsbuch.
- Neu — **Programmkarten zeigen, was sie mitbringen** (alle Werte je Woche /
  nur EC und pH / keine Wochenwerte). Eigene Programme stehen in einer eigenen
  Gruppe.
- Entfernt — Das Feld „Sollwertprofil“ im Grow-Formular und im Hydro-Editor
  sowie der Schalter „Wochen-Ziele verwenden“ am Addback. Grows mit eigenem Plan
  nutzen ihre Wochenziele immer.
- Geändert — Wochenplan: „Werte bearbeiten“ führt jetzt in den Plan des Grows.

### Technik

- `POST /api/grows/{id}/plan/programm`, Planstand „basis“ als Vergleichswert
  nach einem Wechsel, `eigeneAenderungen` im Plan-Stand.
- Wissens-API: `klimaJeWoche` je Feed-Chart-Spalte.
- Neu: `features/grows/programm-deckung.ts(+.test.ts)`.

## 2.0.0-forkai.120

**Fork AI.** Umbau „Ziele & Meldungen“, Schritt 4: der Reiter „Meldungen“.

- Neu — **Reiter „Meldungen“ unter Zielwerte.** Oben, wie viele Grenzwerte
  scharf sind und welche gerade melden; darunter Handy, Ruhezeit, Täglicher
  Überblick und alle Arten von Meldungen; unten die **Wächter aus Home
  Assistant** (z. B. Water Chiller Wächter, CO₂ Wächter) mit Zustand und Zweck —
  nur zur Ansicht, weil sie gerade dann melden sollen, wenn Grow OS steht.
- Behoben — **„Trends & Risiken“ ließ sich nicht abschalten** (F-014). Trend-
  und Licht-Wächter sendeten immer, ohne dass die Seite sie nannte. Jetzt gibt es
  einen Schalter, und der Hinweis sagt, dass Licht in der Dunkelphase auch in der
  Ruhezeit kommt.
- Geändert — Die Systemüberwachung nennt jetzt ausdrücklich den Pumpen-Wächter,
  der an ihr hängt. Der Link „Grenzwerte einstellen“ führt zu den Werte-Karten.

### Technik

- `GET /api/meldungen/ha-waechter`: HA-Automationen mit „Wächter“ im Namen,
  bekannte mit Zweck (`MeldungenApiController`).
- Neu: `features/meldungen/MeldungenReiter.tsx`, `HaWaechterTests`.
- Die Kategorie „Wartung“ hat weiterhin keinen Absender und daher keinen
  Schalter.

## 2.0.0-forkai.119

**Fork AI.** Umbau „Ziele & Meldungen“, Schritt 3: der Reiter „Plan“ und
eigene Programme.

- Neu — **Reiter „Plan“ unter Zielwerte.** Alle Wochen des Plans deines Grows,
  immer eine im Bild (‹ ›, Wischen, Punktreihe). Zielwerte mit Startwert
  darunter, dazu die **Dosierung**: Mengen ändern, Zutaten entfernen und
  wiederherstellen, neue Zutaten hinzufügen, Mengen auf das Anlagenvolumen
  gerechnet.
- Neu — **Speichern mit Wahl.** Das Blatt listet jede Änderung und fragt:
  „Nur für diesen Grow“ oder „Auch ins Programm“. Mitgelieferte Programme bleiben
  unverändert — beim ersten Mal entsteht ein eigenes Programm (Name wählbar), das
  künftige Grows auswählen können; danach landen Übernahmen immer dort. Ein
  freiwilliger Grund wird mitgeschrieben.
- Neu — **Änderungsbuch** unten im Reiter: jede geänderte Zahl und Zutat mit
  Zeitpunkt, Woche, alt → neu, Grund und „auch im Programm“.
- Neu — **EC-Band im Plan.** EC von/bis stehen jetzt je Woche im Plan; das
  Sollwertprofil wird dafür nicht mehr gebraucht. Beim ersten Start trägt der
  Fork das Band in bestehende Pläne nach (Ziel ± halbe Breite des mitgelieferten
  Standards). Wer das EC-Ziel ändert, verschiebt das Band mit. ORP und EC-Band
  sind auch unter Wochenplan → „Werte bearbeiten“ und im Werte-Blatt editierbar.

### Technik

- `POST /api/grows/{id}/plan` (Werte, ganze Dosierung, `auchInsProgramm`,
  `programmName`, `grund`) mit Prüfung und Übergabe an HA.
- Eigene Programme als Datei `/data/knowledge/nutrient-programs/eigen-*.json`
  (`EigeneProgramme`); in jeder Fork-Sicherung enthalten.
- `FeedChartColumn.EcMin/EcMax`; `GrowPlanService.Speichern`,
  `FehlendeFelderNachtragen`; Plan-Stand liefert `eigenesProgrammId/-Name`.
- Neu: `features/zielwerte/PlanReiter.tsx`, `plan-reiter.ts(+.test.ts)`,
  `GrowPlanSpeichernTests`.

## 2.0.0-forkai.118

**Fork AI.** Nachbesserung zu 117.

- Behoben — **Die Glocke auf den Werte-Karten meldete nachts zu viel** (F-017).
  Sie rechnete immer mit den Taggrenzen. Jetzt gilt dieselbe Lichtlogik wie bei
  den echten Alarmen: bei Licht aus schweigen VPD, CO₂ und PPFD, und feste
  Grenzen nehmen ihr Nachtband.

## 2.0.0-forkai.117

**Fork AI.** Umbau „Ziele & Meldungen“, Schritt 2: Die Werte-Karten lassen sich
bearbeiten.

- Neu — **Karte antippen, alles zu diesem Wert in einem Blatt.** Unter
  Betrieb → Zielwerte (Reiter „Werte“, bisher „Jetzt gültig“) öffnet jede Karte
  ein Blatt mit dem Ziel der laufenden Woche, der Alarmgrenze (feste Zahlen
  oder „folgt dem Plan“ mit Toleranz), der Pause zwischen zwei Meldungen, dem
  Schalter „Alarm scharf“, den Werten, die nach Home Assistant gehen, und der
  Herkunft. Ein Speichern für alles. Das Wochenziel landet im Plan des Grows
  und im Änderungsbuch.
- Neu — **Die Glocke auf der Karte** zeigt, ob der Alarm scharf ist oder der
  Wert gerade außerhalb liegt. Oben steht, welche Werte gerade melden.
- Behoben — **Die Herkunft nannte bei „folgt dem Plan“ immer die
  Standard-Toleranz** statt der eingestellten (F-015).
- Behoben — **Speichern unter Grenzwerte löschte das Nachtband der
  Lufttemperatur** (F-016). Die Seite kannte die Nachtgrenzen nicht und schickte
  sie nicht mit; der Server ersetzt aber immer den ganzen Satz.
- Geändert — ORP gilt jetzt auch als Wert, den der Wochenplan nennt.

### Technik

- `/api/zielwerte` liefert zusätzlich `zeltId`, `spalteId`, `eigenerPlan` und je
  Wert `regel`, `alarmVon`/`alarmBis`, `meldet`, `planFelder`.
- Neu: `features/zielwerte/WertBlatt.tsx`, `wert-blatt.ts(+.test.ts)`,
  `GrowDiary.Web.Tests/Api/ZielwerteBlattTests.cs`.

## 2.0.0-forkai.116

**Fork AI.** Jeder Grow hat jetzt seinen eigenen Plan. Erster Schritt des Umbaus
„Ziele & Meldungen“.

- Neu — **Der Plan gehört dem Grow.** Beim Anlegen bekommt ein Grow eine eigene
  Kopie seines Düngeprogramms, samt Startstand, der sich nie mehr ändert.
  Alarme, Live-Kacheln, CO₂, Mischplan und die Übergabe an Home Assistant lesen
  ab jetzt diesen Plan und nicht mehr das Programm in der Bibliothek. Ein
  geändertes Programm verändert damit keinen laufenden und keinen
  abgeschlossenen Grow mehr.
- Neu — **Lücken werden einmal gefüllt.** Nennt ein Programm einen Wert nicht,
  kommt er beim Anlegen aus dem mitgelieferten RDWC- bzw. DWC-Standard und ist
  als solcher vermerkt. Luftfeuchte und Lufttemperatur kennt kein Standard —
  die bleiben leer. Programme ohne Wochen (Canna Aqua, VBX) bekommen ein
  Wochenraster aus der geplanten Vegi-Dauer und den Blütewochen der Sorte.
- Neu — **Laufende Grows werden übernommen.** Beim ersten Start legt der Fork
  für jeden laufenden Grow mit Programm den Plan an, vermerkt als „nachträglich
  angelegt“. Abgeschlossene Grows bleiben unverändert.
- Geändert — **„Werte bearbeiten“ im Wochenplan ändert den Plan des Grows**,
  nicht mehr das Programm. Jede Änderung steht in einem Änderungsbuch.
- Neu — **ORP im Wochenplan.** Das SKX-Programm nennt ORP je Phase (Vega 300,
  Blüte 400–450, Flush 300 mV); das Feld ist im Wochenplan bearbeitbar.

### Technik

- Tabellen `ForkGrowPlan` (Start-/Arbeits-/Endstand) und `ForkGrowPlanBuch`,
  `GrowPlanService`, `GrowPlanBauer`, `GrowPlanRegister`.
- Eine Weiche für alle Leser: `MischplanService.ProgrammFuerGrow` und
  `NutztWochenziele`. Ein Grow mit Plan nutzt die Wochenziele immer.
- Lesewege `GET /api/grows/{id}/plan` (`?stand=start|arbeit|ende`) und
  `GET /api/grows/{id}/plan/buch`.
- `FeedChartColumn.OrpMin/OrpMax`.

## 2.0.0-forkai.115

**Fork AI.** Jeder Helfer in Home Assistant hat nur noch eine Stelle im Fork,
die ihn schreibt.

- Behoben — **Die Feuchte-Obergrenze sprang ohne Zutun zurück.** Der
  Wochenplan setzte sie auf den Wert der Woche, die CO₂-Steuerung schrieb
  spätestens eine Stunde später ihren eigenen gespeicherten Wert darüber. Der
  Wochenplan hielt das für eine Änderung von Hand, meldete „von dir gesetzt“
  und zog die Obergrenze danach nicht mehr nach. Jetzt lässt die
  CO₂-Steuerung die Obergrenze in Ruhe, solange der Wochenplan sie führt. Auf
  der CO₂-Seite (Reiter Klima) steht sie dann nur zum Lesen, mit Verweis auf
  Betrieb → Wochenplan.
- Behoben — **Das CO₂-Ziel stand dauerhaft auf „von dir gesetzt“.** Der
  Wochenplan schrieb den rohen Planwert in die warme Zielstufe, die
  CO₂-Steuerung ihre Prozentstaffel aus demselben Planwert. Das CO₂-Ziel
  schreibt jetzt allein die CO₂-Steuerung, sobald sie einmal gespeichert
  wurde; die Übergabe-Zeile im Wochenplan sagt „über CO₂-Steuerung“.
- Behoben — **Speichern auf der Kühler-Seite nahm dem Wochenplan die
  Wassertemperatur ab.** Das Zielpaar Tag/Nacht wird dort nicht mehr
  geschrieben, solange der Wochenplan es führt.

### Technik

- `WochenplanSyncService.GefuehrteHelfer()` nennt die Helfer, die der Plan
  gerade führt — auch von Hand verstellte, denn deren Handwert soll ebenso
  kein anderes Modul zurücknehmen. `Co2SteuerungService.Schreibliste` und
  `ChillerSteuerungService.Schreibliste` lassen sie aus.
- `WochenplanSyncService.RolleBeiCo2Steuerung`: Rolle `co2-ziel` gehört der
  CO₂-Steuerung, sobald deren Einstellungen gespeichert sind.
- `Co2Live` trägt die wirksame Obergrenze (`rhObergrenzeProzent`, aus HA) und
  `rhObergrenzeAusPlan`; „tief nur bis“ rechnet mit dem wirksamen Wert statt
  mit dem gespeicherten.
- Neue Tests `EineQuelleJeHelferTests` (5) und
  `features/wochenplan/uebergabe-zustand.test.ts`. Gegenprobe: ohne den
  Filter in der CO₂-Schreibliste wird der Test rot.

## 2.0.0-forkai.114

**Fork AI.** Ein toter Knopf auf der Pflanzenkarte, und die Prüfung, die ihn
hätte finden müssen.

- Behoben — **„Töpfe & Sorten bearbeiten“** auf der Grow-Seite führte seit
  forkai.105 auf eine leere Seite. Der Knopf zeigte auf
  `/grows/…/bearbeiten`, das Formular liegt aber unter `/grows/…/setup`.
  Jetzt öffnet er das Formular, wie der Eintrag im „⋯“-Menü.
- Behoben — **Die Oberflächenprüfung war seit forkai.105 rot.** Der Fall
  „je Pflanze Sorte und Topf“ suchte die Sortenwahl noch auf der
  Pflanzenkarte, wo sie seit forkai.105 nur noch angezeigt wird. Er wartete
  90 Sekunden auf ein Feld, das es nicht mehr gab, und scheiterte dann an
  einer Zeitüberschreitung. Jetzt ändert er die Sorte dort, wo man sie
  tatsächlich ändert: im Grow-Formular unter „Töpfe & Sorten“.
- Behoben — **Vier weitere Fälle derselben Datei liefen seit forkai.105 gar
  nicht.** Die Datei läuft der Reihe nach; nach dem ersten Fehler wurden die
  übrigen übersprungen. Zwei davon benutzten ebenfalls Knöpfe, die es auf
  der Karte nicht mehr gibt („Pflanze hinzufügen“, „entfernen“). Sie legen
  an und leeren jetzt im Formular, wie ein Nutzer es tut, und prüfen dabei
  auch die Rückfrage beim Leeren eines belegten Topfs.

### Technik

- Neue Prüfung `link-ziele-haben-routen.node.test.ts`: Jedes Link-Ziel im
  Frontend (`to=`, `to:`, `navigate(`) muss auf eine Route oder eine
  Weiterleitung passen. Bisher gab es nur die Gegenrichtung
  (`routes-reachable`: hat jede Route einen Link?). Die neue Prüfung fand
  genau den einen toten Link und enthält ihn als Bissprobe.
- `pflanze-je-topf.spec.ts` prüft jedes Bedienelement vorab mit zehn
  Sekunden Frist. Fehlt eines, nennt die Meldung das Feld, statt nach
  90 Sekunden das Aufräumen mitzureißen. Die Topf-Zeile auf der Karte wird
  über ihren gewählten Topf gefunden, nicht mehr über die Position.
- Gemessen gegen die laufende App mit Demobestand: die alte Fassung scheitert
  nach 1,5 Minuten und lässt vier Fälle ungelaufen, die neue besteht alle
  fünf — zweimal hintereinander ohne Neustart, der Bestand steht danach wie
  vorher.

## 2.0.0-forkai.113

**Fork AI.** Eine geänderte Messung behält ihren Zeitpunkt.

- Behoben — **Messung ändern ohne Zeitangabe:** Wer eine Messung über die
  Schnittstelle nachträglich ändert (etwa eine Notiz ergänzt) und dabei kein
  `takenAtLocal` mitschickt, hat sie bisher still auf den Moment des
  Speicherns verschoben. Eine Messung von 21:02 stand nach einem Nachtrag um
  21:06 auf 21:06 — bei einer Korrektur am nächsten Morgen wäre sie im
  Verlauf an der falschen Stelle gelandet. Ursache: Der Vertrag setzte für
  ein fehlendes Zeitfeld die aktuelle Uhrzeit ein. Jetzt bleibt beim Ändern
  der gespeicherte Zeitpunkt stehen, solange keiner mitgeschickt wird; ein
  leeres Zeitfeld zählt wie ein fehlendes.
- Unverändert — Wer beim Ändern einen Zeitpunkt mitschickt, setzt ihn wie
  bisher. Beim Anlegen ohne Zeitangabe gilt weiter die Gegenwart. Das
  Messformular der App schickt den Zeitpunkt ohnehin immer mit.

### Technik

- `MeasurementUpsertRequest.TakenAtLocal` ist jetzt optional (kein
  Standardwert mehr); `MeasurementsApiController` füllt es beim Anlegen mit
  der Gegenwart und beim Ändern mit dem gespeicherten Zeitpunkt.
- Gehalten von `MessungBehaeltZeitpunktBeimAendernTests` (über HTTP, weil der
  Fehler im Model-Binding entsteht): zweimal hintereinander ändern, leeres
  Zeitfeld, explizite Zeit, Anlegen ohne Zeit. Ohne die Korrektur sind zwei
  der vier Fälle rot.

## 2.0.0-forkai.112

**Fork AI.** Wochenwerte lassen sich jetzt in der App ändern (F-004).

### Was Sie sehen

- Neu — **Betrieb → Wochenplan → „Werte bearbeiten":** eine Woche im Bild,
  mit ‹ › oder Wischen weiterblättern. EC, pH, Wasser Tag/Nacht, RH max,
  Luft, VPD, CO₂ und PPFD sind direkt editierbar. Weicht ein Wert vom Plan
  ab, wird die Zelle gelb und zeigt den Planwert mit „zurück". Die
  Punktreihe darunter zeigt die laufende Woche, Wochen mit eigenen Werten
  und ungespeicherte Änderungen. Der Balken unten sammelt alle Änderungen
  und speichert sie zusammen.
- Nach dem Speichern gehen die Werte der laufenden Woche sofort an Home
  Assistant, ohne auf 06:00 zu warten.
- Tippfehler, Werte außerhalb des Bereichs und „von" über „bis" sperren
  das Speichern; die Meldung nennt Woche und Feld.

### Technik

- Eigene Werte liegen als Abweichung in der neuen Tabelle
  `ForkWochenwerte` (Programm, Spalte, Feld, Wert). Die ausgelieferte
  Programmdatei bleibt unberührt, Programm-Updates kommen weiter an.
- `WochenwertUeberlagerung` legt sie nach jedem Laden auf die Programme
  (`KnowledgeBaseLoader.NachDemLaden`) — Mischplan, Zielband, Alarme, CO₂
  und Wochenplan-Sync sehen sie ohne eigene Änderung.
- Neue Endpunkte `GET`/`POST /api/wochenplan/werte/{growId}`; Speichern
  prüft alle Änderungen gemeinsam und speichert alles oder nichts. Ein
  Wert gleich dem Plan löscht die Abweichung.
- Die Werte gelten für das Programm, also für jeden Grow, der es benutzt;
  die Seite sagt das dazu, wenn es weitere gibt.

## 2.0.0-forkai.111

**Fork AI.** Beschriftungen (nachgetragen aus den Commits).

- Geändert — **Steuerung:** heißt die Canopy-Temperatur nicht mehr
  „Blatttemperatur"; die Rolle ist mit dem Luftfühler vorbelegt.
  Schaltverhalten unverändert.
- Geändert — **Zielwerte:** nennt beim VPD, ob Blatt- oder Luft-VPD gemeint ist.

## 2.0.0-forkai.110

**Fork AI.** Nachbesserung zu forkai.109 (nachgetragen aus den Commits).

- Behoben — der Server klemmte den Blattoffset noch auf 0…10; ein negativer
  Wert wurde still zu 0, und das Zelt rechnete wieder Luft-VPD.

## 2.0.0-forkai.109

**Fork AI.** Blattoffset mit Vorzeichen (nachgetragen aus den Commits).

- Geändert — **VPD-Blattoffset** ist jetzt die Differenz Blatt minus Luft,
  wie beim AC-Infinity-Controller: kühleres Blatt = negativ (Standard −2,
  Eingabebereich −10…0). Der gespeicherte Wert der Installation wurde nach
  dem Update einmalig neu gesetzt.

## 2.0.0-forkai.108

**Fork AI.** Die Leiste gehorcht wieder, und der Flush bekommt seine
Plan-Temperatur.

### Was Sie sehen

- Behoben — **„Mehr" blieb offen und zwei Ziele leuchteten:** war das
  Mehr-Menü offen und man tippte auf das Ziel, auf dem man ohnehin schon
  stand, passierte nichts — das Menü schloss nur bei einem Seitenwechsel.
  Gleichzeitig war der Mehr-Knopf grün und daneben das Ziel der laufenden
  Seite. Jetzt schließt jeder Tipp auf ein Leisten-Ziel das Menü, und
  solange es offen ist, ist nur „Mehr" markiert.
- Geändert — **SKX Canna Aqua, Flush:** Wassertemperatur 17 °C Tag /
  15 °C Nacht statt 15/15, wie im Plan.

### Technik

- `AppShell.tsx`: `setMoreOpen(false)` am `onClick` jedes Leisten-Ziels,
  Route-Markierung unterdrückt, solange `moreOpen` gilt.

## 2.0.0-forkai.107

**Fork AI.** Alle lesen jetzt dieselbe Woche — und die Wassertemperatur
sinkt erst zum Schluss.

### Was Sie sehen

- Behoben — **Trendwächter urteilte gegen die Phase:** der Wächter, der
  Messreihen bewertet und Push-Nachrichten schickt, rechnete weiter mit
  dem Band der Phase, während Live-Kachel, Messprotokoll und Mischplan
  längst die Wochenspalte des Feedcharts lasen. In Blütewoche 4 hieß das:
  EC 1,4 steht im Plan, gemeldet wurde es gegen 1,0–1,2.
- Behoben — **Addback schlug ein anderes EC vor als der Mischplan:** der
  Vorschlag kam aus der Mitte des Phasenbands statt aus der Woche. Auf
  einem Bildschirm standen damit zwei Ziele.
- Behoben — **CO₂-Seite beschriftete ihr Planziel falsch:** dort stand
  „gilt die ganze Phase, nicht je Woche". Gerechnet wurde längst mit dem
  Wochenwert; jetzt steht auch das dort.
- Geändert — **SKX Canna Aqua, Wassertemperatur der Blüte:** Flores 2 bis 8
  laufen durchgehend auf 20 °C Tag / 18 °C Nacht statt bis auf 17/15 °C
  abzusinken. Die Absenkung auf 15 °C bleibt der Flush-Spalte vorbehalten —
  dort, wo der Plan sie als Endphasen-Maßnahme für wenige Tage vorsieht.
  Kühleres Wasser bremst davor nur die Aufnahme.

### Technik

- `TrendWatchRunner` und `GrowWorkflowApiController` holen ihre Sollwerte
  über `Zielband.FuerGrow` statt über `TargetValueService.GetTargets`; beide
  bekommen dafür die Wissensbasis als Abhängigkeit.
- `Co2SteuerungService.PlanHerkunft` nennt die Wochenspalte, wenn der Grow
  Wochenziele führt und die Spalte ein CO₂-Band hat.

## 2.0.0-forkai.106

**Fork AI.** Vom Livebild direkt ins Stammblatt des Grows.

Wer auf der Live-Seite etwas am laufenden Grow ändern wollte — Sorte,
Zelt, Veg-Dauer, Töpfe — musste über die Grow-Liste gehen und den
richtigen Lauf heraussuchen, obwohl die Seite genau weiß, welcher Grow
gerade gezeigt wird.

Im „⋯" neben „Messen" steht jetzt **Grow bearbeiten** und führt direkt in
das Formular des gezeigten Laufs. Wie die anderen Einträge lässt es sich
über „Knöpfe bearbeiten" nach oben in die Zeile heften. Zeigt das Zelt
keinen laufenden Grow, erscheint der Eintrag nicht, statt ins Leere zu
führen.

## 2.0.0-forkai.105

**Fork AI.** Töpfe belegen und leeren an einer Stelle.

Zwei Karten mit fast gleichem Namen, und nur eine konnte entfernen: „Töpfe
& Sorten" im Formular wies zu, „Pflanzen & Sorten" am Grow löschte. Wer im
Formular vier Töpfe auf „leer" stellte und speicherte, sah danach wieder
sechs belegte — die Auswahl sah aus wie eine Aktion und war keine.

Jetzt liegt beides im Formular, und „leer" wirkt sofort statt beim
Speichern. Das Formular speichert alles auf einmal; ein Fehlgriff im
Auswahlfeld plus Speichern hätte sonst eine Pflanze samt Pheno-Bogen
entfernt, ohne Rückfrage und womöglich mitten in der Blüte. Die Rückfrage
kommt deshalb beim Wählen. „Alle auf leer" fällt weg — sechs Pflanzen mit
einem Klick zu entfernen ist keine Bequemlichkeit, sondern ein Unfall. Ein
nur geplanter Topf verschwindet weiter ohne Rückfrage; dort gibt es nichts
zu verlieren.

## 2.0.0-forkai.104

**Fork AI.** Zwei Wächter erfüllt, die eigene Änderungen gerissen hatten.

Die Mengen im neuen Gaben-Abschnitt rechneten mit `Number(text)`. `Number('')`
ist 0 und gilt als gültige Zahl: eine geleerte Menge wäre still als 0 gebucht
worden, mit Erfolgsmeldung. Unlesbare Mengen brechen das Speichern jetzt ab.

Und die beiden Löschwege aus .96 und .97 — Verbrauchsbuchung und Zählerstand —
gab es nur in der API. Gebaut und von niemandem erreichbar. Jetzt trägt jede
Zeile einen Entfernen-Knopf: in der Verbrauchstabelle des Artikels und in der
Zählerstands-Tabelle unter Strom. Beide Fälle sind real — ein Vertipper bei der
Menge verschiebt Füllstand und Kosten dauerhaft, ein schief einsortierter
Zählerstand hebt die Stromsumme um mehrere tausend kWh.

## 2.0.0-forkai.103

**Fork AI.** Nachbesserung zu .102: Sperrt das Klima ganz — Feuchte oder Blatt
über der Obergrenze —, steht der T6 auf der normalen Stufe. Der Begründungstext
behauptete dann trotzdem die Dosierstufe. Dafür gibt es jetzt einen eigenen Fall.

## 2.0.0-forkai.102

**Fork AI.** Warum der T6 gerade tief läuft.

Der Reiter Klima der CO₂-Steuerung zeigt über den Einstellungen eine
Statuskarte: aktuelle T6-Stufe, je eine Zeile für Blatt und Feuchte mit
Ist-Wert, geltender Tief-Grenze und der Marke erfüllt/zu warm/zu feucht, dazu
ein Satz Klartext für den Haltebereich. Die Grenzen kommen aus denselben
Helfern, mit denen der Template-Helfer in Home Assistant entscheidet — der Fork
liest ihn nur, geregelt wird weiter dort.

## 2.0.0-forkai.101

**Fork AI.** Buchungsziel und Artikelsumme stimmen wieder in der Anzeige.

Zwei Anzeigefehler aus .96, beide erst an der laufenden Instanz sichtbar. Das
Buchungsziel fehlte im DTO der Kosten-Seite: die API lieferte es, das
Artikel-Formular zeigte immer „beim Kauf". Wer danach irgendetwas anderes am
Artikel speicherte, stellte es unbemerkt zurück — stiller Datenverlust, kein
Schönheitsfehler. Und die Summe je Artikel kannte nur Füllungen, während die
Gesamtsumme seit .96 nach Buchungsziel unterscheidet; ein Artikel stand auf
0 €, obwohl gebuchter Verbrauch in der Gesamtsumme auftauchte. Beide rechnen
jetzt dasselbe.

## 2.0.0-forkai.100

**Fork AI.** Gaben direkt bei der Messung buchen.

Die Verbrauchsbuchung gab es seit .96, aber nur über die API — am Becken kam man
nicht dran, und jede Gabe landete weiter als Freitext in der Notiz. Die manuelle
Messseite hat jetzt den Abschnitt **Gaben**: beliebig viele Zeilen aus Artikel
und Menge, denn eine Gabe ist selten ein Mittel. Gebucht wird nach dem Speichern
der Messung; geht dabei etwas schief, steht die Messung trotzdem und die Meldung
sagt genau das.

Diese Änderung und der Blatt-Offset aus .99 entstanden nebeneinander und trugen
beide die Nummer .99; überschnitten haben sie sich nur in der Versionsdatei.

## 2.0.0-forkai.99

**Fork AI.** Der Blatt-Offset geht an den Klimacontroller.

Das Feld „Blatt kühler als Luft" bleibt die eine Stelle, an der der Wert gepflegt
wird — es bekommt nur einen Ausgang: zwei neue Zelt-Einstellungen nennen den
HA-Dienst und den Port, an den der Offset beim Speichern durchgereicht wird
(leer = aus, wie bisher). Das Vorzeichen wird dabei gedreht, weil Grow OS +2
führt und die AC-Infinity-App −2. Scheitert der Dienst, wird trotzdem gespeichert
und gewarnt — ein stilles Auseinanderlaufen ist genau der Zustand, den das Feld
beseitigen soll.

## 2.0.0-forkai.98

**Fork AI.** Der Import kann die Zählerreihe eines Grows neu aufbauen.

Die Dublettenprüfung aus .97 verhindert neue Mischungen, räumt aber nicht auf,
was schon gemischt dasteht: 76 Importwerte um Mitternacht neben sechs
Worker-Ständen um 22 Uhr. Aus 1.585 kWh wurden so 10.024, aus 507 € Strom 3.208.
Ohne Löschweg in der Anwendung bliebe nur Handarbeit an der Datenbank im
`/data`-Volume.

Auf Wunsch verwirft der Import deshalb alle Stände im Zeitraum des Grows und
schreibt die Reihe vollständig neu — absichtlich ohne Rücksicht auf den Anlass:
die Mischung *ist* der Fehler, eine Reihe aus einer Quelle zu einer Tageszeit
das Ziel.

## 2.0.0-forkai.97

**Fork AI.** Drei Fehler aus .96, alle erst an der laufenden Instanz sichtbar.

Das Buchungsziel kam nie an: Modell, Tabelle, Repository und Formular hatten das
Feld, der Request nicht. Der Aufruf antwortete 200 und lieferte trotzdem den
alten Wert zurück. Der Compiler kann das nicht sehen — die Klasse ist gültig,
sie hat das Feld nur nicht. Der Rundweg-Test prüft genau diese Fehlerklasse,
liess den Artikel-Request aber als Ausnahme aus. Die Ausnahme ist gestrichen und
der Demobestand legt jetzt einen Verbrauchsartikel an.

Der Import verglich außerdem das UTC-Datum statt des Ortsdatums. Tagesbuckets
aus Home Assistant beginnen um lokale Mitternacht, in Europe/Berlin also 22:00
UTC des Vortags — die Dublettenprüfung lag damit systematisch einen Tag daneben.
Folge war nicht nur eine Dublette: ein Rückwärtssprung gilt als Zählerwechsel und
addiert den vollen Stand. Der Import schreibt jetzt nur noch vor dem ersten
vorhandenen Stand. Dazu ein Löschweg für einzelne Zählerstände, damit sich ein
misslungener Import zurücknehmen lässt.

## 2.0.0-forkai.96

**Fork AI.** Gaben buchen, Zählerstände nachtragen.

Zwei Lücken mit derselben Form: das Backend kann es, aber kein Mensch kommt dran.
Die Verbrauchsbuchung gab es seit .20 mit genau einem Schreiber, der
CO₂-Steuerung. Jetzt nimmt sie mehrere Zeilen auf einmal an — eine Gabe ist
selten ein Mittel —, prüft erst alles und schreibt dann alles, und lässt sich
einzeln zurücknehmen.

Dazu ein **Buchungsziel je Artikel**: in der Voreinstellung zählt die Füllung
weiterhin voll im Durchgang. Umgestellt ist sie lagerneutral, und nur gebuchter
Verbrauch trifft den Durchgang, bewertet über den Preis je Einheit. Ein
10-L-Kanister, der drei Läufe hält, verzerrt sonst den Lauf, in dem er gekauft
wurde.

Neu ist außerdem der **Zählerstand-Import**: er holt die Tageswerte aus dem
Recorder von Home Assistant und legt je Tag einen Stand an, mit Phase aus dem
Durchgang. Damit lässt sich Strom auch für Zeiträume nachtragen, in denen der
Worker noch nicht lief.

## 2.0.0-forkai.95

**Fork AI.** „noch 5 h 52 min" — und die Dauer bleibt zusammen.

Die Wechselzeile brach in der schmalen Licht-Kachel um, und auf der zweiten
Zeile blieb „min" allein stehen. Jetzt steht dort nur noch die Dauer, und sie
hält als Block zusammen: bricht die Zeile doch einmal, dann davor.

## 2.0.0-forkai.94

**Fork AI.** Kürzere Wechselzeile in der Licht-Kachel.

„Nächster Wechsel: in 6 h 12 min" brach in der schmalen Kachel über zwei Zeilen
um. Jetzt steht dort „Wechsel in: 6 h 12 min" — dass es der nächste ist,
versteht sich von selbst.

## 2.0.0-forkai.93

**Fork AI.** Bugfix: Zyklus und Restzeit fehlten auf der Live-Seite.

Die Live-Seite baut ihre Kacheln über die Bereichsansicht, daneben gibt es einen
zweiten Aufruf im Live-Bildschirm. Zyklus („12/12") und Restzeit wurden nur an
der zweiten Stelle durchgereicht — auf dem Bildschirm fehlten sie damit genau
dort, wo man hinschaut. Es sah nach einem alten Zwischenspeicher aus: der Server
lieferte das Neue, die Oberfläche zeigte es nicht.

Dazu ein Test, der beide Aufrufe vergleicht: was die eine Stelle übergibt, muss
die andere auch übergeben. Das ist in dieser Runde zweimal passiert, einmal beim
Nachtband und einmal hier.

## 2.0.0-forkai.92

**Fork AI.** Live-Kachel Licht: gleiche Schreibweise, und die Restzeit stimmt auch nach dem Weglegen.

Die Kachel schreibt die Restzeit jetzt wie die Licht-Steuerung — „Nächster
Wechsel: in 7 h 5 min". Zwei Schreibweisen für dieselbe Angabe lesen sich wie
zwei verschiedene Angaben.

Dazu: ein Telefon im Standby lässt Zeitgeber ruhen. Wer die Übersicht offen
liegen lässt und später wieder hinschaut, sah sonst die Restzeit von vorhin. Die
Kachel stellt sich beim Zurückkehren auf den Bildschirm sofort nach, zusätzlich
zum Halbminutentakt beim Zuschauen.

## 2.0.0-forkai.91

**Fork AI.** „Nächster Wechsel" sagt nur noch, wann.

Auf der Licht-Steuerung stand „Nächster Wechsel: an 05:00 · in 7 h 5 min". Die
Uhrzeit steht eine Zeile darüber im Zeitplan, die Richtung groß daneben — dreimal
dasselbe in zwei Zeilen. Übrig bleibt die Zahl, nach der man tatsächlich schaut.

## 2.0.0-forkai.90

**Fork AI.** Die Sonne war immer noch ein Emoji.

Android zeichnet ☀ von sich aus bunt, auch wenn es als Schriftzeichen dasteht.
Jetzt bittet der Variantenwähler U+FE0E ausdrücklich um die Schriftform, damit
das Zeichen die gedeckte Farbe der Zeile erbt statt als einziger Farbklecks im
Klima-Band zu stehen.

## 2.0.0-forkai.89

**Fork AI.** Ruhigere Kopfzeile, und das Licht sagt, wann es umschlägt.

Sonne und Mond standen als Emoji im Monospace-Satz direkt am Wort — das einzige
farbige Zeichen auf dem Bildschirm, und zu eng. Sie sind jetzt Schriftzeichen in
der gedeckten Farbe der Zeile, mit Luft zum Wort.

Die Licht-Kachel ist neu geordnet: „12/12" steht oben in der Statusecke, wo auch
sonst „im Ziel" steht, die Schaltzeiten passen damit in eine Zeile. Darunter neu
die Restzeit — „noch 7 Std 53 Min bis an". Gerechnet wird sie in der Oberfläche
und jede halbe Minute neu, nicht auf dem Server: eine mitgelieferte Restzeit
altert zwischen zwei Abrufen und stünde nach fünf Minuten falsch da.

## 2.0.0-forkai.88

**Fork AI.** Blätter lassen sich nach unten wegziehen.

Der Griff oben an jedem Blatt war bisher nur ein Balken. Er verspricht die
Geste, die jedes Telefon-Blatt kann, und tat nichts — wer daran zog, hielt das
Blatt für hängen geblieben. Jetzt folgt das Blatt dem Finger, der Schleier wird
dabei heller, und beim Loslassen entscheidet der Weg: ab etwa 90 px oder nach
einem schnellen Wisch schliesst es, sonst federt es zurück. Gezogen wird am
Griff und am Titel, nicht am ganzen Blatt — sonst würde jede lange Liste darin
beim Scrollen das Blatt mitnehmen. Die Trefferfläche des Griffs ist rund 24 px
hoch statt 4; der sichtbare Balken sitzt unverändert. Schleier, „Abbrechen" und
Escape schliessen weiter wie bisher.

Gilt für alle Blätter: Erfassen, „⋯" auf der Live-Seite, Auswahllisten, Geräte,
Rechenweg der Zuluft und „Leiste anpassen".

## 2.0.0-forkai.87

**Fork AI.** Tag/Nacht-Leiste: mittig und ohne erfundene Nachkommastellen.

Die beiden Spalten stehen jetzt zentriert. Und ein Band wird nur so genau
geschrieben, wie es ist: aus „22,0–28,0" wird „22–28", aus einem eingetragenen
22,5 bleibt „22,5–28". Eine Null hinter dem Komma behauptet eine Genauigkeit,
die niemand eingetragen hat — und kostet auf dem Telefon die Zeile, die das Band
umbrechen lässt.

## 2.0.0-forkai.86

**Fork AI.** Bugfix: Das Nachtband kam auf der Kachel nicht an.

Zwischen dem, was der Server für eine Kachel ausrechnet, und dem, was an sie
ausgeliefert wird, liegt eine von Hand geschriebene Abbildung — und die kannte
die fünf neuen Felder aus forkai.85 nicht. Kein Fehler, kein Log, HTTP 200: die
Werte fielen still auf dem letzten Meter heraus. Gewirkt hat das Nachtband
trotzdem, in Score und Alarm; nur sehen konnte man es nicht.

Dazu eine Zählung, die das künftig von selbst findet: jedes Feld der Kachel muss
auch im ausgelieferten Stand stehen.

## 2.0.0-forkai.85

**Fork AI.** Eigene Zielwerte für die Nacht.

Ohne Licht kühlt das Zelt ab, und dieselbe Wassermenge ergibt in kälterer Luft
eine höhere relative Feuchte. Ein Zielband, das rund um die Uhr gilt, meldet
deshalb jede Nacht dasselbe — und wer jede Nacht falschen Alarm bekommt, glaubt
auch dem echten nicht mehr.

Die Zelt-Grenzwerte tragen jetzt ein zweites Band für die Dunkelphase. Es gilt,
sobald das Licht aus ist, und zwar überall gleich: auf der Live-Kachel, im Score
und im Alarm. Die Meldung nennt die Grenze der Phase, die gerade geprüft wurde,
und schreibt „Nachtband" dazu.

Auf der Kachel stehen beide Bänder nebeneinander, das geltende hell. So sieht
man ohne Rechnen, ob ein Wert nur gerade passt oder auch in der anderen Phase
passen würde. Wo für die Nacht nichts Eigenes hinterlegt ist, steht zweimal
dasselbe — das ist die Aussage, nicht ein vergessenes Feld.

Der Wochenplan füllt das Band selbst: Nacht ist der Tagwert minus 4 K, mit
derselben Toleranz. Wer für eine Woche etwas anderes will, trägt `airTempNightC`
in die Spalte ein. Die Luftfeuchte bekommt bewusst kein Nachtband — Kondensat
entsteht im Dunkeln, eine nachts gelockerte Grenze wäre eine leisere Anzeige und
kein besserer Grow.

Wer nichts einträgt, merkt nichts: ohne Nachtwerte verhält sich alles wie bisher.

## 2.0.0-forkai.84

**Fork AI.** Keine Laufzeit mehr aus zu wenig Verbrauch.

Die CO₂-Flasche hatte nach sechs Tagen 77 g von 10 kg gebucht — 0,8 %. Daraus
ergaben sich rechnerisch 794 Tage und „leer am 09.11.2028". Beides stimmt und
beides ist wertlos: Eine Hochrechnung um Faktor 130 trägt nicht, schon gar nicht
wenn der gebuchte Verbrauch ein Netto-Wert ist, der den echten untertreibt.

Eine Laufzeit aus gebuchtem Verbrauch gibt es jetzt erst ab einem Zwanzigstel
der Füllung und zwei Wochen. Vorher steht da, woran es liegt und was hilft —
statt einer Zahl, der niemand trauen sollte.

Nebenbei: Die Prognose sagt jetzt auch, woher sie kommt — aus dem gebuchten
Verbrauch oder aus früheren Laufzeiten. Das stand vorher pauschal als
„geschätzt aus den letzten Laufzeiten" da, auch wenn gemessen wurde.

## 2.0.0-forkai.83

**Fork AI.** Der Rauchtest meldete einen Fehler, wo keiner war.

Er zählte alle Navigationsgruppen; die Oberfläche rendert nur die, in denen
etwas übrig ist. „Versuch" hat genau einen Eintrag, und der ist versteckt — also
fünf statt sechs. Der Test liest jetzt `sichtbareGruppen()` statt `navGroups`.

Bemerkenswert dabei: Im Test steht selbst der Hinweis, solche Zahlen nicht
anzupassen, sondern die Quelle zu lesen. Genau das war hier nötig — nur war die
gelesene Quelle die falsche.

## 2.0.0-forkai.82

**Fork AI.** Nachtwerte der Wassertemperatur im SKX-Programm angehoben: Blüte
Woche 4 bis 6 stehen nachts jetzt auf 18 °C statt 17 bzw. 16 °C. Die Tagwerte
(20 / 19 / 18 °C) bleiben, wie sie waren.

**Warum.** Wurzeln sind wechselwarm — unter 18 °C bremst die Aufnahme spürbar,
während der Sauerstoffgewinn gegenüber 18 °C rechnerisch bei etwa 0,3 mg/L
liegt. Im RDWC hängen die Wurzeln rund um die Uhr im Wasser, anders als im
Substrat, wo eine Nachtabsenkung im Wurzelraum kaum ankommt. Wer die tiefere
Absenkung als Reiz will, stellt sie am Helfer ein — der Wochenplan-Abgleich
merkt sich das als „von dir gesetzt".

## 2.0.0-forkai.81

**Fork AI.** Aufräumen direkt hinterher: die Prüfung auf die doppelt geschaltete
Steckdose stand zweimal im Code — einmal mit Entity-Namen, einmal als
Ja/Nein-Antwort daneben. Zwei Fassungen derselben Frage laufen irgendwann
auseinander; geblieben ist die mit dem Namen, weil die Warnung ihn anzeigt.
Am Verhalten ändert sich nichts.

## 2.0.0-forkai.80

**Fork AI.** Der Wasserkühler bekommt eine eigene Seite unter Steuerung.

**Bisher stand er nur als Zeile da.** Sie meldete außerdem den falschen Zustand:
abgelesen wurde der Kühl*bedarf*, nicht die Steckdose — also der Wunsch zu
kühlen und nicht die Tat. Hing der Kühler an einer Schaltsperre, behauptete die
Zeile „kühlt". Jetzt kommt der Zustand vom Schalter, und in genau diesem Fall
steht dort „wartet auf Schaltsperre".

**Die Seite zeigt oben vier Zahlen** — Wasser, Ziel jetzt, Gerät und die
laufende Schaltsperre — und darunter die Reiter Betrieb und Schutz. Geschaltet
wird weiter in Home Assistant: an der Mindestpause hängt die Lebensdauer des
Kompressors, und sie soll weiterlaufen, wenn dieses Add-on gerade neu startet.

**Das Ziel gehört nicht dieser Seite.** Tag- und Nachtwert kommen aus dem
Wochenplan oder aus der Crop-Steering-Absenkung. Die Seite sagt, woher der Wert
stammt, und verlinkt dorthin, statt ihn ein zweites Mal zu schreiben.

**Neu: eine Warnung vor zwei Händen an derselben Dose.** Schaltet die
Steckdosen-Funktion auf der Crop-Steering-Seite dieselbe Entität wie die
Regelung, greifen zwei Stellen nach demselben Kompressor — sichtbar erst als
Dose, die von selbst umspringt. Das steht jetzt als rotes Band auf der Seite,
mit dem Entity-Namen und dem Weg zum Abstellen.

**Crop Steering wohnt jetzt unter Steuerung.** Dieselbe Seite, nur ein zweiter
Weg dorthin (`/steuerung/cropsteering`) und eine eigene Zeile in der Übersicht —
keine Kopie, damit Änderungen am Original ankommen.

**Zwei Menüpunkte sind aus dem Menü verschwunden, nicht aus der App.** „Crop
Steering" steht jetzt unter Steuerung, und der Versuch „Zelt (AC-Test)" ist
durch die Steuerungsseiten abgelöst. Beide bleiben über ihre Adresse und die
Suche erreichbar: ein neues Feld `versteckt` nimmt einen Eintrag aus den Menüs,
ohne ihn aus `navigation.ts` zu streichen — dort hängen auch Suche,
Referenz-Doku und die Erreichbarkeitsprüfungen dran.

## 2.0.0-forkai.79

**Fork AI.** Die Verbrauchstabelle war auf dem Handy unbrauchbar.

**Sie lief rechts aus dem Bild.** Die Breite richtete sich nach dem längsten
Inhalt, und die Euro-Spalte lag außerhalb. Jetzt bekommen die beiden
Zahlenspalten feste Breiten und die erste nimmt den Rest.

**Das Datum stand zweimal.** Links „2026-09-12", darunter nochmal „2026-09-12:
203 Impulse". Als Journaltext ist das richtig, in einer Tabelle mit
Datumsspalte doppelt — jetzt „12.09." und darunter nur noch der Inhalt.

**0,08 kg sagt weniger als 77 g.** Unter einem Kilo bzw. Liter wird umgerechnet;
das spart auch Stellen in einer engen Spalte.

Und „1 Buchungen" heißt jetzt „1 Buchung".

## 2.0.0-forkai.78

**Fork AI.** Die Artikel-Karten auf der Kosten-Seite werden kleiner und ruhiger.

**Aus vier Knöpfen werden Textlinks.** „Nachfüllung erfassen", „Als leer
markieren", „Bearbeiten" und „Löschen" standen als vier gleich große Knöpfe
untereinander und brauchten mehr Höhe als alle Stammdaten der Karte zusammen.
Jetzt: „Nachfüllen" und „Leer" als Links, der Rest hinter einem ···. Ein
Löschen, das einen Tipp mehr kostet, passiert seltener versehentlich — und es
stand vorher gleichberechtigt neben allem anderen.

**Der Verbrauch bekommt eine Aufklappzeile** statt eines Knopfes, der schief am
Rand klebte. Rechts steht, was drinsteht — nach dem ersten Aufklappen der
Zeitraum und die Summe, sonst der Grund, warum es nichts zu zeigen gibt.
Zugeklappt wird dort, wo aufgeklappt wurde; der große gestrichelte Knopf unten
entfällt.

**Die Zeiträume sind eine schiebbare Zeile** statt vier umbrechender Knöpfe, in
derselben Pillenform wie in der Steuerung.

## 2.0.0-forkai.77

**Fork AI.** Verbrauch je Artikel über einen wählbaren Zeitraum — auf der
Kosten-Seite, nicht in der Steuerung.

Jede Artikel-Karte bekommt „Verbrauch zeigen": 7 Tage, 30 Tage, dieser Grow oder
alles, darunter die Buchungen mit Menge, Kosten und Herkunft, und eine
Summenzeile. Geladen wird erst beim Aufklappen — für jeden Artikel ungefragt
eine Abfrage zu fahren kostet Zeit für etwas, das vielleicht niemand ansieht.

**Der Preis kommt von der Füllung, nicht vom Artikel.** Die jüngste Füllung vor
einer Buchung hat sie bezahlt; war sie teurer als die vorige, rechnet die
Tabelle das richtig. Der Artikelpreis ist nur der Rückfall für Altdaten. Ist gar
kein Preis bekannt, bleibt die Zeile leer statt auf null zu stehen — eine Null
sähe aus wie „hat nichts gekostet" und die Summe wäre vollständig und zu niedrig
zugleich. Die Seite sagt es dann auch.

**Der laufende Tag fehlt absichtlich.** Er ist noch nicht gebucht; sein Stand
steht in der Steuerung. Zwei Zahlen mit zwei Wahrheiten in einer Tabelle machen
beide unbrauchbar.

Damit funktioniert die Ansicht für jeden Verbrauchsartikel, nicht nur für CO₂ —
Purolyt und Dünger bekommen sie, sobald dort Verbräuche gebucht werden.

## 2.0.0-forkai.76

**Fork AI.** Die Kellerzuluft zieht aus dem Home-Assistant-Dashboard in den Fork.

**Neu: Betrieb → Steuerung → Zuluft.** Oben stehen die vier Zahlen, um die es geht
— Differenz, Stufe, Zustand, Schaltsperre. Darunter in Reitern die Regel
(Mindest-Differenz, Außentemperatur-Minimum), der Lüfter (Stufe min/max,
Mindestlaufzeit und -pause) und der Betrieb mit dem Schalter „Automatik aktiv".

**Der Rechenweg auf Tipp.** Draußen 88 % und der Lüfter saugt trotzdem — das ergibt
erst Sinn, wenn man sieht, dass 12 °C bei 88 % weniger Wasser tragen als 22 °C bei
57 %. Ein Tipp auf die Differenz-Kachel zeigt die Kette von den vier Messwerten bis
zur Zielstufe.

**Geregelt wird weiter in Home Assistant.** Der Fork hält die Sollwerte und schreibt
sie in die Helfer; das Schalten bleibt dort, wo es auch dann noch läuft, wenn dieses
Add-on gerade neu startet.

**Für eine frische Anlage.** Die Steuerung bringt neun Geräterollen und dreizehn
Bauteile mit — Helfer, Rechenwerte und die Automation legt der Fork auf Wunsch selbst
an. Wer die Regelung schon von Hand gebaut hat, behält sie: vorhandene Objekte werden
erkannt, eine handgebaute Automation bleibt unangetastet, und die Seite zeigt beim
ersten Aufruf die Werte der vorhandenen Helfer statt der Werkseinstellungen.

**Behoben.** Selbst angelegte Rechenwerte hatten keine Verfügbarkeit und rechneten bei
ausgefallenem Fühler mit dem Vorgabewert weiter, statt sich abzumelden.

## 2.0.0-forkai.75

**Fork AI.** Zwei Anzeigefehler auf der Seite Geräte & Entitäten.

**Jede Beschriftung stand doppelt.** Das Auswahlfeld brachte seine eigene mit,
obwohl die Zeile außen schon eine hat — einmal mit Einheit, einmal ohne. Für
die Vorlesehilfe bleibt sie erhalten, sichtbar ist sie nur noch einmal.

**Der Livewert lag über dem Namen.** Das Auswahlfeld durfte nicht schrumpfen und
behielt die Breite seines Inhalts; bei langen Entitätsnamen schob sich der Wert
rechts darüber. Jetzt schrumpft es, und ein zu langer Name endet mit drei
Punkten statt unter der Zahl zu verschwinden.

## 2.0.0-forkai.74

**Fork AI.** Behebt den Baufehler aus forkai.72: Die Automations-Vorlagen waren
doppelt eingebunden.

Das SDK nimmt Dateien aus dem Projektordner ohnehin als Inhalt auf. Das
zusätzliche `Include` legte sie ein zweites Mal dazu, und der Bau brach mit
NETSDK1022 ab — deshalb waren forkai.72 und .73 rot. Richtig ist `Update`, so
wie es bei den anderen Ordnern im Projekt auch steht. Im Testprojekt bleibt
`Include` stehen: Dort liegen die Dateien außerhalb des Projektordners, das SDK
findet sie also nicht von selbst.

Am Programm ändert sich nichts.

## 2.0.0-forkai.73

**Fork AI.** Das Einrichten einer Steuerung ist vollständig: Vorschau,
Zustimmung, Probeschaltung.

**Die Automationen bekommen einen eigenen Schritt.** Ein Knopf zeigt erst, was
entstünde — angelegt, erneuert, unangetastet oder entfallen —, und erst ein
zweiter schreibt. Ein Trockenlauf, der nichts anfasst, ist an einem Gasventil
die Mühe wert.

**Neu ist die Probeschaltung.** Sie öffnet das Ventil zwei Sekunden und sieht
nach, ob der Port wirklich umschlägt. In dieser Anlage hat es Tage gedauert
herauszufinden, dass der Port „an" meldete und trotzdem kein Gas ankam — der
Arbeitsdruck stand zu niedrig, das Nadelventil war zu. Zwei Sekunden beim
Einrichten ersparen dem Nächsten diese Suche.

Sie prüft den Schaltweg, nicht den Gasfluss: Ob Gas strömt, zeigen zwei Sekunden
nicht. Der CO₂-Wert vorher und nachher steht trotzdem dabei — ein Sprung ist ein
gutes Zeichen, sein Ausbleiben beweist nichts. Geschlossen wird das Ventil in
jedem Fall, auch wenn das Nachsehen dazwischen scheitert.

## 2.0.0-forkai.72

**Fork AI.** Der Fork kann die Automationen einer Steuerung jetzt anlegen — der
letzte und heikelste Teil des Einrichtens, weil am Ende ein Ventil an einer
Gasflasche hängt.

**Nur was der Fork selbst angelegt hat, fasst er wieder an.** Jede erzeugte
Automation trägt eine Herkunftsmarke. Findet der Dienst unter derselben Kennung
eine Automation ohne diese Marke, rührt er sie nicht an und sagt das. Von Hand
gebaute Automationen enthalten Dinge, die keine Vorlage kennt — in dieser Anlage
etwa die Nachführung des Durchflusses und das Halten des letzten Zustands bei
Geräteaussetzern. Ein Generator, der darüberschreibt, nimmt sie weg, ohne dass
es jemand merkt.

Vor dem Überschreiben wird der alte Stand weggeschrieben, damit ein Zurück
existiert. Nach dem Schreiben wird nachgesehen, ob die Automation wirklich
geladen ist — sonst steht eine Regelung da, die stumm nichts tut.

Der Endpunkt kennt eine Vorschau, die nichts schreibt und nur sagt, was
geschähe. Sie ist die Grundlage für den Knopf, der vor dem Anlegen zeigt, was
entsteht.

Noch nicht dabei: dieser Knopf selbst und die Probeschaltung des Ventils.

## 2.0.0-forkai.71

**Fork AI.** Alle drei Automationen der CO₂-Steuerung liegen jetzt als Vorlage
im Repo — Wächter, Abluft-Drosselung und die Dosierung selbst, abgelesen aus der
laufenden Anlage.

Neu ist der Schlüssel `wenn` in einer Vorlage: Ein Block, der ihn trägt,
entfällt, wenn die genannte Rolle frei ist. Die Dosierung prüft, ob die Abluft
wirklich gedrosselt ist — wer keinen Abluft-Regler hat, bekommt diese Prüfung
nicht und dosiert trotzdem. Ohne diesen Schlüssel wäre die ganze Automation an
einem Gerät gescheitert, das sie gar nicht braucht.

Zwei Abweichungen zur handgebauten Fassung sind in den Vorlagen vermerkt: Beide
enden mit dem Licht statt zu einer festen Uhrzeit, weil sich ohne eine Rolle für
die geplante Aus-Zeit des Lichts keine allgemeingültige Zeit ableiten lässt.
`co2_ende_vor_licht_aus` bleibt dadurch bei einer frisch angelegten Steuerung
wirkungslos.

Es fehlt der Dienst, der die Vorlagen anlegt — mit Herkunftsmarke, Sicherung des
vorhandenen Stands und Nachprüfung.

## 2.0.0-forkai.70

**Fork AI.** Vorarbeit für das Anlegen der Automationen — und dabei zwei Lücken
gefunden.

**Eine Rolle fehlte.** Das Modell kannte nur den *Zustand* des Dosier-Ports,
nicht die Entität, die ihn *schaltet*. Bei AC Infinity sind das zwei Dinge: ein
`binary_sensor` sagt, ob der Port läuft, ein `select` legt ihn um. Ohne die
zweite Rolle könnte der Fork die Dosier-Automation nicht anlegen — er wüsste,
woran er abliest, aber nicht, was er umlegen soll. Neu: „Dosier-Steckdose ·
schalten".

**Der Wächter wäre anderswo stumm.** Die laufende Fassung schickt eine
Push-Nachricht an ein bestimmtes Telefon; diese Entität gibt es bei niemand
sonst. Die Vorlage schreibt stattdessen ins Logbuch. Das Ventil schließt er so
oder so — aber dass er es getan hat, muss irgendwo stehen.

Die Vorlage des Wächters liegt jetzt als Datei im Repo. Dosierung und
Abluft-Drosselung folgen, ebenso der Dienst, der sie anlegt.

## 2.0.0-forkai.69

**Fork AI.** Der Fork legt jetzt auch die Rechenwerte einer Steuerung an — Ziel,
Bedarf, Klima-Freigabe und Impulslänge.

Anders als Einstellwerte und Schalter entstehen Template-Helfer nicht über einen
Befehl, sondern über denselben Einrichtungsdialog, den ein Mensch im Browser
durchklickt: Dialog öffnen, Art wählen, Felder abschicken. Bricht ein Schritt
ab, bleibt ein halb offener Dialog zurück — deshalb wird nach einem Fehlschlag
abgebrochen statt weitergemacht.

Die Rechenvorschriften kommen aus dem Katalog, mit Platzhaltern für die Geräte.
Ist eine gebrauchte Rolle nicht zugeordnet, entsteht der Rechenwert gar nicht
und die Seite sagt, welcher übersprungen wurde. Eine Vorschrift mit stehendem
Platzhalter wäre die schlechtere Wahl: Sie würde nicht ungültig, sondern stumm
mit einem Ausweichwert weiterrechnen.

Der Knopf heißt jetzt „Fehlende anlegen" und macht beides in einem Zug — erst
die Helfer, dann die Rechenwerte, weil die Rechenwerte die Helfer lesen.

Die Automationen fehlen weiterhin; sie brauchen einen anderen Weg und eine
ausdrückliche Zustimmung, weil am Ende ein Gasventil daran hängt.

## 2.0.0-forkai.68

**Fork AI.** Die vier Rechenvorschriften der CO₂-Steuerung stehen jetzt im
Katalog — abgelesen aus der laufenden Anlage, samt Hysterese an den
Temperaturstufen und dem Halten des letzten Zustands, wenn ein Gerät kurz
aussetzt.

Die Geräte darin sind Platzhalter. Derselbe passt an beiden Stellen:
`states('[[canopy]]')` ergibt den Wert, `states.[[canopy]]` das Objekt mit
`last_changed` — weil eine Entitäts-Id mit ihrem Punkt genau das ist, was nach
`states.` gehört.

## 2.0.0-forkai.67

**Fork AI.** Zielwerte, Profile und Grenzwerte liegen jetzt unter einem Menüpunkt.

Bisher standen drei Menüpunkte für eine Frage: „Zielwerte" sagte, was gilt,
„Sollwert-Profile" und die Grenzwerte in „Regeln & Automatik" waren die Orte, an
denen man es ändert. Wer von der Auskunft zum Ändern wollte, verliess die Seite.
`/zielwerte` hat nun drei Reiter — Jetzt gültig, Profile, Grenzwerte — nach dem
Muster, das „Regeln & Automatik" schon benutzt. Die Sprünge in der Gruppenliste
zeigen auf den passenden Reiter statt auf eine andere Seite.

Der alte Pfad `/sollwerte` leitet auf den Reiter „Profile" um; Lesezeichen und
Links aus Home-Assistant-Dashboards laufen also weiter. Der Feed-Chart bleibt
bewusst draussen: er wohnt in der Wissensdatenbank und ist mehr als Zielwerte —
Dosiermengen und Spülwochen stehen dort mit drin.

## 2.0.0-forkai.66

**Fork AI.** Die Frage „was gilt gerade" hat jetzt genau eine Antwort.

Auf der Sollwerte-Seite stand seit forkai.18 der Kasten „Gilt gerade" — damals die
Notlösung für ein Problem, das inzwischen eine eigene Seite hat. Zwei Antworten auf
dieselbe Frage sind schlechter als eine, auch wenn beide stimmen. An seiner Stelle
steht nun ein Verweis: die Sollwerte-Seite ist der Editor, `/zielwerte` die Auskunft.
Der zugehörige Endpunkt und seine Komponente sind mit entfernt statt als toter Weg
liegen zu bleiben.

Dabei aufgefallen: die Übersetzung der Phasennamen stand in drei Fassungen im Code,
zuletzt sagte eine „Vegetativ" und eine andere „Vegi". Sie sitzt jetzt in
`Services/Phasenname.cs`, und alle drei Stellen lesen dort.

## 2.0.0-forkai.65

**Fork AI.** Zwei Mängel der Zielwerte-Seite, die erst die strenge Prüfung gegen
die laufende App gefunden hat.

Das Zielband auf den Karten stand mit englischem Punkt da („1.3 – 1.5"): die Zahl
geht als JSON mit Punkt über die Leitung, und die Anzeige gab sie roh weiter.
Jetzt kommt sie fertig formatiert aus dem Endpunkt, wie überall sonst.

Die Fußzeilen — Alarmangabe und Gruppenhinweis — waren zu blass: Kontrast 2,48
gegen die geforderten 4,5. Das betraf beide Ansichten. Sie stehen jetzt eine
Stufe kräftiger.

## 2.0.0-forkai.64

**Fork AI.** Drei Fehler der neuen Zielwerte-Seite.

Die Sprünge rechts in den Gruppen („Sollwerte ›", „Regeln ›") endeten in einer
Fehlermeldung: sie waren als rohe Links gebaut und verließen damit den
Grundpfad, unter dem das Add-on im Ingress von Home Assistant läuft. Jetzt
navigieren sie innerhalb der App.

Die Herkunft stand bei VPD, CO₂, pH und Wassertemperatur auf „Profil", obwohl
die Wochenspalte diese Werte vorgibt — sie nennt dort zufällig dieselben Zahlen,
und verglichen wurden die Bänder statt der Quellen. Wer VPD ändern wollte, wurde
so zum Profil geschickt, obwohl die Woche es beim nächsten Wechsel übersteuert.

PPFD stand ohne Messwert auf der Seite. Ohne Lichtmessgerät ist die Zeile keine
Auskunft, sondern ein Vorwurf — dieselbe Begründung, aus der Sauerstoff und
Füllstand nicht auftauchen.

## 2.0.0-forkai.63

**Fork AI.** Neue Seite **Betrieb → Zielwerte**. Sie beantwortet die Frage, die
bisher an vier Stellen entstand und an keiner stand: welche Regel greift gerade,
und wo ändere ich sie.

Oben die Werte als Karten — das Zielband als Streifen, der Ist-Wert als Nadel
darin. Damit sieht man nicht nur, DASS etwas danebenliegt, sondern wie weit. Ein
Tipp auf die Karte klappt die Herkunftskette auf: Profil, Feed-Chart-Woche,
Zelt-Grenze untereinander, das Überstimmte durchgestrichen, das Geltende in
Akzentfarbe. Bei einer festen Grenze steht dabei, wer sie gesetzt hat — der
Wochenplan oder ein Mensch.

Darunter dieselben Werte nach Änderungsort gruppiert, je Gruppe ein Sprung
dorthin, und die Übergabe an Home Assistant mit ihrem Zustand. Ganz oben warnt
die Seite vor den zwei Stellen, die die Kette still kappen: eine Zelt-Regel auf
„Fest" und ein von Hand verstellter Helfer.

Licht, Füllstand und Sauerstoff stehen bewusst nicht auf der Seite — sie haben in
keiner der vier Quellen ein Ziel und läsen sich mit „–" wie ein Versäumnis.

## 2.0.0-forkai.62

**Fork AI.** Unterbau für die Seite „Zielwerte": ein Endpunkt, der zu jeder
Messgröße nicht nur das geltende Band liefert, sondern die ganze Herkunftskette —
Profil, Feed-Chart-Woche, Zelt-Grenze — mit der Angabe, welche Stufe gilt und
welche überstimmt wurde. Bei festen Grenzen steht dabei, WER sie gesetzt hat: der
Wochenplan oder ein Mensch. Genau das verschweigt „dein Wert" auf der Kachel
bisher. Dazu die Gruppierung nach Änderungsort und die Hinweise auf Stellen, an
denen eine feste Grenze die Wochenspalte abschneidet oder eine Alarmregel fehlt.
Die Seite selbst kommt im nächsten Schritt; der Endpunkt rechnet nichts eigenes,
sondern liest dieselbe Kette wie Kacheln und Alarme.

## 2.0.0-forkai.61

**Fork AI.** Behebt einen Baufehler aus forkai.59: im Übergabe-Durchlauf war der
Name „jetzt" zweimal vergeben — einmal für die laufende Wochenspalte, einmal für
den aus Home Assistant gelesenen Ist-Wert. Der Übersetzer lehnt das ab, das Image
von forkai.59 ist deshalb gar nicht erst entstanden. Der äußere Name heißt jetzt
anders; an der Übergabe selbst ändert sich nichts. Ebenfalls nachgetragen: der
Eintrag zu forkai.60 war zu knapp für die Prüfung der Release Notes.

## 2.0.0-forkai.60

**Fork AI.** Baufehler aus forkai.59 behoben (doppelt vergebener Name im
Übergabe-Durchlauf); inhaltlich unverändert.

## 2.0.0-forkai.59

**Fork AI.** Die Alarmgrenzen für Lufttemperatur und Luftfeuchte wandern jetzt mit
dem Wochenplan. Beide Messgrößen haben kein Zielband — sie können deshalb nicht auf
„Plan" stehen und standen bis hierher auf Zahlen, die von Hand eingetragen waren
(18–29 °C). Der wöchentliche Abgleich zieht sie nun nach: Lufttemperatur als Band um
den Planwert (±3 K), Luftfeuchte als Obergrenze aus der Wochenspalte. Die Regeln
bleiben „Fest", nur ihre Zahlen ändern sich. Von Hand verstellte Grenzen lässt der
Plan in Ruhe — wie bei den Helfern werden sie als „von dir gesetzt" markiert und auf
der Wochenplan-Seite zum Freigeben angeboten.

## 2.0.0-forkai.58

**Fork AI.** Der Fork legt die Helfer einer Steuerung jetzt selbst in Home
Assistant an — der erste Teil des Einrichtens.

Bisher zeigte die Steuerungsseite in einer fremden Installation nur „nicht
verfügbar": Die rund zwanzig Einstellwerte, Schalter, Zeitstempel und Zähler
hinter der CO₂-Regelung sind hier über Tage von Hand entstanden und entstehen
anderswo gar nicht. Ein Knopf in der Karte „Was in Home Assistant fehlt" legt
sie nun an.

Drei Regeln stecken fest darin. Angelegt wird nur, was fehlt; Vorhandenes wird
nie überschrieben, weil ein Helfer mit richtigem Namen und zurückgesetztem Wert
schlimmer wäre als gar keiner. Und was an einem nicht zugeordneten Gerät hängt,
entsteht nicht: Wer keinen Abluft-Regler hat, bekommt keine Drosselungs-Helfer.
Antwortet Home Assistant nicht, wird gar nichts angelegt — sonst entstünden
blind zwanzig Helfer, die es vielleicht schon gibt.

Rechenwerte und Automationen fehlen noch; sie brauchen andere Wege und kommen
getrennt, weil sie getrennt schiefgehen.

Nebenbei behoben: Zwei Namen im Katalog hätten neue Entitäten erzeugt statt der
bestehenden. Home Assistant leitet die Kennung aus dem Namen ab, und
„CO2 Impulsdauer min" wäre `co2_impulsdauer_min` geworden — beim nächsten Lauf
wäre derselbe Helfer ein zweites Mal entstanden. Ein Test fährt das jetzt für
alle Bauteile durch.

## 2.0.0-forkai.57

**Fork AI.** Behebt die Ursache hinter den Licht-Testfehlern, die forkai.56 im
Test selbst umgangen hat.

`SteuerungRepository` merkte sich in einem einzelnen Schalter, dass die Tabellen
stehen. Im Betrieb stimmt das — dort gibt es eine Datenbank. In den Tests bekommt
jeder Fall seine eigene Datei: Der erste legte die Tabellen an und setzte den
Schalter, jeder weitere sprang über das Anlegen hinweg und stand vor einer leeren
Datei. Gemerkt wird jetzt pro Datenbankdatei, damit der nächste neue Test nicht
über dieselbe Stelle stolpert.

## 2.0.0-forkai.56

**Fork AI.** Behebt die beiden neuen Licht-Tests aus forkai.55: ihnen fehlte die
angelegte Testdatenbank, deshalb kamen sie gar nicht erst an die Ablage. Am
Programm ändert sich nichts.

## 2.0.0-forkai.55

**Fork AI.** Schließt die Testlücke der Licht-Steuerung — die Prüfung auf `main`
ist damit wieder grün.

Die Licht-Einstellungen kamen in forkai.51 ohne einen einzigen Test. Neu ist ein
Rundweg, der prüft, dass keins der neun Felder beim Speichern verlorengeht und
dass ein zweites Speichern das erste ersetzt. Der allgemeine Rundweg kann diesen
Vertrag nicht fahren: er füllt jedes Feld mit einer festen Probe, und „1" ist
keine Uhrzeit im Format HH:mm — das ist jetzt mit Grund vermerkt.

Noch offen: der Weg durch Controller und Dienst samt Prüfung der Zeitformate und
dem Schreiben an den Controller. Dafür braucht es einen gestellten Funk.

## 2.0.0-forkai.54

**Fork AI.** Behebt einen Baufehler in forkai.53 — die Übergabe an Home Assistant
fragte die Helferzustände über die falsche Quelle ab. Das Zustandswörterbuch ist
nach Metrik-Kennungen geschlüsselt, nicht nach Entitäten; jetzt wird jeder Helfer
einzeln abgefragt. Inhaltlich ändert sich nichts.

## 2.0.0-forkai.53

**Fork AI.** Der Wochenplan schreibt seine Werte jetzt selbst nach Home Assistant.

### Was Sie sehen

- Auf der Seite **Wochenplan** ein neuer Abschnitt „Übergabe an Home Assistant"
  mit den vier betreuten Werten: Chiller Tag, Chiller Nacht, RH-Obergrenze und
  CO₂-Ziel — je Zeile der Wert der laufenden Woche und ob er dem Plan folgt.
- Übergeben wird beim Wechsel der Plan-Woche und einmal täglich um 06:00.

### Ihre Handverstellungen bleiben

Stellen Sie einen dieser Helfer in Home Assistant selbst um, merkt der Fork das
beim nächsten Lauf: der Wert wird **nicht** überschrieben, sondern als „von dir
gesetzt" markiert und ausgelassen. Erst ein Klick auf **freigeben** überlässt ihn
wieder dem Plan. Beim allerersten Lauf schreibt der Dienst gar nichts, sondern
merkt sich, was in Home Assistant steht — sonst überschriebe er beim Hochfahren
Einstellungen, die längst stimmen.

### Grenzen

Laufen zwei Durchgänge mit Wochen-Zielen gleichzeitig, wird nichts geschrieben:
die Helfer gehören dem Zelt, nicht dem Grow, und zwei Pläne würden sich um sie
streiten. PPFD, Lichtzeiten und Dosiermengen bleiben außen vor.

## 2.0.0-forkai.52

**Fork AI.** Verständlichere Beschriftung auf der Licht-Seite.

### Was Sie sehen

- Der Schalter unter „Erweitert" hieß „Helfer in Home Assistant mitschreiben"
  und erklärte sich über die alte Dashboard-Karte, die es nicht mehr gibt. Er
  heißt jetzt „Zeiten für eigene Automationen in Home Assistant bereitstellen"
  und sagt, welche vier Helfer dafür nötig wären — und dass er für die Bedienung
  hier nicht gebraucht wird.

## 2.0.0-forkai.51

**Fork AI.** Nachbesserung an der Licht-Seite.

### Was Sie sehen

- Auf dem Telefon quetschten die breiten Knöpfe die Beschriftung daneben auf
  einen Buchstaben je Zeile. Beschriftung steht jetzt oben, die Knöpfe darunter
  und dürfen umbrechen.
- Die Zeitplan-Knöpfe heißen nur noch **Veggie** und **Blüte**; die Zeiten stehen
  in der Zeile darüber.
- Die vier Helfer der alten Dashboard-Karte werden nicht mehr erwartet: Die
  Karte ist weg, und das Mitschreiben ist ab Werk aus (unter „Erweitert"
  weiterhin einschaltbar).

## 2.0.0-forkai.50

**Fork AI.** Das Licht wird jetzt hier bedient, nicht mehr im Grow-Dashboard.

### Was Sie sehen

- Unter **Betrieb → Steuerung** führt die Zeile „Licht LED Top" jetzt auf eine
  eigene Seite mit drei Reitern.
- **Betrieb**: Aus, An und die beiden Zeitpläne (Veggie, Blüte) auf Knopfdruck,
  dazu die Leistungsstufe 1–10.
- **Zeitplan**: die vier Zeiten der beiden Zeitpläne. Wer den gerade aktiven
  Zeitplan ändert, schickt ihn sofort an den Controller.
- **Erweitert**: wie hartnäckig geschrieben wird — Abstand, Prüffrist,
  Wiederholungen.
- Kommt ein Befehl beim Gerät nicht an, steht das dort: erst „wird übernommen",
  nach den Wiederholungen eine deutliche Warnung mit dem echten Zustand.

### Warum

Die Lampe ließ sich bisher nur über eine eigene Karte im Home-Assistant-Dashboard
bedienen. Die Zeitpläne lagen damit woanders als die übrigen Sollwerte des Zelts.

Der Zeitplan selbst läuft weiter im AC-Infinity-Controller — er schaltet auch
dann, wenn Add-on, Home Assistant und Internet aus sind. Geschrieben wird nur,
wenn Sie etwas ändern.

### Technisch

- Neu: `LichtSteuerungService`, `LichtEinstellungen`, `GET/PUT /api/steuerung/licht`,
  `POST /api/steuerung/licht/befehl`, Seite `features/steuerung/LichtDetail.tsx`.
- Sechs Geräte-Rollen (`licht_modus`, `licht_stufe`, `licht_ein_zeit`,
  `licht_aus_zeit`, `licht_zustand`, `licht_status`) unter Steuerung → Geräte.
- Gegen die AC-Infinity-Cloud, die parallele Befehle verwirft: nur Abweichendes
  schreiben, Abstand zwischen den Aufrufen, Prüfung nach Frist, Wiederholung.
- Die vier `input_datetime.led_top_zeitplan_*`-Helfer werden mitgeschrieben,
  solange die alte Dashboard-Karte sie braucht (abschaltbar unter Erweitert).

## 2.0.0-forkai.49

**Fork AI.** Sie bestimmen selbst, welche Knöpfe oben auf der Live-Seite stehen.

### Was Sie sehen

- Im „⋯"-Blatt unten neu: **Knöpfe bearbeiten**. Dort hat jede Handlung einen
  Schalter **anheften**.
- Angeheftetes steht direkt neben der Statuszeile, alles andere bleibt im „⋯".
- Höchstens zwei Knöpfe — der nächste löst den ältesten ab, damit die Zeile
  nicht umbricht.
- Ohne eigene Wahl steht wie bisher nur **Messen** oben.
- Die Einstellung gilt je Gerät: am Telefon dürfen es andere sein als am
  Schreibtisch.

### Warum

Wie viel Platz die Kopfzeile hergibt, hängt am Bildschirm, nicht am Zelt — und
wer täglich nachfüllt, will Addback nicht jedes Mal aufklappen. Statt einer
festen Auswahl entscheidet das jetzt jeder selbst.

## 2.0.0-forkai.48

**Fork AI.** Die Kopfzeile der Live-Seite ist auf dem Telefon einzeilig.

### Was Sie sehen

- Statt drei vollbreiter Knöpfe steht rechts neben „Letzte Messung …" nur noch
  **Messen** und ein **„⋯"**.
- Unter dem „⋯" liegen **Addback starten**, **Anpassen** und — bei mehreren
  Zelten — die **Zeltauswahl**.
- Am Schreibtisch ändert sich fast nichts: dieselbe Zeile, nur ohne den
  Umbruch.

### Warum

Die drei Knöpfe brauchten auf dem Telefon gut 200 Pixel, bevor der erste
Messwert kam — und „Messung erfassen" wie „Addback starten" sind über das
grüne „+" der Kopfleiste ohnehin einen Griff entfernt. „Anpassen" ist ein
Modus für alle paar Wochen und hat in der Dauer-Ansicht nichts verloren.

## 2.0.0-forkai.47

**Fork AI.** Neue Seite: Betrieb → **Wochenplan**.

### Was Sie sehen

- Die laufende Woche mit allen Werten des Düngeprogramms auf einen Blick: EC, pH,
  Wasser Tag/Nacht, VPD, RH, Lufttemperatur, CO₂, PPFD und die wichtigsten
  Anmischmengen.
- Darüber die **Anker**, aus denen sich die Woche ergibt: Vegi-Start, Flip und —
  sofern die Blütewochen der Sorte hinterlegt sind — das Erntefenster.
- Darunter der ganze Verlauf, die laufende Woche hervorgehoben. Hält der Plan
  seine letzte Spalte (gestreckte Vegi), steht es dort.
- Benutzt ein Grow die Wochen-Ziele nicht, sagt die Seite das gleich oben: die
  Werte gelten dann nur fürs Anmischen, nicht für Kacheln und Alarme.

### Warum

Die Wochenwerte wirken seit forkai.46 überall, waren aber nirgends am Stück zu
sehen — EC und pH im Mischplan, das Klima nur indirekt über die Kacheln. Wer
wissen wollte, was nächste Woche gilt, musste die Wissensdatenbank aufschlagen.

## 2.0.0-forkai.46

**Fork AI.** Der Wochenplan bringt jetzt auch das Klima mit.

### Was Sie sehen

- Wasser Tag/Nacht, VPD, CO₂ und PPFD kommen in der Blüte und in der Vegi aus der
  **Woche** des Düngeprogramms, nicht mehr aus der Phase. Live-Kacheln,
  Messprotokoll, Diagnose und alle Grenzwerte mit der Quelle „Plan" wandern damit
  von selbst mit — ohne dass Sie alle paar Wochen etwas nachstellen.
- Für „SKX Canna Aqua" sind alle 14 Spalten gefüllt: Bewurzelung, Vega W1–4,
  Flores W1–8 und Flush.

### Warum

Der Klimateil eines Plans stand bisher nur als Fließtext in den Phasennotizen und
wirkte nirgends. Er sitzt jetzt an derselben Wochenspalte wie EC und pH und erbt
damit deren Anker: Blütewochen ab Flip, Vegi-Wochen ab Start, und beim Strecken der
Vegi bleibt die letzte Spalte stehen. Ein Programm ohne diese Angaben verhält sich
wie zuvor.

### Was noch nicht drin ist

Luftfeuchte und Lufttemperatur stehen an der Spalte, wirken aber noch nicht: das
Zielband des Originals kennt beide Größen nicht. Sie sind für die Wochenplan-Seite
und die Übergabe an Home Assistant vorgesehen.

## 2.0.0-forkai.44

**Fork AI.** Der Fork warnt, wenn Anzeige und Regelung Verschiedenes meinen.

Geregelt wird die CO₂-Dosierung in Home Assistant, und die Automation trägt ihre
Entitäten im YAML. Wer im Fork eine Rolle umhängt, ändert damit die **Anzeige** —
nicht das, was dosiert. Weicht beides voneinander ab, steht auf **Geräte &
Entitäten** jetzt eine Warnung: welche Rolle worauf zeigt und womit die Automation
tatsächlich arbeitet.

### Warum nicht andersherum

Die Automation könnte ihre Entitäten aus Helfern lesen, statt sie fest zu haben.
Das hieße: fünf `condition: state` würden zu Templates, die still scheitern statt
laut — an einer Automation, die ein Ventil an einer Gasflasche schaltet, samt
Off-Verifikation und Notabschaltung. Der Gewinn wäre ein Gerätetausch alle paar
Jahre. Die Warnung schließt die Lücke, ohne dieses Risiko einzugehen.

## 2.0.0-forkai.43

**Fork AI.** Neu am Gerät: **„Wo dieses Gerät vorkommt"**.

Beim Aufklappen steht jetzt zuerst, woran das Gerät im Fork hängt — Messgröße,
Steuerung, Dosierung, Strom — mit den betroffenen Zwecken und einem **Öffnen**
zur jeweiligen Stelle. Das ist die Frage vor jeder Umstellung: *Wenn ich diese
Entität tausche, was hört auf zu funktionieren?* Bisher stand die Antwort
verstreut in den Marken der einzelnen Entitäten.

## 2.0.0-forkai.42

**Fork AI.** Eine Stelle im Menü, an der gepflegt wird.

- **Betrieb → Geräte & Entitäten** trägt jetzt alles: Geräte, Messgrößen,
  Wartung.
- **Home Assistant** und **Sensoren & Wartung** stehen unter **Einrichtung** am
  Ende und heißen jetzt „Home Assistant (Verbindung)" und „Sensoren & Wartung
  (erfassen)". Beide Seiten bleiben unverändert und zeigen dieselben Daten — sie
  werden nur seltener gebraucht: die eine für URL und Token, die andere zum
  Erfassen einer Kalibrierung.
- Im Reiter Messgrößen steht der Zeltname nur noch einmal; „Zelt-RDWC · Zelt"
  las sich wie ein Fehler.

## 2.0.0-forkai.41

**Fork AI.** Dritter Reiter: **Wartung**.

### Was Sie sehen

- Kalibrier- und Prüffristen nach **Fälligkeit** sortiert, gruppiert in „Fällig"
  (die nächsten sieben Tage) und „Später". Je Zeile die Aufgabe, darunter das
  **Gerät** — mit dem Namen, den es auf der Geräteseite trägt, nicht „pH" oder
  „EC" aus dem Inventar.
- Geräte mit Intervall, aber ohne bisheriges Ereignis stehen ebenfalls da.
  Sonst sähe man eine Kalibrierfrist erst, nachdem sie einmal gelaufen ist.

### Bewusst nur lesend

Erfasst und geändert wird weiter unter „Sensoren & Wartung"; die Fußzeile führt
dorthin. Damit bleibt die Originalseite unberührt.

## 2.0.0-forkai.40

**Fork AI.** Formatfehler im Reiter „Messgrößen" behoben.

Der Livewert stand neben dem Auswahlfeld und legte sich bei langen Gerätenamen
über dessen Text — „RDWC Probe Sensor Sonden-Temperatur" und „28,3 °C"
überlagerten sich. Beschriftung und Wert stehen jetzt in einer Kopfzeile über
dem Feld: links die Messgröße mit Einheit, rechts der aktuelle Wert, darunter
das Feld über die volle Breite.

## 2.0.0-forkai.39

**Fork AI.** Die Messgrößen des Zelts stehen jetzt bei den Geräten.

### Was Sie sehen

- **Geräte & Entitäten** hat Reiter: **Geräte** und **Messgrößen**.
- Im Reiter Messgrößen steht je Größe (Lufttemperatur, pH, EC …) die Auswahl im
  Fork-Stil und rechts der Livewert. Angezeigt werden die zugeordneten und die
  wichtigsten Größen; die übrigen leeren stehen hinter dem Schalter „Auch leere
  Messgrößen zeigen". Zwölf leere Zeilen über acht belegten wären Rauschen —
  eine fehlende Kernmessgröße soll man dagegen sehen.
- Geändert wird über denselben Weg wie bisher: die Seite **Einrichtung → Home
  Assistant** bleibt unverändert bestehen und zeigt dieselben Werte.

### Warum so

Der Fork schreibt die Originalseite nicht um, sondern liest und schreibt über
ihren Endpunkt. Damit bleibt eine Weiterentwicklung des Originals an dieser
Stelle ohne Handarbeit übernehmbar — das war die Bedingung.

## 2.0.0-forkai.38

**Fork AI.** Auch die Entitäts-Auswahlen außerhalb der Geräteseite laufen jetzt
über das Blatt.

### Was Sie sehen

- **Steuerung → Geräte & Entitäten**: je Rolle eine Auswahl mit Suche, gruppiert
  in „Eigene Geräte" und „Aus Home Assistant", mit „— wie ab Werk —" oben. Die
  bisherige Grenze von 200 Vorschlägen entfällt — die Suche trägt auch mehr.
- **Einrichtung → Home Assistant**: dieselbe Auswahl für die Messgrößen des
  Zelts und für die Kameras, jeweils mit dem Livewert als Hinweis.

### Bewusst unverändert

Kurze Auswahlen mit zwei, drei festen Werten (Einheit, Phase, Ja/Nein) bleiben
native Felder. Ein Blatt wäre dort Aufwand ohne Gewinn — und je weniger Seiten
des Originals der Fork umschreibt, desto leichter lassen sich dessen
Weiterentwicklungen übernehmen.

## 2.0.0-forkai.37

**Fork AI.** Farben getauscht: **„vermutet" ist jetzt blau**, **„korrigiert"
gelb** — samt Marke an der Gerätezeile, Hinweis an der Entität und der Zeile
über der Korrekturliste.

Der Grund: Gelb zieht den Blick stärker an, und beim Suchen geht es um das, was
Sie selbst gesetzt haben. Eine Vermutung ist dagegen nur ein Hinweis, dass der
Fork mangels Auskunft von Home Assistant geraten hat — die bleibt ruhiger.

## 2.0.0-forkai.36

**Fork AI.** Korrekturen sind jetzt klar von Vermutungen zu unterscheiden — und
nehmen weniger Platz.

- **Eigene Farbe:** „vermutet" bleibt gelb (da ist etwas unklar), „korrigiert"
  wird **cyan** — bis hin zur Marke an der Gerätezeile und dem Hinweis an der
  Entität. Vorher standen beide in fast demselben Gelb nebeneinander.
- **Einklappbar:** Die Liste der von Hand gesetzten Einträge steckt hinter einer
  Zeile „3 Einträge von Hand gesetzt ›". Sie bleibt sichtbar — nur die Liste
  selbst klappt auf, wenn man sie braucht.

## 2.0.0-forkai.35

**Fork AI.** Formulare und Auswahlfelder im Stil der App statt im Stil des Handys.

### Was Sie sehen

- „Rubrik anlegen" öffnet jetzt ein **Blatt von unten** — dasselbe, das hinter
  dem grünen Plus schon steckt — statt ein Formular mitten in die Karte zu
  schieben.
- **„Gehört zu"** und **„Verschieben nach"** sind keine grauen System-Auswahlen
  mehr, sondern ein Blatt mit **Suche**, Gruppen (Rubriken / Geräte) und dem
  Rückweg hervorgehoben ganz oben. Die aktuelle Wahl trägt einen Haken.

### Technik

- `V1Sheet` aus `ErfassenSheet` herausgelöst: gleicher Schleier, Griff, Escape
  und Fokus, gleiches CSS, nur mit freiem Inhalt. `ErfassenSheet` ist der erste
  Nutzer und schrumpft auf die Hälfte.
- `V1Select` darauf aufgesetzt; das geschlossene Feld folgt den Feldregeln aus
  `primitives.css`. Für zwei, drei feste Möglichkeiten bleibt das native Select
  richtig — diese Auswahl ist für Listen, in denen man sucht.

## 2.0.0-forkai.34

**Fork AI.** Auch verschobene **Geräte** zählen als Korrektur.

- Die Kennzahl heißt jetzt **„korrigiert"** und zählt beides: von Hand
  zugeordnete Entitäten **und** Geräte, die umgehängt oder umbenannt wurden.
  Vorher stand nach dem Verschieben eines Geräts weiter eine Null da.
- Die Liste oben führt beide Arten mit Herkunft und **Zurück**: „hängt an
  ‚Kameras' · laut Home Assistant: FRITZ!Box 7590".
- Die gelbe Zahl an einer Gerätezeile zählt Korrekturen im ganzen Zweig — auch
  die am Gerät selbst.

## 2.0.0-forkai.33

**Fork AI.** Die gelbe Marke schlägt bis zum Controller durch.

Steht eine von Hand zugeordnete Entität an einem Port, trägt jetzt auch der
Controller darüber die gelbe Zahl — neben der grünen Zahl seiner angeschlossenen
Geräte. Vorher sah man sie erst nach dem Aufklappen, also genau dann nicht, wenn
man suchte.

## 2.0.0-forkai.32

**Fork AI.** Der Rückweg funktioniert jetzt wirklich.

- **Korrektur verwerfen** löst am Gerät auch die Entitäten, die ihm von Hand
  zugeschlagen wurden. Vorher blieben die hängen — man verwarf die Korrektur und
  die zugewanderte Entität stand weiter da.
- Die Auswahl **„Gehört zu"** hat einen ausdrücklichen Eintrag
  **„— dorthin, wo Home Assistant sie zählt —"**. Damit braucht man nicht zu
  wissen, aus welchem Gerät die Entität ursprünglich kam.

## 2.0.0-forkai.31

**Fork AI.** Die Korrekturliste versteckt sich nicht mehr.

- Gibt es von Hand zugeordnete Entitäten, steht die Liste **sichtbar** oben —
  vorher musste man die Zahl antippen, was niemand erraten konnte. Je Zeile ein
  **Zurück**.
- Die Kennzahl „verschoben" ist gelb statt grün, und ein Gerät, in dem etwas von
  Hand liegt, trägt ein gelbes **!** in der Zeile.

## 2.0.0-forkai.30

**Fork AI.** Verschobene Entitäten sind jetzt zu sehen — und zu finden.

- Neue Kennzahl **„verschoben"** oben. Ein Tipp klappt die Liste aller von Hand
  gesetzten Zuordnungen auf: Entität, wo sie jetzt steht, wo Home Assistant sie
  zählt, und ein **Zurück** je Zeile.
- An der Entität selbst steht die Marke **„verschoben"** samt Herkunft.
- **Bugfix:** Eine zugewanderte Entität benennt ihr neues Gerät nicht mehr um.
  Vorher hieß eine Kamera nach dem Verschieben plötzlich „RDWC CO2 + Light
  Sensor" — der Name kam von dem Gerät, aus dem die Entität stammte, und die
  Entität war danach praktisch unauffindbar.

## 2.0.0-forkai.29

**Fork AI.** „Geräte & Entitäten": eine Zuordnung lässt sich zurücknehmen.

- Nach dem Verschieben einer Entität erscheint oben eine Meldung mit
  **Rückgängig** — vorher schrieb die Auswahl sofort und stumm, ein Fehlgriff
  beim Scrollen fiel erst auf, wenn die Entität irgendwo fehlte.
- Die Auswahl unter einer Entität heißt jetzt **„Gehört zu"** statt nackt
  dazustehen.

## 2.0.0-forkai.28

**Fork AI.** Bugfix auf „Geräte & Entitäten": Das ⋯-Menü öffnete sich nur, wenn
die Zeile ohnehin aufgeklappt war — bei einem zugeklappten Controller tat der
Tipp nichts. Das Menü hängt jetzt am ⋯ selbst und erscheint in jedem Zustand.

## 2.0.0-forkai.27

**Fork AI.** Eigene **Rubriken** auf „Geräte & Entitäten".

- **Rubrik anlegen** (z. B. „Kameras", „Klima") — ein Fach ohne Entitäten, unter
  das Geräte gehängt werden. Im ⋯-Menü eines Geräts steht dafür
  **Verschieben nach …** mit allen Rubriken und Controllern zur Auswahl,
  dazu „an nichts".
- Gedacht für Geräte, die Home Assistant zwar irgendwo einsortiert, aber nicht
  dort, wo sie hingehören — etwa Kameras, die über die FRITZ!Box gemeldet werden.
- Rubriken zählen nicht als Geräte in der Kopfzeile; sie sind ein Fach.

### Technik

- `POST /api/geraete/rubrik`; Spalte `IstRubrik` in `ForkGeraete` wird bei
  bestehenden Anlagen nachgerüstet.
- Ein Eltern-Schlüssel, den es nicht mehr gibt (gelöschte Rubrik), fällt still
  weg — sonst wäre das Gerät aus der Liste verschwunden, obwohl es noch da ist.

## 2.0.0-forkai.26

**Fork AI.** **Geräte & Entitäten** wird kompakter.

- Die drei Knöpfe je Gerät (Umbenennen, Aushängen, Auf Vorgabe) zogen die Karte
  auf dem Handy weit auseinander. Sie sitzen jetzt hinter einem **⋯** in der
  Gerätezeile und erscheinen erst beim Antippen — mit Text, nicht als Symbole.
- „Auf Vorgabe" heißt jetzt **„Korrektur verwerfen"**. Das sagt, was passiert:
  Ihre Änderung fällt weg, es gilt wieder, was aus Home Assistant abgeleitet wird.
- Die Unterzeile wiederholt das Modell nicht mehr, wenn es dem Gerätenamen
  entspricht („FRITZ!Box 7590 (UI) · Controller · FRITZ!Box 7590 (UI)").

## 2.0.0-forkai.25

**Fork AI.** **Geräte & Entitäten** lässt sich jetzt korrigieren.

### Was Sie sehen

- **Umbenennen**: ein Gerät bekommt seinen eigenen Namen, unabhängig davon, wie
  es in Home Assistant heißt.
- **Aushängen**: ein Gerät, das Home Assistant unter einem anderen einsortiert
  hat, steht danach für sich. Gedacht für Fälle wie die Kameras, die über die
  FRITZ!Box gemeldet werden, aber eigene Geräte im Zelt sind.
- **Entität zuschlagen oder lösen**: eine Entität einem anderen Gerät zuordnen —
  für alles, was Home Assistant keinem Gerät zurechnet (Template-Schalter,
  Helfer).
- **Verwerfen**: die Korrektur fällt weg, es gilt wieder das Abgeleitete.
- Die Modellzeile doppelt den Hersteller nicht mehr: aus „FRITZ! FRITZ!Box 7590"
  wird „FRITZ!Box 7590".

### Technik

- `PUT /api/geraete/entitaet`, `PUT /api/geraete/{schluessel}`,
  `DELETE /api/geraete/{schluessel}/korrektur`
- Leer heißt beim Namen wie beim Eltern-Schlüssel **„nicht ändern"**; ein leerer
  Eltern-Schlüssel dagegen ausdrücklich „hängt an nichts". Sonst schriebe schon
  ein Aushängen den aktuellen Namen als Korrektur fest, und ein späteres
  Umbenennen in Home Assistant käme nie mehr an.

## 2.0.0-forkai.24

**Fork AI.** **Geräte & Entitäten** startet eingeklappt.

- Controller zeigen zunächst nur sich selbst; rechts steht als Pille, wie viele
  Geräte an ihnen hängen (RDWC: 4). Ein Tipp klappt sie auf.
- Die Unterzeile eines Controllers nennt jetzt Rolle und Modell — „Controller ·
  AC Infinity UIS Controller AI+ (CTR89Q)" — statt der Zahl, die in der Pille steht.
- Hersteller und Modell kommen aus dem HA-Geräteregister.

## 2.0.0-forkai.23

**Fork AI.** Kleine Korrektur auf **Geräte & Entitäten**: Der Pfeil an einem
Controller klappt jetzt seine **angeschlossenen Geräte** auf und zu — vorher
zeigte er dessen eigene Entitäten, wovon ein Controller oft gar keine hat. Bei
acht Ports am RDWC ist das Zuklappen der eigentliche Sinn der Zeile. Die
Unterzeile nennt die Zahl („4 angeschlossene Geräte"); die Entitäten eines
Ports klappen weiter an der Portzeile selbst auf.

## 2.0.0-forkai.22

**Fork AI.** Neue Seite **Geräte & Entitäten** — alles, was der Fork an Home
Assistant benutzt, an einer Stelle.

### Was Sie sehen

- Menü **Betrieb → Geräte & Entitäten**: eine Zeile je Gerät, Controller tragen
  ihre Ports eingerückt darunter. Ein Tipp klappt die Entitäten auf, und hinter
  jeder steht, wofür der Fork sie benutzt — „Messgröße EC", „Steuerung CO2 ·
  Licht-Status", „Dosierpumpe pH−".
- Die Einheit ist das **Gerät**, nicht die Entität: der Bluelab Guardian ist
  eine Zeile mit pH, EC und Wassertemperatur darin, nicht drei. Geräte ohne
  Entität (CO₂-Flasche, Verschleißteile) stehen trotzdem in der Liste.
- Eingesammelt wird aus sechs Quellen: Messgrößen des Zelts, Zelt-Technik,
  Inventar, Dosierpumpen, Steuerungs-Rollen, Stromzähler.
- Namen und Hierarchie kommen aus Home Assistant. Heißt der Controller dort
  „RDWC", heißt er hier auch so — die alten Entity-IDs (`klein_abluft_…`)
  spielen keine Rolle mehr.

### Was noch nicht

Die Seite **liest nur**. Geändert wird weiter dort, wo es heute steht; die
Marke hinter jeder Entität sagt, wo das ist. Reiter je Gerät (Entitäten,
Wartung, Verschleiß) folgen.

### Technik

- `GET /api/geraete`; Register über `/api/websocket`
  (`config/entity_registry/list`, `config/device_registry/list`)
- Neue Tabellen `ForkGeraete` und `ForkGeraetEntitaeten` für die Korrekturen
  des Nutzers — leer, solange nichts korrigiert wurde
- Reihenfolge der Wahrheit: Zuordnung des Nutzers, dann `device_id` /
  `via_device_id` von Home Assistant, dann die Namensvermutung. Ist der Socket
  nicht erreichbar, wird es gröber, aber nichts bricht

## 2.0.0-forkai.21

**Fork AI.** Die Steuerungen fragen ihre Geräte jetzt nach, statt sie fest im
Code zu haben.

### Was Sie sehen

- Neue Seite **Steuerung → Geräte & Entitäten**: je Regelung eine Liste ihrer
  Rollen — „CO₂-Sensor", „Dosier-Steckdose · Zustand", „Abluft T6 · Stufe",
  „Licht-Status" — und dahinter ein Suchfeld. Tippen schlägt passende Entitäten
  aus Home Assistant vor, gefiltert auf die Domain, die zur Rolle passt; rechts
  steht der aktuelle Wert, damit sofort sichtbar ist, ob die richtige Entität
  dranhängt.
- **Eigene Geräte**: frei benannte Einträge wie „Zuluft Zelt" oder „Außenluft ·
  Temperatur". Eine Rolle kann mit `@Zuluft Zelt` darauf verweisen. Wird das
  Gerät getauscht, ändert sich eine Zeile — alle Regelungen ziehen mit.
- Die **CO₂-Seite** zeigt oben, wie viele Rollen belegt sind, und führt mit
  einem Tipp zur Zuordnung. Geändert wird nur dort, damit ein Gerät genau eine
  Wahrheit behält.
- Nichts müssen Sie eintragen: jede Rolle startet mit der Entität, die vorher
  im Code stand. Nach dem Update läuft alles unverändert weiter.

### Technik

- Neue Tabelle `ForkSteuerungGeraete` (Modul, Rolle, EntityId); Rollen-Registry
  in `Models/SteuerungGeraet.cs`, Auflösung in `Services/SteuerungGeraeteService.cs`.
- `GET/PUT /api/steuerung/geraete`, `POST/DELETE /api/steuerung/geraete/eigene`.
  `GET /api/steuerung/co2` liefert zusätzlich `geraeteZugeordnet`/`geraeteGesamt`.
- Ein Verweis, der ins Leere zeigt, gilt als nicht zugeordnet und wird **nicht**
  still durch die Vorgabe ersetzt — sonst schaltete eine Regelung heimlich auf
  ein anderes Gerät um.
- Die Sollwert-Helfer (`input_number.co2_*`), die abgeleiteten Sensoren und die
  Automation selbst bleiben Konstanten: die legt die Steuerung an, sie sind kein
  Gerät des Nutzers.

## 2.0.0-forkai.20

**Fork AI.** Neuer Menüpunkt **Steuerung** — die CO₂-Begasung wird jetzt hier
eingestellt, nicht mehr im Grow-Dashboard von Home Assistant.

### Was Sie sehen

- Ein sechstes Ziel in der Leiste: **Steuerung** (⊚), vor „Mehr". Die
  Übersicht zeigt je Regelung eine Zeile mit Status-Punkt und aktuellem Wert;
  ein Tipp öffnet die Detailseite.
- **Steuerung → CO₂-Begasung** mit Statuskarte oben (ppm, Ziel, Ventil, Klima,
  T6-Stufe, Canopy, RH, VPD), einer Chip-Leiste zum Wechseln zwischen den
  Steuerungen und fünf Reitern: **Ziel · Dosierung · Klima · Zeiten · Heute**.
- Das Ziel steht auf **Fest** (drei ppm-Werte je Canopy-Bereich) oder auf
  **Plan** — dann ist es ein Prozentanteil des CO₂-Bands der laufenden Phase,
  Vorgabe 55 / 70 / 80 %.
- Im Reiter **Heute**: Impulse, Ventilzeit und Gramm der letzten 14 Tage. Wer
  einen Kosten-Artikel wählt, bekommt die Tagessumme abends dorthin gebucht und
  einen Eintrag in die Chronik.

### Warum

Die CO₂-Regelung ist über Wochen in Home-Assistant-Helfern gewachsen. Zwanzig
`input_number` in einer Dashboard-Kachel sagen nicht, welcher Wert wovon
abhängt. Der Fork kennt Phase, Wochenplan und Kosten-Artikel — hier lässt sich
das Ziel an den Plan hängen und der Verbrauch dorthin buchen, wo er hingehört.

**Geregelt wird weiterhin in Home Assistant.** Ein Ventil an einer Gasflasche
darf nicht davon abhängen, ob ein Web-Add-on gerade neu startet. Der Fork
besitzt die Sollwerte und schreibt sie in die bestehenden Helfer; fällt er aus,
dosiert HA mit den zuletzt geschriebenen Werten weiter.

### Drei Dinge, die dabei geradegezogen wurden

- **Start- und Endzeit sind keine Attrappen mehr.** Sie standen fest in der
  Automation (15 Minuten, 16:30 Uhr). Jetzt gehen sie als Helfer
  (`co2_start_nach_licht_an`, `co2_ende_vor_licht_aus`) nach Home Assistant, und
  die Automation rechnet ihr Fenster daraus gegen die geplante Aus-Zeit des
  Licht-Controllers.
- **Das Planziel heißt jetzt, was es ist.** Es kommt aus dem Sollwertprofil der
  **Phase**, nicht aus einer Wochenspalte — das Feedchart hat keine CO₂-Spalte
  je Woche. Die Seite schreibt das hin, statt eine wöchentliche Änderung zu
  versprechen, die es nicht gibt.
- **Ein Flaschenwechsel kostet nicht mehr den ganzen Tag.** Der Verbrauch kommt
  aus dem Fall von `co2_flasche_rest`; wechselt man mittags die Flasche, springt
  der Helfer nach oben. Bisher wurde die negative Differenz auf null gedeckelt —
  Tagesverbrauch weg, Buchung weg. Jetzt bleibt das Gezählte stehen und die
  Zählung läuft ab dem neuen Stand weiter.

### Für Entwickler

- Neu: `SteuerungApiController` (`GET /api/steuerung`, `GET|PUT
  /api/steuerung/co2`), `Co2SteuerungService`, `Co2SyncWorker` (Tageslauf alle
  2 min, Sollwerte stündlich), `SteuerungRepository` mit
  `ForkSteuerungEinstellungen` und `ForkCo2Tage`.
- `NavBarApiController.MaxItems` von 5 auf 6 — damit „Steuerung" neben den fünf
  Bewährten Platz hat.
- `GeltendeZieleApiController.StageLabel` ist öffentlich, damit die Steuerung
  dieselbe Phasen-Schreibweise nennt statt einer zweiten Übersetzung.
- Referenz: `docs/referenz/steuerung.md`.

## 2.0.0-forkai.19

**Fork AI.** Der Wochenplan sagt jetzt, wenn er seine letzte Spalte hält.

### Was Sie sehen

- Im Kopf „Gilt gerade" steht hinter der Plan-Spalte ein Zusatz, sobald die
  Phase über das Chart hinausläuft: *gehalten seit Woche 5 — du bist in Woche 8
  dieser Phase*.

### Warum

Wer die Vegi streckt, damit die Pflanzen die Fläche zuwachsen, kommt über die
letzte Vega-Spalte des Feed-Charts hinaus. Der Plan bleibt dann auf dieser
Spalte stehen — fachlich richtig, aber stumm: auf dem Bildschirm stand weiter
„Vega W4", und es war nicht zu unterscheiden, ob der Plan noch greift oder
hängt. Die Zählung kommt aus derselben Rechnung wie die Spaltenwahl
(`MischplanService.WocheInPhase`), damit nicht zwei Stellen verschieden zählen.

## 2.0.0-forkai.18

**Fork AI.** Die Sollwert-Profile sagen jetzt, was davon bei Ihnen wirklich gilt.

### Was Sie sehen

- Über der Profilliste steht für jeden laufenden Grow eine Zeile **„Gilt gerade"**:
  welches Profil greift und woher es kommt (am Grow gewählt, vom System geerbt
  oder aus dem Anbaustil), die aktuelle Phase und — falls der Grow die
  Wochen-Ziele seines Düngeprogramms benutzt — dessen aktuelle Spalte.
- Darunter die Zielwerte als Chips, genau die Zahlen, die auch auf den Kacheln
  stehen. **Blau** ist, was aus dem Wochenplan kommt und nicht aus dem Profil.
- Beim pH steht zusätzlich, worauf das Chart anmischen lässt — der Punktwert,
  gegen den gemessen wird der Handlungsbereich.

### Warum

Die Seite zeigte Werte je Phase und sah damit aus wie die letzte Instanz. Sie ist
aber nur der erste von drei Schritten: darüber legt das Feed-Chart wochenweise
EC und pH, und ganz oben stehen die eigenen Grenzwerte des Zelts. Wer im Profil
„Blüte: EC 1,0–1,2" las und im Messprotokoll gegen 1,1–1,3 bewertet wurde, suchte
den Fehler an der falschen Stelle. Die neue Zeile rechnet nichts eigenes: sie
fragt dieselben zwei Stellen wie Kacheln, Messprotokoll und Planziel-Alarme
(`Zielband.FuerGrow`, `Zielband.FuerMetrik`) — eine zweite Rechnung wäre genau
die zweite Auskunft, die dort abgeschafft wurde.

## 2.0.0-forkai.17

**Fork AI.** Die Einstellungen sind am Telefon wieder erreichbar.

### Was Sie sehen

- Im Menü **Mehr** steht jetzt gleich oben, neben „Leiste anpassen", der Punkt
  **Einstellungen**.

### Warum

Seit der neuen Navigation traegt die Icon-Leiste die Hauptfunktionen, und die
Seitenleiste blendet sich am Telefon aus. Der Verweis auf die Einstellungen hing
aber unten in genau dieser Seitenleiste — am Telefon kam man also nur noch ueber
die getippte Adresse hin. Aufgefallen ist es beim Umstellen des Ziels fuer das
„⌂"-Zeichen, das genau dort eingetragen wird. Der Punkt steht bewusst nicht in
den Menuegruppen: aus denen speist sich die anpassbare Icon-Leiste, und die soll
die Grossfunktionen tragen, nicht die Verwaltung. Ein Test haelt den Weg fest.

## 2.0.0-forkai.16

**Fork AI.** Das „⌂"-Zeichen bleibt in der Home-Assistant-App.

### Was Sie sehen

- Der Rücksprung aus Grow OS ins Dashboard öffnete auf dem Telefon den **Browser**
  statt die Ansicht in der Home-Assistant-App zu wechseln. Man stand danach in
  Firefox vor derselben Oberfläche, nur ohne den Weg zurück.
- Jetzt wechselt Home Assistant die Ansicht wie bei einem Klick in der Seitenleiste —
  man bleibt in der App.

### Warum

Der Knopf setzte die Adresse des obersten Fensters. Für einen Browser ist das ein
gewöhnlicher Seitenwechsel; die Android-App von Home Assistant behandelt jeden
Seitenwechsel dagegen als Verweis nach draußen und reicht ihn an den Standardbrowser
weiter. Grow OS meldet den Wechsel deshalb jetzt dem Frontend (`history.pushState` +
Ereignis `location-changed`), statt eine neue Seite zu laden. Läuft Grow OS nicht im
Rahmen von Home Assistant — eigener Tab, fremder Proxy —, bleibt der alte Weg als
Rückfallebene. Ein Test hält die Stelle fest.

## 2.0.0-forkai.15

**Fork AI.** Die Navigationsleiste ist in der hellen Ansicht wieder lesbar.

### Was Sie sehen

- Der **aktive Punkt in der Leiste** am oberen Rand war in der hellen Ansicht zu blass:
  gemessener Kontrast 3,18 — die Schwelle für Fließtext liegt bei 4,5. In der dunklen
  Ansicht war er nie betroffen. Jetzt sind es 5,49 im hellen und 12,88 im dunklen Thema.
- Dasselbe galt für die beiden Zeichen im Erfassen-Blatt und im Anpassen-Modus.

### Warum

Für die Fläche eines Knopfes und für ein Wort braucht es zwei verschiedene Grüntöne — ein
helles Grün, das als Fläche gut aussieht, ist als Schrift auf hellem Grund zu schwach.
Genau diese Verwechslung steckt in der Geschichte dieser Leiste schon ein zweites Mal: beim
ersten Mal war der Kontrast 1,15. Die Linie unter dem aktiven Punkt darf blass bleiben, das
Wort darüber nicht.

### Unter der Haube

- Vier Formulare der Kosten-Seite (Strom-Quelle, Artikel, Nachfüllung, Anschaffung) hatten
  seit ihrer Entstehung in forkai.6 bis forkai.9 **keinen einzigen E2E-Fall**. Der Wächter
  meldete das die ganze Zeit; aufgefallen ist es erst, als der Lauf nicht mehr vorher am
  Lint abbrach. Jetzt füllt ein Rundweg jedes der vier aus, prüft den abgeschickten Rumpf
  (auch, dass aus „36,75“ die Zahl 36.75 wird und nicht 3675), lädt neu und liest nach.
- Der Rundweg für die Strom-Quelle stellt den vorher eingetragenen Zähler am Ende wieder
  her — sonst zeigte die Kosten-Seite der laufenden Anlage danach auf eine Entität, die es
  nicht gibt.

## 2.0.0-forkai.14

**Fork AI.** Alarmgrenzen können jetzt dem Wochenplan folgen — und ein Bugfix an der neuen Navigation.

### Was Sie sehen

- Unter **Betrieb → Regeln & Automatik → Grenzwerte** hat jede Zeile eine neue Spalte **Quelle**:
  *Fest* sind Ihre eingetragenen Zahlen wie bisher, *Plan* nimmt das Zielband der laufenden
  Phase beziehungsweise Woche und erweitert es um eine **Toleranz**.
- Bei *Plan* stehen „Warnen unter/über“ nur noch zur Ansicht da: dort zeigt die Seite, was
  gerade daraus wird. Getippt wird nur die Toleranz.
- Die Push-Nachricht nennt bei einer Plan-Regel dazu, woher die Grenze kommt — zum Beispiel
  „SKX Canna Aqua · Flores · Woche 3“. Eine Zahl, die Sie nirgends eingetragen haben, soll
  nachts nicht ohne Absender auf dem Handy stehen.

### Warum

Wer nach einem Feed-Chart fährt, bewegt sein Ziel jede Woche. Das SKX-Chart nennt in Blüte
Woche 3 ein EC-Ziel von 1,2 und in Woche 6 eines von 1,6. Eine feste Obergrenze von 1,2 hätte
ab Woche 4 täglich gemeldet, obwohl planmäßig gefüttert wurde — und wer wochenlang falschen
Alarm bekommt, glaubt auch dem echten nicht mehr.

### Was gleich bleibt

- **Bestehende Grenzwerte ändern sich nicht.** Alle Regeln starten auf *Fest*; erst ein
  Umschalten ändert etwas.
- *Plan* gibt es nur für Messgrößen, für die der Plan überhaupt einen Wert kennt (pH, EC,
  Wassertemperatur, ORP, VPD, CO₂, PPFD). Luftfeuchte, Sauerstoff und Wasserstand behalten
  feste Grenzen.
- Läuft im Zelt kein Grow — oder trocknet es gerade —, schweigt eine Plan-Regel, statt gegen
  ein Ziel zu melden, das es nicht gibt.
- Die Nachtabsenkung ist eingerechnet: fährt die Rampe die Wassertemperatur planmäßig nach
  unten, ist das kein Alarm.

### Unter der Haube

- Zielband und Alarm benutzen dieselbe Lesart je Messgröße (`Zielband.FuerMetrik`) — beim pH
  den Handlungsbereich, bei der Wassertemperatur den Arbeitsbereich. Vorher lag diese
  Umrechnung nur im Dashboard.
- Plan-Regeln zählen ausdrücklich nicht als „eigene Grenze“ des Nutzers, sonst legte sich das
  Zielband über sich selbst und jede Toleranz wäre nach einem Durchlauf Teil des Ziels.
- Zwei neue Spalten in `TentAlertRules` (Quelle, Toleranz) mit Vorgabe *Fest*.

### Bugfix

- **Navigationsleiste:** Der Umbau aus forkai.13 hatte das Speichern des Rücksprung-Pfads halb
  fertig hinterlassen — der Endpunkt ließ sich nicht übersetzen und wies jeden Aufruf ohne
  Reihenfolge ab. Beide Felder lassen sich jetzt einzeln speichern, ohne das andere zu
  verlieren.

## 2.0.0-forkai.13

**Fork AI.** Die Navigation am Telefon ist neu — Titelzeile, Icon-Leiste, ein Weg zum Eintragen.

### Was Sie sehen

- **Oben eine Titelzeile** mit dem Namen und der Seite, auf der Sie gerade sind. Rechts daneben
  zwei Zeichen: das grüne **+** öffnet das Erfassen-Blatt, das **⌂** springt zurück in die normale
  Home-Assistant-Ansicht. Beide sind auf **jeder** Seite da — bisher lag „Messung erfassen“ nur auf
  der Startseite.
- **Erfassen an einer Stelle**: Messung, Addback, Wasserwechsel, Notiz und Kosten stehen im Blatt
  hinter dem **+**. Dafür sind „Messen“, „Addback“ und „Wasserwechsel“ aus der Leiste verschwunden —
  sie standen dort doppelt, als Reiter und als Knopf.
- **Die Leiste zeigt Zeichen statt langer Wörter** und lässt sich anpassen: unter **Mehr →
  „Leiste anpassen“** bestimmen Sie, welche bis zu fünf Ziele oben stehen und in welcher
  Reihenfolge. Die Einstellung liegt im Server, gilt also am Telefon und am Rechner gleich.
- **Behoben:** Die Reiterzeile brach in eine zweite Zeile um (fünf Ziele, vier Spalten), während der
  Seitenanfang weiter mit einer Zeile rechnete. Dadurch lag die obere Hälfte der Bewertungsscheibe
  unter der Leiste. Die Leiste richtet sich jetzt nach der Zahl ihrer Ziele.
- **Neu unter Einstellungen → Darstellung:** wohin das ⌂ springt (Voreinstellung `/lovelace/0`).

### Ohne HA-Kopfleiste

Wer die weiße Home-Assistant-Leiste über der App loswerden will, installiert die HACS-Integration
**Ingress** (`lovelylain/hass_ingress`) und trägt einen kurzen Block in die `configuration.yaml` ein —
Anleitung in `FORK.md`. Das ist freiwillig: ohne die Integration funktioniert alles genauso, die
Titelzeile lässt dann nur ihren Namenszug weg, damit er nicht zweimal untereinander steht.

## 2.0.0-forkai.12

**Fork AI.** Kosten: Vorschläge für Hersteller und Produkt stehen jetzt direkt unter dem Feld.

- Statt der Browser-Vorschlagsliste (die am Telefon unten über der Tastatur landete) erscheint
  beim Tippen eine kleine Liste unmittelbar unter dem Eingabefeld; ein Tipp übernimmt den
  Eintrag. Am Rechner: ↓/↑ wählt, Enter übernimmt, Esc schließt.
- Beim Produkt bringt ein Vorschlag seinen Hersteller mit, wenn der noch leer ist.

## 2.0.0-forkai.11

**Fork AI.** Kosten: Hersteller und Produkt werden beim Tippen vorgeschlagen.

### Was Sie sehen

- In „Artikel anlegen“, „Bearbeiten“ und „Anschaffung erfassen“ schlagen die Felder **Hersteller**
  und **Produktbezeichnung** beim Tippen vor, was schon im Bestand steht — aus Verbrauchsartikeln,
  Anschaffungen und der Hardware-Liste (Sensoren & Wartung). Produkte werden nach dem getippten
  Hersteller gefiltert.
- Beim Speichern gleicht Grow OS die Schreibweise an: „canna“ oder „Canna “ wird zu dem
  „Canna“, das es schon gibt. Ein unbekannter Hersteller bleibt, wie Sie ihn getippt haben —
  die erste Schreibweise gilt danach als die richtige.

### Technik

- `GET /api/kosten` liefert `hersteller` und `produkte` (Hersteller + Produkt, dublettenfrei,
  Groß/Klein ignoriert). `Stammdaten.Angleichen` im Backend, Vorschläge über natives `<datalist>`.

## 2.0.0-forkai.10

**Fork AI.** Kosten: der erste Tipp auf „Artikel anlegen“ (oder einen der anderen beiden Knöpfe)
springt jetzt zuverlässig zum Formular — auch wenn dabei der Reiter wechselt. Bisher kam der
Sprung einen Zug zu früh und landete im Leeren; erst der zweite Tipp traf. Außerdem rollt das
Formular am Telefon unter die feste Kopfleiste statt dahinter (`scroll-ziel`).

## 2.0.0-forkai.9

**Fork AI.** Die Kosten-Seite bekommt Reiter, Anschaffungen und eine Grow-Zuordnung.

### Was Sie sehen

- **Reiter statt langer Seite**: Strom · Verbrauch · Anschaffungen · Durchgänge, wie auf
  „Regeln & Automatik“. Kachel und KPI-Leiste bleiben oben stehen. Der Reiter steht in der
  Adresse (`/kosten?tab=verbrauch`).
- **Drei Knöpfe oben**: Artikel anlegen, Nachfüllung erfassen, Anschaffung erfassen — ein Tipp
  wechselt auf den Reiter und öffnet das Formular direkt unter der Reiterleiste. Mehrere dürfen
  offen sein, ▴ schließt.
- Neu — **Anschaffungen**: Werkzeug, Technik, Zubehör mit Datum, Stück, Einzelpreis, Hersteller
  und Produkt. Zählt einmal im zugeordneten Grow; „Lager“ zählt nirgends. Auf Wunsch entsteht
  dazu ein Hardware-Artikel unter Sensoren & Wartung und ein Journal-Eintrag. Die Kopfzahl und
  die Aufteilung zeigen Anschaffungen als dritte Farbe.
- **Für Grow**: Nachfüllungen und Anschaffungen lassen sich einem laufenden Grow oder dem Lager
  zuordnen; der Journal-Eintrag folgt der Auswahl.
- Verbrauchsartikel: **Einheit als Auswahl** (kg, g, L, ml, Stück), „Gebinde“ heißt jetzt
  **Inhalt je Packung**, der Preis **Preis je Packung**.

### Technik

- Neue Tabelle `ForkAnschaffungen`; `GET /api/kosten` liefert `anschaffungen`, `einheiten` und
  in Summe/Durchgängen `anschaffungenEur`. `POST/PUT/DELETE /api/kosten/anschaffungen`.
- `POST /api/kosten/artikel` prüft die Einheit gegen die Liste; Nachfüllung/Anschaffung kennen
  `ohneGrow` für „Lager“.

## 2.0.0-forkai.8

**Fork AI.** Verbrauchsartikel bekommen Stammdaten.

### Was Sie sehen

- Ein Artikel hat jetzt **Anzeigename**, **Hersteller**, **Produktbezeichnung** und einen
  **Preis je Gebinde**. Gebinde und Preis belegen die Erfassung einer Nachfüllung vor —
  jede Füllung darf davon abweichen.
- Artikel lassen sich auf der Karte **bearbeiten**.

### Technik

- `ForkVerbrauchsartikel` bekommt die Spalten `Hersteller`, `Produkt`, `PreisEur`; das
  Repository zieht sie beim ersten Zugriff nach (`ALTER TABLE`, einmalig, ohne Datenverlust).
- `POST/PUT /api/kosten/artikel` nehmen die neuen Felder entgegen; `GET /api/kosten` liefert sie.

## 2.0.0-forkai.7

**Fork AI.** Die Kosten-Seite sieht jetzt aus wie der Rest der App.

### Was Sie sehen

- Die Zahlenreihen (Summe, Strom) sind dieselbe Hairline-Leiste wie im Grow-Detail — durchgehender
  Rahmen, Trennlinien zwischen den Kacheln, auch wenn sie am Telefon umbrechen.
- Abstände, Radien und Farben kommen nur noch aus den Tokens des Originals; helles und dunkles
  Thema stimmen mit den übrigen Seiten überein.
- Die Phasentabelle wird am Telefon nicht mehr am rechten Rand abgeschnitten.

## 2.0.0-forkai.6

**Fork AI.** Neue Seite **Kosten** unter Betrieb: Strom vom Zähler und alles, was nachgekauft wird.

### Was Sie sehen

- Neu — **Kosten je Durchgang.** Gesamtsumme seit Grow-Start, Aufteilung Strom/Verbrauchsartikel,
  Ø je Tag, je Pflanze und eine Prognose bis zur Ernte (Flip + Züchter-Blütewochen).
- Neu — **Strom vom kWh-Zähler.** Sie wählen die Home-Assistant-Entität des Gesamtzählers (z. B. die
  DECT-Steckdose vor dem Zelt); Grow OS hält den Stand bei Grow-Start, jedem Phasenwechsel und
  täglich fest und rechnet kWh und Euro je Grow und je Phase. Ein Zähler-Reset wird erkannt.
- Neu — **Verbrauchsartikel** (CO₂-Flasche, Dünger, pH-Down …) mit Nachfüllungen: Datum, Menge,
  Preis. Aus der Laufzeit der vorherigen Füllung entsteht die Prognose „leer ≈“ für die laufende,
  dazu Euro je Tag. Beim Erfassen wird die alte Füllung auf Wunsch als leer geschlossen und ein
  Journal-Eintrag im Grow angelegt.
- Der Strompreis bleibt, wo er war: Einstellungen → Kosten (derselbe Wert wie im Archiv).

### Technik

- Drei eigene Tabellen (`ForkVerbrauchsartikel`, `ForkNachfuellungen`, `ForkZaehlerstaende`), vom
  Repository selbst angelegt — das Kern-Schema des Originals bleibt unberührt.
- Neuer Hintergrunddienst `ZaehlerstandWorker` (Takt 10 min; liest nur, wenn eine Quelle gewählt ist).
- Neue API `/api/kosten` (Seite, Strom-Quelle, Entität prüfen, Zählerstände, Artikel, Nachfüllungen).
- Referenz: `docs/referenz/kosten.md`.

## 2.0.0-forkai.5

**Fork AI.** Der Wochen-Feed-Chart eines Düngeprogramms ist jetzt auf der Wissensseite sichtbar.

### Was Sie sehen

- Neu — **Feed-Chart als Tabelle.** Unter Wissen → Programme zeigt ein Programm mit Feed-Chart
  (SKX Canna Aqua, Athena Blended) den Abschnitt „Feed-Chart (je Woche)“: Spalten je Woche,
  Zeilen je Komponente in ml je Liter, darunter Ziel-EC und Ziel-pH. Auf dem Handy seitlich scrollbar.

### Technik

- Das Programm-DTO der Wissens-API (`/api/knowledge`) trägt neu das optionale Feld `feedChart`;
  Programme ohne Chart liefern `null`, sonst ändert sich an der API nichts.

## 2.0.0-forkai.4

**Fork AI.** Das Düngeprogramm heißt jetzt kurz **„SKX Canna Aqua“** (statt „SKX Canna Aqua (R/DWC Growplan Rev.01)“). Die Quelle steht weiterhin in der Beschreibung.

## 2.0.0-forkai.3

**Fork AI.** Ein Anzeigefehler auf der Addback-Seite.

### Was Sie sehen

- Behoben — **Addback zeigte das falsche Programm an.** Stand am Grow „SKX Canna
  Aqua", meldete die Karte „Programm" trotzdem „Canna Aqua", weil die Seite das
  Programm per Teilstring aus dem Freitext riet. Der Mischplan rechnete die ganze
  Zeit richtig (er nutzt die gespeicherte Programm-Id) — nur die Anzeige und die
  Vorauswahl im Formular waren falsch. Jetzt gilt die Id zuerst, der Freitext nur
  als Rückfall, und ein exakter Name schlägt einen Teilstring-Treffer.

## 2.0.0-forkai.2

**Fork AI.** Neues Düngeprogramm **„SKX Canna Aqua (R/DWC Growplan Rev.01)"**
unter Wissen → Düngeprogramme. Das bestehende „Canna Aqua" bleibt unverändert.

### Was Sie sehen

- Neu — **Wochen-Feed-Chart nach SKX:** 14 Spalten (Root, Vega 1–4,
  Flores 1–8, Flush) mit CalMag Agent, Aqua Vega/Flores A+B, Rhizotonic,
  Cannazym, PK 13/14 und Cannaboost in ml je Liter (Plan: ml je 10 L ÷ 10),
  dazu Ziel-EC und Ziel-pH je Woche.
- Neu — die Regeln aus den Fußnoten des Plans als Text: Addback ±0,1 EC,
  pH-Korrektur erst außerhalb 5,5–6,5, DWC +30 %, Strain-EC-Korridore,
  CO₂/PPFD-Hinweise, CalMag-Blattdüngung, Flush-Kriterien.

### Technik

- Datei `knowledge-defaults/nutrient-programs/skx-canna-aqua.json`; wird beim
  Start automatisch in die Wissensdatenbank übernommen, eigene Anpassungen
  bleiben erhalten.

## 2.0.0-forkai.1

**Fork AI.** Erster Stand des Ablegers von Grow OS (Nerdstreak, MIT).
Läuft als eigenes Add-on **„Grow OS Fork AI"** parallel zum Original.

### Technik

- Eigener Add-on-Slug `grow_os_fork_ai`, eigenes Image
  `ghcr.io/bru-u-kno/grow-operation-system_fork_ai`, eigenes Add-on-Repository.
- Keine funktionalen Änderungen gegenüber 2.0.0-beta.65.

## 2.0.0-beta.65

**Beta.** Ein Aufräum-Release. Die App hatte Endpunkte, die niemand ruft, Code,
den niemand erreicht, und ein Protokoll, das niemand lesen kann. Nichts davon
war ein Absturz — und genau deshalb stand es jahrelang da. Was Sie merken:
weniger Angriffsfläche, eine reparierte Kamera-Anzeige und zwei Formulare, die
nicht mehr stillschweigend Eingaben wegwerfen.

### Was Sie sehen

- Behoben — **die Kamera auf der Zelt-Seite war tot.** Dort stand dauerhaft
  „Kamera — Nicht eingerichtet", auch wenn die Kamera in Home Assistant sauber
  eingetragen war: die Adresse wurde über einen Weg gebaut, den es nicht mehr
  gab. Auffällig war das nur an einer <i>echten</i> Anlage — ohne Home
  Assistant greift die Bedingung davor gar nicht.

- Behoben — **Sollwert-Profile warfen Eingaben still weg.** Wer eine Phase
  deutsch schrieb („Blüte" statt „Flower") oder sich vertippte („ecMinimum"
  statt „ecMin"), bekam „Gespeichert" und ein leeres Profil. Beim nächsten
  Öffnen waren die Zahlen weg, ohne ein Wort. Jetzt steht da, was möglich
  gewesen wäre.

- Behoben — **eine gelöschte Erinnerung nahm die Kalibrierung mit ins Nichts.**
  Wer die Erinnerung in der Aufgabenliste löschte, behielt einen Kalibrier-
  Vorgang, der auf nichts mehr zeigte — und weil eine Erinnerung nur beim
  Anlegen entsteht, bekam er <b>nie wieder</b> eine.

- Neu — **die Chronik eines Grows lässt sich lesen.** Die App sammelt seit
  Monaten, was wann geändert wurde: Grow angelegt, Messung gespeichert, Flip
  12/12. Nur kam niemand daran — es gab keinen Leseweg. Jetzt gibt es einen
  (<code>/api/grows/{id}/chronik</code>). Einen Knopf dafür gibt es bewusst
  nicht: eine Chronik liest man nicht täglich, sondern wenn etwas passiert ist.

### Weniger Angriffsfläche

- Behoben — **ein Endpunkt gab die LAN-Adressen des Rechners heraus.**
  <code>/api/system/network</code> antwortete jedem mit den privaten IPv4-
  Adressen der Maschine — und wurde von nichts benutzt. Sein Nachfolger war
  längst da.

- Behoben — **der Kamera-Weg lag ungeschützt.** Geschützt waren nur drei alte
  Kamera-Adressen; der Weg, den die Oberfläche wirklich nimmt — und über den
  die Bilder aus Ihrem Zelt gehen — stand nicht in der Liste.

- Behoben — **vierzehn Endpunkte, die niemand ruft.** Reste aus dem Umbau von
  der alten Oberfläche auf die neue. Einer davon rechnete bei jedem Aufruf
  über alle Zelte und fragte dabei Home Assistant ab.

### Und im Maschinenraum

- Behoben — **139 unerreichbare Zeilen mit acht pH- und EC-Zahlen für Erde.**
  Die Zahlen sahen aus wie fachliche Wahrheiten; die App sagt zu Erde nichts.
  Eine Zahl ohne Wirkung ist eine Behauptung ohne Deckung.

- Neu — **der Dosiertakt hält seine drei Regeln jetzt nachweisbar ein**: erst
  Dünger dann pH (Dünger verschiebt den pH von selbst), eine Dosis je Zelt und
  Takt, und die zweite Hälfte eines A/B-Düngers geht nicht in stehendes Wasser.
  Die Regeln standen bisher nur als Kommentar in einer Methode, die man nicht
  prüfen konnte.

- Neu — **Prüfungen, die von selbst finden, was liegen bleibt**: jeder Endpunkt
  braucht einen Aufrufer, jeder Verweis muss auf eine Aktion zeigen, die es
  gibt, und das API-Verzeichnis darf nichts nennen, was gelöscht wurde. Alle
  drei haben beim Bauen sofort etwas gefunden.

## 2.0.0-beta.64

**Beta.** Fünf Stellen fragten die falsche Quelle nach der Wachstumsphase —
darunter der Wächter, der Nachrichten aufs Telefon schickt, und die Diagnose,
die dadurch der Kachel widersprach. Dazu vier Fehler, die erst auffielen, als
in den Prüfungen aufgeräumt wurde.

### Die Phase kam von der falschen Stelle

Die App rechnet die Wachstumsphase aus dem Grow: Flip-Datum, Samentyp, geplante
Veg-Dauer, „Finish beginnt". Genau das steht in der Kopfzeile. Fünf Stellen
nahmen sie stattdessen von der <b>letzten Messzeile</b> — und was dort steht,
beschreibt <i>diese</i> Messung, nicht den Grow von heute.

- Behoben — **die Diagnose widersprach der Kachel.** Am selben Grow, in
  derselben Minute: die Kachel sagte „EC 1,03 im Ziel (1,00–1,20)", die
  Diagnose darunter „EC 1,03 ausserhalb (0,20–0,40)". Eine einzige Messzeile
  mit alter Aufschrift reichte.

- Behoben — **der Urlaubswächter urteilte gegen die falschen Bänder.** Wer im
  Juli von Hand gemessen, im August geflippt und danach die Sensoren machen
  lassen hat, bekam wochenlang Warnungen gegen Veg-Bänder — mitten in der
  Blüte. Im belegten Fall wird aus einer EC-Drift dadurch eine <b>Warnung</b>
  statt einer Randnotiz, und die geht aufs Telefon.

- Behoben — **das Messformular schlug die alte Aufschrift vor**, und die App
  markierte dieselbe Zeile gleich darauf mit „≠ Blüte" als falsch. Wer den
  Vorschlag stehen liess, schrieb den Fehler fort.

- Behoben — **die Automessung schrieb die alte Phase weiter.** Jede
  automatisch erfasste Zeile übernahm die Aufschrift der vorherigen; nach einem
  Flip ohne Handmessung trug ab da <b>jede</b> denselben veralteten Wert.

- Behoben — **Zelt- und Hydro-Seite zeigten die Phase der letzten Messung**
  statt der von heute.

### Aufgaben und Erinnerungen

- Behoben — **eine gelöschte Kalibrierung liess ihre Erinnerung stehen.** Eine
  geplante Kalibrierung oder Wartung legt eine Aufgabe an — das ist gewollt.
  Wer sich vertippte und den Vorgang löschte, wurde die Aufgabe aber nie wieder
  los: sie hängte an nichts mehr und war über die Oberfläche nicht erreichbar.
  Am schwersten wog das beim Löschen eines ganzen <b>Geräts</b>: das nimmt alle
  seine Vorgänge auf einmal mit — und liess jede einzelne ihrer Erinnerungen
  zurück. Was bereits <i>abgehakt</i> ist, bleibt stehen: erledigt ist erledigt.

- Behoben — **umgekehrt genauso.** Wer die Erinnerung in der Aufgabenliste
  löschte, behielt eine Kalibrierung, die auf eine Aufgabe zeigte, die es nicht
  mehr gab — und weil eine Erinnerung nur beim <i>Anlegen</i> entsteht, bekam
  dieser Vorgang <b>nie wieder</b> eine. Er stand weiter als geplant da und
  erinnerte an nichts mehr.

### Und was sonst nicht stimmte

- Behoben — **die Geräte-Testseite brach ganz ab, statt eine Zeile zu melden.**
  Antwortete eine Entität nicht — dafür reicht eine Adresse ohne
  „http://" —, gab die Seite einen Serverfehler zurück. Dabei stand der
  erklärende Satz direkt daneben bereit. Jetzt fällt nur die betroffene Zeile
  aus, mit der Ursache dahinter.

- Behoben — **ein Zeitplan mit gleicher Ein- und Aus-Zeit ging an die echte
  Anlage.** 20:00 bis 20:00 wurde angenommen, und derselbe Aufruf zwang den
  Controller danach auf „Schedule". Was ein LED-Treiber oder eine Abluft daraus
  macht, entscheidet ab da das Gerät. „Durchgehend an" ist ein normaler Wunsch
  — nur schreibt man ihn nicht so, und die Seite sagt jetzt, wie stattdessen.

- Behoben — **„zuletzt geschrieben" verschwand nach einem einzigen
  Fehlversuch.** Die Nachtabsenkung sah genau <b>einen</b> Eintrag an. War Home
  Assistant beim letzten Mal kurz weg, stand die Angabe leer — obwohl die Rampe
  seit Wochen zweimal täglich zuverlässig schrieb. Wer prüfen wollte, ob die
  Steuerung arbeitet, fand die einzige Antwort darauf leer vor.

- Behoben — **die Nachtabsenkung meldete Erfolg für Eingaben, die sie wegwarf.**
  Zielgerät und Kühler hängen am Zelt; hat der Grow keines, fiel der ganze
  Block still aus — und die Antwort war trotzdem „Gespeichert.". Und jede
  Kennung wurde angenommen, obwohl der Kühler nur über eine Steckdose
  (<code>switch.…</code>) schaltet.

- Behoben — **der Fotospeicher legte an, was ihm gereicht wurde.** Geprüft
  wurde eine Ebene höher; eine Datei ohne Endung bekam still ein „.jpg"
  angehängt. Die Sperre liegt jetzt dort, wo geschrieben wird.

## 2.0.0-beta.63

**Beta.** Zwei Wünsche aus dem Betrieb — mehrere Messpunkte je Kalibrierung
und Werte im Diagramm zum Antippen — dazu fünf Alarme, die den Nutzer nicht
erreichten, und ein Durchgang durch die ganze Oberfläche.

### Die pH-Sonde hat mehr als einen Messpunkt

- Neu — **eine Kalibrierung trägt jetzt mehrere Punkte.** Eine pH-Sonde wird
  gegen pH 4,01 <i>und</i> 7,00 abgeglichen, manche nehmen zusätzlich 10,01.
  Bisher passte <b>ein</b> Punkt hinein; wer beide festhalten wollte, musste
  zwei Einträge anlegen, die nichts voneinander wissen.

- Neu — **die Steilheit steht jetzt da.** Sie ist die eigentliche Auskunft:
  ein einzelner Abgleich gegen pH 7,00 verrät über die Sonde nichts, denn
  eine tote Sonde lässt sich darauf genauso einstellen wie eine frische. Erst
  der Abstand zwischen zwei Punkten zeigt, ob sie noch spreizt. Gerechnet wird
  aus den Werten <b>vor</b> dem Abgleich — danach steht die Sonde per
  Definition richtig.

  Die Zahl erscheint schon beim Tippen, gestaffelt und mit Quelle:
  95–105 % gilt als gut, unter 85 % ist die Sonde fällig — eine Faustregel
  aus den Handbüchern gängiger Sonden, nicht aus dieser App.

### Werte im Diagramm ablesen

- Neu — **auf eine Stelle im Verlauf tippen zeigt den Wert dort.** Der nächste
  Tipp setzt ihn neu, ein Fadenkreuz zeigt, welche Stelle gemeint ist. Mit den
  Pfeiltasten lässt sich durch die Reihe gehen.

  <b>Am Telefon war das Diagramm bisher stumm</b> — dort gibt es kein
  Überfahren mit der Maus, also sah man eine Kurve und erfuhr keine Zahl.

### Fünf Alarme, die nicht ankamen

- Behoben — **der Lichteinbruch-Alarm war nachts stumm.** Er lief durch den
  Ruhezeit-Filter: ein Blütezelt fährt 12/12 mit Licht aus um 20:00, und die
  übliche Ruhezeit 22–07 überdeckt <b>neun der zwölf</b> Dunkelstunden. Der
  Alarm schwieg also genau dann, wofür es ihn gibt. Licht in der Dunkelphase
  ist in der Blüte kein Ärgernis, sondern der Weg zu Zwittern.

- Behoben — **ein zweiter Grow im Zelt schaltete denselben Alarm ganz ab.**
  Gefragt wurde nur der erste Grow; stand daneben eine später gesteckte
  Autoflower, galt Licht in der Nacht als normal — für das <i>ganze</i> Zelt.

- Behoben — **die Schonfrist der Umwälzpumpe blendete den Luftpumpen-Alarm
  mit.** Unter jeder Umwälz-Warnung riet die App selbst: „Wenn das Absicht ist
  (Intervall-Betrieb), stell die Schonfrist höher." Wer dem folgte und
  240 Minuten eintrug, verzögerte damit auch den Alarm für die
  <b>Luftpumpe</b> um vier Stunden — den Alarm, ohne den das Reservoir binnen
  Stunden sauerstoffarm wird. Eine eigene Schonfrist stellt den
  lebenswichtigen Alarm jetzt nur noch <i>schärfer</i>, nie stumpfer.

- Behoben — **der Urlaubswächter schwieg dauerhaft, wenn ein Push scheiterte.**
  Kippte der EC um 23:10 über das Band und stand die Ruhezeit auf 22–07, galt
  der Befund danach als gemeldet — und weil er sich nicht mehr änderte, kam
  die Nachricht <b>nie</b>.

- Behoben — **eine Lichtflanke ging bei einem Schreibfehler für immer
  verloren**, samt Historie, gelerntem Zyklus und Alarm.

### Und was sonst nicht stimmte

- Behoben — **das kalibrierte Volumen wurde doppelt gezählt.** Der Assistent
  misst das <i>ganze</i> System; die Anzeige rechnete das geschätzte
  Topfvolumen noch einmal drauf. Bei vier Töpfen à 20 L und gemessenen 160 L
  standen 240 L da — direkt nachdem der Nutzer nachgemessen hatte.

- Behoben — **Sollwert-Profile nahmen jede Zahl an.** pH-Min 6,5 mit pH-Max
  5,5 ging durch; danach ist <i>jede</i> pH-Messung „daneben", egal welcher
  Wert. Und die Wassertemperatur geht von dort an den echten Kühler im Zelt.

- Behoben — **drei Formfehler auf dem Schirm**, gefunden bei einem Durchgang
  über alle Seiten in beiden Themen: eine überschrift, die am Telefon
  abgeschnitten wurde, ohne dass die Seite rollte; „gemischt (…)" statt
  „gemischt (2)" in der Sorten-Kachel — weg war die Anzahl, also die Auskunft;
  und ein gerader Bindestrich im Aufgabentitel.

## 2.0.0-beta.62

**Beta.** Vier Fehler, die es <b>nur im Add-on</b> gibt — auf einem
Entwicklungsrechner können sie gar nicht auftreten. Der schwerste: <b>jede
Sicherung war beim nächsten Update weg</b>.

### /app ist nicht /data

Das Add-on läuft als Container: das Programm liegt unter <code>/app</code>, die
Daten unter <code>/data</code>. Nur <code>/data</code> überlebt ein Update und
liegt in den Sicherungen von Home Assistant. Zehn Stellen im Code rechneten den
Datenpfad aber aus dem <i>Programm</i>pfad aus — auf einem gewöhnlichen Rechner
ist das derselbe Ordner, hier nicht.

- Behoben — **die Sicherungen landeten im Container statt auf dem Datenträger.**
  Jede Sicherung lag in der Schreibschicht des Containers und war beim nächsten
  Add-on-Update gelöscht, ohne eine Meldung. Das galt auch für die
  Sicherheitskopie, die vor jedem Import angelegt wird — also für genau den
  Rückweg, den man braucht, wenn ein Import schiefgeht.

  <b>Bestehende Sicherungen sind davon nicht betroffen</b>, solange das Add-on
  seither nicht aktualisiert wurde; neue liegen ab jetzt richtig. Wer sichergehen
  will, lädt seine wichtigen Sicherungen einmal herunter und legt danach eine
  neue an.

- Behoben — **das Kamerabild im Zelt-Bildschirm gab immer 404.** Geschrieben
  wurde es nach <code>/data/snapshots</code>, gelesen aus
  <code>/app/App_Data/snapshots</code>. Der Rückfall auf das zuletzt
  gespeicherte Bild stand deshalb dauerhaft leer.

- Behoben — **die hinterlegte Home-Assistant-Konfiguration** wurde an einer
  Stelle gesucht, an der sie nicht liegt.

Dieselbe Klasse wie die Fotos, die bis beta.61 nie von der Platte verschwanden.
Die Wege stehen jetzt an <b>einer</b> Stelle, und eine Zählung hält das.

### Die Suche zeigte auf jedem Gerät andere Treffer

- Behoben — **das Wissen kam in der Reihenfolge des Dateisystems.** Die Suche
  zeigt höchstens fünf Treffer je Art. Welche fünf, entschied damit der
  Dateizugriff der jeweiligen Maschine: bei 13 Regeln, die „wasser“ enthalten,
  sah man fünf davon — und ein vorhandener Eintrag war schlicht nicht
  auffindbar, ohne dass man hätte sagen können warum. Geladen wird jetzt nach
  Kennung sortiert; die Wissensseite listet dadurch ebenfalls in fester
  Reihenfolge.

### Doppelte Aufgaben bei Kalibrierung und Wartung

- Behoben — **beim Anlegen einer Kalibrierung oder Wartung ging die
  Verknüpfung zur Aufgabe verloren.** Die App legt in dem Fall selbst eine
  Erinnerung an — neben der, die schon mitgegeben war. Es entstanden also
  jedes Mal zwei. Beim <i>Bearbeiten</i> wurde die Verknüpfung korrekt
  übernommen, nur beim Anlegen nicht.

- Behoben — **der Kalibrier-Assistent riet mitten im Lauf „bitte neu
  starten“.** Wer zu früh auf „voll“ drückte, bekam dieselbe Meldung wie
  jemand ohne offenen Lauf. Der schlechteste aller Ratschläge: sein Lauf war in
  Ordnung, er musste nur warten. Jetzt steht dort, worauf gewartet wird.

### Was diese Fehler gefunden hat

Zwei neue Zählungen, beide über eine <b>Grundmenge</b> statt über eine
handgeschriebene Liste:

- <b>Kein Mapping lässt ein Feld fallen.</b> Ein vergessenes Feld ist eine
  Zeile, die es <i>nicht gibt</i> — Testabdeckung kann diese Klasse Fehler
  grundsätzlich nicht sehen. Die Zählung füllt jede Quelle mit
  unterscheidbaren Werten und vergleicht 879 Feldpaare. Sie hat die doppelten
  Aufgaben oben gefunden.
- <b>Niemand rechnet sich den Datenpfad selbst aus.</b> Ein Fehler, den kein
  gewöhnlicher Test fangen kann, weil dort beide Wege zusammenfallen.

Dazu neun Fälle für den Kalibrier-Assistenten, der die Gerade schreibt, aus der
jeder spätere Füllstand und damit die Dosiermenge folgt — er stand bei null
geprüften Zeilen. Und zwei Prüfungen, die von der Uhrzeit des Laufs abhingen.

## 2.0.0-beta.61

**Beta.** Eine Gesamtdurchsicht des Codes — kein neues Feature, sondern
dreiundzwanzig Fehler, die schon da waren. Die schwersten drei liefen still:
eine Liste, die immer leer war und an der sieben Stellen hängen; ein Wächter,
der nur bei einem Pumpenwechsel überhaupt lief; und Fotos, die beim Löschen
eines Grows nie von der Platte verschwanden.

### Drei Sachen, die nie funktioniert haben

- Behoben — **die Liste der laufenden Grows eines Zelts war immer leer.**
  Gefüllt wurde sie von genau zwei Bildschirmen von Hand, von der Datenablage
  nie — und **sieben** Stellen lasen sie. Im Add-on-Betrieb hieß das: die
  Dosierung rechnete mit dem vollen Beckenvolumen statt mit dem halben und fuhr
  damit die doppelte Dosis; der Wächter für Lichteinbruch kehrte sofort um,
  ohne je zu prüfen; die Startseite bekam keinen Grow zugeordnet. Repariert an
  der Wurzel, nicht an sieben Stellen.

- Behoben — **der Kühler-Wächter lief nur, wenn eine Pumpe an- oder ausging.**
  Im Normalbetrieb tut sie das nie: beide Pumpen melden seit Stunden „an“. Der
  Kühler konnte also ausfallen, ohne dass irgendetwas passierte — und im RDWC
  ist das die Kette, die eine Ernte kostet: Kühler aus, Wassertemperatur
  steigt, Sauerstoff fällt, Wurzelfäule. Beim Reparieren kam ein zweiter
  Fehler zum Vorschein, den der erste versteckt hatte: die Entprällung schrieb
  nie zurück, es hätte eine Push-Nachricht pro Minute gegeben. Und der
  Merkposten wurde gesetzt, **bevor** gesendet wurde — ein Home Assistant im
  Neustart verschluckte die Warnung damit endgültig.

- Behoben — **Fotos wurden nie gelöscht.** Der Löschpfad rechnete gegen
  <code>wwwroot/uploads</code>, gespeichert wird unter dem Datenpfad des
  Add-ons. Die Datenbankzeile verschwand, die JPEG blieb für immer auf der
  Platte liegen. Dieselbe Wegrechnung stand zweimal im Code, beide Male falsch;
  jetzt steht sie einmal da — und der Schutz gegen Pfade, die aus dem
  Upload-Ordner herausführen, vergleicht bis zur Ordnergrenze statt nur den
  Namensanfang.

### Zahlen, die verloren gingen

- Behoben — **„21,5“ wurde auf der Ernteseite zu 215.** Die Gewichtsfelder
  hingen direkt an einer Zahl; das Komma fiel beim Tippen weg. Wer das
  Nassgewicht einer Pflanze eintrug, bekam den zehnfachen Ertrag in die
  Bilanz.

- Behoben — **die Summen darunter standen englisch.** „21.5 g“ direkt unter
  einem Feld, in dem „21,5“ steht. Dasselbe in drei Formularen, die Werte aus
  Home Assistant vorbefüllten: „5.82“ und „19.2“ unter der Zeile „Aus Home
  Assistant vorbefüllt“.

- Behoben — **den Haken „Aktiv“ bei einem Grenzwert abzuwählen löschte ihn.**
  Die Seite schickte nur die angehakten Zeilen, der Server ersetzt beim
  Speichern den ganzen Satz. Es kam „gespeichert“, die Zahlen standen weiter
  im Formular — und waren beim nächsten Aufruf weg. „Aktiv“ heißt jetzt
  **pausiert**.

- Behoben — **Erntegewichte je Pflanze gingen still verloren**, wenn die Ernte
  über den Abschluss-Weg gespeichert wurde. Die Summe stand da, die Zeilen je
  Pflanze waren leer.

### Ein Zielband je Messgröße

Vier Auskunftsstellen über denselben Messwert nebeneinander auf dem Schirm —
das ist in diesem Projekt schon dreimal passiert und jetzt an der Wurzel
behoben: Profil, Phase, Feedchart, eigene Grenzen stehen als **eine** Kette da,
und alle fragen sie.

- Behoben — **die Wochen-Ziele des Feedcharts galten nur auf der Live-Kachel.**
  Bei Athena Blended in Blütewoche 4 nennt das Chart EC 2,6, das Profil 1,0–1,2.
  Bei gemessenem EC 2,60 sagte die Kachel „im Ziel“ und das Messprotokoll
  derselben Messung „weit über dem Ziel“.

- Behoben — **ORP hatte vier Zielbänder.** Bei 470 mV in der Blüte schrieb die
  Kachel „daneben, Ziel 400–450“ und zog zehn Punkte vom Score ab; die Diagnose
  fand nichts.

- Behoben — **pH maß auf der Kachel am Anmischziel, in der Diagnose an der
  Komfortzone.** Bei pH 5,85 und Profil 5,90–6,00 hieß es links „daneben“ und
  rechts „im Ziel“. Wer nur **eine** der beiden Grenzen eintrug, verlor
  außerdem die ganze Komfortzone — die Diagnose meldete dann „zu niedrig“ für
  Werte, die die Kachel „im Ziel“ nannte. Eigene Grenzen gelten jetzt je
  Grenze.

- Behoben — **eine eigene Wassertemperatur-Grenze wirkte nur nach unten.** Bei
  einer Regel 15–20 °C beurteilte das Messprotokoll 21 °C als „im Ziel“ und
  nannte 15–22, während die Kachel daneben 15–20 zeigte. Die Kachel zeigt jetzt
  denselben Arbeitsbereich wie Protokoll und Diagnose statt des Tag/Nacht-Paars
  aus dem Profil — das ist in der Wachstumsphase null breit, 19,7 und 20,3
  standen beide rot.

### Stille Fehler an den Rändern

- Behoben — **ein Erntedatum in der Zukunft legte die Reservoir-Alarme still.**
  Ein Vertipper um ein Jahr genügte: die App schaltete auf Trocknungsziele um,
  während der Grow noch lief.

- Behoben — **der gelernte Lichtzyklus kippte um zwölf Stunden**, wenn das
  Licht um Mitternacht ausgeht — bei 12/12 der Normalfall.

- Behoben — **„Verbrauch eingebrochen“ sah den Einbruch auf 0 L nicht.** Ein
  Rückgang auf die Hälfte wurde gemeldet, drei Tage ohne jeden Verbrauch nicht
  — dabei ist das der Fall, der ein Wurzelproblem anzeigt.

- Behoben — **die Mischpause fiel an der Tagesgrenze aus.** Die Automatik
  dosierte um 23:55 und um 00:01 erneut.

- Behoben — **vertauschte Grenzwerte** (Untergrenze über Obergrenze) nahm der
  Server an und antwortete mit „OK“; die Regel hätte danach dauerhaft gewarnt.
  Die Ablehnung trägt jetzt dieselbe Form wie jede andere Fehlermeldung der App
  — vorher bekam der Nutzer nur den englischen Rückfalltext mit der nackten Statusnummer 400.

### Und warum das so lange niemand gesehen hat

Die Seitenliste, gegen die alle Oberflächen-Prüfungen laufen, war
handgeschrieben. **Sieben Seiten hat nie eine Prüfung geöffnet**, darunter vier
Formulare — deshalb standen die englischen Zahlen auf der Ernteseite
monatelang da. Die Liste zählt jetzt die Seiten der App ab; die sieben
aufgenommenen brachten sofort zwei weitere Fehler mit (unlesbare Schrittzahlen
im hellen Thema, die vorbefüllten Punktzahlen). Und die Zahlen-Prüfung liest
jetzt auch, was **in** den Eingabefeldern steht — dort saß der Fehler.

Die Testabdeckung ist zum ersten Mal ehrlich gemessen: Backend 71,8 % der
Zeilen, Oberfläche 47,4 %. Das ist ein Anfang und keine Lösung — 69 Klassen
laufen weiterhin in keinem Test.

## 2.0.0-beta.60

**Beta.** Zwei Meldungen des Testers, und beide gingen tiefer als sie klangen:
ein Grow konnte im Formular nur **eine** Sorte bekommen, obwohl das Datenmodell
sie je Topf längst trug — und der Wasserwechsel war nicht nur schwer zu finden,
er **zählte auch nicht**, wenn man ihn eintrug.

### Ein Grow führt N Sorten — jetzt auch im Formular

- Neu — **„Töpfe & Sorten" beim Anlegen und Bearbeiten.** Der Tester hat
  ausgeschrieben, was ein Grow ist: „ein Durchgang in einem RDWC/DWC, der N
  Pflanzen mit N verschiedenen Sorten/Phenos beinhalten kann. In dem Grow
  sollten die ganzen Sorten im RDWC-System stehen wie bei den Töpfen."

  Das Formular bot **ein** Sortenfeld und schickte den Nutzer per Hinweis weg:
  „Leg den Grow an und trag danach unter ‚Pflanzen & Sorten' jede Pflanze mit
  ihrer eigenen Sorte und ihrem Topf ein." Ein Weg, der aus zwei Schritten
  besteht, weil einer davon fehlt, ist kein Weg. Jetzt stehen unter der
  System-Auswahl die Töpfe des Systems, jeder mit seiner Sorte, dazu „alle auf
  …" für den häufigsten Fall. Beim Bearbeiten steht dort, was wirklich drin ist.

  Gelöscht wird dort nie: was das Formular nicht nennt, bleibt unberührt — eine
  Pflanze entfernt man in der Karte „Pflanzen & Sorten", die vorher nachfragt.

- Geändert — **die Pflanzenzahl folgt den belegten Töpfen.** Belegte Töpfe *sind*
  die Pflanzen; das Feld darüber ist dann nur noch Anzeige. Zwei beschreibbare
  Stellen für dieselbe Zahl laufen auseinander, und genau das ist in diesem
  Projekt schon dreimal passiert.

- Behoben — **fünf Ansichten nannten bei zwei Sorten trotzdem eine.**
  Grow-Liste, Zelt-Detail, Messformular, Addback-Kopf und Addback-Übersicht
  gaben die Hauptsorte aus, als wäre sie die einzige. Nur die Grow-Detailseite
  konnte „gemischt", weil ihre Pflanzen-Karte es ihr meldete — ein Mechanismus,
  den die anderen fünf nicht hatten. Jetzt liest der Server die Sorten aus den
  Pflanzen, und eine Regel gilt überall.

  Und danach zeigte die Grow-Liste immer noch den Züchter: die Zeile lautete
  „Züchter **oder** Sorte", also gewann der Züchter *immer*. Bei einem Becken
  mit White Widow (Royal Queen Seeds) und Gorilla Glue (GG Strains) stand dort
  „Royal Queen Seeds". Gefunden, indem die Karte angesehen wurde — nicht die
  Änderung.

- Behoben — **die Suche fand eine Sorte nicht, die nur an einer Pflanze hing.**
  Gesucht wurde über Name, Hauptsorte und Züchter des Grows. „Wo steht meine
  Gorilla Glue" ist aber genau die Frage, die jemand in ein Suchfeld tippt.

- Neu — **Hinweis bei sehr verschiedenen Blütezeiten.** Ein RDWC teilt ein
  Becken, und die Ernte hat einen Tag. Stehen eine 8-Wochen- und eine
  11-Wochen-Sorte zusammen, rechnet der Zeitstrahl mit der Hauptsorte und liegt
  bei der anderen um Wochen daneben. Das ist kein Fehler, den man
  wegprogrammiert — es ist eine Entscheidung, und die wird jetzt gesagt statt
  verschwiegen. Ab zwei Wochen Unterschied; darunter wäre es Lärm.

### Der Wasserwechsel: gefunden, und er zählt

- Behoben — **ein eingetragener Wasserwechsel räumte keine einzige Mahnung weg.**
  Gemeldet: „der User findet den Wasserwechsel nicht wirklich, das ist sehr
  umständlich von uns gelöst, weil er hat jetzt einen gemacht und will den
  eintragen und zurückdatieren."

  Beim Nachsehen war das Eintragen nicht das Problem — es blieb nur wirkungslos.
  Es gab **zwei Wahrheiten** darüber, wann zuletzt gewechselt wurde: das Häkchen
  „Lösungswechsel" an einer Messung und die Tabelle hinter dem Formular. Von
  vier Rechnungen lasen **drei** nur die erste. Wer den Wechsel im Formular
  eintrug, sah weiter „Wöchentlicher Wasserwechsel: zuletzt vor 20 Tagen".

  Am laufenden Stand nachgestellt und wieder nachgemessen: die Mahnung ist nach
  dem Eintrag weg. Gehalten wird das von einer Zählung über alle Dateien des
  Web-Projekts — wer künftig selbst rechnet, statt zu fragen, wird rot.

- Neu — **eine eigene Seite `/wasserwechsel` im Menü.** Das Formular lag als
  dritter Abschnitt auf „Addback"; das Wort „Wasserwechsel" stand im ganzen
  Menü nur als *Suchbegriff bei den Aufgaben* — wer es tippte, landete also auf
  der falschen Seite. Es ist **umgezogen**, nicht kopiert: auf Addback steht
  jetzt der Stand mit einem Weg dorthin. Nachfüllen und Wechseln sind zwei
  Handlungen.

- Neu — **der Stand als Bild.** Ein Punkt je Tag, der Plan als Länge: neun
  Punkte, von denen zwei überstehen, sieht man. „Letzter Wechsel vor 9 Tagen,
  Plan alle 7" ist ein Satz, den man erst im Kopf verrechnen muss.

- Behoben — **vier Stellen mahnten den Wechsel an, keine führte zum Eintragen.**
  „Heute fällig" auf der Live-Seite, „Fällige Routinen" am Handy, die
  Beobachtungen über Tage: der Knopf hieß überall „Öffnen" und ging zur
  Aufgaben- oder Grow-Seite, wo das Formular auch nicht steht. Jetzt heißt er
  „Eintragen" und führt hin.

- Neu — **ein Wasserwechsel lässt sich wieder entfernen.** Es gab keinen Weg
  zurück. Solange die Mahnung diese Tabelle nicht las, war ein Fehleintrag
  folgenlos; seit sie es tut, legt er sie für eine Woche still.

- Geändert — **ein Teilwechsel ohne Menge wird abgelehnt.** Vorher ließ sich das
  Formular vollständig leer abschicken. Verlangt wird eine Zahl: der Anteil oder
  die Liter. Der Komplettwechsel trägt seine Auskunft im Namen.

- Behoben — **Fehlermeldungen sagten nicht mehr, was fehlt.** Auf dem Schirm
  stand „Eingaben konnten nicht validiert werden", während der Grund daneben
  lag und niemand ihn las. Gilt für die ganze App, nicht nur hier.

### Der Testbestand

- Behoben — **keine Sorte im Testbestand hatte je einen Lauf.** Der laufende
  Demo-Grow hieß „White Widow", die Bibliothek führte „White Widow (Testdaten)",
  und weil die Statistik über die Verknüpfung zählt, stand bei jeder Sorte
  „0 Läufe, kein Ø-Ertrag". Ein Fehler *im* Bestand verdeckt Fehler *in* der App.

- Behoben — **die neue Wasserwechsel-Seite stand im Testbestand leer.** Der
  Bestand markierte den Wechsel nur an der Messung; die Tabelle war nie befüllt.
  Jetzt stehen dort vier Wechsel im selben Rhythmus.

- Behoben — **eine Aufräumzeile in einem Test räumte nichts auf.** Sie rief eine
  Route, die es nicht gab, lief in ein 404, meldete nichts — und der Testbestand
  wuchs mit jedem Lauf. Genau daran ist aufgefallen, dass Löschen fehlt.

- Behoben — **eine fünfte Testdatei fasste denselben Bestand an, ohne sich
  abzustimmen.** Der zweite volle Lauf hintereinander meldete prompt einen
  Fehlschlag, während dieselbe Datei allein dreimal grün lief. Eine Zahl im
  Kommentar („vier Dateien") altert; eine Zählung über alle Dateien nicht.

### Was der Prüfer gefunden hat

Zwei Durchgänge mit einem Agenten, der die Änderung nicht gebaut hat, ergaben
siebzehn Befunde. Die schwersten stammten von mir:

- Behoben — **beim Bearbeiten stand „0 von 4 Töpfen belegt"**, während vier
  Pflanzen mit ihren Sorten in der Datenbank lagen. Zwei Effekte schrieben
  dasselbe Feld; der schnellere gewann und löschte die Belegung wieder. An
  genau dieser Stelle stand ein Kommentar von mir, der das Gegenteil behauptete
  — geprüft hatte ich das Anlegen, nicht das Bearbeiten.
- Behoben — **die Sperre, die ich beschrieben hatte, gab es nicht.** Wer von
  einem 4-Topf- auf ein 2-Topf-System wechselte, konnte speichern; heraus kam
  ein Grow mit vier Pflanzen auf zwei Plätzen, den man danach nicht mehr
  speichern konnte. Die Prüfung bekam die Pflanzenzahl gar nicht zu sehen.
- Behoben — **ein gelöschter Grow ließ seine Pflanzen zurück.** Im Testbestand
  lagen 92 solche Leichen, und jeder volle Testlauf legte zwei weitere dazu.
  Dieselbe Lücke war für die Warnungen schon einmal geschlossen worden, eine
  Tabelle weiter stand sie noch offen. Produktionspflanzen gehen jetzt mit dem
  Lauf; eine Mutterpflanze überlebt ihn und verliert nur den Bezug.
- Behoben — **der Züchter stand neben der falschen Sorte.** Erst zeigte die
  Detailseite „Gorilla Glue · Royal Queen Seeds"; nach dem ersten Fix riet ein
  Namensvergleich, und „Northern Lights" gegen „Northern Lights Auto" ging
  prompt daneben. Jetzt vergleicht der Server die Verknüpfungen — geraten wird
  nicht mehr.
- Behoben — **die Warnung über verschiedene Blütezeiten rechnete nur mit dem
  Maximum.** Bei 8–9 gegen 9–11 Wochen stand „9 bis 11"; tatsächlich liegen
  drei Wochen dazwischen. Und ein Paar 8–9 gegen 9–10 fiel ganz durch.
- Behoben — **zwei meiner eigenen Prüfungen waren zu leicht zu umgehen.** Eine
  kannte nur eine Schreibweise, die andere ließ sich mit einem unbenutzten
  Import zufriedenstellen. Der Prüfer hat beides vorgeführt.
- Behoben — **die Kontrast-Prüfung sah geerbte Deckkraft nicht.** Fünfter
  blinder Fleck dieser Datei. Sie rechnet jetzt die ganze Kette und nimmt
  gesperrte Bedienelemente aus, die WCAG ausdrücklich ausnimmt.
- Behoben — **ein Testfall wartete auf die Schnittstelle und suchte auf dem
  Schirm.** In etwa jedem dritten Lauf rot. Ein Test, dessen Ausgang vom
  Zeitpunkt abhängt, hat nichts geprüft.

Backend bei 1466 Tests, 84 MCP-Tests, 296 Unit-Tests, 601 End-to-End-Fälle —
drei volle Läufe hintereinander ohne Abweichung, und der Testbestand ist danach
unverändert.

## 2.0.0-beta.59

**Beta.** Fehlerbehebungen rund um Pflanzen, Töpfe und den Wasserwechsel — alle
vier vom Tester gemeldet. Drei davon hatten dieselbe Wurzel: die Nummer einer
Pflanze kam aus der **Anzahl** statt aus ihrem **Topf**, und sobald eine
Pflanze fehlte, liefen die beiden auseinander.

### Ein Grow legt seine Pflanzen jetzt selbst an

- Behoben — **ein Grow mit vier Töpfen legte null Pflanzen an.** Gemeldet als
  „der User kann unter Grow nur eine Sorte auswählen, aber bei den Töpfen für
  den Grow 4 Stück". Wer im Formular Sorte und Pflanzenzahl angab, klickte
  danach viermal „Pflanze hinzufügen" und wählte jedes Mal dieselbe Sorte;
  die Karte „Pflanzen & Sorten" stand derweil auf „keine ist einzeln erfasst",
  und der Zeltplan zeichnete vier leere Töpfe.

  Jetzt entstehen sie mit: eine je Topf, mit der Sorte des Grows, höchstens so
  viele wie das System Töpfe hat. Danach lässt sich je Topf eine andere Sorte
  wählen — dafür ist die Karte da. Beim **Bearbeiten** entsteht nichts: wer
  eine Pflanze entfernt hat, will sie nicht beim nächsten Speichern zurück.

### Nach dem Löschen hieß eine Pflanze wie eine andere

- Behoben — **„eine Pflanze gelöscht und wieder hinzugefügt, da taucht diese
  doppelt auf".** Am laufenden Stand nachgestellt: vier Pflanzen, die dritte
  entfernt, eine neue angelegt — heraus kamen zwei mit Namen „Pflanze 4", und
  die neue saß auf Topf 3.

  Der Name kam aus der Anzahl (`Pflanze ${anzahl + 1}`), der Topf aus der
  ersten freien Lücke. Nach einer Löschung ergeben drei Pflanzen „Pflanze 4",
  und die gibt es schon. Der Name folgt jetzt dem Topf — ein Topf trägt eine
  Pflanze, seine Nummer ist also eindeutig. Wer eine Pflanze umsetzt, hat den
  Namen danach mitgezogen; ein selbst vergebener bleibt stehen.

### Der Topf stand zweimal in derselben Zeile

- Geändert — die Zeile las sich `Pflanze 1 · TOPF [1] · White Widow`: Name und
  Topfzahl sagten dasselbe, zweimal nebeneinander. Gemeldet als „etwas komisch,
  kannst du das angenehmer und verständlicher machen". Jetzt steht dort
  `[Topf 1 ▾] [White Widow ▾]` — jede Angabe einmal.

- Geändert — **der Topf wird gewählt statt getippt.** Ein Zahlenfeld liess eine
  belegte Nummer zu und meldete den Fehler erst danach; dabei weiss die App,
  welche Töpfe frei sind. Belegte stehen jetzt mit ihrem Bewohner in der
  Auswahl („Topf 2 · belegt (White Widow)") — ein Tausch ist damit sichtbar
  statt gesperrt.

### Ein Wasserwechsel liess sich nicht nachtragen

- Behoben — **das Formular hatte kein Datumsfeld.** Gemeldet: „wenn das vor
  Tagen passiert ist, dass man das nachtragen kann." Es fragte nach Art,
  Anteil, Menge, EC, pH und Notiz — nach keinem Zeitpunkt, und jeder Eintrag
  landete auf „jetzt". Wer sonntags wechselte und dienstags eintrug, hatte
  einen Wechsel vom Dienstag in der Historie, und die Rechnung „letzter
  Wechsel vor N Tagen" zählte ab dem falschen Tag.

  Das Feld steht jetzt ganz oben — die Frage „wann war das" kommt vor jeder
  Zahl —, ist mit dem aktuellen Zeitpunkt vorbelegt und lässt sich nicht in die
  Zukunft stellen. Der Server konnte es die ganze Zeit; es fragte nur niemand
  danach.

### Diese Release Notes sind auf Deutsch

- Geändert — **der Änderungstext, den Home Assistant beim Update zeigt, steht
  ab dieser Version auf Deutsch.** Bis beta.58 war er englisch, mit dem
  Gedanken, er richte sich an Fremde. Wer aktualisiert, ist kein Fremder. Die
  älteren Einträge bleiben englisch; sie sind Geschichte.

  Gehalten wird das von einer Prüfung, die den neuesten Eintrag liest und
  englische Wendungen meldet — die Regel „alles auf Deutsch" stand seit Langem
  im Projekt und wurde 114-mal gebrochen, weil sie niemand gemessen hat.

### Was die Prüfungen selbst betrifft

Vier E2E-Dateien schreiben an denselben Grow, und die Sammlung läuft parallel.
Zwei volle Läufe hintereinander ergaben verschiedene Ergebnisse: der erste
grün, der zweite rot mit „eingetragen 2026-06-24, im Formular steht
2026-06-10" — ein anderer Fall hatte dazwischengeschrieben. Ein Test, dessen
Ausgang vom Zeitpunkt abhängt, hat nichts geprüft.

Die vier teilen sich jetzt ein Schloss, und die Pflanzen-Fälle stellen ihre
Ausgangslage **her**, statt sie vorauszusetzen: ein Fall, der abbricht,
hinterliess sonst eine Lücke, und der nächste meldete einen Fehler über seinen
Vorgänger statt über die App.

Backend bei 1451 Tests, 275 Unit-Tests, 585 End-to-End-Fälle — drei volle
Läufe hintereinander ohne Abweichung.

## 2.0.0-beta.58

**Beta.** Kein Funktions-Release. Ein Nutzer hat ausgesprochen, was niemand
hören wollte — „CRUD ist grundlegend und du hältst dich nicht daran, und für
das Flip-Datum gibt es keine ordentliche Prüfung" — und er hatte recht, weiter
als nur in dem Fall, der ihn dazu gebracht hat. Gezählt statt geschätzt: **13
von 24** Routen, die etwas anlegen, hatten keinen Weg, es wieder zu löschen;
**471** Felder in 36 schreibenden Verträgen; und **zwei** Testdateien, die je
geprüft haben, ob ein gespeicherter Wert auch zurückkommt.

### Was sich jetzt rückgängig machen lässt

- Neu — **neun Dinge lassen sich entfernen, die vorher blieben**: eine Sorte,
  ein Bereich, ein Lichtplan, ein Kalibrier- oder Wartungseintrag, ein
  Journaleintrag, eine automatische Messung, eine Pflanze und ein Ablauf, den
  man versehentlich gestartet hat. Bis jetzt konnte die App all das anlegen und
  nichts davon löschen. Wer eines zu viel angelegt hatte, behielt es.

- Neu — **jedes davon verweigert sich, wenn noch etwas daran hängt, und sagt
  was.** Eine Sorte, die benutzt wird, bleibt („‚White Widow' wird noch von 3
  Pflanzen verwendet"). Ebenso ein Bereich, in dem noch Pflanzen, Geräte oder
  Grows stehen — ihn zu löschen machte den Grow vorher unspeicherbar, weil die
  App danach jedes Speichern mit „Setup mit Id X existiert nicht" ablehnte. Der
  letzte Lichtplan eines Zelts bleibt auch: die Nachtabsenkung, die
  Lichteinbruch-Überwachung und beide automatischen Messungen hängen daran, und
  jede von ihnen liest einen fehlenden Plan als „nichts zu tun".

- Neu — wer einen Ablauf abbricht, wird seine Erinnerungen in der Aufgabenliste
  mit los. Sie hängen an einer Spalte ohne Fremdschlüssel, es hätte sie also
  nichts aufgeräumt; sie wären als Karteileichen liegen geblieben.

### Das Flip-Datum hatte ein Geschwister

- Behoben — **„Tage bereits in der Phase" wurde bei jedem Grow angeboten und
  bei den meisten weggeworfen.** Dieselbe Form wie das Flip-Datum im letzten
  Release: das Formular zeigte das Feld, der Server nahm es nur außerhalb der
  Keimung an und nur bei Nicht-Autoflowers. Gefunden von einer neuen Zählung,
  nicht von einem Nutzer.

- Neu — **Autoflower-Grows können endlich ihr Alter angeben.** Das Feld „Tage
  seit Keimung" gab es auf dem Server und wurde nirgends angeboten; wer einen
  Autoflower mitten in der Vegetationsphase übernahm, konnte der App nicht
  sagen, wie alt die Pflanze ist.

- Geändert — das Feld „Pflanzen" im Grow-Formular zeigt die Zahl jetzt nur noch
  an, wenn Pflanzen einzeln erfasst sind, statt eine Zahl anzunehmen, die der
  Server danach überschreibt.

### Drei Zählungen, damit diese Klasse Fehler sich selbst meldet

Jede läuft über ihre eigene Grundmenge und verlangt entweder eine Behandlung
oder einen ausgeschriebenen Grund — keine handgepflegten Listen.

- **Jede Route, die „201 Created" verspricht, braucht einen Weg zum Löschen.**
  Nicht jedes POST: ein POST kann eine Handlung sein („auf Blüte umstellen",
  „Testbenachrichtigung"). Drei Ausnahmen tragen ihren Grund im Test.
- **Jedes Feld jeder schreibenden Anfrage muss einen Rundweg überstehen** — ein
  Feld ändern, speichern, zurücklesen, vergleichen. Diese Zählung fand sofort
  den Fall oben.
- **Jede Löschroute braucht einen echten Knopf.** Sieben der neun neuen hatten
  keinen; die Verweigerungsmeldung zum letzten Lichtplan konnte niemand je zu
  sehen bekommen.

Damit die zweite überhaupt möglich wurde, hat das Projekt seinen **ersten
Integrations-Aufbau** bekommen — bis dahin rief jeder Backend-Test die
Controller direkt auf, und das kann die Frage „kommt der Wert, den ein Formular
schickt, auch wieder heraus" nicht beantworten.

### Fürs Protokoll: was ein Prüfdurchgang in dieser Arbeit fand

Gehört klar gesagt, denn genau dafür gibt es ihn. Die Rundweg-Zählung filterte
auf eine Routenform und übersah dadurch **15** Endpunkte — darunter das
Messformular, das meistbenutzte der App. Nachgewiesen, indem ein Fehler
eingebaut wurde, der den pH der Nährlösung bei jeder Messung still verwarf:
**alle 1442 Tests blieben grün.** Die Zählung deckt jetzt jeden schreibenden
Vertrag ab und fängt genau das.

Derselbe Durchgang fand, dass die neuen Löschsperren nur Verweise zählten, die
die Datenbank ohnehin schützt, und dass die Oberflächen-Zählung vier von acht
Feldern sah, weil diese Eingaben kein Typ-Attribut tragen. Beides behoben.

Backend bei 1446 Tests, 272 Unit-Tests, 581 End-to-End-Fälle. Jede Prüfung
trägt einen Bissnachweis.

## 2.0.0-beta.57

**Beta.** Two reports from the field — a flip date that never arrived and a
tent that accepted more plants than it has pots. Both were the same kind of
defect: the app said yes and did nothing.

### The flip date was thrown away without a word

- Fixed — **you can now enter the flip date whatever phase the grow started
  in.** The form shows the field for every grow that is not an autoflower; the
  server only accepted it when the *entry point* was "Flower". But the normal
  case is the other one: a grow starts in germination or veg and is flipped
  weeks later. Whoever typed the date then got HTTP 200 back and an unchanged
  value. Demonstrated against the running app: 2026-08-01 sent, 2026-07-20
  still stored, no message.

  One condition now decides it, in one place: a flip exists unless the plant is
  an autoflower. Three cases in the update: a missing field preserves what is
  stored (an outside caller must not be able to take a grow's flip away), an
  empty field clears it, a date sets it.

  Held by a census over *every* entry point rather than a hand-written list —
  including the one that only exists tomorrow.

### More plants than pots

- Fixed — **a pot holds one plant, and there are no more plants than pots.**
  The check never looked at the pot number at all. Demonstrated against the
  running app on a four-pot system: eight plants accepted, pot 1 occupied
  twice, one plant in a pot 999 — every one of them HTTP 201.

  The number must now lie within the system's pot count and must not belong to
  another plant of the same grow. The count applies to anyone newly entering
  the grow, not only to newly created plants — moving a plant in from another
  grow and releasing one from quarantine were two more doors into the same
  room.

  Existing data that already breaks the rule stays editable on purpose: a
  change is only checked when the pot actually changes. Otherwise the people
  who have the problem would be locked out of fixing it.

- New — **plants can be removed.** There was no way at all: no endpoint, no
  repository call, no button. Whoever added one too many kept it. A mother with
  cuttings still stays — otherwise the lineage loses its beginning; a
  phenotype evaluation belongs to the plant and goes with it. The card asks
  before removing.

- New — the card says **"3 of 4 pots taken"**, marks a doubly occupied pot, and
  greys out "Add plant" with the reason next to it. A disabled button without a
  reason is a broken button.

### Two smaller things that fell out of it

- Fixed — **the reason for a refusal never reached you.** The app reads only
  the `message` field of an error; every per-field sentence sat in a part
  nothing displays. "In Topf 1 steht schon 'Pflanze 1'." now arrives verbatim.

  The obvious fix was wrong and is recorded as such: folding all field errors
  into the message ships the framework's own English texts — "The field
  HumidityPercent must be between 0 and 100." — from 37 attributes that never
  had a German one.

- Fixed — **the "Plants" tile counted the form, not the plants.** The report's
  screenshot showed "Plants 6" above eight rows. Where plants are recorded
  individually they are now the truth, for the grow list, the live tile, the
  area per plant, the archive and grams per plant alike.

### For the record

Backend at 1421 tests, 269 unit tests, 580 end-to-end cases against the running
app. Every new check carries a bite proof: with the old code 14 of 27 new
backend cases went red, and 3 of 12 for the ones a review pass added.

## 2.0.0-beta.56

**Beta.** Three reports from the field, each fixed at its root: one strain per
pot, a Crop Steering page that now *leads* instead of only telling, and a
"502" that appeared while the device had in fact switched.

### One strain per pot

- New — **each plant carries its own pot number** (`SiteIndex`, starting at 1 —
  the same numbering the hydro system's top-down view draws onto its sites).
  The report was "I run a different strain in every pot of my RDWC and can't
  enter that." Per-plant strains had existed since beta.37; what was missing
  was the *place* — and finding the feature at all.

  Deliberately just a number: no pot table, no coordinates, no dragging. The
  top-down view already numbers its sites deterministically from the geometry;
  a second model for the same truth would cost more than it gives.

- New — the "Plants & strains" card has a pot field per row, and a new plant
  gets the next free pot.
- New — the grow form now says, right at the strain field, where the path for
  multiple strains is. That is where you stand when you have the case.
- Changed — the "Strain" tile in the grow overview says **"gemischt (2)"** with
  the list instead of claiming a single main strain.

- Fixed — **a silent data loss that would have started with this feature.** The
  PUT on a plant overwrites *all* fields, and the card listed them by hand: the
  new pot number would have been nulled the next time anyone changed only the
  strain. Demonstrated — with the old version the new check reports "Beim
  Sortenwechsel ging der Topf verloren (war 2, ist undefined)". The card now
  copies the whole record and replaces one field.

### Crop Steering: the chain leads to the fix

- Changed — **every broken link now carries a button that takes you there.** A
  user stood in front of the page and did not know how to switch the control
  on. The chain did say what was missing ("the switch below is off") — but the
  switch, the floor temperature and the target device sit scattered further
  down, and the flip lives on another page entirely. "Zum Schalter", "Zur
  Untergrenze", "Zum Zielgerät" and "Zur Steckdose" jump to the spot and flash
  it; "Zu den Sollwert-Profilen", "Flip eintragen" and "Zur Einrichtung" lead
  to the right page.

  The mapping hangs on machine-readable keys, not on the German titles — a
  mapping by wording would be silently dead after the next rewrite. A census
  checks both directions: every backend key has an action, no action points at
  an invented key, and every link target is a real route.

- Changed — if your floor temperature sits above the flowering night value, the
  warning now also stands **right at the floor field**, where you fix it, not
  only far above in the chain.

### "Sometimes it returns 502 — but the switching works"

- Fixed — **both were true.** The AC Infinity integration often reports a new
  value back only after its next cloud poll — longer than the confirmation
  check waits. The first version knew only *confirmed* or *error*, and so it
  lied in the opposite direction from before beta.55: first success without
  proof, then failure without failure.

  There are three outcomes now: confirmed (green); sent but not yet reported
  back (**amber**, HTTP 200 — the page re-reads the state by itself after 15
  and 45 seconds); and only a call Home Assistant refuses at all is an error,
  because only then nothing was switched.

- Fixed — the same screenshot showed "API request failed with status 502" as
  raw English. The controller returned plain string lists while the app reads
  the error contract — the German sentence existed and never arrived. All
  AC-test responses go through the contract now.

### The phone "jumped" on Edit — the third report about the same button

- Fixed — the page scrolled correctly, but two bars are pinned at the top of a
  phone (108 px), and `scrollIntoView` put the form's title and first field
  right underneath them. Both existing checks *could not* see this: they
  measure against the window edge, and behind a pinned bar you are inside the
  window. One truth (`--mobil-kopf`) plus a scroll margin on all four jump
  targets; the new check measures the bars' lower edge rather than assuming it.

### The demo fixture grew again — and immediately exposed two older defects

The fixture now seeds four plants in pots 1–4 across two strains. Before, it
seeded none at all: in the demo the plants card only ever showed its empty
state, so no screenshot, no end-to-end run and no look at the running app could
ever see the multi-strain path. That is a large part of why nobody found it.

- Fixed — on `/sorten`, "Hunt läuft · 3 Kandidaten" ran into the neighbouring
  column (96 px of text in 93 px). Visible only once the hunt had candidates.
- Fixed — the new Crop Steering buttons were 27 px tall instead of 44. Caught
  by our own touch-target check, after it had passed for the buttons' first
  version.

### For the record

The end-to-end suite is at 580 cases, the backend at 1391 tests. Every new
check carries a bite proof: the defect was rebuilt and the check went red with
the exact word, page and pixel count.

## 2.0.0-beta.55

**Beta.** The test bench learned schedules, the demo fixture stopped lying,
and one night of reading every page on the running app found fourteen visible
defects — plus six places where our own checks checked nothing.

### Zelt (AC-Test) — schedules, and honest success messages

- New — **set the on/off schedule of a device.** Enter the two `time.`
  entities of your AC Infinity device and Grow OS writes the schedule: on
  time, off time, then the mode — in that order, because a device switched to
  "Schedule" while still carrying old times would run the *old* plan. The
  suggested times come from the tent's light schedule, the same source the
  light-intrusion guard reads. Nothing is invented.

- Changed — **"set" now means the controller confirmed it.** The AC Infinity
  cloud silently drops parallel updates ("Unable to update device controls" —
  our tester learned this the hard way and documented it in his Home Assistant
  card). Grow OS now writes one value at a time, re-reads the entity until it
  reports the target, retries up to three times, and stops the remaining steps
  after a failure. A partial success is reported as exactly that, naming what
  arrived and what did not — "saved" would be the most dangerous message of
  all. The already-shipped level buttons go through the same path.

  You can tell: setting a value takes a few seconds now, and the page says
  why. Before, "set to 7" appeared instantly whether the device obeyed or not.

### The demo fixture was a miniature — and it hid defects

Everything is verified against the demo install: every screenshot, every
end-to-end run. One night found eight contradictions in it, each of which had
been masking a class of bugs:

- Fixed — the light switch computed in UTC while the sensor curves used local
  time: twice a day the fixture reported "light on" at PPFD 0, visible on the
  live page.
- Fixed — the fixture ran 18/6 light on a grow 35 days into flower — the exact
  situation the app itself warns about ("this prevents flowering"). The demo
  now runs 12/12, has a light schedule on the tent, and a consistency test
  holds the fixture to the app's own rules.
- Fixed — "weekly water change: 73 days overdue" showed permanently, because
  no measurement carried the change marker even though the EC curve sawtoothed
  weekly and the journal said otherwise.
- Fixed — the "circulation pump draws 0 W" risk pointed at the pH probe;
  following the alert led to the wrong device. There was no pump. The fixture
  now has nine devices (probes, pumps, chiller, filter, a handheld meter that
  correctly needs no mapping) instead of one — the tester has seven.
- Fixed — zero of seventeen metrics were mapped, so every screen showing
  values took a path no real install uses. Eleven sensors are now mapped
  through the same code a real installation uses.
- Fixed — in demo mode every switch command reported success and changed
  nothing. What you set now stays set (a switchboard remembers it), and a
  command for an entity that does not exist fails — like in a real install.

### Fourteen defects found by reading the running pages

- Fixed — **the "Start grow" form spoke English.** Seed type offered
  "Feminized / Autoflower / Regular", start material "Seed / Clone", entry
  point "Germination / Seedling / …", status "Planning / Running / …" — 29 raw
  developer identifiers across the app, including "Automatic" sitting between
  "Feminisiert" and "Regulär" in the same dropdown. One translation table now
  serves all pages (it existed four times, each copy different), and a check
  reads every page — including the forms behind a click — and fails on any
  raw value without a written-out exception.
- Fixed — **"Linked grows" on every hydro system page was always empty**: the
  page filtered on the wrong field. Your running grow now shows up there.
- Fixed — **English decimal points in a German UI**: chart axes ("5.80"),
  the VPD preview in the measurement form ("1.00 kPa"), and every value from
  Home Assistant (HA always sends "17.8"). A check now reads 31 pages and
  fails on any digit-dot-digit that is not a German thousands separator.
- Fixed — **text collided with its neighbours**: on the phone the archive
  showed "21.05.202688 T" (date ran into the duration column), and the dosing
  log broke "6,31" into "6,3" over "1" — a pH value split across two lines.
  Device names broke mid-word ("HA-Senso/r") as soon as the fixture had more
  than one device. Two new checks measure word rectangles, not element boxes.
- Fixed — an overdue maintenance entry showed "-6 T"; it now says "überfällig".
- Fixed — the diagnosis page printed a developer sentence ("Deviation '…'
  passt zu Knowledge-Symptom '…'"). It now says what it means: the deviation
  matches a known condition — in German, without our internal words.
- Fixed — "Es fehlt: zielgerät zugeordnet." — a lower-cased German noun
  mid-sentence, produced by forcing a list title into a sentence. Each
  prerequisite now carries its own sentence form ("Es fehlt ein zugeordnetes
  Zielgerät.").
- Fixed — the new "Übernehmen" link on the test bench had contrast 3.60 in the
  light theme (the token comment even warns about exactly this mix-up).
  Fourth time the light theme caught a defect; the contrast check now reads
  its pages from the shared page list, so no new page can be forgotten.
- Fixed — two defects visible **only on Linux** (fonts run wider there, and
  the add-on runs on Linux): a table cell on /berater broke mid-word, and the
  "Verlauf" heading on the tent page was squeezed past its own text at 360 px.
  Both were green locally and red in the gate — which is now a documented
  rule: pixel measurements count once CI confirms them.

### Six places where our own checks checked nothing

An independent review pass ran after "all green" — and won:

- The **pause between cloud writes** — the reason the writer class exists —
  could be deleted with all 1381 backend tests staying green: the test clock
  discarded wait times, and pause and poll interval were both 2 s, so no test
  could tell them apart. The poll is 1 s now, a test clock records every wait,
  and deleting the pause turns the suite red.
- The fixture consistency test **hard-coded the chiller entity id** instead of
  reading it from the tent — the very mistake it exists to catch. Verified by
  planting a plausible-but-wrong id: 1381 tests stayed green. They fail now.
- Exception lists that excused values nobody had ever seen; a census now
  requires every exception to actually occur somewhere on a rendered page.
- The backend build-stamp guard covered only the backend — while practically
  all visible work ships in the frontend. A new check compares the served
  bundle against the newest source file and names the file and the seconds if
  `npm run build` was forgotten. Prerequisite: the assets folder no longer
  accumulates old bundles (481 files, 246 of them stale bundles, cleaned on
  every build).

### For the record

- The end-to-end suite grew from 399 to 577 cases, the backend from 1364 to
  1385 tests. Every new check carries a bite proof: the defect was rebuilt,
  and the check went red with the exact word, page and pixel count.
- All page-reading checks share one page list, generated from the app's own
  menu — four hand-maintained lists, each missing something different, are
  gone.

## 2.0.0-beta.54

**Beta.** A test bench for controlling devices, and seven checks that stopped
lying.

### Zelt (AC-Test) — a test bench, marked as one

- New — **a separate menu group "Versuch" with the page `/ac-test`.** Enter the
  entities of your AC Infinity controller and set a device's level 0–10 by
  clicking. A banner at the top says what it is: a test that writes real values,
  unfinished, feedback wanted.

  It writes through `number.set_value` — the same path the night ramp has used
  for months, so no new machinery was needed. Nothing regulates itself: a level
  is set when you click it, never otherwise. The controller keeps its own brain;
  two systems steering one device is the trap that already earned the chiller
  its own rule.

  The configuration lives as JSON in the settings store, not as columns on the
  tent. A test does not get a schema that every existing install carries forever.

### Seven checks that could not fail

Each one verified against the source before it was touched.

- Fixed — **the gate could not fail.** In `ci.yml` the only evidence that the
  demo fixture was seeded ended in `|| true`. If the line was missing from the
  log the step still went green, and the strict end-to-end run would have
  measured against an **empty database** and reported success.

- Fixed — **routes proved themselves.** The reachability check searched the
  whole source *including* `App.tsx`, which is where the routes are declared.
  It has been named in `CLAUDE.md` under "checks that check nothing" for weeks;
  nobody had repaired it. `App.tsx` is excluded now, a self-test holds that in
  place, and an invented route is reported — demonstrated.

- Fixed — **two missing count guards.** The submit-button census asserted "no
  form without a submit button" while never saying whether it had seen a form
  at all, and silently skipped every form without buttons. The contrast check
  reported "nothing unreadable" even when no text had been measured — that
  check has been blind three times in this project.

- Fixed — **two exception lists nobody checked.** A typo in an exception
  protected a file that does not exist while the real one failed, silently. The
  header of one of those very test files *describes* this happening. Nothing
  verified it.

- Fixed — **a test that depended on the clock.** The quiet-hours check built a
  one-hour window from `DateTime.Now.Hour`. Start at 14:59:59, evaluate at
  15:00:00, and it fails with nothing changed. Three hours now, current one in
  the middle.

### Four more silent data losses

- Fixed — on settings, "20x" silently became a 15-minute grace period and
  "32,5x" an empty electricity price, both with a success message. Same class as
  the two fixed a day earlier. Twenty pages each had their own number parsing;
  a ratchet now allows only fewer, never more — 22 down to 16.

### The effect landing off-screen, three more times

- Fixed — the care form on "Sensoren & Wartung", the profile panel on setpoint
  profiles, and the strain form all opened outside the window when the list
  above them was long. Same shape as the edit button reported yesterday.

  Two of my own mistakes surfaced here. The fixture's planned calibration set
  the wrong date field, so no device ever showed the trigger — and the new
  checks passed *without* the fix, because with two devices the target is on
  screen anyway. They measure in a 300 px window now, which reproduces a real
  user's install. Of the two new cases only the first is demonstrated to bite;
  the other is precaution, and the test says so.

## 2.0.0-beta.53

**Beta.** A button that worked, and nobody could tell.

### "Edit" did nothing

- Fixed — **on "Sensoren & Wartung" the edit form opens *below* the device
  list.** With two devices that is invisible; with seven it opens past the
  bottom of the window, and nothing scrolled to it. The button had been working
  the whole time — the reporter simply never saw the result. Measured on the
  running build: the form appeared at y = 721 in a 600 px window, scroll
  position 0. It now scrolls into view; y = 57.

  Every existing check would have passed. The button exists, the state changes,
  the form is in the DOM — even Playwright's `toBeVisible` says yes, because the
  element has an area. What was missing was any look at *where* it lands. A new
  check measures that instead: form position against window height, in a
  deliberately short window that stands in for the rows a test fixture does not
  have.

### A test that depended on the calendar

- Fixed — **CI turned red without a code change.** A demo-data check asserted a
  fixed calendar day (2026-07-28) over 24 hours, while the demo curve is
  anchored to *today*: each real day walks the sampled point further along the
  water-change sawtooth. Green on the 20th, red on the 23rd, at EC 1.10 against
  a lower bound of 1.20.

  The luck it had been running on hid more. Against the real 42-day window the
  app displays, **three of seven bounds were wrong** — EC (1.02–1.24 vs. 1.2
  required), reservoir temperature (17.8–24.5 vs. 23 allowed) and dissolved
  oxygen (5.8 vs. 6.0 required). The last two peaks are not faults but the
  built-in chiller failure: warm water holds less oxygen. That is the story the
  fixture is meant to tell, and the bounds forbade it.

  It also covered seven of ten curves. It is a census now: the ground set is
  `Demoverlauf.Schluessel`, the whole 42-day window is walked, and a new curve
  without a plausibility bound fails. The bounds say "no tent looks like this"
  rather than "the curve runs here today" — pinned to the curve, a check only
  verifies itself.

## 2.0.0-beta.52

**Beta.** The water chiller can now be steered, which is what crop steering in
RDWC was missing. And the tests stopped changing the data they were testing.

### Steering the water temperature

Grow OS has *planned* a night ramp since beta.32 — one degree cooler per flower
week, the "Cold Morning Routine" from SKX. It could never *execute* it. A Hailea
chiller takes no setpoint from outside; it has its own thermostat and no bus.

The tester turned it around: set the chiller itself to a low floor — around
15 °C — so its own thermostat is only a backstop, and switch it through a smart
plug. The socket becomes the thermostat and the setpoint comes from the profile.
The clever part is the direction of failure: if the socket ever sticks on, the
chiller cools to its own floor and stops. Cold, not fatal. At 5 °C the same
fault would be root damage — which is why the floor is stated in the interface
as a **condition**, not a recommendation.

- New — **two-point control with compressor protection.** Dead band ±0.4 °C,
  minimum run 5 min, minimum pause 5 min, reading no older than 10 min, all
  adjustable per tent. Off by default: something that cycles a compressor does
  not switch itself on.

  The order of checks is deliberate — first every reason *not* to switch, then
  the control. No setpoint does **not** mean "off then": an autoflower never
  flips, so it has no flower week and no ramp, and switching a running chiller
  off on that basis would be a rising reservoir. It stays as it is.

  The minimum pause is the one that matters. A refrigeration compressor needs
  the pressures to equalise; restarting too early works against residual
  pressure. The last switching time lives in the database, not in memory — a
  field on an object would be null after every add-on update, and then the
  compressor cycles exactly when someone installs one.

- New — **its own one-minute tick,** not the light edge. The night ramp hangs on
  the edge and writes a setpoint twice a day; for a controller that is useless,
  because the water drifts *between* the edges.

- New — **page "Crop Steering"** (`/cropsteering`). The plan, the setpoint
  target and the chiller in one place. Until now the ramp sat in a card on the
  grow, the target values in the setpoint profile and the chiller nowhere;
  anyone asking why the water is this warm had to look in three places. The card
  on the grow now shows the state and links here — the same main action on two
  pages is a finding in this project, not a feature.

- New — **a card on the live screen** while the control is active, carrying the
  reason in full: "18.9 °C, the chiller is off and would start at 20.4 °C
  (day value 20.0 °C)". Without it a chiller standing still at 21 °C looks like
  a fault when the minimum pause is simply running.

- Fixed — **the plant watchdog called a deliberate switch-off a failure.** Every
  regulated pause would have been reported as a fault.

### What a second pair of eyes found

The controller above was reviewed against the running build before release, by
something that had not built it. Three of its findings would have shipped
broken:

- Fixed — **the controller would never have switched anything in a real
  install.** It looked up the socket's state in the dictionary returned by
  `GetStatesAsync` — whose keys are *metric* keys (`chiller`,
  `reservoir-temp`), never entity ids. `switch.kuehler` was never a key there.
  It appeared to work only because the demo data added that one key on purpose:
  **the test fixture was hiding the bug.** The socket is now fetched as a single
  entity, and a test asserts that an entry under the entity id is *not* read —
  put the old lookup back and it goes red.

- Fixed — **a chiller the controller had switched off was still reported as a
  failure,** twenty minutes later. The plant watchdog was told "deliberate"
  only within a time window; a cool night is exactly the case that outlives it.
  The question now goes through the last *command*: off because I wanted it is
  fine, "I commanded on and the socket says off" is the real fault.

- Fixed — **reading age was measured against `last_changed`.** That only moves
  when the state *text* changes, so a temperature sitting steady on its setpoint
  — the normal case once control works — counted as stale and blocked further
  control. `last_updated` is now read and used.

- Fixed — **an emptied field became the harshest setting.** `Number('')` is 0,
  and clamping 0 into the allowed range picked the *minimum*: dead band 0.2 °C,
  minimum pause 1 minute, on a compressor. Below the allowed range the default
  applies now, not the edge.

- Fixed — **an unreadable floor value was swallowed in silence,** with a success
  message. Same class of fault as the 21 numeric fields in the measurement form.

- Fixed — **the floor was stored uncapped** while the calculation capped it, so
  the tile could read "8 °C · set by you" beside a table ending at 12.

- Fixed — **the off threshold could fall below the hard floor.** Setpoint 12
  with a 3.0 dead band kept the chiller running to 9 °C while the same class
  claimed it does not cool below 12.

- Fixed — "Regeln & Automatik" still sent people to the grow for the night ramp.

### One working range, not three

- Fixed — **the app reported its own regulation as a deviation.** The night ramp
  drives the water to the profile's finish night value — 16 °C by default — while
  the working range started at 17 °C. From flower week 3 on, every night reading
  was out of range.

  Both numbers are right; they mean different times of day. The knowledge source
  says "below 18 °C nutrient uptake is inhibited" — and that is precisely the
  *point* of SKX's cold night: the gate the nutrients pass through narrows, and
  the stress goes into resin. What would be a deficiency by day is the method by
  night.

  So the lower bound now follows the profile's night value rather than a number
  invented here, and the verdict says which is which. The upper bound and the
  17 °C day floor keep their source.

  The numbers lived in **three** places across two services, which is why nobody
  noticed when the ramp went from planned to real. They live in one now, and a
  census over both services fails if a bare 17, 22, 14 or 24 reappears — it
  found a third occurrence while being written.

### Tests that changed what they were testing

- Fixed — **the form round-trips wrote into the demo data and left it there.**
  Each run added a tent named "Rundweg HH:MM:SS" and a measurement, and one
  test overwrote an existing measurement. After three runs an empty test tent
  won the tent selection and half the live screen looked blank.

  Every writing round-trip now cleans up and **verifies that it did**: created
  rows are deleted, changed rows are read before and written back after. Proof:
  count the stock before and after a full run — unchanged. The one exception is
  the journal, which has no delete endpoint by design; its entry carries
  "Rundweg" in the title.

- Fixed — **`/cropsteering` was in no visual check at all.** It is now in the
  contrast, phone-cut and touch-target sweeps like every other page.

- Fixed — **a round-trip that was "flaky" had checked nothing.** All five waited
  for the *request* and then navigated away. `waitForRequest` is satisfied the
  moment the browser has sent; asking for the log right after checks for a row
  that cannot be there yet, and on a slow runner the navigation aborts the
  request outright. It always passed locally and fell over once on CI, where it
  was reported as flaky and shrugged off. They now wait for the *response* and
  check its status — an HTTP 500 used to count as "sent".

- Fixed — **a decimal field rejected the German comma.** `<input type="number">`
  drops "0,6" in many browsers, and what would have been saved is the old value
  in silence. The round-trip types a comma on purpose now.

## 2.0.0-beta.51

**Beta.** A layout that had been skewed since it was written, and the reason it
stayed that way.

### Sensors & maintenance

- Fixed — **the device row was skewed.** On a wide screen the three buttons of
  each device stacked vertically inside a 127 px column, making the row 131 px
  tall instead of 66.

  The cause is worth writing down. `.hw-actions { flex-wrap: wrap }` sat *two
  lines below* `@container (min-width: 761px) { .hw-actions { flex-wrap:
  nowrap } }` — and won. A container query does not raise specificity; at a tie,
  order decides. Half of the intent did work: the rule that narrows the column
  uses an attribute selector and is therefore stronger, so the column shrank
  while the `nowrap` evaporated.

- Fixed — **the counters were ragged on a phone.** "Kalibrierung fällig" wraps
  onto two lines and "Störung" does not, so the two zeros beside each other sat
  at different heights. The values now align to the bottom of their tile. The
  same strip appears on the addback and Home Assistant pages.

- Fixed — on a tablet the label column of a device card took 42 % of the width,
  which is right on a phone and about 300 px of empty space next to the word
  "Art" on a 710 px card. It is capped now.

### Three dead media queries

A new check walks every stylesheet and reports any rule inside a `@media` or
`@container` block that a later unconditional rule overrides. It found three
more on the tents page: the button width on a phone, the tighter tile grid on a
tablet, and right-aligned values on narrow screens — all three dead since the
day they were written, for the same reason as above.

The check carries its own proof: it contains the case from this release as
text and must report it.

## 2.0.0-beta.50

**Beta.** Numbers were written the English way, and the measurement log was
carrying a second, worse form.

### Numbers in German sentences

- Fixed — **the decimal point.** Nowhere did the app set a culture, so every
  formatted number followed the culture of its environment: `6,5` on the
  developer's German machine, `6.5` inside the container, which has no `LANG`.
  The container is what you run. **80 user-facing texts** in the backend were
  affected — "SOP-Schwelle 6.5 mg/l", "Anmischen auf 5.8-6.2", every deviation
  message.

  The culture is now set once at startup rather than at 80 call sites. Every
  place that *reads* a number from text or *stores* one as text already pinned
  the invariant culture explicitly, and JSON is culture-independent by
  specification — so only what a human reads changed.

  A quiet trap sits behind this: in invariant mode (a base image without ICU, or
  `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`) `new CultureInfo("de-DE")` raises
  no error — it returns the invariant culture, which calls itself "de-DE" and
  still writes a point. The setting would do nothing and nobody would know.
  Startup now checks its own effect against an actual number and says so
  loudly if it failed.

### The measurement log

- Fixed — **"Messungen" offered a second, crippled measurement form.** Nine
  fields where `/messung` has 31, without the live check, the photo or the
  addback. Since the page entered the menu, both sat next to each other:
  "Messen" and "Messungen". Whoever picked the wrong one got a form with no
  checking. The log is now a log; entering happens on `/messung`.

- Fixed — **impossible values passed unremarked.** 9000 °C of air and EC 99999
  sat in the log looking like any other row, because they carried the same
  verdict as "no target band for this phase" — which is drawn deliberately
  quietly. They now have their own verdict, their own mark, their own colour
  and their own counter in the summary. On the phone the name comes with it
  ("Wassertemperatur unmöglich"), because the value itself is not in that row.

- Fixed — **the cell said 5,95 while the spoken sentence said
  5,953333333333333.** Decimal places now live in one place and are read by the
  cell, the timeline and the spoken sentence alike.

- Fixed — the heading "Verlauf" stood twice, one directly above the other.

### Demo data

- Fixed — **the archive was empty.** The demo seeder fakes the Home Assistant
  side only; grows come from the database and nobody creates one. `/archiv`
  showed "Noch keine archivierten Grows", which meant the whole harvest and
  cost calculation (total, €/g) was invisible on any fresh machine. A finished
  grow with a harvest is now seeded when none exists.

### Checks

- New — **no two menu entries offer the same action.** The duplicate form got
  in because the menu link was checked and the target page never opened.
- New — **no page in the menu stands empty in the demo data.** With a test that
  empties the archive response and proves the check actually fires; a first
  attempt searched for class names and was green for no reason.
- New — impossible values are reported even where there is deliberately no
  target band (CO₂).
- New — the tests run in the same culture as the app, through the *same call*
  rather than a second setting with the same value.

## 2.0.0-beta.49

**Beta.** The phone could not reach the bottom of a page.

- Fixed — **the last content was unreachable.** `html, body { height: 100% }`
  made the `body` exactly one screen tall, so it became the scroll container
  itself. Those 100 % refer to the *layout* viewport, which is as tall as if the
  browser toolbar were already collapsed — what you actually see is less. The
  container therefore had zero reserve at the bottom: whatever the toolbar
  covered stayed covered, and scrolling further was impossible because the
  container was already at its own end. With the *document* scrolling, the
  browser collapses its toolbar as you scroll and the visible area grows.

  Measured at 375 × 812, distance from the last readable character to the bottom
  edge when scrolled all the way down: `/messung` **−293 px → +84 px**,
  `/settings` **−368 px → +80 px** (negative means it sat below the visible
  area — genuinely out of reach, two scroll containers nested). Six further
  pages went from a `body` scroller with 58–101 px to a document scroller with
  82–125 px.

  The same lesson is already written down in `10-grow-wizard-legacy.css:9`,
  where this line was removed once before. The twin rule in `reset.css` stayed.

- Fixed — **a page change kept the scroll position.** Reading a long page to the
  end and then tapping a menu entry opened the new page far down, its heading
  above the visible area. Measured: from `/wissen` at 2531 to the start page at
  2004, heading 1769 px out of sight. Now every push navigation starts at the
  top; going *back* still restores where you were.

- Fixed — the "more" menu had no reserve for the home indicator (13 px, while
  the indicator is 34), the tile dialog's rows ran 131 px past their own dialog
  on a phone, and the context card in the measurement form never actually stuck
  because its wrapper was exactly as tall as the card — sticky range zero.

Verified in a rebuilt Home-Assistant iframe: a single scroll container, the end
reachable, no second scrollbar. No regression across twelve widths from 320 to
1600 px, no contrast failure across 31 pages in both themes.

**Known and not fixed:** the save bar in the hardware form still does not stick.
The obvious explanation — a clipping ancestor — was **disproved** by measurement
(`clip` and `visible` put the bar in exactly the same place). The cause is
elsewhere and needs its own look; a rule that claims to fix it without doing so
would be worse than the defect.

Backend 1206, vitest 200, e2e 256, lint clean.

## 2.0.0-beta.48

**Beta.** The rest of the phone audit: waves two through four, all nineteen
individual findings, and one data defect that turned up along the way.

### Measured

Across twelve viewport widths from 320 to 1600 px, pages with an overflow went
from 19 to **0**. Contrast failures across 31 pages in both themes: **0**.

### What changed

- Fixed — **short labels were taken apart letter by letter.** "PART" stood as
  P/A/R/T, "Feminisiert" as a tower of six lines, a date across three. The
  protection list against this already existed; it was missing exactly the
  classes that show measured values.
- Fixed — **swiping a table moved the whole box.** Heading, help text, search
  field and buttons travelled with it; on the rules page the "apply targets"
  button was then gone entirely. Four pages now wrap the table alone.
- Fixed — **nothing showed that something could be swiped.** Tables, the
  timeline and the camera strip now carry an edge shadow that disappears once
  you reach the end. The tab bar wraps on a phone instead — a shadow would not
  help there, its scrollbar is deliberately hidden.
- Fixed — **buttons pushed themselves to the right and tore holes.** A layout
  instruction (`margin-left: auto`) sat on a *size* class, so with several
  buttons in a row the browser spread the free space across all of them: three
  buttons stood 243 px apart across a whole card.
- Fixed — **the text was squeezed because the button group would not yield.**
  In settings a sentence was pressed into 92 px and needed six lines.
- Fixed — **tap targets below the app's own limit**, and **five classes that had
  no styling at all** — a raw browser button, unstyled input fields, badges in
  body-text size.
- Fixed — **separators that separated nothing.** Where tiles wrapped, a line hung
  in mid-air beside the first tile of the second row while no line divided the
  rows.
- Fixed — **the timeline cut off its own labels.** Deliberately *not* with
  `min-width: fit-content`: that would make a ten-day phase wider than a
  twenty-eight-day one, and the bar lengths carry the duration. Smaller type on
  a phone instead, and a cut that at least looks like a cut.
- Fixed — **62 fixed minimum widths** in grids, in stylesheets and inline —
  `minmax(300px, 1fr)` becomes `minmax(min(100%, 300px), 1fr)`. This removes the
  whole class of defect rather than the reported instances; the last two
  hold-outs were an inline value in a component and a rule that did not exist at
  all above 860 px.
- Fixed — a card inside a card, a non-breaking space between number and unit,
  "Breeder"/"Seed Type" in German, a raw routine id, a navigation bar that was
  see-through in the dark theme.

### Deleting a grow left its warnings behind

`RiskEvents` has no foreign key to `Grows`, and `PRAGMA foreign_keys` is off by
default — so the warnings stayed, belonging to a grow that no longer exists:
open on the task page forever, never touched again by any sync, because that
only runs over active grows. Deleting a grow now takes them along. Existing
installations get their orphans **closed, not deleted** — they disappear from
the task page without destroying anyone's history.

### Six reviewers, 28 objections, 17 of them mine

Every change was checked by reviewers whose job was to refute it. They were
right 17 times. The worst: a rule aimed at "whatever comes first" in a row,
which turned a 5 px status dot into a 330 px bar. The swipe hint was invisible
because the table's own surface covered it. And three times I named a CSS class
that does not exist anywhere in the project — the rule looked right and did
nothing. All corrected and re-measured.

Backend 1206, MCP 56, vitest 200, e2e 256, lint clean.

## 2.0.0-beta.47

**Beta.** The phone. A tester said it plainly: "I sometimes have to hunt for
where a feature is hiding", and "there are still small display errors from too
much text". A visual audit of 30 pages at 375, 360 and 320 px produced 155
findings, which collapsed into 19 shared causes. This release takes the first
wave and the three findings that were not cosmetic at all.

### Three things that were not polish

- Fixed — **a running routine could not be ticked off on a phone.** The step row
  was 960 px wide on a 375 px screen and sat in no scrollable area, so it was
  simply cut. All twelve buttons (Start · Done · Skip across four steps) sat at
  x = 717…897 and were unreachable — by tapping or by swiping. The column widths
  were written into the component. They are now a stylesheet, and below 1100 px
  everything stacks. Measured after: widest box 375 px, unreachable buttons 0.
- Fixed — **the search had no stylesheet at all.** `app-search-hit`, `.k`, `.t`
  and `.s` were in the markup; no rule for any of them existed anywhere in the
  project. What applied instead was the default of a bare `<button>`:
  `text-align: center`, `padding: 0`, three spans butted together. On screen
  that read "SOPWurzelfäule-Behandlung" and "RegelBiofilm ist der Ausgangspunkt
  jedes RDWC-ProblemsWorkshop Lehrmaterial". The desktop had the same defect; it
  only showed less because a hit happened to fit on one line.
- Fixed — **three colours were written for the dark theme only.** In the light
  theme the addback assistant's three most important input fields stood as black
  boxes with black text (measured contrast 1.12 against a required 4.5), the
  camera label was unreadable, and an error message nearly vanished into white.

### The cheap lever

- Fixed — **`.v1-action-row` was never a row.** `flex-wrap` and `gap` were set,
  `display: flex` was not, so the box was an ordinary line of text: buttons met
  at 0 px, two borders reading as one thick line. One line of CSS, fourteen
  button rows.
- Fixed — **fixed minimum widths pushed pages off the screen.** `minmax(320px,
  1fr)` and `min-width: 170px` are promises the browser keeps even when the room
  is gone. On the grow detail page the strain picker stuck out 272 px. Five
  places now give the promise up when it no longer fits.
- Fixed — **cards had no space between their parts.** On the welcome page the
  button clung to the last word of the text, on nine cards.
- Fixed — **uppercasing labels turned micro into mega.** The style rule set every
  field label in capitals, so µS/cm became MS/CM and µmol became MMOL — a factor
  of 1000 — while pH became PH, °dH became °DH and mg/L became MG/L. On the water
  page eight of twelve labels were affected, and that is exactly where somebody
  types in their drinking-water report. The labels were already written correctly
  in the source; the styling was falsifying them.

### Developer words in a German interface

- Fixed — **the task list showed enum names.** The title was built from
  `deviation.Metric`, so a user read "Ec: Abweichung prüfen"; other readings
  would have produced "Ph", "Vpd", "Co2", "WaterTemp" or "DissolvedOxygen".
- Fixed — **the SOP page showed `Recurring`, `MultiDay`, `Active`, `Pending`,
  `Action`, `Measurement`, `Confirmation`, `Task #23` and the raw routine id.**

### Guard

- Added — a source-level test that fails on any **new** hardcoded colour in CSS.
  This trap has now sprung four times, and the existing contrast test kept
  missing it for a structural reason: the e2e run has no backend in CI, so a
  detail page like `/grows/1/addback` is only a loading state there — a contrast
  test on it is green without ever having seen the fields. The new test needs no
  server. The 24 existing hardcoded colours are recorded as a dated inventory,
  measured in the browser across 31 pages in both themes; that list may shrink,
  never grow.

Measured before and after on the running app: contrast failures on
`/grows/1/addback` 28 → 0, on `/zelte/1` 2 → 0; overflow on `/grows/1` 8 → 0;
gap between buttons 0 px → 8 px; touching pairs in cards 9 → 0 on `/start` and
7 → 0 on `/release`; falsified units 10 → 0 on `/wasser` and 9 → 0 on
`/messung`. Across twelve viewport widths from 320 to 1600 px the pages with
findings went from 19 to 3, and the three that remain were there before and are
unchanged by this release.

Backend 1200, vitest 200, e2e 256, lint clean.

## 2.0.0-beta.46

**Beta.** The last of the 37 findings. All of them are now closed.

- Fixed — **CO₂ could not be put on the start screen at all.** "+ Kachel"
  offered it; the resulting tile then showed the raw key as its name ("co2") and
  "—" as its value, while the actual reading sat right there in the same
  response. The tile resolver only knew the two fixed bands, and CO₂ is in
  neither. PPFD was hit whenever a light state was reported.
- Fixed — **"Heute fällig" left out overdue routines**, so clicking "Alle" led to
  a page listing items the start screen claimed did not exist.
- Fixed — **the timeline counted differently than the server, twice.** An
  autoflower never reached flowering, because the start of flowering came only
  from the flip date — which an autoflower does not have; the same screen showed
  "Veg day 70" beside flowering targets. And "days already in phase" was
  ignored, so a grow entered mid-run started at day 1 on the bar while the app
  counted on internally.
- Fixed — **69 places showed raw developer identifiers.** The pathogen and
  treatment files reference 65 symptom keys that have no entry, so
  "slimy-roots-foul-smell" stood in the middle of German prose. All 65 are
  translated and shown as keywords rather than dead links. Nothing was invented —
  only the existing English term rendered in German. A test reads the shipped
  knowledge base and fails as soon as a new keyword arrives without one.
- Fixed — two charts could be open at once, one per tile row; "0 /100" filled
  the score ring while "Nicht bewertet" stood beside it; the knowledge search now
  folds umlauts; and two MCP tools gave misleading answers — `pflanzen` returned
  an empty list for a grow that does not exist (reading as "has no plants"), and
  `sorte` claimed no strain while `grows_auflisten` named one for the same grow.

## 2.0.0-beta.45

**Beta.** The rest of the findings from the same pass.

- Fixed — **the measurement form silently dropped what it could not read.**
  `parseNullableNumber` returns the same thing for "empty" and for "unreadable":
  `null`. Mistype the pH as "6,2x" and the measurement saved *without* a pH — and
  reported success. The value was gone, nobody said so, and the next look at the
  curve is simply missing a point. 21 fields were affected. Saving now stops and
  names the field the way the form does ("pH (Reservoir)", not "reservoirPh").
- Fixed — **a jar could be neither edited nor deleted, and "Fertig" was the only
  action in the whole app with no way back.** One misclick and the jar was out of
  the list, its burping rhythm stopped, recoverable only through the database.
  Curing runs for weeks; in that time you do misclick. There is now delete and
  reopen, and the page shows recently finished jars so you can find one at all.
- Fixed — four mistakes in yesterday's curing code: "0 minutes burped" reset the
  next date without a jar ever being opened; with a humidity pack the text said
  "daily" while the date said "every 2 days"; the day count compared UTC dates,
  so between midnight and 2 a.m. the jar was a day younger than it stood there;
  and an unknown strain id hit the foreign key and ended in a 500 — a server
  error for input that was simply wrong.
- Fixed — **`finishedAtUtc === null` is always false.** The server omits empty
  fields entirely, so `undefined` arrives. The hint pointing to the curing page
  never appeared, and the freshly built reopen button would have shown on every
  open jar.

## 2.0.0-beta.44

**Beta.** A full pass over the running app — ten testers clicking and calling,
every finding reproduced independently before it counted. 37 held up. The two
worst were not in the new code.

- Fixed — **the save button on the measurements page did nothing.** It sat in a
  form, it said "Messung speichern", it was clickable, it showed no error — and
  the measurement was gone. `V1Button` defaults to `type="button"`, which never
  submits a form. Nothing but a click reveals this, which is why it survived
  every build and every test. A test now walks every `onSubmit` form and checks
  that some button actually submits it.
- Fixed — **between 768 and 860 px there was no navigation at all.** The sidebar
  appears from 861 px, the phone bar disappeared at 768 — measured: zero visible
  links in that gap. An iPad in portrait is 768 px, as are most wall tablets;
  opening the app there left you on the start page with no way out. Both rules
  read correctly on their own; the bug lives in the distance between them. A
  test now walks thirteen widths from 320 to 1920.
- Fixed — **"Kritisch" was drawn in the warning colour.** Critical had yellow,
  warning had grey, info had green: every message looked one step milder than it
  is, exactly where urgency should be readable at a glance.
- Fixed — **every link in the app used the wrong green.** 3,4:1 on white in any
  table. The note beside the colour token says outright that this shade only
  manages 3,6:1 as small text and that a darker text variant exists for it — the
  central rule for every link just never used it. Along with it: error messages
  (4,1:1 — the sentence you most need to read), the pump state at 2,2:1, the
  drag handle in edit mode, the emergency button, the tent labels. The contrast
  test itself was too blunt to catch any of it: threshold 3,0 instead of the
  WCAG 4,5, no knowledge of `oklch()` colours, a stale page list. Sharpened —
  and it immediately found the error-message colour, which a pass through the
  running app cannot find, because errors only appear when the backend is gone.
- Fixed — **the flip button was offered to autoflowers and always failed.**
  Autoflowers do not flip; the server rejected it with a 400 every time. The
  comment right beside the condition already knew it — the condition did not.
- Fixed — **no way to the harvest page without a manual measurement.** The
  button hung on the last hand-entered reading instead of the phase the server
  calculates, so it vanished entirely for anyone letting sensors do the work.
- Fixed — **the export button navigated away and showed raw JSON**, and its
  path broke behind the Home Assistant ingress. It now downloads a file.
- Fixed — **three MCP tools answered a different question than the one asked.**
  `alarme` returned the open risks of every grow: the filters were one if-else
  chain with `openOnly` at the front, swallowing the grow filter behind it.
  `technik` fetched maintenance and calibration with no filter at all, so a
  second tent's dates were reported as this one's. `grows_auflisten` with
  "include finished" switched to the archive instead of adding to it, losing
  precisely the running grows. None of these fail loudly — they return a
  plausible answer to a question nobody asked, and an AI reading them cannot
  tell.
- Fixed — **the guided-start button on the emergency cards started nothing.**
  There is no grow context on the knowledge page, so it could only ever open the
  text. It now says so.
- Fixed — **the photo upload replied with an id of 0.** A 201 Created naming the
  wrong resource.

## 2.0.0-beta.43

**Beta.** The last step of a run — the one that decides what months of work
taste like — now happens inside the app instead of beside it.

- Added — **curing.** Grow OS accompanied a plant to its dry weight and then
  stopped. Worse: saving the harvest sets the grow to *completed*, so it left
  the overview at the exact moment curing began — and curing runs another 30 to
  60 days. There is now a jar: when it was filled, what is in it, whether a
  humidity pack sits inside. You log what the hygrometer reads and how long you
  burped — separately, because they are separate things: burping without
  reading keeps the rhythm and teaches nothing, reading without burping knows
  everything and does nothing.
- Added — **the burping rhythm, with its source.** Week 1 daily for 5–10
  minutes, week 2 every 2–3 days for 2–3, weeks 3–4 weekly for 1–2, and from day
  30 the hygrometer decides rather than the calendar — inventing a date past
  that point would be false precision. With a humidity pack the interval
  stretches but does not disappear: a pack exchanges moisture, not air. Sources:
  atmosiscience.com for the rhythm, budtrainer.com for the 58–62 % window.
- Added — **a humidity verdict for the jar**, built like the water traffic light:
  every threshold names its source, and every reading says what to *do*. Both
  directions matter. Too damp is the known danger — above 65 % mould grows in a
  closed jar. Too dry is underrated: below 55 % the terpenes go brittle and
  leave, and unlike too damp that cannot be repaired. The upper bound is the
  same 62 % the mould guard already used for the cure stage; a test keeps the two
  from drifting apart.
- Added — **jars show up under Aufgaben**, deliberately without filtering by
  grow status. Every other list on that page asks for running grows, which is
  exactly why the jars were never in one.
- Added — **your own photos as reference for a symptom.** The knowledge base
  describes 20 symptoms and 8 pathogens and never had a single picture. Foreign
  example images are not available without infringing someone's rights — and
  your own shot is the better comparison anyway: same light, same camera, same
  room. Tag a photo in the journal with what it shows, and it appears under that
  symptom in the knowledge base. By the third case of root rot you can see how
  the first two looked.
- Added — **two more MCP tools**: `aushaerten` (jars, humidity, next burp) and
  `symptom_bilder` (your reference shots for a symptom). Twenty-two tools now.
- Fixed — **clicking a tile did nothing.** Reported from the field: the tile
  click that arrived in beta.38 was only ever wired into the *custom-arranged*
  dashboard. Anyone who never saved an arrangement sees the plain metric band —
  and that band never passed the handler down. The tiles looked identical and
  did nothing, which is the default case, so it hit the majority. Both views now
  open the same 24-hour chart in the same place: below the row, not over it, so
  the neighbouring tiles stay visible for comparison. Tiles without history stay
  silent rather than offering a click that shows nothing.
- Fixed — **`foto_ansehen` never returned an image.** The path was assembled
  twice (`uploads/uploads/4/x.jpg`), so every request 404'd. The tool shipped in
  beta.42 and did not work once. Verified against the running app afterwards:
  the correct path now returns `image/png` with a real PNG signature.
- Fixed — **the photo upload replied with the wrong id.** A `201 Created`
  carrying `"id": 0` — a response that points at a resource and names the wrong
  number. The web interface reloads the list and never noticed; anything using
  the API directly, for instance to tag the fresh photo with a symptom, was
  pointed at nothing. Found while testing the app end to end.
- Fixed — **an existing database would not have survived this update.** The new
  index on the photos table was created in the core schema, which runs *before*
  the column it indexes is added to an existing database — "no such column:
  SymptomId", and the add-on would not start. It never showed in the test suite
  because every test builds a fresh database, where the column is already in the
  CREATE TABLE. It showed the moment the app was actually started. There is now
  a test that downgrades a real database and runs the update over it.

## 2.0.0-beta.42

**Beta.** Two things a lot of features had quietly cost: the menu had grown
past its own structure, and on the phone the picture stood in front of the
action.

- Changed — **the menu is sorted by how often you touch a thing.** "Anlage" had
  become a catch-all with eight entries: five you set up once and never open
  again, three you reach for while a grow is running. Those three sat buried
  between the tent setup and the Home Assistant mapping. There are now five
  groups — *Jetzt* (daily), *Pflanzen* (a few times a week), *Betrieb*
  (dosing, sensors, rules, setpoints — the running grow), *Einrichtung* (tents,
  hydro, water, Home Assistant, phone), *Wissen*. The phone's bottom bar is
  unchanged; it is still the four daily targets.
- Fixed — **on the phone, the critical risk stood behind the camera.** The
  camera stage occupies 260 px whether or not a camera is even mapped, and on a
  narrow screen the right-hand column drops below it — so "critical risk" and
  "due today" sat a full screen height below the fold, behind a picture. The
  source order now puts the action first and `order` pulls the camera back to
  the left wherever both columns fit, so nothing changes on a wide screen. The
  stage also collapses when no camera is mapped.
- Fixed — **every page could be dragged wider than the phone.** Same trap as
  the live screen in beta.40, one level up: `.v1-page` is a single-column grid,
  and a grid child's minimum width is its content's. One long Home Assistant
  entity name made the column 423 px wide on a 351 px screen. All 23 pages now
  measure clean at 375 px.
- Fixed — **Home Assistant entity fields showed a third of what you typed.**
  144 px of a name that needs 266. On a phone the field now drops below its
  label and takes the full width.
- Added — **the shopping list has its own page and its own menu entry.** It
  existed since beta.36, but folded shut at the foot of the knowledge page,
  with no menu entry and no search keywords: typing "Einkaufsliste" into the
  search box returned "Nichts gefunden" — in exactly the situation the list is
  made for. It also no longer clings to the bottom of an opened SOP.
- Fixed — **menu and page said different things.** The menu said "Wasser", the
  page "Leitungswasser". The menu said "Eigener KI-Berater" while the tab next
  to it said "Mappe für eigene KI", with a comment stating outright that Grow
  OS contains no AI. "Addback" opened a page titled "Reservoir". All of these
  now match, and a test walks every menu entry to check that the page's
  breadcrumb names the group it was reached through — this mistake had already
  happened three times.
- Changed — **the grow's export button moved into Verwaltung.** A file download
  is not a daily action, and in the header row it cost a button's width on the
  phone, next to Addback and the phase buttons.
- Added — **the MCP server can hand a photo to your own AI.** Two new tools:
  `fotos` lists what has been photographed for a grow, `foto_ansehen` returns
  the image itself together with a note saying what it shows — subject, age,
  your caption, and the measurement it belongs to. Brown roots after a water
  change mean something different from brown roots in week seven, and a model
  that only gets pixels cannot know which one it is looking at. This keeps the
  arrangement Grow OS is built on: the picture lives here, the AI sits outside.

## 2.0.0-beta.41

**Beta.** A bug report with teeth: the multi-strain tent from beta.38 was only
half built. The plants could carry their own strain — nothing that *counted*
them knew about it.

- Fixed — **a tent with three strains only counted one.** Six plants, two of
  each strain: the strain library showed "1 run" for the grow's main strain and
  "0 runs" for the other two, as if they had never been planted. Runs now count
  through both paths — the grow's main strain *and* the strains of its plants —
  and a grow with two Mimosa plants counts as one run for Mimosa, not two.
- Fixed — **the pheno hunt mixed genetics.** All six plants were listed as
  candidates of a single strain, and worse, they were *scored together*. Yield
  and potency are graded relative to the field (best plant 1, weakest 0), so
  across strain boundaries that grades the genetics, not the phenotype: a
  strain that simply carries less would take the zero, however good its best
  plant is. Anyone picking a keeper from that could throw away the very plant
  they meant to keep. Hunts are now scored per strain — plants without a strain
  form their own group, for the same reason.
- Fixed — **the average yield was wrong even for the main strain.** It divided
  the whole grow's dry weight by the whole grow's plant count, so in a mixed
  tent the main strain was credited with everyone's harvest. Harvests are
  recorded as a total with no link to the individual plant, so there is no
  honest way to split them: mixed grows now show "— gemischt" instead of a
  number that looks precise and isn't.
- Fixed — **deleting a grow reported a failure after succeeding.** The audit
  entry for the deletion was written *after* the row was gone, and its foreign
  key had nothing left to point at — a 500 for a job that had worked, which
  invites a second attempt. Found while cleaning up test data of my own.

## 2.0.0-beta.40

**Beta.** Field feedback, third round — the phone one turned out to be a layout
bug that had been hiding behind an unrelated safety net.

- Fixed — **the phone cut off the right edge.** A tester had to turn the device
  sideways to see the VPD tile. The live page is a grid with a single column,
  and a grid item's minimum width is the width of its widest content — so the
  phase timeline (whose segments carry `min-width: fit-content` and share the
  width by day count, measured up to 711 px) pulled the whole page wider than
  the screen. Every tile row followed, and the rightmost tile of each row lost
  its edge. Now the children may shrink, and the timeline scrolls sideways in
  its own box instead: the bar lengths *are* the durations, so wrapping them
  would destroy what they say.
- Fixed — **wide tables were unreachable on the phone.** The strain and rules
  tables set a 560 px minimum inside a scroll box — but the panel class brought
  `overflow: hidden` and won on equal specificity, so the right-hand columns
  were simply clipped away instead of swipeable.
- New — **push notifications land where the work is.** Tapping a Grow OS
  notification opened the Home Assistant home screen, leaving you to navigate to
  the actual warning yourself. Messages now carry a target: calibration and
  maintenance open **Sensoren & Wartung**, everything else opens **Aufgaben**,
  the daily digest opens the live view. Without a target the payload is
  byte-identical to before.
- New — **the water you actually used, per operation.** A grow carries one water
  source for the whole run; a top-up done with tap water because the RO tank was
  empty had nowhere to be recorded. Addback and water change now hold the source
  and its EC, pre-filled from your water profile (RO uses the value *after*
  treatment) and overridable per operation.
- Changed — **two labels that pointed the wrong way.** "Leitungswasser" is now
  **Wasser** — that page has held your treated water since beta.37, and a tester
  went looking for it under Anlage → Wasser. And the grow tab labelled
  "KI-Berater" is now **Mappe für eigene KI**: there is no AI inside Grow OS,
  the page hands you a dossier for your own agent, and the old name promised a
  feature that does not exist.

## 2.0.0-beta.39

**Beta.** The follow-ups from the audit — and one of them turned out to hide a
real bug.

- New — **you can finally say "I calibrated it".** The app has always shown
  "pH probe due in 3 days"; there was no way anywhere to record that you did
  it. Now every due row on **Sensoren & Wartung** has a button: date,
  optionally the reference solution and the before/after readings, a note, and
  a checkbox for "the probe won't take the reference any more". Maintenance
  works the same way with just a date.
- Fixed — **the calibration cycle would have stopped after the first entry.**
  Completing a calibration calculated a next-due date, but the reminder only
  ever reads *planned* entries with a due date — and nobody created one. The
  first "done" would have silenced the reminder for good, and a pH probe that
  nobody nags about drifts quietly. Completing now schedules the next
  appointment (from that device's interval), including after a failed
  calibration — that is exactly when you want to be reminded. Four tests pin
  the cycle down.
- Fixed — **unknown API paths answered "200 OK" with the start page.** The SPA
  fallback caught every unmatched route, `/api/…` included. A typo in a client
  path looked like success, a request to an endpoint that doesn't exist
  reported 200 and did nothing, and anything expecting JSON got HTML and a
  parse error that pointed nowhere near the cause. Found the honest way: while
  cleaning up test data, three DELETEs reported 200 and deleted nothing. API
  paths now return a proper 404 with the usual error body.
- Changed — **the rest of the dead CSS is gone.** The retired OPS-1 operations
  views and the Sweetspot score colours (90-operations.css: 195 → 140 lines),
  plus four orphaned wizard rules. The three files left on the audit's list
  are now clean — with one correction: the "dead" classes flagged in
  `10-grow-wizard-legacy.css` only ever appeared in the comment describing an
  earlier cleanup. Nothing to remove there.

## 2.0.0-beta.38

**Beta.** A full audit of the codebase — every finding fixed in the same
release. An agent swarm read the app five ways (dead code, backend logic, API
contracts, and twice through a tester's eyes); each serious finding was then
independently re-verified before anything was changed.

- Fixed — **editing a grow silently erased its history.** The edit form
  rebuilds the whole row, and it never knew about confirmed milestones
  (germination, veg, finish), the end date, the night-ramp switch or the feed
  chart opt-in. Changing a note and saving reset the week counter, took the
  archive its runtime and turned the ramp off — without a word. All of it is
  now preserved unless the form actually carries the field, and a round-trip
  test pins each one. The mix-plan program also travels by its stored id now,
  so a failed knowledge fetch can no longer null it on save.
- Fixed — **saving a tent disarmed the night ramp.** Every tent save (even
  just remapping a sensor) cleared the ramp's target device, and the switch on
  the grow kept saying "on" while nothing was written to Home Assistant. Tents
  from before the multi-camera list also lost their camera on edit. Both fields
  survive now.
- Fixed — **hard water broke the water profile round trip.** 1234 µS/cm was
  displayed as "1.234" (German thousands separator) and read back as 1,234 —
  a thousandth of the value, silently. Large values now render without
  grouping, and the parser understands both German and meter-display notation.
- Fixed — **autoflowers got the wrong feed chart column.** Without a flip date
  the mix plan used the week since seed as the *flower* week — two to four
  columns too far right, wrong millilitres, wrong EC target. Flower weeks now
  count from the calculated flower start, the same anchor the stage resolver
  uses. An autoflower entered mid-grow also no longer hangs in "transition"
  for weeks: the brought-along days move the anchor too.
- Fixed — **auto-measurements were timestamped two hours in the past** (UTC
  written into a local-time column — the same family as the old UTC bug, one
  more member found). Daily statistics also bundled 02:00–02:00 instead of a
  real calendar day, the daily dosing limit reset at 02:00 (so a pump could
  legally double its "per day" cap within one local day), and the nightly
  aggregation could skip its five-minute window and lose a day for good. All
  four clocks now agree on what "a day" means.
- Fixed — **the week counter announced "Blüte Woche 0"** when a flip date was
  entered ahead of time; it now stays vegetative until the date arrives, like
  the rest of the app. A light schedule with identical on/off times no longer
  counts as 24 h of light in the cost estimate. A recovery push that could not
  be delivered (quiet hours, HA down) is retried instead of silently dropped —
  the same rule the alarm path already had.
- Fixed — **small tester traps.** "Blütezeit kürzeste zuerst" sorted exactly
  backwards and put unfilled strains first. With two pheno hunts open, the
  weights button only ever worked in the first panel. Journal milestones from
  "Veg bestätigt" and "Finish begonnen" rendered as grey notes. The SOP button
  on risk cards showed even when the risk had no grow. Editing a grow with a
  custom nutrient program showed an empty program line. Resolving a sensor risk
  event after re-saving the tent mapping could crash on a stale sensor id.
- New — **tasks can be deleted.** The endpoint existed, complete with audit
  log — no surface ever offered it. Each open task now has a quiet ✕ next to
  "Erledigt", for the appointments that should never have existed.
- Changed — **roughly a thousand lines of dead code are gone.** A never-used
  server chart pipeline, a template repository seeded on every start for a
  feature that never shipped, duplicate alert endpoints, two still-armed legacy
  MVC write routes (now properly 410), five orphaned service methods, a dead
  status service, and the CSS of three retired designs (the "Instrument
  Cluster" live dashboard, the five-step hydro wizard, the second app shell,
  the removed AI advisor). Nine identical copies of the same error formatter
  became one. Detail routes that answer Create's Location header stayed —
  deleting those would have traded dead code for a live crash.

## 2.0.0-beta.37

**Beta.** Second round of field feedback: one display bug that looked like data
loss, and the strain library grows up.

- Fixed — **the start date seemed to vanish when editing a grow.** Set a start
  date, save, edit again: the field showed empty. The date was stored the whole
  time — the API returns dates with a time part, and a date input can only
  display `yyyy-MM-dd`; anything else renders as blank. The form now trims what
  it loads (flip date too), and a round-trip test pins the format at both ends.
  The lesson is bigger than the fix: nothing had ever tested
  create → save → reopen.
- New — **breeder specs on strains.** Seed type (feminized / automatic /
  regular), THC %, CBD %, sativa share, taste, effect, aroma, indoor yield and
  height — the fields from a seed shop page, so the separate spreadsheet one
  tester kept can retire. All optional, all labelled as breeder claims.
- New — **the strain library filters and sorts.** Search across name, taste,
  effect and aroma; filter by seed type; sort by name, THC, flowering time or
  yield. Strains without a value sort to the end — an unfilled strain must not
  look like the best one.
- New — **your water after treatment.** If you run RO or a desalination stage,
  you mix with what comes out of it, not with what the city report describes.
  The water profile now takes your own measured EC and pH after treatment; the
  water rating judges that as the real starting point (and a treated EC that is
  still high points at the filter or membrane), while the city report stays as
  the reference it is.

## 2.0.0-beta.36

**Beta.** Four things the app already knew but never said.

- New — **diagnosis from the plant.** Everything on the diagnosis page argued
  from numbers: pH speed, EC behaviour, oxygen, ORP. Someone looking at a
  yellow leaf found nothing there — while twenty symptoms and thirty treatments
  sat in the knowledge base, reachable only if you already knew what to search
  for. Now you start where you actually start: **leaf · root · solution**, pick
  what you see, and get the possible causes, what to check yourself, and what
  helps. No AI — three questions and a list; the links have been in the symptom
  files all along. Routine entries in those same categories (a preventive
  measure, a cutting ready for the system) are not offered as findings: an
  entry without possible causes is not something to diagnose.
- New — **the shopping list.** Every procedure carries the materials it needs,
  and they were only ever read when printing the binder. They are now one list:
  each thing once, grouped the way you shop, with the procedure that needs it —
  and what several procedures need comes first.
- New — **two rules from the SOP author, written down.** *pH 5,8–6,2: let it
  swing, don't correct* — the drift itself says whether the plant took cations
  or anions, and a dosing pump held to one number erases that signal. And *pick
  the acid to match the phase*: nitric acid in veg brings nitrogen, phosphoric
  acid in flower brings phosphorus; the leftover becomes a nutrient instead of
  ballast.
- Changed — **the aeration check names both rules of thumb.** 0,10 L/min per
  litre is the lower edge from the DWC literature, 0,5 the optimum per SKX.
  Above the optimum the app now explains what happens (air pockets under the
  lid, exposed roots drying) — with room, because a rule of thumb is a target
  and not a cliff, and without telling you to rebuild a rig that works.

## 2.0.0-beta.35

**Beta.** A history curve on the live screen, if you want one.

- New — **the 24-hour history tile.** Several readings in one picture, the way
  a tester had built for himself outside the app: temperature, humidity, VPD
  and CO₂ over the same time axis, with a cursor that puts the values of that
  moment under the chart. **You add it yourself** in the customise mode — it is
  never there uninvited — and it takes the readings of the section you put it
  in. Each line is scaled to its own range, because 25 °C, 69 % and 0,64 kPa on
  one axis makes VPD a flat line on the floor. Readings that can never draw a
  curve (light is a state, not a number) are not offered.
  - The tile needs no new data: the live screen already loads these 24 hours
    for its sparklines.
- Fixed — **"Regeln & Automatik" looked like it held every automation.** It
  holds thresholds, automatic measurements and notifications; dosing sits with
  the pumps and the night ramp with its grow. The page now names those two in
  its own subtitle instead of leaving you to conclude they don't exist.

## 2.0.0-beta.34

**Beta.** Saying which page does what.

- Fixed — **"why can I assign sensors in two places?"** You couldn't, but the
  page said you could: *Sensoren & Wartung* advertised "HA-Mapping" in its own
  subtitle while its form deliberately cannot set one. The mapping has always
  lived on the Home Assistant page, and measured sensors appear in the
  inventory by themselves. Both pages now say what they are for and what the
  other one is for, and the empty "nicht gemappt" cell is a link to the place
  that can actually fix it instead of a dead end.
- Fixed — **"Regeln & Automatik" holds only one of the automations.** The page
  is about automatic measurements; dosing sits with the pumps and the night
  ramp sits with its grow. It now says so and links there, instead of leaving
  you to conclude the others don't exist.
- Fixed — **"Release & Daten" had no link anywhere.** A full page reachable
  only by typing the URL. It is now linked from Settings, next to Onboarding.

## 2.0.0-beta.33

**Beta.** Two bugs a tester found, both real.

- Fixed — **a mapped light sensor read as "no sensor mapped".** If your light
  entity reports a brightness (`100.0` %, lux, watts) instead of on/off, the
  tile showed nothing and then claimed nothing was mapped — sending you back to
  a mapping screen that was already correct. Brightness now counts: anything
  from 1 upward is lights-on, below that is off. And when a value genuinely
  can't be read, the tile says what arrived (`Sensor liefert „xy"`) instead of
  denying the sensor exists.
- Fixed — **"done" on a deviation did nothing.** Marking a deviation risk as
  resolved dropped it out of the duplicate check, so the very next sync created
  it again from the same unchanged measurement. The button was visibly
  pointless; only adding a new measurement ever cleared it. Now resolved means
  resolved until new data arrives — and if a **newer** measurement still shows
  the deviation, it reports again, because that is news.

## 2.0.0-beta.32

**Beta.** Crop steering, the way it actually works in water.

- New — **the night ramp.** In substrate you steer a crop with dry-backs. In
  a recirculating system the roots never leave the water, so that lever does
  not exist — the lever here is water temperature. The night target now drops
  one degree per flowering week down to a floor, which is the method SKX (the
  author of this app's RDWC procedures) calls the *cold morning routine*.
  - **The whole plan is visible before anything is switched.** Week by week,
    day and night, with the current week marked. An automation whose effect
    you only notice at the chiller has no business in a grow room.
  - **Grow OS plans, Home Assistant regulates.** Twice a day — at lights-on and
    lights-off — Grow OS writes a setpoint to your thermostat or number entity.
    The control loop with its hysteresis stays where the probe and the relay
    are. If this add-on crashes or updates, Home Assistant keeps the last
    setpoint and the ramp simply pauses; nothing swings.
  - Off unless you switch it on, per grow. A floor you set, and a hard limit
    of 12 °C the floor can never go below. Every write goes to the audit log.
  - Before the flip, nothing is written — a guessed flowering week would move
    a real chiller.

## 2.0.0-beta.31

**Beta.** Three watchers, and the timeline finally reaches the end.

- New — **the pump watch.** `pump-air` had been a configured metric since
  forever and no service ever read it. Worse: a threshold rule on it could
  never have fired, because alert evaluation needs a numeric value and an
  on/off state has none — the rule would save, display, and stay silent. Now
  a stopped pump raises the alarm within a minute, on your phone and in the
  app. It reads **both signals**: the state from Home Assistant, and the
  power draw of a metering socket when you have one — that second signal
  catches the pump that reports "on" and moves nothing. The message says what
  it costs and what to do right now. A grace period (default 15 minutes,
  adjustable) keeps interval operation quiet. **This one ignores your
  companion level:** unsubscribing from reminders is not unsubscribing from
  the warning that saves the run.
- New — **wear, inspection and backup are watched too.** Every device carries
  an expected lifespan and an inspection interval; both were only ever
  prefilled from the template and stored. Now they produce due items by
  themselves, computed from *your* numbers and the install date — the app
  invents no lifespans. Plus a backup reminder: without one, everything since
  the last copy hangs on a single SD card.
- New — **the timeline runs to the end.** It used to stop at harvest, which is
  not when a run is finished. Drying and curing are now part of it, with an
  estimated ready date. The durations are sourced (drying 7–14 days, curing
  14 days minimum and 30–60 typical) and labelled as guide values, not
  deadlines.
- Fixed — **the mobile navigation was unreadable in the light theme.** The bar
  carried a hardcoded dark green while the labels followed the theme:
  measured contrast 1.12 where 4.5 is the minimum. It hit the primary mobile
  surface. The contrast test that exists for exactly this trap was measuring
  `main *` only — navigation and header sit outside it and had never been
  checked in any width or theme. Both are fixed: the colour, and the test's
  reach.

## 2.0.0-beta.30

**Beta.** Your tap water gets an opinion, and the feed chart can set the targets.

- New — **the water traffic light.** Your water profile held numbers and said
  nothing about them. Now every value you entered gets a sentence: what a
  carbonate hardness of 10 °dH does to your pH through the week, what a source
  EC of 1,4 mS/cm leaves you for fertilizer, whether that sodium figure matters.
  **Every threshold names its source** — Penn State Extension for the
  horticultural limits, the German detergent law (WRMG § 9) for soft/medium/hard
  — because a traffic light that turns red without cause sends you shopping for
  a reverse-osmosis unit you don't need.
  - Soft water reads as an **advantage**, not a deficiency: the greenhouse
    guidance assumes irrigation water is your calcium source; in a recirculating
    system your feed program is. Only without a CalMag-carrying program does the
    note appear.
  - Calcium, magnesium and nitrate deliberately get **no verdict** — just the
    reminder to count them toward your feed instead of adding on top.
  - Anything you left blank stays silent. A half-filled report is the normal case.
- New — **the feed chart can set your targets.** Under the mixing plan there is
  now a checkbox: use these weekly targets on screen too. Off by default — the
  chart is a manufacturer's suggestion, not a rule. Switched on, its EC and pH
  reach the live tiles, the advisory report and the dosing suggestion, so you
  never read "target EC 1,5" while mixing and something else on the dashboard.
  Only EC and pH are adopted; temperature, ORP and light stay with your phase
  profile, which is all the chart knows. The single EC number moves your
  existing target band without narrowing it — a target with no width would
  put every reading off-target.

## 2.0.0-beta.29

**Beta.** The app stops hoarding its knowledge.

- New — **the mixing plan.** Pick your nutrient program on the grow (the
  program card now actually binds) and the addback page tells you what goes
  into the bucket: "Bloom A 137 ml · PK 341 ml" — computed from the program's
  week chart and your system volume, with EC and pH target for that week.
  The Athena Blended chart ships digitized (mL/gal converted to mL/L, source
  named). Weeks beyond the chart keep the last column instead of going silent.
- New — **the due-routine watch.** Your procedures have always carried their
  rhythms ("water change every 7 days, warn after 8, critical after 10") —
  and nothing ever read them. Now the app does: with a running grow, overdue
  routines appear under Aufgaben by themselves. "Last done" comes from what
  you already record — the solution-change mark of a measurement, or a
  completed procedure.
- New — **companion level.** Full guidance (default), important-only, or
  expert. The expert gets no unsolicited reminders — only the alarms they set
  themselves. Nothing is hidden; nothing is pushed.
- Fixed — **the beta.27 Blended procedure change never actually shipped.** It
  was written to the runtime knowledge copy instead of the shipped defaults —
  the changelog promised it, installations never received it. Both it and the
  new feed chart now live in the shipped defaults, guarded by a test that
  reads exactly there.

## 2.0.0-beta.28

**Beta.** No more 5 °C advice for seedlings.

- Fixed — **the derived temperature target could recommend absurd cold.** The
  temperature band is back-calculated from the VPD target and the measured
  humidity. Physics found "5.3 °C" for a seedling at 54 % RH — correctly
  computed, agronomically nonsense. Temperatures outside 18–32 °C are no
  longer recommended; a band reaching into that range is trimmed (every value
  inside still hits the VPD target). Where no sensible target exists, the tile
  now names the real lever: raise the humidity — for seedlings, with the
  concrete tool (dome or humidifier).

## 2.0.0-beta.27

**Beta.** What did this grow cost — and the Blended kit gets its own path.

- New — **cost per grow.** Enter your electricity price (Einstellungen) and a
  price per litre on each dosing pump; the archive then shows what a finished
  run cost, down to €/g next to the yield. Everything is calculated and says
  so: power is lamp watts × light hours × days (a lower bound — side consumers
  are excluded on purpose), nutrients come from the dosing log. Hand additions
  are in no log, and the report says that too.
- Changed — **the soft-water mixing procedure knows Athena Blended.** Blended
  Balance contains silicate; running it alongside a separate potassium
  silicate would double-dose. The procedure now asks which you run: with
  Balance, it moves to the FRONT of the mixing order (per Athena) and the
  separate silicate step disappears. Sourced from Athena's own Balance
  documents. The addback procedure carries the same warning.

## 2.0.0-beta.26

**Beta.** For growers without permanent probes — the app now carries both worlds.

- New — **every value shows where it came from.** Tiles have always fallen back
  to your hand measurements when no sensor is mapped; what was missing was the
  honesty. A hand-measured value now reads "Hand · vor 2 Std" — and after 36
  hours it turns into "Hand · vor 3 Tagen — nachmessen?". A pH from last week
  no longer poses as live data. Each metric carries its own age: a merged
  report used to stamp everything with the newest timestamp.
- New — **the aeration check.** Most growers have no DO meter, but everyone
  knows the number on the pump box. Enter your air pump's L/h on the hydro
  system and Grow OS tells you whether it is enough for your volume — green,
  tight, too little, or too much (throttle it: excessive turbulence damages
  young roots). Clearly labelled as a calculated rule of thumb, never shown as
  a measured mg/L.
- New — **the DO tile knows physics.** Without a DO reading it now shows the
  ceiling: how much oxygen your water can hold at its current temperature
  (USGS solubility table). Warm water holds little — cooling first often beats
  buying a bigger pump.

## 2.0.0-beta.25

**Beta.** Tap water gets a profile — and a long-dead field gets a job.

- New — **the tap-water profile** under *Anlage → Leitungswasser*. Every city
  publishes a drinking-water report; enter its values once and Grow OS knows
  what your water brings before the first drop of nutrients: starting EC,
  hardness (classified soft/medium/hard along the legal bands), Ca/Mg, the pH
  buffer, the disinfectant. The fields follow a real report — lay your PDF next
  to the form and every number has a place.
- New — **the situation report opens with the source water.** Without it, an
  advisor reads "EC 0.28 before feeding" as residual salt and recommends a
  water change that would change nothing. The advisor folder and the Claude
  connection get this automatically.
- Fixed — **the water question your procedures kept asking.** A grow has
  carried a water source (tap/RO/mixed) since forever — entered on creation,
  read by nothing. Now the "what are you mixing with?" question at the start of
  a procedure comes pre-answered from the grow. Preselected, not enforced.

## 2.0.0-beta.24

**Beta.** The advisor gets a door — and a second add-on to walk through it.

- New — **Grow Berater, a separate add-on.** The folder from beta.23 works, but
  it has to be carried somewhere by hand. The advisor add-on skips the errand:
  a chat page in the Home Assistant sidebar that fetches the situation and the
  knowledge from Grow OS on every question. Same source as the download, so the
  two cannot drift apart. Anthropic, OpenAI or Ollama — with Ollama nothing
  leaves the house. Install it or don't: Grow OS itself still has no AI and no
  key.
- New — **read access from the internal add-on network** (172.30.32.0/23), which
  is what lets one add-on ask the other. GET only, and no shared secret — the
  admin key was removed on purpose and is not coming back. Nothing is reachable
  from outside Home Assistant.
- Changed — **the advisor page speaks to whoever opens it.** The old copy was
  written for someone who had already built the thing: "an advisor that only
  looks things up and does not connect them fails here" says nothing about what
  you are about to see. Page, readme and the hints on the four test questions
  now say what happens and how to spot a bad answer.
- Changed — **the instruction now permits documented numbers.** It forbade
  inventing them, which a careful assistant read as "give no numbers at all" —
  so it withheld a dosage that was printed in the material in front of it. The
  rule is now the one that was meant: quote what is documented, name where it
  comes from, invent nothing.

## 2.0.0-beta.23

**Beta.** Bring an RDWC advisor home — the knowledge, not just the readings.

- New — **the advisor folder.** Until now the export carried your measurements
  but not the material they are measured against, which leaves an assistant
  guessing from forum knowledge. The folder adds what already lives in Grow OS:
  11 procedures, 30 treatments, 20 symptoms, 8 pathogens, 37 rules and the
  setpoints, written out as readable text instead of JSON. Nine files, around
  120 KB — small enough for a Claude project, a GPT, or a local model via
  Ollama. Grow OS still sends nothing and needs no key.
- New — **four test questions with model answers**, so you can check the
  advisor before you trust it. A language model sounds equally certain when it
  is right and when it is inventing; the difference can only be tested, not
  heard.
- New — **the instruction sets four limits**: invent no numbers, do not
  overrule the operator's own setpoints, switch nothing, and ask rather than
  guess when the deciding value is missing. Dosing and automation stay in Grow
  OS behind their interlocks.
- Fixed — **journal entries without a title** printed as "2026-07-28 · — text"
  in the report, with a dash leading nowhere.

## 2.0.0-beta.22

**Beta.** Unreadable badges in light mode, and a test that catches the next one.

- Fixed — **"Kritisch" and "Info" on the diagnosis page** carried hard-coded
  light colours chosen for dark mode. In light mode the measured contrast was
  1.1 — effectively invisible. The text colour now comes from the theme:
  measured 5.5 in light, 10.7 in dark.
- Added — **a readability check in both themes.** It measures the contrast that
  is actually painted, compositing semi-transparent surfaces over what is beneath
  them; without that, a badge tinted at 10 % opacity reads as its own colour and
  the check reports nonsense. Two passes, because one is not enough: walking the
  routes only sees what renders without a backend, and empty pages have no
  badges — that pass ran green against the reinstated bug. The tone-carrying
  building blocks are therefore also placed into a real page and measured there.

## 2.0.0-beta.21

**Beta.** Light mode on the grow pages.

- Fixed — **the grow picker and every input field were dark on dark in light
  mode.** Their background colour was hard-coded to near-black while the text
  colour follows the theme. Both now use the `--sunk` token, which exists for
  both themes and whose dark value matches what was there before.
- Fixed — **the grow name appeared twice, one line under the other:** once in
  the picker, once in the back link below it. The link now reads "Zur
  Grow-Übersicht".
- Fixed — **"Verlauf8 gesamt".** The card header was a block, so both labels
  sat flush against each other. Title left, count right.
- Fixed — **the edit button had a minimum height but nothing centring its
  label,** so the text stuck to the top edge and cut visually into the line
  above it.

## 2.0.0-beta.20

**Beta.** Timestamps that say UTC now come back as UTC.

- Fixed — **45 read sites across ten repository classes read "…Utc" columns
  as local time.** Storage was correct; reading was not. Display never showed
  it, because the value carries its offset and means the same instant. The
  damage was wherever something calculated with it: the dosing check "is the
  last water level younger than two hours?" saw a reading from ten minutes ago
  as two hours in the future and never fired; the status report said "measured
  -120 minutes ago"; the trend lines on the live screen applied the time zone a
  second time, shifting the axis by the offset.
- Fixed — **the status report shows local date and time.** A human reads it,
  and just before midnight it printed the wrong day.
- Added — **a test reads the source and rejects any new site of this kind.**
  The mistake is made by copying the line next to it, and 45 occurrences never
  turned a single test red. Both new tests were verified against the
  reinstated bug; they measure differences, not clock readings, because on a
  UTC build server the offset is zero and none of this is visible.

## 2.0.0-beta.19

**Beta.** Two numbers the rig already knows, no longer typed in by hand.

- New — **guided volume calibration.** The level sensor (eTape) reports
  centimetres, but dosing needs litres, and the sum of pot and tank size is
  only a nameplate figure. Grow OS now reads the sensor every second while you
  fill: 15 s of a steady level in the empty system marks the zero point, 60 s
  of steady level afterwards raises the question "full?". You confirm by hand,
  because a pause in filling looks exactly like finished to a sensor. From the
  two points and the litres off your water meter comes the line — and the
  conversion sits at the source, so tiles, history, thresholds and the dosing
  factor all see litres.
- New — **the light cycle is learned, not entered.** The Home Assistant entity
  only knows on and off; the cycle follows from the last five days of
  transitions (median over the full on-phases, so one forgotten lamp cannot
  skew it). That makes two things visible that nobody would otherwise catch:
  18/6 during flower prevents flowering, and light in the middle of the dark
  phase causes revegetation or hermaphrodites.
- Fixed — **the water level arrived as text instead of a number.** The tile
  took it for a label, dropped the unit and drew no trend line. Next to a
  centimetre sensor, "72" without "L" is simply ambiguous.
- Fixed — **light transitions were read as local time although stored as UTC.**
  The time zone was applied twice: "on at 08:00" for a lamp that comes on at
  06:00.
- Fixed — **new columns ran before their tables existed.** They sat in the
  block for migration metadata, which runs early; they now have their own
  place at the end of schema setup.
- Changed — **the calibration run survives a page reload.** Standing at the
  reservoir with a hose, a locked screen would otherwise have cost the zero
  point.

## 2.0.0-beta.18

**Beta.** Second reality pass over beta.17's new behaviour.

- Fixed — **reservoir alarms kept running during drying.** The reservoir is
  drained, the pH probe sits in air reading nonsense — and it would have
  alarmed through exactly the critical drying days. Reservoir rules now pause
  during the drying window, and only there: someone running a reservoir
  without a grow record who sets rules means them.
- Fixed — **the second nutrient half never checked circulation.** A required
  confirmed circulation, B ran unchecked five minutes later. If the pump dies
  in between, B now waits for the next tick instead of dosing into still
  water — and the whole tent holds while it waits.

## 2.0.0-beta.17

**Beta.** A pass over the app's real-world logic — how a grow actually behaves,
not how the code does.

- Fixed — **nighttime is no longer "off target".** Tiles, score and alerts
  compared value against target with no idea of the light cycle: PPFD 0 at
  lights-off is correct, CO₂ falls to ambient because the plant consumes none,
  and VPD targets mean the day. Grow OS now asks the light sensor first, then
  the tent's light schedule; at lights-off, PPFD/CO₂/VPD carry no verdict and
  send no alarms. Nightly false alarms are how real alarms stop being believed.
- New — **dosing safety from the real reservoir.** Nobody doses into still
  water: a confirmed-stopped circulation pump now blocks even a manual dose,
  and the automation requires confirmed *running* circulation — a dead
  circulation pump is often the very reason the values drift. The mixing pause
  now belongs to the reservoir, not the pump: after *any* dose into the same
  water the reading says nothing for a while, whoever dosed. While a second
  nutrient half is outstanding, the whole tent holds; the automation doses
  nutrients before pH and at most once per tent per tick.
- New — **a mold ceiling on the humidity advice.** The VPD inversion knows only
  physics; in warm flower air it recommended humidity where grey mold becomes
  likely. Each phase now has a ceiling (seedling 80 % … finish 55 %); when the
  ceiling eats the whole band, the tile says the honest thing: lower the
  temperature, don't raise the humidity.
- Fixed — **a rooted clone is veg from day one.** It never had cotyledons; the
  seedling phase belongs to seeds. Before, every clone got 14 estimated
  seedling days with seedling EC.
- Fixed — **a CO₂ sensor is not CO₂ enrichment.** Without a burner, ambient
  ~420 ppm sat forever "off target". New tent switch "CO₂-Anreicherung"; without
  it the tile carries no target and explains why.
- New — **doses scale with the fill level.** The learned effect per ml comes
  from a full reservoir; in half the water the same dose works nearly twice as
  hard. Doses now shrink with the level (never grow). And the learning window
  cuts at the last water change — fresh water buffers differently.
- New — **drying is watched.** After harvest the tent becomes a drying room —
  the highest mold risk of the whole cycle, and the app used to look away.
  While the last grow is harvested, less than three weeks old and has no dry
  weight entered, the temperature and humidity tiles carry the 60/60 targets.
- New — **"Finish beginnt"** on the grow page: flushing starts when the
  trichomes say so, not when the breeder's weeks run out. Works for
  autoflowers too — the current phase now comes from one resolver for buttons
  and tiles alike.

## 2.0.0-beta.16

**Beta.** The main navigation moves to the top on phones.

- Fixed — **the bottom bar was cut off inside the Home Assistant app**, leaving
  only the top edge of the active item on screen. Not a contrast problem, a
  geometric one: under ingress Grow OS runs in an iframe whose height comes from
  the *large* viewport, so it extends below what the phone actually shows — and
  `position: fixed; bottom: 0` sticks to that iframe's bottom edge, not the
  screen's. Nothing inside the frame can measure how much is cut off. The four
  main entries now sit in their own row directly under the header, where the top
  edge is always visible.

## 2.0.0-beta.15

**Beta.** The seedling phase is now something you observe, not something the
calendar decides.

- New — **"Sämling ist durch"** on the grow page. The transition to veg does not
  hang on a date: it hangs on the plant. Real serrated leaves instead of the two
  round cotyledons, a thicker stem, new leaf pairs coming regularly, side shoots
  at the nodes, visibly more water going. One to three weeks after germination
  is typical — typical, not certain. Press the button when you see it, and the
  targets follow.
- Fixed — **the phase bar and the targets contradicted each other.** The bar said
  "Veg day 8" while the tiles showed seedling targets, because the app carried
  two different phase models: the bar knew germination/veg/flower, the targets
  additionally knew a seedling. The bar now shows the seedling too, and marks it
  "geschätzt" until you record the transition.
- Fixed — **a seed grow without a germination date stayed a seedling forever.**
  That is the normal case, since almost nobody records germination — and after
  three months a full-grown plant would still have been fed seedling EC.
- Fixed — **an empty temperature target now explains itself.** At 40 % humidity
  no temperature reaches a VPD target of 0.40–0.50 kPa: even at 5 °C the VPD is
  already 0.41. The calculation knew this and said nothing, leaving the tile
  blank. It now says so, and points at the humidity — which is the thing that
  actually needs changing.
- Removed — **the second QR code** on the Home Assistant page. It pointed at the
  ingress address *with* its token, so it worked once at best, and "generate new
  code" produced the same address every time. The working one lives under
  "Aufs Handy holen".
- Fixed — **the bottom bar on a phone was hard to see.** Against the page
  background it had a contrast of 1.04 — the same shade, with a one-pixel border
  as its only edge.

## 2.0.0-beta.14

**Beta.** Two fixes from the first real use of beta.13.

- Fixed — **the QR code led to a blank page.** It pointed at
  `/hassio/ingress/<slug>`; Home Assistant registers an add-on's sidebar panel
  at plain `/<slug>`. Under the wrong path the frontend finds no panel and draws
  nothing at all — no error, just an empty screen. Scanning the code now lands
  where clicking Grow OS in the sidebar lands. The page also states the
  precondition: "Show in sidebar" has to be on, or Home Assistant never creates
  an address for Grow OS in the first place.
- Fixed — **you could not look at the shipped setpoint profiles.** RDWC and DWC
  offered only "copy", so finding out what is actually in them meant creating a
  copy first — changing something in order to read something. They now open
  read-only, with the ranges written as `6–6,2` rather than two numbers running
  into each other.

## 2.0.0-beta.13

**Beta.** Dosing that suggests and then acts, a report for your own AI, and a
dashboard you can rearrange with your thumb.

- Fixed — **no pump had ever learned anything.** Every dose recorded the value
  before it and nothing wrote the value after, so the calculation that derives
  the effect per millilitre skipped every row. Grow OS now fills it in once the
  solution has mixed. The window is one mixing period wide: earlier you measure
  a streak, later you measure the plants drinking, and a wrongly attributed
  effect would sit inside every dose that follows.
- New — **"What would be needed now?"** on the dosing page. Grow OS computes the
  amount from what that pump has learned and shows its reasoning: the reading and
  where it came from, its age, the target and where *that* came from. The
  suggestion passes the same limits as a real dose, so it never shows an amount
  that would be refused.
- Fixed — that calculation used to read only your last hand-entered measurement
  and only a threshold you had typed yourself. It now takes the sensor value
  when that is the newer one, and falls back to your phase profile for the
  target. Without both, most setups got no suggestion at all.
- New — **automatic dosing.** Off by default, per pump. It requires an auto-off
  in Home Assistant, refuses to dose against a stale reading, and stays locked
  while the probe is uncalibrated or overdue — a drifting probe reports 6.0 while
  5.4 sits in the reservoir, and the pump would confidently dose the wrong way.
  Every unattended dose also goes out as a notification.
- New — **two-part nutrients (A and B).** Pair two pumps with a ratio; A runs,
  the separation time passes, then B. They must never meet concentrated:
  calcium from A precipitates with the sulphates and phosphates from B, and what
  flocculates never reaches the plant. The waiting half is stored in the
  database, so a restart between A and B cannot silently swallow it.
- New — **a report for your own AI agent**, on the grow page. Phase and day,
  current values with their targets and where each target came from, open
  issues, recent doses and journal entries. Grow OS sends nothing: you download
  the file and decide who sees it. There is still no AI inside the app.
- Fixed — **the hardware form never offered the wear templates.** All twelve
  existed in the knowledge base; none was reachable, so every device you added
  had an empty lifespan and the maintenance reminder that hangs off it never
  fired. A UV-C lamp keeps glowing past 9000 hours and stops clarifying — exactly
  the case nobody notices without a reminder.
- New — **airflow at leaf level** as a measurement, in m/min (RDWC 90–120,
  otherwise 60–90). It belongs with VPD: airflow breaks up the humid boundary
  layer at the leaf, and when that layer sits still your hygrometer reads a
  number the leaf never experiences. **Water flow** is deliberately a three-way
  choice, not a number — the source says "moderate, not strong" and names no
  throughput.
- New — **rearrange dashboard tiles with your finger.** HTML5 drag-and-drop has
  no touch support, so on a phone you could previously only add, remove and
  rename. Drag by the handle; the rest of the tile still scrolls.

## 2.0.0-beta.12

**Beta.** A QR code that puts Grow OS on your phone's home screen.

- New — **Anlage → Aufs Handy holen.** Scan the code with your phone, log into
  Home Assistant once, then "Add to Home Screen". You get an icon that opens
  straight into Grow OS.
- The obvious route is the broken one: the address in your address bar carries
  an ingress token that changes on every request, so a bookmark on it is dead
  the next day. The code points at the stable sidebar path instead
  (`/hassio/ingress/<slug>`), which Grow OS asks the Supervisor for — the slug
  differs depending on how the add-on was installed and cannot be guessed.
- The page builds the full address in your browser, because the server only
  knows Home Assistant as `http://supervisor/core` and has no idea what name
  you reach it under. Enter a different one if you need to; `localhost` is
  refused (on a phone it points at the phone), and a `.local` name gets a note
  about Android rather than a ban.
- Said plainly on the page: this does not remove Home Assistant's frame around
  Grow OS, and the home-screen icon belongs to Home Assistant. That would need
  an open port, and then Grow OS would need a login of its own.

## 2.0.0-beta.11

**Beta.** Calibrate a pump by volume, not by stopwatch — and your own limits
now reach the diagnosis too.

- New — **calibrate to 100 ml.** The old way ran the pump for 30 seconds and
  asked what was in the cup. At 23 ml, misreading by 1 ml is a 4 % error, and
  that error sits inside every dose afterwards. Grow OS now runs until roughly
  100 ml has come out, where the same misreading is worth 1 %. A brand-new pump
  still starts with the timed run — before the first calibration nobody knows
  how long 100 ml takes. On a slow pump the target drops to 50 or 25 ml so the
  run still fits inside the allowed time.
- New — the button counts down. A 100 ml run takes over two minutes; without a
  number on screen that looks like a crash.
- Fixed — **your own thresholds now count in the diagnosis.** They were handed
  to the analysis and then dropped on the floor: the field was never assigned,
  so the diagnosis kept reading only the shipped knowledge while the alerts
  already used your values. The same measurement got two different verdicts on
  two pages.
- Fixed — a pH range you enter yourself is now binding, even when it is
  narrower than the comfort zone. The shipped number is a mix-to target and is
  deliberately widened to avoid noise; a number you type is a threshold. Anyone
  deliberately running tighter than the comfort zone was told nothing at all.

## 2.0.0-beta.10

**Beta.** DWC gets its own targets, and you can write your own.

- New — **DWC has its own setpoint profile.** Until now there was one shipped
  set of values, for RDWC, and DWC was produced from it by multiplying EC in
  code — only EC, so everything else was identical, and NFT or aeroponics
  quietly got RDWC values with nothing saying so. DWC now carries its own
  numbers per phase (EC about 30 % higher, the smaller buffer), and profiles
  are picked by growing style.
- New — **Wissen → Sollwert-Profile**: copy a shipped profile and write your
  own experience into it, per phase. Only what you actually change becomes
  yours; everything you leave alone keeps receiving our updates. A full copy
  would have cut you off from every later improvement at the first save.
- New — **choose a profile where it belongs.** The hydro system sets the
  default, because DWC or RDWC is a property of your hardware — set it once
  and every grow in it inherits. A single grow may differ, because setpoints
  describe how you run *that* plant. Two runs in the same reservoir are
  allowed to differ.
- New — the tile names the profile when it is not the shipped one, the same
  way it already says "dein Wert". A threshold you enter on the tent still
  beats every profile.


## 2.0.0-beta.9

**Beta.** Your own limits now win — everywhere, and the tile says so.

- Fixed — **the app had two opinions about the same value.** With pH limits of
  5,60–5,90 entered and a reading of 5,99, the live tile said "zu niedrig"
  (against the shipped 6,00–6,10) while the alert said "zu hoch" (against
  yours). Follow the tile and you dose pH up; follow your own limit and you
  dose it down — opposite directions, with nothing saying which one applied.
  One place now decides, and the live tiles, the diagnosis, the dosing and the
  alerts all read from it.
- New — **the tile names the source**: "Ziel 5,60–5,90 · dein Wert". Shipped
  values stay unlabelled, so the note only appears where it answers something.
- Note — what you did *not* enter stays with the shipped phase values. Setting
  your own pH does not flatten the phase staircase for EC, VPD or anything
  else. A switched-off limit does not count, and half a range is allowed:
  "not above 6,2" leaves the bottom open.


## 2.0.0-beta.8

**Beta.** Two fixes everyone gets, plus a test mode for people who develop
Grow OS.

- Fixed — **no more false alarm right after a restart.** For the first seconds
  after starting, the watchdog reported "Überwachung steht", because the
  heartbeat lives in memory and no round had run yet. That fired after every
  restart and every update — exactly when you are looking at the screen. A
  fresh start is now its own quiet state; a stall that follows a completed
  round still counts as one.
- Fixed — with no Home Assistant configured, the camera request went out
  anyway, failed, and tripped the connection guard, so "Home Assistant
  antwortet nicht" appeared even where no camera was ever set up.
- New (for developers) — **test data**: start with `GROW_OS_DEMO=1` and Grow OS
  fills itself with invented but plausible readings, backfills 24 hours of
  history, and draws a placeholder camera frame. pH and EC drift upwards over
  the day so there is something real to correct, which is what the dosing needs
  to be tried against. A strip across the app says the values are invented for
  as long as it is on. Environment variable only — there is deliberately no
  switch in the interface, because invented readings in a running tent would
  not merely be wrong: alerts and the dosing hang off them.


## 2.0.0-beta.7

**Beta.** Grow OS can act, not just watch: dosing pumps — by hand for now.

- New — **Anlage → Dosierung.** Set up a peristaltic pump, tell Grow OS what
  it doses and which Home Assistant entity switches it, calibrate it, and give
  a dose at the press of a button. The pump on screen turns for exactly as long
  as it really runs. Nothing happens on its own yet; the automation follows
  once the arithmetic and the limits have proven themselves on real tents.
- New — **calibration**: the pump runs into a measuring cup, you enter what
  landed in it, and Grow OS knows its ml/min. Without that, millilitres are not
  a runtime and it refuses rather than assuming a flow rate. A worn tube pumps
  less than a new one, so the pump carries its own due date.
- New — **test mode**, to walk the whole thing through without hardware: it
  computes, waits out the real runtime and logs, but switches nothing. Test
  doses are marked as such everywhere and never count towards what Grow OS
  learns — otherwise there would later be a number with no drop behind it.
- New — **the log records refusals too**, with the reason. Otherwise you are
  left wondering why nothing happened overnight.
- Note — the dose is not computed from the concentration. How hard your
  solution resists a pH change depends on water hardness and nutrients, so
  Grow OS measures instead of guessing: the first doses you give yourself, and
  from three of them onward it knows the effect per millilitre in *your*
  reservoir.
- Safety — every dose passes hard limits: largest single dose, mixing pause,
  daily count and volume, and a runtime ceiling. Grow OS switches every
  configured pump off once at startup, in case a crash left one running. The
  later automation stays locked until you confirm a Home-Assistant-side
  auto-off — the only thing that helps if Grow OS dies between on and off.


## 2.0.0-beta.6

**Beta.** The Live screen judges your values again — without waiting for a
hand-typed measurement first.

- Fixed — **target ranges no longer wait for a manual measurement.** Every
  target hung off your last typed-in measurement, because the phase was read
  from it. A grow with live sensors but nothing typed in got no target on any
  tile: no colour, no "im Ziel", no "daneben" — while the header above it said
  "Veg · Tag 7". The phase comes from the grow now. A measurement you do
  record still wins over the calculation.
- New — **Luft and RLF are judged too.** The knowledge base only carries a VPD
  target for climate, so the two largest tiles on the screen stayed silent
  while the small VPD tile beside them was judged. They now show a range read
  back out of the VPD target: at 46 % humidity, the temperature that lands in
  your VPD target. Same knowledge, read the other way round — nothing invented.
- New — **each of those ranges says what it depends on**: "Ziel 15,8–19,6 °C ·
  bei 46 % RLF". Without that line it reads as "cool the tent down", when the
  real fix may be raising the humidity. Now you can see both levers.
- Fixed — **one climate problem counts once.** Luft, RLF and VPD describe the
  same situation; deducting for all three turned a mild offset into "Kritisch"
  and listed three names for one problem.
- Fixed — the header said "Alle Messwerte im Zielband" even when there were no
  target ranges at all: "all good" where it had to say "I checked nothing". The
  score printed a number out of the missing-sensor penalty alone, with a verdict
  beside it. Both now say plainly when nothing could be judged.


## 2.0.0-beta.5

**Beta.** Two things the 2.0 rebuild had dropped, brought back.

- New — **arrange the Live screen yourself again.** Press "Anpassen" and the
  fixed rows become movable: drag tiles within a section or into another one,
  rename sections, add your own, remove what you never look at. Any Home
  Assistant entity can become a tile — a UV clarifier, a socket's power draw,
  a fan — including things Grow OS knows nothing about. Saved per tent, with
  "Zurücksetzen" to get the standard back.
  Nothing changes unless you press it: without an arrangement of your own the
  screen looks exactly as it does today, and entering the mode starts from
  what is on screen, so no tile appears or disappears on the way in.
- New — **each tile shows its last 24 hours as a curve.** "Too high" does not
  tell you whether a value is still climbing or already coming back down;
  the curve does. It takes the place of the target bar rather than being added
  below it, so the tiles stay the size they were, and it is drawn in the
  colour of the tile's status.
- Note — an arrangement saved before the 2.0 rebuild is not brought back.
  It was built for a different screen, is missing everything added since, and
  would quietly take values off your dashboard. Your Live screen therefore
  looks the same after this update as before it.
- Fixed — dragging on a phone still does not work (a browser limitation that
  was there before as well). Adding, removing, renaming and the ↑ ↓ buttons
  work everywhere.


## 2.0.0-beta.4

**Beta.** The watchdog learned to see each tent on its own.

- Fixed — **one dark tent no longer hides behind another's fresh data.** The
  watchdog judged "newest reading anywhere", so a tent going silent while a
  second one kept reporting raised nothing at all. It now keeps a pulse per
  tent: the push names the dark tent ("Zelt 'Hauptzelt' liefert seit 45
  Minuten nichts"), and a further tent failing later is a new message instead
  of being swallowed as a repetition of the old one.
- New — **the system watch is visible where it matters.** The
  Systemüberwachung card lists for each tent when its data last arrived, and Live
  shows a warning strip above the metric tiles whenever monitoring itself has
  a problem — right where fresh-looking numbers would otherwise lie. Quiet
  when everything is fine.
- Fixed — switching cameras could leave the previous camera's image on stage
  under the new camera's name when the new one delivered nothing.
- Fixed — picking a different strain in the grow form kept the flowering
  weeks the previous pick had filled in, as if they were your own numbers.


## 2.0.0-beta.3

**Beta.** One page, rebuilt after real use: the knowledge base.

- New — **SOPs & Bibliothek is organised by urgency** instead of showing 93
  equal cards. Emergencies come first: the root-rot and power-outage SOPs sit
  at the top as red cards with a guided start, next to "I see something —
  what is it?" leading into the symptom list. Below, the routine SOPs form one
  table with kind, duration and step count — the things you actually compare.
  The reference material (symptoms, treatments, pathogens, target values,
  wear) sits in six compact panels; each row shows the one fact that helps
  while scanning, like where a symptom leads or how long an air stone lasts.
- Changed — search results are compact rows tagged by category, no longer a
  wall of cards.


## 2.0.0-beta.2

**Beta.** Everything the first beta was missing or got wrong, found by using it.

- New — **plan how long a grow stays in veg.** Without it the timeline could only
  ever say "day 68 and counting": no flip date, no harvest estimate. Enter the
  intended veg days and the timeline shows when the flip is due ("in 8 days",
  or "overdue by 12"), plus an estimated harvest. Leave it empty and nothing is
  invented — the run simply stays open.
- New — **the timeline shows all three phases**: germination, veg, flowering.
  Phases without data say so instead of disappearing, and the running phase
  fills up so you can see where in the plan today sits. Same timeline
  everywhere now: Live, the grow list and the grow itself.
- New — **a grow can point at a strain from your library.** Picking one fills in
  the breeder and its flowering weeks, and the strain statistics ("runs",
  "average yield") finally count the right runs instead of matching names,
  where a typo silently dropped a run.
- New — **ask the assistant a question.** The model could be connected and
  tested, but there was no way to ask it anything. Answers now show which of
  your records back each statement, and anything unbacked is marked before you
  read it.
- New — **the first run guides you**: an empty installation now shows the three
  steps in the order they have to happen instead of an empty cockpit.
- Fixed — **tasks could be created but never ticked off.** The button lived on a
  panel that was replaced during the redesign.
- Fixed — **light schedules and plant management were unreachable.** The tent
  detail page lost its only link, taking the light times, the tent history and
  the whole setup and plant management with it — which also broke the pheno
  hunt, since that is where plants are created.
- Fixed — **the pheno hunt lost its weighting**, so scores could no longer be
  tuned.
- Fixed — **the score ring was green while the score said "critical."** Ring,
  number and word now agree. The tent page also had a second, different scoring
  formula; there is one now.
- Fixed — **the sidebar scrolled away on long pages**, leaving a bare strip
  underneath.
- Fixed — the start date is required; without one, the day you create the grow
  is day one.
- Fixed — thresholds now show the target range of the current phase beside them,
  from the same source the tiles use, and can be filled from it.
- Changed — **the tent/grow selectors at the top are gone.** They steered
  nothing: no page ever read them, while each page picked its own grow. Every
  page now chooses for itself, visibly, and the sidebar counters count across
  all running grows — which is what their pages show.

## 2.0.0-beta.1

**Beta.** The complete UI redesign — every screen rebuilt 1:1 from the designer's
handoff. Marked beta because a few rough edges are still expected; data, automations
and the API are untouched, and 1.8.4 remains the last stable release.

- New — **the whole app follows one design language now**: instrument-cluster panels,
  hairline borders, mono labels, a dark and a light theme with a proper toggle
  (sidebar and settings stay in sync).
- New — **Live** is a single cockpit: score ring with the reasons behind the number,
  climate and nutrient bands with target ranges, camera stage, the current risk with
  its SOP, today's tasks and the slow-moving observations — plus the grow phase
  timeline.
- New — **Messen** checks values while you type and shows deviations live; saving can
  jump straight into the matching Addback.
- New — **Addback** and **Grow anlegen** are one page each instead of wizards.
- New — **Aufgaben** sorts by what matters: risks first, then appointments, then
  maintenance — one main action per row.
- New — **Grows** shows each run as a card with its phase bar; finished runs live in
  **Ernte & Archiv** as a yield table with a two-run comparison.
- New — **Sorten & Pheno-Hunt** on one page: the library with runs, average yield and
  keeper per strain, and the candidate strip with the scoring sheet inline.
- New — **Journal & Fotos** is one stream — entries, measurement photos and events
  together, with a photos-only filter.
- New — **SOPs & Bibliothek** merges knowledge into one searchable collection;
  emergency SOPs are tagged and highlighted.
- New — **Regeln & Automatik** puts thresholds (with per-rule cooldown), auto
  measurements, notifications and the AI assistant behind one set of tabs.
- New — **Home Assistant** shows the mapping as rows with live values and adds
  a QR panel to pair the phone in the grow room.
- New — **Zelte & Räume** is a master-detail view: climate, light, air and occupancy
  per tent, camera and mapped sensors included.
- Fixed — a global reset was overriding half the design system's spacing.
- Fixed — the tent's active grows were never populated, which silently disabled
  alert rows and tile target ranges.
- Fixed — the live score could show 100 while values were out of range; it now
  counts real deviations.

## 1.8.4

- New — **VPD is finally checked against its target.** A target band per stage has been in
  the reference data all along, and nothing read it: the value was calculated, charted and
  put on tiles, but never compared to anything. Grow OS now says when the leaf VPD leaves
  its band — and when it is *below* it, says why that matters here: RDWC transpires two to
  two and a half times as much as soil and wants the upper end, so a low VPD is holding the
  plant back rather than protecting it. Check the airflow at leaf level before reaching for
  temperature or humidity.

## 1.8.3

- Fixed — **the add-on log was 99 % noise.** Every call to Home Assistant produced four lines,
  once a minute, around the clock — 4,968 of 5,000 lines in a real log, with the 32 lines
  that meant something buried underneath and a Raspberry Pi writing the rest to its SD card
  all day. Warnings and errors still show; the chatter is gone.
- Fixed — **an update could be offered before its image existed.** Home Assistant reads the
  version straight from the repository, so it announced a new release the moment that file
  changed, while the image was still building — a few minutes in which pressing Update gave
  a bare "unknown error". The image is now published and verified first, and only then is
  the version announced.

## 1.8.2

- New — seven rules recovered from the workshop material, which turned out to be almost
  entirely graphics. Among them: **RDWC transpires two to two-and-a-half times as much as
  soil** and therefore wants slightly *higher* VPD than the usual recommendation, not lower;
  **airflow at leaf level** belongs to VPD (90–120 m/min for RDWC, about 10–15 % more than
  other systems) because it breaks the moist layer on the leaf; the canopy runs a gradient of
  roughly 26 / 24.5 / 23 °C top to bottom, so where the sensor hangs decides the number; and
  biofilm is where every RDWC problem starts — rising oxygen consumption with falling ORP is
  the early sign, long before anything shows on the roots.

## 1.8.1

- Changed — **a new tent now computes leaf VPD, not air VPD.** The leaf sits about 2 °C below
  air temperature, and every RDWC recommendation is drawn for that number; the offset used to
  default to 0, so a fresh tent quietly showed a different figure than the charts mean.
  Existing tents keep whatever you set — 0 might have been deliberate.
- New — **a reminder that ORP is a consumable.** It has to be brought back up with HOCl every
  two to three days because it decays while doing its job, and the day it is forgotten
  nothing else looks wrong. Grow OS now says so once it has been more than three days —
  and stays quiet for anyone who doesn't track ORP at all.
- New — four more rules from the workshop material, including one worth knowing: RDWC
  transpires differently from soil, so a VPD table from soil growing does not transfer.

## 1.8.0

- New — **the nutrient solution is diagnosed as a pattern, not a value.** SOP-N1 lays out a
  table and asks you to read five signals together, because a falling pH with stable EC and
  good oxygen is a plant feeding, while the *same* falling pH with rising EC and low oxygen
  is biofilm. Grow OS checked each of those separately and could never reach that
  conclusion. The Diagnose page now shows the whole table at once — and the two rows no
  sensor covers, the look and the smell of the water, are listed as checks for you rather
  than quietly dropped.
- Changed — **cuttings quarantine now follows SOP-C1**: the three-bath method as three
  separate baths per cutting, the substrate carrier handled properly (rockwool, EasyPlug,
  Jiffy — or none at all, in which case the whole section is skipped), the choice between
  HOCl and H₂O₂, the drying phase, and the release criteria.
- New — **an addback procedure**, which was missing entirely: fill to 90 %, add one
  component at a time in mixing order, stir between each, never more than 500 ml of any one
  part per container, and never straight into the control bucket.
- Changed — a step can now depend on more than one answer. Decontaminating a substrate plug
  needs both the agent and there being a plug; with a single condition, bare-root cuttings
  were told to dip something they don't have.

## 1.7.3

- New — **starting a routine now asks what it needs to know.** Root-rot treatment wants to
  know how badly the plants are affected and how many there are, and it asks before you
  start rather than half-way through — finding out mid-treatment that a different path
  applied is the thing a written procedure exists to prevent.
- Changed — the steps you get are the steps that apply. A lightly affected plant skips the
  root cut and gets the short rinse; a badly affected one gets the cut and the long one. And
  the treatment is laid out **one plant at a time**: plant 1 goes from lifting out all the
  way to the quarantine container, including disinfecting the shears, before plant 2 is
  touched at all. That order is the whole point — it is what stops the pathogen crossing to
  the next plant.

## 1.7.2

- New — **procedures can branch and repeat.** The source SOPs are not flat lists: root-rot
  treatment handles a badly affected plant differently from a healthy one, and the block that
  matters most — rinse, then disinfect the shears and the surface — runs once per plant,
  because that disinfection is what stops the pathogen travelling. Steps can now carry a
  condition and repeat per plant, so the app can follow the document instead of summarising it.
- Changed — **root-rot treatment now follows SOP-S1 step by step**: eighteen steps instead of
  fourteen, split into the passive path (no cutting, 1–2 minutes in the second bath) and the
  active one (cut first, then 180 seconds), with the ORP levels the SOP specifies for each
  bath, the spray bottle and the refilled system.
- Fixed — that procedure claimed root rot was confirmed below **4 mg/L** of dissolved oxygen.
  SOP-S1 says **6 mg/L**.
- New — six rules from the RDWC Procedure, including two that are easy to get backwards:
  **ORP shock looks exactly like a nutrient deficiency** (yellowing, dry foliage — feeding it
  makes it worse), and **the smell tells you which way you're wrong** — putrid means anaerobic,
  fresh bean sprouts means healthy, chlorine means over-oxidised.

## 1.7.1

Checking the code against the source SOPs — rather than only against the knowledge files —
turned up four places where Grow OS contradicted the documents it is built on.

- New — **pH drift is now judged by speed, not just by position.** SOP-N1 separates a normal
  swing (0.1-0.4 a day, the plant feeding) from a real drift (0.5 or more within 12-24 h,
  which points at instability, biofilm or precipitation). Grow OS only ever looked at the
  absolute value, so a jump from 5.8 to 6.3 overnight — which never leaves the target band —
  went unmentioned. It is now reported with the SOP's own list of immediate checks.
- Fixed — **dissolved oxygen was flagged too late.** SOP-N1 calls for action below
  6.5 mg/L; Grow OS stayed silent until 6.0. That is exactly the range where root rot starts
  while nothing looks wrong. Below 6.0 now counts as confirmed, per SOP-S1.
- Fixed — **flushing was reported as a mistake.** The growplan ends at EC 0.4, but the Finish
  setpoint said 1.1-1.6 — the peak of flower. Anyone following the plan down was told their
  value was out of range. Finish is now 0.4-1.1.
- Fixed — two numbers for the DWC multiplier (1.3 vs 1.35) and a third hard-coded copy of the
  pH thresholds. Both now come from one place.
- New — eight rules from the SOPs added to the knowledge base, each citing its document and
  section, so every recommendation can be traced back to where it is written down.

## 1.7.0

- New — **the watchdog now notices slow failures.** It used to spot only that monitoring
  itself had stopped. It now also reports a value drifting the same direction day after day
  (even while it stays inside its band), consumption collapsing — the plant telling you it
  stopped drinking — consumption doubling, which usually means a leak, and a water change
  that never happened. Each finding names the growplan rule behind it. One message per
  finding, not one per check, and a restart doesn't replay what was already reported.
  Deterministic: no model, no API key, works on every install.
- New — **search.** One box in the sidebar, Ctrl+K from anywhere, and in the "Mehr" panel on
  a phone. It finds pages, grows, tents, systems, strains, SOPs and knowledge entries — and
  it knows the words you'd actually reach for: "kamera" finds Zelte, "mangel" finds Diagnose.
- Fixed — **things were hidden.** Eight of twenty-three destinations sat in groups that were
  collapsed on first visit, including everything a new install needs: Zelte, Hydro, Sensoren,
  Home Assistant. And Einstellungen lived under a group called "Wissen". Regrouped.
- New — **cameras on the dashboard** (from 1.6.1): a tent with three of them shows all three
  at once, tiles carry a width, and sections can be reordered by drag or by arrow buttons —
  dragging does nothing on a touchscreen.
- New — groundwork for an optional AI assistant: connect Claude, OpenAI or a local model,
  see exactly what would be sent before anything is, and have every claim checked against
  the documents it cites. Nothing is switched on unless you set it up, and the app is fully
  usable without it.

## 1.6.2

- Fixed — some knowledge entries in 1.6.1 linked to source PDFs that aren't part of the
  image, so the link led nowhere. The source is still named — document title and section —
  it just isn't a link any more where the document isn't shipped.

## 1.6.1

- New — **cameras on the dashboard**. A camera can now be a tile, so a tent with three of
  them shows all three at once instead of paging through one at a time. Tiles carry a width
  as well (− / +), and cameras start two columns wide. The separate camera panel steps aside
  once you've placed your own camera tiles.
- New — **sections can be moved**. Drag a section, or use ↑ ↓ — dragging does nothing on a
  touchscreen, and the dashboard is mostly read on a phone.
- Fixed — asking the camera proxy for an entity that isn't assigned to the tent used to
  quietly serve the tent's *first* camera instead. It looked like a working feature while
  showing the wrong tent; it now says the camera isn't assigned.
- Changed — **pH is no longer nagged over normal drift**. The growplan says to let pH swing
  freely between 5.8 and 6.2 and only correct below 5.5 or above 6.5. Grow OS did the
  opposite: it warned as soon as the value left the narrow per-stage band and suggested
  pH-Down. Your mixing target is still shown as a hint.
- New — **a warning when the light is too strong for the CO₂ you have**. The PPFD targets
  assume CO₂ enrichment. Without it, 800–900 is the ceiling, so above 900 PPFD with CO₂
  under 800 ppm Grow OS now says so — reduce in steps of 50, keep 30 cm to the tips.
- Fixed — ORP targets sat at 300–400 for every stage. They now follow the plan: 400–450 in
  flower, 450–500 in finish.
- Fixed — **knowledge updates never reached existing installations.** The reference data was
  copied once on first start and then left alone forever, so corrections like the ones above
  only ever reached new installs. Grow OS now compares against what it last shipped: its own
  files are refreshed, anything you edited yourself is left alone. Files older than this
  mechanism are backed up next to the original as `*.user-backup` before being refreshed.

## 1.6.0

- New — **build your own live dashboard**. "Dashboard anpassen" on the live screen turns the
  value sections into something you arrange: drag tiles to reorder them or move them between
  sections, remove what you don't need, rename a section or create new ones — saved per tent.
  "Auf Standard zurücksetzen" always brings the built-in arrangement back.
- New — **your own sensors on the dashboard**. Any Home Assistant entity can become a tile,
  including ones Grow OS knows nothing about — a UV clarifier, a pump, a switch. Non-numeric
  states (on/off) are shown as they are. Give it your own caption and unit if you like.

## 1.5.0

- New — **Pheno Hunt**. Compare the siblings from one batch of seed and pick the keeper.
  Each plant gets a score sheet that fills up as the run goes: structure and vigour while
  growing, which training it got (LST, topping, supercropping, lollipopping …) and how it
  took it, stress and pest resilience, then flowering days, stretch, yield, bud density and
  resin at harvest, and finally aroma, flavour, effect and THC after the cure. Everything
  optional — an unrated trait simply doesn't count.
- New — **the ranking follows your goals**. You set once what matters (yield, quality,
  potency, resilience, structure) and Grow OS ranks the plants accordingly, showing the
  breakdown per plant so the number is explainable. Yield and THC are scored against the
  other plants of the same hunt, because those numbers only mean something in comparison.
  You can always override a plant's score by hand, mark a keeper, and note when a pheno was
  confirmed in a second run.

## 1.4.0

- New — **strain library**. A new "Sorten" page under Meine Grows is your own genetics
  catalogue: name, breeder, indica/sativa/hybrid, flowering weeks, free notes — plus the
  traits that actually change how you grow a plant: feeding appetite, stretch, and
  preferred VPD. (The backend for this existed but had no screen at all.)
- Fixed — a strain that prefers **higher humidity could not be saved**: the VPD preference
  is a shift in kPa, so negative values are perfectly normal, but it was being validated
  like a multiplier that must stay above zero.

## 1.3.0

- New — **Watchdog: Grow OS now tells you when the monitoring itself goes quiet.** A normal
  alert says "this value is wrong"; the watchdog says "I can't see anything right now" —
  the case where silence used to be ambiguous. Every minute it checks whether the
  background worker is still running, whether Home Assistant is answering, and whether
  fresh sensor values are actually arriving. If one of those stops it sends a single clear
  push (and one when it recovers) — never a repeated complaint.
- New — the Notification Center shows the **current system state in plain words** ("Alles
  wach — letzte Sensordaten vor 3 Minuten") and has a "Systemtest senden" button that
  pushes that state to your phone, so you can prove the path works.

## 1.2.0

- New — **leaf temperature offset for VPD**. What a plant actually feels is leaf VPD, and
  leaves sit 1-3 °C below air temperature (more under LED, which has no infrared). You can
  now set that offset per tent ("Blatt kühler als Luft"), and both the live dashboard and
  the measurement page use it — the measurement page even shows which offset it applied.
  Left at 0 you get the plain air VPD as before.
- Improved — when no VPD sensor is mapped, the calculated value now comes from the **live**
  temperature and humidity instead of the last stored measurement, which could be days old.
- Fixed — the status gauge's glow was **clipped into a square** by its own SVG bounds; it
  now fades out as a full circle.

## 1.1.1

- New — **the live tiles now show a real 24-hour curve**. Each sensor tile on the live
  dashboard draws the last day's trend where a fixed decorative bar used to sit, in the
  tile's own colour — so the day/night rhythm and any drift are visible at a glance.
  Tiles without recorded history are unchanged.

## 1.1.0

- New — **your sensor history is finally visible**. Grow OS has been recording every
  mapped sensor every 5 minutes and condensing it into daily statistics each night — but
  there was no way to look at it. The tent page now has a **Verlauf** section with a curve
  per metric (pH, EC, water temp, air temp, humidity, VPD) over 7, 14 or 30 days. Each
  curve shows the daily median as a line, the day's min/max as a band, and — if you've set
  thresholds for that tent — your target range behind it, so "am I inside my limits?" is
  answered at a glance.

## 1.0.52

- Fixed — on **mobile**, a grow's name showed twice on its page (once in the summary
  card, once in the KPI card below). The KPI card no longer repeats the name.

## 1.0.51

- Improved — the **diagnosis, SOPs, journal and measurement views now match the rest of
  the app**. Those grow pages still used the older, denser card style; they now use the
  same surfaces, badges, buttons and typography as everything else.

## 1.0.50

- Fixed — **"Alle Kameras testen" now shows every camera**, not just the first. On the
  Home Assistant page a snapshot preview appears for each mapped camera (with readable
  labels), each with its own state so a broken one doesn't hide the others.
- Fixed — the grow's name **no longer shows twice** on the grow page (removed the small
  duplicate in the top bar; the big title stays).

## 1.0.49

- Fixed (major) — **threshold alerts now repeat reliably**. They were edge-triggered:
  you got one push when a value first crossed the limit and then silence, even while it
  stayed out of range — and the check only ran every 5 minutes, so per-minute settings
  did nothing. Now alerts are level-triggered: while a value stays out of range Grow OS
  re-notifies every "Erneut erinnern alle N Minuten", checked once a minute by a
  dedicated watcher.
- New — **immediate push when you save a threshold**. If the current value is already out
  of range when you save, you get the alert right away instead of waiting for the interval.

## 1.0.48

- Fixed — opening a grow on a **desktop** now shows the "Zu diesem Grow" links
  (Messungen · Diagnose · Journal & Fotos · SOPs · Automatik, each pre-selected to that
  grow). They were inside a mobile-only block, so on desktop there was no way to get
  from a grow to its own pages.

## 1.0.47

- Improved — the **Archive page** now uses the same clean look as the rest of the app
  (big header, stat cards, list rows) instead of the old table style, and shows each
  grow's yield inline plus a total-yield figure.
- Fixed — errors on the **Grows page** are shown as a proper banner instead of a bare
  line of red text, matching how the rest of the app surfaces errors.

## 1.0.46

- New — **set the light cycle per tent**. The tent page now has a "Lichtzyklus" section
  where you enter when the light goes on and off; it shows the resulting photoperiod
  (e.g. 18/6, 12/12). This is the precondition the light-based automations ("30 min after
  lights on/off") trigger on — previously it only lived in Home Assistant, now it's
  visible and editable in Grow OS.

## 1.0.45

- New — **start a routine yourself**. The SOPs page now has a catalog of the built-in
  routines (weekly water change, system cleaning, root-rot treatment, flip to flower,
  harvest flush, …) with a "Starten" button each — so you can run an SOP whenever you
  want, not only when a risk happens to recommend one. Routines already running are
  marked "Läuft".

## 1.0.44

- Simplified — the **Diagnose page** was three overlapping cards full of internal terms
  (deviations, symptom ids, confidence levels). It's now one clear shape: "Handlungsbedarf"
  up top — what's actually wrong, each with its actions (acknowledge, resolve, start an
  SOP) — and below it a quiet, plain-language "Auffällige Werte & Tipps" list of the
  underlying readings and suggestions. Nothing lost, just far less noise.

## 1.0.43

- New — **pick which camera** for a measurement snapshot. When a tent has several cameras
  you now choose which one to snapshot from (with readable names derived from the entity,
  e.g. "Hauptzelt"), and with a single camera it shows which one it uses.
- Changed — **the harvest no longer vanishes**. Your yield now shows up in the Archive
  (dry weight and rating per grow, plus a total-yield figure), and the harvest page has a
  "Speichern & Grow abschließen" button that saves the harvest and moves the grow to the
  archive in one step — closing the grow's lifecycle instead of leaving it running.

## 1.0.42

- Fixed (real) — the **double scrollbar**, this time at the root. A global
  `overflow-x: hidden` on the body turned it into an internal vertical scroll container,
  which inside the Home Assistant ingress iframe showed up as a second scrollbar next to
  the iframe's own. Switched to `overflow-x: clip` so the document scrolls naturally —
  one scrollbar.
- New — **test your calibration reminder**. On the Notification Center, the "Kalibrierung
  fällig" card has a "Test-Erinnerung senden" button that runs the real reminder path now
  and tells you the result: it either sends the push to your phone, or explains why it
  wouldn't (no phone saved, category off, quiet hours, or nothing due). If you set up a
  daily calibration and got no reminder, this shows you why — most likely the phone was
  never saved before the fix in 1.0.40.

- Changed — the **grow pages (Automatik, Diagnose, Journal & Fotos, SOPs) now match the
  rest of the app**. They used a different, smaller header, and the grow switcher was
  broken on desktop (cut-off dropdown) and missing entirely on mobile. They now use the
  standard page header with a clean grow switcher that works on phone and desktop.

## 1.0.40

- Fixed — **double scrollbar**. Making the sidebar scrollable added a second full-size
  scrollbar next to the page's, which looked wrong and made scrolling feel broken. The
  sidebar now has a thin, subtle scrollbar so there's one clear page scrollbar again.
- Fixed — **measurement snapshots are now visible**. After "Snapshot aufnehmen" (or
  picking photos) you now see thumbnails of the attached images, each removable with an
  ×, instead of only a filename.
- Fixed — the **camera mapping on the Home Assistant page** was cramped into a half-width
  column; it now spans the full width with a clean row of actions.
- Fixed — the **Save button on the Notifications page** sat too low; page headers now
  align their action button to the top.
- Fixed — task and SOP rows on the **Aufgaben** page pointed at the old grow tabs; they
  now open the Journal / SOPs page for that grow.

## 1.0.39

- Fixed — the **sidebar now scrolls**. With every menu group expanded it could run past
  the bottom of the screen and the lowest entries were unreachable; it scrolls now.
- Fixed — on the **Notifications page your phone is actually saved**. The only Save
  button was buried at the bottom in an unrelated section, so entering a push service
  and tapping "Test" never saved it. There's now a Save button at the top, and "Test-Push"
  saves first before sending.
- Fixed — **Automatik showed "Kamera-Snapshot ins Journal" twice** (once per active
  template). It's a single switch now that applies to all active automations.

## 1.0.38

- Simplified (big) — **Automatik is now just switches**. The old page asked you to build
  configs with metric keys, aggregations, and field mappings — things a grower should
  never have to touch. It's gone. Now you pick a grow and flip on ready-made templates:
  "Messung 30 Min nach Licht AN" and "…nach Licht AUS". The measurement automatically
  captures whatever sensors you've mapped in Home Assistant — no entities to choose. Each
  active template has one extra switch, "Kamera-Snapshot ins Journal", which drops a camera
  image into that grow's photo diary on every automatic measurement.

## 1.0.37

- Changed — **lifecycle confirmations now live on the measurement page**. Confirming
  germination, rooting, or the flip to 12/12 is something you do when you check the
  plant, so those buttons moved into the measurement page's context card (shown only
  when they apply to the selected grow). The grow overview no longer carries them.
- Changed — **Harvest only shows when the grow is ready**. The Ernte action on the grow
  overview now appears only once the grow is in Flower, Finish, or Dry — otherwise it's
  hidden. Export stays on the overview.

## 1.0.36

- Fixed — removed the duplicate "Messungen" entry from the sidebar. Recording a
  measurement lives under Täglich → Messung; a grow's measurement history is still
  reachable from that grow's overview.

## 1.0.35

- Changed (big) — **features are no longer hidden inside a grow**. The six tabs that
  used to live inside a grow (Messungen, Diagnose, Journal & Fotos, SOPs,
  Automatisierung) are now their own top-level pages, each doing one thing, each with
  a grow switcher up top — so you see and use them right away without opening a grow
  first. New sidebar grouping: Täglich · Verlauf & Daten · Automatik & Regeln · Meine
  Grows · Einrichten · Wissen. Automatik is now fully editable on its own page (create
  the 30-min light preset, add configs, edit field mappings, enable/disable) instead
  of only being reachable inside a grow. Opening a grow now shows a clean overview
  only, with quick links to that grow's pages.
- Internal — Playwright end-to-end smoke tests now load every route and fail the build
  if a page crashes while rendering; CI also runs eslint and the e2e suite.

## 1.0.34

- New — **Automatik overview page**. A new "Automatik" entry under "Automatik & Regeln"
  surfaces features that used to be buried: all your auto-measurements across every grow
  (with their trigger, e.g. "30 min after lights-on") and every sensor's calibration
  interval — each with a link straight to where you edit it. Nothing hidden in a grow tab
  anymore.

## 1.0.33

- New — **daily digest push**. In the Notification Center you can enable a once-a-day
  summary at a time you choose (e.g. 5:30) — so first thing in the morning you know the
  system is up and how the values look. Pick the format: short ("all OK / N issues") or
  detailed (the key values per tent). The digest is delivered even during quiet hours,
  since you chose the time deliberately.

## 1.0.32

- New — **attach a camera snapshot to a measurement**. On the measurement page you can pick
  one of the tent's cameras and take a snapshot of the current image with one click; it's
  attached as a photo and saved with the measurement.

## 1.0.31

- New — **multiple cameras per tent**. Map several camera entities to a tent (e.g. one per
  plant) on the Home Assistant page, then switch between them right on the live dashboard
  with the ‹ 1/3 › control on the camera view. Existing single-camera setups keep working
  unchanged.

## 1.0.30

- New — a new measurement is now **pre-filled from Home Assistant**. When you open the
  measurement page, the mapped sensor values (pH, EC, water temp, DO, ORP, level, climate)
  are filled in automatically from the tent's live values — you only correct what you need.
  An "Aus Home Assistant übernehmen" button re-pulls the current values on demand.

## 1.0.29

- Fixed — long pages (e.g. the observation section on the measurement page) were cut off
  in Firefox and only scrolled when you click-dragged. A global `overflow-x: hidden` was
  turning into an implicit vertical scroll container ("window in window"); switched it to
  `overflow-x: clip` so the page scrolls normally.

## 1.0.28

- New — the sidebar is reorganised into **collapsible groups** by what you want to do:
  Täglich · Meine Grows · Automatik & Regeln · Einrichten · Lernen & System. Each group
  opens and closes and remembers its state, and the group of the current page always stays
  open — so you can slim the menu down to just what you use. Grenzwerte and Benachrichtigungen
  now live together under "Automatik & Regeln".

## 1.0.27

- Refined — you can no longer manually create a "fixed sensor": those appear
  automatically from the Home Assistant mapping, so the add form only offers handheld
  meter and equipment. When editing a synced sensor, its kind is shown read-only. Also
  shortened the "Art" helper text.

## 1.0.26

- New — hardware now has an explicit **device kind**: fixed sensor (HA-mapped, live
  values), handheld meter (e.g. a BlueLab pen — calibrated, never mapped), or equipment
  (pump, chiller, UPS — maintenance only). Pick it in the hardware form; the kind shows
  on each card.
- Fixed — handheld meters and equipment no longer get a bogus "Mapping prüfen" warning
  on the Aufgaben page. Mapping warnings only apply to fixed sensors without an entity,
  and the HA card no longer warns when your setup simply has no fixed sensors.

## 1.0.25

- Improved — clickable elements now read as clickable on **every** page, consistently:
  a global pointer-cursor rule for all buttons/tabs/switches (browsers don't do this by
  default), plus matching green hover highlights for buttons, tabs, switches, tent chips
  and clickable risk rows — the same affordance the sidebar navigation got earlier.

## 1.0.24

- Fixed — the clock in the live dashboard's LIVE chip now ticks in real time. It used to
  show the last data-refresh timestamp (moving only every 30 seconds), which looked like a
  hanging clock. If the data itself ever goes stale (e.g. Home Assistant briefly down),
  the chip now says so explicitly ("Daten vor X min").

## 1.0.23

- New — **mapped entities become sensors automatically**. Map an entity on the Home
  Assistant page (e.g. your pH probe) and it appears under **Sensoren** as tracked
  hardware — with a sensible calibration interval per type (pH 14 days, EC/ORP/DO 30)
  and the calibration cycle armed, so the calibration push reminder works from day one.
  Your edits (name, interval) survive re-saving the mapping; unmapping keeps the item.
- New — **water level in liters or centimeters**: two separate mapping slots
  ("Wasserstand (Liter)" and "Wasserstand (cm)"), correct units on the live dashboard
  and threshold alerts for both.
- Fixed — the Settings page no longer claims "HA aus · keine URL" when running as an
  add-on; it now shows "aktiv · Über Add-on".

## 1.0.22

- Fixed — a threshold breach that starts during quiet hours (or while Home Assistant is
  briefly unreachable) is no longer silently swallowed: Grow OS now retries and delivers
  the push as soon as sending is possible again.
- Internal — major test expansion: Home Assistant HTTP behavior is now covered with faked
  HA responses (entity parsing, supervisor URL, circuit breaker, notify payloads) plus
  full-loop alert/notification behavior tests (548 tests total).

## 1.0.21

- New — **Notification Center** (Benachrichtigungen): one place to pick your phone once, set
  quiet hours, and choose what Grow OS pushes you. All notifications now share this single
  device and quiet-hours setting.
- New — **calibration-due push**: a daily reminder when a sensor calibration is due.
- New — **sensor-offline push**: get notified when a mapped sensor stops reporting values
  (and again when it recovers), with a short delay so a brief hiccup doesn't false-alarm.
- The threshold page is now called **Grenzwerte** and only sets min/max per sensor — the
  phone and categories moved to the Notification Center. (Set your phone there once.)

## 1.0.20

- Simplified — removed the confusing "HA Entity" field from the Sensors (hardware) form. It
  never actually connected anything: live values come only from the per-tent mapping on the
  Home Assistant page. Sensors are now purely physical inventory (name, type, tent,
  calibration, maintenance); entities are mapped in exactly one place — the Home Assistant tab.

## 1.0.19

- Fixed (major) — mapped RDWC/DWC reservoir sensors (pH, EC, water temp, ORP, DO, water
  level) now show their live values on the dashboard as soon as they are mapped, even
  before the grow has any measurements. Previously the reservoir tiles stayed blank ("—")
  unless the grow was recognized as active-hydro or a manual measurement already existed.
- Clearer wording on the Sensors page: the mapping hint no longer reads "HA getrennt"
  (which looked like a lost connection) — it now explains that entities are mapped under
  the Home Assistant tab, and that the add-on connection itself is always active.

## 1.0.18

- Improved — the sidebar navigation now reads clearly as clickable: a pointer cursor on
  hover, a distinct green hover highlight with an outline, and brighter idle text so the
  menu items no longer look like static labels.

## 1.0.17

- Fixed — the Home Assistant page no longer shows the connection as "inactive" when
  running as an add-on. As an add-on the connection is automatic (Supervisor token), so
  the status card now reads "active · via add-on".

## 1.0.16

- New — threshold alerts with push notifications. Under **Alarme** you can set a min/max
  per sensor (pH, EC, water temp, ORP, DO, air temp, humidity, VPD, CO₂). Grow OS sends a
  push to your phone through Home Assistant when a value goes out of range — pick your HA
  notify service from a dropdown and send a test push. Edge-triggered with a cooldown so
  you are not spammed.
- Fixed — the Reservoir section on the live dashboard now shows your reservoir sensor
  values as soon as they are mapped (RDWC/DWC group), even before a grow is running, with
  a hint that grow-specific targets and addback need a DWC/RDWC grow.

## 1.0.15

- Fixed: live sensor values could suddenly blank out (showing "—") and only came
  back after leaving and reopening the dashboard. A transient connection hiccup on
  the 30-second background refresh was wiping the values; the dashboard now keeps
  the last good readings instead of clearing them.

## 1.0.14

- Launch cleanup. Removed the unused in-app remote-access / admin-key settings —
  as a Home Assistant add-on, Home Assistant already handles authentication and
  remote access (web and mobile app), so no separate key is needed.
- Removed dead offline/PWA plumbing that never activated behind the ingress.
- Internal only: no action required, and your data on `/data` is preserved across
  the update as usual.
