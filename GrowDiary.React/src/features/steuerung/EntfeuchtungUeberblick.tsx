import { V1Card } from '../../components/v1'
import { tempMax, zahl } from './entfeuchter-band'
import { tempGrenzen, zusatzTempOk } from './entfeuchter-zusatz'
import { hauptStatus, zusatzStatus } from './entfeuchtung-modell'
import { Lesen } from './EntfeuchtungBausteine'
import type { Ctx } from './EntfeuchtungBausteine'
import { Bremsen, Herkunft, Kontext, Lage, MessKopf, Warum, ZielBandAnzeige, ZielZeile, Zusammenspiel } from './EntfeuchterUeberblick'
import type { WarumZeile } from './EntfeuchterUeberblick'
import { RF_KNAPP_PUNKTE, ZONEN_WORT, schlechtereZone } from './entfeuchter-zonen'
import type { Zone } from './entfeuchter-zonen'
import {
  abweichungsWort,
  feuchteBand,
  feuchteZoneImBand,
  kontextPlaketten,
  kurzeZahl,
  temperaturBand,
  temperaturZoneImBand,
  zielText,
} from './entfeuchtung-ziel'
import { KACHEL_HOECHST, KACHEL_ZIEL } from './EntfeuchtungEinstellungen'
import { Klappkachel } from './Klappkachel'
import type { ZielwerteKurz } from './useZielwerteKurz'
import './steuerung.css'

/** So viele Kelvin unter der Untergrenze der Temperatur gelten noch als „knapp daneben". */
const TEMP_RAND_UNTEN_K = 2

const urteil = (zone: Zone | null): 'ok' | 'warn' | 'crit' | null => (zone == null ? null : zone === 'ziel' ? 'ok' : zone === 'knapp' ? 'warn' : 'crit')

/**
 * Fork AI (A-015, A-021): Reiter „Überblick" — nur zum Lesen, nichts zum Eingeben.
 *
 * Aufbau (08.10. und 13.10.2026): Lagemeldung · Messwerte und Zonen EINMAL für das Zelt, mit den
 * Plaketten für Licht und Woche oben (sie gelten für beides), je Messwert die große Zahl mit dem Urteil in
 * Worten, die ZIEL-Zeile, das Band mit zwei Seiten (zu feucht UND zu trocken, zu warm UND zu kühl) und
 * darunter eine Zeile „Woher kommt das?" · Geräte mit nur ihrem Status · „Warum läuft welches Gerät?" ·
 * die Erklärung der Zusammenarbeit.
 */
export function UeberblickTab({ h, z, haupt, zusatz, zielwerte, onSpringen }: Ctx & {
  zielwerte: ZielwerteKurz | null
  onSpringen: (kachel: string) => void
}) {
  const hl = h.seite!.live
  const a = h.anzeige!
  const zl = z?.seite?.live ?? null
  const za = z?.anzeige ?? null

  const tagMax = tempMax(a.tempMaxTagModus, a.tempMaxTagAbstandK, a.tempMaxTagFestC, hl.planLuftTagC)
  const nachtMax = tempMax(a.tempMaxNachtModus, a.tempMaxNachtAbstandK, a.tempMaxNachtFestC, hl.planLuftNachtC)
  const phase = hl.tagPhase === true ? 'Tag' : hl.tagPhase === false ? 'Nacht' : null
  const aktivMax = phase === 'Nacht' ? nachtMax : tagMax
  const grenzen = za ? tempGrenzen(za, aktivMax) : null
  const planLuft = phase === 'Nacht' ? hl.planLuftNachtC : hl.planLuftTagC

  // Luftfeuchte: Obergrenze aus dem Plan (ohne Plan die EIN-Schwelle), Untergrenze aus dem Grenzwert.
  const feuchteOben = hl.grenzeAktivProzent ?? hl.rhObergrenzeProzent ?? hl.einAktivProzent
  const fBand = feuchteBand(feuchteOben, zielwerte?.feuchteRegel, hl.tagPhase)
  const fZone = feuchteZoneImBand(hl.feuchteProzent, fBand)

  // Temperatur: Grenzen des Grenzwerts (folgt dem Plan); ohne ihn die alte Regel — im Ziel bis zum früheren
  // Abschalten des Zusatzes, ohne Zusatz eine Stufe unter der Höchsttemperatur. Nie über der Höchsttemperatur.
  const tRoh = temperaturBand(planLuft, zielwerte?.temperaturRegel, hl.tagPhase)
  const tBand = tRoh.unten == null && tRoh.oben == null
    ? { unten: null, oben: grenzen ? grenzen.folgeAus : aktivMax - 1 }
    : { unten: tRoh.unten, oben: Math.min(tRoh.oben ?? aktivMax, aktivMax) }
  const tZone = temperaturZoneImBand(hl.tempC, tBand, aktivMax, TEMP_RAND_UNTEN_K)
  const lage = schlechtereZone(fZone, tZone)

  const fWort = abweichungsWort('feuchte', hl.feuchteProzent, fBand, fZone)
  const tWort = abweichungsWort('temperatur', hl.tempC, tBand, tZone)
  const lageText = [
    fZone && `Luftfeuchte ${zahl(hl.feuchteProzent)} % — ${fWort}${zielText(fBand) ? ` (Ziel ${zielText(fBand)} %)` : ''}`,
    tZone && `Temperatur ${zahl(hl.tempC)} °C — ${tWort}${zielText(tBand) ? ` (Ziel ${zielText(tBand)} °C)` : ''}, die Entfeuchter dürfen bis ${zahl(aktivMax)} °C laufen`,
  ].filter(Boolean).join(' · ')

  const feuchteWerte = [hl.feuchteProzent, fBand.unten, fBand.oben, fBand.oben == null ? null : fBand.oben + RF_KNAPP_PUNKTE].filter((w): w is number => w != null && Number.isFinite(w))
  const fVon = feuchteWerte.length ? Math.floor(Math.min(...feuchteWerte) - 3) : 40
  const fBis = feuchteWerte.length ? Math.ceil(Math.max(...feuchteWerte) + 3) : 70
  const tWerte = [hl.tempC, tBand.unten, tBand.oben, aktivMax].filter((w): w is number => w != null && Number.isFinite(w))
  const tVon = Math.floor(Math.min(...tWerte) - 2)
  const tBis = Math.ceil(Math.max(...tWerte) + 2)

  // „wartet": über der Abschalt-Temperatur immer; im Bereich darunter, aber noch nicht unter der Wieder-ein-Temperatur, nur wenn die Feuchte
  // den Zusatz auch wollen würde (über der Einschalt-Schwelle) und er nicht nach VPD geregelt wird — sonst steht er aus anderem Grund.
  const feuchteWillZusatz = hl.feuchteProzent != null && hl.einAktivProzent != null && hl.feuchteProzent > hl.einAktivProzent && zl?.schaltgroesse !== 'vpd'
  const wartet = Boolean(zl && grenzen && zl.zusatzAn !== true && z?.geladen?.hilfe !== 'aus'
    && ((zl.tempC != null && zl.tempC > grenzen.folgeAus) || (feuchteWillZusatz && zusatzTempOk(zl.tempC, false, grenzen) === false)))
  const geraete: Array<{ name: string; rolle: string; status: string }> = [
    { name: haupt, rolle: z ? 'Haupt-Entfeuchter · führt' : 'Entfeuchter', status: hauptStatus(hl) },
    ...(z && zl ? [{ name: zusatz, rolle: 'Zusatz-Entfeuchter · hilft', status: zusatzStatus(zl, wartet) }] : []),
  ]

  const bremsen = hl.bremsen ?? []

  const warum: WarumZeile[] = [
    { frage: `${haupt}: Feuchte über der EIN-Schwelle`, antwort: `${zahl(hl.feuchteProzent)} % · EIN ab ${zahl(hl.einAktivProzent)} %`, ok: hl.feuchteProzent == null || hl.einAktivProzent == null ? null : hl.feuchteProzent > hl.einAktivProzent },
    { frage: `${haupt}: Zelt unter der Höchsttemperatur (${zahl(aktivMax)} °C)`, antwort: `${zahl(hl.tempC)} °C`, ok: hl.tempC == null ? null : hl.tempC < aktivMax },
    ...(phase === 'Tag' ? [{ frage: `${haupt}: Tagbetrieb erlaubt`, antwort: a.tagbetriebErlauben ? 'ja — er darf auch bei Licht an laufen' : 'nein — nur in der Dunkelphase', ok: a.tagbetriebErlauben }] : []),
    ...(zl && grenzen && z?.geladen
      ? [
          { frage: `${zusatz}: ${haupt} läuft`, antwort: zl.fuehrungAn == null ? 'unbekannt' : zl.fuehrungAn ? 'ja — der Zusatz darf mithelfen' : 'nein — der Zusatz startet sofort, wenn nötig', ok: zl.fuehrungAn },
          { frage: `${zusatz}: Zelt nicht zu warm (geht ab ${zahl(grenzen.folgeAus)} °C aus)`, antwort: `${zahl(zl.tempC)} °C · wieder an unter ${zahl(grenzen.wiederEin)} °C`, ok: zusatzTempOk(zl.tempC, zl.zusatzAn === true, grenzen) },
          { frage: `${zusatz}: Hilfe und Automatik an`, antwort: z.geladen.hilfe === 'aus' ? 'Hilfe steht auf „aus".' : z.geladen.automatikAktiv ? 'ja' : 'Automatik ist aus.', ok: z.geladen.hilfe !== 'aus' && z.geladen.automatikAktiv },
        ]
      : []),
  ]

  // Woher kommt das? — eine Zeile je Quelle, nur was wirklich bekannt ist.
  const planText = ['Plan', zielwerte?.growName && `Grow ${zielwerte.growName}`, zielwerte?.programm, zielwerte?.woche ? `Woche ${zielwerte.woche}` : hl.planWoche ? `Woche ${hl.planWoche}` : null]
    .filter(Boolean).join(' · ')
  // Die gerade wirksamen Schwellen kommen nicht immer vom Nutzer: bei VPD aus dem VPD-Band des Plans, und liegt der Deckel
  // (Plan-Obergrenze minus Klima-Abstand) unter der eingestellten Einschalt-Schwelle, begrenzt er sie.
  const eingestelltEin = phase === 'Nacht' ? a.feuchteEinNacht : a.feuchteEinTag
  const einAusQuelle = a.vpdRegelung
    ? 'aus dem VPD-Band des Plans'
    : a.arbeitsbereichQuelle === 'plan' && hl.rhObergrenzeProzent != null
      ? `aus dem Plan (Obergrenze ${zahl(hl.grenzeAktivProzent ?? hl.rhObergrenzeProzent, 0)} % minus Vorlauf ${zahl(a.vorlaufProzent, a.vorlaufProzent % 1 ? 1 : 0)} %)`
      : hl.deckelProzent != null && eingestelltEin > hl.deckelProzent
      ? `vom Plan gedeckelt (Obergrenze minus Klima-Abstand = ${zahl(hl.deckelProzent, 0)} %)`
      : 'von dir, unter Einstellungen'
  const feuchteHerkunft = [
    ...(fBand.oben == null ? [] : [{ wert: `Obergrenze ${kurzeZahl(fBand.oben)} %${hl.rhObergrenzeNachtProzent != null && hl.rhObergrenzeProzent != null && hl.rhObergrenzeNachtProzent !== hl.rhObergrenzeProzent ? ` (${phase === 'Nacht' ? 'Nacht' : 'Tag'})` : ''}`, quelle: hl.rhObergrenzeProzent == null ? 'aus der Einschalt-Schwelle (kein Plan-Wert)' : planText }]),
    ...(fBand.unten == null ? [] : [{ wert: `Untergrenze ${kurzeZahl(fBand.unten)} %`, quelle: 'dein Grenzwert' }]),
    ...(hl.einAktivProzent == null ? [] : [{ wert: `Ein ${zahl(hl.einAktivProzent, 0)} / Aus ${zahl(hl.ausAktivProzent, 0)} %`, quelle: einAusQuelle }]),
  ]
  const toleranz = zielwerte?.temperaturRegel?.toleranz
  const temperaturHerkunft = [
    ...(planLuft == null ? [] : [{ wert: `Ziel ${kurzeZahl(planLuft)} °C`, quelle: `${planText} · Plan-Luft${phase === 'Nacht' ? ' Nacht' : ''}` }]),
    ...(tBand.unten != null && zielwerte?.temperaturRegel
      ? [{ wert: `Spielraum${toleranz ? ` ± ${kurzeZahl(toleranz)} K` : ''} (${zielText(tBand)} °C)`, quelle: `dein Grenzwert${zielwerte.temperaturQuelle ? ` · ${zielwerte.temperaturQuelle}` : ''}` }]
      : []),
    { wert: `Höchsttemperatur ${zahl(aktivMax)} °C`, quelle: (phase === 'Nacht' ? a.tempMaxNachtModus : a.tempMaxTagModus) === 'plan' ? 'aus dem Plan (Plan-Luft + Abstand)' : 'von dir eingestellt (fest)' },
  ]

  return (
    <>
      {lage && <Lage zone={lage} titel="Entfeuchtung" text={lageText} />}

      <Klappkachel titel="Messwerte & Zonen (Zelt)" zusammenfassung={`${zahl(hl.feuchteProzent)} % rF · ${zahl(hl.tempC)} °C${phase ? ` · ${phase}` : ''}`}>
        <V1Card className="ef-band">
          <Kontext plaketten={kontextPlaketten(hl.tagPhase, zielwerte?.woche ?? hl.planWoche)} />

          <div className="ef-block">
            <MessKopf
              wert={zahl(hl.feuchteProzent)}
              einheit="% rF"
              zone={fZone}
              zustand={fWort}
              ton={fZone === 'kritisch' ? 'kritisch' : fZone === 'knapp' ? 'warn' : undefined}
              beiwerk=""
            />
            {zielText(fBand) && <ZielZeile text={zielText(fBand)!} einheit="%" />}
            {fBand.oben != null && (
              <ZielBandAnzeige
                von={fVon} bis={fBis} zielVon={fBand.unten} zielBis={fBand.oben}
                grenzen={[...(fBand.unten == null ? [] : [fBand.unten]), fBand.oben + RF_KNAPP_PUNKTE]}
                ist={hl.feuchteProzent} urteil={urteil(fZone)}
              />
            )}
            <Herkunft
              zeilen={feuchteHerkunft}
              links={[{ text: 'Plan öffnen', to: '/plan' }, { text: 'Grenzwerte einstellen', to: '/grenzwerte' }, { text: 'Ein-/Ausschalten einstellen', springeZu: KACHEL_ZIEL }]}
              onSpringen={onSpringen}
            />
          </div>

          <div className="ef-block">
            <MessKopf
              wert={zahl(hl.tempC)}
              einheit="°C"
              zone={tZone}
              zustand={tWort}
              ton={tZone === 'kritisch' ? 'kritisch' : tZone === 'knapp' ? 'warn' : undefined}
              beiwerk={`Entfeuchter dürfen bis ${zahl(aktivMax)} °C`}
            />
            {zielText(tBand) && <ZielZeile text={zielText(tBand)!} einheit="°C" />}
            <ZielBandAnzeige
              von={tVon} bis={tBis} zielVon={tBand.unten} zielBis={tBand.oben}
              grenzen={[...(tBand.unten == null ? [] : [tBand.unten]), aktivMax]}
              ist={hl.tempC} urteil={urteil(tZone)}
            />
            <Herkunft
              zeilen={temperaturHerkunft}
              links={[{ text: 'Plan öffnen', to: '/plan' }, { text: 'Grenzwerte einstellen', to: '/grenzwerte' }, { text: 'Höchsttemperatur einstellen', springeZu: KACHEL_HOECHST }]}
              onSpringen={onSpringen}
            />
          </div>

          <Herkunft
            titel="So liest du das Band"
            zeilen={[
              { wert: 'Grün', quelle: 'Ziel — hier gibt es keine Abweichung' },
              { wert: 'Gelbe Striche', quelle: `Grenzen; bei ${zahl(aktivMax)} °C gehen alle Entfeuchter aus${grenzen ? `, der Zusatz schon bei ${zahl(grenzen.folgeAus)} °C` : ''}` },
              { wert: 'Zu trocken oder zu kühl', quelle: 'zählt genauso als Abweichung wie zu feucht oder zu warm' },
            ]}
          />
          <div className="ef-legende">
            <span><i className="is-ziel" />{ZONEN_WORT.ziel}</span>
            <span><i className="is-knapp" />{ZONEN_WORT.knapp}</span>
            <span><i className="is-kritisch" />{ZONEN_WORT.kritisch}</span>
          </div>
        </V1Card>
      </Klappkachel>

      <Klappkachel
        titel="Was bremst gerade?"
        zusammenfassung={bremsen.length === 0 ? 'nichts' : bremsen.length === 1 ? '1 Hinweis' : `${bremsen.length} Hinweise`}
        offen={bremsen.length > 0}
      >
        <Bremsen bremsen={bremsen} />
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
