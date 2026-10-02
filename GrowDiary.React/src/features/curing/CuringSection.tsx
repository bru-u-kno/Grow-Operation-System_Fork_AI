import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch, ApiRequestError } from '../../api'
import { V1Alert, V1Card, V1Section } from '../../components/v1'
import type { CuringJar } from './curing-typen'
import { faelligText, feuchteTon } from './curing-typen'
import { istUnlesbar, zahlOderNull } from '../../zahlenfeld'
import './curing.css'

type StrainOption = { id: number; name: string }

/**
 * Die Gläser dieses Grows — direkt am Grow, wo die Ernte auch steht.
 *
 * Hier wird eingeglast; das tägliche Lüften wohnt auf der Seite „Aushärten",
 * die alle Gläser über alle Grows zeigt. Denn wer vor dem Schrank steht, hat
 * meist mehrere Gläser vor sich und nicht einen Grow im Kopf.
 */
export function CuringSection({ growId, harvested }: { growId: number; harvested: boolean }) {
  const [jars, setJars] = useState<CuringJar[]>([])
  const [strains, setStrains] = useState<StrainOption[]>([])
  const [error, setError] = useState<string | null>(null)
  const [refresh, setRefresh] = useState(0)
  const [neu, setNeu] = useState({ label: '', filledAtLocal: heute(), weightG: '', strainId: '', hasHumidityPack: false })
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    async function load() {
      try {
        const [geladen, sorten] = await Promise.all([
          apiFetch<CuringJar[]>(`/api/grows/${growId}/curing/jars`, { signal: controller.signal }),
          apiFetch<StrainOption[]>('/api/strains', { signal: controller.signal }).catch(() => []),
        ])
        if (!controller.signal.aborted) {
          setJars(geladen)
          setStrains(sorten)
        }
      } catch {
        /* Ohne Gläser bleibt der Abschnitt leer — kein Grund, die Seite zu stören. */
      }
    }
    void load()
    return () => controller.abort()
  }, [growId, refresh])

  async function anlegen() {
    // Bisher wurde „21,5g" still zu NaN und im JSON damit zu `null` — das Glas
    // stand ohne Gewicht da, mit Erfolgsmeldung.
    if (istUnlesbar(neu.weightG)) {
      setError('„Gewicht im Glas" ist keine Zahl. Bitte korrigieren oder das Feld leeren.')
      return
    }
    setBusy(true)
    setError(null)
    try {
      await apiFetch(`/api/grows/${growId}/curing/jars`, {
        method: 'POST',
        body: JSON.stringify({
          label: neu.label.trim(),
          filledAtLocal: neu.filledAtLocal,
          weightG: zahlOderNull(neu.weightG),
          strainId: neu.strainId ? Number(neu.strainId) : null,
          hasHumidityPack: neu.hasHumidityPack,
        }),
      })
      setNeu({ label: '', filledAtLocal: heute(), weightG: '', strainId: '', hasHumidityPack: false })
      setRefresh((n) => n + 1)
    } catch (caught) {
      setError(caught instanceof ApiRequestError ? caught.message : 'Glas konnte nicht angelegt werden.')
    } finally {
      setBusy(false)
    }
  }

  // Vor der Ernte gibt es nichts einzuglasen — dann bleibt der Abschnitt weg,
  // statt ein leeres Formular an einen bluehenden Grow zu haengen.
  if (!harvested && jars.length === 0) return null

  // `== null` faengt null UND undefined: der Server laesst leere Felder ganz
  // weg (JsonIgnoreCondition.WhenWritingNull in Program.cs), es kommt also
  // `undefined` an. Mit `=== null` war die Liste immer leer und der Hinweis
  // darunter erschien nie.
  const offen = jars.filter((jar) => jar.finishedAtUtc == null)

  return (
    <V1Section title="Aushärten">
      <V1Card>
        {error && <V1Alert message={error} tone="warn" />}

        <div className="cu-inline" data-audit="grow-curing">
          {jars.length === 0 ? (
            <p className="cu-hint">
              Noch kein Glas. Nach dem Trocknen kommt das Aushärten: 30 bis 60 Tage im
              Glas bei 58–62 % Feuchte, die erste Woche täglich lüften. Leg hier ein Glas
              an, dann steht es unter <Link to="/aushaerten">Aushärten</Link> mit seinem
              Lüft-Rhythmus.
            </p>
          ) : (
            jars.map((jar) => (
              <div className="cu-inline-row" key={jar.id}>
                <strong>{jar.label}</strong>
                <span className="cu-facts">
                  {jar.strainName ? `${jar.strainName} · ` : ''}
                  Tag {jar.duty.dayInCure}
                  {jar.weightG != null ? ` · ${jar.weightG.toLocaleString('de-DE')} g` : ''}
                </span>
                {jar.latestHumidity && (
                  <span className={`ls-pill is-${feuchteTon(jar.latestHumidity.level)}`}>
                    {jar.latestHumidity.percent.toLocaleString('de-DE')} %
                  </span>
                )}
                <span className="cu-alter">{faelligText(jar.duty)}</span>
              </div>
            ))
          )}

          {offen.length > 0 && (
            <p className="cu-hint">
              Lüften und Feuchte eintragen: unter <Link to="/aushaerten">Aushärten</Link>.
            </p>
          )}

          <div className="cu-new">
            <label>
              <span>Bezeichnung</span>
              <input
                type="text"
                value={neu.label}
                onChange={(event) => setNeu((c) => ({ ...c, label: event.target.value }))}
                placeholder="Glas 1"
                aria-label="Bezeichnung des Glases"
              />
            </label>
            <label>
              <span>Eingeglast</span>
              <input
                type="date"
                value={neu.filledAtLocal}
                onChange={(event) => setNeu((c) => ({ ...c, filledAtLocal: event.target.value }))}
                aria-label="Datum des Einglasens"
              />
            </label>
            <label>
              <span>Gramm</span>
              <input
                type="text"
                inputMode="decimal"
                value={neu.weightG}
                onChange={(event) => setNeu((c) => ({ ...c, weightG: event.target.value }))}
                placeholder="80"
                aria-label="Gewicht im Glas"
              />
            </label>
            {strains.length > 1 && (
              <label>
                <span>Sorte</span>
                <select
                  value={neu.strainId}
                  onChange={(event) => setNeu((c) => ({ ...c, strainId: event.target.value }))}
                  aria-label="Sorte im Glas"
                >
                  <option value="">— alle —</option>
                  {strains.map((strain) => <option key={strain.id} value={strain.id}>{strain.name}</option>)}
                </select>
              </label>
            )}
            <label className="cu-check">
              <input
                type="checkbox"
                checked={neu.hasHumidityPack}
                onChange={(event) => setNeu((c) => ({ ...c, hasHumidityPack: event.target.checked }))}
              />
              <span>Feuchtigkeitsregler drin</span>
            </label>
            <button
              type="button"
              className="ls-btn is-small is-primary"
              disabled={busy || !neu.label.trim()}
              onClick={() => void anlegen()}
            >
              Glas anlegen
            </button>
          </div>
        </div>
      </V1Card>
    </V1Section>
  )
}

/** Heute als „2026-08-17" — was ein date-Feld erwartet. */
function heute(): string {
  const jetzt = new Date()
  return `${jetzt.getFullYear()}-${String(jetzt.getMonth() + 1).padStart(2, '0')}-${String(jetzt.getDate()).padStart(2, '0')}`
}
