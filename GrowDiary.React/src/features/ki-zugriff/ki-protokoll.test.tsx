import { readFileSync, readdirSync } from 'node:fs'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import type { KiProtokollEintragDto, KiSchluesselDto } from '../../types'
import { SchluesselZeile } from './KiZugriffAbschnitt'
import KiProtokoll, { KiProtokollListe, LEER_SATZ } from './KiProtokoll'
import { AKTIONEN, aktionText, ergebnisSchild, haDienstText, kurzerPfad, protokollWeg, schluesselText, zusammenfassen } from './ki-protokoll-logik'

/**
 * „Was die KI zuletzt getan hat" (A-003, Fork AI 03.10.2026) — ohne Browser.
 *
 * Die Regeln:
 * - Aus `POST /api/grows/1/measurements` wird „Messung eingetragen"; jedes
 *   Muster der Zuordnung trifft eine echte Route des Backends.
 * - Aus `ki_stufe_fehlt` wird „abgewiesen: Stufe fehlt" — auf dem Schirm steht
 *   nie ein Fehlercode oder eine Art.
 * - Bei jedem Schlüssel gibt es „Nur diesen zeigen" und zurück „Alle zeigen".
 */

function sichtbarerText(html: string): string {
  return html.replace(/<[^>]+>/g, ' ').replace(/&quot;/g, '"').replace(/&amp;/g, '&').replace(/\s+/g, ' ')
}

function eintrag(teil: Partial<KiProtokollEintragDto> = {}): KiProtokollEintragDto {
  return {
    id: 1,
    zeitpunktUtc: '2026-10-03T18:41:00Z',
    schluesselId: 7,
    schluesselName: 'Claude am Telefon',
    methode: 'POST',
    pfad: '/api/grows/1/measurements',
    status: 201,
    fehlercode: null,
    erfolg: true,
    art: 'ki-zugriff-schreibend',
    beschreibung: 'über KI-Assistent ‚Claude am Telefon‘: POST /api/grows/1/measurements → 201',
    haDienst: null,
    ...teil,
  }
}

function schluessel(teil: Partial<KiSchluesselDto> = {}): KiSchluesselDto {
  return {
    id: 7, name: 'Claude am Telefon', praefix: 'ab12cd34', stufen: ['Dokumentieren'], rueckfrageBei: [],
    erstelltAmUtc: '2026-10-03T08:00:00Z', zuletztGenutztAmUtc: null, gesperrtAmUtc: null, ...teil,
  }
}

/* ------------------------------------------------------------------ */
/* Zuordnung gegen die echten Routen                                   */
/* ------------------------------------------------------------------ */

/**
 * Alle schreibenden Routen aus den Controllern, mit Beispielwerten für die
 * Platzhalter: `{id:int}` → `7`, alles andere → `licht`. Kommentare zählen
 * nicht — eine Route in einem Kommentar ist keine Route.
 */
function schreibendeRouten(): Array<{ methode: string; pfad: string }> {
  const ordner = new URL('../../../../GrowDiary.Web/Api/Controllers/', import.meta.url)
  const dateien = readdirSync(ordner).filter((d) => d.endsWith('.cs'))
  const texte = dateien.map((d) => readFileSync(new URL(d, ordner), 'utf8')
    .split('\n').filter((z) => !z.trimStart().startsWith('//')).join('\n'))

  // Die Basis steht nur in einer Datei einer partiellen Klasse.
  const basisJeKlasse = new Map<string, string>()
  for (const text of texte) {
    const basis = /\[Route\("([^"]+)"\)\][\s\S]*?class (\w+)/.exec(text)
    if (basis) basisJeKlasse.set(basis[2], basis[1])
  }

  const routen: Array<{ methode: string; pfad: string }> = []
  for (const text of texte) {
    const klasse = /class (\w+)/.exec(text)?.[1]
    const basis = klasse ? basisJeKlasse.get(klasse) ?? '' : ''
    for (const t of text.matchAll(/\[Http(Post|Put|Delete|Patch)(?:\("([^"]*)"\))?\]/g)) {
      const vorlage = t[2] ?? ''
      const roh = vorlage.startsWith('/') ? vorlage : [basis, vorlage].filter(Boolean).join('/')
      const pfad = '/' + roh.replace(/^\//, '').replace(/\{[^}:]+:int\}/g, '7').replace(/\{[^}]+\}/g, 'licht')
      routen.push({ methode: t[1].toUpperCase(), pfad })
    }
  }
  return routen
}

describe('Zuordnung Pfad → Aktion', () => {
  const routen = schreibendeRouten()

  it('sieht ihre Grundmengen', () => {
    // Ohne diese Wächter liefe die Prüfung darunter bei leerer Menge null Mal und wäre grün.
    expect(routen.length).toBeGreaterThanOrEqual(150)
    expect(routen).toContainEqual({ methode: 'POST', pfad: '/api/grows/7/measurements' })
    expect(AKTIONEN.length).toBeGreaterThanOrEqual(25)
  })

  it('jedes Muster trifft eine echte Route — eine Zuordnung, die nie greift, ist keine', () => {
    const tot = AKTIONEN.filter((a) => !routen.some((r) => r.methode === a.methode && a.muster.test(r.pfad)))
    expect(tot.map((a) => `${a.methode} ${a.muster} → ${a.getan}`)).toEqual([])
  })

  /** Eine schreibende Anfrage über einen Schlüssel — erledigt, wenn nicht anders gesagt. */
  const aktion = (methode: string | null, pfad: string | null, erfolg = true, art = 'ki-zugriff-schreibend') =>
    aktionText({ art, methode, pfad, erfolg })

  it('macht die häufigen Aktionen lesbar', () => {
    expect(aktion('POST', '/api/grows/1/measurements')).toBe('Messung eingetragen')
    expect(aktion('POST', '/api/grows/12/journal')).toBe('Journal-Eintrag')
    expect(aktion('POST', '/api/dosing/pumps/3/dose')).toBe('Pumpe dosiert')
    expect(aktion('POST', '/api/steuerung/licht/befehl')).toBe('Licht geschaltet')
    expect(aktion('PUT', '/api/measurements/5')).toBe('Messung geändert')
    expect(aktion('DELETE', '/api/measurements/5')).toBe('Messung gelöscht')
  })

  it('was nicht ausgeführt wurde, heisst nicht „geschaltet"', () => {
    expect(aktion('POST', '/api/steuerung/licht/befehl', false)).toBe('Licht schalten')
    expect(aktion('POST', '/api/grows/1/measurements', false)).toBe('Messung eintragen')
    expect(aktion('POST', '/api/dosing/pumps/3/dose', false)).toBe('Pumpe dosieren')
    for (const a of AKTIONEN) expect(a.versuch, `${a.getan}: Versuch-Form fehlt`).toBeTruthy()
  })

  it('nimmt die Methode ernst: dieselbe Route mit anderer Methode ist eine andere Aktion', () => {
    expect(aktion('GET', '/api/grows/1/measurements')).toBe('Gelesen: grows/1/measurements')
    expect(aktion('GET', '/api/settings', false)).toBe('Lesen: settings')
  })

  it('zeigt für Unbekanntes Methode und kurzen Pfad', () => {
    expect(aktion('POST', '/api/grows/1/plan/nacht')).toBe('POST grows/1/plan/nacht')
    expect(kurzerPfad('/api/' + 'a'.repeat(60))).toHaveLength(40)
    expect(kurzerPfad('/api/' + 'a'.repeat(60)).endsWith('…')).toBe(true)
    expect(aktion(null, null)).toBe('Unbekannte Anfrage')
  })

  it('kennt die Einträge, die keine Aktion sind', () => {
    expect(aktion('DELETE', '/api/measurements/5', true, 'ki-sicherung-vorher')).toBe('Sicherung vor der Aktion')
    expect(aktion('POST', '/api/x', false, 'ki-adresse-gesperrt')).toBe('Zu viele falsche Schlüssel')
  })
})

/* ------------------------------------------------------------------ */
/* Ergebnis-Schild                                                     */
/* ------------------------------------------------------------------ */

describe('Ergebnis-Schild', () => {
  it('erledigt bei Erfolg', () => {
    expect(ergebnisSchild(eintrag())).toEqual({ text: 'erledigt', ton: 'ok' })
  })

  it('nach Fehlercode', () => {
    const schild = (fehlercode: string, status: number) => ergebnisSchild(eintrag({ fehlercode, status, erfolg: false })).text
    expect(schild('ki_stufe_fehlt', 403)).toBe('abgewiesen: Stufe fehlt')
    expect(schild('ki_kein_zugriff', 403)).toBe('abgewiesen: nie erlaubt')
    expect(schild('ki_nicht_eingestuft', 403)).toBe('abgewiesen: nicht freigegeben')
    expect(schild('ki_zugriff_aus', 403)).toBe('abgewiesen: Zugriff aus')
    expect(schild('ki_hoechstwert', 422)).toBe('Höchstwert erreicht')
    expect(schild('ki_hoechstwert', 429)).toBe('Höchstwert erreicht')
    expect(schild('ki_sicherung_fehlgeschlagen', 503)).toBe('nicht ausgeführt: Sicherung fehlgeschlagen')
  })

  it('falscher und gesperrter Schlüssel: gleicher Code, verschiedene Schilder', () => {
    expect(ergebnisSchild(eintrag({ fehlercode: 'ki_schluessel_ungueltig', status: 401, erfolg: false, schluesselId: null })).text)
      .toBe('abgewiesen: falscher Schlüssel')
    expect(ergebnisSchild(eintrag({ fehlercode: 'ki_schluessel_ungueltig', status: 401, erfolg: false, schluesselId: 7 })).text)
      .toBe('abgewiesen: Schlüssel gesperrt')
  })

  it('nach Status, wenn kein Code bekannt ist', () => {
    const schild = (status: number) => ergebnisSchild(eintrag({ status, erfolg: status < 400 })).text
    expect(schild(400)).toBe('abgelehnt: Eingabe fehlerhaft')
    expect(schild(404)).toBe('nicht gefunden')
    expect(schild(500)).toBe('fehlgeschlagen')
    expect(schild(403)).toBe('abgewiesen')
  })

  it('alte Einträge ohne Code: die Art sagt, was war', () => {
    expect(ergebnisSchild(eintrag({ art: 'ki-zugriff-aus', status: null, erfolg: false, schluesselId: null })).text).toBe('abgewiesen: Zugriff aus')
    expect(ergebnisSchild(eintrag({ art: 'ki-schluessel-abgewiesen', status: null, erfolg: false, schluesselId: null })).text)
      .toBe('abgewiesen: falscher Schlüssel')
  })

  it('wer es war — auch ohne Schlüssel', () => {
    expect(schluesselText(eintrag())).toBe('Claude am Telefon')
    expect(schluesselText(eintrag({ schluesselName: null }))).toBe('gelöschter Schlüssel')
    expect(schluesselText(eintrag({ schluesselName: null, schluesselId: null }))).toBe('ohne gültigen Schlüssel')
  })

  it('der Weg: ohne Filter keine Abfrage, mit Filter die Schlüssel-Id', () => {
    expect(protokollWeg(null)).toBe('/api/settings/ki-zugriff/protokoll')
    expect(protokollWeg(7)).toBe('/api/settings/ki-zugriff/protokoll?schluesselId=7')
  })
})

/* ------------------------------------------------------------------ */
/* Darstellung                                                         */
/* ------------------------------------------------------------------ */

describe('Die Liste', () => {
  const liste = [
    eintrag({ id: 2, pfad: '/api/steuerung/licht/befehl', status: 403, fehlercode: 'ki_stufe_fehlt', erfolg: false }),
    eintrag({ id: 1 }),
  ]

  it('zeigt Zeit deutsch, Name, Aktion und Ergebnis — nie Bezeichner', () => {
    const html = renderToStaticMarkup(<KiProtokollListe eintraege={liste} filterName={null} />)
    const text = sichtbarerText(html)
    expect(text).toMatch(/\b\d{2}\.10\.2026, \d{2}:\d{2}\b/)
    expect(text).toContain('Claude am Telefon')
    expect(text).toContain('Messung eingetragen')
    expect(text).toContain('erledigt')
    expect(text).toContain('Licht schalten')
    expect(text).not.toContain('Licht geschaltet')
    expect(text).toContain('abgewiesen: Stufe fehlt')
    for (const roh of ['ki_stufe_fehlt', 'ki-zugriff-schreibend', '/api/', 'POST', '→']) {
      expect(text, `„${roh}" steht roh auf dem Schirm`).not.toContain(roh)
    }
    expect(html.split('data-audit="ki-protokoll-eintrag"').length - 1).toBe(2)
  })

  it('leer: der Satz aus dem Auftrag', () => {
    expect(LEER_SATZ).toBe('Noch nichts — sobald ein Assistent etwas einträgt, steht es hier.')
    expect(sichtbarerText(renderToStaticMarkup(<KiProtokollListe eintraege={[]} filterName={null} />))).toContain(LEER_SATZ)
    expect(sichtbarerText(renderToStaticMarkup(<KiProtokollListe eintraege={[]} filterName="Claude" />)))
      .toContain('Von „Claude" steht noch nichts hier.')
  })

  it('beim ersten Zeichnen: Überschrift, Laden — und mit Filter der Weg zurück', () => {
    const ohne = sichtbarerText(renderToStaticMarkup(<KiProtokoll filter={null} onFilter={() => {}} />))
    expect(ohne).toContain('Was die KI zuletzt getan hat')
    expect(ohne).not.toContain('Alle zeigen')

    const mit = sichtbarerText(renderToStaticMarkup(<KiProtokoll filter={{ id: 7, name: 'Claude am Telefon' }} onFilter={() => {}} />))
    expect(mit).toContain('Nur „Claude am Telefon"')
    expect(mit).toContain('Alle zeigen')
  })
})

describe('„Nur diesen zeigen" in der Schlüsselliste', () => {
  const zeile = (nurDieser: boolean) => renderToStaticMarkup(
    <SchluesselZeile eintrag={schluessel()} bearbeitet={false} onStufenAendern={() => {}} onSperren={() => {}} onLoeschen={() => {}}
      nurDieser={nurDieser} onNurDiesenZeigen={() => {}} />,
  )

  it('jeder Schlüssel hat den Knopf, auch ein gesperrter', () => {
    expect(sichtbarerText(zeile(false))).toContain('Nur diesen zeigen')
    expect(zeile(false)).toContain('aria-pressed="false"')
    const gesperrt = renderToStaticMarkup(
      <SchluesselZeile eintrag={schluessel({ gesperrtAmUtc: '2026-10-03T09:00:00Z' })} bearbeitet={false}
        onStufenAendern={() => {}} onSperren={() => {}} onLoeschen={() => {}} onNurDiesenZeigen={() => {}} />,
    )
    expect(sichtbarerText(gesperrt)).toContain('Nur diesen zeigen')
  })

  it('ist er gewählt, führt derselbe Knopf zurück zu allen', () => {
    expect(sichtbarerText(zeile(true))).toContain('Alle zeigen')
    expect(sichtbarerText(zeile(true))).not.toContain('Nur diesen zeigen')
    expect(zeile(true)).toContain('aria-pressed="true"')
  })
})

describe('Gleiche Einträge hintereinander', () => {
  const falsch = (id: number) => eintrag({ id, schluesselId: null, schluesselName: null, methode: 'GET', pfad: '/api/ki-zugriff/ich', status: 401, fehlercode: 'ki_schluessel_ungueltig', erfolg: false })

  it('fasst zehn gleiche Abweisungen zu einer Zeile mit Anzahl zusammen', () => {
    const liste = Array.from({ length: 10 }, (_, i) => falsch(100 - i))
    const gruppen = zusammenfassen(liste)
    expect(gruppen).toHaveLength(1)
    expect(gruppen[0].anzahl).toBe(10)
    expect(gruppen[0].eintrag.id).toBe(100)
    const html = renderToStaticMarkup(<KiProtokollListe eintraege={liste} filterName={null} />)
    expect(html.match(/data-audit="ki-protokoll-eintrag"/g)).toHaveLength(1)
    expect(html).toContain('10× hintereinander')
  })

  it('trennt, sobald etwas anderes dazwischen steht — die Reihenfolge bleibt', () => {
    const anders = eintrag({ id: 50 })
    const gruppen = zusammenfassen([falsch(3), falsch(2), anders, falsch(1)])
    expect(gruppen.map((g) => g.anzahl)).toEqual([2, 1, 1])
    expect(gruppen[1].eintrag.id).toBe(50)
  })

  it('eine einzelne Zeile trägt keine Anzahl', () => {
    const html = renderToStaticMarkup(<KiProtokollListe eintraege={[falsch(1)]} filterName={null} />)
    expect(html).not.toContain('hintereinander')
  })
})

/* ------------------------------------------------------------------ */
/* Home Assistant über den Fork (Prüferbefund 04.10.2026)              */
/* ------------------------------------------------------------------ */

describe('Home Assistant im Protokoll', () => {
  const ha = (haDienst: string, teil: Partial<KiProtokollEintragDto> = {}) => eintrag({
    methode: 'POST', pfad: '/api/ki-ha/dienst', status: 200, haDienst,
    beschreibung: `über KI-Assistent ‚Claude am Telefon‘: POST /api/ki-ha/dienst → 200 (Home Assistant: ${haDienst})`, ...teil,
  })

  it('zeigt Dienst und Entität statt „POST ki-ha/dienst"', () => {
    expect(aktionText(ha('light.turn_on → light.zelt'))).toBe('Home Assistant: light.turn_on (light.zelt)')
    expect(aktionText(ha('scene.turn_on'))).toBe('Home Assistant: scene.turn_on')
    expect(aktionText(ha('zha.issue_zigbee_cluster_command', { status: 403, fehlercode: 'ki_kein_zugriff', erfolg: false })))
      .toBe('Home Assistant: zha.issue_zigbee_cluster_command — abgewiesen')
    expect(haDienstText('light.turn_on → light.zelt', false, 503)).toBe('Home Assistant: light.turn_on (light.zelt) — nicht ausgeführt')
    // Ohne Angabe (andere Wege, alte Einträge) bleibt alles wie bisher.
    expect(aktionText(eintrag({ methode: 'POST', pfad: '/api/ki-ha/dienst' }))).toBe('POST ki-ha/dienst')
  })

  it('rendert die Zeile lesbar — ohne Pfeil, Pfad oder Code', () => {
    const liste = [
      ha('lock.unlock → lock.haustuer', { id: 3, status: 403, fehlercode: 'ki_kein_zugriff', erfolg: false }),
      ha('light.turn_on → light.zelt', { id: 2 }),
    ]
    const text = sichtbarerText(renderToStaticMarkup(<KiProtokollListe eintraege={liste} filterName={null} />))
    expect(text).toContain('Home Assistant: light.turn_on (light.zelt)')
    expect(text).toContain('Home Assistant: lock.unlock (lock.haustuer) — abgewiesen')
    expect(text).toContain('abgewiesen: nie erlaubt')
    for (const roh of ['→', '/api/', 'ki-ha/dienst', 'ki_kein_zugriff', 'POST']) {
      expect(text, `„${roh}" steht roh auf dem Schirm`).not.toContain(roh)
    }
  })

  it('fasst nur gleiche Dienste zusammen — verschiedene Dienste oder Entitäten bleiben getrennt', () => {
    const gruppen = zusammenfassen([
      ha('light.turn_on → light.zelt', { id: 6 }),
      ha('light.turn_on → light.zelt', { id: 5 }),
      ha('light.turn_off → light.zelt', { id: 4 }),
      ha('light.turn_off → light.flur', { id: 3 }),
      ha('switch.turn_on → switch.pumpe', { id: 2 }),
    ])
    expect(gruppen.map((g) => [g.eintrag.haDienst, g.anzahl])).toEqual([
      ['light.turn_on → light.zelt', 2],
      ['light.turn_off → light.zelt', 1],
      ['light.turn_off → light.flur', 1],
      ['switch.turn_on → switch.pumpe', 1],
    ])
  })
})
