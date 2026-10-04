import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { ApiRequestError } from '../../api'
import { KI_STUFEN } from '../../deutsche-woerter'
import type { KiSchluesselDto, KiStufe } from '../../types'
import KiZugriffAbschnitt, { KlartextAnzeige, SchluesselZeile, StufenAuswahl } from './KiZugriffAbschnitt'
import {
  FELD, RISKANTE_STUFEN, STUFEN_WARNUNG, VORBELEGUNG, ZUSTAENDE, anfrageAus, feldFehlerJeFeld, hoechstwerteLesen,
  praefixAnzeige, sammelmeldung, stufenSchilder, stufenText, stufenWahl, warnungAblehnen, warnungBestaetigen,
  zustandWaehlen, type StufenZustand,
} from './ki-zugriff-logik'

/**
 * Zugriff für KI-Assistenten (A-003) — die Oberfläche ohne Browser.
 *
 * Die Regeln, um die es geht:
 * - Je Stufe drei Zustände: Gesperrt · Mit Rückfrage · Frei (A-005).
 * - „Geräte schalten" und „Verwaltung" verlassen Gesperrt erst nach einem
 *   bestätigten Warnhinweis.
 * - Auf dem Schirm stehen die deutschen Stufen-Namen, nie die Bezeichner
 *   („GrowPlanen", „GeraeteSchalten").
 * - Der Klartext eines Schlüssels steht genau einmal da — in der Anzeige nach
 *   dem Anlegen, nie in der Liste.
 *
 * Gerendert wird mit `renderToStaticMarkup`: die Vitest-Umgebung hat kein DOM,
 * und die Regeln selbst liegen in `ki-zugriff-logik.ts`.
 */

/** Nur der sichtbare Text — Attribute wie `data-stufe="GeraeteSchalten"` sieht niemand. */
function sichtbarerText(html: string): string {
  return html.replace(/<[^>]+>/g, ' ').replace(/&quot;/g, '"').replace(/&amp;/g, '&').replace(/\s+/g, ' ')
}

/** Die rohen Bezeichner, die anders aussehen als ihr deutsches Wort. */
const ROHE_BEZEICHNER = ['GrowPlanen', 'GeraeteSchalten']

function schluessel(teil: Partial<KiSchluesselDto> = {}): KiSchluesselDto {
  return {
    id: 7,
    name: 'Claude am Telefon',
    praefix: 'ab12cd34',
    stufen: ['Dokumentieren', 'GrowPlanen'],
    rueckfrageBei: [],
    erstelltAmUtc: '2026-10-03T08:00:00Z',
    zuletztGenutztAmUtc: null,
    gesperrtAmUtc: null,
    ...teil,
  }
}

describe('Drei Zustände je Stufe, mit Warnhinweis', () => {
  it('sieht ihre Grundmenge: genau zwei riskante Stufen, jede mit Warntext', () => {
    // Ohne diesen Wächter liefen die Schleifen darunter bei leerer Menge
    // null Mal durch und wären grün.
    expect([...RISKANTE_STUFEN].sort()).toEqual(['GeraeteSchalten', 'Verwaltung'])
    for (const stufe of RISKANTE_STUFEN) {
      expect(STUFEN_WARNUNG[stufe], `Warntext für ${stufe} fehlt`).toBeTruthy()
    }
  })

  it('ist vorbelegt mit Dokumentieren frei, alles andere gesperrt', () => {
    expect(VORBELEGUNG).toEqual(['Dokumentieren'])
    expect(stufenWahl()).toEqual({
      zustaende: { Dokumentieren: 'frei', GrowPlanen: 'gesperrt', GeraeteSchalten: 'gesperrt', Verwaltung: 'gesperrt' },
      offeneWarnung: null,
    })
    expect(anfrageAus(stufenWahl())).toEqual({ stufen: ['Dokumentieren'], rueckfrageBei: [] })
  })

  it('liest stufen und rueckfrageBei in drei Zustände — eine Rückfrage ohne Freigabe zählt nicht', () => {
    const wahl = stufenWahl(['Dokumentieren', 'GrowPlanen'], ['GrowPlanen', 'Verwaltung'])
    expect(wahl.zustaende).toEqual({ Dokumentieren: 'frei', GrowPlanen: 'rueckfrage', GeraeteSchalten: 'gesperrt', Verwaltung: 'gesperrt' })
    expect(anfrageAus(wahl)).toEqual({ stufen: ['Dokumentieren', 'GrowPlanen'], rueckfrageBei: ['GrowPlanen'] })
  })

  it('stellt eine harmlose Stufe sofort um', () => {
    const rueckfrage = zustandWaehlen(stufenWahl(), 'GrowPlanen', 'rueckfrage')
    expect(rueckfrage.zustaende.GrowPlanen).toBe('rueckfrage')
    expect(rueckfrage.offeneWarnung).toBeNull()
    expect(anfrageAus(rueckfrage)).toEqual({ stufen: ['Dokumentieren', 'GrowPlanen'], rueckfrageBei: ['GrowPlanen'] })

    const gesperrt = zustandWaehlen(rueckfrage, 'Dokumentieren', 'gesperrt')
    expect(anfrageAus(gesperrt)).toEqual({ stufen: ['GrowPlanen'], rueckfrageBei: ['GrowPlanen'] })
  })

  for (const stufe of ['GeraeteSchalten', 'Verwaltung'] as KiStufe[]) {
    for (const ziel of ['rueckfrage', 'frei'] as const) {
      it(`${stufe} → ${ziel}: ohne Bestätigung bleibt die Stufe gesperrt`, () => {
        const wahl = zustandWaehlen(stufenWahl(), stufe, ziel)
        expect(wahl.zustaende[stufe], 'Freigegeben, obwohl niemand den Warnhinweis bestätigt hat.').toBe('gesperrt')
        expect(wahl.offeneWarnung).toEqual({ stufe, ziel })
        expect(anfrageAus(wahl).stufen).not.toContain(stufe)

        const abgelehnt = warnungAblehnen(wahl)
        expect(abgelehnt.zustaende[stufe]).toBe('gesperrt')
        expect(abgelehnt.offeneWarnung).toBeNull()
      })

      it(`${stufe} → ${ziel}: nach „Freigeben" gilt der gewählte Zustand`, () => {
        const wahl = warnungBestaetigen(zustandWaehlen(stufenWahl(), stufe, ziel))
        expect(wahl.zustaende[stufe]).toBe(ziel)
        expect(wahl.offeneWarnung).toBeNull()
      })
    }
  }

  it('zwischen Mit Rückfrage und Frei fragt niemand — die Stufe ist schon freigegeben', () => {
    const vorhanden = stufenWahl(['Dokumentieren', 'GeraeteSchalten'], ['GeraeteSchalten'])
    const frei = zustandWaehlen(vorhanden, 'GeraeteSchalten', 'frei')
    expect(frei.zustaende.GeraeteSchalten).toBe('frei')
    expect(frei.offeneWarnung).toBeNull()
    const zurueck = zustandWaehlen(frei, 'GeraeteSchalten', 'rueckfrage')
    expect(zurueck.zustaende.GeraeteSchalten).toBe('rueckfrage')
    expect(zurueck.offeneWarnung).toBeNull()
  })

  it('fragt beim zweiten Mal wieder — auch bei einem Schlüssel, der die Stufe schon hatte', () => {
    // Bearbeiten eines Schlüssels mit Geräte schalten: sperren geht ohne
    // Rückfrage, wieder freigeben nicht.
    const vorhanden = stufenWahl(['Dokumentieren', 'GeraeteSchalten'])
    const gesperrt = zustandWaehlen(vorhanden, 'GeraeteSchalten', 'gesperrt')
    expect(gesperrt.zustaende.GeraeteSchalten).toBe('gesperrt')
    expect(gesperrt.offeneWarnung).toBeNull()

    const wieder = zustandWaehlen(gesperrt, 'GeraeteSchalten', 'frei')
    expect(wieder.zustaende.GeraeteSchalten).toBe('gesperrt')
    expect(wieder.offeneWarnung).toEqual({ stufe: 'GeraeteSchalten', ziel: 'frei' })
  })

  it('ein anderer Zustand derselben Stufe ändert nur das Ziel; Gesperrt schliesst den Hinweis', () => {
    const offen = zustandWaehlen(stufenWahl(), 'Verwaltung', 'frei')
    const anders = zustandWaehlen(offen, 'Verwaltung', 'rueckfrage')
    expect(anders.offeneWarnung).toEqual({ stufe: 'Verwaltung', ziel: 'rueckfrage' })
    expect(anders.zustaende.Verwaltung).toBe('gesperrt')

    const zu = zustandWaehlen(anders, 'Verwaltung', 'gesperrt')
    expect(zu).toEqual(stufenWahl())
  })

  it('der Umschalter: je Stufe eine beschriftete Radiogruppe mit drei Radioknöpfen', () => {
    const wahl = stufenWahl(['Dokumentieren', 'GrowPlanen'], ['GrowPlanen'])
    const html = renderToStaticMarkup(<StufenAuswahl wahl={wahl} onWahl={() => {}} />)

    const gruppen = html.match(/<div[^>]*role="radiogroup"[^>]*>/g) ?? []
    expect(gruppen).toHaveLength(KI_STUFEN.length)
    for (const gruppe of gruppen) {
      // Beschriftet über den Namen der Stufe, beschrieben über ihre Erklärung.
      const titel = /aria-labelledby="([^"]+)"/.exec(gruppe)?.[1]
      expect(titel, `Radiogruppe ohne Beschriftung: ${gruppe}`).toBeTruthy()
      expect(html).toContain(`id="${titel}"`)
      expect(gruppe).toMatch(/aria-describedby="[^"]+"/)
    }

    for (const stufe of KI_STUFEN) {
      const knoepfe = html.match(new RegExp(`<input[^>]*data-stufe="${stufe}"[^>]*>`, 'g')) ?? []
      expect(knoepfe, `${stufe}: drei Radioknöpfe erwartet`).toHaveLength(3)
      // Ein Name je Stufe — erst das macht die Pfeiltasten und den einen Tabulator-Halt.
      const namen = new Set(knoepfe.map((k) => /name="([^"]+)"/.exec(k)?.[1]))
      expect(namen.size).toBe(1)
      expect(knoepfe.every((k) => k.includes('type="radio"'))).toBe(true)
      const gewaehlt = knoepfe.filter((k) => /\schecked(=""|\s|>|\/)/.test(k)).map((k) => /data-zustand="([^"]+)"/.exec(k)?.[1])
      expect(gewaehlt).toEqual([wahl.zustaende[stufe]])
    }

    const text = sichtbarerText(html)
    for (const { text: wort } of ZUSTAENDE) expect(text).toContain(wort)
    // Kein „ab" mehr — weder im Umschalter noch in den Erklärungen.
    expect(text).not.toMatch(/\bab\b/)
  })

  it('zeigt den Warnhinweis und lässt die Stufe auf Gesperrt', () => {
    const wahl = zustandWaehlen(stufenWahl(), 'GeraeteSchalten', 'rueckfrage')
    const html = renderToStaticMarkup(<StufenAuswahl wahl={wahl} onWahl={() => {}} />)

    expect(html).toContain('data-audit="ki-stufen-warnung"')
    expect(sichtbarerText(html)).toContain('sofort auslösen')
    expect(sichtbarerText(html)).toContain('Achtung: Geräte schalten auf „Mit Rückfrage"')
    expect(sichtbarerText(html)).toContain('Freigeben')
    expect(sichtbarerText(html)).toContain('Gesperrt lassen')

    const gewaehlt = (stufe: KiStufe): StufenZustand | undefined => {
      const knopf = (html.match(new RegExp(`<input[^>]*data-stufe="${stufe}"[^>]*>`, 'g')) ?? [])
        .find((k) => /\schecked(=""|\s|>|\/)/.test(k))
      return /data-zustand="([^"]+)"/.exec(knopf ?? '')?.[1] as StufenZustand | undefined
    }
    expect(gewaehlt('GeraeteSchalten'), 'Die Stufe ist freigegeben, solange die Warnung offen ist.').toBe('gesperrt')
    expect(gewaehlt('Dokumentieren')).toBe('frei')
  })

  it('zeigt ohne offene Warnung keinen Warnhinweis', () => {
    const html = renderToStaticMarkup(<StufenAuswahl wahl={stufenWahl()} onWahl={() => {}} />)
    expect(html).not.toContain('ki-stufen-warnung')
  })
})

describe('Deutsche Stufen-Namen', () => {
  it('die Häkchen tragen deutsche Namen', () => {
    const text = sichtbarerText(renderToStaticMarkup(<StufenAuswahl wahl={stufenWahl()} onWahl={() => {}} />))
    for (const name of ['Dokumentieren', 'Grow planen', 'Geräte schalten', 'Verwaltung']) expect(text).toContain(name)
    for (const roh of ROHE_BEZEICHNER) expect(text, `„${roh}" steht roh an den Häkchen`).not.toContain(roh)
  })

  it('die Schlüsselzeile zeigt Stufen deutsch, Präfix, Datum und „noch nie"', () => {
    const html = renderToStaticMarkup(
      <SchluesselZeile eintrag={schluessel({ stufen: ['Verwaltung', 'GrowPlanen', 'GeraeteSchalten', 'Dokumentieren'] })}
        bearbeitet={false} onStufenAendern={() => {}} onSperren={() => {}} onLoeschen={() => {}} />,
    )
    const text = sichtbarerText(html)
    expect(text).toContain('Claude am Telefon')
    expect(text).toContain('gok_ab12cd34…')
    expect(text).toContain('Grow planen')
    expect(text).toContain('Geräte schalten')
    expect(text).toContain('03.10.2026')
    expect(text).toContain('zuletzt genutzt noch nie')
    for (const roh of ROHE_BEZEICHNER) expect(text, `„${roh}" steht roh in der Liste`).not.toContain(roh)
    expect(text).toContain('Stufen ändern')
    expect(text).toContain('Sperren')
    expect(text).toContain('Löschen')
  })

  it('ein gesperrter Schlüssel zeigt „gesperrt" und kann nur noch gelöscht werden', () => {
    const text = sichtbarerText(renderToStaticMarkup(
      <SchluesselZeile eintrag={schluessel({ gesperrtAmUtc: '2026-10-03T09:00:00Z', zuletztGenutztAmUtc: '2026-10-03T08:30:00Z' })}
        bearbeitet={false} onStufenAendern={() => {}} onSperren={() => {}} onLoeschen={() => {}} />,
    ))
    expect(text).toContain('gesperrt')
    expect(text).not.toContain('noch nie')
    expect(text).not.toContain('Sperren')
    expect(text).not.toContain('Stufen ändern')
    expect(text).toContain('Löschen')
  })

  it('die Schilder sagen je Stufe frei oder mit Rückfrage — gesperrte fehlen', () => {
    expect(stufenSchilder(['GeraeteSchalten', 'Dokumentieren', 'GrowPlanen'], ['GrowPlanen']).map((s) => s.text))
      .toEqual(['Dokumentieren · frei', 'Grow planen · mit Rückfrage', 'Geräte schalten · frei'])

    const text = sichtbarerText(renderToStaticMarkup(
      <SchluesselZeile eintrag={schluessel({ stufen: ['Dokumentieren', 'GeraeteSchalten'], rueckfrageBei: ['GeraeteSchalten'] })}
        bearbeitet={false} onStufenAendern={() => {}} onSperren={() => {}} onLoeschen={() => {}} />,
    ))
    expect(text).toContain('Dokumentieren · frei')
    expect(text).toContain('Geräte schalten · mit Rückfrage')
    expect(text).not.toContain('Grow planen')
    expect(text).not.toContain('Verwaltung')
    for (const roh of [...ROHE_BEZEICHNER, 'rueckfrage']) expect(text).not.toContain(roh)
  })

  it('stufenText reiht deutsch und in fester Reihenfolge', () => {
    expect(stufenText(['GeraeteSchalten', 'Dokumentieren'])).toBe('Dokumentieren · Geräte schalten')
    expect(stufenText([])).toBe('keine Stufe — nur lesen')
  })
})

describe('Klartext nur einmal', () => {
  const klartext = 'gok_ab12cd34EFGHijklMNOPqrstUVWXyz0123456789-_'

  it('steht in der Anzeige nach dem Anlegen genau einmal — mit dem Satz dazu', () => {
    const html = renderToStaticMarkup(<KlartextAnzeige name="Claude" klartext={klartext} onAusblenden={() => {}} />)
    expect(html.split(klartext).length - 1).toBe(1)
    expect(sichtbarerText(html)).toContain('Wird nur jetzt angezeigt — danach nicht mehr.')
    expect(sichtbarerText(html)).toContain('Kopieren')
  })

  it('die Liste zeigt nie mehr als acht Zeichen nach gok_', () => {
    // Auch wenn das Backend einmal zu viel schickt — die Liste ist kein Ort
    // für einen Schlüssel.
    expect(praefixAnzeige('ab12cd34')).toBe('gok_ab12cd34…')
    expect(praefixAnzeige('gok_ab12cd34')).toBe('gok_ab12cd34…')
    expect(praefixAnzeige(klartext)).toBe('gok_ab12cd34…')

    const html = renderToStaticMarkup(
      <SchluesselZeile eintrag={schluessel({ praefix: klartext })} bearbeitet={false}
        onStufenAendern={() => {}} onSperren={() => {}} onLoeschen={() => {}} />,
    )
    expect(html).not.toContain(klartext)
    expect(html).not.toContain('EFGH')
  })

  it('der Abschnitt zeigt beim ersten Zeichnen keinen Klartext, nur den Ladezustand', () => {
    const html = renderToStaticMarkup(<KiZugriffAbschnitt />)
    expect(html).toContain('data-audit="settings-ki-zugriff"')
    expect(sichtbarerText(html)).toContain('Schlüssel & Freigaben')
    expect(sichtbarerText(html)).toContain('Ab Werk ist das aus')
    expect(html).not.toContain('ki-klartext')
  })
})

describe('Höchstwerte lesen', () => {
  it('liest deutsche Zahlen', () => {
    expect(hoechstwerteLesen({ maxDosisMl: '2,5', maxBefehleJeStunde: '20' }))
      .toEqual({ ok: true, werte: { maxDosisMlJeBefehl: 2.5, maxSchaltbefehleJeStunde: 20 } })
    expect(hoechstwerteLesen({ maxDosisMl: '1.200', maxBefehleJeStunde: '1.000' }))
      .toEqual({ ok: true, werte: { maxDosisMlJeBefehl: 1200, maxSchaltbefehleJeStunde: 1000 } })
  })

  it('macht aus leer keine 0 und keine Vorbelegung', () => {
    const gelesen = hoechstwerteLesen({ maxDosisMl: '', maxBefehleJeStunde: '' })
    expect(gelesen.ok).toBe(false)
    if (!gelesen.ok) {
      expect(gelesen.felder[FELD.maxDosis]).toBeTruthy()
      expect(gelesen.felder[FELD.maxBefehle]).toBeTruthy()
    }
  })

  it('meldet Unlesbares mit dem Feldnamen', () => {
    const gelesen = hoechstwerteLesen({ maxDosisMl: '2,5x', maxBefehleJeStunde: '20' })
    expect(gelesen.ok).toBe(false)
    if (!gelesen.ok) expect(gelesen.meldung).toContain('Höchstens ml je Dosierbefehl')
  })

  it('verlangt ganze Befehle und mehr als 0 ml', () => {
    const gelesen = hoechstwerteLesen({ maxDosisMl: '0', maxBefehleJeStunde: '2,5' })
    expect(gelesen.ok).toBe(false)
    if (!gelesen.ok) {
      expect(gelesen.felder[FELD.maxDosis]).toContain('größer als 0')
      expect(gelesen.felder[FELD.maxBefehle]).toContain('ganze Zahl')
    }
  })
})

describe('Feldfehler aus der API', () => {
  const fehler = new ApiRequestError(400, {
    code: 'validation_failed',
    message: 'Eingaben konnten nicht validiert werden.',
    fieldErrors: {
      'Hoechstwerte.MaxDosisMlJeBefehl': ['Höchstens 100 ml.'],
      'Stufen[0]': ['Unbekannte Stufe „Alles".'],
      Sonstiges: ['Etwas anderes.'],
    },
  }, 'HTTP 400')

  it('ordnet jede Schreibweise dem Feld zu', () => {
    expect(feldFehlerJeFeld(fehler)).toEqual({
      [FELD.maxDosis]: 'Höchstens 100 ml.',
      [FELD.stufen]: 'Unbekannte Stufe „Alles".',
      sonstiges: 'Etwas anderes.',
    })
  })

  it('lässt keinen Fehler verschwinden, der kein sichtbares Feld hat', () => {
    expect(sammelmeldung(fehler, [FELD.maxDosis, FELD.stufen], 'x'))
      .toBe('Bitte die markierten Felder prüfen. Etwas anderes.')
    expect(sammelmeldung(fehler, [], 'x')).not.toContain('markierten')
    expect(sammelmeldung(new Error('Netz weg'), [], 'x')).toBe('Netz weg')
  })
})
