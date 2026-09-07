import { useEffect, useMemo, useState } from 'react'
import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch } from '../api'
import type { KnowledgeOverviewDto, NutrientProgramDto, WearTemplateDto } from '../types'
import { V1Page, V1Skeleton } from '../components/v1'
import { ShoppingList } from '../features/knowledge/ShoppingList'
import { SymptomPhotos } from '../features/knowledge/SymptomPhotos'
import { stichwort } from '../features/knowledge/stichwoerter'
import '../features/knowledge/knowledge.css'

type TopicId = 'rdwc' | 'addback' | 'rootrot' | 'ph-ec' | 'athena' | 'canna' | 'sensors' | 'troubleshooting'
type KnowledgeRecord = Record<string, unknown>

type Catalogs = {
  programs: NutrientProgramDto[]
  sops: KnowledgeRecord[]
  treatments: KnowledgeRecord[]
  symptoms: KnowledgeRecord[]
  setpoints: KnowledgeRecord[]
  pathogens: KnowledgeRecord[]
  wear: WearTemplateDto[]
}

type Topic = {
  id: TopicId
  title: string
  kicker: string
  intro: string
  keywords: string[]
  sections: Array<{ title: string; text: string }>
  action?: { label: string; to: string }
}

type Entry = {
  key: string
  title: string
  subtitle: string
  preview: string
  search: string
  refId?: string
  topic?: Topic
  record?: KnowledgeRecord
}

type Category = {
  id: string
  label: string
  kicker: string
  desc: string
  entries: Entry[]
}

const emptyCatalogs: Catalogs = { programs: [], sops: [], treatments: [], symptoms: [], setpoints: [], pathogens: [], wear: [] }

const topics: Topic[] = [
  {
    id: 'rdwc',
    title: 'RDWC Grundlagen',
    kicker: 'System verstehen',
    intro: 'Reservoir, Umlauf, Sauerstoff, Temperatur und Hygiene hängen in RDWC enger zusammen als bei Erde oder Coco.',
    keywords: ['rdwc', 'dwc', 'reservoir', 'wasser', 'umlauf', 'sauerstoff'],
    sections: [
      { title: 'Worum geht es?', text: 'RDWC ist ein rezirkulierendes Wassersystem. Ein Fehler im Reservoir betrifft deshalb sehr schnell alle Pflanzen.' },
      { title: 'Worauf achten?', text: 'pH, EC, Wassertemperatur, Sauerstoff, Wasserstand und saubere Hardware sind die Kernwerte.' },
      { title: 'In der App', text: 'Hydro-Setup, Addback, Sensoren und Live-Dashboard sind deshalb getrennte Workflows.' },
    ],
    action: { label: 'Hydro öffnen', to: '/hydro' },
  },
  {
    id: 'addback',
    title: 'Addback & Wasserwechsel',
    kicker: 'Nährlösung logisch ergänzen',
    intro: 'Addback ist kein blindes Nachkippen. Ziel ist, Wasserstand und Ziel-EC kontrolliert wieder in den Bereich zu bringen.',
    keywords: ['addback', 'topoff', 'ec', 'reservoir', 'nachfuellen', 'wasserwechsel'],
    sections: [
      { title: 'Prinzip', text: 'Erst Ist-Zustand messen, dann Zielwert bestimmen, dann Wasser oder Nährlösung ergänzen und nachmessen.' },
      { title: 'Fehler vermeiden', text: 'Nicht nur EC korrigieren. Wasserstand, pH, Temperatur und Pflanzenphase gehören dazu.' },
      { title: 'In der App', text: 'Der Addback-Assistent nutzt Grow, Hydro-System, Reservoirvolumen und Programm als Kontext.' },
    ],
    action: { label: 'Addback öffnen', to: '/addback' },
  },
  {
    id: 'rootrot',
    title: 'Pathogene / Root Rot',
    kicker: 'Risiko & Sofortmaßnahmen',
    intro: 'Wurzelfäule ist in Hydro kritisch, weil sich Probleme über Wasser, Biofilm und Sauerstoffmangel schnell ausbreiten können.',
    keywords: ['root', 'rot', 'wurzel', 'faeule', 'pathogen', 'hygiene', 'pythium'],
    sections: [
      { title: 'Symptome', text: 'Braune oder slimige Wurzeln, muffiger Geruch, fallender Sauerstoff, instabile Werte und schlapper Wuchs.' },
      { title: 'Sofortmaßnahmen', text: 'Temperatur, Sauerstoff, Biofilm, tote Wurzelmasse und Hygiene prüfen. Keine hektischen Mehrfachkorrekturen.' },
      { title: 'In der App', text: 'Wissen, SOPs, Sensorvertrauen und Risiken laufen hier zusammen.' },
    ],
  },
  {
    id: 'ph-ec',
    title: 'pH / EC / ORP / DO',
    kicker: 'Werte richtig deuten',
    intro: 'pH und EC müssen zusammen mit Wasserstand, Sauerstoff und Pflanzenphase gelesen werden.',
    keywords: ['ph', 'ec', 'orp', 'do', 'stabilisierung', 'naehrstoff'],
    sections: [
      { title: 'pH', text: 'pH-Drift kann normal sein, aber starke Sprünge deuten auf Puffer-, Hygiene- oder Dosierprobleme hin.' },
      { title: 'EC', text: 'Steigender EC bei fallendem Wasserstand kann auf stärkere Wasseraufnahme als Nährstoffaufnahme hinweisen.' },
      { title: 'In der App', text: 'Addback und Live-Score bewerten Werte konservativ und behaupten keine Stabilität, wenn Daten fehlen.' },
    ],
    action: { label: 'Live öffnen', to: '/' },
  },
  {
    id: 'athena',
    title: 'Athena Blended',
    kicker: 'Programm',
    intro: 'Athena wird als auswählbares Nährstoffprogramm im Grow gespeichert, damit Empfehlungen den richtigen Kontext haben.',
    keywords: ['athena', 'blended', 'grow', 'bloom'],
    sections: [
      { title: 'Ziel', text: 'Programmkontext für Grow, Addback, Zielwerte und spätere Empfehlungen.' },
      { title: 'Wichtig', text: 'Nicht nur Herstellername speichern, sondern auch Phase, Wasserquelle und Systemart berücksichtigen.' },
      { title: 'In der App', text: 'Beim Grow-Start wird das Programm gewählt und in Addback/Knowledge weiterverwendet.' },
    ],
    action: { label: 'Grow starten', to: '/grows/new' },
  },
  {
    id: 'canna',
    title: 'Canna Aqua',
    kicker: 'Programm',
    intro: 'Canna Aqua ist für rezirkulierende Systeme relevant und bleibt als Programmkontext auswählbar.',
    keywords: ['canna', 'aqua', 'vega', 'flores'],
    sections: [
      { title: 'Ziel', text: 'Nährstofflogik passend zu rezirkulierendem Hydro-System dokumentieren.' },
      { title: 'Wichtig', text: 'Programmauswahl ist kein Ersatz für Messwerte. Sie liefert Kontext für die Interpretation.' },
      { title: 'In der App', text: 'Programmwahl wird im Grow gespeichert und später für Empfehlungen genutzt.' },
    ],
    action: { label: 'Grow starten', to: '/grows/new' },
  },
  {
    id: 'sensors',
    title: 'Sensoren & Kalibrierung',
    kicker: 'Vertrauen in Messwerte',
    intro: 'Automatisierung ist nur so gut wie die Sensoren. pH/EC/ORP/DO brauchen Kalibrierung, Wartung und Plausibilitätsprüfung.',
    keywords: ['sensor', 'kalibrierung', 'wartung', 'ph', 'ec', 'orp', 'do'],
    sections: [
      { title: 'Sensorvertrauen', text: 'Keine Sensoren bedeutet nicht 100 % stabil. Dann ist die Bewertung offen und muss eingerichtet werden.' },
      { title: 'Kalibrierung', text: 'pH und EC sollten dokumentiert kalibriert werden, sonst sind Empfehlungen nicht belastbar.' },
      { title: 'In der App', text: 'Sensoren-Seite, HA-Mapping und Live-Dashboard greifen hier zusammen.' },
    ],
    action: { label: 'Sensoren öffnen', to: '/hardware' },
  },
  {
    id: 'troubleshooting',
    title: 'Symptome & Diagnose',
    kicker: 'Symptom → Ursache → Handlung',
    intro: 'Probleme sollen nicht als lose Datensätze erscheinen, sondern als geführte Diagnose.',
    keywords: ['symptom', 'diagnose', 'fehler', 'treatment', 'risiko'],
    sections: [
      { title: 'Vorgehen', text: 'Symptom beschreiben, Kontext prüfen, wahrscheinlichste Ursache wählen, eine Maßnahme durchführen und nachmessen.' },
      { title: 'Vermeiden', text: 'Mehrere starke Korrekturen gleichzeitig machen spätere Auswertung unmöglich.' },
      { title: 'In der App', text: 'Aufgaben, Wissen, Addback und Messungen machen diese Diagnose nachvollziehbar.' },
    ],
    action: { label: 'Aufgaben öffnen', to: '/aufgaben' },
  },
]

/* ── value helpers ─────────────────────────────────────────────── */
function isRecord(value: unknown): value is KnowledgeRecord {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}
function asStr(v: unknown): string | undefined {
  return typeof v === 'string' && v.trim() ? v : undefined
}
function asStrArr(v: unknown): string[] | undefined {
  if (!Array.isArray(v)) return undefined
  const out = v.filter((x): x is string => typeof x === 'string' && x.trim().length > 0)
  return out.length ? out : undefined
}
function asRecArr(v: unknown): KnowledgeRecord[] | undefined {
  if (!Array.isArray(v)) return undefined
  const out = v.filter(isRecord)
  return out.length ? out : undefined
}
function getId(record: KnowledgeRecord): string | undefined {
  return asStr(record.id) ?? asStr(record.key)
}
function getTitle(record: KnowledgeRecord, fallback: unknown): string {
  return asStr(record.name) ?? asStr(record.title) ?? asStr(record.id) ?? asStr(record.key) ?? String(fallback ?? 'Eintrag')
}
function getRecordTag(record: KnowledgeRecord): string {
  return asStr(record.type) ?? asStr(record.category) ?? asStr(record.systemType) ?? asStr(record.riskLevel) ?? asStr(record.manufacturer) ?? ''
}
// A short human-readable preview line for list entries — the first descriptive
// field, or the longest plain string, so the list shows content instead of only a
// title + cryptic tag.
function getPreview(record: KnowledgeRecord): string {
  const candidates = ['summary', 'description', 'intro', 'context', 'goal', 'overview', 'note', 'notes', 'method', 'standard']
  for (const key of candidates) {
    const value = asStr(record[key])
    if (value && value.length > 4) return value
  }
  // SOPs haben keinen Beschreibungstext — aber Schritte: „8 Schritte, geführt."
  const steps = record.steps
  if (Array.isArray(steps) && steps.length > 0) return `${steps.length} Schritte, geführt.`
  let best = ''
  for (const [key, value] of Object.entries(record)) {
    if (['name', 'title', 'id', 'key', 'type', 'category', 'schemaVersion', 'slug', 'icon'].includes(key)) continue
    const str = asStr(value)
    // „1.0" oder ein einzelnes Wort sagen in der Vorschau nichts.
    if (str && str.length >= 12 && str.length > best.length) best = str
  }
  return best
}

/** Springt eine SOP auf Symptome an, ist sie ein Notfall-Ablauf. */
function isSymptomTriggered(record: KnowledgeRecord): boolean {
  const triggers = asRecArr(record.triggers)
  if (!triggers) return false
  return triggers.some((trigger) => asStr(trigger.type) === 'Symptom')
}

const KEY_LABELS: Record<string, string> = {
  standard: 'Standard-Dosis', context: 'Kontext', method: 'Methode', timing: 'Zeitpunkt', frequency: 'Häufigkeit',
  durationStandard: 'Dauer (Standard)', durationHeavy: 'Dauer (stark)', heavy: 'Bei starkem Befall', light: 'Bei leichtem Befall',
  preventive: 'Vorbeugend', maxConcentration: 'Max. Konzentration', notes: 'Hinweis', warning: 'Warnung', compatibility: 'Kompatibilität',
  reference: 'Referenz', title: 'Titel', bestFor: 'Geeignet für', avoidFor: 'Ungeeignet für', manufacturer: 'Hersteller',
}
function humanize(key: string): string {
  if (KEY_LABELS[key]) return KEY_LABELS[key]
  const spaced = key.replace(/([A-Z])/g, ' $1').replace(/[_-]+/g, ' ').trim()
  const cased = spaced.charAt(0).toUpperCase() + spaced.slice(1)
  return cased.replace(/\b(ph|ec|orp|do|co2|vpd|ppfd|ipm|hocl|dwc|rdwc)\b/gi, (m) => m.toUpperCase())
}

const STAGE_LABELS: Record<string, string> = {
  seedling: 'Sämling', clone: 'Steckling', earlyVeg: 'Frühe Veg', veg: 'Vegetativ', lateVeg: 'Späte Veg',
  earlyFlower: 'Frühe Blüte', midFlower: 'Mittlere Blüte', flower: 'Blüte', lateFlower: 'Späte Blüte',
  ripen: 'Reife', flush: 'Spülen',
}
function num(v: unknown): number | undefined {
  return typeof v === 'number' && Number.isFinite(v) ? v : undefined
}
function range(a: unknown, b: unknown): string {
  const x = num(a)
  const y = num(b)
  if (x === undefined && y === undefined) return '–'
  if (x === undefined) return String(y)
  if (y === undefined) return String(x)
  return x === y ? String(x) : `${x}–${y}`
}

const STAGE_COLS: Array<{ label: string; render: (s: KnowledgeRecord) => string }> = [
  { label: 'pH', render: (s) => range(s.phMin, s.phMax) },
  { label: 'EC', render: (s) => range(s.ecMin, s.ecMax) },
  { label: 'ORP', render: (s) => range(s.orpMin, s.orpMax) },
  { label: 'H₂O °C', render: (s) => (num(s.waterTempDayC) !== undefined ? `${num(s.waterTempDayC)}/${num(s.waterTempNightC) ?? '–'}` : '–') },
  { label: 'VPD', render: (s) => range(s.vpdMin, s.vpdMax) },
  { label: 'PPFD', render: (s) => range(s.ppfdMin, s.ppfdMax) },
  { label: 'CO₂', render: (s) => range(s.co2Min, s.co2Max) },
]

/* ── detail renderers ──────────────────────────────────────────── */
function StageTable({ stages }: { stages: KnowledgeRecord }) {
  const rows = Object.entries(stages).filter(([, v]) => isRecord(v)) as Array<[string, KnowledgeRecord]>
  if (!rows.length) return null
  const cols = STAGE_COLS.filter((col) => rows.some(([, s]) => col.render(s) !== '–'))
  return (
    <div className="ix-kb-table-wrap">
      <table className="ix-kb-table">
        <thead>
          <tr>
            <th>Phase</th>
            {cols.map((c) => <th key={c.label}>{c.label}</th>)}
          </tr>
        </thead>
        <tbody>
          {rows.map(([stage, s]) => (
            <tr key={stage}>
              <td>{STAGE_LABELS[stage] ?? humanize(stage)}</td>
              {cols.map((c) => <td key={c.label}>{c.render(s)}</td>)}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

/* Fork AI: Wochen-Feed-Chart eines Düngeprogramms als Tabelle. Spalten = die
   Chart-Spalten (Root, Vega 1–4, Flores 1–8, Flush …), Zeilen = Komponenten
   in Reihenfolge ihres ersten Auftretens, darunter Ziel-EC und Ziel-pH.
   Zahlen in ml je Liter; Spannen als „min–max". */
function FeedChartTable({ chart }: { chart: KnowledgeRecord }) {
  const columns = (asRecArr(chart.columns) ?? []).filter((c) => Array.isArray(c.items))
  if (!columns.length) return null
  const fmt = (v: number) => v.toLocaleString('de-DE', { maximumFractionDigits: 2 })
  const components: string[] = []
  columns.forEach((c) => (asRecArr(c.items) ?? []).forEach((it) => {
    const name = asStr(it.component)
    if (name && !components.includes(name)) components.push(name)
  }))
  const cell = (c: KnowledgeRecord, name: string) => {
    const it = (asRecArr(c.items) ?? []).find((x) => asStr(x.component) === name)
    const lo = num(it?.minMlPerLiter); const hi = num(it?.maxMlPerLiter)
    if (lo === undefined) return '–'
    return hi !== undefined && hi !== lo ? `${fmt(lo)}–${fmt(hi)}` : fmt(lo)
  }
  const ph = (c: KnowledgeRecord) => {
    const lo = num(c.phMin); const hi = num(c.phMax)
    if (lo === undefined && hi === undefined) return '–'
    if (lo !== undefined && hi !== undefined && hi !== lo) return `${fmt(lo)}–${fmt(hi)}`
    return fmt((lo ?? hi) as number)
  }
  const note = asStr(chart.note)
  const unit = asStr(chart.unit) === 'mlPerLiter' ? 'ml/L' : (asStr(chart.unit) ?? '')
  return (
    <>
      <div className="ix-kb-table-wrap">
        <table className="ix-kb-table">
          <thead>
            <tr>
              <th>{unit ? `Komponente (${unit})` : 'Komponente'}</th>
              {columns.map((c) => <th key={asStr(c.id) ?? asStr(c.label)}>{asStr(c.label) ?? asStr(c.id)}</th>)}
            </tr>
          </thead>
          <tbody>
            {components.map((name) => (
              <tr key={name}>
                <td>{name}</td>
                {columns.map((c) => <td key={asStr(c.id) ?? asStr(c.label)}>{cell(c, name)}</td>)}
              </tr>
            ))}
            <tr>
              <td><strong>Ziel-EC</strong></td>
              {columns.map((c) => <td key={asStr(c.id) ?? asStr(c.label)}>{num(c.ecTarget) !== undefined ? fmt(num(c.ecTarget) as number) : '–'}</td>)}
            </tr>
            <tr>
              <td><strong>Ziel-pH</strong></td>
              {columns.map((c) => <td key={asStr(c.id) ?? asStr(c.label)}>{ph(c)}</td>)}
            </tr>
          </tbody>
        </table>
      </div>
      {note && <p className="ix-kb-lede" style={{ marginTop: 8 }}>{note}</p>}
    </>
  )
}

function ObjFacts({ obj }: { obj: KnowledgeRecord }) {
  const facts = Object.entries(obj).filter(([, v]) => asStr(v) !== undefined || num(v) !== undefined)
  if (!facts.length) return null
  return (
    <dl className="ix-kb-facts">
      {facts.map(([k, v]) => (
        <div key={k} className="ix-kb-fact">
          <dt>{humanize(k)}</dt>
          <dd>{asStr(v) ?? String(v)}</dd>
        </div>
      ))}
    </dl>
  )
}

function RefChips({ ids, index, onNavigate }: { ids: string[]; index: Map<string, RefTarget>; onNavigate: (id: string) => void }) {
  return (
    <div className="ix-kb-refs">
      {ids.map((id) => {
        const hit = index.get(id)
        // Ohne Eintrag ist es kein toter Verweis, sondern ein Stichwort: die
        // Wissensbasis nennt 65 Symptome, zu denen es keinen eigenen Datensatz
        // gibt. Vorher stand hier der rohe Schluessel — „slimy-roots-foul-smell"
        // mitten im deutschen Text, an 69 Stellen.
        if (!hit) return <span key={id} className="ix-kb-ref dead" title="Stichwort ohne eigenen Eintrag">{stichwort(id)}</span>
        return (
          <button key={id} type="button" className="ix-kb-ref" onClick={() => onNavigate(id)}>
            {hit.title}<span className="arr">↗</span>
          </button>
        )
      })}
    </div>
  )
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div className="ix-kb-sec">
      <h4>{title}</h4>
      {children}
    </div>
  )
}

function BulletList({ items }: { items: string[] }) {
  return <ul className="ix-kb-ul">{items.map((it, i) => <li key={i}>{it}</li>)}</ul>
}

const ID_ARRAY_FIELDS: Array<[string, string]> = [
  ['symptoms', 'Symptome'],
  ['targetSymptoms', 'Ziel-Symptome'],
  ['suggestedTreatmentIds', 'Empfohlene Maßnahmen'],
  ['suggestedSopIds', 'Empfohlene SOPs'],
]
const TEXT_ARRAY_FIELDS: Array<[string, string]> = [
  ['requiredMaterials', 'Material'],
  ['applicableSetups', 'Geeignete Setups'],
  ['possibleCauses', 'Mögliche Ursachen'],
  ['diagnosticChecks', 'Diagnose-Checks'],
  ['replacementTriggers', 'Austausch-Anzeichen'],
]

function RecordDetail({ category, record, index, onNavigate }: { category: Category; record: KnowledgeRecord; index: Map<string, RefTarget>; onNavigate: (id: string) => void }) {
  const consumed = new Set<string>(['id', 'key', 'name', 'title', 'schemaVersion', 'slug', 'icon', 'stepType'])
  const mark = (...keys: string[]) => keys.forEach((k) => consumed.add(k))

  const title = getTitle(record, record)

  const metaKeys: Array<{ k: string; fmt?: (v: unknown) => string; tone?: (v: unknown) => string }> = [
    { k: 'type' },
    { k: 'category' },
    { k: 'systemType' },
    { k: 'scientificName' },
    { k: 'riskLevel', fmt: (v) => `Risiko: ${v}`, tone: (v) => (v === 'High' || v === 'Critical' ? 'crit' : v === 'Medium' ? 'warn' : 'ok') },
    { k: 'treatable', fmt: (v) => (v ? 'Behandelbar' : 'Nicht behandelbar'), tone: (v) => (v ? 'ok' : 'crit') },
    { k: 'durationDays', fmt: (v) => `${v} Tage` },
    { k: 'estimatedDurationMinutes', fmt: (v) => `~${v} min` },
    { k: 'expectedLifespanDays', fmt: (v) => `Lebensdauer ${v} T` },
    { k: 'inspectionIntervalDays', fmt: (v) => `Prüfung alle ${v} T` },
  ]
  const metaTags = metaKeys
    .map((spec) => {
      const v = record[spec.k]
      mark(spec.k)
      if (v === undefined || v === null || v === '') return null
      return { key: spec.k, text: spec.fmt ? spec.fmt(v) : String(v), tone: spec.tone ? spec.tone(v) : '' }
    })
    .filter((t): t is { key: string; text: string; tone: string } => t !== null)

  const lede = asStr(record.summary) ?? asStr(record.description) ?? asStr(record.notes) ?? asStr(record.intro)
  mark('summary', 'description', 'notes', 'intro')

  const bestFor = asStr(record.bestFor)
  const avoidFor = asStr(record.avoidFor)
  mark('bestFor', 'avoidFor')

  const steps = (asRecArr(record.steps) ?? []).slice().sort((a, b) => (num(a.order) ?? 0) - (num(b.order) ?? 0))
  mark('steps')

  const treatmentSopId = asStr(record.treatmentSopId)
  mark('treatmentSopId')

  const dosage = isRecord(record.dosage) ? record.dosage : undefined
  const application = isRecord(record.application) ? record.application : undefined
  const stages = isRecord(record.stages) ? record.stages : undefined
  mark('dosage', 'application', 'stages')

  // Fork AI: Wochen-Feed-Chart (Düngeprogramme)
  const feedChart = isRecord(record.feedChart) ? record.feedChart : undefined
  mark('feedChart')

  const triggers = asRecArr(record.triggers)
  const triggerTypes = triggers ? triggers.map((t) => asStr(t.type)).filter((x): x is string => !!x) : undefined
  mark('triggers')

  const sources = asRecArr(record.sources)
  mark('sources')

  TEXT_ARRAY_FIELDS.forEach(([k]) => mark(k))
  ID_ARRAY_FIELDS.forEach(([k]) => mark(k))

  // generic leftovers
  const leftover = Object.keys(record).filter((k) => !consumed.has(k))
  const leftoverFacts = leftover.filter((k) => asStr(record[k]) !== undefined || num(record[k]) !== undefined || typeof record[k] === 'boolean')
  const leftoverArrays = leftover.map((k) => [k, asStrArr(record[k])] as const).filter((p): p is [string, string[]] => p[1] !== undefined)

  return (
    <>
      <span className="kk">{category.kicker}</span>
      <h2>{title}</h2>
      {metaTags.length > 0 && (
        <div className="ix-kb-meta">
          {metaTags.map((t) => <span key={t.key} className={t.tone ? `ix-kb-tag ${t.tone}` : 'ix-kb-tag'}>{t.text}</span>)}
        </div>
      )}
      {lede && <p className="ix-kb-lede">{lede}</p>}

      {/* Die eigenen Aufnahmen zu diesem Symptom — direkt unter der
          Beschreibung, weil ein Bild schneller zu erfassen ist als drei
          Absaetze. Nur eigene: fremde Beispielbilder waeren nicht zu haben,
          ohne fremde Rechte zu verletzen, und ein Bild aus dem eigenen Zelt
          sagt bei gleichem Licht und gleicher Kamera ohnehin mehr. */}
      {category.id === 'symptoms' && typeof record.id === 'string' && (
        <SymptomPhotos symptomId={record.id} symptomName={title} />
      )}

      {bestFor && <Section title="Geeignet für"><p>{bestFor}</p></Section>}
      {avoidFor && <Section title="Ungeeignet für"><p>{avoidFor}</p></Section>}

      {steps.length > 0 && (
        <Section title="Ablauf">
          <div className="ix-kb-steps">
            {steps.map((s, i) => (
              <div key={asStr(s.id) ?? i} className="ix-kb-step">
                <div className="h">
                  <span className="num">{String(num(s.order) ?? i + 1).padStart(2, '0')}</span>
                  <strong>{asStr(s.title) ?? `Schritt ${i + 1}`}</strong>
                </div>
                {asStr(s.description) && <p>{asStr(s.description)}</p>}
              </div>
            ))}
          </div>
        </Section>
      )}

      {TEXT_ARRAY_FIELDS.map(([k, label]) => {
        const items = asStrArr(record[k])
        return items ? <Section key={k} title={label}><BulletList items={items} /></Section> : null
      })}

      {ID_ARRAY_FIELDS.map(([k, label]) => {
        const items = asStrArr(record[k])
        return items ? <Section key={k} title={label}><RefChips ids={items} index={index} onNavigate={onNavigate} /></Section> : null
      })}

      {treatmentSopId && (
        <Section title="Behandlungs-SOP"><RefChips ids={[treatmentSopId]} index={index} onNavigate={onNavigate} /></Section>
      )}

      {dosage && <Section title="Dosierung"><ObjFacts obj={dosage} /></Section>}
      {application && <Section title="Anwendung"><ObjFacts obj={application} /></Section>}
      {stages && <Section title="Phasen-Sollwerte"><StageTable stages={stages} /></Section>}
      {feedChart && <Section title="Feed-Chart (je Woche)"><FeedChartTable chart={feedChart} /></Section>}

      {triggerTypes && triggerTypes.length > 0 && (
        <Section title="Auslöser"><BulletList items={triggerTypes.map(humanize)} /></Section>
      )}

      {sources && (
        <Section title="Quellen">
          <BulletList items={sources.map((s) => [asStr(s.title), asStr(s.reference)].filter(Boolean).join(' — ') || asStr(s.url) || 'Quelle')} />
        </Section>
      )}

      {leftoverArrays.map(([k, items]) => (
        <Section key={k} title={humanize(k)}><BulletList items={items} /></Section>
      ))}
      {leftoverFacts.length > 0 && (
        <Section title="Weitere Angaben">
          <dl className="ix-kb-facts">
            {leftoverFacts.map((k) => {
              const v = record[k]
              const text = typeof v === 'boolean' ? (v ? 'Ja' : 'Nein') : (asStr(v) ?? String(v))
              return <div key={k} className="ix-kb-fact"><dt>{humanize(k)}</dt><dd>{text}</dd></div>
            })}
          </dl>
        </Section>
      )}
    </>
  )
}

function TopicDetail({ topic }: { topic: Topic }) {
  return (
    <>
      <span className="kk">{topic.kicker}</span>
      <h2>{topic.title}</h2>
      <p className="ix-kb-lede">{topic.intro}</p>
      {topic.sections.map((s) => <Section key={s.title} title={s.title}><p>{s.text}</p></Section>)}
      {topic.action && (
        <div className="ix-kb-sec">
          <Link to={topic.action.to} className="v1-button is-primary">{topic.action.label}</Link>
        </div>
      )}
    </>
  )
}

type RefTarget = { catId: string; entryKey: string; title: string }

function KnowledgePage() {
  const [catalogs, setCatalogs] = useState<Catalogs>(emptyCatalogs)
  const [query, setQuery] = useState('')
  const [categoryId, setCategoryId] = useState<string | null>(null)
  const [entryKey, setEntryKey] = useState<string | null>(null)
  const [detailOpen, setDetailOpen] = useState(false)
  const [listPanel, setListPanel] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    const controller = new AbortController()

    async function load() {
      setLoading(true)
      const safe = async <T,>(path: string, fallback: T): Promise<T> => {
        try { return await apiFetch<T>(path, { signal: controller.signal }) } catch { return fallback }
      }

      const [overview, sops, treatments, symptoms, setpoints, pathogens, wear] = await Promise.all([
        safe<KnowledgeOverviewDto>('/api/knowledge', { programs: [], playbooks: [] }),
        safe<KnowledgeRecord[]>('/api/knowledge/sops', []),
        safe<KnowledgeRecord[]>('/api/knowledge/treatments', []),
        safe<KnowledgeRecord[]>('/api/knowledge/symptoms', []),
        safe<KnowledgeRecord[]>('/api/knowledge/setpoints', []),
        safe<KnowledgeRecord[]>('/api/knowledge/pathogens', []),
        safe<WearTemplateDto[]>('/api/knowledge/wear', []),
      ])

      if (controller.signal.aborted) return
      setCatalogs({ programs: overview.programs ?? [], sops, treatments, symptoms, setpoints, pathogens, wear })
      setLoading(false)
    }

    void load()
    return () => controller.abort()
  }, [])

  const categories = useMemo<Category[]>(() => {
    const recCat = (id: string, label: string, kicker: string, desc: string, records: KnowledgeRecord[]): Category => ({
      id, label, kicker, desc,
      entries: records.map((raw, i) => {
        const rec = isRecord(raw) ? raw : {}
        return {
          key: `${id}-${getId(rec) ?? i}`,
          refId: getId(rec),
          title: getTitle(rec, raw),
          // SOPs tragen Notfall/Routine wie im Entwurf — abgeleitet aus den
          // Triggern: was auf Symptome anspringt, ist ein Notfall-Ablauf.
          subtitle: id === 'sops' ? (isSymptomTriggered(rec) ? 'Notfall' : 'Routine') : getRecordTag(rec),
          preview: getPreview(rec),
          search: `${getTitle(rec, raw)} ${JSON.stringify(raw)}`.toLowerCase(),
          record: rec,
        }
      }),
    })

    const grundlagen: Category = {
      id: 'grundlagen', label: 'Grundlagen', kicker: 'Guide',
      desc: 'Kompakte Erklärungen zu RDWC, Addback, Werten und Diagnose.',
      entries: topics.map((t) => ({
        key: `grundlagen-${t.id}`,
        title: t.title,
        subtitle: t.kicker,
        preview: t.intro,
        search: [t.title, t.kicker, t.intro, ...t.keywords, ...t.sections.map((s) => `${s.title} ${s.text}`)].join(' ').toLowerCase(),
        topic: t,
      })),
    }

    return [
      grundlagen,
      recCat('sops', 'SOPs', 'SOP', 'Schritt-für-Schritt-Prozeduren für den Betrieb.', catalogs.sops),
      recCat('treatments', 'Maßnahmen', 'Maßnahme', 'Behandlungen gegen Symptome und Schädlinge.', catalogs.treatments),
      recCat('symptoms', 'Symptome', 'Symptom', 'Symptom, mögliche Ursache und empfohlene Maßnahme.', catalogs.symptoms),
      recCat('pathogens', 'Pathogene', 'Pathogen', 'Erreger, Risiko-Level und Gegenmaßnahmen.', catalogs.pathogens),
      recCat('setpoints', 'Sollwerte', 'Zielwerte', 'Phasen-Zielwerte für pH, EC, ORP und Klima.', catalogs.setpoints),
      recCat('programs', 'Programme', 'Programm', 'Nährstoff-Programme und ihr Einsatzkontext.', catalogs.programs as unknown as KnowledgeRecord[]),
      recCat('wear', 'Verschleiß', 'Verschleiß', 'Lebensdauer und Austausch-Anzeichen für Hardware.', catalogs.wear as unknown as KnowledgeRecord[]),
    ]
  }, [catalogs])

  const refIndex = useMemo<Map<string, RefTarget>>(() => {
    const map = new Map<string, RefTarget>()
    for (const cat of categories) {
      for (const entry of cat.entries) {
        if (entry.refId && !map.has(entry.refId)) map.set(entry.refId, { catId: cat.id, entryKey: entry.key, title: entry.title })
      }
    }
    return map
  }, [categories])

  /**
   * Suchtext ohne Umlaute und ohne Scharf-s.
   *
   * „Wurzelfaeule" fand nichts, obwohl der Eintrag „Wurzelfäule" heißt — wer
   * am Handy tippt oder sich die Umlaute spart, kam nicht ans Ziel. Die
   * Faltung gilt für beide Seiten des Vergleichs, sonst hilft sie nur in eine
   * Richtung.
   */
  const falte = (text: string) => text
    .toLowerCase()
    .replace(/ä/g, 'ae').replace(/ö/g, 'oe').replace(/ü/g, 'ue').replace(/ß/g, 'ss')

  const q = falte(query.trim())
  const searchResults = useMemo(() => {
    if (!q) return []
    // Rank by match quality so exact/title hits come first and body-only matches
    // (via the serialized record) rank lowest — "calmag" should surface the CalMag
    // entries before an SOP that merely references them.
    const scored: Array<{ cat: Category; entry: Entry; score: number }> = []
    for (const cat of categories) {
      for (const entry of cat.entries) {
        const title = falte(entry.title)
        let score = 0
        if (title === q) score = 100
        else if (title.startsWith(q)) score = 80
        else if (title.includes(q)) score = 60
        else if (falte(entry.subtitle).includes(q)) score = 40
        else if (falte(entry.preview).includes(q)) score = 30
        else if (falte(entry.search).includes(q)) score = 15
        if (score > 0) scored.push({ cat, entry, score })
      }
    }
    scored.sort((a, b) => b.score - a.score || a.entry.title.localeCompare(b.entry.title))
    return scored.slice(0, 60)
  }, [q, categories])

  const currentCategory = categories.find((c) => c.id === categoryId) ?? null
  const entries = currentCategory?.entries ?? []
  const selectedEntry = entries.find((e) => e.key === entryKey) ?? entries[0] ?? null

  function openTarget(target: RefTarget) {
    setQuery('')
    setCategoryId(target.catId)
    setEntryKey(target.entryKey)
    setDetailOpen(true)
  }
  function navigateToId(id: string) {
    const hit = refIndex.get(id)
    if (hit) openTarget(hit)
  }
  const detailEntry = detailOpen ? selectedEntry : null
  const detailCategory = currentCategory
  const totalCount = categories.reduce((sum, cat) => sum + cat.entries.length, 0)

  // ---------- Zone 1 + 2: SOPs, getrennt nach Notfall und Routine ----------
  const sopsKategorie = categories.find((cat) => cat.id === 'sops') ?? null
  const notfallSops = sopsKategorie?.entries.filter((entry) => entry.subtitle === 'Notfall') ?? []
  const routineSops = (sopsKategorie?.entries.filter((entry) => entry.subtitle !== 'Notfall') ?? [])
    .slice()
    .sort((a, b) => sopSortRang(a) - sopSortRang(b))

  // ---------- Zone 3: Bibliothek ----------
  const panels: Array<{ schluessel: string; label: string; ton: string; hinweis: string; eintraege: Array<{ cat: Category; entry: Entry }> }> = [
    { schluessel: 'symptoms', label: 'Symptome', ton: 'warn', hinweis: 'Symptom → Ursache → Maßnahme' },
    { schluessel: 'treatments', label: 'Maßnahmen', ton: 'warn', hinweis: 'mit Dosierung & Timing' },
    { schluessel: 'pathogens', label: 'Pathogene', ton: 'danger', hinweis: 'Risiko & Gegenmaßnahmen' },
    { schluessel: 'grundlagen', label: 'Grundlagen', ton: 'accent', hinweis: 'verstehen, nicht nachschlagen' },
    { schluessel: 'setpoints+programs', label: 'Zielwerte & Programme', ton: 'info', hinweis: 'Quelle der Kachel-Ziele' },
    { schluessel: 'wear', label: 'Verschleiß', ton: 'muted', hinweis: 'Lebensdauer & Austausch' },
  ].map((panel) => ({
    ...panel,
    eintraege: panel.schluessel.split('+').flatMap((id) => {
      const cat = categories.find((item) => item.id === id)
      return cat ? cat.entries.map((entry) => ({ cat, entry })) : []
    }),
  }))

  const offenesPanel = panels.find((panel) => panel.schluessel === listPanel) ?? null

  function zeigeEintrag(cat: Category, entry: Entry) {
    setCategoryId(cat.id)
    setEntryKey(entry.key)
    setDetailOpen(true)
  }

  const gridEntries: Array<{ cat: Category; entry: Entry }> = q
    ? searchResults.map(({ cat, entry }) => ({ cat, entry }))
    : []

  let body: ReactNode
  if (loading) {
    body = <V1Skeleton tiles={6} label="Lade Wissensbasis" />
  } else if (detailEntry) {
    body = (
      <section className="ls-panel" data-audit="knowledge-detail">
        <div className="ls-panel-head">
          <button type="button" className="ls-btn is-small" style={{ marginLeft: 0 }} onClick={() => setDetailOpen(false)}>← Zurück</button>
        </div>
        <div className="kb-detail">
          {detailEntry.topic && <TopicDetail topic={detailEntry.topic} />}
          {detailEntry.record && detailCategory && <RecordDetail category={detailCategory} record={detailEntry.record} index={refIndex} onNavigate={navigateToId} />}
        </div>
      </section>
    )
  } else if (q) {
    // ---------- Suche: über alles, als kompakte Zeilen ----------
    body = gridEntries.length === 0 ? (
      <p className="gc-facts">Für „{query}" wurde nichts gefunden — durchsucht wurden alle {totalCount} Einträge.</p>
    ) : (
      <section className="ls-panel" data-audit="knowledge-search-results">
        <div className="ls-panel-head">
          <span className="ls-label">Treffer · {gridEntries.length}</span>
        </div>
        {gridEntries.map(({ cat, entry }) => (
          <button key={`${cat.id}-${entry.key}`} type="button" className="co-row kb-rowbtn" onClick={() => zeigeEintrag(cat, entry)}>
            <span className={`kb-tag is-${toneFuer(cat.id, entry)}`}>{cat.label}</span>
            <span className="co-row-text">{entry.title}</span>
            {entry.preview && <span className="kb-row-preview">{entry.preview}</span>}
          </button>
        ))}
      </section>
    )
  } else if (offenesPanel) {
    // ---------- Ein Bereich komplett, als Zeilen ----------
    body = (
      <section className="ls-panel" data-audit="knowledge-panel-list">
        <div className="ls-panel-head">
          <button type="button" className="ls-btn is-small" style={{ marginLeft: 0 }} onClick={() => setListPanel(null)}>← Übersicht</button>
          <span className="ls-label">{offenesPanel.label} · {offenesPanel.eintraege.length}</span>
          <span className="ls-panel-meta">{offenesPanel.hinweis}</span>
        </div>
        {offenesPanel.eintraege.map(({ cat, entry }) => (
          <button key={entry.key} type="button" className="co-row kb-rowbtn" onClick={() => zeigeEintrag(cat, entry)}>
            <span className="co-row-text">{entry.title}</span>
            <span className="co-row-value is-faint">{zeilenMeta(cat.id, entry)}</span>
          </button>
        ))}
      </section>
    )
  } else {
    // ---------- Übersicht: drei Zonen nach Dringlichkeit ----------
    body = (
      <>
        {/* Zone 1 · Notfall: wenn etwas schiefgeht, sucht niemand. */}
        <section data-audit="knowledge-emergency">
          <div className="kb-zone is-danger"><span>Im Notfall</span><i /></div>
          <div className="co-grid is-300">
            {notfallSops.map((entry) => (
              <div key={entry.key} className="kb-card is-danger">
                <span className="kb-tag is-danger">SOP · Notfall</span>
                <div className="kb-card-title">{entry.title}</div>
                {entry.preview && <p>{entry.preview}</p>}
                <button type="button" className="ls-btn is-small is-dangerfill" style={{ marginLeft: 0 }} onClick={() => sopsKategorie && zeigeEintrag(sopsKategorie, entry)}>
                  {/* Nicht „Geführt starten": auf der Wissensseite gibt es
                      keinen Grow-Bezug, der Knopf kann also gar nichts
                      starten — er oeffnet den Ablauf zum Lesen. Gestartet
                      wird ein Ablauf am Grow oder aus einem Risiko heraus. */}
                  Ablauf öffnen
                </button>
              </div>
            ))}
            <div className="kb-card">
              <span className="kb-tag is-warn">Vom Symptom aus</span>
              <div className="kb-card-title">Ich sehe etwas — was ist das?</div>
              <p>{panels[0].eintraege.length} Symptome mit Ursachen und passender Maßnahme — von „bräunliche Wurzeln" bis „CalMag-Mangel".</p>
              <button type="button" className="ls-btn is-small" style={{ marginLeft: 0 }} onClick={() => setListPanel('symptoms')}>
                Symptome durchsuchen
              </button>
            </div>
          </div>
        </section>

        {/* Zone 2 · Abläufe: vergleichbar in einer Tabelle statt elf Karten. */}
        {routineSops.length > 0 && sopsKategorie && (
          <section className="ls-panel" data-audit="knowledge-sops-table">
            <div className="ls-panel-head">
              <span className="ls-label">Abläufe · SOPs</span>
              <span className="ls-panel-meta">geführt, Schritt für Schritt · laufende stehen unter Aufgaben</span>
            </div>
            <div className="co-table-wrap">
            {/* Die letzte Spalte traegt einen Knopf; mit .5fr ragte er 6 px
                ueber die Tabellenflaeche hinaus, und die Zeilentrenner hoerten
                sichtbar vor dem rechten Rand auf. */}
            <div className="co-table" style={{ gridTemplateColumns: '2fr .8fr .4fr .45fr .7fr' }}>
              <div className="co-th">Ablauf</div>
              <div className="co-th">Art</div>
              <div className="co-th">Dauer</div>
              <div className="co-th">Schritte</div>
              <div className="co-th" aria-hidden="true"></div>
              {routineSops.map((entry) => {
                const fakten = sopFakten(entry)
                return (
                  <SopZeile key={entry.key}>
                    <div className="co-td is-name">{entry.title}</div>
                    <div className="co-td is-muted">{fakten.art}</div>
                    <div className="co-td">{fakten.dauer}</div>
                    <div className="co-td">{fakten.schritte}</div>
                    <div className="co-td">
                      <button type="button" className="ls-btn is-small" style={{ marginLeft: 0 }} onClick={() => zeigeEintrag(sopsKategorie, entry)}>Öffnen</button>
                    </div>
                  </SopZeile>
                )
              })}
            </div>
            </div>
          </section>
        )}

        {/* Zone 3 · Bibliothek: Beispiele zeigen, der Rest ist einen Klick weit. */}
        <section data-audit="knowledge-library">
          <div className="kb-zone"><span>Bibliothek · Nachschlagen</span><i /></div>
          <div className="co-grid is-300">
            {panels.map((panel) => (
              <div key={panel.schluessel} className="ls-panel">
                <div className="ls-panel-head">
                  <span className={`kb-tag is-${panel.ton}`}>{panel.label} · {panel.eintraege.length}</span>
                  <span className="ls-panel-meta">{panel.hinweis}</span>
                </div>
                {panel.eintraege.slice(0, 3).map(({ cat, entry }) => (
                  <button key={entry.key} type="button" className="co-row kb-rowbtn" onClick={() => zeigeEintrag(cat, entry)}>
                    <span className="co-row-text">{entry.title}</span>
                    <span className="co-row-value is-faint">{zeilenMeta(cat.id, entry)}</span>
                  </button>
                ))}
                {panel.eintraege.length > 3 && (
                  <button type="button" className="co-row kb-rowbtn kb-more" onClick={() => setListPanel(panel.schluessel)}>
                    Alle {panel.eintraege.length} anzeigen
                  </button>
                )}
              </div>
            ))}
          </div>
        </section>
      </>
    )
  }

  return (
    <V1Page
      eyebrow="Wissen"
      title="SOPs & Bibliothek"
      action={
        <input
          className="kb-search"
          data-audit="knowledge-search"
          value={query}
          onChange={(event) => { setQuery(event.target.value); setDetailOpen(false); setListPanel(null) }}
          placeholder="Suchen: Wurzelfäule, Addback, CalMag …"
          aria-label="Wissensbasis durchsuchen"
        />
      }
    >
      {body}
      {/* Die Materiallisten der Ablaeufe, zusammengefuehrt. Nur auf der
          Uebersicht: unter einer geoeffneten SOP klebte sie bisher als
          fremder Block am Fuss und gehoerte dort zu nichts. Der eigene
          Weg dahin ist /einkaufsliste. */}
      {!detailOpen && !listPanel && <ShoppingList />}
    </V1Page>
  )
}

/** Nur ein Fragment — die Zellen müssen direkte Grid-Kinder bleiben. */
function SopZeile({ children }: { children: ReactNode }) {
  return <>{children}</>
}

/** Ton je Kategorie — nach Gefahr: Notfall rot, Routine grün, Zielwerte cyan. */
function toneFuer(catId: string, entry: Entry): string {
  if (catId === 'pathogens') return 'danger'
  if (catId === 'sops') return entry.subtitle === 'Notfall' ? 'danger' : 'accent'
  if (catId === 'treatments' || catId === 'symptoms') return 'warn'
  if (catId === 'setpoints' || catId === 'programs') return 'info'
  if (catId === 'wear') return 'muted'
  return 'accent'
}

/** Routine zuerst, dann Abläufe, dann Mehrtägiges — und darin nach Aufwand. */
function sopSortRang(entry: Entry): number {
  const typ = entry.record ? asStr(entry.record.type) : undefined
  const gruppe = typ === 'Recurring' ? 0 : typ === 'MultiDay' ? 2 : 1
  const minuten = entry.record && typeof entry.record.estimatedDurationMinutes === 'number'
    ? entry.record.estimatedDurationMinutes : 999
  return gruppe * 10000 + minuten
}

/** Art, Dauer und Schrittzahl einer SOP — die Spalten der Ablauf-Tabelle. */
function sopFakten(entry: Entry): { art: string; dauer: string; schritte: string } {
  const rec = entry.record ?? {}
  const typ = asStr(rec.type)
  const art = typ === 'Recurring' ? 'Routine' : typ === 'MultiDay' ? 'Mehrtägig' : 'Ablauf'
  const tage = typeof rec.durationDays === 'number' ? rec.durationDays : undefined
  const minuten = typeof rec.estimatedDurationMinutes === 'number' ? rec.estimatedDurationMinutes : undefined
  const dauer = tage != null ? `${tage} T`
    : minuten != null ? (minuten >= 90 ? `~${(minuten / 60).toLocaleString('de-DE', { maximumFractionDigits: 1 })} h` : `~${minuten} min`)
      : '—'
  const schritte = Array.isArray(rec.steps) ? String(rec.steps.length) : '—'
  return { art, dauer, schritte }
}

/**
 * Die kleine Angabe rechts in einer Bibliothekszeile — je Kategorie das,
 * was beim Überfliegen hilft: wohin ein Symptom führt, wie riskant ein
 * Pathogen ist, wie lange ein Teil hält.
 */
function zeilenMeta(catId: string, entry: Entry): string {
  const rec = entry.record ?? {}
  switch (catId) {
    case 'symptoms': {
      if (asStr(rec.treatmentSopId)) return '→ SOP'
      const massnahmen = asStrArr(rec.suggestedTreatmentIds)
      return massnahmen ? `→ ${massnahmen.length} Maßnahme${massnahmen.length === 1 ? '' : 'n'}` : 'Diagnose'
    }
    case 'treatments':
      return isRecord(rec.dosage) ? 'Dosierung' : 'Anwendung'
    case 'pathogens': {
      if (rec.treatable === false) return 'nicht behandelbar'
      const risiko = asStr(rec.riskLevel)
      return risiko === 'High' || risiko === 'Critical' ? 'Risiko hoch' : risiko === 'Medium' ? 'Risiko mittel' : 'Risiko gering'
    }
    case 'grundlagen':
      return 'Guide'
    case 'setpoints':
      return 'Zielwerte'
    case 'programs':
      return 'Programm'
    case 'wear': {
      const tage = typeof rec.expectedLifespanDays === 'number' ? rec.expectedLifespanDays : undefined
      if (tage != null) return `~${Math.round(tage / 30)} Monate`
      const pruefung = typeof rec.inspectionIntervalDays === 'number' ? rec.inspectionIntervalDays : undefined
      return pruefung != null ? `Prüfung ${pruefung} T` : ''
    }
    default:
      return entry.subtitle
  }
}

export default KnowledgePage
