import { describe, expect, it } from 'vitest'
import { ApiRequestError, formatApiError } from './api'
import type { ApiError } from './types'

/**
 * Welche Meldung der Nutzer wirklich zu sehen bekommt.
 *
 * **Der Anlass (02.09.2026).** `src/api.ts` stand bei **0 %** Abdeckung — die
 * Datei, durch die jeder einzelne Aufruf der Oberfläche läuft. Die
 * Erfolgspfade fahren die 718 Playwright-Fälle ohnehin; ungeprüft sind genau
 * die **Fehlerpfade**, und dort sind in diesem Projekt schon zwei Fehler
 * gelandet:
 *
 * - Beim Absenden eines Teilwechsels ohne Menge stand „Eingaben konnten nicht
 *   validiert werden." auf dem Schirm. Der eigentliche Grund lag daneben,
 *   ungelesen: das Backend schickt ihn in `fieldErrors`, gelesen wurde nur
 *   `message`.
 * - Dieselbe Stelle hat schon einmal eine **englische** Meldung durchgereicht.
 *
 * Eine Meldung, die nicht sagt, was zu tun ist, ist so gut wie keine — der
 * Nutzer sitzt vor einem Formular, das sich nicht abschicken lässt, und
 * erfährt nicht warum.
 */

/** Ein Fehlerrumpf, wie ihn das Backend schickt. */
function rumpf(teile: Partial<ApiError>): ApiError {
  return {
    code: 'irgendwas',
    message: 'Etwas ging schief.',
    ...teile,
  } as ApiError
}

describe('Die Meldung aus einer Fehlerantwort', () => {
  it('nimmt die Feldmeldung statt der Sammelmeldung', () => {
    const fehler = new ApiRequestError(400, rumpf({
      code: 'validation_failed',
      message: 'Eingaben konnten nicht validiert werden.',
      fieldErrors: { amountLiters: ['Bitte eine Menge in Litern angeben.'] },
    }), 'egal')

    expect(
      fehler.message,
      'Auf dem Schirm steht die Sammelmeldung, und der eigentliche Grund liegt daneben '
      + 'ungelesen. Der Nutzer sitzt vor einem Formular, das sich nicht abschicken laesst, '
      + 'und erfaehrt nicht warum.',
    ).toBe('Bitte eine Menge in Litern angeben.')
  })

  it('haengt mehrere Feldmeldungen aneinander, statt eine zu verschlucken', () => {
    const fehler = new ApiRequestError(400, rumpf({
      code: 'validation_failed',
      message: 'Eingaben konnten nicht validiert werden.',
      fieldErrors: {
        amountLiters: ['Bitte eine Menge angeben.'],
        occurredAt: ['Der Zeitpunkt liegt vor dem Start des Grows.'],
      },
    }), 'egal')

    expect(fehler.message).toContain('Bitte eine Menge angeben.')
    expect(
      fehler.message,
      'Von zwei Feldmeldungen kommt nur eine an. Der Nutzer korrigiert die erste, '
      + 'schickt wieder ab und bekommt die zweite — ein Formular, das ihn Runde fuer Runde '
      + 'im Kreis schickt.',
    ).toContain('Der Zeitpunkt liegt vor dem Start des Grows.')
  })

  it('laesst eine eigene Meldung des Endpunkts stehen', () => {
    // Die Gegenrichtung: nur die SAMMELmeldung darf verdraengt werden. Eine
    // eigene Meldung ist bewusst gewaehlt und sagt mehr als das Feld.
    const fehler = new ApiRequestError(400, rumpf({
      code: 'grow_without_tent',
      message: 'Zielgeraet und Kuehler haengen am Zelt, dieser Grow hat keines.',
      fieldErrors: { targetEntityId: ['Pflichtfeld.'] },
    }), 'egal')

    expect(
      fehler.message,
      'Die eigene Meldung des Endpunkts wurde von einem duerren „Pflichtfeld." verdraengt.',
    ).toBe('Zielgeraet und Kuehler haengen am Zelt, dieser Grow hat keines.')
  })

  it('ignoriert leere Feldmeldungen', () => {
    const fehler = new ApiRequestError(400, rumpf({
      code: 'validation_failed',
      message: 'Eingaben konnten nicht validiert werden.',
      fieldErrors: { feld: ['   ', ''] },
    }), 'egal')

    expect(
      fehler.message,
      'Leerzeichen aus fieldErrors haben die Sammelmeldung verdraengt — auf dem Schirm '
      + 'stuende dann gar nichts.',
    ).toBe('Eingaben konnten nicht validiert werden.')
  })

  it('faellt auf den Ersatztext zurueck, wenn kein Rumpf kam', () => {
    // Bei 502 oder abgerissener Verbindung gibt es kein JSON.
    const fehler = new ApiRequestError(502, null, 'Cultivate Control antwortet nicht.')

    expect(fehler.message).toBe('Cultivate Control antwortet nicht.')
    expect(fehler.status).toBe(502)
  })

  it('behaelt Rumpf und Status zum Nachsehen', () => {
    // Einige Seiten verzweigen auf `code` — etwa um zum richtigen Feld zu
    // springen. Ginge der Rumpf verloren, ginge das still nicht mehr.
    const nutzlast = rumpf({ code: 'chiller_not_a_switch' })
    const fehler = new ApiRequestError(400, nutzlast, 'egal')

    expect(fehler.payload?.code).toBe('chiller_not_a_switch')
  })
})

describe('formatApiError', () => {
  it('nimmt die Meldung eines Api-Fehlers', () => {
    const fehler = new ApiRequestError(400, rumpf({ message: 'Zelt nicht gefunden.' }), 'egal')

    expect(formatApiError(fehler, 'Ersatz')).toBe('Zelt nicht gefunden.')
  })

  it('nimmt die Meldung eines gewoehnlichen Fehlers', () => {
    expect(formatApiError(new Error('Netzwerk weg.'), 'Ersatz')).toBe('Netzwerk weg.')
  })

  it('nimmt den Ersatztext, wenn gar kein Fehler geworfen wurde', () => {
    // `throw 'text'` und abgebrochene Anfragen landen hier.
    expect(formatApiError('irgendwas', 'Konnte nicht geladen werden.'))
      .toBe('Konnte nicht geladen werden.')
  })
})
