import { useEffect, useState } from 'react'
import { apiFetch, ApiRequestError } from '../../api'
import type { ChangeoutDto, WasserwechselVorgangDto } from '../../types'
import { teileText } from '../vorgang/ablauf-rechnung'
import { V1Alert, V1Empty, V1Section, V1Skeleton } from '../../components/v1'
import { classNames, formatDateTime, formatNumber } from '../../utils'
import './changeouts.css'

function pair(before: number | null, after: number | null): string {
  if (before == null && after == null) return '–'
  return `${formatNumber(before, 2)} → ${formatNumber(after, 2)}`
}

/**
 * Die bisherigen Wasserwechsel eines Grows — Liste und Löschen.
 *
 * <b>Seit A-006 (05.10.2026) ohne eigenes Formular.</b> Eingetragen wird im
 * Ablauf darüber (`VorgangAblauf`), der Wechsel, Messungen, Verbrauch und
 * Tagebuch in einem Vorgang anlegt. Ein zweites Formular für dieselbe Handlung
 * wäre ein Befund, kein Feature (CLAUDE.md).
 *
 * <b>Altdaten</b> (Wechsel ohne Vorgang) stehen weiter in der Liste und lassen
 * sich wie bisher entfernen. Ein Wechsel aus dem Ablauf zeigt, was mit ihm
 * angelegt wurde — und Entfernen nimmt den ganzen Vorgang.
 *
 * @param markiert Vorgang, auf den ein Link zeigt (`?vorgang=`): er wird
 *   hervorgehoben und ins Bild gerollt.
 * @param onGeaendert Meldet nach oben, dass sich etwas geändert hat — die
 *   Seite lädt damit ihren Stand neu.
 */
export function ChangeoutsPanel({ growId, growName, neuLaden, markiert, onGeaendert, leerHinweis }: {
  growId: number
  growName: string
  neuLaden: number
  markiert?: number | null
  onGeaendert?: () => void
  /**
   * Was statt „noch kein Wasserwechsel" steht, wenn die Liste leer ist.
   *
   * <b>Gefunden am laufenden Stand (31.08.2026).</b> Oben stand „0 Tage seit
   * dem letzten Wechsel", unten „Noch kein Wasserwechsel" — beides richtig,
   * zusammen ein Widerspruch. Der letzte Wechsel kann auch aus einer Messung
   * stammen (Altdaten mit „Lösungswechsel").
   */
  leerHinweis?: string
}) {
  const [items, setItems] = useState<ChangeoutDto[]>([])
  const [vorgaenge, setVorgaenge] = useState<WasserwechselVorgangDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [refresh, setRefresh] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    async function run() {
      try {
        const [wechsel, liste] = await Promise.all([
          apiFetch<ChangeoutDto[]>(`/api/grows/${growId}/changeouts`, { signal: controller.signal }),
          apiFetch<WasserwechselVorgangDto[]>(`/api/grows/${growId}/wasserwechsel`, { signal: controller.signal }),
        ])
        if (controller.signal.aborted) return
        setItems([...wechsel].sort((a, b) => b.performedAtUtc.localeCompare(a.performedAtUtc)))
        setVorgaenge(liste)
        setError(null)
      } catch (caught) {
        if (controller.signal.aborted) return
        setError(caught instanceof ApiRequestError ? caught.message : 'Wasserwechsel konnten nicht geladen werden.')
      } finally {
        if (!controller.signal.aborted) setLoading(false)
      }
    }
    void run()
    return () => controller.abort()
  }, [growId, refresh, neuLaden])

  // Ein Link auf einen Vorgang: ins Bild rollen, sobald die Liste steht.
  useEffect(() => {
    if (markiert == null || loading) return
    document.querySelector(`[data-vorgang="${markiert}"]`)?.scrollIntoView({ block: 'center' })
  }, [markiert, loading])

  /**
   * Einen Eintrag zurücknehmen — beim Vorgang mit allem, was er angelegt hat.
   *
   * Mit Rückfrage, weil ein verlorener Eintrag nicht wiederherzustellen ist.
   */
  async function entfernen(eintrag: ChangeoutDto, vorgang: WasserwechselVorgangDto | undefined) {
    const wann = formatDateTime(eintrag.performedAtUtc)
    const mit = vorgang ? ` mit ${teileText(vorgang)}` : ''
    if (!window.confirm(`Wasserwechsel vom ${wann}${mit} wirklich entfernen?`)) return
    setError(null)
    setNotice(null)
    try {
      if (vorgang) await apiFetch(`/api/grows/${growId}/wasserwechsel/${vorgang.id}`, { method: 'DELETE' })
      else await apiFetch(`/api/grows/${growId}/changeouts/${eintrag.id}`, { method: 'DELETE' })
      setNotice(vorgang ? `Wasserwechsel mit ${teileText(vorgang)} entfernt.` : 'Wasserwechsel entfernt.')
      setRefresh((value) => value + 1)
      onGeaendert?.()
    } catch (caught) {
      setError(caught instanceof ApiRequestError ? caught.message : 'Entfernen fehlgeschlagen.')
    }
  }

  return (
    <V1Section title="Bisherige Wechsel" className="changeouts-section">
      {notice && <V1Alert title="Entfernt" message={notice} tone="ok" />}
      {error && <V1Alert message={error} tone="warn" />}

      {loading ? (
        <V1Skeleton rows={3} label="Lade Wasserwechsel" />
      ) : items.length === 0 ? (
        <V1Empty
          title="Noch kein Wechsel eingetragen"
          text={leerHinweis ?? `Für ${growName} ist noch kein Wasserwechsel eingetragen.`}
        />
      ) : (
        <div className="v1-list" data-audit="changeout-list">
          {items.map((item) => {
            const vorgang = vorgaenge.find((v) => v.wechsel?.id === item.id)
            return (
              <div key={item.id} className={classNames('v1-list-row', vorgang != null && vorgang.id === markiert && 'is-markiert')}
                data-vorgang={vorgang?.id}>
                <strong>{formatDateTime(item.performedAtUtc)}</strong>
                <span>
                  {item.kind === 'Full' ? 'Komplettwechsel' : 'Teilwechsel'}
                  {item.percentChanged != null && item.kind !== 'Full' ? ` · ${formatNumber(item.percentChanged, 0)} %` : ''}
                  {item.volumeChangedLiters != null ? ` · ${formatNumber(item.volumeChangedLiters, 1)} L` : ''}
                  {` · EC ${pair(item.ecBefore, item.ecAfter)} · pH ${pair(item.phBefore, item.phAfter)}`}
                  {item.notes ? ` · ${item.notes}` : ''}
                  {vorgang && <small className="changeouts-vorgang">Vorgang: {teileText(vorgang)}</small>}
                  {item.erinnerungNeuStarten === false && <small className="changeouts-vorgang">zählt nicht für die Erinnerung</small>}
                </span>
                {/* Deutsch, seit die Seite im Hauptmenue steht. „FULL"/„PART"
                    fielen der Wort-Pruefung nicht auf, weil sie nicht woertlich
                    die Enum-Werte sind. Gefunden vom Pruefer. */}
                <em>{item.kind === 'Full' ? 'ganz' : 'teils'}</em>
                <button
                  type="button"
                  className="ls-btn is-small changeouts-weg"
                  onClick={() => void entfernen(item, vorgang)}
                  aria-label={`Wasserwechsel vom ${formatDateTime(item.performedAtUtc)} entfernen`}
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
