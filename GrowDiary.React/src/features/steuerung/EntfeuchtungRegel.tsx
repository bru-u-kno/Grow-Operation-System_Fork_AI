import { V1Card, V1LinkButton, V1Switch } from '../../components/v1'
import { HYSTERESE_STUFEN, zahl } from './entfeuchter-band'
import { HILFE_STUFEN, VPD_STUFEN, fehlerZu, gleich, hilfeText, hilfeWaehlen } from './entfeuchter-zusatz'
import { Klappkachel } from './Klappkachel'
import { GruppenKopf, Lesen, RegelKachel } from './EntfeuchtungBausteine'
import type { Ctx } from './EntfeuchtungBausteine'
import { Zahl } from './SteuerungsFelder'
import './steuerung.css'

/**
 * Fork AI (A-015): Reiter „Regel" — nach Regeln geordnet, nicht nach Geräten.
 *
 * Jede Regel steht einmal mit ihrer Erklärung da, darunter dieselben Felder für
 * jedes Gerät. Was nur ein Gerät hat, steht danach in einer eigenen Gruppe
 * („Nur für …"): beim Hauptentfeuchter die Außenluft, beim Zusatz die
 * Hilfsstärke und „Nachts durchlaufen".
 *
 * Die Regelgröße (Luftfeuchte oder VPD) ist ein einziger Schalter und gilt für
 * alle Geräte; bei Luftfeuchte gibt es nur einen Abstand in %, kPa-Felder
 * erscheinen nur bei VPD.
 */
export function RegelTab({ h, z, haupt, zusatz }: Ctx) {
  const he = h.entwurf!
  const hs = h.seite!
  const hl = hs.live
  const ze = z?.entwurf ?? null
  const zs = z?.seite ?? null
  const zl = zs?.live ?? null

  const vpd = he.vpdRegelung
  const ausSpaetestens = hl.einAktivProzent == null ? null : hl.einAktivProzent - (h.anzeige?.hystereseProzent ?? he.hystereseProzent)

  return (
    <>
      {/* ---------------------------------------------------- Regelgröße */}
      <RegelKachel
        titel="Wonach wird geregelt?"
        zusammenfassung={`${vpd ? 'VPD' : 'Luftfeuchte'} · gilt für ${ze ? 'alle Entfeuchter' : haupt}`}
        erklaerung="Eine Einstellung für alle Entfeuchter. Luftfeuchte ist der Standard: Ziel ist die Obergrenze aus dem Plan. Bei VPD wandern die Schwellen mit Temperatur und VPD-Band."
      >
        <div className="st-feldzeile is-gestapelt">
          <div className="ef-stufen" role="radiogroup" aria-label="Regelgröße">
            <button type="button" role="radio" className="st-chip" aria-checked={!vpd} aria-current={!vpd} onClick={() => h.setz('vpdRegelung', false)}>
              Luftfeuchte
            </button>
            <button type="button" role="radio" className="st-chip" aria-checked={vpd} aria-current={vpd} onClick={() => h.setz('vpdRegelung', true)}>
              VPD
            </button>
          </div>
          <p className="ef-folge">
            {vpd
              ? 'Die Schwellen folgen dem VPD-Band aus dem Plan; die Plan-Luftfeuchte deckelt EIN.'
              : `Ziel ist die Plan-Obergrenze${hl.rhObergrenzeProzent == null ? '' : ` (${zahl(hl.rhObergrenzeProzent, 0)} %)`}. Tag und Nacht gelten dieselben Schwellen, solange du unten keine festen setzt.`}
          </p>
        </div>
      </RegelKachel>

      {/* ------------------------------------------------ Wie ruhig schaltet er? */}
      <RegelKachel
        titel="Wie ruhig schaltet er?"
        zusammenfassung={vpd && ze ? `${haupt} ${zahl(he.hystereseProzent, 0)} % · ${zusatz} ${zahl(ze.vpdHystereseKpa, 2)} kPa` : `${zahl(he.hystereseProzent, 0)} %${ze ? ' · beide gleich' : ''}`}
        erklaerung="Wie weit die Messung vom Ziel abweichen darf, bevor ein Entfeuchter schaltet. Knapp hält den Wert enger, schaltet aber öfter; ruhig schont den Kompressor."
      >
        <div className="st-feldzeile is-gestapelt">
          <span className="st-etikett">
            {vpd || !ze ? haupt : 'Alle Entfeuchter'}
            <small>Abstand EIN → AUS in % Luftfeuchte</small>
            {h.feldFehler.HystereseProzent && <span className="st-fehler">{h.feldFehler.HystereseProzent}</span>}
          </span>
          <div className="ef-stufen" role="radiogroup" aria-label={`Abstand EIN → AUS, ${haupt}`}>
            {HYSTERESE_STUFEN.map((s) => (
              <button
                key={s.wert} type="button" role="radio" className="st-chip"
                aria-checked={!h.eigeneHysterese && he.hystereseProzent === s.wert}
                aria-current={!h.eigeneHysterese && he.hystereseProzent === s.wert}
                onClick={() => { h.setEigeneHysterese(false); h.setz('hystereseProzent', s.wert) }}
              >
                {s.label} · {s.wert} %
              </button>
            ))}
            <button type="button" role="radio" className="st-chip" aria-checked={h.eigeneHysterese} aria-current={h.eigeneHysterese} onClick={() => h.setEigeneHysterese(true)}>
              eigener Wert
            </button>
          </div>
          {h.eigeneHysterese && (
            <div className="ef-unterfeld">
              <Zahl label={`Eigener Abstand, ${haupt}`} hinweis="1 bis 10 %." einheit="%" wert={he.hystereseProzent} min={1} max={10} schritt={0.5} onChange={(v) => h.setz('hystereseProzent', v)} />
            </div>
          )}
          <p className="ef-folge">
            Heißt jetzt: EIN ab {zahl(hl.einAktivProzent)} %, AUS spätestens bei {zahl(ausSpaetestens)} %
            {vpd && hl.ausAktivProzent != null && ausSpaetestens != null && hl.ausAktivProzent < ausSpaetestens - 0.05
              ? ` (liegt das VPD-Ziel tiefer, gilt das: gerade ${zahl(hl.ausAktivProzent)} %)`
              : ''}
            .
          </p>
        </div>

        {vpd && z && ze && zl && (
          <div className="st-feldzeile is-gestapelt">
            <span className="st-etikett">
              {zusatz}
              <small>Abstand in kPa VPD, rechts und links vom Plan-Ziel</small>
              {fehlerZu(z.feldFehler, 'vpdHystereseKpa') && <span className="st-fehler">{fehlerZu(z.feldFehler, 'vpdHystereseKpa')}</span>}
            </span>
            <div className="ef-stufen" role="radiogroup" aria-label={`VPD-Abstand, ${zusatz}`}>
              {VPD_STUFEN.map((s) => {
                const an = !z.eigeneHysterese && gleich(ze.vpdHystereseKpa, s.wert)
                return (
                  <button
                    key={s.wert} type="button" role="radio" className="st-chip" aria-checked={an} aria-current={an}
                    onClick={() => { z.setEigeneHysterese(false); z.setzEinzel('vpdHystereseKpa', s.wert) }}
                  >
                    {s.label} · {zahl(s.wert, 2)}
                  </button>
                )
              })}
              <button type="button" role="radio" className="st-chip" aria-checked={z.eigeneHysterese} aria-current={z.eigeneHysterese} onClick={() => z.setEigeneHysterese(true)}>
                eigener Wert
              </button>
            </div>
            {z.eigeneHysterese && (
              <div className="ef-unterfeld">
                <Zahl label={`Eigener VPD-Abstand, ${zusatz}`} hinweis="0,05 bis 0,60 kPa." einheit="kPa" wert={ze.vpdHystereseKpa} min={0.05} max={0.6} schritt={0.05} onChange={(v) => z.setzEinzel('vpdHystereseKpa', v)} />
              </div>
            )}
            <p className="ef-folge">
              {zl.vpdZiel == null
                ? 'Das Plan-Ziel ist noch nicht bekannt.'
                : `EIN bei VPD unter ${zahl(zl.vpdZiel - (z.anzeige?.vpdHystereseKpa ?? ze.vpdHystereseKpa), 2)}, AUS über ${zahl(zl.vpdZiel + (z.anzeige?.vpdHystereseKpa ?? ze.vpdHystereseKpa), 2)} kPa.`}
            </p>
          </div>
        )}
      </RegelKachel>

      {/* ---------------------------------------------- Einschalten erst nach … */}
      <RegelKachel
        titel="Einschalten erst nach"
        zusammenfassung={`${haupt} ${he.einschaltverzoegerungMin} min${ze ? ` · ${zusatz} ${ze.zuschaltVerzoegerungMin} min` : ''}`}
        erklaerung="Wie lange die Bedingung anliegen muss, bevor das Gerät anspringt. Das fängt kurze Spitzen ab (Zelt offen, Gießen)."
      >
        <Zahl label={haupt} hinweis="So lange muss die Feuchte über EIN liegen, bevor er anspringt." einheit="min" wert={he.einschaltverzoegerungMin} min={0} max={60} schritt={1} onChange={(v) => h.setz('einschaltverzoegerungMin', Math.round(v))} fehler={h.feldFehler.EinschaltverzoegerungMin} />
        {z && ze && (
          <Zahl label={zusatz} hinweis={`So lange muss ${haupt} laufen, bevor ${zusatz} mithilft. Ist ${haupt} aus, startet der Zusatz sofort.`} einheit="min" wert={ze.zuschaltVerzoegerungMin} min={0} max={60} schritt={1} onChange={(v) => z.setzEinzel('zuschaltVerzoegerungMin', Math.round(v))} fehler={fehlerZu(z.feldFehler, 'zuschaltVerzoegerungMin')} />
        )}
      </RegelKachel>

      {/* ------------------------------------------------------- Tagbetrieb */}
      <RegelKachel
        titel="Auch tagsüber entfeuchten"
        zusammenfassung={`${haupt} ${he.tagbetriebErlauben ? 'ja' : 'nein'}${ze ? ` · ${zusatz} ${ze.tagbetriebErlauben ? 'ja' : 'nein'}` : ''}`}
        erklaerung="Aus: nur in der Dunkelphase. Wirkt mit der vom Fork angelegten Regelung."
        offen={false}
      >
        <V1Switch label={haupt} checked={he.tagbetriebErlauben} onChange={(an) => h.setz('tagbetriebErlauben', an)} />
        {z && ze && <V1Switch label={zusatz} checked={ze.tagbetriebErlauben} onChange={(an) => z.setz('tagbetriebErlauben', an)} />}
      </RegelKachel>

      {/* ---------------------------------------------------------- Automatik */}
      <RegelKachel
        titel="Automatik"
        zusammenfassung={`${haupt} ${he.automatikAktiv ? 'an' : 'aus'}${ze ? ` · ${zusatz} ${ze.automatikAktiv ? 'an' : 'aus'}` : ''}`}
        erklaerung="Aus hält die Regelung für dieses Gerät an; es bleibt, wie es gerade steht."
        offen={false}
      >
        <V1Switch label={haupt} checked={he.automatikAktiv} onChange={(an) => h.setz('automatikAktiv', an)} />
        {z && ze && <V1Switch label={zusatz} checked={ze.automatikAktiv} onChange={(an) => z.setz('automatikAktiv', an)} />}
      </RegelKachel>

      <Klappkachel
        titel="Feste Schwellen"
        zusammenfassung={vpd ? 'ruhen, solange VPD gewählt ist' : `Tag EIN ${zahl(he.feuchteEinTag, 0)} % · AUS ${zahl(he.feuchteAusTag, 0)} %`}
        offen={!he.vpdRegelung}
      >
        <V1Card>
          <p className="st-hinweis">Gelten für alle Entfeuchter.</p>
          {he.vpdRegelung && (
            <p className="st-hinweis st-ruht-hinweis">Gerade ohne Wirkung: Die Regelgröße steht auf VPD. Diese Werte gelten nur als Rückfallebene — wenn du auf Luftfeuchte umstellst oder der Plan keine VPD-Werte liefert. Auch dann deckelt die Plan-Feuchte die EIN-Schwelle.</p>
          )}
          <Zahl ruht={he.vpdRegelung} label="Tag · EIN ab" hinweis="Licht an." einheit="%" wert={he.feuchteEinTag} min={30} max={90} schritt={1} onChange={(v) => h.setz('feuchteEinTag', v)} fehler={h.feldFehler.FeuchteEinTag} />
          <Zahl ruht={he.vpdRegelung} label="Tag · AUS unter" hinweis="Muss unter EIN liegen." einheit="%" wert={he.feuchteAusTag} min={30} max={90} schritt={1} onChange={(v) => h.setz('feuchteAusTag', v)} fehler={h.feldFehler.FeuchteAusTag} />
          <Zahl ruht={he.vpdRegelung} label="Nacht · EIN ab" hinweis="Licht aus." einheit="%" wert={he.feuchteEinNacht} min={30} max={90} schritt={1} onChange={(v) => h.setz('feuchteEinNacht', v)} fehler={h.feldFehler.FeuchteEinNacht} />
          <Zahl ruht={he.vpdRegelung} label="Nacht · AUS unter" hinweis="Muss unter EIN liegen." einheit="%" wert={he.feuchteAusNacht} min={30} max={90} schritt={1} onChange={(v) => h.setz('feuchteAusNacht', v)} fehler={h.feldFehler.FeuchteAusNacht} />
        </V1Card>
      </Klappkachel>
      {/* ------------------------------------------------ Nur Hauptentfeuchter */}
      <GruppenKopf>Nur für {haupt}</GruppenKopf>
      <Klappkachel titel="Außenluft zuerst" zusammenfassung={`${he.wartezeitAussenluftMin} min`} offen={false}>
        <V1Card>
          <Zahl label="Außenluft zuerst" hinweis="Trocknet die Zuluft gerade, wartet er stattdessen so lange — die Außenluft bekommt ihre Chance." einheit="min" wert={he.wartezeitAussenluftMin} min={0} max={120} schritt={1} onChange={(v) => h.setz('wartezeitAussenluftMin', Math.round(v))} fehler={h.feldFehler.WartezeitAussenluftMin} />
          <p className="st-hinweis">
            {hl.zuluftVorrang === true
              ? `Gerade: Zuluft trocknet → es gelten ${he.wartezeitAussenluftMin} min.`
              : hl.zuluftVorrang === false
                ? `Gerade: Außenluft bringt nichts → es gelten ${he.einschaltverzoegerungMin} min.`
                : 'Ob die Zuluft gerade trocknet, ist nicht bekannt.'}
            {' '}Ob die Zuluft trocknet, entscheidet die Zuluft-Steuerung.
          </p>
        </V1Card>
      </Klappkachel>
      <Klappkachel titel={hs.live.planWoche ? `Aus dem Plan · ${hs.live.planWoche}` : 'Aus dem Plan'} zusammenfassung={`${vpd ? `VPD ${hl.vpdUnten == null ? '–' : `${zahl(hl.vpdUnten, 2)}–${zahl(hl.vpdOben, 2)} kPa`} · ` : ''}Feuchte max. ${hl.rhObergrenzeProzent == null ? '–' : `${zahl(hl.rhObergrenzeProzent, 0)} %`}`} offen={false}>
        <V1Card>
          {vpd && <Lesen label="VPD-Band" hinweis="aus dem Plan" wert={hl.vpdUnten == null ? '–' : `${zahl(hl.vpdUnten, 2)} – ${zahl(hl.vpdOben, 2)} kPa`} />}
          <Lesen
            label="Luftfeuchte max."
            hinweis={hl.deckelProzent == null ? 'aus dem Plan' : `aus dem Plan · Deckel für EIN: ${zahl(hl.rhObergrenzeProzent, 0)} % − Klima-Abstand = ${zahl(hl.deckelProzent, 0)} %`}
            wert={hl.rhObergrenzeProzent == null ? '–' : `${zahl(hl.rhObergrenzeProzent, 0)} %`}
          />
          <Lesen label="Blatt-Offset" hinweis="vom Zelt" wert={hl.blattOffsetC == null ? '–' : `${zahl(hl.blattOffsetC)} °C`} />
          <div className="st-feldzeile">
            <span className="st-etikett">Ändern im Plan</span>
            <V1LinkButton to="/plan" variant="ghost">Plan ›</V1LinkButton>
          </div>
        </V1Card>
      </Klappkachel>

      {/* ------------------------------------------------------- Nur Zusatz */}
      {z && ze && zl && zs && (
        <>
          <GruppenKopf>Nur für {zusatz}</GruppenKopf>
          <Klappkachel titel="Hilfsstärke" zusammenfassung={HILFE_STUFEN.find((s) => s.wert === ze.hilfe)?.label ?? 'eigene Werte'}>
            <V1Card>
              <div className="st-feldzeile is-gestapelt">
                <span className="st-etikett">
                  Wie stark soll {zusatz} helfen?
                  <small>{haupt} führt. Der Zusatz hilft nur dazu.</small>
                </span>
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
                    <button type="button" role="radio" className="st-chip" aria-checked="true" aria-current="true">eigene Werte</button>
                  )}
                </div>
                <p className="st-hinweis">{hilfeText(ze.hilfe, haupt)}</p>
                <p className={ze.hilfe === 'normal' ? 'ez-empf' : 'ez-empf is-abweichend'}>
                  {ze.hilfe === 'normal' ? 'Empfohlen: normal ✓' : 'Empfohlen: normal'}
                  {ze.hilfe !== 'normal' && (
                    <button type="button" onClick={() => z.setEntwurf(hilfeWaehlen(ze, z.geladen!, 'normal'))}>zurücksetzen</button>
                  )}
                </p>
              </div>
            </V1Card>
          </Klappkachel>
          <Klappkachel titel="Schaltgröße" zusammenfassung={zl.schaltgroesse === 'vpd' ? 'VPD' : zl.schaltgroesse === 'feuchte' ? 'Luftfeuchte' : '–'} offen={false}>
            <V1Card>
              <Lesen
                label="Schaltgröße"
                hinweis={vpd ? 'Folgt der Regelgröße oben. Bei VPD braucht er ein VPD-Ziel im Plan, sonst nimmt der Fork die Plan-Luftfeuchte.' : 'Folgt der Regelgröße oben: Luftfeuchte.'}
                wert={zl.schaltgroesse === 'vpd' ? 'VPD' : zl.schaltgroesse === 'feuchte' ? 'Luftfeuchte' : '–'}
              />
              {vpd && <Lesen label="VPD-Ziel" hinweis={zl.planWoche ?? undefined} wert={zl.vpdZiel == null ? '–' : `${zahl(zl.vpdZiel, 2)} kPa`} />}
              <Lesen
                label="Luftfeuchte EIN / AUS"
                hinweis={vpd ? 'Schwellen aus dem Plan; gelten nachts mit „Nachts durchlaufen" und wenn der Plan kein VPD liefert.' : 'Dieselben Schwellen wie beim Hauptgerät, Tag und Nacht.'}
                wert={zl.feuchteEinProzent == null || zl.feuchteAusProzent == null ? '–' : `${zahl(zl.feuchteEinProzent, 0)} % / ${zahl(zl.feuchteAusProzent, 0)} %`}
              />
            </V1Card>
          </Klappkachel>
          <Klappkachel titel="Nachts durchlaufen" zusammenfassung={ze.nachtDurchlaufen ? 'an' : 'aus'} offen={false}>
            <V1Card>
              <V1Switch
                label="Nachts durchlaufen"
                checked={ze.nachtDurchlaufen}
                onChange={(an) => z.setz('nachtDurchlaufen', an)}
                hint="An: nachts läuft er, bis das Zelt zu warm wird (keine Feuchtespitzen). Aus: auch nachts nach der Luftfeuchte takten. Wirkt mit der vom Fork angelegten Regelung."
              />
            </V1Card>
          </Klappkachel>
        </>
      )}
    </>
  )
}
