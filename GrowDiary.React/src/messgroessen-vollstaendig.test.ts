import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
import { definitions } from './features/home-assistant/messgroessen'
import { KNOWN_METRICS } from './features/live/dashboard-layout'
import { VERLAUFS_METRIKEN } from './features/live/useTentSparklines'
import { KURVEN_FARBEN } from './features/live/verlauf-modell'

/**
 * Jede Messgröße des Backends ist zuordenbar und hat im Verlauf Platz, Namen und Farbe.
 *
 * <b>Der Anlass (05.10.2026).</b> Für die Außenwerte des AC-Infinity-Controllers
 * kamen drei Messgrößen dazu. Eine neue Messgröße muss an rund zehn Stellen
 * stehen — Aufzählung im Backend, Typ und Zuordnungsformular hier, Liste,
 * Farbe und Name des Verlaufs. Fehlt eine, merkt es niemand: Die Verlaufs-Liste
 * hatte 11 Einträge, der Endpunkt nahm höchstens 12 und schnitt den Rest
 * <i>still</i> ab — mit 14 wären die beiden letzten Wertekarten einfach nicht
 * erschienen.
 *
 * <b>Warum eine Zählung.</b> Grundmenge ist die Aufzählung im Backend
 * (`Enums.cs`), nicht eine Liste hier. Was dort steht, braucht eine Zeile im
 * Formular oder einen ausgeschriebenen Grund.
 */

const WEB = new URL('../../GrowDiary.Web/', import.meta.url)

const ohneKommentare = (code: string) => code.replace(/\/\*[\s\S]*?\*\//g, '').replace(/\/\/.*$/gm, '')

/** Die Werte der Aufzählung `SensorMetricType` im Backend — ohne XML-Doku und Kommentare. */
function backendMessgroessen(): string[] {
  const code = ohneKommentare(readFileSync(new URL('Models/Enums.cs', WEB), 'utf8'))
  const block = code.match(/public enum SensorMetricType\s*\{([^}]*)\}/)
  if (!block) throw new Error('SensorMetricType nicht in Enums.cs gefunden — die Zählung sähe nichts.')
  return block[1].split(',').map((teil) => teil.trim()).filter(Boolean)
}

/** Die Werte der Union `SensorMetricType` in `types/automation.ts`. */
function typMessgroessen(): string[] {
  const code = readFileSync(new URL('./types/automation.ts', import.meta.url), 'utf8')
  const treffer = code.match(/export type SensorMetricType =([^\n]*(?:\n\s*\|[^\n]*)*)/)
  if (!treffer) throw new Error('SensorMetricType nicht in types/automation.ts gefunden.')
  return [...treffer[1].matchAll(/'([^']+)'/g)].map((m) => m[1])
}

/**
 * Messgrößen ohne Zeile im Zuordnungsformular — je mit Grund.
 *
 * Ein Eintrag ohne Grund ist keine Ausnahme, sondern eine Lücke mit Deckel.
 * Diese drei standen schon vor der Zählung so da; sie sind ein offener Befund,
 * kein Vorbild.
 */
const OHNE_FORMULARZEILE: Record<string, string> = {
  UpsBattery: 'Nur über die API zuordenbar (PUT /api/settings/tents/{id}); im Formular steht bisher nur der USV-Status. Offener Befund vom 05.10.2026.',
  PumpCirculationPower: 'Nur über die API zuordenbar; der Pumpenwächter liest den Wert, das Formular kennt ihn nicht. Offener Befund vom 05.10.2026.',
  PumpAirPower: 'Wie PumpCirculationPower — Leistung der Luftpumpe, nur über die API. Offener Befund vom 05.10.2026.',
}

/** Backend-Werte, die der Typ der Oberfläche nicht kennt — je mit Grund. */
const OHNE_TYP: Record<string, string> = {
  PumpCirculationPower: 'Die Oberfläche zeigt den Wert nirgends; siehe OHNE_FORMULARZEILE.',
  PumpAirPower: 'Wie PumpCirculationPower.',
}

describe('Messgrößen: Backend, Typ und Zuordnungsformular', () => {
  const backend = backendMessgroessen()

  it('sieht ihre Grundmenge', () => {
    // Sonst liefe jede Prüfung darunter null Mal und wäre grün.
    // Untergrenzen mit Luft: sie prüfen „sieht etwas", nicht „genau so viele".
    expect(backend.length).toBeGreaterThanOrEqual(15)
    expect(backend).toContain('OutsideTemperature')
    expect(typMessgroessen().length).toBeGreaterThanOrEqual(15)
    expect(definitions.length).toBeGreaterThanOrEqual(15)
  })

  it('jeder Backend-Wert steht im Typ der Oberfläche', () => {
    const typ = new Set(typMessgroessen())
    const fehlend = backend.filter((wert) => !typ.has(wert) && !(wert in OHNE_TYP))
    expect(fehlend, `Im Backend, aber nicht in types/automation.ts: ${fehlend.join(', ')}`).toEqual([])
  })

  it('jeder Backend-Wert hat eine Zeile im Zuordnungsformular oder einen Grund', () => {
    const imFormular = new Set(definitions.map((d) => d.metricType))
    const fehlend = backend.filter((wert) => !imFormular.has(wert as never) && !(wert in OHNE_FORMULARZEILE))
    expect(fehlend, `Ohne Zeile in messgroessen.ts und ohne Grund: ${fehlend.join(', ')}`).toEqual([])
  })

  it('die Ausnahmen sind echt und begründet', () => {
    // Eine Ausnahme für einen Wert, den es nicht gibt, greift nie — und eine,
    // die längst im Formular steht, verdeckt nur noch.
    const imFormular = new Set<string>(definitions.map((d) => d.metricType))
    for (const [wert, grund] of [...Object.entries(OHNE_FORMULARZEILE), ...Object.entries(OHNE_TYP)]) {
      expect(backend, `Ausnahme für „${wert}", den es im Backend nicht gibt`).toContain(wert)
      expect(grund.length, `Ausnahme für „${wert}" ohne Grund`).toBeGreaterThan(20)
    }
    for (const wert of Object.keys(OHNE_FORMULARZEILE)) {
      expect(imFormular.has(wert), `„${wert}" steht im Formular — die Ausnahme ist überholt`).toBe(false)
    }
  })

  it('jede Formularzeile meint einen Backend-Wert', () => {
    const fremd = definitions.map((d) => d.metricType).filter((wert) => !backend.includes(wert))
    expect(fremd, `Zeilen ohne Gegenstück in Enums.cs: ${fremd.join(', ')}`).toEqual([])
  })
})

describe('Verlauf: Platz, Name und Farbe je Messgröße', () => {
  it('die Liste passt in die Obergrenze des Verlaufs-Endpunkts', () => {
    const code = ohneKommentare(readFileSync(new URL('Api/Controllers/SensorHistoryApiController.cs', WEB), 'utf8'))
    const grenze = code.match(/const int MaxMetrics\s*=\s*(\d+)\s*;/)
    if (!grenze) throw new Error('MaxMetrics nicht im SensorHistoryApiController gefunden.')
    expect(VERLAUFS_METRIKEN.length).toBeGreaterThanOrEqual(10)
    expect(
      VERLAUFS_METRIKEN.length,
      `Die Live-Seite fragt ${VERLAUFS_METRIKEN.length} Kurven in einem Abruf an, der Endpunkt liefert höchstens ${grenze[1]} — der Rest fiele still weg.`,
    ).toBeLessThanOrEqual(Number(grenze[1]))
  })

  it('jede Kurve hat eine eigene Farbe aus der Tabelle, nicht aus der Reserve', () => {
    const ohne = VERLAUFS_METRIKEN.filter((key) => !(key in KURVEN_FARBEN))
    expect(ohne, `Ohne eigene Farbe: ${ohne.join(', ')}`).toEqual([])
  })

  it('jede Kurve hat einen Namen — in der Oberfläche und im Backend', () => {
    // Ohne Namen schriebe die Wertekarte den Schlüssel („outside-vpd") hin.
    const bekannt = new Set(KNOWN_METRICS.map((m) => m.key))
    const code = ohneKommentare(readFileSync(new URL('Services/AlertEvaluationService.cs', WEB), 'utf8'))
    const ohneOberflaeche = VERLAUFS_METRIKEN.filter((key) => !bekannt.has(key))
    const ohneBackend = VERLAUFS_METRIKEN.filter((key) => !code.includes(`"${key}" => (`))
    expect(ohneOberflaeche, `Nicht in KNOWN_METRICS: ${ohneOberflaeche.join(', ')}`).toEqual([])
    expect(ohneBackend, `Nicht in AlertEvaluationService.MetricDisplay: ${ohneBackend.join(', ')}`).toEqual([])
  })

  it('Außenwerte heißen in Oberfläche und Backend gleich', () => {
    // Eine Wahrheit je Name: die Wertekarte nimmt den Namen der Live-Kachel
    // (Backend), fällt sonst auf KNOWN_METRICS zurück — beide müssen stimmen.
    const code = ohneKommentare(readFileSync(new URL('Services/AlertEvaluationService.cs', WEB), 'utf8'))
    const aussen = KNOWN_METRICS.filter((m) => m.key.startsWith('outside-'))
    expect(aussen).toHaveLength(3)
    for (const { key, label } of aussen) {
      expect(code, `${key}: Backend nennt es anders als „${label}"`).toContain(`"${key}" => ("${label}",`)
    }
  })
})
