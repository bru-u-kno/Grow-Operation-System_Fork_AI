import { classNames } from '../../utils'
import { flipLabel, type Phase } from '../grows/phase-timeline'
import { KlappTitel } from './Einklappen'
import { phasenKopf, phasenZeile } from './phasen-uebersicht'

/**
 * Die Phasen auf einen Blick (cc.266/267, Wunsch des Nutzers): Kopfzeile mit laufender Phase und Ernte, darunter ein
 * Balken im Verhältnis der Dauern und — in der vollen Fassung — eine Zeile je Phase mit Tagen und Wochen, ausgeschrieben.
 *
 * Kein Wischen mehr: die frühere Zeitachse trug die Namen IM Balken und musste am Telefon gewischt werden.
 *
 * <b>Fassungen.</b> `voll` (Live, Grow-Seite) mit Titel „Phasen“ im Muster der Live-Seite: Pfeilknopf links, zugeklappt
 * die Kurzauskunft dahinter (`onUmschalten` setzt das Einklappen voraus). `kompakt` (Grow-Liste): nur Kopfzeile und Balken.
 * Mit `startDatum` (Grow-Seite) steht unten „Start · Geflippt“, sonst nur ein noch ausstehender Flip.
 */
export function PhasenUebersicht({ phases, ernte, flipIsPlanned, daysToFlip, flipDatum, startDatum, fertig, variante = 'voll', zu = false, onUmschalten }: {
  phases: Phase[]
  /** Kurzes Erntedatum („31.10.“) oder „—“. */
  ernte: string
  flipIsPlanned: boolean
  daysToFlip: number | null
  flipDatum: string
  /** Nur auf der Grow-Seite: Startdatum, dann steht die Datumszeile „Start · Flip“ immer da. */
  startDatum?: string
  /** Nur auf der Grow-Seite: „Fertig ~…“ (nach Trocknen und Aushärten). */
  fertig?: string
  variante?: 'voll' | 'kompakt'
  zu?: boolean
  onUmschalten?: () => void
}) {
  const kopf = phasenKopf(phases)
  const zeilen = phases.map(phasenZeile)
  const erntetext = ernte === '—' ? 'Ernte offen' : `Ernte ~${ernte}${kopf?.ueberPlan && phases.some((phase) => phase.state === 'current' && phase.name === 'Blüte') ? ' · überfällig' : ''}`
  const voll = variante === 'voll'
  // Die Datumszeile: auf der Grow-Seite immer „Start · Geflippt/Flip geplant“, sonst nur, solange der Flip aussteht.
  const flipText = flipLabel(flipIsPlanned, daysToFlip, flipDatum)
  // Jede Angabe als eigenes Stück, damit ein Umbruch nie mitten durch „Fertig ~28.12.“ geht.
  const datumzeile: string[] = startDatum != null
    ? [`Start ${startDatum}`, flipText, fertig && fertig !== '—' ? `Fertig ~${fertig}` : null].filter((teil): teil is string => teil != null)
    : flipIsPlanned ? [flipText] : []
  const ueberfaellig = daysToFlip != null && daysToFlip < 0 && flipIsPlanned
  // Eingeklappt ist nur, wo es einen Knopf gibt (im Anpassen-Modus der Live-Seite fehlt er, dort ist alles offen).
  const eingeklappt = voll && onUmschalten != null && zu
  const kurzauskunft = [kopf?.text, erntetext].filter(Boolean).join(' · ')
  return (
    <div className="ls-ph" data-audit="live-phasen">
      {voll && (
        <div className="ls-band-label">
          <KlappTitel zu={eingeklappt} onUmschalten={onUmschalten}>
            <span>Phasen</span>
            {eingeklappt && <em className={classNames('ls-bilanz', kopf?.ueberPlan && 'is-warn')}>{kurzauskunft}</em>}
          </KlappTitel>
          <i />
        </div>
      )}
      {!eingeklappt && (
        <>
          <div className="ls-ph-kopf">
            {kopf && <strong className={classNames(kopf.ueberPlan && 'is-ueber')}>{kopf.text}</strong>}
            <span className={classNames('ls-ph-ernte', kopf?.ueberPlan && 'is-ueber')}>{erntetext}</span>
          </div>
          <div className="ls-timeline ls-ph-balken" role="img" aria-label={phases.map((phase) => phase.label).join(', ')}>
            {phases.map((phase) => (
              <div
                key={phase.name}
                className={classNames('ls-phase', `is-${phase.state}`, phase.days === 0 && 'is-unknown')}
                style={phase.days === 0 ? undefined : { flexGrow: phase.days }}
              >
                {phase.progress != null && <i className="ls-phase-fill" style={{ width: `${Math.round(phase.progress * 100)}%` }} aria-hidden="true" />}
              </div>
            ))}
          </div>
          {voll && (
            <ul className="ls-ph-liste">
              {zeilen.map((zeile) => (
                <li key={zeile.name} className={classNames(`is-${zeile.state}`, zeile.ueberPlan && 'is-ueber')}>
                  <span className="ls-ph-name">{zeile.name}</span>
                  {/* Die laufende Phase in zwei Zeilen (Tag / Woche), die übrigen in einer — so bricht nie ein Teil auseinander. */}
                  <span className={classNames('ls-ph-text', zeile.state === 'current' && 'is-zweizeilig')}>
                    {zeile.state === 'current' ? zeile.teile.map((teil) => <span key={teil}>{teil}</span>) : zeile.text}
                  </span>
                </li>
              ))}
            </ul>
          )}
          {datumzeile.length > 0 && <p className={classNames('ls-ph-flip', ueberfaellig && 'is-ueber')}>{datumzeile.map((teil) => <span key={teil}>{teil}</span>)}</p>}
        </>
      )}
    </div>
  )
}
