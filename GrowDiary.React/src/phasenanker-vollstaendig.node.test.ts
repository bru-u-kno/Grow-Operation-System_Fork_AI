import { readdirSync, readFileSync } from 'node:fs'
import { join, relative } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

/**
 * Die Oberfläche rechnet keine Phasenbeginne selbst.
 *
 * <b>Der Anlass (02.10.2026).</b> Der Zeitstrahl schätzte die Vegi aus
 * Keimdatum + 14 Tagen und die Autoflower-Blüte aus 28 Tagen — eine vierte
 * Fassung neben Resolver, Mischplan und Plan-Auswertung im Backend. Die Knöpfe
 * im Grow-Detail und im Messformular entschieden über `flipDate`/`rootedAt`.
 * Jetzt kommen Phase und Beginne aus `phasenanker` (Backend `Phasenanker`).
 *
 * <b>Die Grundmenge</b> sind alle Quelldateien unter `src/` ohne Tests; gezählt
 * wird jeder LESENDE Zugriff (`.feld`) auf ein Rohfeld, aus dem ein
 * Phasenbeginn entsteht — Kommentare ausgenommen. Wer liest, steht unten mit
 * Grund. Die Gegenstücke im Backend: `PhasenankerVollstaendigTests.cs`.
 */
const SRC = fileURLToPath(new URL('.', import.meta.url))
const ROHFELD = /\.(vegStartedAt|rootedAt|germinatedAt|flipDate|finishStartedAt|daysAlreadyInPhase|cloneIsRooted|seedlingDays|autoflowerDaysSinceGermination)\b/g

const AUSNAHMEN = new Map([
  ['pages/GrowSetupPage.tsx', 'Das Formular selbst: lädt und schickt Einstieg, mitgebrachte Tage und Flipdatum.'],
  ['features/grows/grow-plan-model.ts', 'Vorschau beim Anlegen: rechnet aus dem eingegebenen Flipdatum den Plan, bevor es einen Anker gibt.'],
  ['features/grows/GrowPlanPanel.tsx', 'Zeigt das Flipdatum dieser Vorschau (eigener Typ PlanTimeline, kein Grow).'],
])

function ohneKommentare(code: string): string {
  return code.replace(/\/\*[\s\S]*?\*\//g, '').replace(/(^|[^:])\/\/.*$/gm, '$1')
}

function quellen(verzeichnis = SRC): string[] {
  return readdirSync(verzeichnis, { withFileTypes: true }).flatMap((eintrag) => {
    if (eintrag.name === 'node_modules') return []
    const pfad = join(verzeichnis, eintrag.name)
    if (eintrag.isDirectory()) return quellen(pfad)
    if (!/\.(ts|tsx)$/.test(eintrag.name) || /\.test\.tsx?$/.test(eintrag.name)) return []
    return [pfad]
  })
}

function treffer(): Map<string, string[]> {
  const ergebnis = new Map<string, string[]>()
  for (const datei of quellen()) {
    const felder = [...ohneKommentare(readFileSync(datei, 'utf8')).matchAll(ROHFELD)].map((t) => t[1])
    if (felder.length > 0) ergebnis.set(relative(SRC, datei).replaceAll('\\', '/'), [...new Set(felder)])
  }
  return ergebnis
}

describe('Phasenanker — eine Wahrheit für Phase und Woche', () => {
  it('sieht seine Grundmenge', () => {
    expect(quellen().length).toBeGreaterThan(150)
    expect(ohneKommentare('// grow.flipDate\nconst x = grow.flipDate').match(ROHFELD)).toEqual(['.flipDate'])
    expect(ohneKommentare("const url = 'http://x'; a.rootedAt").match(ROHFELD)).toEqual(['.rootedAt'])
  })

  it('niemand liest die Rohfelder an der Funktion vorbei', () => {
    const fremd = [...treffer()].filter(([datei]) => !AUSNAHMEN.has(datei)).map(([datei, felder]) => `${datei}: ${felder.join(', ')}`)
    expect(fremd, `Diese Dateien rechnen Phasen selbst — phasenanker benutzen oder mit Grund eintragen:\n  ${fremd.join('\n  ')}`).toEqual([])
  })

  it('jede Ausnahme ist echt und noch nötig', () => {
    const t = treffer()
    for (const [datei, grund] of AUSNAHMEN) {
      expect(grund.length).toBeGreaterThan(10)
      expect(t.has(datei), `${datei} liest kein Rohfeld mehr (oder gibt es nicht) — Ausnahme streichen.`).toBe(true)
    }
  })
})

/**
 * Wortwahl des Nutzers (02.10.2026): der Schritt heißt „Bewurzelung
 * abgeschlossen", nicht „Bewurzelung bestätigen". Geprüft über Oberfläche UND
 * Backend-Meldungen, Kommentare ausgenommen.
 */
describe('„Bewurzelung abgeschlossen"', () => {
  const ALT = /Bewurzelung\s*(best[aä]tig|bestaetig)|Bewurzelungsbest/i

  function backendDateien(verzeichnis: string): string[] {
    return readdirSync(verzeichnis, { withFileTypes: true }).flatMap((eintrag) => {
      if (['bin', 'obj', 'node_modules'].includes(eintrag.name)) return []
      const pfad = join(verzeichnis, eintrag.name)
      if (eintrag.isDirectory()) return backendDateien(pfad)
      return /\.(cs|json)$/.test(eintrag.name) ? [pfad] : []
    })
  }

  const dateien = [...quellen(), ...backendDateien(fileURLToPath(new URL('../../GrowDiary.Web', import.meta.url)))]

  it('sieht Oberfläche und Backend', () => {
    expect(dateien.filter((d) => d.endsWith('.cs')).length).toBeGreaterThan(300)
    expect(dateien.filter((d) => d.endsWith('.tsx')).length).toBeGreaterThan(50)
    expect(ALT.test('Bewurzelung bestätigen')).toBe(true)
  })

  it('kein sichtbarer Text sagt mehr „Bewurzelung bestätigen"', () => {
    const alt = dateien.filter((d) => ALT.test(ohneKommentare(readFileSync(d, 'utf8'))))
      .map((d) => relative(SRC, d))
    expect(alt, `alte Wortwahl in:\n  ${alt.join('\n  ')}`).toEqual([])
  })
})
