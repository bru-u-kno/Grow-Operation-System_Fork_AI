import { useState } from 'react'
import type { InternodeSpacing, PhenoPlantDto } from '../../types/pheno'
import { V1Field, V1Button, V1Switch } from '../../components/v1'
import { TRAINING_METHODS, sheetFrom, type SheetDraft } from './pheno-sheet-model'

const SPACING: Array<{ value: InternodeSpacing; label: string }> = [
  { value: 'Unknown', label: '—' },
  { value: 'Tight', label: 'eng' },
  { value: 'Medium', label: 'mittel' },
  { value: 'Wide', label: 'weit' },
]

function num(value: string): number | null {
  const parsed = Number.parseFloat(value.replace(',', '.'))
  return Number.isFinite(parsed) ? parsed : null
}

/** A 1–10 rating as a slider, because typing numbers for twelve traits is a chore. */
function Rating({ label, hint, value, max = 10, onChange }: { label: string; hint?: string; value: number | null; max?: number; onChange: (value: number | null) => void }) {
  return (
    <V1Field label={`${label}${value != null ? ` · ${value}` : ''}`} hint={hint}>
      <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
        <input
          type="range"
          min={1}
          max={max}
          step={1}
          value={value ?? 1}
          onChange={(event) => onChange(Number(event.target.value))}
          style={{ flex: 1, opacity: value == null ? 0.45 : 1 }}
          aria-label={label}
        />
        <V1Button variant="ghost" onClick={() => onChange(null)}>{value == null ? '—' : '×'}</V1Button>
      </div>
    </V1Field>
  )
}

function Group({ title, when, children }: { title: string; when: string; children: React.ReactNode }) {
  return (
    <div style={{ marginTop: 14 }}>
      <div style={{ display: 'flex', alignItems: 'baseline', gap: 10, marginBottom: 8 }}>
        <span className="v1-card-kicker">{title}</span>
        <span className="rc2-measurement-note">{when}</span>
      </div>
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(min(100%, 210px), 1fr))', gap: 12 }}>{children}</div>
    </div>
  )
}

/**
 * The full pheno-hunt score sheet for one plant, grouped by when you actually fill it in.
 * Everything is optional — an unrated trait simply doesn't count toward the score.
 */
export function PhenoSheetEditor({
  plant,
  onSave,
  onCancel,
}: {
  plant: PhenoPlantDto
  onSave: (draft: SheetDraft) => Promise<void>
  onCancel: () => void
}) {
  const [draft, setDraft] = useState<SheetDraft>(() => sheetFrom(plant))
  const [saving, setSaving] = useState(false)
  const set = (patch: Partial<SheetDraft>) => setDraft((current) => ({ ...current, ...patch }))

  const toggleMethod = (method: string) => set({
    trainingMethods: draft.trainingMethods.includes(method)
      ? draft.trainingMethods.filter((item) => item !== method)
      : [...draft.trainingMethods, method],
  })

  const stretch = draft.heightAtFlipCm && draft.heightAtHarvestCm && draft.heightAtFlipCm > 0
    ? (draft.heightAtHarvestCm / draft.heightAtFlipCm).toFixed(2).replace('.', ',')
    : null

  return (
    <div>
      <Group title="Wuchs & Struktur" when="während des Wachstums">
        <Rating label="Wüchsigkeit" hint="Wie kräftig sie früh loslegt." value={draft.vigorScore} onChange={(v) => set({ vigorScore: v })} />
        <Rating label="Verzweigung" hint="Seitentriebe — viele Triebe = oft mehr Ertrag." value={draft.branchingScore} onChange={(v) => set({ branchingScore: v })} />
        <Rating label="Blatt-zu-Blüte" hint="Hoch = wenig Blattwerk, leichter zu schneiden." value={draft.leafToBudScore} onChange={(v) => set({ leafToBudScore: v })} />
        <V1Field label="Internodienabstand">
          <select value={draft.internodeSpacing ?? 'Unknown'} onChange={(event) => set({ internodeSpacing: event.target.value as InternodeSpacing })}>
            {SPACING.map((item) => <option key={item.value} value={item.value}>{item.label}</option>)}
          </select>
        </V1Field>
        <V1Field label="Höhe beim Flip (cm)">
          <input inputMode="decimal" value={draft.heightAtFlipCm ?? ''} onChange={(event) => set({ heightAtFlipCm: num(event.target.value) })} />
        </V1Field>
      </Group>

      <Group title="Stress & Training" when="während des Wachstums">
        <V1Field label="Angewandte Techniken" wide>
          <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
            {TRAINING_METHODS.map((method) => (
              <V1Button key={method} variant={draft.trainingMethods.includes(method) ? 'primary' : 'secondary'} onClick={() => toggleMethod(method)}>
                {method}
              </V1Button>
            ))}
          </div>
        </V1Field>
        <Rating label="Reaktion aufs Training" hint="Wie gut hat sie es weggesteckt?" value={draft.trainingResponseScore} onChange={(v) => set({ trainingResponseScore: v })} />
        <Rating label="Stressverträglichkeit" hint="Hitze, Trockenheit, Nährstoff-Schwankungen." value={draft.stressToleranceScore} onChange={(v) => set({ stressToleranceScore: v })} />
        <Rating label="Schädlings-Widerstand" hint="1 = stark befallen, 5 = blieb unbehelligt." max={5} value={draft.pestResistanceScore} onChange={(v) => set({ pestResistanceScore: v })} />
      </Group>

      <Group title="Blüte & Ernte" when="bei der Ernte">
        <V1Field label="Blütetage"><input inputMode="numeric" value={draft.floweringDays ?? ''} onChange={(event) => set({ floweringDays: num(event.target.value) })} /></V1Field>
        <V1Field label="Höhe bei Ernte (cm)" hint={stretch ? `Streckung ×${stretch}` : 'Zusammen mit der Flip-Höhe ergibt das die Streckung.'}>
          <input inputMode="decimal" value={draft.heightAtHarvestCm ?? ''} onChange={(event) => set({ heightAtHarvestCm: num(event.target.value) })} />
        </V1Field>
        <V1Field label="Ertrag frisch (g)"><input inputMode="decimal" value={draft.wetYieldG ?? ''} onChange={(event) => set({ wetYieldG: num(event.target.value) })} /></V1Field>
        <V1Field label="Ertrag trocken (g)" hint="Zählt für die Note — verglichen mit den Geschwistern."><input inputMode="decimal" value={draft.dryYieldG ?? ''} onChange={(event) => set({ dryYieldG: num(event.target.value) })} /></V1Field>
        <Rating label="Blütendichte" value={draft.budDensityScore} onChange={(v) => set({ budDensityScore: v })} />
        <Rating label="Harz / Trichome" value={draft.resinScore} onChange={(v) => set({ resinScore: v })} />
        <Rating label="Schnitt-Aufwand" hint="Hoch = angenehm zu schneiden." value={draft.trimEaseScore} onChange={(v) => set({ trimEaseScore: v })} />
      </Group>

      <Group title="Qualität" when="nach Trocknen & Curing">
        <Rating label="Geruch" value={draft.aromaScore} onChange={(v) => set({ aromaScore: v })} />
        <Rating label="Geschmack" value={draft.flavorScore} onChange={(v) => set({ flavorScore: v })} />
        <Rating label="Wirkung" value={draft.effectScore} onChange={(v) => set({ effectScore: v })} />
        <V1Field label="THC (%)"><input inputMode="decimal" value={draft.thcPercent ?? ''} onChange={(event) => set({ thcPercent: num(event.target.value) })} /></V1Field>
        <V1Field label="CBD (%)"><input inputMode="decimal" value={draft.cbdPercent ?? ''} onChange={(event) => set({ cbdPercent: num(event.target.value) })} /></V1Field>
        <V1Field label="Geruchs-Notiz" wide><input value={draft.aromaNotes ?? ''} onChange={(event) => set({ aromaNotes: event.target.value || null })} placeholder="z. B. Zitrone, Diesel, erdig" /></V1Field>
        <V1Field label="Wirkungs-Notiz" wide><input value={draft.effectNotes ?? ''} onChange={(event) => set({ effectNotes: event.target.value || null })} placeholder="z. B. klar, körperlich, abends" /></V1Field>
        <V1Field label="Terpene" wide><input value={draft.terpeneNotes ?? ''} onChange={(event) => set({ terpeneNotes: event.target.value || null })} placeholder="z. B. Limonen, Myrcen" /></V1Field>
      </Group>

      <Group title="Entscheidung" when="am Ende">
        <V1Field label="Gesamtnote von Hand" hint="Leer lassen = Grow OS rechnet sie aus deinen Bewertungen.">
          <input inputMode="decimal" value={draft.manualOverallScore ?? ''} onChange={(event) => set({ manualOverallScore: num(event.target.value) })} placeholder="—" />
        </V1Field>
        <V1Field label="Notizen" wide>
          <textarea rows={3} value={draft.notes ?? ''} onChange={(event) => set({ notes: event.target.value || null })} />
        </V1Field>
      </Group>

      <div style={{ display: 'grid', gap: 8, marginTop: 12 }}>
        <V1Switch label="Keeper" hint="Diese behältst du — Grundlage für die Mutterpflanze." checked={draft.isKeeper} onChange={(checked) => set({ isKeeper: checked })} />
        <V1Switch label="Im zweiten Lauf bestätigt" hint="Ein Phäno ist erst nach einer Wiederholung wirklich beurteilt." checked={draft.confirmedInSecondRun} onChange={(checked) => set({ confirmedInSecondRun: checked })} />
      </div>

      <div className="v1-action-row" style={{ marginTop: 14 }}>
        <V1Button variant="primary" disabled={saving} onClick={() => { setSaving(true); void onSave(draft).finally(() => setSaving(false)) }}>
          {saving ? 'Speichert…' : 'Bogen speichern'}
        </V1Button>
        <V1Button variant="ghost" onClick={onCancel}>Schließen</V1Button>
      </div>
    </div>
  )
}
