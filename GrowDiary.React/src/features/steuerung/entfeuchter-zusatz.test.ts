import { describe, expect, it } from 'vitest'
import {
  HILFE_VORGABEN,
  aenderungBilden,
  aenderungsListe,
  entwurfNachfuehren,
  ersteBand,
  feuchteBand,
  fehlerZu,
  hilfeErkennen,
  hilfeNachEinzelwert,
  hilfeText,
  hilfeWaehlen,
  hoechstEmpfehlung,
  istGeaendert,
  leereFelder,
  namenAenderung,
  planHinweisZeigen,
  statusHinweis,
  tempGrenzen,
  temperaturBand,
  unbekannteFehler,
  vpdBand,
  wirksamerName,
  zustandWort,
} from './entfeuchter-zusatz'
import type { EntfeuchterZusatzEinstellungen, EntfeuchterZusatzLive } from './steuerung-typen'

/** Bru, 06.10.2026: Modus „fest" mit 26,5 / 25, Hilfsstärke normal. */
const BRU: EntfeuchterZusatzEinstellungen = {
  hilfe: 'normal',
  automatikAktiv: true,
  tagbetriebErlauben: true,
  nachtDurchlaufen: true,
  vpdHystereseKpa: 0.15,
  zuschaltVerzoegerungMin: 10,
  folgeAbstandK: 1,
  wiederEinAbstandK: 1,
  mindestlaufzeitMin: 15,
  mindestpauseMin: 10,
  meldung: { aktiv: true, grenzeW: 60, dauerMin: 5, wiederholungH: 2 },
  ablauf: 'tank',
  tempMaxTagModus: 'fest', tempMaxTagAbstandK: 6.5, tempMaxTagFestC: 26.5,
  tempMaxNachtModus: 'fest', tempMaxNachtAbstandK: 9, tempMaxNachtFestC: 25,
}

/** Der Beispielstand aus VERTRAG.md. */
const LIVE: EntfeuchterZusatzLive = {
  haErreichbar: true, fuehrungName: 'RDWC Dehumi', zusatzName: 'Dehumi RDWC Tent',
  planWoche: 'Blütewoche 7', planLuftTagC: 20, planLuftNachtC: 16,
  tempMaxTagC: 26.5, tempMaxNachtC: 25,
  folgeAusTagC: 25.5, folgeAusNachtC: 24, wiederEinTagC: 24.5, wiederEinNachtC: 23,
  tempC: 25.1, feuchteProzent: 55.2, vpd: 1.31, tagPhase: true,
  schaltgroesse: 'vpd', vpdZiel: 1.4, vpdEinSchwelle: 1.25, vpdAusSchwelle: 1.55,
  feuchteEinProzent: 39, feuchteAusProzent: 35,
  zusatzAn: true, zusatzOnline: true, leistungW: 313, energieHeuteKwh: 2.9,
  fuehrungAn: true, ziehtNichts: false, planUnvollstaendig: false, automatikAn: true,
}

describe('Hilfsstärke', () => {
  it('erkennt die drei Voreinstellungen an den Einzelwerten', () => {
    expect(hilfeErkennen(HILFE_VORGABEN.sparsam)).toBe('sparsam')
    expect(hilfeErkennen(HILFE_VORGABEN.normal)).toBe('normal')
    expect(hilfeErkennen(HILFE_VORGABEN.kraeftig)).toBe('kraeftig')
  })

  it('nennt alles andere „eigene Werte" — schon ein einziger abweichender Wert reicht', () => {
    expect(hilfeErkennen({ ...HILFE_VORGABEN.normal, zuschaltVerzoegerungMin: 11 })).toBe('eigene')
    expect(hilfeErkennen({ ...HILFE_VORGABEN.sparsam, vpdHystereseKpa: 0.2 })).toBe('eigene')
  })

  it('rechnet mit Gleitkomma-Rauschen wie mit dem Wert (0,1 + 0,05 ≠ 0,15 exakt)', () => {
    expect(hilfeErkennen({ ...HILFE_VORGABEN.normal, vpdHystereseKpa: 0.1 + 0.05 })).toBe('normal')
  })

  it('hält die Tabelle aus ENTSCHEIDUNGEN.md, Punkt 3', () => {
    expect(HILFE_VORGABEN.sparsam).toEqual({ folgeAbstandK: 1.5, wiederEinAbstandK: 1, vpdHystereseKpa: 0.25, zuschaltVerzoegerungMin: 20, mindestpauseMin: 15 })
    expect(HILFE_VORGABEN.normal).toEqual({ folgeAbstandK: 1, wiederEinAbstandK: 1, vpdHystereseKpa: 0.15, zuschaltVerzoegerungMin: 10, mindestpauseMin: 10 })
    expect(HILFE_VORGABEN.kraeftig).toEqual({ folgeAbstandK: 0.5, wiederEinAbstandK: 0.5, vpdHystereseKpa: 0.1, zuschaltVerzoegerungMin: 5, mindestpauseMin: 10 })
  })

  it('setzt mit einer Stufe die fünf Einzelwerte — und sonst nichts', () => {
    const e = hilfeWaehlen(BRU, BRU, 'sparsam')
    expect(e).toMatchObject({ hilfe: 'sparsam', ...HILFE_VORGABEN.sparsam })
    expect(e.mindestlaufzeitMin).toBe(15)
    expect(e.tempMaxTagFestC).toBe(26.5)
  })

  it('lässt bei „aus" die Einzelwerte wieder auf den gespeicherten Stand zurückfallen', () => {
    const sparsam = hilfeWaehlen(BRU, BRU, 'sparsam')
    const aus = hilfeWaehlen(sparsam, BRU, 'aus')
    expect(aus.hilfe).toBe('aus')
    expect(aenderungBilden(BRU, aus)).toEqual({ hilfe: 'aus' })
  })

  it('folgt einem geänderten Einzelwert: passt er zu einer Stufe, heißt sie so, sonst „eigene Werte"', () => {
    const eigene = hilfeNachEinzelwert({ ...BRU, zuschaltVerzoegerungMin: 12 })
    expect(eigene.hilfe).toBe('eigene')
    const zurueck = hilfeNachEinzelwert({ ...eigene, zuschaltVerzoegerungMin: 10 })
    expect(zurueck.hilfe).toBe('normal')
    const kraeftig = hilfeNachEinzelwert({ ...BRU, ...HILFE_VORGABEN.kraeftig })
    expect(kraeftig.hilfe).toBe('kraeftig')
  })

  it('bleibt bei „aus", auch wenn ein Einzelwert geändert wird', () => {
    expect(hilfeNachEinzelwert({ ...BRU, hilfe: 'aus', mindestpauseMin: 30 }).hilfe).toBe('aus')
  })

  it('schreibt die Texte aus der Tabelle (sparsam 1,5 K / 20 min, kräftig 0,5 K / 5 min)', () => {
    expect(hilfeText('sparsam', 'X')).toContain('1,5 K früher aus')
    expect(hilfeText('sparsam', 'X')).toContain('erst nach 20 min')
    expect(hilfeText('kraeftig', 'X')).toContain('0,5 K früher aus')
    expect(hilfeText('kraeftig', 'X')).toContain('nach 5 min')
    expect(hilfeText('normal', 'X')).toContain('1,0 K früher aus')
    expect(hilfeText('aus', 'RDWC Dehumi')).toBe('Nur RDWC Dehumi entfeuchtet. Der Zusatz bleibt aus.')
  })
})

describe('Speichern schickt nur geänderte Felder', () => {
  it('schickt ohne Änderung nichts', () => {
    expect(aenderungBilden(BRU, { ...BRU, meldung: { ...BRU.meldung } })).toEqual({})
    expect(istGeaendert(BRU, BRU)).toBe(false)
  })

  it('schickt bei einem geänderten Feld genau dieses Feld (06.10.2026: Tag 26,5 darf nicht auf 29 springen)', () => {
    const koerper = aenderungBilden(BRU, { ...BRU, tempMaxNachtFestC: 24 })
    expect(koerper).toEqual({ tempMaxNachtFestC: 24 })
    expect(koerper).not.toHaveProperty('tempMaxTagFestC')
    expect(istGeaendert(BRU, { ...BRU, tempMaxNachtFestC: 24 })).toBe(true)
  })

  it('schickt von der Gruppe „meldung" nur das geänderte Teilfeld', () => {
    const koerper = aenderungBilden(BRU, { ...BRU, meldung: { ...BRU.meldung, grenzeW: 80 } })
    expect(koerper).toEqual({ meldung: { grenzeW: 80 } })
  })

  it('schickt mit einer gewählten Stufe die Stufe und die Einzelwerte, die sich ändern', () => {
    const koerper = aenderungBilden(BRU, hilfeWaehlen(BRU, BRU, 'sparsam'))
    expect(koerper).toEqual({
      hilfe: 'sparsam', folgeAbstandK: 1.5, vpdHystereseKpa: 0.25, zuschaltVerzoegerungMin: 20, mindestpauseMin: 15,
    })
    expect(koerper).not.toHaveProperty('wiederEinAbstandK')
  })

  it('schickt „eigene Werte" nie — das ist ein Befund des Servers', () => {
    const koerper = aenderungBilden(BRU, hilfeNachEinzelwert({ ...BRU, mindestpauseMin: 12 }))
    expect(koerper).toEqual({ mindestpauseMin: 12 })
  })

  it('nennt „eigene Werte" nur, wenn der Zusatz vorher aus war — sonst bliebe er mit den neuen Werten aus', () => {
    const aus = { ...BRU, hilfe: 'aus' as const }
    const aenderung = { ...hilfeWaehlen(aus, aus, 'sparsam'), mindestpauseMin: 12 }
    const koerper = aenderungBilden(aus, hilfeNachEinzelwert(aenderung))
    expect(koerper.hilfe).toBe('eigene')
  })

  it('ein Entwurf, der wieder dem Stand gleicht, schickt nichts', () => {
    const verstellt = hilfeNachEinzelwert({ ...BRU, mindestpauseMin: 12 })
    const zurueck = hilfeNachEinzelwert({ ...verstellt, mindestpauseMin: 10 })
    expect(aenderungBilden(BRU, zurueck)).toEqual({})
  })

  it('zweimal speichern: der zweite Körper vergleicht mit dem Stand der ersten Antwort', () => {
    // Erster Durchgang: Tag auf 27.
    const erster = aenderungBilden(BRU, { ...BRU, tempMaxTagFestC: 27 })
    expect(erster).toEqual({ tempMaxTagFestC: 27 })
    // Die Antwort des Servers ist der neue Bezug.
    const antwort = { ...BRU, tempMaxTagFestC: 27 }
    // Zweiter Durchgang: Nacht auf 24 — Tag steht schon in der Antwort und fehlt im Körper.
    expect(aenderungBilden(antwort, { ...antwort, tempMaxNachtFestC: 24 })).toEqual({ tempMaxNachtFestC: 24 })
  })
})

describe('Änderungsliste („Wird gespeichert — nur das:")', () => {
  const plan = { tag: 20, nacht: 16 }

  it('ist leer ohne Änderung', () => {
    expect(aenderungsListe(BRU, BRU, plan)).toEqual([])
  })

  it('nennt nur das geänderte Feld, mit alt und neu', () => {
    const zeilen = aenderungsListe(BRU, { ...BRU, tempMaxTagFestC: 27 }, plan)
    expect(zeilen).toHaveLength(1)
    expect(zeilen[0]).toMatchObject({ feld: 'tempMaxTagFestC', label: 'Höchsttemperatur tagsüber (fest)', von: '26,5', nach: '27,0 °C' })
  })

  it('rechnet im Modus „Plan +" die Höchsttemperatur dazu (20 + 7 = 27)', () => {
    const plus = { ...BRU, tempMaxTagModus: 'plan' as const }
    const zeilen = aenderungsListe(plus, { ...plus, tempMaxTagAbstandK: 7 }, plan)
    expect(zeilen).toHaveLength(1)
    expect(zeilen[0]).toMatchObject({ feld: 'tempMaxTagAbstandK', von: '+6,5', nach: '+7,0 K', folge: 'Höchsttemperatur 27,0 °C' })
  })

  it('fasst die Einzelwerte einer gewählten Stufe in der Zeile „Hilfsstärke" zusammen', () => {
    const zeilen = aenderungsListe(BRU, hilfeWaehlen(BRU, BRU, 'sparsam'), plan)
    expect(zeilen.map((z) => z.feld)).toEqual(['hilfe'])
    expect(zeilen[0]).toMatchObject({ von: 'normal', nach: 'sparsam' })
    expect(zeilen[0].folge).toContain('Zusatz geht früher aus: 1,0 → 1,5 K')
    expect(zeilen[0].folge).toContain('Zuschalten nach: 10 → 20 min')
    expect(zeilen[0].folge).not.toContain('Wieder einschalten')
  })

  it('listet bei „eigene Werte" die Einzelwerte einzeln', () => {
    const eigene = hilfeNachEinzelwert({ ...BRU, mindestpauseMin: 12 })
    const zeilen = aenderungsListe(BRU, eigene, plan)
    expect(zeilen.map((z) => z.feld).sort()).toEqual(['hilfe', 'mindestpauseMin'])
    expect(zeilen.find((z) => z.feld === 'hilfe')).toMatchObject({ von: 'normal', nach: 'eigene Werte' })
  })

  it('nennt zu jedem Feld im PUT-Körper eine Zeile (Kasten und Körper kommen aus demselben Vergleich)', () => {
    const entwurf = {
      ...hilfeWaehlen(BRU, BRU, 'kraeftig'),
      tempMaxNachtFestC: 24, automatikAktiv: false, mindestlaufzeitMin: 20, ablauf: 'schlauch' as const,
      meldung: { aktiv: false, grenzeW: 80, dauerMin: 6, wiederholungH: 3 },
    }
    const koerper = aenderungBilden(BRU, entwurf)
    const gedeckt = aenderungsListe(BRU, entwurf, plan).map((z) => z.feld)
    for (const feld of Object.keys(koerper)) {
      if (feld === 'meldung') {
        for (const teil of Object.keys(koerper.meldung!)) expect(gedeckt, `meldung.${teil}`).toContain(`meldung.${teil}`)
      } else if (['folgeAbstandK', 'wiederEinAbstandK', 'vpdHystereseKpa', 'zuschaltVerzoegerungMin', 'mindestpauseMin'].includes(feld)) {
        expect(gedeckt, `${feld} steht in der Zeile Hilfsstärke`).toContain('hilfe')
      } else {
        expect(gedeckt, feld).toContain(feld)
      }
    }
    expect(Object.keys(koerper).length).toBeGreaterThan(8)
  })
})

describe('Ein frischer Stand und ein angefangener Entwurf', () => {
  it('Felder, die nicht angefasst wurden, folgen dem neuen Stand', () => {
    const neu = { ...BRU, tempMaxTagFestC: 27 }
    const entwurf = { ...BRU, mindestlaufzeitMin: 20 }
    const ergebnis = entwurfNachfuehren(BRU, neu, entwurf)
    expect(ergebnis.tempMaxTagFestC).toBe(27)
    expect(ergebnis.mindestlaufzeitMin).toBe(20)
    // Und das nächste Speichern schickt nur noch, was der Nutzer selbst angefasst hat.
    expect(aenderungBilden(neu, ergebnis)).toEqual({ mindestlaufzeitMin: 20 })
  })

  it('der Stand von woanders wird nicht mit dem alten Wert überschrieben (06.10.2026)', () => {
    const neu = { ...BRU, tempMaxNachtFestC: 23 }
    const ergebnis = entwurfNachfuehren(BRU, neu, { ...BRU, tempMaxTagFestC: 27 })
    expect(aenderungBilden(neu, ergebnis)).toEqual({ tempMaxTagFestC: 27 })
  })

  it('führt auch die Teilfelder der Meldung getrennt nach', () => {
    const neu = { ...BRU, meldung: { ...BRU.meldung, dauerMin: 9 } }
    const entwurf = { ...BRU, meldung: { ...BRU.meldung, grenzeW: 80 } }
    expect(entwurfNachfuehren(BRU, neu, entwurf).meldung).toEqual({ aktiv: true, grenzeW: 80, dauerMin: 9, wiederholungH: 2 })
  })
})

describe('Höchsttemperatur und Folge-Rechnung', () => {
  it('rechnet Tag und Nacht wie im Beispiel des Vertrags (26,5 → 25,5 → 24,5; 25 → 24 → 23)', () => {
    expect(tempGrenzen(BRU, 26.5)).toEqual({ max: 26.5, folgeAus: 25.5, wiederEin: 24.5 })
    expect(tempGrenzen(BRU, 25)).toEqual({ max: 25, folgeAus: 24, wiederEin: 23 })
    expect(tempGrenzen(BRU, 26.5)).toMatchObject({ folgeAus: LIVE.folgeAusTagC, wiederEin: LIVE.wiederEinTagC })
    expect(tempGrenzen(BRU, 25)).toMatchObject({ folgeAus: LIVE.folgeAusNachtC, wiederEin: LIVE.wiederEinNachtC })
  })

  it('folgt den Abständen: kräftig 0,5 K / 0,5 K, sparsam 1,5 K / 1 K', () => {
    expect(tempGrenzen(HILFE_VORGABEN.kraeftig, 26.5)).toEqual({ max: 26.5, folgeAus: 26, wiederEin: 25.5 })
    expect(tempGrenzen(HILFE_VORGABEN.sparsam, 26.5)).toEqual({ max: 26.5, folgeAus: 25, wiederEin: 24 })
  })

  it('rundet ohne Gleitkomma-Reste (26,7 − 0,5 = 26,2)', () => {
    expect(tempGrenzen({ folgeAbstandK: 0.5, wiederEinAbstandK: 0.5 }, 26.7).folgeAus).toBe(26.2)
  })

  it('empfiehlt im Modus „Plan +" den Aufschlag, im Modus „fest" die Temperatur daraus', () => {
    const plus = { ...BRU, tempMaxTagModus: 'plan' as const, tempMaxTagAbstandK: 7 }
    expect(hoechstEmpfehlung('tag', plus, 20)).toMatchObject({ text: 'Empfohlen: +6,5 K', gleich: false, feld: 'tempMaxTagAbstandK', wert: 6.5 })
    expect(hoechstEmpfehlung('tag', BRU, 20)).toMatchObject({ text: 'Empfohlen: 26,5 °C', gleich: true, feld: 'tempMaxTagFestC', wert: 26.5 })
    expect(hoechstEmpfehlung('nacht', BRU, 16)).toMatchObject({ text: 'Empfohlen: 25,0 °C', gleich: true })
    expect(hoechstEmpfehlung('nacht', { ...BRU, tempMaxNachtFestC: 29 }, 16)).toMatchObject({ gleich: false, wert: 25 })
  })

  it('erfindet ohne Plan keine Empfehlung für den festen Wert', () => {
    expect(hoechstEmpfehlung('tag', BRU, null)).toBeNull()
  })
})

describe('Bänder', () => {
  it('zeichnet das Temperatur-Band mit „an", „Zusatz" und „Haupt" in dieser Reihenfolge', () => {
    const band = temperaturBand(tempGrenzen(BRU, 26.5), 25.1, 'RDWC Dehumi', 'Dehumi RDWC Tent')
    expect(band.marken.map((m) => [m.label, m.wert])).toEqual([['an', 24.5], ['Zusatz', 25.5], ['Haupt', 26.5]])
    const pos = band.marken.map((m) => m.pos)
    expect(pos[0]).toBeLessThan(pos[1])
    expect(pos[1]).toBeLessThan(pos[2])
    expect(band.istWarm).toBe(false)
    expect(band.kurz).toBe('Dehumi RDWC Tent aus 25,5 · RDWC Dehumi aus 26,5')
  })

  it('färbt den Istwert warm, sobald das Zelt über „Zusatz aus" liegt — und weitet die Skala dafür', () => {
    const band = temperaturBand(tempGrenzen(BRU, 26.5), 27.6, 'A', 'B')
    expect(band.istWarm).toBe(true)
    expect(band.bis).toBeGreaterThanOrEqual(28.6)
    expect(band.ist).toBeLessThanOrEqual(100)
  })

  it('zeigt ohne Temperaturmesswert keinen Punkt statt einer erfundenen Null', () => {
    const band = temperaturBand(tempGrenzen(BRU, 26.5), null, 'A', 'B')
    expect(band.ist).toBeNull()
    expect(band.istWarm).toBe(false)
  })

  it('rechnet das VPD-Band aus Ziel ± Abstand des Entwurfs (1,40 ± 0,15 = 1,25 / 1,55)', () => {
    const band = vpdBand(LIVE, 0.15)!
    expect(band.marken.map((m) => [m.label, m.wert])).toEqual([['EIN', 1.25], ['Plan', 1.4], ['AUS', 1.55]])
    expect(band.kurz).toBe('EIN unter 1,25 · AUS über 1,55')
    expect(vpdBand(LIVE, 0.25)!.marken.map((m) => m.wert)).toEqual([1.15, 1.4, 1.65])
  })

  it('nimmt ohne Ziel die Schwellen des Servers und ohne beides gar kein Band', () => {
    const ohneZiel = vpdBand({ ...LIVE, vpdZiel: null }, 0.15)!
    expect(ohneZiel.marken.map((m) => m.label)).toEqual(['EIN', 'AUS'])
    expect(ohneZiel.marken.map((m) => m.wert)).toEqual([1.25, 1.55])
    expect(vpdBand({ ...LIVE, vpdZiel: null, vpdEinSchwelle: null, vpdAusSchwelle: null }, 0.15)).toBeNull()
  })

  it('zeichnet das Feuchte-Band aus den Plan-Schwellen (EIN 39 über, AUS 35 unter)', () => {
    const band = feuchteBand(LIVE)!
    expect(band.kurz).toBe('EIN über 39 · AUS unter 35')
    expect(band.von).toBeLessThanOrEqual(31)
    expect(band.bis).toBeGreaterThanOrEqual(59)
    expect(feuchteBand({ ...LIVE, feuchteEinProzent: null })).toBeNull()
  })

  it('wählt tagsüber die Schaltgröße des Forks und nachts mit „Nachts durchlaufen" die Plan-Feuchte', () => {
    expect(ersteBand(LIVE, BRU).art).toBe('vpd')
    expect(ersteBand({ ...LIVE, schaltgroesse: 'feuchte' }, BRU).art).toBe('feuchte')
    expect(ersteBand({ ...LIVE, tagPhase: false }, BRU).art).toBe('feuchte')
    expect(ersteBand({ ...LIVE, tagPhase: false }, { ...BRU, nachtDurchlaufen: false }).art).toBe('vpd')
    expect(ersteBand({ ...LIVE, schaltgroesse: 'keine' }, BRU)).toEqual({ band: null, art: null })
  })
})

describe('Zustandswort und Hinweis', () => {
  const geladen = { hilfe: 'normal' as const, automatikAktiv: true }

  it('sagt, was gerade ist', () => {
    expect(zustandWort(LIVE, geladen, false)).toEqual({ text: 'entfeuchtet', ton: 'an' })
    expect(zustandWort({ ...LIVE, zusatzAn: false }, geladen, false)).toEqual({ text: 'bereit', ton: 'neutral' })
    expect(zustandWort({ ...LIVE, zusatzAn: false }, geladen, true)).toEqual({ text: 'zu warm — aus', ton: 'warn' })
    expect(zustandWort({ ...LIVE, ziehtNichts: true }, geladen, false)).toEqual({ text: 'an, zieht nichts', ton: 'warn' })
    expect(zustandWort({ ...LIVE, zusatzOnline: false }, geladen, false).text).toBe('offline')
  })

  it('„zieht nichts" hängt allein am Befund des Servers, nicht am Schaltzustand', () => {
    expect(zustandWort({ ...LIVE, ziehtNichts: true, zusatzAn: false }, geladen, false)).toEqual({ text: 'an, zieht nichts', ton: 'warn' })
    expect(zustandWort({ ...LIVE, ziehtNichts: false, leistungW: 3 }, geladen, false).text).toBe('entfeuchtet')
    expect(statusHinweis({ ...LIVE, ziehtNichts: false, leistungW: 3 }, BRU, tempGrenzen(BRU, 26.5), ersteBand(LIVE, BRU))).not.toContain('Zieht nichts')
  })

  it('stellt die Ursache als Frage, nicht als Tatsache', () => {
    const text = statusHinweis({ ...LIVE, ziehtNichts: true }, BRU, tempGrenzen(BRU, 26.5), ersteBand(LIVE, BRU))
    expect(text).toContain('Tank voll oder Gerät ausgeschaltet?')
  })

  it('sagt „unbekannt", wenn Home Assistant den Zustand nicht liefert', () => {
    expect(zustandWort({ ...LIVE, zusatzAn: null }, geladen, false).text).toBe('Zustand unbekannt')
  })

  it('geht vor mit Automatik aus und Hilfe aus', () => {
    expect(zustandWort(LIVE, { ...geladen, automatikAktiv: false }, false).text).toBe('Automatik aus')
    expect(zustandWort({ ...LIVE, automatikAn: false }, geladen, false).text).toBe('Automatik aus')
    expect(zustandWort(LIVE, { ...geladen, hilfe: 'aus' }, false).text).toBe('Zusatz aus (Hilfe aus)')
  })

  it('schreibt den Hinweis nur mit Werten, die es gibt', () => {
    const g = tempGrenzen(BRU, 26.5)
    const eins = ersteBand(LIVE, BRU)
    expect(statusHinweis(LIVE, BRU, g, eins)).toBe('Läuft, bis das VPD über 1,55 kPa steigt — frühestens nach 15 min. Dazwischen bleibt er, wie er ist.')
    expect(statusHinweis({ ...LIVE, zusatzAn: false }, BRU, g, eins)).toBe('Steht, bis das VPD unter 1,25 kPa fällt (nach 10 min Pause) und das Zelt kühler als 24,5 °C ist.')
    expect(statusHinweis({ ...LIVE, zusatzAn: null }, BRU, g, eins)).toBe('')
    expect(statusHinweis({ ...LIVE, ziehtNichts: true }, BRU, g, eins)).toContain('unter 60 W')
    expect(statusHinweis({ ...LIVE, tagPhase: false }, BRU, g, ersteBand({ ...LIVE, tagPhase: false }, BRU)))
      .toContain('Nachts durchlaufen: er läuft, bis das Zelt über 25,5 °C steigt')
  })
})

describe('Warnungen', () => {
  it('„Plan unvollständig" erscheint nie zusammen mit „Home Assistant antwortet nicht"', () => {
    expect(planHinweisZeigen({ haErreichbar: true, planUnvollstaendig: true })).toBe(true)
    expect(planHinweisZeigen({ haErreichbar: false, planUnvollstaendig: true })).toBe(false)
    expect(planHinweisZeigen({ haErreichbar: true, planUnvollstaendig: false })).toBe(false)
    expect(planHinweisZeigen({ haErreichbar: true, planUnvollstaendig: null })).toBe(false)
  })
})

describe('Felder und Fehler', () => {
  it('findet leere Zahlenfelder, auch in der Gruppe „meldung", mit den Namen des Servers', () => {
    expect(leereFelder(BRU)).toBeNull()
    expect(leereFelder({ ...BRU, mindestpauseMin: Number.NaN, meldung: { ...BRU.meldung, grenzeW: Number.NaN } }))
      .toEqual({ MindestpauseMin: 'Bitte eine Zahl eintragen.', 'Meldung.GrenzeW': 'Bitte eine Zahl eintragen.' })
  })

  it('findet einen Feldfehler in jeder Schreibweise des Servers', () => {
    expect(fehlerZu({ MindestpauseMin: 'zu klein' }, 'mindestpauseMin')).toBe('zu klein')
    expect(fehlerZu({ 'Meldung.GrenzeW': 'zu groß' }, 'meldung.grenzeW')).toBe('zu groß')
    expect(fehlerZu({ 'Meldung.grenzeW': 'zu groß' }, 'meldung.grenzeW')).toBe('zu groß')
    expect(fehlerZu({ MeldungGrenzeW: 'zu groß' }, 'meldung.grenzeW')).toBe('zu groß')
    expect(fehlerZu({ MindestpauseMin: 'zu klein' }, 'mindestlaufzeitMin')).toBeUndefined()
  })

  it('meldet Feldfehler, die kein Feld der Seite trifft, damit sie niemand übersieht', () => {
    expect(unbekannteFehler({ MindestpauseMin: 'a', 'Meldung.GrenzeW': 'b', Unbekannt: 'c' })).toEqual(['Unbekannt: c'])
  })
})

describe('Namen', () => {
  const namen = {
    fuehrung: { anzeigename: 'RDWC Dehumi', vorgabe: 'RDWC Dehumi' },
    zusatz: { anzeigename: 'Zelt-Trotec', vorgabe: 'Dehumi RDWC Tent' },
  }

  it('schickt nichts, wenn sich kein Name ändert', () => {
    expect(namenAenderung(namen, { fuehrung: 'RDWC Dehumi', zusatz: 'Zelt-Trotec' })).toEqual({})
  })

  it('schickt nur den geänderten Namen', () => {
    expect(namenAenderung(namen, { fuehrung: 'Hauptgerät', zusatz: 'Zelt-Trotec' })).toEqual({ fuehrung: 'Hauptgerät' })
  })

  it('schickt leer, wenn ein eigener Name zurück auf die Vorgabe soll', () => {
    expect(namenAenderung(namen, { fuehrung: 'RDWC Dehumi', zusatz: '  ' })).toEqual({ zusatz: '' })
  })

  it('ein leeres Feld bei einem Namen, der schon die Vorgabe ist, ändert nichts', () => {
    expect(namenAenderung(namen, { fuehrung: '', zusatz: 'Zelt-Trotec' })).toEqual({})
    expect(wirksamerName('', 'Vorgabe')).toBe('Vorgabe')
    expect(wirksamerName(' Eigen ', 'Vorgabe')).toBe('Eigen')
  })
})
