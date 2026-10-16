import { V1Button } from '../../components/v1'
import {
  finishVerschiebungTage, gleicheWochen, istEigen, wochenText, wochenZahl, type Wochen,
} from './zuechterwochen'

export interface ZuechterWochenAenderung {
  breederWeeksEigen: boolean
  breederFlowerWeeksMin: number | null
  breederFlowerWeeksMax: number | null
}

/**
 * Blütedauer laut Züchter im Grow-Formular. Folgt der Sorte, solange kein
 * eigener Wert gesetzt ist; ein eigener Wert zeigt seine Abweichung an.
 */
export function ZuechterWochenFeld({ strainId, eigenGesetzt, wochen, sorte, sorteName, onChange }: {
  strainId: number | null
  eigenGesetzt: boolean | null | undefined
  wochen: Wochen
  /** Wochen der gewählten Sorte aus der Bibliothek; null = keine Sorte oder nicht gefunden. */
  sorte: Wochen | null
  sorteName: string | null
  onChange: (aenderung: ZuechterWochenAenderung) => void
}) {
  const eigen = istEigen(strainId, eigenGesetzt, sorte != null)
  const sorteHatAngabe = sorte != null && (sorte.min != null || sorte.max != null)

  if (!eigen && sorte) {
    return (
      <div className="v1-field is-wide" data-audit="zuechterwochen-folgt-sorte">
        <span>Blütedauer laut Züchter</span>
        <strong>{sorteHatAngabe ? `${wochenText(sorte)} Wochen` : 'keine Angabe'}</strong>
        <small>
          {sorteHatAngabe
            ? `Aus der Sorte ${sorteName ?? ''} — wird mitgeführt, auch wenn die Sorte sich ändert.`
            : `Die Sorte ${sorteName ?? ''} hat keine Angabe; gerechnet wird mit dem Standardwert.`}
        </small>
        <div>
          <V1Button variant="secondary" onClick={() => onChange({
            breederWeeksEigen: true, breederFlowerWeeksMin: sorte.min, breederFlowerWeeksMax: sorte.max,
          })}>Eigene Werte eintragen</V1Button>
        </div>
      </div>
    )
  }

  const weichtAb = sorteHatAngabe && !gleicheWochen(wochen, sorte)
  const tage = weichtAb ? finishVerschiebungTage(wochen, sorte) : null
  return (
    <div className="v1-field is-wide" data-audit="zuechterwochen-eigen">
      <span>Blütedauer laut Züchter (Wochen)</span>
      <div className="grow-form-wochen">
        <input
          aria-label="Blüte von (Wochen)"
          inputMode="numeric"
          value={wochen.min ?? ''}
          onChange={(event) => onChange({ breederWeeksEigen: true, breederFlowerWeeksMin: wochenZahl(event.target.value), breederFlowerWeeksMax: wochen.max })}
        />
        <input
          aria-label="Blüte bis (Wochen)"
          inputMode="numeric"
          value={wochen.max ?? ''}
          onChange={(event) => onChange({ breederWeeksEigen: true, breederFlowerWeeksMin: wochen.min, breederFlowerWeeksMax: wochenZahl(event.target.value) })}
        />
      </div>
      {weichtAb && sorte && (
        <small role="status" data-audit="zuechterwochen-abweichung">
          Weicht von der Sorte ab (Sorte: {wochenText(sorte)}).
          {tage != null && tage !== 0 && ` Finish beginnt dadurch ${Math.abs(tage)} Tage ${tage < 0 ? 'früher' : 'später'}.`}
        </small>
      )}
      {sorte && sorteHatAngabe && (
        <div>
          <V1Button variant="secondary" onClick={() => onChange({
            breederWeeksEigen: false, breederFlowerWeeksMin: sorte.min, breederFlowerWeeksMax: sorte.max,
          })}>Wie Sorte ({wochenText(sorte)})</V1Button>
        </div>
      )}
    </div>
  )
}
