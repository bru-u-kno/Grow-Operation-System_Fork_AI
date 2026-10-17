/**
 * Fork AI (forkai.225): Rechenteil der Zuluft-Hysterese — ohne React, damit er sich ohne
 * Browser prüfen lässt.
 *
 * Die Regelung in Home Assistant schaltet ein bei Differenz ≥ Mindest-Differenz und aus bei
 * Differenz < Mindest-Differenz − Abschalt-Abstand. Dazwischen bleibt der Zustand, wie er ist.
 */

export type Haltebereich = {
  /** Ab dieser Differenz wird angesaugt. */
  anAb: number
  /** Darunter hört der Lüfter auf; nie unter 0. */
  ausUnter: number
}

/** Auf zwei Stellen gerundet, damit 1 − 0,3 nicht 0,7000000000000001 wird. */
const rund = (w: number) => Math.round(w * 100) / 100

export function haltebereich(mindestDifferenz: number, abschaltAbstand: number): Haltebereich {
  return { anAb: rund(mindestDifferenz), ausUnter: Math.max(0, rund(mindestDifferenz - abschaltAbstand)) }
}

/**
 * Hält der Lüfter gerade nur, weil er schon läuft? Ja, wenn der Bedarf an ist, die Differenz
 * aber unter der Einschaltgrenze liegt — er ginge jetzt nicht neu an, hört aber auch noch nicht auf.
 */
export function haeltGerade(bedarf: boolean | null, differenz: number | null, hb: Haltebereich): boolean {
  return bedarf === true && differenz != null && Number.isFinite(differenz) && differenz < hb.anAb
}

export type BandStellen = {
  /** Skalenende in g/m³. */
  bis: number
  /** Positionen in Prozent der Bandbreite. */
  ausPos: number
  anPos: number
  /** Position des Istwerts; null ohne Messwert. */
  istPos: number | null
}

/**
 * Die Skala beginnt bei 0 und reicht bis zum Doppelten der Einschaltgrenze (mindestens 2), bei
 * größerer Differenz bis dahin. Ein fester Ausschnitt wie beim Entfeuchter lohnt hier nicht: die
 * Differenz ist selten negativ, und die 0 ist der natürliche Anfang.
 */
export function bandStellen(hb: Haltebereich, differenz: number | null): BandStellen {
  const ist = differenz != null && Number.isFinite(differenz) ? differenz : null
  const bis = Math.max(2, Math.ceil(hb.anAb * 2), ist == null ? 0 : Math.ceil(ist))
  const pos = (w: number) => Math.min(100, Math.max(0, Math.round((w / bis) * 1000) / 10))
  return { bis, ausPos: pos(hb.ausUnter), anPos: pos(hb.anAb), istPos: ist == null ? null : pos(ist) }
}

/** Ein Messwert, wie er im Rechenweg steht — mit „–" statt einer erfundenen Null. */
export function zeigZahl(wert: number | null, einheit: string, stellen: number): string {
  return wert == null || !Number.isFinite(wert) ? '–' : wert.toLocaleString('de-DE', { minimumFractionDigits: stellen, maximumFractionDigits: stellen }) + einheit
}
