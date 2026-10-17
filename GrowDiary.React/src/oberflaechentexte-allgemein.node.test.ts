import { existsSync, readFileSync, readdirSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

/**
 * Was ein Fremder auf dem Schirm liest, nennt keine Geräte und Namen eines
 * einzelnen Haushalts.
 *
 * <b>Der Anlass (A-017, Neuling-Test vom 17.10.2026).</b> Der Fork wurde an einer
 * leeren, fremden Anlage durchgespielt. Dort hießen die Reiter des Assistenten
 * „Licht · LED Top", „Zuluft · Keller", „Water Chiller", eine Rolle „Abluft T6 ·
 * Stufe" und „Dosier-Steckdose", und der Platzhalter eines Eingabefelds nannte
 * einen Dienst, den es nur bei einem Hersteller gibt. Für den Entwickler stimmt
 * das alles — für jeden anderen sind es Rätsel.
 *
 * <b>Was geprüft wird.</b> Die Zeichenketten (nicht die Kommentare) der Dateien, die
 * Beschriftungen, Hinweise und Platzhalter der Steuerung und des Zelt-Formulars
 * liefern. Technische Schlüssel bleiben unberührt (Rollenkennungen, Entitäten wie
 * `co2_t6_stufe_normal`): `\bT6\b` trifft nur das freistehende Wort, nicht
 * `T6StufeNormal` oder `t6_`.
 *
 * <b>Hardware als Beispiel ist erlaubt</b> („z. B. AC Infinity"), nicht als Vorgabe.
 * Die Seite „Geräte stellen" (AcTestPage) ist ausdrücklich für einen Hersteller
 * gebaut; dort gilt nur das Verbot privater Gerätenamen.
 *
 * Selbsttest unten: Findet die Prüfung ihre Dateien und ihre Zeichenketten überhaupt?
 */
const WURZEL = fileURLToPath(new URL('../..', import.meta.url))

const ALLGEMEIN = [
  'GrowDiary.Web/Models/SteuerungGeraet.cs',
  'GrowDiary.Web/Api/Controllers/SteuerungApiController.cs',
  'GrowDiary.Web/Services/SteuerungAbsicherungService.cs',
  'GrowDiary.React/src/pages/SteuerungPage.tsx',
  'GrowDiary.React/src/pages/TentsPage.tsx',
  'GrowDiary.React/src/features/steuerung/LichtDetail.tsx',
  'GrowDiary.React/src/features/steuerung/ZuluftDetail.tsx',
  'GrowDiary.React/src/features/steuerung/ChillerDetail.tsx',
]
const HERSTELLERSEITE = ['GrowDiary.React/src/pages/AcTestPage.tsx']

// Zusammengesetzt, damit die Datei sich nicht selbst trifft.
const PRIVAT: Array<{ muster: RegExp; grund: string }> = [
  { muster: new RegExp(['LED', ' Top'].join('')), grund: 'Gerätename eines Haushalts — „Lampe" / „Licht"' },
  { muster: new RegExp(['Water', ' Chiller'].join('')), grund: 'englischer Name eines Geräts — „Wasserkühler"' },
  { muster: new RegExp(['Dosier', '-Steckdose'].join('')), grund: 'Haushaltsjargon — „CO₂-Ventil"' },
  { muster: new RegExp('\\b' + ['T', '6'].join('') + '\\b'), grund: 'Typbezeichnung eines Lüfters — „Abluft"' },
  { muster: new RegExp(['Zuluft', ' (·|-)? ?Keller'].join('')), grund: 'Raumname eines Haushalts — „Zuluft"' },
  { muster: new RegExp(['ac', 'infinity_'].join('')), grund: 'Dienstname eines Herstellers als Platzhalter' },
]
const HERSTELLER = /AC[ -]Infinity/g
const BEISPIEL = /z\. ?B\. (bei |beim )?$/

/** Zeichenketten und Code ohne Kommentare. */
export function ohneKommentare(text: string): string {
  return text
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .split('\n')
    .map((zeile) => zeile.replace(/(^|[\s;,)}])\/\/.*$/, '$1'))
    .join('\n')
}

function lesen(pfad: string): string {
  const voll = join(WURZEL, pfad)
  expect(existsSync(voll), `${pfad} fehlt — Liste pflegen`).toBe(true)
  return ohneKommentare(readFileSync(voll, 'utf8'))
}

describe('Oberflächentexte sind allgemein verständlich', () => {
  for (const datei of [...ALLGEMEIN, ...HERSTELLERSEITE]) {
    it(`${datei}: keine Namen eines einzelnen Haushalts`, () => {
      const text = lesen(datei)
      for (const { muster, grund } of PRIVAT) {
        // Die Hersteller-Seite nennt ihren Hersteller, aber keine privaten Gerätenamen.
        if (HERSTELLERSEITE.includes(datei) && !/LED/.test(muster.source)) continue
        const treffer = text.split('\n').filter((z) => muster.test(z))
        expect(treffer, `${muster.source} — ${grund}`).toEqual([])
      }
    })
  }

  for (const datei of ALLGEMEIN) {
    it(`${datei}: Hersteller nur als Beispiel („z. B.")`, () => {
      const text = lesen(datei)
      const lose: string[] = []
      for (const m of text.matchAll(HERSTELLER)) {
        const davor = text.slice(Math.max(0, (m.index ?? 0) - 24), m.index ?? 0)
        if (!BEISPIEL.test(davor)) lose.push(text.slice(Math.max(0, (m.index ?? 0) - 40), (m.index ?? 0) + 30).replace(/\s+/g, ' '))
      }
      expect(lose, 'Hersteller ohne „z. B." davor').toEqual([])
    })
  }

  it('Selbsttest: die Dateien sind da und enthalten Zeichenketten', () => {
    expect(ALLGEMEIN.length).toBeGreaterThanOrEqual(8)
    for (const datei of ALLGEMEIN) expect(lesen(datei).match(/"[^"\n]{12,}"/g)?.length ?? 0, datei).toBeGreaterThan(0)
    // Kommentare werden wirklich entfernt.
    expect(ohneKommentare('a // LED Top\n/* Water Chiller */ b')).not.toMatch(/LED|Chiller/)
  })

  it('Selbsttest: die Muster erkennen, was sie erkennen sollen', () => {
    const probe = ['Licht · ' + 'LED' + ' Top', 'Abluft ' + 'T' + '6 · Stufe', 'Dosier' + '-Steckdose', 'Zuluft · ' + 'Keller']
    for (const text of probe) expect(PRIVAT.some((p) => p.muster.test(text)), text).toBe(true)
    expect(PRIVAT.some((p) => p.muster.test('T6StufeNormal co2_t6_stufe_normal'))).toBe(false)
  })

  it('Selbsttest: die Ordner der geprüften Dateien gibt es', () => {
    expect(readdirSync(join(WURZEL, 'GrowDiary.React/src/features/steuerung')).length).toBeGreaterThan(5)
  })
})
