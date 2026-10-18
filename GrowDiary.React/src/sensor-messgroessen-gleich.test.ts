import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
import { definitions, kriteriumFuerMessgroesse } from './features/home-assistant/messgroessen'

/**
 * Fork AI (forkai.247): Etikett und Domains einer Messgröße — eine Wahrheit, zwei Sprachen.
 *
 * Der Fork-MCP ordnet Messgrößen über `PUT /api/settings/tents/{id}/sensors/{metricType}` zu. Das Backend braucht dafür
 * dieselben Etiketten (der Assistent sagt „Außen Temp.") und eine Domainregel. Beides steht in
 * `GrowDiary.Web/Services/SensorMessgroessen.cs`; hier wird es gegen die Oberfläche gehalten, statt es zweimal zu pflegen:
 *
 * - jede Zeile des Zuordnungsformulars hat im Backend einen Eintrag mit GLEICHEM Etikett,
 * - jede Domain, die die Oberfläche vorschlägt, nimmt das Backend an (Teilmenge; das Backend darf mehr — „Licht" liest auch
 *   Zahlen, siehe dort).
 */

const ohneKommentare = (code: string) => code.replace(/\/\*[\s\S]*?\*\//g, '').replace(/\/\/.*$/gm, '')

/** Messgröße → { Etikett, Domains } aus der Tabelle im Backend — ohne Kommentare. */
function backendTabelle(): Map<string, { etikett: string; domains: string[] }> {
  const code = ohneKommentare(readFileSync(new URL('../../GrowDiary.Web/Services/SensorMessgroessen.cs', import.meta.url), 'utf8'))
  const tabelle = new Map<string, { etikett: string; domains: string[] }>()
  for (const treffer of code.matchAll(/\[SensorMetricType\.(\w+)\]\s*=\s*new\("([^"]+)",\s*\[([^\]]*)\]\)/g)) {
    const domains = [...treffer[3].matchAll(/"([^"]+)"/g)].map((d) => d[1])
    tabelle.set(treffer[1], { etikett: treffer[2], domains })
  }
  return tabelle
}

describe('Messgrößen: Etikett und Domains in Backend und Oberfläche', () => {
  const backend = backendTabelle()

  it('sieht ihre Grundmenge', () => {
    // Sonst liefen die Vergleiche unten null Mal und wären grün.
    expect(backend.size).toBeGreaterThanOrEqual(20)
    expect(backend.get('OutsideTemperature')).toEqual({ etikett: 'Außen Temp.', domains: ['sensor'] })
    expect(definitions.length).toBeGreaterThanOrEqual(15)
  })

  it('jede Formularzeile hat im Backend dasselbe Etikett', () => {
    const abweichend = definitions
      .filter((d) => backend.get(d.metricType)?.etikett !== d.label)
      .map((d) => `${d.metricType}: Oberfläche „${d.label}", Backend „${backend.get(d.metricType)?.etikett ?? '—'}"`)
    expect(abweichend, abweichend.join('\n')).toEqual([])
  })

  it('jede vorgeschlagene Domain nimmt das Backend an', () => {
    const fehlend = definitions.flatMap((d) => {
      const erlaubt = backend.get(d.metricType)?.domains ?? []
      return kriteriumFuerMessgroesse(d).domains.filter((domain) => !erlaubt.includes(domain)).map((domain) => `${d.metricType}: ${domain}`)
    })
    expect(fehlend, `Die Oberfläche schlägt vor, das Backend lehnt ab: ${fehlend.join(', ')}`).toEqual([])
  })
})
