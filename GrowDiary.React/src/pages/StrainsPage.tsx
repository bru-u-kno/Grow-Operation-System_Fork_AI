import { useEffect, useMemo, useRef, useState } from 'react'
import { apiFetch, ApiRequestError } from '../api'
import type { CreateStrainRequest, GrowSummary, HarvestDto, PlantInstanceDto, SeedKind, StrainDominance, StrainDto } from '../types'
import { samenartName } from '../deutsche-woerter'
import type { PhenoHuntDto, PhenoPlantDto, PhenoWeightsDto } from '../types/pheno'
import { PhenoSheetEditor } from '../features/pheno/PhenoSheetEditor'
import type { SheetDraft } from '../features/pheno/pheno-sheet-model'
import { formatNumber } from '../utils'
import { feldText, unlesbarMeldung, unlesbareFelder, zahlOderNull } from '../zahlenfeld'
import { V1Page, V1Card, V1Field, V1Button, V1Alert, V1Empty, V1Skeleton } from '../components/v1'

type StrainDraft = {
  name: string
  breeder: string
  dominance: StrainDominance
  flowerWeeksMin: string
  flowerWeeksMax: string
  nutrientDemandFactor: string
  stretchFactor: string
  vpdPreferenceShift: string
  notes: string
  seedKind: '' | 'Feminized' | 'Automatic' | 'Regular'
  thcPercent: string
  cbdPercent: string
  sativaPercent: string
  taste: string
  effect: string
  aroma: string
  yieldIndoorGm2: string
  heightIndoorCm: string
}

// Die Beschriftungen kommen aus deutsche-woerter.ts. Hier stand die VIERTE
// eigene Uebersetzungstabelle der App, und sie liess „Automatic" englisch
// zwischen „Feminisiert" und „Regulär" stehen — im selben Auswahlfeld.
const SEED_KINDS: Array<{ value: '' | SeedKind; label: string }> = [
  { value: '', label: '—' },
  ...(['Feminized', 'Automatic', 'Regular'] as const)
    .map((value) => ({ value, label: samenartName(value) })),
]

const DOMINANCE: Array<{ value: StrainDominance; label: string }> = [
  { value: 'Unknown', label: 'Unbekannt' },
  { value: 'Indica', label: 'Indica' },
  { value: 'Sativa', label: 'Sativa' },
  { value: 'Hybrid', label: 'Hybrid' },
]

function emptyDraft(): StrainDraft {
  return { name: '', breeder: '', dominance: 'Unknown', flowerWeeksMin: '', flowerWeeksMax: '', nutrientDemandFactor: '', stretchFactor: '', vpdPreferenceShift: '', notes: '', seedKind: '', thcPercent: '', cbdPercent: '', sativaPercent: '', taste: '', effect: '', aroma: '', yieldIndoorGm2: '', heightIndoorCm: '' }
}

function draftFrom(strain: StrainDto): StrainDraft {
  return {
    name: strain.name,
    breeder: strain.breeder ?? '',
    dominance: strain.dominance,
    flowerWeeksMin: feldText(strain.flowerWeeksMin),
    flowerWeeksMax: feldText(strain.flowerWeeksMax),
    nutrientDemandFactor: feldText(strain.nutrientDemandFactor),
    stretchFactor: feldText(strain.stretchFactor),
    vpdPreferenceShift: feldText(strain.vpdPreferenceShift),
    notes: strain.notes ?? '',
    seedKind: strain.seedKind ?? '',
    thcPercent: feldText(strain.thcPercent),
    cbdPercent: feldText(strain.cbdPercent),
    sativaPercent: feldText(strain.sativaPercent),
    taste: strain.taste ?? '',
    effect: strain.effect ?? '',
    aroma: strain.aroma ?? '',
    yieldIndoorGm2: feldText(strain.yieldIndoorGm2),
    heightIndoorCm: feldText(strain.heightIndoorCm),
  }
}

/**
 * Eine ganze Zahl — nach derselben Leseregel wie alle anderen Felder.
 *
 * Hier stand `parseInt`: „1.200" g/m² wurde zu 1, weil `parseInt` am Punkt
 * aufhört. Nachkommastellen werden wie bisher abgeschnitten („12,5" → 12).
 */
function int(value: string): number | null {
  const zahl = zahlOderNull(value)
  return zahl == null ? null : Math.trunc(zahl)
}

function draftToRequest(draft: StrainDraft): CreateStrainRequest {
  return {
    name: draft.name.trim(),
    breeder: draft.breeder.trim() || null,
    dominance: draft.dominance,
    flowerWeeksMin: int(draft.flowerWeeksMin),
    flowerWeeksMax: int(draft.flowerWeeksMax),
    nutrientDemandFactor: zahlOderNull(draft.nutrientDemandFactor),
    stretchFactor: zahlOderNull(draft.stretchFactor),
    vpdPreferenceShift: zahlOderNull(draft.vpdPreferenceShift),
    notes: draft.notes.trim() || null,
    seedKind: draft.seedKind || null,
    thcPercent: zahlOderNull(draft.thcPercent),
    cbdPercent: zahlOderNull(draft.cbdPercent),
    sativaPercent: int(draft.sativaPercent),
    taste: draft.taste.trim() || null,
    effect: draft.effect.trim() || null,
    aroma: draft.aroma.trim() || null,
    yieldIndoorGm2: int(draft.yieldIndoorGm2),
    heightIndoorCm: int(draft.heightIndoorCm),
  }
}

/**
 * „2× Mimosa EVO · 2× RS11 · 2× Pineapple Express" — was wirklich im Zelt steht.
 *
 * Bei einer einzigen Sorte bleibt es bei der schlichten Anzahl: „6 Kandidaten"
 * liest sich besser als „6× Pineapple Express", wenn es nichts zu unterscheiden
 * gibt.
 */
function sortenVerteilung(plants: PhenoPlantDto[]): string {
  const zaehler = new Map<string, number>()
  for (const plant of plants) {
    const name = plant.strainName ?? 'ohne Sorte'
    zaehler.set(name, (zaehler.get(name) ?? 0) + 1)
  }
  if (zaehler.size <= 1) {
    return `${plants.length} ${plants.length === 1 ? 'Kandidat' : 'Kandidaten'}`
  }
  return [...zaehler.entries()]
    .sort((a, b) => b[1] - a[1])
    .map(([name, anzahl]) => `${anzahl}× ${name}`)
    .join(' · ')
}

function dominanceLabel(value: StrainDominance): string {
  return DOMINANCE.find((item) => item.value === value)?.label ?? value
}

/**
 * Läufe und Ø-Ertrag je Sorte.
 *
 * `mixedOnly` heißt: diese Sorte lief nur in Zelten mit mehreren Sorten. Dann
 * gibt es keinen ehrlichen Ertragswert — die Ernte wird als Gesamtgewicht
 * erfasst, ohne Zuordnung zur einzelnen Pflanze, und die ganze Ernte der
 * Hauptsorte gutzuschreiben wäre schlicht falsch.
 */
type StrainStats = { runs: number; avgPerPlant: number | null; mixedOnly: boolean }

/** Pheno-Hunt-Zeile: entweder ein Keeper oder der laufende Hunt. */
type HuntState = { grow: GrowSummary; hunt: PhenoHuntDto }

/**
 * Sorten & Pheno-Hunt auf einer Seite, wie im Entwurf: oben die Bibliothek als
 * Tabelle mit Läufen, Ø-Ertrag und Pheno-Keeper, darunter je aktivem Grow der
 * Kandidaten-Streifen. Bewerten öffnet den Bogen direkt unter dem Streifen.
 */
function StrainsPage() {
  const [strains, setStrains] = useState<StrainDto[]>([])
  const [grows, setGrows] = useState<GrowSummary[]>([])
  const [harvestByGrow, setHarvestByGrow] = useState<Map<number, HarvestDto>>(new Map())
  const [hunts, setHunts] = useState<HuntState[]>([])
  // Alle Pflanzen aller Grows — traegt je Zeile growId UND strainId.
  const [plants, setPlants] = useState<PlantInstanceDto[]>([])
  const [draft, setDraft] = useState<StrainDraft>(emptyDraft)
  const [editingId, setEditingId] = useState<number | null>(null)
  const [formOpen, setFormOpen] = useState(false)

  // Das Formular steht an anderer Stelle als der Knopf, der es oeffnet. Bei
  // wenigen Zeilen faellt das nicht auf, bei vielen schon: dann geht es
  // ausserhalb des Sichtbaren auf, nichts scrollt hin, und fuer den Nutzer
  // „reagiert der Knopf nicht". Genau so kam die Meldung zu /sensoren.
  //
  // `block: start`, nicht `center`: die Ueberschrift ist die Rueckmeldung,
  // dass der Klick angekommen ist.
  // Ein ZAEHLER, nicht `formOpen`.
  //
  // Die erste Fassung hing an `formOpen` — und beim zweiten Klick ist das schon
  // `true`, der Effekt lief also nicht mehr. Wer eine Zeile bearbeitet, hoch
  // scrollt und die naechste anklickt, bekam den Inhalt gewechselt und sonst
  // nichts: das Formular blieb unten ausserhalb des Bildes stehen.
  //
  // Der Tester hat genau das als Video geschickt, NACHDEM der erste Klick
  // repariert war. Mein Kommentar an dieser Stelle behauptete sogar, ein
  // Wechsel scrolle „ohnehin nicht weg" — geprueft hatte ich nur den ersten
  // Klick.
  //
  // Ein Zaehler feuert bei jedem Aufruf, unabhaengig davon, ob das Formular
  // schon offen war.
  const formularRef = useRef<HTMLDivElement | null>(null)
  const [hinScrollen, setHinScrollen] = useState(0)

  useEffect(() => {
    if (hinScrollen === 0) return
    formularRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' })
  }, [hinScrollen])
  const [openPlantId, setOpenPlantId] = useState<number | null>(null)
  // Die Gewichtung gilt app-weit, aber der Editor gehoert in das Panel, dessen
  // Knopf gedrueckt wurde — vorher erschien er stur im ersten Hunt, und in
  // jedem weiteren wirkte der Knopf schlicht tot.
  const [weightsOpen, setWeightsOpen] = useState<number | null>(null)
  const [weightDraft, setWeightDraft] = useState<PhenoWeightsDto | null>(null)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [reloadKey, setReloadKey] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    async function load() {
      try {
        const [strainList, active, archived, plantList] = await Promise.all([
          apiFetch<StrainDto[]>('/api/strains', { signal: controller.signal }),
          apiFetch<GrowSummary[]>('/api/grows?archived=false', { signal: controller.signal }).catch(() => []),
          apiFetch<GrowSummary[]>('/api/grows?archived=true', { signal: controller.signal }).catch(() => []),
          // Ohne die Pflanzen zaehlt die Seite nur die Hauptsorte des Grows —
          // ein Zelt mit drei Sorten meldete fuer zwei davon „0 Runs".
          apiFetch<PlantInstanceDto[]>('/api/plants', { signal: controller.signal }).catch(() => [] as PlantInstanceDto[]),
        ])
        if (controller.signal.aborted) return
        setStrains(strainList)
        setPlants(plantList)
        const allGrows = [...active, ...archived]
        setGrows(allGrows)

        // Ernten der archivierten Grows für den Ø-Ertrag je Sorte.
        const harvests = await Promise.all(archived.map((grow) =>
          apiFetch<HarvestDto>(`/api/grows/${grow.id}/harvest`, { signal: controller.signal }).catch(() => null),
        ))
        if (controller.signal.aborted) return
        const harvestMap = new Map<number, HarvestDto>()
        archived.forEach((grow, index) => { const harvest = harvests[index]; if (harvest) harvestMap.set(grow.id, harvest) })
        setHarvestByGrow(harvestMap)

        // Pheno-Hunts der aktiven Grows — nur die mit Pflanzen erscheinen.
        const running = active.filter((grow) => grow.status === 'Running' || grow.status === 'Planning')
        const huntResults = await Promise.all(running.map((grow) =>
          apiFetch<PhenoHuntDto>(`/api/pheno/grows/${grow.id}`, { signal: controller.signal }).catch(() => null),
        ))
        if (controller.signal.aborted) return
        const aktiveHunts = running
          .map((grow, index) => ({ grow, hunt: huntResults[index] }))
          .filter((item): item is HuntState => item.hunt != null && item.hunt.plants.length > 0)
        setHunts(aktiveHunts)
        // Die Gewichtung gilt global, nicht je Grow — der erste Hunt liefert
        // den aktuellen Stand.
        if (aktiveHunts.length > 0) setWeightDraft(aktiveHunts[0].hunt.weights)
      } catch (caught) {
        if (!controller.signal.aborted) setError(caught instanceof ApiRequestError ? caught.message : 'Sorten konnten nicht geladen werden.')
      } finally {
        if (!controller.signal.aborted) setLoading(false)
      }
    }
    void load()
    return () => controller.abort()
  }, [reloadKey])

  // Der Filter ersetzt die Excel des Testers: Typ als harte Auswahl, Text
  // ueber Geschmack/Effekt/Aroma/Name, Sortierung waehlbar.
  const [filterKind, setFilterKind] = useState<'' | 'Feminized' | 'Automatic' | 'Regular'>('')
  const [filterText, setFilterText] = useState('')
  const [sortBy, setSortBy] = useState<'name' | 'thc' | 'flower' | 'yield'>('name')

  const sorted = useMemo(() => {
    const suchtext = filterText.trim().toLowerCase()
    const gefiltert = strains
      .filter((strain) => filterKind === '' || strain.seedKind === filterKind)
      .filter((strain) => {
        if (suchtext === '') return true
        return [strain.name, strain.breeder, strain.taste, strain.effect, strain.aroma, strain.notes]
          .some((feld) => feld?.toLowerCase().includes(suchtext))
      })
    // Beim Sortieren nach einer Zahl gehoert „keine Angabe" ans Ende, nicht
    // zwischen die Werte — sonst wirkt eine ungepflegte Sorte wie die beste.
    // Absteigend heisst das -Unendlich, aufsteigend +Unendlich; die alte
    // Negations-Konstruktion drehte die Bluetezeit komplett um.
    const zahl = (wert: number | null) => wert ?? Number.NEGATIVE_INFINITY
    const zahlAufsteigend = (wert: number | null) => wert ?? Number.POSITIVE_INFINITY
    return gefiltert.sort((a, b) => {
      switch (sortBy) {
        case 'thc': return zahl(b.thcPercent) - zahl(a.thcPercent) || a.name.localeCompare(b.name, 'de')
        case 'flower': return zahlAufsteigend(a.flowerWeeksMin) - zahlAufsteigend(b.flowerWeeksMin) || a.name.localeCompare(b.name, 'de')
        case 'yield': return zahl(b.yieldIndoorGm2) - zahl(a.yieldIndoorGm2) || a.name.localeCompare(b.name, 'de')
        default: return a.name.localeCompare(b.name, 'de')
      }
    })
  }, [strains, filterKind, filterText, sortBy])

  /** Wie viele verschiedene Sorten in einem Grow stehen — 0 = keine Pflanzen erfasst. */
  const sortenJeGrow = useMemo(() => {
    const map = new Map<number, Set<number>>()
    for (const plant of plants) {
      if (plant.growId == null || plant.strainId == null) continue
      const vorhanden = map.get(plant.growId) ?? new Set<number>()
      vorhanden.add(plant.strainId)
      map.set(plant.growId, vorhanden)
    }
    return map
  }, [plants])

  const statsByStrain = useMemo(() => {
    const map = new Map<number, StrainStats>()
    for (const strain of strains) {
      // Ein Lauf zaehlt fuer eine Sorte auf ZWEI Wegen: der Grow traegt sie als
      // Hauptsorte, ODER eine seiner Pflanzen tut es. Der Namensvergleich
      // bleibt als Rueckfall fuer Laeufe von vor der Verknuepfung — sonst
      // faenge die Statistik jedes Bestandsgrows bei null an.
      const ueberGrow = grows.filter((grow) => grow.strainId != null
        ? grow.strainId === strain.id
        : grow.strain != null && grow.strain.toLowerCase() === strain.name.toLowerCase())
      const ueberPflanzen = new Set(plants
        .filter((plant) => plant.strainId === strain.id && plant.growId != null)
        .map((plant) => plant.growId as number))
      // Vereinigung, damit ein Grow mit zwei Mimosa-Pflanzen EINEN Lauf zaehlt.
      const growIds = new Set<number>([...ueberGrow.map((grow) => grow.id), ...ueberPflanzen])
      const matched = grows.filter((grow) => growIds.has(grow.id))

      // Der Ertrag ist nur dort belegbar, wo eine Sorte allein im Zelt stand:
      // die Ernte wird als Gesamtgewicht erfasst, ohne Bezug zur einzelnen
      // Pflanze. Bei gemischten Zelten waere jede Zahl geraten.
      const reinsortig = matched.filter((grow) => (sortenJeGrow.get(grow.id)?.size ?? 1) <= 1)
      const yields = reinsortig
        .map((grow) => {
          const harvest = harvestByGrow.get(grow.id)
          return harvest?.dryWeightG != null && grow.plantCount ? harvest.dryWeightG / grow.plantCount : null
        })
        .filter((value): value is number => value != null)

      map.set(strain.id, {
        runs: matched.length,
        avgPerPlant: yields.length > 0 ? yields.reduce((sum, value) => sum + value, 0) / yields.length : null,
        mixedOnly: matched.length > 0 && reinsortig.length === 0,
      })
    }
    return map
  }, [strains, grows, plants, harvestByGrow, sortenJeGrow])

  const keeperByStrain = useMemo(() => {
    // Zugeordnet wird ueber die Sorte der PFLANZE, nicht ueber die Hauptsorte
    // des Grows. Vorher schrieb ein Zelt mit sechs Pflanzen aus drei Sorten
    // alle sechs Kandidaten der Hauptsorte gut — die beiden anderen Sorten
    // sahen aus, als liefe fuer sie nichts.
    const map = new Map<number, string>()
    const kandidatenJeSorte = new Map<number, PhenoPlantDto[]>()
    for (const { hunt } of hunts) {
      for (const plant of hunt.plants) {
        if (plant.strainId == null) continue
        const liste = kandidatenJeSorte.get(plant.strainId) ?? []
        liste.push(plant)
        kandidatenJeSorte.set(plant.strainId, liste)
      }
    }
    for (const [strainId, kandidaten] of kandidatenJeSorte) {
      const keeper = kandidaten.find((plant) => plant.evaluation?.isKeeper)
      map.set(strainId, keeper
        ? `${keeper.phenoLabel ?? keeper.label} · Keeper`
        : `Hunt läuft · ${kandidaten.length} ${kandidaten.length === 1 ? 'Kandidat' : 'Kandidaten'}`)
    }
    return map
  }, [hunts])

  function startNew() {
    setDraft(emptyDraft())
    setEditingId(null)
    setFormOpen(true)
    setHinScrollen((n) => n + 1)
  }

  function startEdit(strain: StrainDto) {
    setDraft(draftFrom(strain))
    setEditingId(strain.id)
    setFormOpen(true)
    setHinScrollen((n) => n + 1)
  }

  async function save() {
    if (draft.name.trim() === '') {
      setError('Bitte einen Sortennamen eingeben.')
      return
    }
    // `parseFloat` las „22,5 %" bisher als 22,5 und „6,2x" als 6,2 — der Rest
    // verschwand ohne Meldung.
    const unlesbar = unlesbarMeldung(unlesbareFelder([
      [draft.flowerWeeksMin, 'Blüte von (Wochen)'], [draft.flowerWeeksMax, 'Blüte bis (Wochen)'],
      [draft.thcPercent, 'THC (%)'], [draft.cbdPercent, 'CBD (%)'], [draft.sativaPercent, 'Sativa-Anteil (%)'],
      [draft.yieldIndoorGm2, 'Ertrag innen (g/m²)'], [draft.heightIndoorCm, 'Höhe innen (cm)'],
      [draft.nutrientDemandFactor, 'Nährstoffbedarf'], [draft.stretchFactor, 'Streckung'], [draft.vpdPreferenceShift, 'VPD-Vorliebe (kPa)'],
    ]))
    if (unlesbar) {
      setError(unlesbar)
      return
    }
    setSaving(true)
    setError(null)
    setNotice(null)
    try {
      const body = JSON.stringify(draftToRequest(draft))
      if (editingId == null) await apiFetch('/api/strains', { method: 'POST', body })
      else await apiFetch(`/api/strains/${editingId}`, { method: 'PUT', body })
      setNotice(editingId == null ? `„${draft.name.trim()}" angelegt.` : `„${draft.name.trim()}" gespeichert.`)
      setFormOpen(false)
      setEditingId(null)
      setReloadKey((key) => key + 1)
    } catch (caught) {
      setError(caught instanceof ApiRequestError ? caught.message : 'Sorte konnte nicht gespeichert werden.')
    } finally {
      setSaving(false)
    }
  }

  /**
   * Eine Sorte entfernen.
   *
   * Bis zum 25.08.2026 ging das nirgends — wer sich vertippte, behielt den
   * Eintrag fuer immer in jeder Auswahlliste. Der Server lehnt ab, solange
   * Pflanzen oder Aushaerte-Glaeser die Sorte fuehren, und sagt auch warum.
   */
  async function entfernen() {
    if (editingId == null) return
    const name = draft.name.trim()
    if (!window.confirm(`„${name}" wirklich entfernen?`)) return

    setError(null)
    setNotice(null)
    try {
      await apiFetch(`/api/strains/${editingId}`, { method: 'DELETE' })
      setNotice(`„${name}" entfernt.`)
      setFormOpen(false)
      setEditingId(null)
      setReloadKey((key) => key + 1)
    } catch (caught) {
      setError(caught instanceof ApiRequestError ? caught.message : 'Sorte konnte nicht entfernt werden.')
    }
  }

  async function saveWeights() {
    if (!weightDraft) return
    setError(null)
    setNotice(null)
    try {
      await apiFetch('/api/pheno/weights', { method: 'PUT', body: JSON.stringify(weightDraft) })
      setNotice('Gewichtung gespeichert — die Noten sind neu berechnet.')
      setWeightsOpen(null)
      setReloadKey((key) => key + 1)
    } catch (caught) {
      setError(caught instanceof ApiRequestError ? caught.message : 'Gewichtung konnte nicht gespeichert werden.')
    }
  }

  async function saveSheet(plant: PhenoPlantDto, sheetDraft: SheetDraft) {
    setError(null)
    setNotice(null)
    try {
      await apiFetch(`/api/pheno/plants/${plant.plantInstanceId}`, {
        method: 'PUT',
        body: JSON.stringify({ ...sheetDraft, plantInstanceId: plant.plantInstanceId }),
      })
      setNotice(`Bogen für „${plant.label}" gespeichert.`)
      setOpenPlantId(null)
      setReloadKey((key) => key + 1)
    } catch (caught) {
      setError(caught instanceof ApiRequestError ? caught.message : 'Bogen konnte nicht gespeichert werden.')
    }
  }

  return (
    <V1Page
      eyebrow="Pflanzen / Sorten"
      title="Sorten & Pheno-Hunt"
      action={<button type="button" className="ls-btn is-primary" onClick={startNew}>+ Sorte</button>}
    >
      {error && <V1Alert message={error} tone="critical" />}
      {notice && <V1Alert message={notice} tone="ok" />}

      {formOpen && (
      <div ref={formularRef} className="scroll-ziel" data-audit="sorten-formular">
        <V1Card className="st-form">
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(min(100%, 190px), 1fr))', gap: 12 }}>
            <V1Field label="Name"><input value={draft.name} onChange={(event) => setDraft((d) => ({ ...d, name: event.target.value }))} placeholder="z. B. Purple Lemonade" /></V1Field>
            <V1Field label="Züchter"><input value={draft.breeder} onChange={(event) => setDraft((d) => ({ ...d, breeder: event.target.value }))} placeholder="z. B. FastBuds" /></V1Field>
            <V1Field label="Dominanz">
              <select value={draft.dominance} onChange={(event) => setDraft((d) => ({ ...d, dominance: event.target.value as StrainDominance }))}>
                {DOMINANCE.map((item) => <option key={item.value} value={item.value}>{item.label}</option>)}
              </select>
            </V1Field>
            <V1Field label="Blüte von (Wochen)"><input inputMode="numeric" value={draft.flowerWeeksMin} onChange={(event) => setDraft((d) => ({ ...d, flowerWeeksMin: event.target.value }))} placeholder="8" /></V1Field>
            <V1Field label="Blüte bis (Wochen)"><input inputMode="numeric" value={draft.flowerWeeksMax} onChange={(event) => setDraft((d) => ({ ...d, flowerWeeksMax: event.target.value }))} placeholder="10" /></V1Field>
          </div>

          {/* Die Zuechter-Angaben von der Samenpackung — Feedback des Testers,
              der sie bisher in einer eigenen Excel pflegte. Alles optional, und
              alles ausdruecklich „laut Zuechter": es sind Werbeangaben. */}
          <p className="gc-facts" style={{ margin: '14px 0 8px' }}>
            Angaben laut Züchter — von der Samenpackung oder der Shopseite. Danach lässt sich die Bibliothek filtern.
          </p>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(min(100%, 190px), 1fr))', gap: 12 }}>
            <V1Field label="Samen-Typ">
              <select value={draft.seedKind} onChange={(event) => setDraft((d) => ({ ...d, seedKind: event.target.value as StrainDraft['seedKind'] }))}>
                {SEED_KINDS.map((item) => <option key={item.value} value={item.value}>{item.label}</option>)}
              </select>
            </V1Field>
            <V1Field label="THC (%)"><input inputMode="decimal" value={draft.thcPercent} onChange={(event) => setDraft((d) => ({ ...d, thcPercent: event.target.value }))} placeholder="32" /></V1Field>
            <V1Field label="CBD (%)"><input inputMode="decimal" value={draft.cbdPercent} onChange={(event) => setDraft((d) => ({ ...d, cbdPercent: event.target.value }))} placeholder="0,8" /></V1Field>
            <V1Field label="Sativa-Anteil (%)" hint="Der Rest ist Indica."><input inputMode="numeric" value={draft.sativaPercent} onChange={(event) => setDraft((d) => ({ ...d, sativaPercent: event.target.value }))} placeholder="30" /></V1Field>
            <V1Field label="Ertrag innen (g/m²)"><input inputMode="numeric" value={draft.yieldIndoorGm2} onChange={(event) => setDraft((d) => ({ ...d, yieldIndoorGm2: event.target.value }))} placeholder="600" /></V1Field>
            <V1Field label="Höhe innen (cm)"><input inputMode="numeric" value={draft.heightIndoorCm} onChange={(event) => setDraft((d) => ({ ...d, heightIndoorCm: event.target.value }))} placeholder="150" /></V1Field>
            <V1Field label="Geschmack" wide><input value={draft.taste} onChange={(event) => setDraft((d) => ({ ...d, taste: event.target.value }))} placeholder="Grapefruit, Zitrus, Melone, Banane" /></V1Field>
            <V1Field label="Effekt" wide><input value={draft.effect} onChange={(event) => setDraft((d) => ({ ...d, effect: event.target.value }))} placeholder="Entspannt, Konzentriert, Beruhigend" /></V1Field>
            <V1Field label="Aroma" wide><input value={draft.aroma} onChange={(event) => setDraft((d) => ({ ...d, aroma: event.target.value }))} placeholder="Zitrone, Würzig, Kirsche" /></V1Field>
          </div>

          <p className="gc-facts" style={{ margin: '14px 0 8px' }}>
            Feinheiten — leer lassen, wenn du sie nicht kennst. Sie beschreiben, wie stark diese Sorte von einer Durchschnittspflanze abweicht.
          </p>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(min(100%, 190px), 1fr))', gap: 12 }}>
            <V1Field label="Nährstoffbedarf" hint="1,0 = normal. 1,2 = frisst 20 % mehr, 0,8 = empfindlich.">
              <input inputMode="decimal" value={draft.nutrientDemandFactor} onChange={(event) => setDraft((d) => ({ ...d, nutrientDemandFactor: event.target.value }))} placeholder="1,0" />
            </V1Field>
            <V1Field label="Streckung" hint="1,0 = normal. Über 1 streckt sie in der Blüte stärker.">
              <input inputMode="decimal" value={draft.stretchFactor} onChange={(event) => setDraft((d) => ({ ...d, stretchFactor: event.target.value }))} placeholder="1,0" />
            </V1Field>
            <V1Field label="VPD-Vorliebe (kPa)" hint="Verschiebung zum Standard. +0,1 = mag es etwas trockener.">
              <input inputMode="decimal" value={draft.vpdPreferenceShift} onChange={(event) => setDraft((d) => ({ ...d, vpdPreferenceShift: event.target.value }))} placeholder="0" />
            </V1Field>
          </div>

          <V1Field label="Notizen" wide>
            <textarea rows={3} value={draft.notes} onChange={(event) => setDraft((d) => ({ ...d, notes: event.target.value }))} placeholder="Geruch, Wuchsform, Erfahrungen …" />
          </V1Field>

          <div className="co-actions" style={{ marginTop: 12 }}>
            <V1Button variant="primary" onClick={() => void save()} disabled={saving}>{saving ? 'Speichert…' : 'Speichern'}</V1Button>
            <V1Button variant="ghost" onClick={() => { setFormOpen(false); setEditingId(null) }}>Abbrechen</V1Button>
            {/* Nur beim Bearbeiten: eine Sorte, die es noch nicht gibt,
                laesst sich nicht entfernen. */}
            {editingId != null && (
              <V1Button variant="ghost" onClick={() => void entfernen()}>Entfernen</V1Button>
            )}
          </div>
        </V1Card>
      </div>
      )}

      {loading ? (
        <V1Skeleton rows={4} label="Lade Sorten" />
      ) : sorted.length === 0 ? (
        <V1Empty title="Noch keine Sorte" text="Leg deine erste Sorte an — danach kannst du Grows und Pflanzen darauf verweisen." action={<V1Button variant="primary" onClick={startNew}>Sorte anlegen</V1Button>} />
      ) : (
        <section className="ls-panel" data-audit="strains-table">
          <div className="st-filter" data-audit="strains-filter">
            <input
              value={filterText}
              onChange={(event) => setFilterText(event.target.value)}
              placeholder="Suchen: Name, Geschmack, Effekt, Aroma …"
              aria-label="Sorten durchsuchen"
            />
            <select value={filterKind} onChange={(event) => setFilterKind(event.target.value as typeof filterKind)} aria-label="Nach Samen-Typ filtern">
              {SEED_KINDS.map((art) => (
                <option key={art.value} value={art.value}>
                  {art.value === '' ? 'Alle Typen' : art.label}
                </option>
              ))}
            </select>
            <select value={sortBy} onChange={(event) => setSortBy(event.target.value as typeof sortBy)} aria-label="Sortierung">
              <option value="name">Name A–Z</option>
              <option value="thc">THC absteigend</option>
              <option value="flower">Blütezeit kürzeste zuerst</option>
              <option value="yield">Ertrag absteigend</option>
            </select>
          </div>
          <div className="co-table-wrap">
          {/* Mindestens 680 px: bei den 560 px aus `collection.css` bekam die
              Pheno-Spalte 93 px, und „Hunt läuft · 3 Kandidaten" braucht 96 —
              der Text lief ohne jedes Anzeichen in die Nachbarspalte. Sichtbar
              wurde das erst, als der Testbestand Pflanzen bekam und der Hunt
              damit ueberhaupt Kandidaten zeigte. Dieselbe Klasse wie im Archiv. */}
          <div
            className="co-table"
            style={{ gridTemplateColumns: '1.6fr 1.1fr .5fr .5fr .8fr 1.1fr', minWidth: 680 }}
          >
            <div className="co-th">Sorte</div>
            <div className="co-th">Züchter</div>
            <div className="co-th">Typ</div>
            <div className="co-th">Runs</div>
            <div className="co-th">Ø Ertrag</div>
            <div className="co-th">Pheno-Keeper</div>
            {sorted.map((strain) => {
              // Schluessel ist die Id, nicht der Name: zwei Sorten gleichen
              // Namens von verschiedenen Zuechtern haetten sich sonst
              // gegenseitig ueberschrieben.
              const stats = statsByStrain.get(strain.id)
              const keeper = keeperByStrain.get(strain.id)
              return (
                <StrainRow key={strain.id}>
                  <div className="co-td is-name">
                    <button type="button" className="co-td-link" onClick={() => startEdit(strain)}>{strain.name}</button>
                  </div>
                  <div className="co-td is-muted">{strain.breeder ?? '—'}</div>
                  <div className="co-td is-muted">
                    {/* Ein gemeinsames span: als zwei Flex-Kinder durfte das
                        schmalere bis auf ein Wort je Zeile schrumpfen —
                        „Fe/mi/ni/si/er/t" ueber sechs Zeilen. */}
                    <span>
                      {strain.seedKind ? (SEED_KINDS.find((k) => k.value === strain.seedKind)?.label ?? strain.seedKind) : dominanceLabel(strain.dominance)}
                      {strain.thcPercent != null && <span className="st-thc"> · {strain.thcPercent.toLocaleString('de-DE')} % THC</span>}
                    </span>
                  </div>
                  <div className="co-td">{stats?.runs ?? 0}</div>
                  <div className={stats?.mixedOnly ? 'co-td is-muted' : 'co-td'}>
                    {stats?.avgPerPlant != null
                      ? `${formatNumber(stats.avgPerPlant, 0)} g/Pflanze`
                      : stats?.mixedOnly
                        ? <span title="Die Ernte wird als Gesamtgewicht erfasst — in einem Zelt mit mehreren Sorten lässt sie sich nicht ehrlich aufteilen.">— gemischt</span>
                        : '—'}
                  </div>
                  <div className={keeper?.includes('Keeper') ? 'co-td is-good' : 'co-td is-muted'}>{keeper ?? '—'}</div>
                </StrainRow>
              )
            })}
          </div>
          </div>
        </section>
      )}

      {hunts.map(({ grow, hunt }) => (
        <section key={grow.id} className="ls-panel" data-audit="pheno-hunt-panel">
          <div className="ls-panel-head">
            {/* Nicht mehr die Hauptsorte des Grows: bei mehreren Sorten im
                Zelt stand hier ein Name, unter dem auch fremde Pflanzen
                gelistet waren. Jetzt steht da, was wirklich drinsteht. */}
            <span className="ls-label">Pheno-Hunt · {grow.name}</span>
            <span className="ls-panel-meta">{sortenVerteilung(hunt.plants)} · {weightSummary(weightDraft ?? hunt.weights)}</span>
            <button type="button" className="ls-btn is-small" onClick={() => setWeightsOpen((open) => (open === grow.id ? null : grow.id))}>
              {weightsOpen === grow.id ? 'Schließen' : 'Gewichtung'}
            </button>
          </div>
          {weightsOpen === grow.id && weightDraft && (
            <div className="ph-weights" data-audit="pheno-weights">
              <p className="gc-facts">Was zählt für dich? Die Noten werden nach dem Speichern neu berechnet.</p>
              <div className="ph-weight-grid">
                {WEIGHT_BUCKETS.map((bucket) => (
                  <V1Field key={bucket.key} label={`${bucket.label} · ${weightDraft[bucket.key]}`}>
                    <input
                      type="range" min={0} max={100} step={5}
                      value={weightDraft[bucket.key]}
                      onChange={(event) => setWeightDraft({ ...weightDraft, [bucket.key]: Number(event.target.value) })}
                    />
                  </V1Field>
                ))}
              </div>
              <div className="co-actions">
                <V1Button variant="primary" onClick={() => void saveWeights()}>Gewichtung speichern</V1Button>
              </div>
            </div>
          )}
          <div className="co-cells">
            {rankPlants(hunt.plants).map((plant) => {
              const keeper = plant.evaluation?.isKeeper ?? false
              return (
                <div key={plant.plantInstanceId} className={`co-cand${keeper ? ' is-keeper' : ''}`}>
                  <div className="co-cand-name">{plant.label}{keeper ? ' · Keeper' : ''}</div>
                  <div className="co-cand-sub">{scoreLine(plant)}</div>
                  <button
                    type="button"
                    className={`ls-btn is-small${keeper ? ' is-keeperbtn' : ''}`}
                    style={{ marginLeft: 0 }}
                    onClick={() => setOpenPlantId((current) => (current === plant.plantInstanceId ? null : plant.plantInstanceId))}
                  >
                    {openPlantId === plant.plantInstanceId ? 'Schließen' : plant.evaluation ? 'Bogen' : 'Bewerten'}
                  </button>
                </div>
              )
            })}
          </div>
          {hunt.plants.filter((plant) => plant.plantInstanceId === openPlantId).map((plant) => (
            <div key={plant.plantInstanceId} style={{ padding: '12px 14px', borderTop: '1px solid var(--hair)' }}>
              <PhenoSheetEditor plant={plant} onSave={(sheetDraft) => saveSheet(plant, sheetDraft)} onCancel={() => setOpenPlantId(null)} />
            </div>
          ))}
        </section>
      ))}
    </V1Page>
  )
}

const WEIGHT_BUCKETS: Array<{ key: keyof PhenoWeightsDto; label: string }> = [
  { key: 'yield', label: 'Ertrag' },
  { key: 'quality', label: 'Qualität' },
  { key: 'potency', label: 'Wirkstoff' },
  { key: 'resilience', label: 'Robustheit' },
  { key: 'structure', label: 'Struktur' },
]

/** „Ertrag 40 % · Qualität 30 %" — die zwei schwersten Kriterien. */
function weightSummary(weights: PhenoWeightsDto): string {
  const summe = WEIGHT_BUCKETS.reduce((sum, bucket) => sum + (weights[bucket.key] ?? 0), 0)
  if (summe <= 0) return 'ohne Gewichtung'
  return WEIGHT_BUCKETS
    .map((bucket) => ({ label: bucket.label, anteil: Math.round(((weights[bucket.key] ?? 0) / summe) * 100) }))
    .sort((a, b) => b.anteil - a.anteil)
    .slice(0, 2)
    .map((item) => `${item.label} ${item.anteil} %`)
    .join(' · ')
}

/** Nur ein Fragment — die Zellen müssen direkte Grid-Kinder bleiben. */
function StrainRow({ children }: { children: React.ReactNode }) {
  return <>{children}</>
}

function rankPlants(plants: PhenoPlantDto[]): PhenoPlantDto[] {
  return [...plants].sort((a, b) => (b.score.total ?? -1) - (a.score.total ?? -1))
}

/** „Ertrag 8,0 · Qualität 7,5" — die zwei besten Noten, sonst „noch offen". */
function scoreLine(plant: PhenoPlantDto): string {
  const buckets: Array<[string, number | null]> = [
    ['Ertrag', plant.score.yield],
    ['Qualität', plant.score.quality],
    ['Wirkstoff', plant.score.potency],
    ['Robustheit', plant.score.resilience],
    ['Struktur', plant.score.structure],
  ]
  const known = buckets.filter((bucket): bucket is [string, number] => bucket[1] != null)
  if (known.length === 0) return 'noch nicht bewertet'
  return known
    .sort((a, b) => b[1] - a[1])
    .slice(0, 2)
    .map(([label, value]) => `${label} ${formatNumber(value, 1)}`)
    .join(' · ')
}

export default StrainsPage
