/* src/abweichung-standard.node.test.ts
   EINE WAHRHEIT JE ZAHL: die Standard-Abweichung der Lufttemperatur.

   Der Server rechnet bei leerem Feld mit `WochenplanSyncService.LufttemperaturSpanne`;
   das Grenzwerte-Blatt muss ohne Server vorrechnen und führt dieselbe Zahl als
   `STANDARD_ABWEICHUNG_K`. Laufen die beiden auseinander, zeigt das Blatt eine
   andere Grenze, als nach dem Speichern gilt — genau der Fehler, den forkai.154
   behebt. Diese Prüfung liest die C#-Datei selbst, keine abgeschriebene Liste.
*/

import { readFileSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

const WURZEL = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..')
const CS = join(WURZEL, 'GrowDiary.Web', 'Services', 'WochenplanSyncService.cs')
const TS = join(WURZEL, 'GrowDiary.React', 'src', 'features', 'zielwerte', 'tag-nacht.ts')

/**
 * Der Wert aus der EINEN Definitionszeile. Nur Code-Zeilen — ein Kommentar, der
 * den Namen nennt, ist keine Definition. Zwei Treffer oder keiner: null.
 */
function definiert(datei: string, muster: RegExp): number | null {
  const zeilen = readFileSync(datei, 'utf8').split('\n')
    .filter((z) => !z.trim().startsWith('//') && !z.trim().startsWith('*') && !z.trim().startsWith('/*'))
  const treffer = zeilen.map((z) => muster.exec(z)).filter((m): m is RegExpExecArray => m != null)
  return treffer.length === 1 ? Number(treffer[0][1]) : null
}

const serverWert = () => definiert(CS, /\bconst\s+double\s+LufttemperaturSpanne\s*=\s*([0-9.]+)\s*;/)
const blattWert = () => definiert(TS, /\bexport\s+const\s+STANDARD_ABWEICHUNG_K\s*=\s*([0-9.]+)\b/)

describe('Standard-Abweichung der Lufttemperatur', () => {
  it('die Prüfung findet beide Definitionen genau einmal', () => {
    expect(serverWert()).not.toBeNull()
    expect(blattWert()).not.toBeNull()
  })

  it('Blatt und Server rechnen mit derselben Zahl', () => {
    expect(blattWert()).toBe(serverWert())
  })
})
