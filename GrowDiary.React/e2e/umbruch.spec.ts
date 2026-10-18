import { test, expect } from '@playwright/test'
import { ALLE_SEITEN } from './seiten'
import { einblendungenAbwarten } from './oberflaeche-messung'

/**
 * Umbrüche, die `worttrennung` nicht sieht — auf allen Menüseiten, bei 360 px (normal und mit doppelter Systemschrift)
 * und bei 768 px.
 *
 * <b>Warum zusätzlich.</b> `worttrennung` findet nur Wörter, die MITTEN durchbrechen, nur bei 390 und 1440 px und nur
 * mit normaler Schrift. Gerätematrix und Randprüfung messen Kästen und Ränder, nicht, wie ein Text umbricht.
 * Die Erkundung vom 18.10.2026 fand damit „~75 / min", „100 / %", „208 / L" und eine untere Leiste mit „Tagebu…".
 *
 * Gesucht wird (je Textblatt, nicht je Kasten):
 *  - zahl-einheit: eine Zahl steht am Zeilenende, ihre Einheit (ml, °C, %, min …) am Anfang der nächsten
 *  - waise:        letzte Zeile besteht aus einem Wort von höchstens 3 Zeichen
 *  - abgeschnitten: Inhalt breiter als der Kasten bei overflow hidden/clip oder Auslassungspunkten
 *  - knopf-umbruch: Knopf, Link, Chip oder Tabellenkopf mit kurzem Text bricht auf mehrere Zeilen
 *  - schmal:       Fließtext mit durchschnittlich unter 9 Zeichen je Zeile bei mindestens 3 Zeilen
 *
 * <b>Wie sie läuft.</b> Nur mit `UMBRUCH=1` (eigenes Projekt `umbruch`), gegen eine laufende Demo-App:
 * `GROW_OS_URL=http://127.0.0.1:5191 UMBRUCH=1 npx playwright test e2e/umbruch.spec.ts`. Sie ist rot, solange es
 * Funde gibt, und gehört deshalb noch nicht in das Tor — wie die Gerätematrix. Zum Wiedererkennen: Wer den Fehler
 * „geschütztes Leerzeichen → normales" einbaut (z. B. in `phase-timeline.ts`), sieht sie auf `/grows/1` rot werden.
 *
 * <b>Nicht gemeldet</b> wird die Höhe von Kennzahlen mit overflow hidden (`strong` in `/kosten`, `/hydro`): die
 * Erkundung hielt sie für abgeschnitten, auf dem Bild standen sie vollständig da.
 */

type Fund = { art: string, seite: string, text: string, tag: string, klasse: string, kasten: number }

const FASSUNGEN = [
  { name: '360', breite: 360, hoehe: 800, schrift: 100 },
  { name: '360-gross', breite: 360, hoehe: 800, schrift: 200 },
  { name: '768', breite: 768, hoehe: 1024, schrift: 100 },
]

test.setTimeout(900_000)

for (const f of FASSUNGEN) {
  test(`Umbruchprüfung ${f.name}`, async ({ browser }) => {
    const kontext = await browser.newContext({ viewport: { width: f.breite, height: f.hoehe } })
    const page = await kontext.newPage()
    // Große Schrift gilt auf einem echten Gerät von Anfang an — Rechnungen beim Aufbau (z. B. Diagramm-Achsen) sehen sie dann.
    if (f.schrift !== 100) {
      await page.addInitScript((p) => {
        const setzen = () => {
          const st = document.createElement('style')
          st.textContent = `html{font-size:${p}% !important}`
          document.documentElement.appendChild(st)
        }
        // Das Skript läuft vor dem Dokument: erst setzen, wenn es ein <html> gibt.
        if (document.documentElement) setzen()
        else new MutationObserver((_, o) => { if (document.documentElement) { o.disconnect(); setzen() } }).observe(document, { childList: true })
      }, f.schrift)
    }
    const funde: Fund[] = []
    let geprueft = 0
    for (const pfad of ALLE_SEITEN) {
      try {
        await page.goto(pfad, { waitUntil: 'networkidle' })
        await einblendungenAbwarten(page)
        await page.waitForTimeout(150)
        const roh = await page.evaluate(() => {
          const raus: Array<{ art: string, text: string, tag: string, klasse: string, kasten: number }> = []
          const EINHEIT = /^(ml|l|L|°C|°|%|min|h|d|W|kW|kWh|Wh|ppm|mS\/cm|µS\/cm|mS|cm|mm|m|g|kg|mg|µmol|PPFD|kPa|EC|pH|Tage|Std|s|x|×|€|Euro|L\/h|l\/h|ml\/L|ml\/l)\.?[,;:]?$/
          const ZAHL = /^[−\-+~≈]?\d[\d.,:]*$/
          const kurz = (s: string) => s.replace(/\s+/g, ' ').trim().slice(0, 90)
          const sichtbar = (el: Element) => {
            const r = (el as HTMLElement).getBoundingClientRect()
            if (r.width <= 1 || r.height <= 1) return false
            const cs = getComputedStyle(el)
            return cs.visibility !== 'hidden' && cs.display !== 'none' && cs.opacity !== '0'
          }
          const woerter = (knoten: Text) => {
            const t = knoten.textContent || ''
            const liste: Array<{ w: string, top: number, links: number }> = []
            const re = /\S+/g
            let m: RegExpExecArray | null
            while ((m = re.exec(t))) {
              const r = document.createRange()
              r.setStart(knoten, m.index)
              r.setEnd(knoten, m.index + m[0].length)
              const rs = r.getClientRects()
              if (!rs.length) continue
              liste.push({ w: m[0], top: Math.round(rs[0].top), links: Math.round(rs[0].left) })
            }
            return liste
          }
          const BLOCK = 'main *, header *, nav *'
          const gesehen = new Set<Element>()
          for (const el of Array.from(document.querySelectorAll<HTMLElement>(BLOCK))) {
            if (gesehen.has(el) || !sichtbar(el)) continue
            const tag = el.tagName.toLowerCase()
            if (['script', 'style', 'svg', 'path', 'code', 'pre', 'option', 'select', 'textarea'].includes(tag)) continue
            // nur Blätter mit eigenem Text
            const textKnoten = Array.from(el.childNodes).filter((n): n is Text => n.nodeType === Node.TEXT_NODE && !!(n.textContent || '').trim())
            if (!textKnoten.length) continue
            if (el.closest('svg')) continue
            gesehen.add(el)
            const cs = getComputedStyle(el)
            const kasten = Math.round(el.getBoundingClientRect().width)
            const klasse = (el.getAttribute('class') || '').slice(0, 60)
            const volltext = kurz(el.textContent || '')

            // C: abgeschnitten
            const klemmt = /(hidden|clip)/.test(cs.overflowX) || cs.textOverflow === 'ellipsis'
            if (klemmt && el.scrollWidth > el.clientWidth + 1 && el.clientWidth > 0)
              raus.push({ art: 'abgeschnitten', text: volltext, tag, klasse, kasten })

            // Wörter und Zeilen
            const ws = textKnoten.flatMap(woerter)
            if (ws.length < 2) continue
            const zeilen = new Map<number, string[]>()
            // Zeilen: Wörter mit nahezu gleicher Oberkante (Toleranz 4 px wegen Hoch-/Tiefstellung)
            const tops: number[] = []
            for (const w of ws) {
              let z = tops.find((t) => Math.abs(t - w.top) <= 4)
              if (z === undefined) { z = w.top; tops.push(z) }
              zeilen.set(z, [...(zeilen.get(z) || []), w.w])
            }
            const reihe = [...zeilen.entries()].sort((a, b) => a[0] - b[0]).map((e) => e[1])

            // A: Zahl | Einheit über Zeilengrenze
            for (let i = 0; i < reihe.length - 1; i++) {
              const letzte = reihe[i].at(-1) || ''
              const erste = reihe[i + 1][0] || ''
              if (ZAHL.test(letzte) && EINHEIT.test(erste))
                raus.push({ art: 'zahl-einheit', text: `${letzte} | ${erste}   (${volltext})`, tag, klasse, kasten })
            }
            // B: Waise
            if (reihe.length >= 2) {
              const l = reihe.at(-1)!
              if (l.length === 1 && l[0].length <= 3 && !/^[\d.,]+$/.test(l[0]) === true)
                raus.push({ art: 'waise', text: `„${l[0]}"   (${volltext})`, tag, klasse, kasten })
            }
            // D: Knopf/Link/Chip/Kopf mit kurzem Text bricht um
            const istSteuerung = tag === 'button' || tag === 'th' || tag === 'label' || (tag === 'a' && !!el.closest('nav, header')) || /chip|badge|pill|tag|tab/i.test(klasse)
            if (istSteuerung && reihe.length >= 2 && volltext.length <= 24)
              raus.push({ art: 'knopf-umbruch', text: `${reihe.length} Zeilen: ${volltext}`, tag, klasse, kasten })
            // E: Fließtext in zu schmaler Spalte
            if (reihe.length >= 3) {
              const zeichen = reihe.reduce((s, z) => s + z.join(' ').length, 0) / reihe.length
              if (zeichen < 9 && ws.length >= 4) raus.push({ art: 'schmal', text: `Ø ${zeichen.toFixed(1)} Zeichen/Zeile: ${volltext}`, tag, klasse, kasten })
            }
          }
          return { raus, geprueft: gesehen.size }
        })
        geprueft += roh.geprueft
        for (const r of roh.raus) funde.push({ ...r, seite: pfad })
      } catch (e) {
        funde.push({ art: 'seitenfehler', seite: pfad, text: String(e).slice(0, 160), tag: '', klasse: '', kasten: 0 })
      }
    }
    await kontext.close()
    // Mengenwächter: eine Prüfung, die kaum Text sieht, meldet grün und hat nichts gemessen.
    expect(geprueft, `Die Umbruchprüfung sah nur ${geprueft} Textblöcke`).toBeGreaterThan(500)
    const einzeln = [...new Set(funde.map((x) => `${x.art} · ${x.seite} · ${x.tag}.${x.klasse.split(' ')[0]} (${x.kasten}px) · ${x.text}`))]
    expect(einzeln, `Umbrüche bei ${f.name}`).toEqual([])
  })
}
