/* Fork AI (F-048): Reiterleiste nach dem Wechsel ins Bild rollen.
 *
 * Bru tippt am Handy weit unten auf einen Reiter und sah den neuen Inhalt
 * nicht, weil er unter der Bildkante lag. Nach dem Wechsel rollt die Seite
 * deshalb so, dass die Reiterleiste direkt unter der festen Kopfflaeche steht
 * (`.scroll-ziel` liefert den Abstand) und der Inhalt darunter sichtbar ist.
 *
 * Kurze Reiter: ist die Seite nicht lang genug, um so weit zu rollen, bekommt
 * der Seitenrahmen unten einen Auslauf (`--reiter-auslauf`). So steht die
 * Leiste nach jedem Wechsel an derselben Stelle (Mockup, von Bru am
 * 23.09.2026 freigegeben).
 */

const VAR = '--reiter-auslauf'

/** Wie viel Auslauf fehlt, damit `ziel` erreichbar ist. Rein, damit testbar. */
export function auslaufBerechnen(ziel: number, seitenHoehe: number, fensterHoehe: number): number {
  const maxRollen = seitenHoehe - fensterHoehe
  return ziel > maxRollen ? Math.ceil(ziel - maxRollen) : 0
}

export function auslaufZuruecksetzen(): void {
  document.documentElement.style.removeProperty(VAR)
}

/**
 * Gemessene Unterkante der festen Leisten am Handy plus kleiner Abstand. Der
 * feste Wert `--mobil-kopf` stimmt nicht mehr, sobald die Schrift am Telefon
 * groesser gestellt ist — dann waechst die Leiste, die Zahl nicht.
 */
function kopfUnterkante(): number {
  let unten = 0
  for (const el of document.querySelectorAll<HTMLElement>('.v1-mobile-topbar, .v1-mobile-nav')) {
    if (getComputedStyle(el).position !== 'fixed') continue
    const r = el.getBoundingClientRect()
    if (r.height > 0 && r.top < 4) unten = Math.max(unten, r.bottom)
  }
  return unten > 0 ? Math.ceil(unten) + 8 : 0
}

/**
 * Rollt `ziel` unter die feste Kopfflaeche — mit eigener Rechnung statt
 * `scrollIntoView`.
 *
 * F-049 (23.09.2026): In Brus HA-App landete ein per `scrollIntoView`
 * angesprungenes Kosten-Formular um etwa die Kopfhoehe zu tief (Titel und erstes
 * Feld unter der Leiste), im Desktop-Chromium dagegen richtig. Die Reiter aus
 * F-048 rechnen selbst und landeten auch bei ihm richtig. Deshalb lesen beide
 * den Rand aus `scroll-margin-top` (`.scroll-ziel`) und rollen mit
 * `window.scrollTo` — der Browser muss den Rand nicht selbst beachten.
 */
export function insBildRollen(ziel: HTMLElement | null): void {
  if (!ziel?.isConnected) return
  auslaufZuruecksetzen()
  const rand = Math.max(parseFloat(getComputedStyle(ziel).scrollMarginTop) || 0, kopfUnterkante())
  const oben = Math.max(0, ziel.getBoundingClientRect().top + window.scrollY - rand)
  const auslauf = auslaufBerechnen(oben, document.documentElement.scrollHeight, window.innerHeight)
  if (auslauf > 0) document.documentElement.style.setProperty(VAR, `${auslauf}px`)
  window.scrollTo({ top: oben, behavior: 'smooth' })
}

/**
 * Zwei Frames warten wie `FormularHuelle` in der Kosten-Seite: laeuft der
 * Reiter ueber die URL, rendert der Router den neuen Inhalt erst im naechsten
 * Zug — vorher gemessen waere die Seitenhoehe die des alten Reiters.
 */
export function spaeterInsBild(ziel: () => HTMLElement | null): () => void {
  let raf2 = 0
  const raf1 = requestAnimationFrame(() => {
    raf2 = requestAnimationFrame(() => insBildRollen(ziel()))
  })
  return () => { cancelAnimationFrame(raf1); cancelAnimationFrame(raf2) }
}

export function reiterInsBild(leiste: HTMLElement | null): () => void {
  return spaeterInsBild(() => leiste)
}
