/**
 * Fork AI: Wo die Bildschirmzeile einer Kachel endet.
 *
 * Der Verlauf einer angetippten Kachel stand bisher unter dem GANZEN Bereich.
 * Am Telefon (zwei Spalten) lag er damit rund 1100 px unter der Kachel, hinter
 * drei Kachelzeilen und der Verlaufskarte: man tippte, und scheinbar geschah
 * nichts — oder irgendetwas (Bru, 05.10.2026). Jetzt kommt er direkt unter die
 * Zeile, in der die Kachel steht.
 *
 * Wo eine Zeile endet, weiß nur das Layout: die Kacheln brechen frei um
 * (`flex-wrap`), bei 390 px nach zwei, am Rechner nach fünf. Also wird
 * gemessen — mit ausgeblendetem Verlauf, denn ein Verlauf an alter Stelle
 * (nach einer Drehung des Telefons) würde die Zeilen verschieben, die er
 * messen soll.
 *
 * Gezählt werden die direkten Kinder mit `data-kachel-platz`.
 *
 * @returns Index des letzten Platzes in derselben Zeile, oder null, wenn der
 * Platz in keiner Kachelzeile steht.
 */
export function zeilenEnde(platz: HTMLElement): number | null {
  const zeile = platz.parentElement
  if (!zeile) return null
  const plaetze = [...zeile.children].filter((kind): kind is HTMLElement =>
    kind instanceof HTMLElement && kind.hasAttribute('data-kachel-platz'))
  const index = plaetze.indexOf(platz)
  if (index < 0) return null

  const verlauf = [...zeile.children].find((kind): kind is HTMLElement =>
    kind instanceof HTMLElement && kind.hasAttribute('data-kachel-verlauf'))
  const vorher = verlauf?.style.display ?? ''
  if (verlauf) verlauf.style.display = 'none'
  try {
    const oben = platz.offsetTop
    let nach = index
    while (nach + 1 < plaetze.length && plaetze[nach + 1].offsetTop === oben) nach++
    return nach
  } finally {
    if (verlauf) verlauf.style.display = vorher
  }
}

/** Der Platz einer Kachel in der Seite — per Kennung, die ihr Bereich vergibt. */
export function kachelPlatz(kennung: string): HTMLElement | null {
  return document.querySelector<HTMLElement>(`[data-kachel-platz="${CSS.escape(kennung)}"]`)
}

/** Die Kennung des Verlaufs — die Kachel verweist per `aria-controls` darauf. */
export const kachelVerlaufId = (kennung: string) => `kachel-verlauf-${kennung}`

/**
 * Was ein Bereich sich zur offenen Kachel merkt: welche, nach welchem Platz
 * der Verlauf steht — und ob er nach dem Erscheinen ins Bild rollen soll.
 *
 * `insBild` setzt nur ein Tipp. Der Verlauf hängt sich auch ohne Tipp neu ein
 * (Anpassen → Fertig, Bereich zu und wieder auf), und dann sprang die Seite um
 * 500 px (Befund des Prüfers, 05.10.2026). Nach dem Rollen löscht der Verlauf
 * die Marke selbst (`onImBild`).
 */
export type OffeneKachel = { kennung: string; nach: number; insBild?: boolean }

/** Öffnet oder schließt — gemessen im Moment des Tipps, nicht in einem Effekt. */
export function kachelUmschalten(offen: OffeneKachel | null, kennung: string): OffeneKachel | null {
  if (offen?.kennung === kennung) return null
  const platz = kachelPlatz(kennung)
  const nach = platz ? zeilenEnde(platz) : null
  return nach == null ? null : { kennung, nach, insBild: true }
}
