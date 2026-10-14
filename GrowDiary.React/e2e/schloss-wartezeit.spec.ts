import { test } from '@playwright/test'
import { gibSchloss, nimmSchloss } from './schloss'

/**
 * Das Schloss darf das Limit des wartenden Falls nicht auffressen.
 *
 * <b>Der Anlass (14.10.2026).</b> 16 E2E-Dateien nehmen dasselbe Schloss in `beforeEach`. Hält ein Fall es lange —
 * der Addback-Fall „Nachmessung" wartet gut eine Minute auf den Hintergrundtakt —, steht jeder andere Fall im
 * `beforeEach` und wartet. Diese Zeit zählt in sein 30-Sekunden-Limit: Er brach mit „Test timeout of 30000ms exceeded
 * while running beforeEach hook" ab, ohne dass irgendetwas an der App falsch war. In der CI wurden so am 14.10. drei
 * von sechs Läufen rot (auch am 10.10.); erst die Wiederholung lief durch, wenn das Schloss inzwischen frei war.
 *
 * <b>Der Nachbau.</b> Zwei Fälle halten das Schloss je 3 Sekunden; das Limit für Hooks ist 2 Sekunden, der Fall setzt sich
 * danach selbst ein großzügiges Limit (im Rumpf, wie der Addback-Fall mit `test.setTimeout(180_000)`). Der Fall, der
 * hinten ansteht, wartet 3 s im `beforeEach` und überschritte dort sein Limit, obwohl er selbst nichts falsch macht. Mit
 * einem einzigen Worker laufen die Fälle nacheinander und es gibt nichts zu warten; dann prüft dieser Fall nichts (die CI
 * fährt mehrere Worker).
 *
 * <b>Warum 2 s Limit und nicht kürzer als das Halten.</b> Eine erste Fassung ließ den Wartenden knapp UNTER seinem Limit
 * warten — da half schon eine Verlängerung nach dem Warten, und der Nachbau war grün, obwohl die CI weiter rot wurde.
 * Das Limit muss schon WÄHREND des Wartens verlängert werden.
 */

const HALTEN_MS = 3_000

test.describe.configure({ mode: 'parallel', timeout: 2_000 })

test.beforeEach(async () => { await nimmSchloss() })
test.afterEach(() => { gibSchloss() })

for (const name of ['erster', 'zweiter']) {
  test(`${name} Fall: die Wartezeit auf das Schloss zählt nicht in sein Limit`, async () => {
    test.setTimeout(test.info().timeout + 10_000)
    await new Promise((weiter) => { setTimeout(weiter, HALTEN_MS) })
  })
}
