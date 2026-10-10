import { useEffect, useState } from 'react'
import { apiFetch, ApiRequestError } from '../../api'
import { V1Alert, V1Empty, V1Section } from '../../components/v1'
import type { AddbackLogDto, AddbackVorgangDto } from '../../types'
import { classNames, formatDateTime, formatNumber } from '../../utils'
import { NACHFUELL_ART, teileText } from '../vorgang/ablauf-rechnung'
import '../changeouts/changeouts.css'

function paar(vorher: number | null, nachher: number | null): string {
  if (vorher == null && nachher == null) return '–'
  return `${formatNumber(vorher, 2)} → ${formatNumber(nachher, 2)}`
}

const WASSER: Record<string, string> = { Tap: 'Leitungswasser', RO: 'Osmose', Mixed: 'Mischung' }

/**
 * Die bisherigen Nachfüll-Einträge eines Grows — Liste und Löschen.
 *
 * Wie die Liste der Wasserwechsel (`ChangeoutsPanel`): eingetragen wird im
 * Ablauf darüber, hier steht, was war. **Altdaten** (Einträge des früheren
 * Addback-Assistenten, ohne Vorgang) stehen mit in der Liste und lassen sich
 * einzeln entfernen. Ein Eintrag aus dem Ablauf zeigt, was mit ihm angelegt
 * wurde — und Entfernen nimmt den ganzen Vorgang.
 *
 * @param markiert Vorgang, auf den ein Link zeigt (`?vorgang=`): hervorgehoben und ins Bild gerollt.
 */
export function NachfuellListe({ growId, growName, eintraege, vorgaenge, laedt, markiert, onGeaendert }: {
  growId: number
  growName: string
  eintraege: AddbackLogDto[]
  vorgaenge: AddbackVorgangDto[]
  laedt: boolean
  markiert: number | null
  onGeaendert: () => void
}) {
  const [fehler, setFehler] = useState<string | null>(null)
  const [meldung, setMeldung] = useState<string | null>(null)

  useEffect(() => {
    if (markiert == null || laedt) return
    document.querySelector(`[data-nachfuell-vorgang="${markiert}"]`)?.scrollIntoView({ block: 'center' })
  }, [markiert, laedt])

  /** Einen Eintrag zurücknehmen — beim Vorgang mit allem, was er angelegt hat. Mit Rückfrage. */
  async function entfernen(eintrag: AddbackLogDto, vorgang: AddbackVorgangDto | undefined) {
    const wann = formatDateTime(eintrag.performedAtUtc)
    const mit = vorgang ? ` mit ${teileText(vorgang)}` : ''
    if (!window.confirm(`${NACHFUELL_ART[eintrag.kind]} vom ${wann}${mit} wirklich entfernen?`)) return
    setFehler(null)
    setMeldung(null)
    try {
      if (vorgang) await apiFetch(`/api/grows/${growId}/addback/vorgaenge/${vorgang.id}`, { method: 'DELETE' })
      else await apiFetch(`/api/grows/${growId}/addback/logs/${eintrag.id}`, { method: 'DELETE' })
      setMeldung(vorgang ? `${NACHFUELL_ART[eintrag.kind]} mit ${teileText(vorgang)} entfernt.` : `${NACHFUELL_ART[eintrag.kind]} entfernt.`)
      onGeaendert()
    } catch (caught) {
      setFehler(caught instanceof ApiRequestError ? caught.message : 'Entfernen fehlgeschlagen.')
    }
  }

  const sortiert = [...eintraege].sort((a, b) => b.performedAtUtc.localeCompare(a.performedAtUtc))

  return (
    <V1Section title="Bisher nachgefüllt" className="changeouts-section">
      {meldung && <V1Alert title="Entfernt" message={meldung} tone="ok" />}
      {fehler && <V1Alert message={fehler} tone="warn" />}

      {!laedt && sortiert.length === 0 ? (
        <V1Empty title="Noch nichts nachgefüllt" text={`Für ${growName} ist noch kein Nachfüllen eingetragen.`} />
      ) : (
        <div className="v1-list" data-audit="addback-log-list">
          {sortiert.map((eintrag) => {
            const vorgang = vorgaenge.find((v) => v.eintrag?.id === eintrag.id)
            return (
              <div key={eintrag.id} className={classNames('v1-list-row', vorgang != null && vorgang.id === markiert && 'is-markiert')}
                data-nachfuell-vorgang={vorgang?.id}>
                <strong>{formatDateTime(eintrag.performedAtUtc)}</strong>
                <span>
                  {NACHFUELL_ART[eintrag.kind]}
                  {eintrag.litersAdded != null ? ` · ${formatNumber(eintrag.litersAdded, 1)} L` : ''}
                  {eintrag.waterUsed ? ` ${WASSER[eintrag.waterUsed] ?? ''}` : ''}
                  {` · EC ${paar(eintrag.ecBefore, eintrag.ecAfter)} · pH ${paar(eintrag.phBefore, eintrag.phAfter)}`}
                  {/* Altdaten: der frühere Assistent schrieb seine Komponentenliste in die Notiz. */}
                  {eintrag.notes && !vorgang ? <small className="changeouts-vorgang nachfuell-notiz">{eintrag.notes}</small> : eintrag.notes ? ` · ${eintrag.notes}` : ''}
                  {vorgang && <small className="changeouts-vorgang">Vorgang: {teileText(vorgang)}</small>}
                </span>
                <em>{eintrag.kind === 'TopOff' ? 'Wasser' : eintrag.kind === 'Correction' ? 'Korr.' : 'Dünger'}</em>
                <button
                  type="button"
                  className="ls-btn is-small changeouts-weg"
                  onClick={() => void entfernen(eintrag, vorgang)}
                  aria-label={`${NACHFUELL_ART[eintrag.kind]} vom ${formatDateTime(eintrag.performedAtUtc)} entfernen`}
                  title="Entfernen"
                >Entfernen</button>
              </div>
            )
          })}
        </div>
      )}
    </V1Section>
  )
}
