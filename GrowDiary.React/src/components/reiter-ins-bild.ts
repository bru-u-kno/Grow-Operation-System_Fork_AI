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
 * Zwei Frames warten wie `FormularHuelle` in der Kosten-Seite: laeuft der
 * Reiter ueber die URL, rendert der Router den neuen Inhalt erst im naechsten
 * Zug — vorher gemessen waere die Seitenhoehe die des alten Reiters.
 */
export function reiterInsBild(leiste: HTMLElement | null): () => void {
  let raf2 = 0
  const raf1 = requestAnimationFrame(() => {
    raf2 = requestAnimationFrame(() => {
      if (!leiste?.isConnected) return
      auslaufZuruecksetzen()
      const rand = parseFloat(getComputedStyle(leiste).scrollMarginTop) || 0
      const ziel = Math.max(0, leiste.getBoundingClientRect().top + window.scrollY - rand)
      const auslauf = auslaufBerechnen(ziel, document.documentElement.scrollHeight, window.innerHeight)
      if (auslauf > 0) document.documentElement.style.setProperty(VAR, `${auslauf}px`)
      window.scrollTo({ top: ziel, behavior: 'smooth' })
    })
  })
  return () => { cancelAnimationFrame(raf1); cancelAnimationFrame(raf2) }
}
