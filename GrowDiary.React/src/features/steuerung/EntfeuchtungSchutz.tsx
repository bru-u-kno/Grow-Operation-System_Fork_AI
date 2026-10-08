import { V1Alert, V1Card, V1Switch } from '../../components/v1'
import { tempMax, zahl } from './entfeuchter-band'
import { fehlerZu, tempGrenzen } from './entfeuchter-zusatz'
import { GruppenKopf, RegelKachel } from './EntfeuchtungBausteine'
import type { Ctx } from './EntfeuchtungBausteine'
import { Klappkachel } from './Klappkachel'
import { TempMaxBlock, Zahl } from './SteuerungsFelder'
import './steuerung.css'

/**
 * Fork AI (A-015): Reiter „Schutz" — die Höchsttemperatur einmal für alle,
 * dann je Regel dieselben Felder für jedes Gerät, dann das, was nur der Zusatz hat.
 *
 * <b>Die Höchsttemperatur wird über den Hauptentfeuchter gespeichert</b> (wie
 * auf seiner früheren Seite): beide Entfeuchter lesen dieselben Helfer.
 */
export function SchutzTab({ h, z, haupt, zusatz }: Ctx) {
  const he = h.entwurf!
  const hl = h.seite!.live
  const anzeige = h.anzeige ?? he
  const ze = z?.entwurf ?? null
  const zl = z?.seite?.live ?? null

  const tagMax = tempMax(anzeige.tempMaxTagModus, anzeige.tempMaxTagAbstandK, anzeige.tempMaxTagFestC, hl.planLuftTagC)
  const nachtMax = tempMax(anzeige.tempMaxNachtModus, anzeige.tempMaxNachtAbstandK, anzeige.tempMaxNachtFestC, hl.planLuftNachtC)
  const grenze = hl.co2CanopyGrenzeC
  const ueberCo2 = grenze != null && (tagMax > grenze || nachtMax > grenze)
  const aktivMax = hl.tagPhase === false ? nachtMax : tagMax
  const grenzen = z?.anzeige ? tempGrenzen(z.anzeige, aktivMax) : null

  return (
    <>
      {/* ---------------------------------------------- Höchsttemperatur, einmal */}
      <Klappkachel titel="Höchsttemperatur · darüber gehen alle aus" zusammenfassung={`Tag ${zahl(tagMax)} °C · Nacht ${zahl(nachtMax)} °C`}>
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
            <V1Alert tone="warn" message={`Liegt über der CO₂-Grenze von ${zahl(grenze)} °C — dann steigt der Entfeuchter erst nach der CO₂-Klimasperre aus.`} />
          )}
          <p className="st-hinweis">Kein Pflanzenziel, sondern Geräteschutz: Entfeuchter geben selbst Wärme ab. Die Höchsttemperatur gilt für {z ? 'alle Entfeuchter' : 'den Entfeuchter'}.</p>
        </V1Card>
      </Klappkachel>

      {/* ------------------------------------------------------- Laufzeit */}
      <RegelKachel
        titel="Mindestlaufzeit"
        zusammenfassung={`${haupt} ${he.mindestlaufzeitMin} min${ze ? ` · ${zusatz} ${ze.mindestlaufzeitMin} min` : ''}`}
        erklaerung="Vorher schaltet erreichte Feuchte (oder VPD) das Gerät nicht ab. Übertemperatur schaltet immer sofort ab."
        offen={false}
      >
        <Zahl label={haupt} hinweis="Mindestlaufzeit" einheit="min" wert={he.mindestlaufzeitMin} min={0} max={60} schritt={1} onChange={(v) => h.setz('mindestlaufzeitMin', Math.round(v))} fehler={h.feldFehler.MindestlaufzeitMin} />
        {z && ze && (
          <Zahl label={zusatz} hinweis="Mindestlaufzeit" einheit="min" wert={ze.mindestlaufzeitMin} min={0} max={60} schritt={1} onChange={(v) => z.setz('mindestlaufzeitMin', Math.round(v))} fehler={fehlerZu(z.feldFehler, 'mindestlaufzeitMin')} />
        )}
      </RegelKachel>

      {/* ---------------------------------------------------- Nur Zusatz */}
      {z && ze && zl && (
        <>
          <GruppenKopf>Nur für {zusatz}</GruppenKopf>
          <Klappkachel titel="Früher aus, später wieder an" zusammenfassung={`−${zahl(z.anzeige?.folgeAbstandK ?? ze.folgeAbstandK)} K · Wieder-EIN −${zahl(z.anzeige?.wiederEinAbstandK ?? ze.wiederEinAbstandK)} K`}>
            <V1Card>
              <Zahl label="Zusatz geht früher aus" hinweis={grenzen ? `Bei ${zahl(grenzen.max)} °C geht ${haupt} aus, der Zusatz schon bei ${zahl(grenzen.folgeAus)}.` : 'Abstand unter der Höchsttemperatur.'} einheit="K" wert={ze.folgeAbstandK} min={0.5} max={3} schritt={0.5} onChange={(v) => z.setzEinzel('folgeAbstandK', v)} fehler={fehlerZu(z.feldFehler, 'folgeAbstandK')} />
              <Zahl label="Wieder einschalten erst, wenn es kühler ist" hinweis={grenzen ? `Weiterer Abstand unter dem Abschaltwert (jetzt ${zahl(grenzen.wiederEin)} °C). Verhindert das Takten an der Grenze.` : 'Weiterer Abstand unter dem Abschaltwert. Verhindert das Takten an der Grenze.'} einheit="K" wert={ze.wiederEinAbstandK} min={0.5} max={3} schritt={0.5} onChange={(v) => z.setzEinzel('wiederEinAbstandK', v)} fehler={fehlerZu(z.feldFehler, 'wiederEinAbstandK')} />
              <p className="st-hinweis" data-audit="zusatz-fuehrung-regel">Der Zusatz geht wegen VPD oder Feuchte nur aus, wenn das Hauptgerät {haupt} läuft.</p>
            </V1Card>
          </Klappkachel>
          <Klappkachel titel="Mindestpause" zusammenfassung={`${ze.mindestpauseMin} min`} offen={false}>
            <V1Card>
              <Zahl label="Mindestpause" hinweis="Kompressorschutz." einheit="min" wert={ze.mindestpauseMin} min={1} max={120} schritt={1} onChange={(v) => z.setzEinzel('mindestpauseMin', Math.round(v))} fehler={fehlerZu(z.feldFehler, 'mindestpauseMin')} />
            </V1Card>
          </Klappkachel>
          <Klappkachel titel="Meldung „zieht nichts“" zusammenfassung={ze.meldung.aktiv ? `an · unter ${ze.meldung.grenzeW} W nach ${ze.meldung.dauerMin} min` : 'aus'} offen={false}>
            <V1Card>
              <V1Switch
                label="Melden, wenn der Shelly an ist, das Gerät aber nichts zieht"
                checked={ze.meldung.aktiv}
                onChange={(an) => z.setzMeldung('aktiv', an)}
                hint={zl.leistungW == null ? 'Tank voll oder Gerät ausgeschaltet?' : `Tank voll oder Gerät ausgeschaltet? Gerade nimmt ${zusatz} ${zahl(zl.leistungW, 0)} W auf.`}
              />
              <Zahl label="Meldung unter" hinweis={'Leistung, ab der es als „zieht nichts" gilt.'} einheit="W" wert={ze.meldung.grenzeW} min={5} max={200} schritt={5} onChange={(v) => z.setzMeldung('grenzeW', Math.round(v))} fehler={fehlerZu(z.feldFehler, 'meldung.grenzeW')} />
              <Zahl label="Meldung nach" hinweis="So lange muss es anhalten, bevor gemeldet wird." einheit="min" wert={ze.meldung.dauerMin} min={1} max={60} schritt={1} onChange={(v) => z.setzMeldung('dauerMin', Math.round(v))} fehler={fehlerZu(z.feldFehler, 'meldung.dauerMin')} />
              <Zahl label="Meldung wiederholen alle" hinweis="Solange das Problem besteht. Als Meldung in Home Assistant und als Push an die in den Meldungs-Einstellungen gewählte Adresse. Nie bei ausgeschaltetem Shelly." einheit="h" wert={ze.meldung.wiederholungH} min={1} max={24} schritt={1} onChange={(v) => z.setzMeldung('wiederholungH', Math.round(v))} fehler={fehlerZu(z.feldFehler, 'meldung.wiederholungH')} />
            </V1Card>
          </Klappkachel>
        </>
      )}
    </>
  )
}
