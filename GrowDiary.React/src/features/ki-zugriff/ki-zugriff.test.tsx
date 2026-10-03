import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { ApiRequestError } from '../../api'
import { KI_STUFEN } from '../../deutsche-woerter'
import type { KiSchluesselDto, KiStufe } from '../../types'
import KiZugriffAbschnitt, { KlartextAnzeige, SchluesselZeile, StufenAuswahl } from './KiZugriffAbschnitt'
import {
  FELD, RISKANTE_STUFEN, RUECKFRAGE_OPTIONEN, STUFEN_WARNUNG, VORBELEGUNG, feldFehlerJeFeld, hoechstwerteLesen,
  praefixAnzeige, sammelmeldung, stufeAnklicken, stufenText, stufenWahl, warnungAblehnen, warnungBestaetigen,
} from './ki-zugriff-logik'

/**
 * Zugriff für KI-Assistenten (A-003) — die Oberfläche ohne Browser.
 *
 * Die Regeln, um die es geht:
 * - „Geräte schalten" und „Verwaltung" bekommen den Haken erst nach einem
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
    erstelltAmUtc: '2026-10-03T08:00:00Z',
    zuletztGenutztAmUtc: null,
    gesperrtAmUtc: null,
    ...teil,
  }
}

describe('Stufen-Häkchen mit Warnhinweis', () => {
  it('sieht ihre Grundmenge: genau zwei riskante Stufen, jede mit Warntext', () => {
    // Ohne diesen Wächter liefen die Schleifen darunter bei leerer Menge
    // null Mal durch und wären grün.
    expect([...RISKANTE_STUFEN].sort()).toEqual(['GeraeteSchalten', 'Verwaltung'])
    for (const stufe of RISKANTE_STUFEN) {
      expect(STUFEN_WARNUNG[stufe], `Warntext für ${stufe} fehlt`).toBeTruthy()
    }
  })

  it('ist vorbelegt mit nur Dokumentieren', () => {
    expect(VORBELEGUNG).toEqual(['Dokumentieren'])
    expect(stufenWahl()).toEqual({ auswahl: ['Dokumentieren'], offeneWarnung: null })
  })

  it('hakt eine harmlose Stufe sofort an', () => {
    const wahl = stufeAnklicken(stufenWahl(), 'GrowPlanen', true)
    expect(wahl.auswahl).toEqual(['Dokumentieren', 'GrowPlanen'])
    expect(wahl.offeneWarnung).toBeNull()
  })

  for (const stufe of ['GeraeteSchalten', 'Verwaltung'] as KiStufe[]) {
    it(`${stufe}: ohne Bestätigung bleibt der Haken aus`, () => {
      const wahl = stufeAnklicken(stufenWahl(), stufe, true)
      expect(wahl.auswahl, 'Der Haken ist da, obwohl niemand den Warnhinweis bestätigt hat.').not.toContain(stufe)
      expect(wahl.offeneWarnung).toBe(stufe)

      const abgelehnt = warnungAblehnen(wahl)
      expect(abgelehnt.auswahl).not.toContain(stufe)
      expect(abgelehnt.offeneWarnung).toBeNull()
    })

    it(`${stufe}: nach „Freigeben" ist der Haken da`, () => {
      const wahl = warnungBestaetigen(stufeAnklicken(stufenWahl(), stufe, true))
      expect(wahl.auswahl).toContain(stufe)
      expect(wahl.offeneWarnung).toBeNull()
    })
  }

  it('fragt beim zweiten Mal wieder — auch bei einem Schlüssel, der die Stufe schon hatte', () => {
    // Bearbeiten eines Schlüssels mit Geräte schalten: abwählen geht ohne
    // Rückfrage, wieder anhaken nicht.
    const vorhanden = stufenWahl(['Dokumentieren', 'GeraeteSchalten'])
    const ab = stufeAnklicken(vorhanden, 'GeraeteSchalten', false)
    expect(ab.auswahl).toEqual(['Dokumentieren'])
    expect(ab.offeneWarnung).toBeNull()

    const wieder = stufeAnklicken(ab, 'GeraeteSchalten', true)
    expect(wieder.auswahl).not.toContain('GeraeteSchalten')
    expect(wieder.offeneWarnung).toBe('GeraeteSchalten')
  })

  it('schliesst die Warnung, wenn man den offenen Haken wieder abwählt', () => {
    const offen = stufeAnklicken(stufenWahl(), 'Verwaltung', true)
    const zu = stufeAnklicken(offen, 'Verwaltung', false)
    expect(zu).toEqual({ auswahl: ['Dokumentieren'], offeneWarnung: null })
  })

  it('zeigt den Warnhinweis und lässt das Häkchen aus', () => {
    const wahl = stufeAnklicken(stufenWahl(), 'GeraeteSchalten', true)
    const html = renderToStaticMarkup(<StufenAuswahl wahl={wahl} onWahl={() => {}} />)

    expect(html).toContain('data-audit="ki-stufen-warnung"')
    expect(sichtbarerText(html)).toContain('sofort auslösen')
    expect(sichtbarerText(html)).toContain('Freigeben')

    const geraete = /<input[^>]*data-stufe="GeraeteSchalten"[^>]*>/.exec(html)?.[0] ?? ''
    expect(geraete, 'Häkchen für Geräte schalten nicht gefunden').not.toBe('')
    expect(geraete, 'Das Häkchen ist gesetzt, solange die Warnung offen ist.').not.toContain('checked')

    const doku = /<input[^>]*data-stufe="Dokumentieren"[^>]*>/.exec(html)?.[0] ?? ''
    expect(doku).toContain('checked')
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

  it('die Rückfrage-Auswahl: „nie" heisst null, sonst der Stufenname', () => {
    expect(RUECKFRAGE_OPTIONEN[0]).toEqual({ wert: null, text: 'nie' })
    expect(RUECKFRAGE_OPTIONEN.slice(1).map((o) => o.wert)).toEqual(KI_STUFEN)
    expect(RUECKFRAGE_OPTIONEN.map((o) => o.text)).toEqual(
      ['nie', 'ab Dokumentieren', 'ab Grow planen', 'ab Geräte schalten', 'ab Verwaltung'])
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
    expect(sichtbarerText(html)).toContain('Zugriff für KI-Assistenten')
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
