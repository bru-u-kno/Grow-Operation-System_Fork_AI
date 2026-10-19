/**
 * Welchen Messpunkt hat der Nutzer angetippt?
 *
 * **Der Anlass (01.09.2026).** Der Nutzer: „beim diagram von den live daten und
 * den verlauf ins diagramm an einer stelle klicken und dann die werte angezeigt
 * werden und beim nächsten klick werden die daten aktualisiert."
 *
 * **Warum das mehr ist als eine Bequemlichkeit.** Am Handy gibt es kein Hover.
 * Ein Diagramm, das seine Zahlen nur beim Überfahren mit der Maus zeigt, ist
 * auf dem Telefon stumm — und Cultivate Control wird überwiegend am Telefon benutzt.
 *
 * Die Rechnung steht hier und nicht im Bauteil, damit sie ohne Browser prüfbar
 * ist: ein Diagramm, das man nur ansehen kann, lässt sich nur ansehen.
 */

/** Ein Punkt, so wie ihn das Diagramm kennt. */
export type Messpunkt = { t: string; v: number }

/**
 * Rechnet einen Klick in Bildschirm-Koordinaten auf die Zeichenfläche um.
 *
 * Das SVG hat einen festen `viewBox` und skaliert über `width: 100%`. Ein Klick
 * kommt aber in echten Pixeln — ohne diese Umrechnung träfe man auf einem
 * breiten Bildschirm systematisch zu weit links.
 */
export function inZeichenflaeche(klickX: number, kastenLinks: number, kastenBreite: number, breiteImBild: number): number {
  if (kastenBreite <= 0) return 0
  return ((klickX - kastenLinks) / kastenBreite) * breiteImBild
}

/**
 * Der Punkt, der einer Stelle am nächsten liegt — oder `null` ohne Punkte.
 *
 * @param punkte die Reihe, in der gesucht wird
 * @param x die Stelle auf der Zeichenfläche
 * @param xVon rechnet einen Zeitpunkt auf die Zeichenfläche um
 *
 * **Der nächste, nicht der darunter.** Wer knapp neben die Linie tippt, meint
 * den Punkt daneben — nicht „nichts". Ein Diagramm, das auf einen Treffer
 * besteht, ist mit dem Finger unbedienbar.
 */
export function punktBeiX<T extends Messpunkt>(
  punkte: readonly T[],
  x: number,
  xVon: (zeit: number) => number,
): { punkt: T; index: number } | null {
  if (punkte.length === 0) return null

  let besterIndex = 0
  let besterAbstand = Number.POSITIVE_INFINITY

  for (let i = 0; i < punkte.length; i += 1) {
    const abstand = Math.abs(xVon(new Date(punkte[i].t).getTime()) - x)
    if (abstand < besterAbstand) {
      besterAbstand = abstand
      besterIndex = i
    }
  }

  return { punkt: punkte[besterIndex], index: besterIndex }
}

/**
 * Der Zeitpunkt, wie er unter dem Diagramm steht.
 *
 * Bei Tageswerten reicht der Tag; bei Rohwerten braucht es die Uhrzeit, sonst
 * stehen sechs Punkte desselben Tages unter demselben Text.
 */
export function zeitpunktText(iso: string, aufloesung: 'daily' | 'raw'): string {
  const datum = new Date(iso)
  if (Number.isNaN(datum.getTime())) return ''

  return aufloesung === 'daily'
    ? new Intl.DateTimeFormat('de-DE', { day: '2-digit', month: '2-digit' }).format(datum)
    : new Intl.DateTimeFormat('de-DE', {
      day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit',
    }).format(datum)
}
