import type { Frame, Page } from '@playwright/test'

/**
 * Überstand und Trefferflächen — gemessen im Browser, an einer Stelle.
 *
 * Bis zum 05.10.2026 standen beide Messungen je einmal in ihrer Prüfung
 * (`handy-zuschnitt.spec.ts`, `touch-targets.spec.ts`). Die Gerätematrix
 * (`geraete/geraetematrix.spec.ts`) misst dasselbe auf zwölf Geräten und im
 * HA-Rahmen — eine zweite, abgetippte Fassung wäre die nächste Stelle, an der
 * zwei Prüfungen Verschiedenes für „kein Überstand" halten.
 *
 * Beide nehmen `Page` oder `Frame`: im HA-Rahmen läuft die App in einem
 * iframe, und gemessen wird in dessen Dokument.
 */
type Ziel = Page | Frame

/** Was rechts hinausragt, ohne dass man es wegwischen könnte (höchstens acht Funde). */
export function ueberstand(ziel: Ziel): Promise<string[]> {
  return ziel.evaluate(() => {
    const breite = document.documentElement.clientWidth

    // In einem Wischbereich zu liegen ist eine Entscheidung, kein Fehler:
    // eine breite Tabelle oder ein Reiterband scrollen absichtlich in sich.
    const imWischbereich = (el: HTMLElement): boolean => {
      for (let n = el.parentElement; n && n !== document.body; n = n.parentElement) {
        const ox = getComputedStyle(n).overflowX
        if ((ox === 'auto' || ox === 'scroll') && n.scrollWidth > n.clientWidth + 1) return true
      }
      return false
    }

    // Screenreader-Text steht bewusst ausserhalb des Sichtfelds (clip-path).
    // Er ist unsichtbar — seine Geometrie zaehlt nicht.
    const nurFuerScreenreader = (el: HTMLElement): boolean => {
      for (let n: HTMLElement | null = el; n && n !== document.body; n = n.parentElement) {
        const cs = getComputedStyle(n)
        if (cs.clipPath !== 'none' && cs.position === 'absolute') return true
      }
      return false
    }

    return [...document.querySelectorAll<HTMLElement>('main *')]
      .filter((el) => {
        const box = el.getBoundingClientRect()
        if (box.width === 0 || box.height === 0) return false
        if (box.right <= breite + 1) return false
        return !imWischbereich(el) && !nurFuerScreenreader(el)
      })
      .slice(0, 8)
      .map((el) => `${el.tagName.toLowerCase()}.${(el.className || '').toString().split(' ')[0]}@${Math.round(el.getBoundingClientRect().right)}`)
  })
}

/** Scrollt die Seite selbst seitwärts? Dann ist der Rahmen kaputt. */
export function scrolltSeitwaerts(ziel: Ziel): Promise<boolean> {
  return ziel.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1)
}

/**
 * Wartet, bis endliche Einblendungen fertig sind.
 *
 * Ein Element, das gerade noch einblendet, ist beim Messen zu klein. Nur auf
 * *endliche* Animationen warten: der Live-Bildschirm hat einen Dauerpuls, und
 * dessen `finished` wird nie erfüllt.
 */
export async function einblendungenAbwarten(ziel: Ziel): Promise<void> {
  await ziel.evaluate(() => Promise.all(
    document.getAnimations()
      .filter((animation) => {
        const timing = (animation.effect as KeyframeEffect | null)?.getComputedTiming()
        return timing != null && Number.isFinite(timing.iterations ?? Infinity) && Number.isFinite(timing.endTime ?? Infinity)
      })
      .map((animation) => animation.finished.catch(() => undefined)),
  ))
}

/**
 * Bedienelemente, deren Trefferfläche kleiner als `min` ist.
 *
 * Gemessen wird die *Trefferfläche*, nicht die Kastengröße: ein 24px-Kreis mit
 * einem ::after, das auf 44px aufpolstert, ist in Ordnung. Deshalb prüft die
 * Messung über elementFromPoint, ob der Rand des Quadrats um die Mitte noch
 * dasselbe Bedienelement trifft.
 */
export function zuKleineZiele(ziel: Ziel, min: number): Promise<string[]> {
  return ziel.evaluate((min) => {
    const out: string[] = []
    const controls = document.querySelectorAll('button, a[href], select, input:not([type=hidden]), [role=button]')
    for (const element of Array.from(controls)) {
      if (!(element instanceof HTMLElement)) continue
      const box = element.getBoundingClientRect()
      if (box.width === 0 || box.height === 0) continue
      // Unsichtbares zählt nicht.
      const style = getComputedStyle(element)
      if (style.visibility === 'hidden' || style.display === 'none' || style.pointerEvents === 'none') continue
      // Absichtlich versteckte Eingaben — etwa das native file-Input hinter
      // einem eigenen Knopf — bedient niemand direkt.
      if (Number(style.opacity) === 0 || style.clipPath.startsWith('inset(50%')) continue
      if (box.width <= 4 || box.height <= 4) continue
      // Eine Checkbox in einem <label> tippt man ueber das Label — das ist der
      // dokumentierte Fehlalarm des Juni-Audits (label.v1-switch = 344x47).
      const wrappingLabel = element.closest('label')
      if (wrappingLabel && wrappingLabel !== element && wrappingLabel.getBoundingClientRect().height >= min - 2) continue

      const centreX = box.left + box.width / 2
      const centreY = box.top + box.height / 2
      // Erreicht die Trefferfläche oben und unten die halbe Mindesthöhe?
      const reaches = (dy: number) => {
        const hit = document.elementFromPoint(centreX, centreY + dy)
        return hit === element || element.contains(hit) || (hit instanceof HTMLElement && hit.contains(element))
      }
      const tall = box.height >= min || (reaches(-min / 2 + 1) && reaches(min / 2 - 1))
      if (!tall) {
        const label = (element.textContent ?? '').trim().slice(0, 24) || element.getAttribute('aria-label') || element.className
        out.push(`${element.tagName.toLowerCase()}.${element.className.split(' ')[0]} "${label}" ${Math.round(box.width)}x${Math.round(box.height)}`)
      }
    }
    return out
  }, min)
}

/**
 * Konsolen- und Netzfehler, die am Testaufbau liegen, nicht an der App:
 * Kamerabilder und HA-Entitäten gibt es ohne Home Assistant nicht.
 * Genutzt von `console-clean.spec.ts` und der Gerätematrix.
 */
export function istAufbauRauschen(text: string): boolean {
  return /\/api\/live\/tents\/\d+\/camera/.test(text)
    || /\/api\/home-assistant\/entities/.test(text)
    || /favicon/.test(text)
}
