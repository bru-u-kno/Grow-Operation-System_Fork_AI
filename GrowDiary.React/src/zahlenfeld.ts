/**
 * Getippten Text in eine Zahl verwandeln — an einer Stelle für die ganze App.
 *
 * **Warum es diese Datei gibt.** Sieben Seiten hatten je eine eigene Fassung
 * davon, und sie waren nicht gleich:
 *
 * - `ManualMeasurementPage` prüfte mit `Number.isFinite` und meldete unlesbare
 *   Felder — dort war der Fehler 2026-08 schon einmal behoben worden.
 * - `MeasurementEditPage` prüfte mit `Number.isNaN`. Damit gilt `Infinity` als
 *   gültige Zahl, und aus „6,2x" wird stillschweigend `null`. Die Bearbeiten-
 *   Seite ist laut `e2e/formular-rundweg.spec.ts` „der einzige Weg, auf dem ein
 *   vorhandener Wert VERSCHWINDEN kann" — genau dort stand der Fehler noch.
 * - `DosingPumpSetupPage` hatte gar keine Leerprüfung: `Number('')` ist `0` und
 *   `Number.isFinite(0)` ist `true`. Ein geleertes Feld wurde also zur **Null**.
 *   Für den Mindestabstand einer Dosierpumpe heisst das: keine Mischpause mehr,
 *   still, mit Erfolgsmeldung.
 *
 * Die Unterscheidung, um die es geht, ist **leer gegen unlesbar**. Beides ergibt
 * `null`, meint aber Verschiedenes: „ich habe nichts gemessen" gegen „ich habe
 * mich vertippt". Wer sie gleich behandelt, verliert Daten ohne ein Wort.
 */

/** Leer heisst leer — auch bei Leerzeichen. */
export function istLeer(text: string): boolean {
  return text.trim() === ''
}

/**
 * Punkte als Tausendertrenner: eine Gruppe aus ein bis drei Ziffern (nicht mit
 * 0 beginnend), dann nur Gruppen aus GENAU drei Ziffern — „1.200", „12.500",
 * „1.234.567". Nur so schreibt man Tausender; alles andere mit Punkt kann kein
 * Tausendertrenner sein.
 *
 * Die führende 0 ist ausgeschlossen, weil „0.500" niemand für fünfhundert
 * schreibt — das ist ein halber Liter in englischer Schreibweise.
 */
const TAUSENDER_GRUPPIERT = /^[+-]?[1-9]\d{0,2}(\.\d{3})+$/

/**
 * Der Zahlenwert, oder `null` bei leer **und** bei unlesbar.
 *
 * Wer wissen muss, welcher der beiden Fälle vorliegt, fragt zusätzlich
 * {@link istUnlesbar} — sonst geht ein Tippfehler als „nicht gemessen" durch.
 *
 * **Die Leseregel ist die deutsche: Komma = Dezimalzeichen, Punkt =
 * Tausendertrenner.** Festgelegt vom Nutzer am 02.10.2026: „1.200" ist 1200.
 *
 * | getippt | gelesen | warum |
 * |---|---|---|
 * | „1,5" | 1,5 | Komma ist das Dezimalzeichen |
 * | „1.200", „12.500", „1.234.567" | 1200, 12500, 1234567 | Punkte in Dreiergruppen = Tausender |
 * | „1.200,5" | 1200,5 | beides zusammen |
 * | „5.8", „6.25", „1.20", „1.2000", „0.500" | 5,8 · 6,25 · 1,2 · 1,2 · 0,5 | siehe unten |
 * | „1.2,5", „1,2,3", „1,5.3" | unlesbar | Punkt vor dem Komma, der keine Tausendergruppe ist |
 *
 * **Ein Punkt, der kein Tausendertrenner sein KANN, ist ein Dezimalpunkt.**
 * „5.8" als unlesbar abzuweisen wäre die strengere Wahl, aber die schlechtere:
 * Messgeräte zeigen den Punkt, manche Telefon-Tastaturen bieten im Zahlenfeld
 * nur ihn an, und „5.8" war bis heute in jedem Feld ein gültiger pH-Wert. Wer
 * das jetzt abwiese, bräche eine eingeübte Eingabe — und es gibt keine zweite
 * Deutung, die man damit schützen würde: „5.8" kann nach keiner Leseart
 * achtundfünfzig oder fünftausendacht heißen.
 *
 * **Der Preis dieser Regel:** wer englisch denkt und „1.200" für eins-komma-zwei
 * meint, bekommt 1200. Das ist die Entscheidung des Nutzers, und sie ist
 * sicherer als die alte: ein EC von 1200 oder ein pH von 5800 fängt die Sperre
 * je Messfeld ab (`MessfelderVollstaendigTests`) — ein Preis von 1,20 € statt
 * 1200 € oder ein CO₂-Grenzwert von 1,2 ppm statt 1200 ppm fiel niemandem auf.
 */
export function zahlOderNull(text: string): number | null {
  if (istLeer(text)) return null
  const roh = text.trim()

  let normalisiert: string
  const teile = roh.split(',')
  if (teile.length > 2) {
    return null                                   // „1,2,3"
  } else if (teile.length === 2) {
    const [ganz, bruch] = teile
    // Vor dem Komma dürfen Punkte nur Tausendergruppen sein, dahinter gar keine.
    if (ganz.includes('.') && !TAUSENDER_GRUPPIERT.test(ganz)) return null
    if (bruch.includes('.')) return null
    normalisiert = `${ganz.split('.').join('')}.${bruch}`
  } else if (TAUSENDER_GRUPPIERT.test(roh)) {
    normalisiert = roh.split('.').join('')        // „1.200" → „1200"
  } else {
    normalisiert = roh                            // „5.8", „1200"
  }

  // `Number`, nicht `parseFloat`: `parseFloat('6,2x')` ist 6,2 und meldet
  // nichts. `Number.isFinite`, nicht `Number.isNaN`: sonst gilt `Infinity` als
  // Zahl und landet in der Datenbank.
  const wert = Number(normalisiert)
  return Number.isFinite(wert) ? wert : null
}

/**
 * Ein Zahlenwert, den eine **Maschine** geschrieben hat — nicht ein Mensch.
 *
 * Das ist die zweite und einzige andere Leseregel der App, und sie steht mit
 * Absicht neben der ersten: ein Zustand aus Home Assistant („1.234" Volt) oder
 * der Wert eines `<input type="number">` (der Browser liefert immer die
 * technische Form) hat einen Dezimal**punkt** und nie Tausendertrenner. Die
 * deutsche Regel machte daraus 1234 — tausendmal zu viel.
 *
 * Ein Komma wird nur gelesen, wenn kein Punkt dasteht: manche Vorlagen-Sensoren
 * in Home Assistant schreiben deutsch („5,8").
 *
 * Wer getippten Text liest, nimmt {@link zahlOderNull}.
 */
export function maschinenZahl(text: string | null | undefined): number | null {
  if (text == null || istLeer(text)) return null
  const roh = text.trim()
  const wert = Number(roh.includes('.') ? roh : roh.replace(',', '.'))
  return Number.isFinite(wert) ? wert : null
}

/** Steht da etwas, das keine Zahl ist? */
export function istUnlesbar(text: string): boolean {
  return !istLeer(text) && zahlOderNull(text) === null
}

/**
 * Die Beschriftungen aller Felder, in denen etwas Unlesbares steht.
 *
 * @param felder Paare aus Rohtext und Beschriftung — die Beschriftung ist das,
 *   was der Nutzer sieht („pH (Reservoir)", nicht `reservoirPh`).
 */
export function unlesbareFelder(felder: Array<[string, string]>): string[] {
  return felder.filter(([roh]) => istUnlesbar(roh)).map(([, beschriftung]) => beschriftung)
}

/**
 * Ein Satz für den Nutzer, wenn Felder unlesbar sind — oder `null`.
 *
 * Er sagt ausdrücklich, was sonst passiert. „Ungültige Eingabe" allein lässt
 * offen, ob gespeichert wurde; genau diese Unklarheit hat den Fehler damals so
 * teuer gemacht.
 */
export function unlesbarMeldung(beschriftungen: string[]): string | null {
  if (beschriftungen.length === 0) return null
  if (beschriftungen.length === 1) {
    return `„${beschriftungen[0]}" ist keine Zahl. Bitte korrigieren oder das Feld leeren — `
      + 'sonst geht der Wert verloren, ohne dass es jemand merkt.'
  }
  return `Diese Felder enthalten keine Zahl: ${beschriftungen.join(', ')}. `
    + 'Bitte korrigieren oder leeren — sonst gehen die Werte verloren, ohne dass es jemand merkt.'
}

/**
 * Eine gespeicherte Zahl zurück in ein Eingabefeld — mit Komma.
 *
 * Das Gegenstück zu {@link zahlOderNull}: die eine Richtung liest, was der
 * Nutzer tippt, die andere schreibt zurück, was gespeichert war.
 *
 * **Der Anlass (01.09.2026).** Diese drei Zeilen standen **fünfmal** in der
 * Oberfläche — als `draftNumber`, `numberToInput` und zweimal als
 * `formatDraftNumber` — und alle fünf schrieben `String(value)`. Ein
 * gespeichertes Nassgewicht von 21,5 g kam damit als „21.5" ins Feld zurück,
 * direkt neben einer Spalte, die „21,5" schreibt. Wer nichts änderte und
 * speicherte, schickte den Punkt wieder los.
 *
 * Gehalten wird das von `e2e/deutsche-zahlen.spec.ts`, das die Werte der
 * Eingabefelder mitliest — nicht nur den Text daneben.
 */
export function feldText(wert: number | null | undefined): string {
  if (wert == null || Number.isNaN(wert)) return ''
  return String(wert).replace('.', ',')
}
