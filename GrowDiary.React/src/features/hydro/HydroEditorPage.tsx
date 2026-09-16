/* src/features/hydro/HydroEditorPage.tsx
   Ersetzt den 5-Schritt-Wizard aus HydroPage.
   Prinzip: Typ zuerst, alles auf einer Seite, irrelevante Felder werden
   AUSGEBLENDET statt deaktiviert, und die Draufsicht prueft live mit. */

import { useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { apiFetch, formatApiError } from '../../api'
import type { CreateHydroSetupRequest, HydroSetupDto, ReservoirPosition, SelectableHydroStyle } from '../../types'
import { V1Alert, V1Button, V1Field } from '../../components/v1'
import { SystemPlan } from './SystemPlan'
import { buildSystemPlan, layoutTypeFromRows, rowsFromLayoutType } from './system-plan-model'
import { useHydroSetups } from './useHydroSetups'
import './hydro.css'

type Draft = {
  name: string
  tentId: string
  hydroStyle: SelectableHydroStyle
  siteCount: number
  rows: number
  potLiters: number
  tankLiters: number
  reservoirPosition: ReservoirPosition
  hasCirculationPump: boolean
  hasAirPump: boolean
  airPumpLitersPerHour: string
  airStoneCount: number
  hasChiller: boolean
  hasUvSterilizer: boolean
  notes: string
  setpointProfileId: string | null
}

const emptyDraft: Draft = {
  name: '', tentId: '', hydroStyle: 'RDWC', siteCount: 4, rows: 2, setpointProfileId: null,
  potLiters: 19, tankLiters: 60, reservoirPosition: 'Left',
  hasCirculationPump: true, hasAirPump: true, airPumpLitersPerHour: '', airStoneCount: 4,
  hasChiller: false, hasUvSterilizer: false, notes: '',
}

const tankPositions: Array<{ value: ReservoirPosition; label: string }> = [
  { value: 'Left', label: 'Links' },
  { value: 'Right', label: 'Rechts' },
  { value: 'Top', label: 'Oben' },
  { value: 'Bottom', label: 'Unten' },
  { value: 'External', label: 'Anderer Raum' },
]

export default function HydroEditorPage() {
  const navigate = useNavigate()
  const params = useParams<{ id?: string }>()
  const editingId = params.id ? Number(params.id) : null
  const { tents, setups, loading } = useHydroSetups()
  const [draft, setDraft] = useState<Draft>(emptyDraft)
  const [hydrated, setHydrated] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Bestehendes Setup einmalig in den Draft uebernehmen
  const existing = editingId ? setups.find((setup) => setup.id === editingId) : undefined
  if (existing && !hydrated) {
    setDraft({
      name: existing.name,
      tentId: existing.tentId ? String(existing.tentId) : '',
      hydroStyle: existing.hydroStyle === 'DWC' ? 'DWC' : 'RDWC',
      setpointProfileId: existing.setpointProfileId ?? null,
      siteCount: existing.potCount ?? 1,
      rows: rowsFromLayoutType(existing.layoutType, existing.potCount ?? 1),
      potLiters: existing.potSizeLiters ?? 19,
      tankLiters: existing.reservoirLiters ?? 0,
      reservoirPosition: existing.reservoirPosition,
      hasCirculationPump: existing.hasCirculationPump,
      hasAirPump: existing.hasAirPump,
      airPumpLitersPerHour: existing.airPumpLitersPerHour == null ? '' : String(existing.airPumpLitersPerHour).replace('.', ','),
      airStoneCount: existing.airStoneCount ?? 0,
      hasChiller: existing.hasChiller,
      hasUvSterilizer: existing.hasUvSterilizer,
      notes: existing.notes ?? '',
    })
    setHydrated(true)
  }

  const tent = tents.find((item) => String(item.id) === draft.tentId)
  const isRdwc = draft.hydroStyle === 'RDWC'
  const patch = (values: Partial<Draft>) => setDraft((current) => ({ ...current, ...values }))

  const plan = useMemo(() => buildSystemPlan({
    hydroStyle: draft.hydroStyle,
    siteCount: draft.siteCount,
    rows: draft.rows,
    potLiters: draft.potLiters,
    tankLiters: draft.tankLiters,
    reservoirPosition: draft.reservoirPosition,
    tentWidthCm: tent?.widthCm ?? null,
    tentDepthCm: tent?.depthCm ?? null,
  }), [draft, tent])

  const problems: string[] = []
  if (!draft.name.trim()) problems.push('Name fehlt.')
  if (!draft.tentId) problems.push('Zelt waehlen — sonst kann der Plan nicht maßstabsgetreu rechnen.')
  if (isRdwc && draft.siteCount < 2) problems.push('RDWC braucht mindestens zwei Sites.')
  if (draft.potLiters <= 0) problems.push('Topfvolumen fehlt.')

  const tankThin = isRdwc && draft.tankLiters < draft.siteCount * draft.potLiters * 0.25

  async function save() {
    if (problems.length > 0) { setError(problems[0]); return }
    setSaving(true)
    setError(null)
    try {
      const request: CreateHydroSetupRequest = {
        tentId: draft.tentId ? Number(draft.tentId) : null,
        name: draft.name.trim(),
        hydroStyle: draft.hydroStyle,
        setpointProfileId: draft.setpointProfileId,
        potCount: isRdwc ? draft.siteCount : 1,
        potSizeLiters: draft.potLiters,
        reservoirLiters: isRdwc ? draft.tankLiters : null,
        layoutType: layoutTypeFromRows(draft.rows, isRdwc ? draft.siteCount : 1) as CreateHydroSetupRequest['layoutType'],
        reservoirPosition: isRdwc ? draft.reservoirPosition : 'None',
        hasCirculationPump: draft.hasCirculationPump,
        hasAirPump: draft.hasAirPump,
        airPumpLitersPerHour: draft.airPumpLitersPerHour.trim() === '' ? null : Number(draft.airPumpLitersPerHour.replace(',', '.')) || null,
        airStoneCount: draft.airStoneCount,
        hasChiller: draft.hasChiller,
        hasUvSterilizer: draft.hasUvSterilizer,
        notes: draft.notes.trim() || null,
        displayOrder: existing?.displayOrder ?? setups.length + 1,
      }
      const saved = editingId
        ? await apiFetch<HydroSetupDto>(`/api/hydro-setups/${editingId}`, { method: 'PUT', body: JSON.stringify({ ...request, status: existing?.status ?? 'Active' }) })
        : await apiFetch<HydroSetupDto>('/api/hydro-setups', { method: 'POST', body: JSON.stringify(request) })
      navigate(`/hydro/${saved.id}`)
    } catch (caught) {
      setError(formatApiError(caught, 'Hydro-Setup konnte nicht gespeichert werden.'))
    } finally {
      setSaving(false)
    }
  }

  return (
    <main className="v1-page hydro-editor">
      <header className="v1-hero">
        <div>
          <div className="v1-eyebrow">Anlage / Hydro / {editingId ? 'Bearbeiten' : 'Neu'}</div>
          <h1>{editingId ? 'System bearbeiten' : 'System anlegen'}</h1>
          <p>Eine Seite statt fuenf Schritte. Links eingeben, rechts sofort sehen — die Draufsicht prueft mit, ob das System ins Zelt passt.</p>
        </div>
      </header>

      {error && <V1Alert message={error} tone="warn" />}

      <div className="v1-split">
        <div className="hydro-editor__form">
          <section className="v1-section">
            <header className="v1-section-head"><h2>1 · Systemtyp</h2></header>
            <div className="v1-section-body v1-choice-grid">
              <button type="button" className={`v1-choice ${isRdwc ? 'active' : ''}`} onClick={() => patch({ hydroStyle: 'RDWC', siteCount: Math.max(2, draft.siteCount) })}>
                <strong>RDWC</strong>
                <span>Mehrere Sites, gemeinsamer Tank, Umwaelzpumpe. Addback zentral.</span>
              </button>
              <button type="button" className={`v1-choice ${!isRdwc ? 'active' : ''}`} onClick={() => patch({ hydroStyle: 'DWC' })}>
                <strong>DWC</strong>
                <span>Ein Eimer, kein Tank, kein Layout. Addback pro Eimer.</span>
              </button>
            </div>
          </section>

          <section className="v1-section">
            <header className="v1-section-head"><h2>2 · Name &amp; Zelt</h2></header>
            <div className="v1-section-body v1-form-grid">
              <V1Field label="Name">
                <input value={draft.name} onChange={(event) => patch({ name: event.target.value })} placeholder="RDWC 6-Site" />
              </V1Field>
              <V1Field label="Zelt" hint={tent && !tent.widthCm ? 'Am Zelt fehlen die Maße — Plan rechnet mit 120×120.' : null}>
                <select value={draft.tentId} onChange={(event) => patch({ tentId: event.target.value })}>
                  <option value="">Zelt waehlen</option>
                  {tents.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}
                </select>
              </V1Field>
            </div>
          </section>

          <section className="v1-section">
            <header className="v1-section-head">
              <h2>3 · Sites &amp; Volumen</h2>
              <span className="v1-badge">{plan.cols}×{plan.rows} · {plan.totalLiters} L</span>
            </header>
            <div className="v1-section-body v1-form-grid">
              {isRdwc && (
                <V1Field label={`Sites — ${draft.siteCount}`} wide hint="Das Raster ergibt sich aus Sites und Reihen. Ein Widerspruch wie „6 Sites / 2×2“ ist damit unmoeglich.">
                  <input type="range" min={2} max={12} step={1} value={draft.siteCount} onChange={(event) => patch({ siteCount: Number(event.target.value) })} />
                </V1Field>
              )}
              {isRdwc && (
                <V1Field label="Anordnung" wide>
                  <div className="v1-chip-row">
                    {[1, 2, 3].map((rows) => (
                      <button key={rows} type="button" className={`v1-button ${draft.rows === rows ? 'is-primary' : ''}`} onClick={() => patch({ rows })}>
                        {rows} {rows === 1 ? 'Reihe' : 'Reihen'}
                      </button>
                    ))}
                  </div>
                </V1Field>
              )}
              <V1Field label="Liter pro Site">
                <input type="number" min={4} max={80} value={draft.potLiters} onChange={(event) => patch({ potLiters: Number(event.target.value) })} />
              </V1Field>
              {isRdwc && (
                <V1Field label="Tank Liter" hint={tankThin ? 'Klein gegenueber den Eimern — pH driftet schneller.' : null}>
                  <input type="number" min={0} max={600} value={draft.tankLiters} onChange={(event) => patch({ tankLiters: Number(event.target.value) })} />
                </V1Field>
              )}
              {isRdwc && (
                <V1Field label="Tankposition" wide>
                  <div className="v1-chip-row">
                    {tankPositions.map((option) => (
                      <button key={option.value} type="button" className={`v1-button ${draft.reservoirPosition === option.value ? 'is-primary' : ''}`} onClick={() => patch({ reservoirPosition: option.value })}>
                        {option.label}
                      </button>
                    ))}
                  </div>
                </V1Field>
              )}
            </div>
          </section>

          <section className="v1-section">
            <header className="v1-section-head"><h2>4 · Technik — optional</h2></header>
            <div className="v1-section-body v1-form-grid">
              <label className="v1-switch"><input type="checkbox" checked={draft.hasCirculationPump} onChange={(event) => patch({ hasCirculationPump: event.target.checked })} /><strong>Umwaelzpumpe</strong></label>
              <label className="v1-switch"><input type="checkbox" checked={draft.hasAirPump} onChange={(event) => patch({ hasAirPump: event.target.checked })} /><strong>Luftpumpe</strong></label>
              <label className="v1-switch"><input type="checkbox" checked={draft.hasChiller} onChange={(event) => patch({ hasChiller: event.target.checked })} /><strong>Chiller</strong></label>
              <label className="v1-switch"><input type="checkbox" checked={draft.hasUvSterilizer} onChange={(event) => patch({ hasUvSterilizer: event.target.checked })} /><strong>UV-C</strong></label>
              <V1Field label="Luftsteine"><input type="number" min={0} max={24} value={draft.airStoneCount} onChange={(event) => patch({ airStoneCount: Number(event.target.value) })} /></V1Field>
              <V1Field label="Luftpumpe (L/h)" hint="Steht auf dem Karton — z. B. V-20: 1200. Daraus schätzt Grow OS, ob die Belüftung reicht.">
                <input inputMode="decimal" value={draft.airPumpLitersPerHour} onChange={(event) => patch({ airPumpLitersPerHour: event.target.value })} placeholder="z. B. 1200" />
              </V1Field>
              <V1Field label="Notizen" wide><textarea rows={3} value={draft.notes} onChange={(event) => patch({ notes: event.target.value })} /></V1Field>
            </div>
          </section>
        </div>

        <aside className="hydro-editor__preview">
          <section className="v1-card">
            <SystemPlan
              hydroStyle={draft.hydroStyle}
              siteCount={draft.siteCount}
              rows={draft.rows}
              potLiters={draft.potLiters}
              tankLiters={draft.tankLiters}
              reservoirPosition={draft.reservoirPosition}
              tentWidthCm={tent?.widthCm ?? null}
              tentDepthCm={tent?.depthCm ?? null}
            />
          </section>

          <section className="v1-section">
            <header className="v1-section-head"><h2>Pruefung</h2></header>
            <div className="v1-list">
              {problems.map((problem) => (
                <div key={problem} className="v1-list-row warning"><span>{problem}</span></div>
              ))}
              {!plan.fits && plan.sites.length > 1 && (
                <div className="v1-list-row critical"><span>Sites passen nicht ins Zelt — nur {Math.round(Math.min(plan.aisleCm, plan.rowGapCm))} cm Luft.</span></div>
              )}
              {tankThin && <div className="v1-list-row warning"><span>Tankanteil {plan.tankSharePct} % — unter 25 % puffert das System pH und EC kaum.</span></div>}
              {problems.length === 0 && plan.fits && !tankThin && (
                <div className="v1-list-row"><span>Alles plausibel. Systemvolumen {plan.totalLiters} L — Addback rechnet damit.</span></div>
              )}
            </div>
            <div className="v1-section-body v1-form-actions">
              <V1Button variant="primary" disabled={saving || problems.length > 0} onClick={() => void save()}>
                {saving ? 'Speichert…' : 'Speichern'}
              </V1Button>
              <V1Button variant="ghost" onClick={() => navigate('/hydro')}>Abbrechen</V1Button>
            </div>
          </section>
        </aside>
      </div>

      {loading && <div className="v1-empty"><strong>Lade Zelte…</strong></div>}
    </main>
  )
}
