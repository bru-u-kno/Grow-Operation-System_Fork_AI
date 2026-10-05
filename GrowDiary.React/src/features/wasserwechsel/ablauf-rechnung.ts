import type { MischplanRolle, WasserwechselVorgangDto, WaterSource } from '../../types'
import { zahlOderNull } from '../../zahlenfeld'

/**
 * Wasserwechsel-Ablauf (A-006) — die Regeln ohne Oberfläche.
 *
 * Was hier steht, ist nur die Buchhaltung des Formulars: welche Werte der
 * Nutzer geändert hat, was zurückgeholt werden kann, welche Zeilen gebucht
 * werden, wie die Tagebuchzeile lautet. **Die Vorschläge selbst rechnet das
 * Backend** (`MischplanVorschlagRechnung`) — Wasser-EC, EC-Ziel, CalMag. Hier
 * wird keine dieser Zahlen ein zweites Mal gerechnet.
 */

/** Eine Zeile der Ansetz-Tabelle. */
export type AblaufZeile = {
  /** `plan:<Komponente>` oder `extra:<ArtikelId>` — stabil über Neuberechnungen. */
  schluessel: string
  name: string
  art: 'plan' | 'extra'
  rolle: MischplanRolle | null
  /** Vorschlag des Plans in ml; `null` bei Zeilen „nicht im Plan". */
  vorschlagMl: number | null
  hinweis: string | null
  artikelId: number | null
  einheit: string
}

/** Eingaben je Zeile, als Text (damit „0,5" beim Tippen nicht zerfällt). */
export type Werte = Record<string, string>

/** Eine Zahl auf deutsch mit fester Stellenzahl: 0,50 · 1,2 · 160. */
export function zahl(wert: number, stellen: number): string {
  return new Intl.NumberFormat('de-DE', { minimumFractionDigits: stellen, maximumFractionDigits: stellen }).format(wert)
}

/** Was im Feld steht: der eigene Wert, sonst der Vorschlag. */
export function wertDerZeile(zeile: AblaufZeile, eigen: Werte): string {
  return eigen[zeile.schluessel] ?? (zeile.vorschlagMl != null ? zahl(zeile.vorschlagMl, 0) : '')
}

/** Menge, die in der Zeile steht — 0 bei leer oder unlesbar. */
export function mengeDerZeile(zeile: AblaufZeile, eigen: Werte): number {
  return zahlOderNull(wertDerZeile(zeile, eigen)) ?? 0
}

/** Hat der Nutzer diese Plan-Zeile vom Vorschlag weg geändert? */
export function istGeaendert(zeile: AblaufZeile, eigen: Werte): boolean {
  const meins = eigen[zeile.schluessel]
  return zeile.art === 'plan' && meins != null && zeile.vorschlagMl != null && zahlOderNull(meins) !== zeile.vorschlagMl
}

/**
 * Ein gemerkter eigener Wert, der gerade nicht eingesetzt ist und vom Vorschlag
 * abweicht — dann bietet die Zeile „↶ deins …" an.
 */
export function meinsVerfuegbar(zeile: AblaufZeile, eigen: Werte, gemerkt: Werte): boolean {
  const alt = gemerkt[zeile.schluessel]
  return zeile.art === 'plan' && eigen[zeile.schluessel] == null && alt != null && zahlOderNull(alt) !== zeile.vorschlagMl
}

/** „↺ Alles auf Vorschlag": eigene Werte der Plan-Zeilen weg, Zeilen „nicht im Plan" bleiben. */
export function alleAufVorschlag(zeilen: AblaufZeile[], eigen: Werte): Werte {
  const plan = new Set(zeilen.filter((z) => z.art === 'plan').map((z) => z.schluessel))
  return Object.fromEntries(Object.entries(eigen).filter(([schluessel]) => !plan.has(schluessel)))
}

/** „↶ Meine Werte wieder einsetzen": alle gemerkten, die gerade nicht stehen. */
export function meineWiederEinsetzen(zeilen: AblaufZeile[], eigen: Werte, gemerkt: Werte): Werte {
  const zurueck = zeilen.filter((z) => meinsVerfuegbar(z, eigen, gemerkt)).map((z) => [z.schluessel, gemerkt[z.schluessel]])
  return { ...eigen, ...Object.fromEntries(zurueck) }
}

/**
 * Anteil der Plan-Dosis bei den Grunddüngern (A, B, PK): eingesetzt ÷ vorgeschlagen.
 *
 * Grundlage für „Mit deinen Mengen ≈ …". `null`, wenn der Plan keine
 * Grunddünger nennt.
 */
export function anteilPlanDosis(zeilen: AblaufZeile[], eigen: Werte): number | null {
  const grund = zeilen.filter((z) => z.rolle === 'Grundduenger' && z.vorschlagMl != null)
  const plan = grund.reduce((summe, z) => summe + (z.vorschlagMl ?? 0), 0)
  if (plan <= 0) return null
  return grund.reduce((summe, z) => summe + mengeDerZeile(z, eigen), 0) / plan
}

/**
 * Faustregel: der Dünger-EC wächst etwa im Verhältnis der Dosis.
 *
 * Etikett, keine Messung — genau so steht es auf der Seite („Faustregel").
 * Die Lösung ist am Ende so stark, wie das Messgerät sagt; das steht im
 * Schritt „Nachher".
 */
export function ecMitDeinenMengen(wasserEc: number | null, ecZielDuenger: number | null, anteil: number | null): number | null {
  if (wasserEc == null || ecZielDuenger == null || anteil == null) return null
  return wasserEc + ecZielDuenger * anteil
}

/** Die Wasser-Zeilen: eine je Wasserart, bei der Mischung zwei. Liter auf eine Stelle. */
export function wasserZeilen(liter: number, wasser: WaterSource, osmoseAnteil: number): Array<{ wasser: 'Tap' | 'RO'; name: string; menge: number }> {
  const runden = (wert: number) => Math.round(wert * 10) / 10
  if (wasser === 'RO') return [{ wasser: 'RO', name: 'Osmosewasser', menge: runden(liter) }]
  if (wasser === 'Mixed') {
    // Leitung = Rest: zwei einzeln gerundete Teile ergaben 68,8 + 68,8 = 137,6 L
    // bei 137,5 L angesetzt (Befund des Prüfers) — gebucht wird, was angesetzt wurde.
    const osmose = runden(liter * osmoseAnteil)
    return [
      { wasser: 'RO', name: 'Osmosewasser', menge: osmose },
      { wasser: 'Tap', name: 'Leitungswasser', menge: runden(liter - osmose) },
    ].filter((z) => z.menge > 0) as Array<{ wasser: 'Tap' | 'RO'; name: string; menge: number }>
  }
  return [{ wasser: 'Tap', name: 'Leitungswasser', menge: runden(liter) }]
}

/** Vorher → nachher als Änderung mit Vorzeichen: „−0,48", „+0,03", „±0,00". */
export function aenderung(vorher: number | null, nachher: number | null, stellen: number): string | null {
  if (vorher == null || nachher == null) return null
  const differenz = Number((nachher - vorher).toFixed(stellen))
  if (differenz === 0) return `±${zahl(0, stellen)}`
  return `${differenz > 0 ? '+' : '−'}${zahl(Math.abs(differenz), stellen)}`
}

export type TagebuchWerte = {
  liter: number
  wasserName: string
  vorher: { ec: number | null; ph: number | null; wt: number | null; do: number | null; orp: number | null }
  nachher: { ec: number | null; ph: number | null; wt: number | null; do: number | null; orp: number | null }
  zugaben: Array<{ name: string; menge: number; einheit: string }>
  notiz: string
}

/**
 * Die Zeile fürs Tagebuch — dieselbe, die die Vorschau zeigt und die gespeichert wird.
 *
 * Gebaut nur hier: Vorschau und gespeicherter Text können so nicht auseinanderlaufen.
 */
export function tagebuchZeile(w: TagebuchWerte): { titel: string; text: string } {
  const paar = (name: string, vor: number | null, nach: number | null, stellen: number, einheit = '') => {
    if (vor == null && nach == null) return null
    const v = vor == null ? '—' : zahl(vor, stellen)
    const n = nach == null ? '—' : zahl(nach, stellen)
    return `${name} ${v} → ${n}${einheit.replace(' ', '\u00a0')}`
  }
  const werte = [
    paar('EC', w.vorher.ec, w.nachher.ec, 2),
    paar('pH', w.vorher.ph, w.nachher.ph, 2),
    paar('Wasser', w.vorher.wt, w.nachher.wt, 1, ' °C'),
    paar('DO', w.vorher.do, w.nachher.do, 1, ' mg/L'),
    paar('ORP', w.vorher.orp, w.nachher.orp, 0, ' mV'),
  ].filter((teil): teil is string => teil != null)

  const zugaben = w.zugaben
    .filter((z) => z.menge > 0)
    // Geschütztes Leerzeichen: „180 | ml" brach in der Vorschau auseinander (Prüfer).
    .map((z) => `${z.name} ${zahl(z.menge, Number.isInteger(z.menge) ? 0 : 1)}\u00a0${z.einheit}`)

  const zeilen = [
    werte.join(' · '),
    zugaben.length > 0 ? `Zugaben: ${zugaben.join(' · ')}` : '',
    w.notiz.trim(),
  ].filter((zeile) => zeile !== '')

  return { titel: `Wasserwechsel ${zahl(w.liter, Number.isInteger(w.liter) ? 0 : 1)} L ${w.wasserName}`, text: zeilen.join('\n') }
}

/** „2 Messwerten, 6 Buchungen und der Tagebuchzeile" — was am Vorgang hängt. */
export function teileText(vorgang: WasserwechselVorgangDto): string {
  const messwerte = [vorgang.vorher, vorgang.nachher].filter(Boolean).length
  const teile = [
    messwerte > 0 ? `${messwerte} ${messwerte === 1 ? 'Messwert' : 'Messwerten'}` : null,
    vorgang.buchungen.length > 0 ? `${vorgang.buchungen.length} ${vorgang.buchungen.length === 1 ? 'Buchung' : 'Buchungen'}` : null,
    vorgang.tagebuch ? 'der Tagebuchzeile' : null,
  ].filter((teil): teil is string => teil != null)
  if (teile.length === 0) return 'nichts weiter'
  return teile.length === 1 ? teile[0] : `${teile.slice(0, -1).join(', ')} und ${teile[teile.length - 1]}`
}
