import { expect, type Frame, type Page } from '@playwright/test'

/**
 * Die App so, wie Home Assistant sie zeigt: in einem iframe unter der
 * HA-Kopfleiste (schmal) bzw. neben der Seitenleiste (breit).
 *
 * Nachgestellt nach dem HA-Frontend 20260826.7 (`hassio-ingress-view`,
 * `ha-panel-app`): unter 870 px Breite gilt HA als „narrow" — dann steht über
 * der App die 56 px hohe Kopfleiste mit dem Menüknopf, und die App bekommt die
 * volle Breite. Darüber liegt die Seitenleiste ausgeklappt links (256 px) und
 * die App füllt den Rest bis oben. Die App fragt per postMessage nach Route und
 * „narrow" (`home-assistant/subscribe-properties`) und bekommt beides — wie in
 * `ha-tiefenlink.spec.ts`.
 *
 * Was hier NICHT nachgestellt ist: das Scrollverhalten der WebView in der
 * HA-App (F-049) — das kann nur ein echtes Gerät zeigen (Punkt 3 des Plans).
 */
const SLUG = 'd48160c2_grow_os_fork_ai'
export const HA_SCHMAL_BIS = 870
const KOPFLEISTE = 56
const SEITENLEISTE = 256

export async function imHaRahmen(page: Page, pfad: string): Promise<Frame> {
  // Gleiche Herkunft wie die App — Ingress liefert HA und App unter einer Adresse.
  await page.goto('/icons/gibt-es-nicht.txt').catch(() => undefined)
  await page.evaluate(
    ({ pfad, slug, schmalBis, kopf, seite }) => {
      const schmal = window.innerWidth < schmalBis
      document.documentElement.innerHTML = `
        <body style="margin:0;background:#111;font-family:sans-serif;overflow:hidden">
          ${schmal
            ? `<div id="ha-kopf" style="height:${kopf}px;background:#03a9f4;color:#fff;display:flex;align-items:center;padding:0 16px">Grow OS Fork AI</div>`
            : `<div id="ha-seite" style="position:fixed;left:0;top:0;bottom:0;width:${seite}px;background:#1c1c1c"></div>`}
          <iframe id="f" title="Grow OS" src="${pfad}"
            style="position:fixed;border:0;${schmal
              ? `left:0;right:0;top:${kopf}px;bottom:0;width:100%;height:calc(100% - ${kopf}px)`
              : `left:${seite}px;top:0;width:calc(100% - ${seite}px);height:100%`}"></iframe>
        </body>`
      const f = document.getElementById('f') as HTMLIFrameElement
      const w = window as unknown as Record<string, unknown>
      w.__haPfad = `/app/${slug}${pfad === '/' ? '' : pfad}`
      const schicke = () => {
        const voll = w.__haPfad as string
        const praefix = `/app/${slug}`
        f.contentWindow?.postMessage(
          { type: 'home-assistant/properties', narrow: schmal, route: { prefix: praefix, path: voll.slice(praefix.length) } },
          '*',
        )
      }
      window.addEventListener('message', (e) => {
        if (e.source !== f.contentWindow) return
        if (e.data?.type === 'home-assistant/subscribe-properties') schicke()
        if (e.data?.type === 'home-assistant/navigate') {
          w.__haPfad = e.data.path
          schicke()
        }
      })
    },
    { pfad, slug: SLUG, schmalBis: HA_SCHMAL_BIS, kopf: KOPFLEISTE, seite: SEITENLEISTE },
  )
  await expect.poll(() => page.frames().length).toBeGreaterThan(1)
  const frame = page.frames().find((f) => f !== page.mainFrame())!
  await frame.waitForLoadState('domcontentloaded')
  return frame
}
