import { readdirSync, readFileSync } from 'node:fs'
import { join, relative } from 'node:path'
import { describe, expect, it } from 'vitest'
import { mitUmlauten, ohneUmlaut, textstuecke } from './sprache-umlaute'

/**
 * Jeder Text, den ein Mensch liest, hat echte Umlaute — in der Oberfläche, im Server und in den KI-Werkzeugen.
 *
 * <b>Der Anlass (24.10.2026).</b> Ein Durchgang durch die ganze App fand „Zelt waehlen“ auf der Hydro-Seite und rund 105 Meldungen im
 * Server („Grow geaendert“, „fuer“, „ueber“). `MeldungenMitUmlautenTests` (cc.265) sieht nur die Controller und bestimmte Fehlerfunktionen;
 * Dienste, die Chronik, die KI-Werkzeuge und die Oberfläche blieben außen vor. Diese Prüfung geht über **alle** Zeichenketten und JSX-Texte
 * mit Leerzeichen darin (Schlüssel, Klassen, Bezeichner nicht) und nutzt dieselbe Wortliste wie das einmalige Umschreiben.
 */
const WURZEL = join(import.meta.dirname, '..', '..')

/** Ausnahmen mit Grund: Namen, die in Home Assistant angelegt sind und deshalb so heißen müssen, wie sie dort stehen. */
const AUSNAHMEN: Record<string, string> = {
  'GrowDiary.Web/Models/Kennungen.cs': 'Namen der Hilfsgrößen in Home Assistant (Bestand wird gefunden, nicht umbenannt)',
  'GrowDiary.Web/Models/SteuerungBauteil.cs': 'Namen der Hilfsgrößen und Vorlagen in Home Assistant',
  'GrowDiary.Web/Infrastructure/DatabaseInitializer.Schema.cs': 'SQL der Datenbank-Migration (Text in der Migration bleibt wie ausgeliefert)',
}

function quelldateien(): string[] {
  const aus: string[] = []
  const gehe = (ordner: string): void => {
    for (const e of readdirSync(ordner, { withFileTypes: true })) {
      if (['node_modules', 'bin', 'obj', 'dist', 'Migrations', 'e2e', 'zz-plausibel', 'zz-schrift'].includes(e.name)) continue
      const pfad = join(ordner, e.name)
      if (e.isDirectory()) gehe(pfad)
      else if (/\.(cs|tsx?)$/.test(e.name) && !/(\.test\.|\.d\.ts|Tests?\.cs$)/.test(e.name) && !pfad.endsWith('sprache-umlaute.ts')) aus.push(pfad)
    }
  }
  for (const w of ['GrowDiary.Web', 'GrowMcp', join('GrowDiary.React', 'src')]) gehe(join(WURZEL, w))
  return aus
}

describe('Umlaute in Texten', () => {
  it('kein Text, den ein Mensch liest, steht mit ae/oe/ue/ss statt ä/ö/ü/ß da', () => {
    const fehler: string[] = []
    let stuecke = 0
    for (const datei of quelldateien()) {
      const name = relative(WURZEL, datei).replaceAll('\\', '/')
      if (AUSNAHMEN[name]) continue
      const text = readFileSync(datei, 'utf8')
      for (const s of textstuecke(datei, text)) {
        stuecke++
        const funde = ohneUmlaut(s.text)
        if (funde.length > 0) fehler.push(`${name}: ${funde.join(', ')} in „${s.text.slice(0, 80).replace(/\s+/g, ' ')}“`)
      }
    }
    // Mengenwächter: sieht die Prüfung ihre Grundmenge überhaupt?
    expect(stuecke).toBeGreaterThan(2000)
    expect(fehler, `Texte ohne Umlaut (Wortliste: src/sprache-umlaute.ts):\n${fehler.join('\n')}`).toEqual([])
  })

  it('die Ausnahmen gibt es noch (sonst gehört die Zeile gestrichen)', () => {
    for (const name of Object.keys(AUSNAHMEN)) expect(() => readFileSync(join(WURZEL, name), 'utf8')).not.toThrow()
  })

  it('beißt: findet Wörter ohne Umlaut und lässt Kennungen, Platzhalter und Parameter in Ruhe', () => {
    expect(ohneUmlaut('Zelt waehlen — Grow geloescht')).toEqual(['waehlen', 'geloescht'])
    expect(mitUmlauten('Zelt waehlen, fuer 10 Minuten ueber dem Wert')).toBe('Zelt wählen, für 10 Minuten über dem Wert')
    expect(mitUmlauten('Die Blueten gross: PRUEFUNG')).toBe('Die Blueten groß: PRÜFUNG')
    expect(ohneUmlaut('Zaehler {Zaehler} bleibt')).toEqual(['Zaehler'])         // Platzhalter in {…} bleibt
    expect(ohneUmlaut('ergaenzen=false und neue Quelle')).toEqual([])           // Parameter vor „=“, „neue“ und „Quelle“ sind richtig
    expect(ohneUmlaut('befehl=preset: veggie oder bluete')).toEqual([])         // Kennwert der KI-Werkzeuge
    expect(ohneUmlaut('Steuerung true value request Israel Bluelab')).toEqual([])
  })

  it('liest Texte aus C#, TSX und lässt Klassenlisten aus', () => {
    expect(textstuecke('a.cs', 'var x = $"Zelt {z} waehlen"; var k = "ueberblick";').map((s) => s.text)).toEqual(['Zelt ', ' waehlen'])
    expect(textstuecke('a.tsx', 'const a = <p className="st-geraet-wert is-faint">Zelt waehlen</p>').map((s) => s.text)).toEqual(['Zelt waehlen'])
    expect(textstuecke("a.tsx", "const a = <h2>Pruefung</h2>").map((s) => s.text)).toEqual(["Pruefung"])
  })
})
