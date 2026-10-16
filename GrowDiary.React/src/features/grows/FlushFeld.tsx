import { V1Button } from '../../components/v1'
import {
  FLUSH_KURZ, FLUSH_STANDARD, bluetewochenBisFlush, finishBeginn, flushTageGueltig, flushWochenText, kurzDatum, planHinweis, planHinweisText,
} from './flush'

/**
 * „Flush vor der Ernte": eine oder zwei Wochen, je Grow. Darunter, wann das
 * Finish beginnt — und, wenn der Düngeplan weniger Blütewochen führt, als Sorte
 * und Flush verlangen, ein Hinweis mit dem, was beim Speichern passiert.
 */
export function FlushFeld({ flushTage, onChange, maxWochen, flipDatum, flipBekannt, planName, blueteImPlan, spanne }: {
  flushTage: number | null | undefined
  onChange: (tage: number) => void
  /** Züchterwochen der Sorte, Maximum — wie der Server rechnet. */
  maxWochen: number | null
  /** Flipdatum (Datum), falls schon bekannt. */
  flipDatum: string | null
  /** Kennt der Fork den Blütebeginn (Flipdatum oder geplante Vegi-Dauer)? Sonst entstehen Wochen erst später. */
  flipBekannt: boolean
  planName: string | null
  /** Nummerierte Blütewochen des Plans oder Programms; null = unbekannt. */
  blueteImPlan: number | null
  /** „9–10" aus der Sorte. */
  spanne: string | null
}) {
  const aktuell = flushTageGueltig(flushTage)
  const finish = finishBeginn(flipDatum, maxWochen, aktuell)
  const hinweis = planHinweis(bluetewochenBisFlush(maxWochen, aktuell), blueteImPlan)

  return (
    <div className="v1-field is-wide" data-audit="flush-dauer">
      <span>Flush vor der Ernte</span>
      <div className="grow-form-wochen" role="group" aria-label="Flush vor der Ernte">
        {[FLUSH_KURZ, FLUSH_STANDARD].map((tage) => (
          <V1Button
            key={tage}
            variant={aktuell === tage ? 'primary' : 'secondary'}
            onClick={() => onChange(tage)}
          >
            {flushWochenText(tage)}
          </V1Button>
        ))}
      </div>
      <small data-audit="flush-finish">
        {finish
          ? `Finish beginnt am ${kurzDatum(finish)} (Ernte laut Sorte minus ${aktuell} Tage).`
          : `Das Finish beginnt ${aktuell} Tage vor der Ernte laut Sorte.`}
      </small>
      {hinweis && (
        <small role="status" data-audit={hinweis.art === 'fehlt' ? 'flush-plan-fehlt' : 'flush-plan-mehr'}>
          {planHinweisText(hinweis, planName, spanne, aktuell, flipBekannt)}
        </small>
      )}
    </div>
  )
}
