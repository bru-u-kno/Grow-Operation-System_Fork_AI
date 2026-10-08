import { V1Card } from '../../components/v1'
import { tempMax, zahl } from './entfeuchter-band'
import { tempGrenzen } from './entfeuchter-zusatz'
import { hauptStatus, zusatzStatus } from './entfeuchtung-modell'
import { Lesen } from './EntfeuchtungBausteine'
import type { Ctx } from './EntfeuchtungBausteine'
import { Lage, MessKopf, Warum, ZonenLegende, ZonenSkala, Zusammenspiel } from './EntfeuchterUeberblick'
import type { WarumZeile } from './EntfeuchterUeberblick'
import { RF_KNAPP_PUNKTE, ZONEN_WORT, feuchteZone, schlechtereZone, temperaturZone } from './entfeuchter-zonen'
import { Klappkachel } from './Klappkachel'
import './steuerung.css'

/**
 * Fork AI (A-015): Reiter „Überblick" — nur zum Lesen, nichts zum Eingeben.
 *
 * Aufbau (Bru, 08.10.2026): Lagemeldung · Messwerte und Zonen EINMAL für das
 * Zelt (sie sind für alle Geräte dieselben) · Geräte mit nur ihrem Status, ohne
 * Leistung · „Warum läuft welches Gerät?" · die Erklärung der Zusammenarbeit.
 */
export function UeberblickTab({ h, z, haupt, zusatz }: Ctx) {
  const hl = h.seite!.live
  const a = h.anzeige!
  const zl = z?.seite?.live ?? null
  const za = z?.anzeige ?? null

  const tagMax = tempMax(a.tempMaxTagModus, a.tempMaxTagAbstandK, a.tempMaxTagFestC, hl.planLuftTagC)
  const nachtMax = tempMax(a.tempMaxNachtModus, a.tempMaxNachtAbstandK, a.tempMaxNachtFestC, hl.planLuftNachtC)
  const phase = hl.tagPhase === true ? 'Tag' : hl.tagPhase === false ? 'Nacht' : null
  const aktivMax = phase === 'Nacht' ? nachtMax : tagMax
  const grenzen = za ? tempGrenzen(za, aktivMax) : null

  // Zonen: Ziel der Luftfeuchte ist die Plan-Obergrenze (ohne Plan die EIN-Schwelle); die Temperatur ist im Ziel
  // bis zum früheren Abschalten des Zusatzes (ohne Zusatz: eine Stufe unter der Höchsttemperatur).
  const feuchteZiel = hl.rhObergrenzeProzent ?? hl.einAktivProzent
  const fZone = feuchteZone(hl.feuchteProzent, feuchteZiel)
  const tZielBis = grenzen ? grenzen.folgeAus : aktivMax - 1
  const tZone = temperaturZone(hl.tempC, aktivMax, tZielBis)
  const lage = schlechtereZone(fZone, tZone)
  const lageText = [
    fZone && `Luftfeuchte ${zahl(hl.feuchteProzent)} % (${ZONEN_WORT[fZone]}${feuchteZiel == null ? '' : `, Ziel ${zahl(feuchteZiel, 0)} %`})`,
    tZone && `Temperatur ${zahl(hl.tempC)} °C (${ZONEN_WORT[tZone]}, Höchsttemperatur ${zahl(aktivMax)} °C)`,
  ].filter(Boolean).join(' · ')

  const feuchteWerte = [hl.feuchteProzent, feuchteZiel, hl.einAktivProzent, hl.ausAktivProzent].filter((w): w is number => w != null && Number.isFinite(w))
  const fVon = feuchteWerte.length ? Math.floor(Math.min(...feuchteWerte) - 3) : 40
  const fBis = feuchteWerte.length ? Math.ceil(Math.max(...feuchteWerte, (feuchteZiel ?? 0) + RF_KNAPP_PUNKTE) + 3) : 70
  const tVon = Math.floor(Math.min(hl.tempC ?? aktivMax, grenzen?.wiederEin ?? aktivMax, aktivMax) - 3)
  const tBis = Math.ceil(Math.max(hl.tempC ?? aktivMax, aktivMax) + 2)

  const wartet = Boolean(zl && grenzen && zl.tempC != null && zl.tempC > grenzen.folgeAus && zl.zusatzAn !== true && z?.geladen?.hilfe !== 'aus')
  const geraete: Array<{ name: string; rolle: string; status: string }> = [
    { name: haupt, rolle: z ? 'Haupt-Entfeuchter · führt' : 'Entfeuchter', status: hauptStatus(hl) },
    ...(z && zl ? [{ name: zusatz, rolle: 'Zusatz-Entfeuchter · hilft', status: zusatzStatus(zl, wartet) }] : []),
  ]

  const warum: WarumZeile[] = [
    { frage: `${haupt}: Feuchte über der EIN-Schwelle`, antwort: `${zahl(hl.feuchteProzent)} % · EIN ab ${zahl(hl.einAktivProzent)} %`, ok: hl.feuchteProzent == null || hl.einAktivProzent == null ? null : hl.feuchteProzent > hl.einAktivProzent },
    { frage: `${haupt}: Zelt unter der Höchsttemperatur (${zahl(aktivMax)} °C)`, antwort: `${zahl(hl.tempC)} °C`, ok: hl.tempC == null ? null : hl.tempC < aktivMax },
    ...(phase === 'Tag' ? [{ frage: `${haupt}: Tagbetrieb erlaubt`, antwort: a.tagbetriebErlauben ? 'ja — er darf auch bei Licht an laufen' : 'nein — nur in der Dunkelphase', ok: a.tagbetriebErlauben }] : []),
    ...(zl && grenzen && z?.geladen
      ? [
          { frage: `${zusatz}: ${haupt} läuft`, antwort: zl.fuehrungAn == null ? 'unbekannt' : zl.fuehrungAn ? 'ja — der Zusatz darf mithelfen' : 'nein — der Zusatz startet sofort, wenn nötig', ok: zl.fuehrungAn },
          { frage: `${zusatz}: Zelt nicht zu warm (geht ab ${zahl(grenzen.folgeAus)} °C aus)`, antwort: `${zahl(zl.tempC)} °C · wieder an unter ${zahl(grenzen.wiederEin)} °C`, ok: zl.tempC == null ? null : zl.tempC <= grenzen.folgeAus },
          { frage: `${zusatz}: Hilfe und Automatik an`, antwort: z.geladen.hilfe === 'aus' ? 'Hilfe steht auf „aus".' : z.geladen.automatikAktiv ? 'ja' : 'Automatik ist aus.', ok: z.geladen.hilfe !== 'aus' && z.geladen.automatikAktiv },
        ]
      : []),
  ]

  return (
    <>
      {lage && <Lage zone={lage} titel="Entfeuchtung" text={lageText} />}

      <Klappkachel titel="Messwerte & Zonen (Zelt)" zusammenfassung={`${zahl(hl.feuchteProzent)} % rF · ${zahl(hl.tempC)} °C${phase ? ` · ${phase}` : ''}`}>
        <V1Card className="ef-band">
          <div className="ef-block">
            <MessKopf
              wert={zahl(hl.feuchteProzent)}
              einheit="% rF"
              zone={fZone}
              zustand={fZone ? ZONEN_WORT[fZone] : 'keine Aussage'}
              ton={fZone === 'kritisch' ? 'kritisch' : fZone === 'knapp' ? 'warn' : undefined}
              beiwerk={`VPD ${zahl(hl.vpd, 2)} kPa${phase ? ` · ${phase}` : ''}`}
            />
            {feuchteZiel != null && (
              <ZonenSkala
                von={fVon} bis={fBis} zielBis={feuchteZiel} knappBis={feuchteZiel + RF_KNAPP_PUNKTE}
                marken={[
                  ...(hl.ausAktivProzent == null ? [] : [{ wert: hl.ausAktivProzent, label: 'AUS' }]),
                  ...(hl.einAktivProzent == null ? [] : [{ wert: hl.einAktivProzent, label: 'EIN' }]),
                  { wert: feuchteZiel, label: 'Ziel' },
                ]}
                ist={hl.feuchteProzent} zone={fZone} einheit="%"
              />
            )}
          </div>
          <div className="ef-block">
            <MessKopf
              wert={zahl(hl.tempC)}
              einheit="°C"
              zone={tZone}
              zustand={tZone === 'kritisch' ? 'zu warm — alle aus' : tZone ? ZONEN_WORT[tZone] : 'keine Aussage'}
              ton={tZone === 'kritisch' ? 'kritisch' : tZone === 'knapp' ? 'warn' : undefined}
              beiwerk={`Höchsttemperatur ${zahl(aktivMax)} °C${phase ? ` · ${phase}` : ''}`}
            />
            <ZonenSkala
              von={tVon} bis={tBis} zielBis={tZielBis} knappBis={aktivMax}
              marken={grenzen
                ? [{ wert: grenzen.wiederEin, label: 'an' }, { wert: grenzen.folgeAus, label: 'Zusatz' }, { wert: grenzen.max, label: 'Haupt' }]
                : [{ wert: tZielBis, label: 'Ziel bis' }, { wert: aktivMax, label: 'Höchst' }]}
              ist={hl.tempC} zone={tZone} einheit="°C"
            />
          </div>
          <ZonenLegende />
          <p className="st-hinweis">
            {hl.portAn === true
              ? `${haupt} läuft, bis die Feuchte unter ${zahl(hl.ausAktivProzent)} % fällt — frühestens nach ${a.mindestlaufzeitMin} min Laufzeit.`
              : `${haupt} springt an, wenn die Feuchte über ${zahl(hl.einAktivProzent)} % steigt.`}
            {a.vpdRegelung && hl.vpdUnten != null && hl.vpdOben != null
              ? ` Schwellen aus dem VPD-Band ${zahl(hl.vpdUnten, 2)}–${zahl(hl.vpdOben, 2)}, EIN gedeckelt von der Plan-Feuchte.`
              : a.vpdRegelung
                ? ' Feste Schwellen (Rückfallebene).'
                : ' Regelgröße Luftfeuchte: EIN gedeckelt von der Plan-Feuchte.'}
          </p>
        </V1Card>
      </Klappkachel>

      <Klappkachel titel="Geräte" zusammenfassung={geraete.map((g) => `${g.name} ${g.status}`).join(' · ')}>
        <V1Card>
          {geraete.map((g) => (
            <div key={g.name} className="st-feldzeile">
              <span className="st-etikett">{g.name}<small>{g.rolle}</small></span>
              <span className="st-nurlesen">{g.status}</span>
            </div>
          ))}
        </V1Card>
      </Klappkachel>

      <Warum an={hl.portAn} zeilen={warum} titel="Warum läuft welches Gerät?" />

      {z && grenzen && z.anzeige && (
        <Zusammenspiel
          haupt={haupt} zusatz={zusatz} tempMax={grenzen.max}
          abstaende={{ folgeAus: grenzen.folgeAus, wiederEin: grenzen.wiederEin, mindestpauseMin: z.anzeige.mindestpauseMin }}
        />
      )}

      {zl && (zl.energieHeuteKwh != null || zl.leistungW != null) && (
        <Klappkachel titel={`Verbrauch heute · ${zusatz}`} zusammenfassung={zl.energieHeuteKwh == null ? '' : `${zahl(zl.energieHeuteKwh)} kWh`} offen={false}>
          <V1Card>
            {zl.energieHeuteKwh != null && <Lesen label="Energie heute" wert={`${zahl(zl.energieHeuteKwh)} kWh`} />}
            {zl.leistungW != null && <Lesen label="Leistung jetzt" wert={`${zahl(zl.leistungW, 0)} W`} />}
          </V1Card>
        </Klappkachel>
      )}
    </>
  )
}
