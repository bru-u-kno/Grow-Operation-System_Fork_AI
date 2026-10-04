import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import type { JournalEntryDto } from '../../types'
import { EintragFelderFormular, JournalStreamSection } from './JournalStreamSection'
import { EINTRAG_ARTEN, MEILENSTEIN_ARTEN, aenderungsAnfrage, alsFelder, artenFuer, eintragFehler, istMeilenstein, textZeilen } from './journal-bearbeiten'
import { emptyJournalForm, emptyPhotoForm, emptyTaskForm } from './grow-detail-model'

/**
 * Journaleinträge bearbeiten (05.10.2026) — die Oberfläche ohne Browser.
 *
 * Anlass: „CANNA pH- Pro Bloom: Menge nicht notiert" stand im Journal und
 * ließ sich nicht nachtragen. Die Bedienung selbst (öffnen, ändern,
 * speichern, nochmal) fährt `e2e/journal-bearbeiten.spec.ts` am laufenden
 * Stand; hier stehen die Regeln.
 */

function eintrag(teil: Partial<JournalEntryDto> = {}): JournalEntryDto {
  return {
    id: 19,
    growId: 1,
    measurementId: null,
    title: 'Wasserwechsel RDWC 160 L – Blütewoche 7',
    body: 'CANNA pH- Pro Bloom: Menge nicht notiert',
    entryType: 'ReservoirChange',
    source: 'Manual',
    occurredAtUtc: '2026-10-04T08:30:00Z',
    createdAtUtc: '2026-10-04T08:31:00Z',
    ...teil,
  }
}

describe('Vorbefüllen', () => {
  it('übernimmt Titel, Text, Art und Zeitpunkt (in Ortszeit, Minuten genau)', () => {
    const felder = alsFelder(eintrag())
    expect(felder.title).toBe('Wasserwechsel RDWC 160 L – Blütewoche 7')
    expect(felder.body).toBe('CANNA pH- Pro Bloom: Menge nicht notiert')
    expect(felder.entryType).toBe('ReservoirChange')
    expect(felder.occurredAtLocal).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/)
    // Rückweg: dieselbe Minute wie der gespeicherte Zeitpunkt.
    expect(new Date(felder.occurredAtLocal).getTime()).toBe(new Date('2026-10-04T08:30:00Z').getTime())
  })

  it('macht aus fehlendem Titel oder Text ein leeres Feld statt „null"', () => {
    const felder = alsFelder(eintrag({ title: null, body: null }))
    expect(felder.title).toBe('')
    expect(felder.body).toBe('')
  })
})

describe('Die Anfrage', () => {
  it('schickt den Zeitpunkt nur mit, wenn er geändert wurde', () => {
    const ausgang = alsFelder(eintrag())
    expect(aenderungsAnfrage({ ...ausgang, body: 'neu' }, ausgang).occurredAtLocal).toBeNull()
    expect(aenderungsAnfrage({ ...ausgang, occurredAtLocal: '2026-10-03T18:15' }, ausgang).occurredAtLocal).toBe('2026-10-03T18:15')
  })

  it('schickt einen geleerten Text als "" — null hieße beim Backend „unverändert"', () => {
    const ausgang = alsFelder(eintrag())
    const anfrage = aenderungsAnfrage({ ...ausgang, body: '' }, ausgang)
    expect(anfrage.body).toBe('')
    expect(anfrage.title).toBe(ausgang.title)
  })

  it('lehnt einen Eintrag ohne Titel und Text ab, wie das Backend', () => {
    expect(eintragFehler({ title: ' ', body: '', entryType: 'Note', occurredAtLocal: '2026-10-04T10:00' })).toMatch(/Titel oder Text/)
    expect(eintragFehler({ title: '', body: 'nur Text', entryType: 'Note', occurredAtLocal: '2026-10-04T10:00' })).toBeNull()
  })
})

describe('Die Art bleibt erhalten', () => {
  it('sieht ihre Grundmenge', () => {
    expect(EINTRAG_ARTEN.length).toBeGreaterThanOrEqual(9)
    expect(Object.keys(MEILENSTEIN_ARTEN).length).toBeGreaterThanOrEqual(5)
  })

  it('bietet einen Meilenstein beim Bearbeiten an, statt ihn still zur Beobachtung zu machen', () => {
    for (const art of Object.keys(MEILENSTEIN_ARTEN)) {
      expect(artenFuer(art).map((a) => a.value), art).toContain(art)
    }
    // „+ Eintrag" bleibt ohne Meilensteine.
    expect(artenFuer('Note').map((a) => a.value)).not.toContain('FlipToFlower')
  })

  it('zeigt den Meilenstein im Formular als gewählte Option mit deutschem Namen', () => {
    const html = renderToStaticMarkup(
      <EintragFelderFormular werte={{ ...alsFelder(eintrag()), entryType: 'FlipToFlower' }} onChange={() => {}} />,
    )
    expect(html).toMatch(/<option value="FlipToFlower" selected="">Blüte eingeleitet<\/option>/)
  })
})

describe('Meilensteine und Textfeld', () => {
  it('sperrt bei Meilensteinen den Zeitpunkt — das Datum gehört der Phase', () => {
    expect(istMeilenstein('FlipToFlower')).toBe(true)
    expect(istMeilenstein('ReservoirChange')).toBe(false)
    const html = renderToStaticMarkup(
      <EintragFelderFormular werte={{ ...alsFelder(eintrag()), entryType: 'FlipToFlower' }} zeitpunktGesperrt onChange={() => {}} />,
    )
    expect(html).toMatch(/type="datetime-local"[^>]*disabled=""/)
  })

  it('macht das Textfeld für lange Einzeiler und mehrere Zeilen höher, höchstens 8', () => {
    expect(textZeilen('')).toBe(2)
    expect(textZeilen('a'.repeat(200))).toBeGreaterThanOrEqual(5)
    expect(textZeilen('a\nb\nc')).toBe(4)
    expect(textZeilen('x\n'.repeat(50))).toBe(8)
  })
})

describe('Im Journal-Strom', () => {
  const html = renderToStaticMarkup(
    <JournalStreamSection
      growId="1"
      entries={[eintrag(), eintrag({ id: 20, title: 'Zweiter', entryType: 'Note' })]}
      measurements={[]}
      journalForm={emptyJournalForm()}
      photoForm={emptyPhotoForm()}
      taskForm={emptyTaskForm()}
      saving={null}
      selectedMeasurementId={null}
      onMeasurementSelection={() => {}}
      onJournalFormChange={() => {}}
      onPhotoFormChange={() => {}}
      onTaskFormChange={() => {}}
      onJournalSubmit={() => {}}
      onPhotoSubmit={() => {}}
      onTaskSubmit={() => {}}
    />,
  )

  it('hat jeder Eintrag „Bearbeiten" neben „Entfernen"', () => {
    expect(html.match(/>Bearbeiten<\/button>/g)?.length).toBe(2)
    expect(html.match(/>Entfernen<\/button>/g)?.length).toBe(2)
  })

  it('nennt der Knopf für Screenreader, welchen Eintrag er bearbeitet', () => {
    expect(html).toContain('aria-label="Eintrag „Wasserwechsel RDWC 160 L – Blütewoche 7&quot; bearbeiten"')
    expect(html).toContain('aria-label="Eintrag „Zweiter&quot; bearbeiten"')
  })

  it('zeigt mehrzeiligen Text als Text, nicht als Formular, solange niemand bearbeitet', () => {
    expect(html).not.toContain('data-audit="journal-edit-form"')
    expect(html).toContain('CANNA pH- Pro Bloom: Menge nicht notiert')
  })
})
