import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
import { KETTEN_AKTIONEN } from './ketten-aktionen'

/**
 * Jeder Ketten-Schlüssel aus dem Backend hat auf der Oberfläche eine Aktion.
 *
 * **Warum als Zählung.** Die Knöpfe an den gerissenen Kettengliedern hängen an
 * `KETTEN_AKTIONEN`. Kommt im Backend ein neuer Schlüssel dazu (neues Glied,
 * neuer Lücken-Grund), stünde das Glied sonst wieder ohne Knopf da — still,
 * denn nichts würde rot. Die Grundmenge ist `KettenSchluessel.cs`; gelesen
 * wird die Datei selbst, keine abgetippte Liste.
 */
describe('Ketten-Aktionen', () => {
  const quelle = readFileSync(
    new URL('../../../../GrowDiary.Web/Services/KettenSchluessel.cs', import.meta.url), 'utf8')

  const schluessel = [...quelle.matchAll(/public const string \w+ = "([^"]+)";/g)]
    .map((treffer) => treffer[1])

  it('findet die Grundmenge überhaupt', () => {
    // Sonst läuft die Schleife unten null Mal durch und ist grün, ohne etwas
    // geprüft zu haben — der Mengenwächter aus CLAUDE.md.
    expect(schluessel.length).toBeGreaterThanOrEqual(8)
    expect(schluessel).toContain('absenkung')
    expect(schluessel).toContain('plan-untergrenze-zu-hoch')
  })

  it('jeder Backend-Schlüssel hat eine Aktion', () => {
    const ohne = schluessel.filter((wert) => !(wert in KETTEN_AKTIONEN))
    expect(ohne, `Ohne Aktion in ketten-aktionen.ts: ${ohne.join(', ')} — `
      + 'das Glied stünde wieder ohne Knopf da.').toEqual([])
  })

  it('keine Aktion zeigt auf einen erfundenen Schlüssel', () => {
    // Die Gegenrichtung: eine Aktion für einen Schlüssel, den das Backend nie
    // vergibt, ist toter Code — und sieht in einer Prüfung nach Abdeckung aus.
    const erfunden = Object.keys(KETTEN_AKTIONEN).filter((wert) => !schluessel.includes(wert))
    expect(erfunden, `Diese Aktionen haben keinen Backend-Schlüssel: ${erfunden.join(', ')}`)
      .toEqual([])
  })

  it('jede Weg-Aktion zeigt auf eine Route, die es gibt', () => {
    const app = readFileSync(new URL('../../App.tsx', import.meta.url), 'utf8')
    const routen = [...app.matchAll(/<Route path="([^"]+)"/g)].map((treffer) => treffer[1])
    expect(routen.length).toBeGreaterThan(10)

    for (const aktion of Object.values(KETTEN_AKTIONEN)) {
      if (aktion.art !== 'weg') continue
      /* Fork AI (forkai.67): Der Reiter steht als ?tab= in der Adresse, die
         Route selbst kennt ihn nicht — verglichen wird deshalb der Pfadteil.
         Ohne das faellt jedes Ziel durch, das auf einen Reiter einer
         Sammelseite zeigt, obwohl die Route existiert. */
      const pfad = aktion.ziel.replace('{growId}', ':growId').split('?')[0]
      const bekannt = routen.some((r) => r === pfad || r === aktion.ziel.split('?')[0]
        || (pfad.includes(':') && r.split('/').length === pfad.split('/').length
            && r.split('/').every((teil, i) => teil.startsWith(':') || pfad.split('/')[i].startsWith(':') || teil === pfad.split('/')[i])))
      expect(bekannt, `${aktion.ziel} steht in keiner Route von App.tsx`).toBe(true)
    }
  })
})
