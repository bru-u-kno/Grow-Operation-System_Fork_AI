import { useEffect, useState } from 'react'
import { apiFetch, ApiRequestError, formatApiError } from '../../api'
import { V1Sheet } from '../../components/V1Sheet'
import { V1Alert, V1Badge, V1Button, V1Field, V1Section } from '../../components/v1'
import { classNames } from '../../utils'
import { GROESSEN, dosierungZeilen, feldName, zeilen, zeitleiste, type Auswertung } from './plan-auswertung'
import { buchText } from './plan-reiter'
import './zielwerte.css'

/**
 * Fork AI (Grow-Plan, Schritt 6b): „Plan · Auswertung“ auf der Grow-Seite.
 *
 * Für laufende und abgeschlossene Grows gleich: je Woche Start, Ende (bzw.
 * aktueller Stand) und gemessener Mittelwert, darunter die Zeitleiste der
 * Änderungen. Abgeschlossene Grows ohne Plan sagen das ehrlich.
 */
export function PlanAuswertung({ growId }: { growId: number }) {
  const [daten, setDaten] = useState<Auswertung | null>(null)
  const [keinPlan, setKeinPlan] = useState(false)
  const [groesse, setGroesse] = useState('ec')
  const [blatt, setBlatt] = useState(false)
  const [name, setName] = useState('')
  const [meldung, setMeldung] = useState<{ text: string; ton: 'ok' | 'critical' } | null>(null)
  const [speichert, setSpeichert] = useState(false)

  useEffect(() => {
    async function laden() {
      try {
        setDaten(await apiFetch<Auswertung>(`/api/grows/${growId}/plan/auswertung`))
      } catch (caught) {
        if (caught instanceof ApiRequestError && caught.status === 404) setKeinPlan(true)
        else setMeldung({ ton: 'critical', text: formatApiError(caught, 'Die Auswertung konnte nicht geladen werden.') })
      }
    }
    void laden()
  }, [growId])

  if (keinPlan) {
    return (
      <V1Section title="Plan · Auswertung">
        <p className="pa-leer">Für diesen Grow ist kein Plan gespeichert — er lief vor der Einführung der Grow-Pläne oder ohne Düngeprogramm.</p>
      </V1Section>
    )
  }
  if (!daten) return meldung ? <V1Alert tone={meldung.ton} message={meldung.text} /> : null

  const liste = groesse === 'dosierung' ? dosierungZeilen(daten) : zeilen(daten, groesse)
  const geaendert = liste.filter((z) => z.geaendert).length
  const ereignisse = zeitleiste(daten)
  const wochenName = (id: string | null) => daten.wochen.find((w) => w.id === id)?.label ?? ''

  async function alsProgramm() {
    setSpeichert(true)
    setMeldung(null)
    try {
      const antwort = await apiFetch<{ programmName: string }>(`/api/grows/${growId}/plan/als-programm`, {
        method: 'POST',
        body: JSON.stringify({ name }),
      })
      setBlatt(false)
      setMeldung({ ton: 'ok', text: `Gespeichert als Programm „${antwort.programmName}“ — beim nächsten Grow unter „Eigene Programme“.` })
      setDaten(await apiFetch<Auswertung>(`/api/grows/${growId}/plan/auswertung`))
    } catch (caught) {
      setMeldung({ ton: 'critical', text: formatApiError(caught, 'Das Programm konnte nicht gespeichert werden.') })
    } finally {
      setSpeichert(false)
    }
  }

  return (
    <V1Section
      title="Plan · Auswertung"
      action={<V1Button variant="secondary" onClick={() => { setName(`${daten.growName} · ${daten.programmName}`); setBlatt(true) }} audit="plan-als-programm">Als Programm</V1Button>}
    >
      <div className="pa" data-audit="plan-auswertung">
        <p className="pr-kopf">
          Plan aus <b>{daten.programmName}</b>
          {daten.startProgrammName && daten.startProgrammName !== daten.programmName && <> · gestartet mit {daten.startProgrammName}</>}
          {daten.eingefroren
            ? <V1Badge tone="neutral">eingefroren {daten.eingefrorenUtc ? new Date(daten.eingefrorenUtc).toLocaleDateString('de-DE') : ''}</V1Badge>
            : <V1Badge tone="accent">läuft</V1Badge>}
          {daten.startVermerk && <V1Badge tone="warn">Start {daten.startVermerk}</V1Badge>}
        </p>
        {meldung && <V1Alert tone={meldung.ton} message={meldung.text} />}

        <div className="pa-chips" role="tablist" aria-label="Messgröße">
          {[...GROESSEN.map((g) => ({ key: g.key, label: g.label })), { key: 'dosierung', label: 'Dosierung' }].map((g) => (
            <button key={g.key} type="button" role="tab" aria-selected={groesse === g.key}
              className={classNames('pa-chip', groesse === g.key && 'ist-an')} onClick={() => setGroesse(g.key)}>
              {g.label}
            </button>
          ))}
        </div>

        <table className="pa-tabelle">
          <thead>
            <tr>
              <th>Woche</th><th>Start</th><th>{daten.eingefroren ? 'Ende' : 'Jetzt'}</th>
              {groesse !== 'dosierung' && <th>gemessen</th>}
            </tr>
          </thead>
          <tbody>
            {liste.map((z) => (
              <tr key={z.id}>
                <td>{z.label}</td>
                <td>{z.start}</td>
                <td className={classNames(z.geaendert && 'ist-geaendert')}>{z.ende}</td>
                {groesse !== 'dosierung' && <td className={classNames(z.abweichung && 'ist-ab')}>{z.gemessen}</td>}
              </tr>
            ))}
          </tbody>
        </table>
        <p className="pa-legende">
          <span className="ist-geaendert">geändert</span> {geaendert > 0 ? `in ${geaendert} Woche${geaendert === 1 ? '' : 'n'}` : ''}
          {groesse !== 'dosierung' && <> · <span className="ist-ab">außerhalb</span> des Ziels (mit Alarm-Toleranz)</>}
          {' '}· gemessen = Mittelwert der gespeicherten Messungen; CO₂ und VPD werden dort nicht gespeichert.
        </p>

        <h3 className="program-gruppe">Zeitleiste · {ereignisse.length}</h3>
        {ereignisse.length === 0 && <p className="pa-leer">Noch keine Änderungen.</p>}
        <div className="pa-zeit">
          {ereignisse.map((e) => (
            <div key={e.id}>
              <b>{new Date(e.zeitUtc).toLocaleDateString('de-DE')}</b>
              <span>
                {e.spalteId && `${wochenName(e.spalteId)} · `}{buchText(e, feldName)}
                {e.grund && ` · „${e.grund}“`}
              </span>
            </div>
          ))}
        </div>
      </div>

      <V1Sheet
        open={blatt}
        onClose={() => !speichert && setBlatt(false)}
        title="Als Programm speichern"
        subtitle={daten.eingefroren ? 'aus dem eingefrorenen Endstand' : 'aus dem aktuellen Stand des Plans'}
        footer={(
          <div className="wb-fuss">
            <span />
            <V1Button variant="ghost" onClick={() => setBlatt(false)} disabled={speichert}>Abbrechen</V1Button>
            <V1Button variant="primary" onClick={() => void alsProgramm()} disabled={speichert || name.trim() === ''} audit="plan-als-programm-speichern">
              {speichert ? 'Speichert …' : 'Speichern'}
            </V1Button>
          </div>
        )}
      >
        <section className="wb-block">
          <V1Field label="Name des Programms" hint="Steht beim nächsten Grow unter „Eigene Programme“.">
            <input value={name} maxLength={80} onChange={(e) => setName(e.target.value)} />
          </V1Field>
        </section>
      </V1Sheet>
    </V1Section>
  )
}
