import { describe, expect, it } from 'vitest'
import { ApiRequestError } from '../../api'
import { feldFehlerAus, leereZahlenfelder, ohneLuecken, zahlAusFeld } from './feld-fehler'

describe('zahlAusFeld', () => {
  it('leer wird NaN — nie 0', () => {
    expect(zahlAusFeld('')).toBeNaN()
    expect(zahlAusFeld('  ')).toBeNaN()
  })
  it('liest Zahlen, ignoriert Unlesbares', () => {
    expect(zahlAusFeld('5')).toBe(5)
    expect(zahlAusFeld('0')).toBe(0)
    expect(zahlAusFeld('1.5')).toBe(1.5)
    expect(zahlAusFeld('abc')).toBeNull()
  })
})

describe('leereZahlenfelder', () => {
  it('nennt leere Zahlenfelder mit C#-Namen', () => {
    expect(leereZahlenfelder({ mindestpauseMin: Number.NaN, hystereseK: 1, modus: 'auto' }))
      .toEqual({ MindestpauseMin: 'Bitte eine Zahl eintragen.' })
  })
  it('null, wenn alles gefüllt ist (auch 0 ist gefüllt)', () => {
    expect(leereZahlenfelder({ mindestpauseMin: 0, hystereseK: 1 })).toBeNull()
  })
})

describe('feldFehlerAus', () => {
  it('liest payload.fieldErrors und schreibt den Namen groß', () => {
    const fehler = new ApiRequestError(400, {
      code: 'validation_failed', message: 'x', traceId: 't',
      fieldErrors: { mindestpauseMin: ['Mindestens 1 Minute.'] },
    } as never, 'x')
    expect(feldFehlerAus(fehler)).toEqual({ MindestpauseMin: 'Mindestens 1 Minute.' })
  })
  it('null ohne Feldfehler', () => {
    expect(feldFehlerAus(new Error('x'))).toBeNull()
  })
})

describe('ohneLuecken', () => {
  it('ersetzt nur leere Zahlenfelder durch den gespeicherten Wert', () => {
    const gespeichert = { stufeMax: 8, mindestDifferenzGm3: 1, modus: 'auto' }
    expect(ohneLuecken({ stufeMax: Number.NaN, mindestDifferenzGm3: 2, modus: 'aus' }, gespeichert))
      .toEqual({ stufeMax: 8, mindestDifferenzGm3: 2, modus: 'aus' })
  })
})
