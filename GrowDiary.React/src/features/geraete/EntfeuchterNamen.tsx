import { useEffect, useMemo, useState } from 'react'
import { apiFetch, formatApiError } from '../../api'
import { V1Alert, V1Button, V1Section, V1Skeleton } from '../../components/v1'
import { namenAenderung, wirksamerName } from '../steuerung/entfeuchter-zusatz'
import type { EntfeuchterNamen as Namen } from '../steuerung/steuerung-typen'
import '../steuerung/steuerung.css'

/**
 * A-009: Der Anzeigename je Entfeuchter-Gerät.
 *
 * <b>Eine Stelle für den Namen.</b> Steuerung, Meldungen und Chip-Leiste
 * benutzen alle diesen Namen — kein „Port 7" fest im Text (ENTSCHEIDUNGEN.md,
 * Punkt 6). Die Vorgabe ist der Name der Entität in Home Assistant; leer lassen
 * heißt „der Vorgabe folgen", auch wenn sie dort einmal umbenannt wird.
 *
 * Gespeichert wird nur, was sich ändert (`namenAenderung`).
 */
export function EntfeuchterNamen() {
  const [namen, setNamen] = useState<Namen | null>(null)
  const [eingabe, setEingabe] = useState({ fuehrung: '', zusatz: '' })
  const [fehler, setFehler] = useState<string | null>(null)
  const [hinweis, setHinweis] = useState<string | null>(null)
  const [laedt, setLaedt] = useState(true)
  const [speichert, setSpeichert] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    const laden = async () => {
      try {
        const geladen = await apiFetch<Namen>('/api/steuerung/entfeuchter-namen', { signal: controller.signal })
        if (controller.signal.aborted) return
        setNamen(geladen)
        setEingabe({ fuehrung: geladen.fuehrung.anzeigename, zusatz: geladen.zusatz.anzeigename })
        setFehler(null)
      } catch (caught) {
        if (!controller.signal.aborted) setFehler(formatApiError(caught, 'Die Namen der Entfeuchter konnten nicht geladen werden.'))
      } finally {
        if (!controller.signal.aborted) setLaedt(false)
      }
    }
    void laden()
    return () => controller.abort()
  }, [])

  const koerper = useMemo(() => (namen ? namenAenderung(namen, eingabe) : {}), [namen, eingabe])
  const geaendert = Object.keys(koerper).length > 0

  const speichern = async () => {
    setSpeichert(true); setHinweis(null)
    try {
      const zurueck = await apiFetch<Namen>('/api/steuerung/entfeuchter-namen', { method: 'PUT', body: JSON.stringify(koerper) })
      setNamen(zurueck)
      setEingabe({ fuehrung: zurueck.fuehrung.anzeigename, zusatz: zurueck.zusatz.anzeigename })
      setFehler(null)
      setHinweis('Gespeichert.')
    } catch (caught) {
      setFehler(formatApiError(caught, 'Die Namen konnten nicht gespeichert werden.'))
    } finally {
      setSpeichert(false)
    }
  }

  if (laedt) return <V1Skeleton label="Namen werden geladen" rows={2} />

  return (
    <V1Section title="Namen der Entfeuchter">
      <div className="ez-namen">
        {fehler && <V1Alert tone="critical" message={fehler} />}
        {hinweis && <V1Alert tone="ok" message={hinweis} />}
        {namen && (
          <>
            {([
              { rolle: 'fuehrung', label: 'Entfeuchter 1 · führt' },
              { rolle: 'zusatz', label: 'Entfeuchter 2 · Zusatz' },
            ] as const).map(({ rolle, label }) => {
              const vorgabe = namen[rolle].vorgabe
              return (
                <div key={rolle} className="st-feldzeile is-gestapelt">
                  <span className="st-etikett">
                    {label}
                    <small>Vorgabe aus Home Assistant: {vorgabe}</small>
                  </span>
                  <div className="ez-namen-zeile">
                    <input
                      value={eingabe[rolle]}
                      placeholder={vorgabe}
                      aria-label={`Name von ${label}`}
                      onChange={(event) => setEingabe({ ...eingabe, [rolle]: event.target.value })}
                    />
                    {wirksamerName(eingabe[rolle], vorgabe) !== vorgabe && (
                      <V1Button onClick={() => setEingabe({ ...eingabe, [rolle]: '' })}>Vorgabe</V1Button>
                    )}
                  </div>
                </div>
              )
            })}
            <p className="st-hinweis">Der Name gilt überall in der App: Steuerung, Meldungen, Chip-Leiste. Leer lassen heißt: der Vorgabe folgen.</p>
            <div className="st-knopfleiste">
              <V1Button variant="primary" disabled={!geaendert || speichert} onClick={() => void speichern()}>
                {speichert ? 'Speichert…' : 'Namen speichern'}
              </V1Button>
            </div>
          </>
        )}
      </div>
    </V1Section>
  )
}
