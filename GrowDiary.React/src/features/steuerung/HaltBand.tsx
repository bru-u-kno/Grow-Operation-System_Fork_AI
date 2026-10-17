import { bandStellen, zeigZahl } from './zuluft-haltebereich'
import type { Haltebereich } from './zuluft-haltebereich'
import './steuerung.css'

/**
 * Fork AI (forkai.225): Die Hysterese als Band — wo der Lüfter angeht, wo er aufhört und was dazwischen
 * gilt. Der Punkt ist die Differenz, nach der die Regelung entscheidet. Farbe allein trägt nichts: die
 * Zonen haben Namen, und das Ganze liest sich auch als Satz (aria-label).
 *
 * Fork AI (forkai.238): Eine Komponente für Detailseite und Übersichtskachel. <c>kompakt</c> lässt die
 * Legende weg — in der Kachel steht der Grund schon als Satz darüber.
 */
export function HaltBand({ hb, differenz, kompakt = false }: { hb: Haltebereich; differenz: number | null; kompakt?: boolean }) {
  const s = bandStellen(hb, differenz)
  const gleich = hb.ausUnter === hb.anAb
  const satz = gleich
    ? `An und aus bei ${zeigZahl(hb.anAb, '', 1)} g/m³`
    : `Aus unter ${zeigZahl(hb.ausUnter, '', 1)}, an ab ${zeigZahl(hb.anAb, '', 1)} g/m³, dazwischen bleibt er, wie er ist`
  return (
    <div className="zl-band" role="img" aria-label={`${satz}${differenz == null ? '' : `; jetzt ${zeigZahl(differenz, '', 2)}`}`}>
      <div className="ef-skala">
        <div className="ef-bahn" />
        {!gleich && <div className="zl-halt" style={{ left: `${s.ausPos}%`, width: `${s.anPos - s.ausPos}%` }} />}
        <div className="zl-an" style={{ left: `${s.anPos}%` }} />
        {!gleich && <span className="ef-strich" style={{ left: `${s.ausPos}%` }} />}
        <span className="ef-strich" style={{ left: `${s.anPos}%` }} />
        {s.istPos != null && <span className="ef-ist" style={{ left: `${s.istPos}%` }}><em>{zeigZahl(differenz, '', 2)}</em></span>}
      </div>
      <div className="ef-marken">
        {gleich ? (
          <span style={{ left: `${s.anPos}%` }}>{zeigZahl(hb.anAb, '', 1)}<b>an und aus</b></span>
        ) : (
          <>
            <span style={{ left: `${s.ausPos}%`, transform: 'translateX(-85%)' }}>{zeigZahl(hb.ausUnter, '', 1)}<b>aus unter</b></span>
            <span style={{ left: `${s.anPos}%`, transform: 'translateX(-15%)' }}>{zeigZahl(hb.anAb, '', 1)}<b>an ab</b></span>
          </>
        )}
      </div>
      {!kompakt && (
        <p className="zl-legende">
          {gleich ? 'Ohne Haltebereich.' : <><span className="zl-punkt halt" aria-hidden="true" />hält, wie er ist <span className="zl-punkt an" aria-hidden="true" />saugt</>}
        </p>
      )}
    </div>
  )
}
