import { execFileSync } from 'node:child_process'
import { rmSync, writeFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'

/**
 * Das Repo ist öffentlich — in Dateien steht nichts Persönliches.
 *
 * <b>Der Anlass (13.10.2026).</b> Der Fork soll von weiteren Personen genutzt
 * werden. Eine Durchsicht fand den Vornamen des Entwicklers in rund 250
 * Kommentaren und Testnamen, seine Handy-Entität, seine Domain und eine Adresse
 * aus seinem Heimnetz. Nichts davon war ein Geheimnis, aber nichts davon gehört
 * in ein fremdes Repo. Dieser Test verhindert, dass es zurückkommt.
 *
 * Gesucht wird in allen versionierten Dateien (`git grep`), nicht in einer Liste
 * von Ordnern, die jemand pflegen müsste. Mit `--untracked` auch in neuen, noch nicht
 * vorgemerkten Dateien: Am 17.10.2026 (forkai.240) kam ein Vorname aus übernommenem
 * Archivtext durch das Commit-Tor, weil das Tor vor `git add` läuft und `git grep`
 * ohne diesen Schalter nur versionierte Dateien sieht — erst die CI fand ihn.
 * Ignorierte Dateien (`node_modules`, Bauordner) bleiben außen vor. Der GitHub-Name im Pfad der Add-on-
 * Adresse (`bru-u-kno`) ist Kleinschreibung mit Bindestrichen und trifft nicht.
 * Was als Beispiel gebraucht wird, nimmt Beispielwerte: `mobile_app_pixel_7`,
 * `smarthome.example.org`, `192.168.1.x`, „der Nutzer".
 */
// Die Muster sind zusammengesetzt: Sie sollen die verbotenen Wörter finden, nicht
// selbst in Klartext im Repo stehen (sonst träfe der Test sich selbst).
const VORNAME = ['B', 'ru'].join('')
const DOMAIN = ['k9', 'd'].join('') + '\\.world'
const VERBOTEN: Array<{ muster: string; grund: string }> = [
  { muster: `\\b${VORNAME}(s|no)?\\b`, grund: 'Vorname — „der Nutzer" schreiben' },
  { muster: `mobile_app_${VORNAME.toLowerCase()}no`, grund: 'Handy-Entität einer Person — mobile_app_pixel_7 nehmen' },
  { muster: DOMAIN, grund: 'private Domain — example.org nehmen' },
  { muster: '192\\.168\\.178\\.', grund: 'Adresse aus einem Heimnetz — 192.168.1.x nehmen' },
  { muster: ['Claude', 'Memory'].join('') + '|/home/' + 'claude/', grund: 'Pfade der Entwickler-VM' },
]

function suche(muster: string): string[] {
  try {
    const ausgabe = execFileSync(
      'git',
      ['grep', '--untracked', '-I', '-n', '-E', muster, '--', '.', ':!*package-lock.json', ':!GrowDiary.React/src/datenschutz.node.test.ts'],
      { cwd: new URL('../..', import.meta.url), encoding: 'utf8' },
    )
    return ausgabe.split('\n').filter(Boolean)
  } catch (fehler) {
    // `git grep` endet mit Code 1, wenn nichts gefunden wurde.
    if ((fehler as { status?: number }).status === 1) return []
    throw fehler
  }
}

const gitDa = (() => {
  try { execFileSync('git', ['rev-parse', '--git-dir'], { cwd: new URL('../..', import.meta.url), stdio: 'ignore' }); return true } catch { return false }
})()

describe.skipIf(!gitDa)('Datenschutz im öffentlichen Repo', () => {
  it('sieht auch eine neue, noch nicht versionierte Datei (Selbsttest)', () => {
    // Beweist, dass die Suche ihre Grundmenge wirklich einschließt: ohne `--untracked`
    // bliebe diese Datei unsichtbar und der Test wäre rot.
    const datei = new URL(`../../datenschutz-selbsttest-${process.pid}.tmp`, import.meta.url)
    writeFileSync(datei, `${VORNAME} wohnt unter 192.168.178.1\n`)
    try {
      const treffer = suche(`\\b${VORNAME}\\b`).filter((zeile) => zeile.includes('datenschutz-selbsttest-'))
      expect(treffer.length).toBeGreaterThanOrEqual(1)
    } finally {
      rmSync(datei, { force: true })
    }
  })

  for (const { muster, grund } of VERBOTEN) {
    it(`kein Treffer für ${muster} (${grund})`, () => {
      expect(suche(muster)).toEqual([])
    })
  }
})
