import { useState } from 'react'
import { V1Alert, V1Card, V1LinkButton, V1Switch } from '../../components/v1'
import { tempMax, zahl } from './entfeuchter-band'
import { HILFE_STUFEN, fehlerZu, hilfeText, hilfeWaehlen } from './entfeuchter-zusatz'
import { Lesen } from './EntfeuchtungBausteine'
import type { Ctx } from './EntfeuchtungBausteine'
import { ZielBandAnzeige } from './EntfeuchterUeberblick'
import {
  SCHWELLE_MAX,
  abstandAnwenden,
  aussenluftNachEinschalten,
  einMinimum,
  einSchwelleSetzen,
  schwellenGetrennt,
} from './entfeuchtung-schwellen'
import { STUFEN, STUFEN_AUSWAHL, stufeErkennen, stufeFuerHaupt, stufeFuerZusatz } from './entfeuchtung-stufen'
import type { StufenName } from './entfeuchtung-stufen'
import { kurzeZahl } from './entfeuchtung-ziel'
import { Klappkachel } from './Klappkachel'
import { TempMaxBlock, Zahl } from './SteuerungsFelder'
import type { ZielwerteKurz } from './useZielwerteKurz'
import './steuerung.css'

/** Die Kacheln dieses Reiters; der Überblick springt über diese Kennungen hierher. */
export const KACHEL_ZIEL = 'ziel'
export const KACHEL_HOECHST = 'hoechst'

/** Wahl für BEIDE Geräte; zeigt „Unterschiedlich", solange die zwei Geräte verschieden stehen. */
type Gemeinsam = 'an' | 'aus' | 'unterschiedlich'
const gemeinsam = (haupt: boolean, zusatz: boolean | null): Gemeinsam => (zusatz == null || zusatz === haupt ? (haupt ? 'an' : 'aus') : 'unterschiedlich')

/**
 * Fork AI (A-021): Reiter „Einstellungen" — ersetzt die Reiter „Regel" und „Schutz".
 *
 * <b>Anlass (Bru, 13.10.2026).</b> Rund 36 Einzelwerte, doppelt für beide Geräte, mit Abhängigkeiten
 * untereinander. Jetzt: je Frage EINE Antwort für beide Geräte, die Einzelwerte unter „Erweitert".
 * Jeder Wert bleibt änderbar — was nicht oben steht, steht unter „Erweitert“; gespeichert wird weiter über die zwei bekannten Wege (Hauptentfeuchter
 * ersetzt alles, Zusatz nur Geändertes) mit EINEM Knopf.
 *
 * Reihenfolge: Betrieb · Ziel · Höchsttemperatur · Stufe · Zusatz-Stärke · Meldungen · Erweitert.
 */
export function EinstellungenTab({ h, z, haupt, zusatz, zielwerte }: Ctx & { zielwerte: ZielwerteKurz | null }) {
  const he = h.entwurf!
  const hl = h.seite!.live
  const anzeige = h.anzeige ?? he
  const ze = z?.entwurf ?? null
  const zl = z?.seite?.live ?? null
  const vpd = he.vpdRegelung

  // Einschalt-Schwelle: ein Wert für Tag und Nacht, solange sie gleich stehen; sonst von Anfang an zwei.
  const [getrennt, setGetrennt] = useState(() => schwellenGetrennt(he))

  // ------------------------------------------------------------------ Betrieb
  const automatik = gemeinsam(he.automatikAktiv, ze?.automatikAktiv ?? null)
  const tagbetrieb = gemeinsam(he.tagbetriebErlauben, ze?.tagbetriebErlauben ?? null)
  const setzTagbetrieb = (an: boolean) => { h.setz('tagbetriebErlauben', an); z?.setz('tagbetriebErlauben', an) }

  // ------------------------------------------------------------------ Ziel
  const planOben = hl.rhObergrenzeProzent
  const aus = he.feuchteAusTag
  const ausN = he.feuchteAusNacht
  const bandVon = Math.floor(Math.min(aus, ausN, he.feuchteEinTag, planOben ?? 100) - 4)
  const bandBis = Math.ceil(Math.max(he.feuchteEinTag, he.feuchteEinNacht, planOben ?? 0) + 4)
  const einHinweis = hl.deckelProzent == null
    ? 'Ab dieser Luftfeuchte schalten die Entfeuchter ein.'
    : `Ab dieser Luftfeuchte schalten die Entfeuchter ein — höchstens ${zahl(hl.deckelProzent, 0)} % (Plan-Obergrenze minus Klima-Abstand), darüber begrenzt der Plan.`

  // ------------------------------------------------------------------ Höchsttemperatur
  const tagMax = tempMax(anzeige.tempMaxTagModus, anzeige.tempMaxTagAbstandK, anzeige.tempMaxTagFestC, hl.planLuftTagC)
  const nachtMax = tempMax(anzeige.tempMaxNachtModus, anzeige.tempMaxNachtAbstandK, anzeige.tempMaxNachtFestC, hl.planLuftNachtC)
  const co2Grenze = hl.co2CanopyGrenzeC
  const ueberCo2 = co2Grenze != null && (tagMax > co2Grenze || nachtMax > co2Grenze)

  // ------------------------------------------------------------------ Stufe
  const stufe = stufeErkennen(he, ze)
  const stufeWaehlen = (name: StufenName) => {
    const haupt2 = stufeFuerHaupt(name, he)
    h.setzMehrere({ ...haupt2, ...abstandAnwenden(he, haupt2.hystereseProzent) })
    z?.setz('mindestlaufzeitMin', stufeFuerZusatz(name).mindestlaufzeitMin)
  }

  // ------------------------------------------------------------------ Meldungen
  const feuchteRegel = zielwerte?.feuchteRegel ?? null
  const tempRegel = zielwerte?.temperaturRegel ?? null
  const bereich = (min: number | null | undefined, max: number | null | undefined, einheit: string) =>
    min != null && max != null ? `${kurzeZahl(min)} – ${kurzeZahl(max)} ${einheit}` : max != null ? `≤ ${kurzeZahl(max)} ${einheit}` : min != null ? `≥ ${kurzeZahl(min)} ${einheit}` : '–'

  return (
    <>
      {/* ----------------------------------------------------------------- Betrieb */}
      <Klappkachel
        titel="Betrieb"
        zusammenfassung={`Automatik ${automatik === 'an' ? 'an' : automatik === 'aus' ? 'aus' : 'teils'} · ${tagbetrieb === 'an' ? 'Tag und Nacht' : tagbetrieb === 'aus' ? 'nur nachts' : 'unterschiedlich'}`}
      >
        <V1Card>
          <V1Switch
            label="Automatik"
            checked={automatik === 'an'}
            onChange={(an) => { h.setz('automatikAktiv', an); z?.setz('automatikAktiv', an) }}
            hint={`Aus hält die Regelung für ${z ? 'alle Entfeuchter' : 'den Entfeuchter'} an; ${z ? 'sie bleiben' : 'er bleibt'}, wie ${z ? 'sie' : 'er'} gerade ${z ? 'stehen' : 'steht'}.${automatik === 'unterschiedlich' ? ` Gerade ist nur ein Gerät an — dieser Schalter setzt beide.` : ''}`}
          />
          <div className="st-feldzeile is-gestapelt">
            <span className="st-etikett">
              Wann darf entfeuchtet werden?
              <small>{z ? 'Gilt für alle Entfeuchter. ' : ''}„Nur nachts“: tagsüber (Licht an) {z ? 'bleiben sie' : 'bleibt er'} aus.{tagbetrieb === 'unterschiedlich' ? ` Gerade stehen die Geräte verschieden — eine Wahl setzt beide.` : ''}</small>
            </span>
            <div className="ef-stufen" role="radiogroup" aria-label="Wann darf entfeuchtet werden?">
              <button type="button" role="radio" className="st-chip" aria-checked={tagbetrieb === 'an'} aria-current={tagbetrieb === 'an'} onClick={() => setzTagbetrieb(true)}>Tag und Nacht</button>
              <button type="button" role="radio" className="st-chip" aria-checked={tagbetrieb === 'aus'} aria-current={tagbetrieb === 'aus'} onClick={() => setzTagbetrieb(false)}>Nur nachts</button>
            </div>
          </div>
        </V1Card>
      </Klappkachel>

      {/* ----------------------------------------------------------------- Ziel */}
      <Klappkachel
        titel="Ziel · Luftfeuchte"
        zusammenfassung={vpd ? 'nach VPD geregelt' : `Ein ${zahl(he.feuchteEinTag, 0)} · Aus ${zahl(aus, 0)} %`}
        id={KACHEL_ZIEL}
      >
        <V1Card>
          <p className="st-hinweis">
            {vpd
              ? 'Gerade wird nach VPD geregelt (unter „Erweitert“). Die Schwellen folgen dem VPD-Band aus dem Plan; die festen Werte hier gelten als Rückfallebene.'
              : 'Geregelt wird nach der Luftfeuchte. Der Plan setzt die Obergrenze, die Schwelle darunter stellst du ein.'}
          </p>
          <Lesen label="Plan-Obergrenze" hinweis={hl.planWoche ? `aus dem Plan · Woche ${hl.planWoche}` : 'aus dem Plan'} wert={planOben == null ? '–' : `≤ ${zahl(planOben, 0)} %`} />
          {!vpd && (
            <ZielBandAnzeige von={bandVon} bis={bandBis} zielVon={aus} zielBis={he.feuchteEinTag} grenzen={[aus, he.feuchteEinTag]} ist={null} urteil={null} />
          )}
          {!vpd && (
            <p className="st-hinweis">
              Über {zahl(he.feuchteEinTag, 0)} % gehen die Geräte an, unter {zahl(aus, 0)} % wieder aus, dazwischen bleibt es, wie es ist — so schalten sie nicht dauernd hin und her.
            </p>
          )}
          {getrennt ? (
            <>
              <Zahl ruht={vpd} label="Einschalten über (Tag)" hinweis={einHinweis} einheit="%" wert={he.feuchteEinTag} min={einMinimum(he.hystereseProzent)} max={SCHWELLE_MAX} schritt={1}
                onChange={(v) => h.setzMehrere(einSchwelleSetzen(he, he.hystereseProzent, { wert: v, tageszeit: 'tag' }))} fehler={h.feldFehler.FeuchteEinTag ?? h.feldFehler.FeuchteAusTag} />
              <Zahl ruht={vpd} label="Einschalten über (Nacht)" hinweis="Licht aus." einheit="%" wert={he.feuchteEinNacht} min={einMinimum(he.hystereseProzent)} max={SCHWELLE_MAX} schritt={1}
                onChange={(v) => h.setzMehrere(einSchwelleSetzen(he, he.hystereseProzent, { wert: v, tageszeit: 'nacht' }))} fehler={h.feldFehler.FeuchteEinNacht ?? h.feldFehler.FeuchteAusNacht} />
              <Lesen label="Ausschalten unter (Tag · Nacht)" hinweis={`= Einschalten minus Abstand (${zahl(he.hystereseProzent, 0)} %)`} wert={`${zahl(aus, 0)} · ${zahl(ausN, 0)} %`} />
            </>
          ) : (
            <>
              <Zahl ruht={vpd} label="Einschalten über" hinweis={`Tag und Nacht. ${einHinweis}`} einheit="%" wert={he.feuchteEinTag} min={einMinimum(he.hystereseProzent)} max={SCHWELLE_MAX} schritt={1}
                onChange={(v) => h.setzMehrere(einSchwelleSetzen(he, he.hystereseProzent, { wert: v, tageszeit: 'beide' }))} fehler={h.feldFehler.FeuchteEinTag ?? h.feldFehler.FeuchteAusTag} />
              <Lesen label="Ausschalten unter" hinweis={`= Einschalten minus Abstand (${zahl(he.hystereseProzent, 0)} %) — folgt der Stufe`} wert={`${zahl(aus, 0)} %`} />
            </>
          )}
        </V1Card>
      </Klappkachel>

      {/* ----------------------------------------------------------------- Höchsttemperatur */}
      <Klappkachel titel="Höchsttemperatur · darüber gehen alle aus" zusammenfassung={`Tag ${zahl(tagMax)} °C · Nacht ${zahl(nachtMax)} °C`} id={KACHEL_HOECHST}>
        <V1Card>
          <TempMaxBlock
            titel="Tag"
            planText={hl.planLuftTagC == null ? 'Kein Plan-Wert — es gilt der feste Wert.' : `Plan-Luft: ${zahl(hl.planLuftTagC)} °C`}
            modus={he.tempMaxTagModus}
            abstand={he.tempMaxTagAbstandK}
            fest={he.tempMaxTagFestC}
            plan={hl.planLuftTagC}
            ergebnis={tagMax}
            onModus={(m) => h.setz('tempMaxTagModus', m)}
            onAbstand={(v) => h.setz('tempMaxTagAbstandK', v)}
            onFest={(v) => h.setz('tempMaxTagFestC', v)}
            fehlerAbstand={h.feldFehler.TempMaxTagAbstandK}
            fehlerFest={h.feldFehler.TempMaxTagFestC}
          />
          <TempMaxBlock
            titel="Nacht"
            planText={hl.planLuftNachtC == null ? 'Kein Plan-Wert — es gilt der feste Wert.' : `Plan-Luft Nacht: ${zahl(hl.planLuftNachtC)} °C`}
            modus={he.tempMaxNachtModus}
            abstand={he.tempMaxNachtAbstandK}
            fest={he.tempMaxNachtFestC}
            plan={hl.planLuftNachtC}
            ergebnis={nachtMax}
            onModus={(m) => h.setz('tempMaxNachtModus', m)}
            onAbstand={(v) => h.setz('tempMaxNachtAbstandK', v)}
            onFest={(v) => h.setz('tempMaxNachtFestC', v)}
            fehlerAbstand={h.feldFehler.TempMaxNachtAbstandK}
            fehlerFest={h.feldFehler.TempMaxNachtFestC}
          />
          {ueberCo2 && (
            <V1Alert tone="warn" message={`Liegt über der CO₂-Grenze von ${zahl(co2Grenze)} °C — dann steigt der Entfeuchter erst nach der CO₂-Klimasperre aus.`} />
          )}
          <p className="st-hinweis">Kein Pflanzenziel, sondern Geräteschutz: Entfeuchter geben selbst Wärme ab. Die Höchsttemperatur gilt für {z ? 'alle Entfeuchter' : 'den Entfeuchter'}, getrennt für Tag und Nacht.</p>
        </V1Card>
      </Klappkachel>

      {/* ----------------------------------------------------------------- Stufe */}
      <Klappkachel titel="Wie reagieren die Entfeuchter?" zusammenfassung={stufe === 'eigene' ? 'eigene Werte' : STUFEN_AUSWAHL.find((s) => s.wert === stufe)?.label}>
        <V1Card>
          <p className="st-hinweis">Wie ruhig oder schnell die Geräte schalten. Eine Wahl statt mehrerer Zahlen — die einzelnen Werte findest du unter „Erweitert“. Ruhig schont den Kompressor und schwankt mehr; Schnell hält die Feuchte enger, schaltet aber öfter.</p>
          <div className="ef-stufen" role="radiogroup" aria-label="Wie reagieren die Entfeuchter?">
            {STUFEN_AUSWAHL.map((s) => (
              <button key={s.wert} type="button" role="radio" className="st-chip" aria-checked={stufe === s.wert} aria-current={stufe === s.wert} onClick={() => stufeWaehlen(s.wert)}>
                {s.label}
              </button>
            ))}
            {stufe === 'eigene' && (
              <button type="button" role="radio" className="st-chip" aria-checked="true" aria-current="true">Eigene Werte</button>
            )}
          </div>
          <table className="ef-stufentabelle">
            <thead>
              <tr>
                <th scope="col">Wert</th>
                {STUFEN_AUSWAHL.map((s) => <th key={s.wert} scope="col" className={stufe === s.wert ? 'is-gewaehlt' : undefined}>{s.label}</th>)}
                {stufe === 'eigene' && <th scope="col" className="is-gewaehlt">Deine</th>}
              </tr>
            </thead>
            <tbody>
              <tr>
                <th scope="row">Abstand EIN → AUS</th>
                {STUFEN_AUSWAHL.map((s) => <td key={s.wert} className={stufe === s.wert ? 'is-gewaehlt' : undefined}>{zahl(STUFEN[s.wert].abstandProzent, 0)} %</td>)}
                {stufe === 'eigene' && <td className="is-gewaehlt">{zahl(he.hystereseProzent, he.hystereseProzent % 1 ? 1 : 0)} %</td>}
              </tr>
              <tr>
                <th scope="row">Einschalten nach</th>
                {STUFEN_AUSWAHL.map((s) => <td key={s.wert} className={stufe === s.wert ? 'is-gewaehlt' : undefined}>{STUFEN[s.wert].einschaltMin} min</td>)}
                {stufe === 'eigene' && <td className="is-gewaehlt">{he.einschaltverzoegerungMin} min</td>}
              </tr>
              <tr>
                <th scope="row">Mindestlauf{z ? ' (Haupt / Zusatz)' : ''}</th>
                {STUFEN_AUSWAHL.map((s) => <td key={s.wert} className={stufe === s.wert ? 'is-gewaehlt' : undefined}>{STUFEN[s.wert].mindestlaufHauptMin}{z ? ` / ${STUFEN[s.wert].mindestlaufZusatzMin}` : ''} min</td>)}
                {stufe === 'eigene' && <td className="is-gewaehlt">{he.mindestlaufzeitMin}{ze ? ` / ${ze.mindestlaufzeitMin}` : ''} min</td>}
              </tr>
            </tbody>
          </table>
          <p className="st-hinweis">
            {stufe === 'eigene'
              ? 'Deine Werte passen zu keiner Stufe. Sie bleiben, wie sie sind, bis du eine Stufe wählst.'
              : 'Die Stufe setzt diese Werte auf einmal.'}
            {' '}Die Schutzzeiten (Mindestlaufzeit{z ? ', beim Zusatz auch die Mindestpause' : ''}) gelten in jeder Stufe.
          </p>
        </V1Card>
      </Klappkachel>

      {/* ----------------------------------------------------------------- Zusatz */}
      {z && ze && zl && (
        <Klappkachel titel={`Zusatz-Entfeuchter · Hilfsstärke`} zusammenfassung={HILFE_STUFEN.find((s) => s.wert === ze.hilfe)?.label ?? 'eigene Werte'}>
          <V1Card>
            <p className="st-hinweis">{haupt} führt. {zusatz} springt nur zu, wenn {haupt} allein nicht genügt, und geht früher wieder aus. Die Stärke setzt auch „Zuschalten nach“ und die Mindestpause des Zusatzes.</p>
            <div className="ef-stufen" role="radiogroup" aria-label="Hilfsstärke">
              {HILFE_STUFEN.map((s) => (
                <button
                  key={s.wert} type="button" role="radio" className="st-chip"
                  aria-checked={ze.hilfe === s.wert} aria-current={ze.hilfe === s.wert}
                  onClick={() => z.setEntwurf(hilfeWaehlen(ze, z.geladen!, s.wert))}
                >
                  {s.label}
                </button>
              ))}
              {ze.hilfe === 'eigene' && (
                <button type="button" role="radio" className="st-chip" aria-checked="true" aria-current="true">Eigene Werte</button>
              )}
            </div>
            <p className="st-hinweis">{hilfeText(ze.hilfe, haupt)}</p>
            <p className="st-hinweis" data-audit="zusatz-fuehrung-regel">Der Zusatz geht wegen {vpd ? 'VPD oder Feuchte' : 'der Luftfeuchte'} nur aus, wenn das Hauptgerät {haupt} läuft.</p>
            {ze.hilfe === 'eigene' && (
              <p className="ef-folge">
                Deine Werte: zuschalten nach {ze.zuschaltVerzoegerungMin} min · Mindestpause {ze.mindestpauseMin} min · früher aus um {zahl(ze.folgeAbstandK)} K · wieder an erst {zahl(ze.wiederEinAbstandK)} K darunter.
              </p>
            )}
          </V1Card>
        </Klappkachel>
      )}

      {/* ----------------------------------------------------------------- Meldungen */}
      <Klappkachel titel="Meldungen aufs Handy" zusammenfassung="Grenzwerte und Handy">
        <V1Card>
          <p className="st-hinweis">Du wirst gemeldet, wenn ein Messwert über die Grenze geht. Die Grenzen liegen auf der Seite Grenzwerte; dort folgen sie dem Plan, solange du sie nicht von Hand änderst.</p>
          {feuchteRegel && (
            <>
              <Lesen label="Luftfeuchte Tag" hinweis={zielwerte?.feuchteQuelle ?? 'Grenzwert'} wert={bereich(feuchteRegel.min, feuchteRegel.max, '%')} />
              {feuchteRegel.nachtMax != null && feuchteRegel.nachtMax !== feuchteRegel.max && (
                <Lesen label="Luftfeuchte Nacht" hinweis="eigener Wert für die Dunkelphase" wert={`≤ ${kurzeZahl(feuchteRegel.nachtMax)} %`} />
              )}
            </>
          )}
          {tempRegel && (
            <>
              <Lesen label="Lufttemperatur Tag" hinweis={zielwerte?.temperaturQuelle ?? 'Grenzwert'} wert={bereich(tempRegel.min, tempRegel.max, '°C')} />
              <Lesen label="Lufttemperatur Nacht" hinweis="Dunkelphase" wert={bereich(tempRegel.nachtMin ?? tempRegel.min, tempRegel.nachtMax ?? tempRegel.max, '°C')} />
            </>
          )}
          {ze && <Lesen label={`${zusatz} zieht nichts`} hinweis="Meldung aus Home Assistant" wert={ze.meldung.aktiv ? 'an' : 'aus'} />}
          <div className="ef-linkknoepfe">
            <V1LinkButton to="/grenzwerte" variant="secondary">Grenzwerte öffnen →</V1LinkButton>
            <V1LinkButton to="/handy" variant="secondary">Handy-Meldungen öffnen →</V1LinkButton>
          </div>
        </V1Card>
      </Klappkachel>

      {/* ----------------------------------------------------------------- Erweitert */}
      <Klappkachel titel="Erweitert" zusammenfassung="Alle Einzelwerte und Schalter" offen={false}>
        <V1Card>
          <p className="st-hinweis">Hier stehen die einzelnen Werte der Stufe. Änderst du einen, wechselt die Stufe oben auf „Eigene Werte“.</p>
          <Zahl label="Abstand EIN → AUS" hinweis="Wie weit die Feuchte unter die Einschalt-Schwelle fallen muss, bevor das Gerät ausgeht. Verschiebt die Ausschalt-Schwelle." einheit="%" wert={he.hystereseProzent} min={1} max={10} schritt={0.5}
            onChange={(v) => h.setzMehrere({ hystereseProzent: v, ...abstandAnwenden(he, v) })} fehler={h.feldFehler.HystereseProzent} />
          <Zahl label={`Einschalten nach (${haupt})`} hinweis="So lange muss die Feuchte über der Schwelle liegen, bevor er anspringt. Die Wartezeit bei trockener Außenluft wächst bei Bedarf mit." einheit="min" wert={he.einschaltverzoegerungMin} min={0} max={60} schritt={1}
            onChange={(v) => h.setzMehrere({ einschaltverzoegerungMin: Math.round(v), wartezeitAussenluftMin: aussenluftNachEinschalten(he.wartezeitAussenluftMin, Math.round(v)) })} fehler={h.feldFehler.EinschaltverzoegerungMin ?? h.feldFehler.WartezeitAussenluftMin} />
          <Zahl label={`Mindestlaufzeit (${haupt})`} hinweis="Vorher schaltet ihn die erreichte Luftfeuchte nicht ab. Übertemperatur schaltet immer sofort ab." einheit="min" wert={he.mindestlaufzeitMin} min={0} max={60} schritt={1}
            onChange={(v) => h.setz('mindestlaufzeitMin', Math.round(v))} fehler={h.feldFehler.MindestlaufzeitMin} />
          {z && ze && (
            <>
              <div className="v1-eyebrow ef-gruppe">{zusatz}</div>
              <Zahl label="Mindestlaufzeit" hinweis="Kompressorschutz." einheit="min" wert={ze.mindestlaufzeitMin} min={0} max={60} schritt={1}
                onChange={(v) => z.setz('mindestlaufzeitMin', Math.round(v))} fehler={fehlerZu(z.feldFehler, 'mindestlaufzeitMin')} />
              <Zahl label="Zuschalten nach" hinweis={`So lange muss ${haupt} laufen, bevor ${zusatz} mithilft. Gehört zur Hilfsstärke.`} einheit="min" wert={ze.zuschaltVerzoegerungMin} min={0} max={60} schritt={1}
                onChange={(v) => z.setzEinzel('zuschaltVerzoegerungMin', Math.round(v))} fehler={fehlerZu(z.feldFehler, 'zuschaltVerzoegerungMin')} />
              <Zahl label="Mindestpause" hinweis="Kompressorschutz. Gehört zur Hilfsstärke." einheit="min" wert={ze.mindestpauseMin} min={1} max={120} schritt={1}
                onChange={(v) => z.setzEinzel('mindestpauseMin', Math.round(v))} fehler={fehlerZu(z.feldFehler, 'mindestpauseMin')} />
              <Zahl label="Geht früher aus um" hinweis="Abstand unter der Höchsttemperatur. Gehört zur Hilfsstärke." einheit="K" wert={ze.folgeAbstandK} min={0} max={5} schritt={0.5}
                onChange={(v) => z.setzEinzel('folgeAbstandK', v)} fehler={fehlerZu(z.feldFehler, 'folgeAbstandK')} />
              <Zahl label="Wieder an erst, wenn es kühler ist" hinweis="Weiterer Abstand unter dem Abschaltwert. Verhindert das Takten an der Grenze. Gehört zur Hilfsstärke." einheit="K" wert={ze.wiederEinAbstandK} min={0} max={5} schritt={0.5}
                onChange={(v) => z.setzEinzel('wiederEinAbstandK', v)} fehler={fehlerZu(z.feldFehler, 'wiederEinAbstandK')} />
            </>
          )}
          <Zahl label={`Wartezeit bei trockener Außenluft (${haupt})`} hinweis="Trocknet die Zuluft gerade, wartet er stattdessen so lange. Nie kürzer als „Einschalten nach“." einheit="min" wert={he.wartezeitAussenluftMin} min={0} max={120} schritt={1}
            onChange={(v) => h.setz('wartezeitAussenluftMin', Math.round(v))} fehler={h.feldFehler.WartezeitAussenluftMin} />
          {z && ze && vpd && (
            <Zahl label={`VPD-Abstand (${zusatz})`} hinweis="Nur bei VPD-Regelung: Abstand in kPa rechts und links vom Plan-Ziel." einheit="kPa" wert={ze.vpdHystereseKpa} min={0.05} max={0.6} schritt={0.05}
              onChange={(v) => z.setzEinzel('vpdHystereseKpa', v)} fehler={fehlerZu(z.feldFehler, 'vpdHystereseKpa')} />
          )}
          {z && ze && (
            <>
              <div className="v1-eyebrow ef-gruppe">Meldung „zieht nichts“ ({zusatz})</div>
              <Zahl label="Meldung unter" hinweis="Leistung, ab der es als „zieht nichts“ gilt." einheit="W" wert={ze.meldung.grenzeW} min={5} max={200} schritt={5}
                onChange={(v) => z.setzMeldung('grenzeW', Math.round(v))} fehler={fehlerZu(z.feldFehler, 'meldung.grenzeW')} />
              <Zahl label="Meldung nach" hinweis="So lange muss es anhalten, bevor gemeldet wird." einheit="min" wert={ze.meldung.dauerMin} min={1} max={60} schritt={1}
                onChange={(v) => z.setzMeldung('dauerMin', Math.round(v))} fehler={fehlerZu(z.feldFehler, 'meldung.dauerMin')} />
              <Zahl label="Meldung wiederholen alle" hinweis="Solange das Problem besteht. Als Meldung in Home Assistant und als Push an die in den Meldungs-Einstellungen gewählte Adresse. Nie bei ausgeschaltetem Shelly." einheit="h" wert={ze.meldung.wiederholungH} min={1} max={24} schritt={1}
                onChange={(v) => z.setzMeldung('wiederholungH', Math.round(v))} fehler={fehlerZu(z.feldFehler, 'meldung.wiederholungH')} />
            </>
          )}
          <div className="v1-eyebrow ef-gruppe">Schalter</div>
          <V1Switch
            label="Einschalt-Schwelle für Tag und Nacht getrennt"
            checked={getrennt}
            onChange={(an) => {
              setGetrennt(an)
              // Wieder zusammenlegen: die Nacht folgt dem Tag, sonst stünden zwei verschiedene Werte hinter einem Feld.
              if (!an) h.setzMehrere(einSchwelleSetzen(he, he.hystereseProzent, { wert: he.feuchteEinTag, tageszeit: 'beide' }))
            }}
            hint="Gilt für die feste Einschalt-Schwelle der Luftfeuchte. Aus: ein Wert für Tag und Nacht."
          />
          {z && ze && (
            <V1Switch
              label="Zusatz läuft nachts durch"
              checked={ze.nachtDurchlaufen}
              onChange={(an) => z.setz('nachtDurchlaufen', an)}
              hint="An: nachts läuft er ohne Feuchte-Schwellen, bis das Zelt zu warm wird. Aus: auch nachts nach der Luftfeuchte takten."
            />
          )}
          <V1Switch
            label="Nach VPD regeln statt nach Luftfeuchte"
            checked={vpd}
            onChange={(an) => h.setz('vpdRegelung', an)}
            hint="Gilt für alle Entfeuchter. Aus: Luftfeuchte (Standard). Bei VPD wandern die Schwellen mit Temperatur und VPD-Band aus dem Plan."
          />
          {z && ze && (
            <V1Switch
              label="Melden, wenn der Zusatz kaum Strom zieht"
              checked={ze.meldung.aktiv}
              onChange={(an) => z.setzMeldung('aktiv', an)}
              hint={`Zum Beispiel Tank voll oder Gerät aus${zl?.leistungW == null ? '' : ` — gerade nimmt ${zusatz} ${zahl(zl.leistungW, 0)} W auf`}. Die Meldung läuft in Home Assistant und steht zusätzlich unter Ziele & Meldungen.`}
            />
          )}
        </V1Card>
      </Klappkachel>
    </>
  )
}
