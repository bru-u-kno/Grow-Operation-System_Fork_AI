import { useState } from 'react'
import { apiFetch, formatApiError } from '../../api'
import { V1Sheet } from '../../components/V1Sheet'
import { V1Alert, V1Button, V1Field, V1Switch, V1Tabs } from '../../components/v1'
import { classNames } from '../../utils'
import type { TentAlertRulesDto } from '../../types/alert'
import {
  UEBERGABE_JE_METRIK, alarmGeaendert, entwurfAus, planAenderungen, pruefen,
  regelnMitAenderung, zahlText, type AlarmRegel, type Entwurf, type PlanFeld,
} from './wert-blatt'

export type BlattWert = {
  key: string
  name: string
  einheit: string | null
  ist: string
  lage: string
  alarmVon: number | null
  alarmBis: number | null
  meldet: boolean
  regel: AlarmRegel | null
  planFelder: PlanFeld[] | null
  kette: Array<{ name: string; hinweis: string | null; wert: string | null; gilt: boolean; weg: boolean }>
}

export type BlattUebergabe = { rolle: string; name: string; wert: string; zustand: string }

const HERKUNFT: Record<string, string> = {
  programm: 'aus dem Programm',
  standard: 'aus dem Standard',
  eigen: 'von dir geändert',
  fehlt: 'fehlt — bitte eintragen',
}

/**
 * Fork AI (Grow-Plan, Schritt 2): alles zu EINER Messgröße in einem Blatt —
 * Ziel der laufenden Woche, Alarm, Pause zwischen Meldungen, was nach Home
 * Assistant geht und woher der Wert kommt.
 */
export function WertBlatt({ wert, growId, zeltId, spalteId, woche, uebergabe, onClose, onGespeichert }: {
  wert: BlattWert
  growId: number
  zeltId: number
  spalteId: string | null
  woche: string | null
  uebergabe: BlattUebergabe[]
  onClose: () => void
  onGespeichert: (meldung: string) => void
}) {
  const felder = wert.planFelder ?? []
  const [entwurf, setEntwurf] = useState<Entwurf>(() => entwurfAus(felder, wert.regel))
  const [fehler, setFehler] = useState<string | null>(null)
  const [speichert, setSpeichert] = useState(false)

  const aenderungen = spalteId ? planAenderungen(felder, entwurf, spalteId) : []
  const alarmNeu = alarmGeaendert(wert.regel, entwurf)
  const anzahl = aenderungen.length + (alarmNeu ? 1 : 0)
  const planMoeglich = wert.regel?.planMoeglich ?? false
  const hierhin = UEBERGABE_JE_METRIK[wert.key] ?? []
  const zeilen = uebergabe.filter((u) => hierhin.includes(u.rolle))

  const setz = (teil: Partial<Entwurf>) => setEntwurf((alt) => ({ ...alt, ...teil }))
  const setzPlan = (feld: string, text: string) =>
    setEntwurf((alt) => ({ ...alt, plan: { ...alt.plan, [feld]: text } }))

  async function speichern() {
    const problem = pruefen(felder, entwurf)
    if (problem) {
      setFehler(problem)
      return
    }
    setSpeichert(true)
    setFehler(null)
    try {
      if (aenderungen.length > 0) {
        await apiFetch(`/api/wochenplan/werte/${growId}`, {
          method: 'POST',
          body: JSON.stringify({ aenderungen }),
        })
      }
      if (alarmNeu) {
        // Frisch holen: der Server ersetzt den ganzen Satz, und der Wochenplan
        // kann ihn seit dem Laden der Seite nachgezogen haben.
        const aktuell = await apiFetch<TentAlertRulesDto>(`/api/alerts/tents/${zeltId}`)
        await apiFetch(`/api/alerts/tents/${zeltId}`, {
          method: 'PUT',
          body: JSON.stringify({ rules: regelnMitAenderung(aktuell.rules, wert.key, entwurf) }),
        })
      }
      onGespeichert(`${wert.name}: ${anzahl === 1 ? 'eine Änderung' : `${anzahl} Änderungen`} gespeichert.`)
    } catch (caught) {
      setFehler(formatApiError(caught, 'Speichern hat nicht geklappt.'))
    } finally {
      setSpeichert(false)
    }
  }

  return (
    <V1Sheet
      open
      onClose={onClose}
      title={wert.name}
      subtitle={`jetzt ${wert.ist}${wert.einheit ? ` ${wert.einheit}` : ''} · ${wert.lage}`}
      footer={(
        <div className="wb-fuss">
          <span>{anzahl === 0 ? 'Nichts geändert' : anzahl === 1 ? '1 Änderung' : `${anzahl} Änderungen`}</span>
          <V1Button variant="ghost" onClick={onClose}>Schließen</V1Button>
          <V1Button variant="primary" onClick={() => void speichern()} disabled={anzahl === 0 || speichert} audit="wert-blatt-speichern">
            {speichert ? 'Speichert…' : 'Speichern'}
          </V1Button>
        </div>
      )}
    >
      {fehler && <V1Alert message={fehler} tone="critical" />}

      <section className="wb-block">
        <h3>{woche ? `Ziel · ${woche}` : 'Ziel'}</h3>
        {felder.length === 0 || !spalteId ? (
          <p className="wb-hinweis">
            Für diesen Wert nennt der Plan kein Ziel. Die feste Alarmgrenze unten ist hier zugleich das Ziel.
          </p>
        ) : (
          <div className="wb-felder">
            {felder.map((f) => (
              <V1Field
                key={f.feld}
                label={`${f.bezeichnung}${f.einheit ? ` (${f.einheit})` : ''}`}
                hint={`Start ${f.startwert == null ? '–' : zahlText(f.startwert)} · ${HERKUNFT[f.herkunft] ?? f.herkunft}`}
              >
                <input
                  inputMode="decimal"
                  value={entwurf.plan[f.feld] ?? ''}
                  placeholder={f.herkunft === 'fehlt' ? 'eintragen' : ''}
                  className={classNames(aenderungen.some((a) => a.feld === f.feld) && 'wb-geaendert')}
                  onChange={(e) => setzPlan(f.feld, e.target.value)}
                />
              </V1Field>
            ))}
          </div>
        )}
        {felder.length > 0 && spalteId && (
          <p className="wb-hinweis">Gilt nur für diesen Grow und diese Woche.</p>
        )}
      </section>

      <section className="wb-block">
        <h3>Alarm</h3>
        {planMoeglich && (
          <V1Tabs
            label="Alarmgrenze"
            items={[{ value: 'Fest', label: 'Feste Zahlen' }, { value: 'Plan', label: 'Folgt dem Plan' }]}
            active={entwurf.quelle}
            onChange={(quelle) => setz({ quelle })}
          />
        )}
        {entwurf.quelle === 'Fest' ? (
          <div className="wb-felder">
            <V1Field label="melden unter">
              <input inputMode="decimal" value={entwurf.min} onChange={(e) => setz({ min: e.target.value })} />
            </V1Field>
            <V1Field label="melden über">
              <input inputMode="decimal" value={entwurf.max} onChange={(e) => setz({ max: e.target.value })} />
            </V1Field>
          </div>
        ) : (
          <div className="wb-felder">
            <V1Field
              label="Toleranz ±"
              hint={`leer = ${zahlText(wert.regel?.standardToleranz)}`}
            >
              <input inputMode="decimal" value={entwurf.toleranz} onChange={(e) => setz({ toleranz: e.target.value })} />
            </V1Field>
          </div>
        )}
        {entwurf.quelle === 'Fest' && (wert.regel?.nachtMin != null || wert.regel?.nachtMax != null) && (
          <p className="wb-hinweis">
            Nachts gilt {zahlText(wert.regel?.nachtMin)}–{zahlText(wert.regel?.nachtMax)} — das zieht der Wochenplan nach.
          </p>
        )}
        {(wert.alarmVon != null || wert.alarmBis != null) && (
          <p className="wb-hinweis">
            Zurzeit meldet er unter {zahlText(wert.alarmVon) || '–'} und über {zahlText(wert.alarmBis) || '–'}
            {wert.meldet ? ' — und der Wert liegt gerade draußen.' : '.'}
          </p>
        )}
        <div className="wb-felder">
          <V1Field label="höchstens alle … Minuten melden">
            <input inputMode="numeric" value={entwurf.karenz} onChange={(e) => setz({ karenz: e.target.value })} />
          </V1Field>
        </div>
        <V1Switch label="Alarm scharf" checked={entwurf.aktiv} onChange={(aktiv) => setz({ aktiv })} />
      </section>

      {zeilen.length > 0 && (
        <section className="wb-block">
          <h3>Geht nach Home Assistant</h3>
          {zeilen.map((u) => (
            <div key={u.rolle} className="wb-zeile">
              <span>{u.name}</span>
              <span>{u.wert} · {u.zustand}</span>
            </div>
          ))}
        </section>
      )}

      <section className="wb-block">
        <h3>Herkunft</h3>
        {wert.kette.map((stufe, i) => (
          <div key={stufe.name + i} className={classNames('wb-zeile', stufe.gilt && 'ist-gilt', stufe.weg && 'ist-weg')}>
            <span>{stufe.name}{stufe.hinweis ? ` · ${stufe.hinweis}` : ''}</span>
            <span>{stufe.wert ?? '–'}</span>
          </div>
        ))}
      </section>
    </V1Sheet>
  )
}
