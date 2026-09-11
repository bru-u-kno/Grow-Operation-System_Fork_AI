import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch } from '../../api'
import { classNames } from '../../utils'
import './gilt-gerade.css'

/**
 * Fork AI: „Gilt gerade" — was für die laufenden Grows wirklich als Ziel gilt.
 *
 * Die Profiltabelle darunter zeigt Werte je PHASE. Das Feed-Chart überschreibt
 * EC und pH aber je WOCHE, und die eigenen Grenzwerte des Zelts stehen noch
 * darüber. Ohne diesen Kopf liest man auf der Seite eine Zahl und im
 * Messprotokoll eine andere — und hält das für einen Fehler.
 *
 * Bewusst ohne Grow-Auswahl: die Seite gehört zu den Einstellungen. Laufen
 * zwei Grows, stehen zwei Karten da; läuft keiner, steht hier gar nichts.
 */

type Ziel = { key: string; label: string; wert: string; vomWochenplan: boolean }

type Geltend = {
  growId: number
  growName: string
  phase: string
  profilName: string
  profilHerkunft: string
  wochenplan: string | null
  anmischen: string | null
  werte: Ziel[]
}

function GiltGerade() {
  const [eintraege, setEintraege] = useState<Geltend[]>([])

  useEffect(() => {
    const controller = new AbortController()
    async function laden() {
      try {
        const data = await apiFetch<Geltend[]>('/api/setpoint-profiles/gilt-gerade', { signal: controller.signal })
        if (!controller.signal.aborted) setEintraege(data)
      } catch {
        // Stiller Ausfall mit Absicht: das hier ist eine Zusatzauskunft. Eine
        // rote Meldung über der Profilliste wäre lauter als ihr Nutzen, und
        // die Liste selbst lädt unabhängig davon.
      }
    }
    void laden()
    return () => controller.abort()
  }, [])

  if (eintraege.length === 0) return null

  return (
    <>
      {eintraege.map((eintrag) => (
        <section key={eintrag.growId} className="gg" data-audit={`gilt-gerade-${eintrag.growId}`}>
          <div className="gg-kopf">Gilt gerade · {eintrag.growName}</div>
          <p className="gg-zeile">
            <b>{eintrag.profilName}</b> · {eintrag.phase} <span className="gg-leise">({eintrag.profilHerkunft})</span>
          </p>
          {eintrag.wochenplan && (
            <p className="gg-zeile gg-leiser">
              Wochen-Ziele:{' '}
              {/* Ziel ist /wissen ohne Parameter: die Seite kennt keinen
                  Deep-Link auf einen Eintrag (kein useSearchParams). Ein
                  ?record=… sähe aus wie ein Sprung und landete nirgends. */}
              <Link className="gg-link" to="/wissen">
                {eintrag.wochenplan}
              </Link>
            </p>
          )}

          <div className="gg-chips">
            {eintrag.werte.map((wert) => (
              <span key={wert.key} className={classNames('gg-chip', wert.vomWochenplan && 'ist-wochenplan')}>
                {wert.label} <b>{wert.wert}</b>
              </span>
            ))}
          </div>

          <p className="gg-fuss">
            {eintrag.wochenplan
              ? `Blau = kommt aus dem Wochenplan, nicht aus dem Profil. Das Chart kennt nur EC und pH.${
                  eintrag.anmischen ? ` Anmischen laut Chart: ${eintrag.anmischen}.` : ''
                }`
              : 'Alle Werte kommen aus dem Profil — dieser Grow benutzt keine Wochen-Ziele.'}
          </p>
        </section>
      ))}
    </>
  )
}

export default GiltGerade
