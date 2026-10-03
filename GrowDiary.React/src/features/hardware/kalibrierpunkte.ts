import { unlesbareFelder, zahlOderNull } from '../../zahlenfeld'

/**
 * Die Messpunkte einer Kalibrierung — und die Steilheit daraus.
 *
 * **Der Anlass (01.09.2026).** Der Nutzer: „beim ph messer gibt es mehr
 * messpunkte also beispiel 4 und 7 oder auch andere." Das Formular hatte
 * **ein** Feldpaar.
 *
 * **Warum das mehr ist als ein zweites Feld.** Ein einzelner Abgleich gegen
 * pH 7,00 sagt über die Sonde nichts: eine tote Sonde lässt sich auf 7,00
 * genauso einstellen wie eine frische. Erst der Abstand zwischen zwei Punkten
 * verrät, ob sie noch spreizt.
 *
 * **Dieselbe Rechnung wie im Backend.** `Kalibrierpunkte.SteilheitProzent`
 * rechnet identisch; hier steht sie, damit der Nutzer die Zahl schon beim
 * Tippen sieht. Beide Seiten prüfen denselben ausgerechneten Fall
 * (6,82 bei Puffer 7 und 4,15 bei Puffer 4 ergeben 89,3 %) — laufen sie
 * auseinander, wird eine der beiden Prüfungen rot.
 */

/** Eine Zeile des Formulars, so wie sie auf dem Schirm steht. */
export type PunktZeile = {
  loesung: string
  sollText: string
  vorherText: string
  nachherText: string
}

/** Ein Punkt, wie ihn der Server erwartet. */
export type Punkt = {
  loesung: string | null
  sollwert: number | null
  vorher: number | null
  nachher: number | null
}

/** Die übliche Vorbelegung je Art von Sonde. */
export function vorbelegung(art: string): PunktZeile[] {
  const leer = { sollText: '', vorherText: '', nachherText: '' }
  if (/ph/i.test(art)) {
    // Zweipunkt ist der Normalfall; wer drei fährt, ergänzt eine Zeile.
    return [
      { loesung: 'pH 4,01', ...leer, sollText: '4,01' },
      { loesung: 'pH 7,00', ...leer, sollText: '7,00' },
    ]
  }
  if (/ec|leitf/i.test(art)) {
    return [{ loesung: '1413 µS/cm', ...leer, sollText: '1,413' }]
  }
  return [{ loesung: '', ...leer }]
}

/** Die Zeilen, die wirklich etwas tragen. */
export function speicherbarePunkte(zeilen: readonly PunktZeile[]): Punkt[] {
  return zeilen
    .map((z) => ({
      loesung: z.loesung.trim() || null,
      sollwert: zahlOderNull(z.sollText),
      vorher: zahlOderNull(z.vorherText),
      nachher: zahlOderNull(z.nachherText),
    }))
    .filter((p) => p.sollwert != null || p.vorher != null || p.nachher != null)
}

/**
 * Die Felder, in denen etwas Unlesbares steht — beschriftet wie am Eingabefeld
 * („Vorher 2").
 *
 * **Der Anlass (offene Punkte 03.10.2026, B9).** {@link speicherbarePunkte}
 * macht aus „6,8x" ein `null`, und eine Zeile ohne lesbaren Wert fällt ganz
 * weg. Ohne diese Frage vor dem Speichern verschwand der Punkt still — und mit
 * ihm die Steilheit, an der die Fälligkeit der Sonde hängt.
 */
export function unlesbarePunktFelder(zeilen: readonly PunktZeile[]): string[] {
  return zeilen.flatMap((z, i) => unlesbareFelder([
    [z.sollText, `Sollwert ${i + 1}`],
    [z.vorherText, `Vorher ${i + 1}`],
    [z.nachherText, `Nachher ${i + 1}`],
  ]))
}

/** Unter diesem Wert gilt eine Sonde als fällig — Faustregel, siehe unten. */
export const STEILHEIT_FAELLIG_UNTER = 85

/**
 * Die Steilheit in Prozent — oder `null`, solange zwei taugliche Punkte fehlen.
 *
 * Gerechnet wird über die Werte **vor** dem Abgleich: danach steht die Sonde
 * per Definition auf den Sollwerten, und die Steilheit wäre immer 100 %.
 */
export function steilheitProzent(punkte: readonly Punkt[]): number | null {
  const taugliche = punkte
    .filter((p) => p.sollwert != null && p.vorher != null)
    .sort((a, b) => (a.sollwert as number) - (b.sollwert as number))

  if (taugliche.length < 2) return null

  const unten = taugliche[0]
  const oben = taugliche[taugliche.length - 1]

  const erwartet = (oben.sollwert as number) - (unten.sollwert as number)
  if (Math.abs(erwartet) < 0.001) return null

  const gemessen = (oben.vorher as number) - (unten.vorher as number)
  return Math.round((gemessen / erwartet) * 1000) / 10
}

/**
 * Der Satz zur Steilheit — mit Etikett.
 *
 * Projektregel: Faustregeln nur mit Etikett. Die Zahlen 95–105 % und 85 %
 * stehen so in den Handbüchern gängiger Sonden (Bluelab, Hanna, Milwaukee);
 * Grow OS gibt sie weiter und erfindet keine eigene Schwelle.
 */
export const STEILHEIT_GUT_AB = 95
export const STEILHEIT_GUT_BIS = 105

/**
 * **Drei Stufen, nicht zwei.** Eine erste Fassung nannte alles über 85 % „im
 * üblichen Bereich" — auch 89 %, das nun einmal *nicht* in 95–105 liegt. Eine
 * Sonde, die nachlässt, aber noch taugt, ist genau der Fall, den der Nutzer
 * früh sehen will.
 */
export function steilheitSatz(prozent: number | null): string | null {
  if (prozent == null) return null
  const zahl = prozent.toLocaleString('de-DE', { maximumFractionDigits: 1 })
  const kopf = `Steilheit ${zahl} % — `
  const regel = ` (Faustregel aus den Sonden-Handbüchern: ${STEILHEIT_GUT_AB}–${STEILHEIT_GUT_BIS} % gut, `
    + `unter ${STEILHEIT_FAELLIG_UNTER} % fällig.)`

  if (prozent < STEILHEIT_FAELLIG_UNTER) return `${kopf}die Sonde ist fällig.${regel}`
  if (prozent < STEILHEIT_GUT_AB) return `${kopf}brauchbar, aber unter dem üblichen Bereich; im Auge behalten.${regel}`
  if (prozent > STEILHEIT_GUT_BIS) return `${kopf}ungewöhnlich hoch. Stimmen die Pufferlösungen und ihre Sollwerte?${regel}`
  return `${kopf}im üblichen Bereich.${regel}`
}
