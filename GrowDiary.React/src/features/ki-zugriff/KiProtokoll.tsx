import { useEffect, useState } from 'react'
import { apiFetch, formatApiError } from '../../api'
import { V1Alert, V1Badge, V1Empty, V1Skeleton } from '../../components/v1'
import type { KiProtokollEintragDto } from '../../types'
import { formatDateTime } from '../../utils'
import { aktionText, ergebnisSchild, protokollWeg, schluesselText } from './ki-protokoll-logik'

/** Der Satz, wenn noch nichts protokolliert ist. */
export const LEER_SATZ = 'Noch nichts — sobald ein Assistent etwas einträgt, steht es hier.'

/**
 * „Was die KI zuletzt getan hat" (A-003, Fork AI 03.10.2026) — unter der
 * Schlüsselliste im Abschnitt „Zugriff für KI-Assistenten".
 *
 * Liest `/api/settings/ki-zugriff/protokoll` selbst und neu, sobald der Filter
 * wechselt. Gefiltert wird über die Knöpfe „Nur diesen zeigen" in der
 * Schlüsselliste; zurück geht es über „Alle zeigen" hier oder dort.
 */
export default function KiProtokoll({ filter, onFilter }: {
  /** Der Schlüssel, dessen Einträge gezeigt werden — `null` für alle. */
  filter: { id: number; name: string } | null
  onFilter: (id: number | null) => void
}) {
  const [eintraege, setEintraege] = useState<KiProtokollEintragDto[] | null>(null)
  const [fehler, setFehler] = useState<string | null>(null)
  const [runde, setRunde] = useState(0)
  const filterId = filter?.id ?? null

  // Ein neuer Filter heisst ein neues Bauteil (key im Abschnitt) — der Zustand
  // beginnt dann leer, und hier wird nur nach dem Laden gesetzt.
  useEffect(() => {
    const abbruch = new AbortController()
    async function laden() {
      try {
        const liste = await apiFetch<KiProtokollEintragDto[]>(protokollWeg(filterId), { signal: abbruch.signal })
        if (!abbruch.signal.aborted) { setEintraege(liste); setFehler(null) }
      } catch (caught) {
        if (!abbruch.signal.aborted) setFehler(formatApiError(caught, 'Was die KI zuletzt getan hat, konnte nicht geladen werden.'))
      }
    }
    void laden()
    return () => abbruch.abort()
  }, [filterId, runde])

  function neuLaden() {
    setEintraege(null)
    setFehler(null)
    setRunde((r) => r + 1)
  }

  return (
    <div className="ki-block ki-protokoll" id="ki-protokoll" data-audit="ki-protokoll">
      <div className="ki-block-kopf">
        <h3>Was die KI zuletzt getan hat</h3>
        <button type="button" className="ls-btn is-small" onClick={neuLaden} data-audit="ki-protokoll-aktualisieren">
          Aktualisieren
        </button>
      </div>

      {filter && (
        <div className="ki-protokoll-filter" data-audit="ki-protokoll-filter">
          <span>Nur „{filter.name}"</span>
          <button type="button" className="ls-btn is-small" onClick={() => onFilter(null)} data-audit="ki-protokoll-alle">Alle zeigen</button>
        </div>
      )}

      {fehler ? <V1Alert message={fehler} tone="warn" />
        : eintraege == null ? <V1Skeleton rows={2} label="Lade, was die KI zuletzt getan hat" />
          : <KiProtokollListe eintraege={eintraege} filterName={filter?.name ?? null} />}
    </div>
  )
}

/** Die Liste selbst — ohne Laden, damit sie sich ohne Browser prüfen lässt. */
export function KiProtokollListe({ eintraege, filterName }: { eintraege: KiProtokollEintragDto[]; filterName: string | null }) {
  if (eintraege.length === 0) {
    return <V1Empty title={filterName ? `Von „${filterName}" steht noch nichts hier.` : LEER_SATZ} />
  }
  return (
    <ul className="ki-protokoll-liste">
      {eintraege.map((eintrag) => {
        const schild = ergebnisSchild(eintrag)
        return (
          <li key={eintrag.id} className="ki-protokoll-eintrag" data-audit="ki-protokoll-eintrag">
            <div className="ki-protokoll-zeile">
              <strong className="ki-protokoll-aktion">{aktionText(eintrag)}</strong>
              <V1Badge tone={schild.ton}>{schild.text}</V1Badge>
            </div>
            <div className="ki-zeit">{formatDateTime(eintrag.zeitpunktUtc)} · {schluesselText(eintrag)}</div>
          </li>
        )
      })}
    </ul>
  )
}
