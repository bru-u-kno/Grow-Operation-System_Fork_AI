import { useEffect, useMemo, useRef, useState } from 'react'
import { nurDatum } from '../features/grows/nur-datum'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { apiFetch, formatApiError } from '../api'
import type { GrowDetail, GrowEntryPoint, GrowStatus, GrowSummary, GrowUpsertPayload, HydroSetupDto, KnowledgeOverviewDto, NutrientProgramDto, SeedType, StartMaterial, StrainDto, TentDto, PlantInstanceDto } from '../types'
import { V1Alert, V1Badge, V1Button, V1Card, V1Empty, V1Field, V1LinkButton, V1Page, V1Section, V1Skeleton } from '../components/v1'
import { formatLiters, toNullableInt } from '../components/v1-utils'
import { classNames } from '../utils'
import { aufstellungName, einstiegName, materialName, samenName, statusName, zeltZweckName } from '../deutsche-woerter'
import { V1Sheet } from '../components/V1Sheet'
import { deckung, istEigenesProgramm, wechselNoetig } from '../features/grows/programm-deckung'
import { GrowPlanPanel } from '../features/grows/GrowPlanPanel'
import { buildTimeline, canCreate, checkPlan } from '../features/grows/grow-plan-model'
import '../features/grows/grows.css'

const entryPoints: GrowEntryPoint[] = ['Germination', 'Seedling', 'Veg', 'Flower', 'Flush']
const statuses: GrowStatus[] = ['Planning', 'Running', 'Completed', 'Aborted']
const seedTypes: SeedType[] = ['Feminized', 'Autoflower', 'Regular']
const startMaterials: StartMaterial[] = ['Seed', 'Clone']

function emptyForm(): GrowUpsertPayload {
  return {
    templateId: null, name: '', tentId: null, systemId: null, setupId: null, strain: null, breeder: null, seedType: 'Feminized', startMaterial: 'Seed', germinationMethod: 'PaperTowel',
    cloneSource: null, cloneIsRooted: false, phenoNumber: null, breederFlowerWeeksMin: null, breederFlowerWeeksMax: null, plannedVegDays: null, strainId: null, setpointProfileId: null, hydroStyle: 'RDWC', plantCount: null, reservoirSize: null,
    containerSize: null, propagationMedium: 'Rockwool', light: null, hasChiller: false, waterSource: 'RO', nutrients: null, startDate: new Date().toISOString().slice(0, 10),
    entryPoint: 'Germination', daysAlreadyInPhase: null, autoflowerDaysSinceGermination: null, flipDate: null, notes: null, status: 'Planning', environment: 'Indoor',
  }
}

function GrowSetupPage() {
  const { growId } = useParams()
  const navigate = useNavigate()
  const isEditing = Boolean(growId)
  const [tents, setTents] = useState<TentDto[]>([])
  const [strains, setStrains] = useState<StrainDto[]>([])
  const [hydroSetups, setHydroSetups] = useState<HydroSetupDto[]>([])
  const [programs, setPrograms] = useState<NutrientProgramDto[]>([])
  // Fuer die Belegungspruefung: welche anderen Grows sitzen schon im Zelt.
  const [otherGrows, setOtherGrows] = useState<GrowSummary[]>([])
  const [form, setForm] = useState<GrowUpsertPayload>(() => emptyForm())
  const [customProgram, setCustomProgram] = useState('')
  // Die gespeicherte Programm-Id des Grows. Ohne sie hing das Programm am
  // Namensvergleich — und ein fehlgeschlagener Wissens-Abruf haette es beim
  // Speichern still auf null gesetzt.
  const [feedProgramId, setFeedProgramId] = useState<string | null>(null)
  // Fork AI (Grow-Plan): das Programm beim Öffnen — ein Wechsel fragt nach den eigenen Änderungen.
  const [programmVorher, setProgrammVorher] = useState<string | null>(null)
  const [wechsel, setWechsel] = useState<{ anzahl: number; name: string } | null>(null)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Wie viele Pflanzen einzeln erfasst sind. Sind es welche, sind SIE die
  // Wahrheit ueber die Anzahl — der Server zieht plantCount danach.
  const [erfasstePflanzen, setErfasstePflanzen] = useState(0)
  /* forkai.105: Die Pflanzen selbst, nicht nur ihre Zahl. „Leer" im Topf muss
     wissen, WELCHE Pflanze es entfernt — sonst kann es nur zuweisen, so wie
     bisher. */
  const [pflanzenListe, setPflanzenListe] = useState<PlantInstanceDto[]>([])

  /* Die Pflanzen werden MIT dem Grow geladen, nicht daneben.

     Die erste Fassung hatte dafuer einen eigenen Effekt — und der lief gegen
     den Hauptlader: dessen `setForm({ ...emptyForm(), … })` kennt `toepfe`
     nicht und loeschte die eben gesetzte Belegung wieder. Die Pflanzen-Abfrage
     ist die schnellere von beiden, also gewann das Loeschen praktisch immer.
     Auf `/grows/1/setup` stand deshalb „0 von 4 Toepfen belegt", waehrend vier
     Pflanzen mit ihren Sorten in der Datenbank lagen — das GEGENTEIL von dem,
     was der Kommentar an dieser Stelle behauptete.

     Gefunden vom Pruefer. Zwei Effekte, die dasselbe Feld schreiben, sind ein
     Wettlauf; die Reihenfolge zu sortieren waere eine Wette. Hier gibt es
     jetzt einen Schreiber. */
  useEffect(() => {
    const controller = new AbortController()
    async function load() {
      setLoading(true)
      setError(null)
      try {
        const [tentData, hydroData, knowledge, grow, growsData, strainData, pflanzen] = await Promise.all([
          apiFetch<TentDto[]>('/api/settings/tents', { signal: controller.signal }),
          apiFetch<HydroSetupDto[]>('/api/hydro-setups?includeArchived=true', { signal: controller.signal }),
          apiFetch<KnowledgeOverviewDto>('/api/knowledge', { signal: controller.signal }),
          isEditing && growId ? apiFetch<GrowDetail>(`/api/grows/${growId}`, { signal: controller.signal }) : Promise.resolve(null),
          apiFetch<GrowSummary[]>('/api/grows?archived=false', { signal: controller.signal }).catch(() => []),
          apiFetch<StrainDto[]>('/api/strains', { signal: controller.signal }).catch(() => [] as StrainDto[]),
          isEditing && growId
            ? apiFetch<PlantInstanceDto[]>(`/api/plants?growId=${growId}`, { signal: controller.signal })
                .catch(() => [] as PlantInstanceDto[])
            : Promise.resolve([] as PlantInstanceDto[]),
        ])
        if (controller.signal.aborted) return
        setTents(tentData)
        setHydroSetups(hydroData.filter((setup) => setup.status === 'Active'))
        setPrograms(knowledge.programs ?? [])
        setOtherGrows(growsData)
        setStrains(strainData)
        if (grow) {
          setFeedProgramId(grow.feedProgramId ?? null)
          setProgrammVorher(grow.feedProgramId ?? null)
          // Ein Programm, das keiner Karte entspricht, ist ein eigenes — es
          // gehoert beim Bearbeiten sichtbar ins Freitextfeld, nicht ins Leere.
          const kartenTreffer = (knowledge.programs ?? []).some((program) => program.name === grow.nutrients || program.key === grow.nutrients)
          if (grow.nutrients && !kartenTreffer) setCustomProgram(grow.nutrients)
        }
        setErfasstePflanzen(pflanzen.length)
        setPflanzenListe(pflanzen)
        if (grow) setForm({ ...emptyForm(), name: grow.name, tentId: grow.tentId, systemId: grow.systemId, setupId: grow.setupId, strain: grow.strain, breeder: grow.breeder, seedType: grow.seedType, startMaterial: grow.startMaterial, hydroStyle: grow.hydroStyle, plantCount: grow.plantCount, reservoirSize: grow.reservoirSize, containerSize: grow.containerSize, light: grow.light, hasChiller: grow.hasChiller, waterSource: grow.waterSource, nutrients: grow.nutrients, startDate: nurDatum(grow.startDate) ?? emptyForm().startDate, entryPoint: grow.entryPoint, daysAlreadyInPhase: grow.daysAlreadyInPhase, autoflowerDaysSinceGermination: grow.autoflowerDaysSinceGermination, flipDate: nurDatum(grow.flipDate), notes: grow.notes, status: grow.status, environment: grow.environment, germinationMethod: grow.germinationMethod, propagationMedium: grow.propagationMedium, cloneSource: grow.cloneSource, cloneIsRooted: grow.cloneIsRooted, phenoNumber: grow.phenoNumber, breederFlowerWeeksMin: grow.breederFlowerWeeksMin, breederFlowerWeeksMax: grow.breederFlowerWeeksMax, plannedVegDays: grow.plannedVegDays, strainId: grow.strainId, setpointProfileId: grow.setpointProfileId ?? null,
          /* Die Belegung kommt aus den PFLANZEN — im selben setForm wie alles
             andere, damit sie niemand ueberschreibt. */
          toepfe: pflanzen
            .filter((pflanze) => pflanze.siteIndex != null)
            .map((pflanze) => ({ topf: pflanze.siteIndex as number, strainId: pflanze.strainId ?? null }))
            .sort((a, b) => a.topf - b.topf) })
      } catch (caught) {
        if (!controller.signal.aborted) setError(formatApiError(caught, 'Grow-Wizard konnte nicht geladen werden.'))
      } finally {
        if (!controller.signal.aborted) setLoading(false)
      }
    }
    void load()
    return () => controller.abort()
  }, [growId, isEditing])

  const selectedTent = tents.find((tent) => tent.id === form.tentId) ?? null
  const exactHydro = useMemo(() => hydroSetups.filter((setup) => form.tentId ? setup.tentId === form.tentId : true), [form.tentId, hydroSetups])
  const availableHydro = exactHydro.length > 0 ? exactHydro : hydroSetups
  const selectedHydro = hydroSetups.find((setup) => setup.id === form.systemId) ?? null
  const selectedProgram = programs.find((program) => program.key === feedProgramId)
    ?? programs.find((program) => program.name === form.nutrients || program.key === form.nutrients)
    ?? null

  function patch(value: Partial<GrowUpsertPayload>) { setForm((current) => ({ ...current, ...value })) }

  /* Wieviele Toepfe belegt sind — die eine Wahrheit ueber die Pflanzenzahl,
     sobald der Nutzer sie im Abschnitt „Toepfe & Sorten" gesetzt hat. */
  const belegteToepfe = form.toepfe?.length ?? 0

  /* Die Pflanzenzahl, die gilt — an EINER Stelle, fuer Anzeige, Pruefung und
     Speichern. Vorher stand dieselbe Ableitung dreimal im Code und einmal gar
     nicht: die Pruefung rechnete mit `form.plantCount`, das auf diesem Weg
     immer null ist. Reihenfolge: einzeln erfasste Pflanzen schlagen die
     Belegung im Formular, die Belegung schlaegt das Zahlenfeld. */
  const pflanzenzahl = erfasstePflanzen > 0 ? erfasstePflanzen
    : belegteToepfe > 0 ? belegteToepfe
    : form.plantCount ?? null
  function selectTent(id: number) { setForm((current) => ({ ...current, tentId: id, systemId: hydroSetups.some((setup) => setup.id === current.systemId && setup.tentId === id) ? current.systemId : null, setupId: null })) }
  /**
   * Ein anderes Hydro-System — die Belegung bleibt, wie sie ist.
   *
   * <b>Zwei Anlaeufe, und der erste war schlimmer als das Problem.</b> Der
   * Pruefer meldete: wechselt jemand von einem 4-Topf- auf ein 2-Topf-System,
   * bleiben vier Eintraege stehen und der Kopf schreibt „4 von 2 belegt". Mein
   * erster Fix schnitt die ueberzaehligen Toepfe weg — und beim Zurueckwechseln
   * auf das grosse System standen Topf 3 und 4 auf „leer", obwohl dort Pflanzen
   * sitzen. Das Formular log ueber den Bestand, und ein Speichern haette den
   * Nutzer im Glauben gelassen, er habe zwei Toepfe frei.
   *
   * Richtig ist: nichts wegwerfen. Die Ueberzahl IST ein Widerspruch, und die
   * Pruefung rechts sagt ihn schon aus („4 Pflanzen auf 2 Sites — zu wenig
   * Plaetze") und sperrt das Speichern. Aufgeloest wird er, indem der Nutzer
   * Pflanzen entfernt — nicht, indem das Formular es heimlich fuer ihn tut.
   */
  function selectHydro(setup: HydroSetupDto) {
    patch({
      systemId: setup.id,
      setupId: null,
      hydroStyle: setup.hydroStyle,
      reservoirSize: formatLiters(setup.totalVolumeLiters ?? setup.reservoirLiters),
      containerSize: formatLiters(setup.potSizeLiters),
      hasChiller: setup.hasChiller,
    })
  }

  async function saveGrow(aenderungenBehalten?: boolean) {
    // Der Validator lief frueher pro Wizard-Schritt. Auf einer Seite gilt er
    // einmal fuer alles — und meldet den ersten Einwand, statt zu einem Schritt
    // zu springen, den es nicht mehr gibt.
    for (let current = 1; current <= 5; current += 1) {
      const message = validateStep(current, form, selectedHydro)
      if (message) { setError(message); return }
    }
    setSaving(true)
    setError(null)
    try {
      // Fork AI (Grow-Plan): Programmwechsel am laufenden Grow. Erst den Plan
      // umstellen (der setzt auch das Programm am Grow), dann das Formular
      // speichern — dann ist das Programm dort schon gleich.
      const neuesProgramm = selectedProgram?.key ?? null
      if (isEditing && growId && neuesProgramm && wechselNoetig(programmVorher, neuesProgramm, true)) {
        const plan = await apiFetch<{ eigeneAenderungen: number }>(`/api/grows/${growId}/plan`).catch(() => null)
        if (plan) {
          if (aenderungenBehalten === undefined && plan.eigeneAenderungen > 0) {
            setWechsel({ anzahl: plan.eigeneAenderungen, name: selectedProgram?.name ?? neuesProgramm })
            setSaving(false)
            return
          }
          await apiFetch(`/api/grows/${growId}/plan/programm`, {
            method: 'POST',
            body: JSON.stringify({ programmId: neuesProgramm, aenderungenBehalten: aenderungenBehalten ?? false }),
          })
          setProgrammVorher(neuesProgramm)
        }
      }
      setWechsel(null)
      // Die Programmkarte waehlte bisher nur einen NAMEN — jetzt traegt sie auch
      // die Id ins Wissen, und erst die macht den Mischplan moeglich.
      // Faellt der Wissens-Abruf aus, haelt die gespeicherte Id das Programm —
      // null wird nur daraus, wenn der Nutzer wirklich keins gewaehlt hat.
      const payload = {
        ...form,
        nutrients: form.nutrients || customProgram || null,
        setupId: form.setupId ?? null,
        feedProgramId: selectedProgram?.key ?? feedProgramId,
        // Belegte Toepfe SIND die Pflanzen. Das Feld oben ist dann nur Anzeige;
        // hier wird die Zahl daraus gezogen, damit Formular und Bestand nicht
        // auseinanderlaufen.
        plantCount: pflanzenzahl,
      }
      const saved = await apiFetch<GrowDetail>(isEditing && growId ? `/api/grows/${growId}` : '/api/grows', { method: isEditing ? 'PUT' : 'POST', body: JSON.stringify(payload) })
      navigate(`/grows/${saved.id}`)
    } catch (caught) {
      setError(formatApiError(caught, 'Grow konnte nicht gespeichert werden.'))
    } finally {
      setSaving(false)
    }
  }

  if (loading) return <V1Page eyebrow="Pflanzen" title={isEditing ? 'Grow bearbeiten' : 'Grow starten'}><V1Skeleton rows={5} label="Lade Formular" /></V1Page>

  // Prüfung und Timeline rechnen bei jeder Eingabe mit — das ist der Grund,
  // warum die sechs Schritte zu einer Seite werden konnten.
  const planInput = {
    /* Die Zahl, die WIRKLICH gilt — nicht das Feld.
       Seit die Toepfe die Pflanzenzahl bestimmen, bleibt `form.plantCount` auf
       diesem Weg fuer immer null: das Feld ist schreibgeschuetzt, sobald ein
       Topf belegt ist. `checkPlan` bekam damit null und liess die ganze
       Sites-Pruefung aus. Nachgestellt vom Pruefer: 4 Toepfe belegt, dann auf
       ein 2-Topf-System gewechselt — „Grow starten" blieb aktiv, und heraus kam
       ein Grow mit plantCount 4 auf 2 Sites, den man danach nicht mehr
       speichern konnte. Der Kommentar bei `selectHydro` behauptete genau die
       Sperre, die hier fehlte. */
    plantCount: pflanzenzahl,
    // Pflichtfeld mit Regel statt Fehlermeldung: ohne Angabe ist heute Tag 1.
    startDate: form.startDate || new Date().toISOString().slice(0, 10),
    flipDate: form.flipDate ?? null,
    vegDays: null,
    flowerDays: null,
    tent: selectedTent,
    hydro: selectedHydro,
    otherGrows: otherGrows.filter((grow) => grow.id !== Number(growId)),
    programName: selectedProgram?.name ?? (customProgram.trim() || null),
    /* Die Bluetewochen der wirklich gewaehlten Sorten — nicht die des Grows.
       Ein Becken hat einen Erntetag; zwei Sorten mit acht und elf Wochen
       passen physikalisch, zeitlich aber nicht. Das soll der Nutzer sehen,
       BEVOR er startet. */
    bluetewochen: (form.toepfe ?? []).map((eintrag) => {
      const sorte = strains.find((s) => s.id === eintrag.strainId)
      if (!sorte) return null
      /* Die SPANNE, nicht nur das Maximum. Die erste Fassung nahm
         `flowerWeeksMax ?? Min` je Sorte — bei White Widow 8-9 und Gorilla
         Glue 9-11 stand deshalb „9 bis 11", obwohl die eine ab Woche 8 fertig
         sein kann und die andere bis 11 braucht: drei Wochen, nicht zwei. Und
         ein Paar 8-9 gegen 9-10 fiel ganz durch. Gefunden vom Pruefer. */
      return { min: sorte.flowerWeeksMin ?? sorte.flowerWeeksMax ?? null,
               max: sorte.flowerWeeksMax ?? sorte.flowerWeeksMin ?? null }
    }),
  }
  const timeline = buildTimeline(planInput)
  const findings = checkPlan(planInput)
  const allowed = canCreate(findings)

  return (
    <V1Page eyebrow="Pflanzen" title={isEditing ? 'Grow bearbeiten' : 'Grow starten'} className="grow-wizard-page" action={<Link className="v1-button is-ghost" to={isEditing && growId ? `/grows/${growId}` : '/grows'}>Zurück</Link>}>
      <div className="grow-wizard-mobile-surface" data-audit="grow-wizard">
      {error && <V1Alert message={error} tone="warn" />}
      {/* Eine Seite statt sechs Schritte: links eintragen, rechts sofort sehen.
          Ob die Pflanzenzahl zu den Sites passt oder das Zelt am Starttag belegt
          ist, merkte man vorher erst am Ende — oder gar nicht. */}
      <div className="grow-wizard-shell">
        <div className="grow-wizard-main">
          <RunStep form={form} patch={patch} strains={strains} erfasstePflanzen={erfasstePflanzen} belegteToepfe={belegteToepfe} pflanzenzahl={pflanzenzahl} />
          <TentStep tents={tents} selectedId={form.tentId} onSelect={selectTent} />
          <HydroStep setups={availableHydro} exactCount={exactHydro.length} selectedId={form.systemId ?? null} onSelect={selectHydro} tent={selectedTent} />
          <ToepfeStep
            form={form}
            patch={patch}
            strains={strains}
            hydro={selectedHydro}
            pflanzen={pflanzenListe}
            onPflanzeEntfernt={(id) => {
              setPflanzenListe((v) => v.filter((pf) => pf.id !== id))
              setErfasstePflanzen((v) => Math.max(0, v - 1))
            }}
            onFehler={setError}
          />
          <TimeStep form={form} patch={patch} />
          <ProgramStep programs={programs} selected={form.nutrients ?? ''} custom={customProgram} setCustom={setCustomProgram} selectProgram={setFeedProgramId} patch={patch} />
        </div>

        <aside className="grow-wizard-context">
          <GrowPlanPanel
            timeline={timeline}
            findings={findings}
            summary={<Summary form={form} tent={selectedTent} hydro={selectedHydro} program={selectedProgram} custom={customProgram} />}
          />
        </aside>
      </div>

      <div className="v1-form-actions sticky-actions" data-audit="grow-wizard-actions">
        <V1Button variant="ghost" onClick={() => navigate(isEditing && growId ? `/grows/${growId}` : '/grows')}>Abbrechen</V1Button>
        <V1Button
          variant="primary"
          disabled={saving || !allowed}
          onClick={() => void saveGrow()}
        >
          {saving ? 'Speichert...' : isEditing ? 'Speichern' : 'Grow starten'}
        </V1Button>
      </div>
      </div>

      {/* Fork AI (Grow-Plan): Programmwechsel mit eigenen Änderungen — fragen, nicht raten. */}
      <V1Sheet
        open={wechsel !== null}
        onClose={() => !saving && setWechsel(null)}
        title="Programm wechseln"
        subtitle={wechsel ? `auf ${wechsel.name}` : undefined}
        footer={(
          <div className="wechsel-knoepfe">
            <V1Button variant="ghost" onClick={() => setWechsel(null)} disabled={saving}>Abbrechen</V1Button>
            <V1Button variant="secondary" onClick={() => void saveGrow(false)} disabled={saving} audit="programmwechsel-verwerfen">Änderungen verwerfen</V1Button>
            <V1Button variant="primary" onClick={() => void saveGrow(true)} disabled={saving} audit="programmwechsel-behalten">Änderungen übernehmen</V1Button>
          </div>
        )}
      >
        <div className="wechsel-text">
          <p>
            Im Plan dieses Grows stecken {wechsel?.anzahl === 1 ? 'eine eigene Änderung' : `${wechsel?.anzahl} eigene Änderungen`}.
            Was soll damit passieren?
          </p>
          <p><b>Übernehmen:</b> geänderte Werte und Dosierungen gehen in die gleichnamigen Wochen des neuen Programms. Wochen, die es dort nicht gibt, entfallen.</p>
          <p><b>Verwerfen:</b> der Plan wird frisch aus dem neuen Programm aufgebaut.</p>
          <p>Der Startstand bleibt für die Auswertung erhalten, und der Wechsel steht im Änderungsbuch.</p>
        </div>
      </V1Sheet>
    </V1Page>
  )
}

/**
 * Die Töpfe des Systems — und die Sorte, die in jedem steht.
 *
 * <b>Der Anlass (31.08.2026).</b> Der Tester hat definiert, was ein Grow ist:
 * „ein Durchgang in einem RDWC/DWC, der N Pflanzen mit N verschiedenen
 * Sorten/Phenos beinhalten kann. In dem Grow sollten die ganzen Sorten im
 * RDWC-System stehen wie bei den Töpfen."
 *
 * <b>Was vorher war.</b> Ein einziges Sortenfeld, dazu ein Hinweis: „Mehrere
 * Sorten im Zelt? Leg den Grow an und trag danach unter ‚Pflanzen &amp;
 * Sorten' jede Pflanze mit ihrer eigenen Sorte und ihrem Topf ein." Das
 * Datenmodell konnte es längst — nur das Formular schickte den Nutzer weg. Ein
 * Weg, der aus zwei Schritten besteht, weil einer davon fehlt, ist kein Weg.
 *
 * <b>Warum hier und nicht oben.</b> Die Töpfe kommen aus dem Hydro-System.
 * Vor dessen Auswahl gibt es nichts zu belegen — deshalb steht der Abschnitt
 * darunter und nicht im Kopf des Formulars.
 */
function ToepfeStep({ form, patch, strains, hydro, pflanzen, onPflanzeEntfernt, onFehler }: {
  form: GrowUpsertPayload
  patch: (value: Partial<GrowUpsertPayload>) => void
  strains: StrainDto[]
  hydro: HydroSetupDto | null
  pflanzen: PlantInstanceDto[]
  onPflanzeEntfernt: (id: number) => void
  onFehler: (text: string) => void
}) {
  const sorten = [...strains].sort((a, b) => a.name.localeCompare(b.name, 'de'))
  const topfzahl = hydro?.potCount ?? 0
  const belegung = form.toepfe ?? []
  /* Toepfe, die es im GEWAEHLTEN System nicht gibt — nach einem Systemwechsel.
     Sie werden nicht weggeworfen (das hat der erste Anlauf getan, und beim
     Zurueckwechseln standen belegte Toepfe ploetzlich leer da). Stattdessen
     stehen sie hier als das, was sie sind: ein Widerspruch, den der Nutzer
     aufloest. Die Pruefung rechts sperrt das Speichern ohnehin. */
  const ausserhalb = belegung.filter((eintrag) => eintrag.topf > topfzahl)

  function sorteFuer(topf: number): number | null {
    return belegung.find((eintrag) => eintrag.topf === topf)?.strainId ?? null
  }

  /** Die wirklich erfasste Pflanze in einem Topf — null, wenn er nur geplant ist. */
  function pflanzeIn(topf: number): PlantInstanceDto | null {
    return pflanzen.find((pf) => pf.siteIndex === topf) ?? null
  }

  /**
   * Setzt einen Topf.
   *
   * <b>„Leer" wirkt sofort, nicht beim Speichern.</b> Das Formular speichert
   * alles auf einmal; würde „leer" erst dort greifen, genügte ein Fehlgriff im
   * Auswahlfeld plus Speichern, und eine Pflanze samt Pheno-Bewertung wäre weg
   * — ohne Rückfrage, womöglich mitten in der Blüte. Genau dieser Datenverlust
   * war der Grund, das Entfernen überhaupt auszulagern. Die Rückfrage hier
   * holt ihn zurück an die Stelle, an der der Nutzer sie erwartet.
   *
   * Ein Topf, der nur geplant und noch nicht erfasst ist, verschwindet ohne
   * Rückfrage aus der Liste — dort gibt es nichts zu verlieren.
   */
  async function setzeTopf(topf: number, wert: string) {
    const ohne = belegung.filter((eintrag) => eintrag.topf !== topf)

    if (wert !== '') {
      patch({ toepfe: [...ohne, { topf, strainId: Number(wert) }].sort((a, b) => a.topf - b.topf) })
      return
    }

    const vorhanden = pflanzeIn(topf)
    if (vorhanden) {
      const jaWirklich = window.confirm(
        `Topf ${topf} leeren? „${vorhanden.label}" wird entfernt, eine Pheno-Bewertung dieser Pflanze geht mit.`)
      if (!jaWirklich) return
      try {
        await apiFetch(`/api/plants/${vorhanden.id}`, { method: 'DELETE' })
        onPflanzeEntfernt(vorhanden.id)
      } catch (caught) {
        onFehler(formatApiError(caught, `Topf ${topf} konnte nicht geleert werden.`))
        return
      }
    }

    patch({ toepfe: ohne.sort((a, b) => a.topf - b.topf) })
  }

  /**
   * Alle Töpfe auf dieselbe Sorte — der häufigste Fall in einem Griff.
   *
   * forkai.105: „leer" fehlt hier bewusst. Sechs Pflanzen auf einen Schlag zu
   * entfernen ist keine Bequemlichkeit mehr, sondern ein Unfall mit einem
   * Klick. Wer leeren will, tut es Topf für Topf, jeder mit eigener Rückfrage.
   */
  function alleAuf(wert: string) {
    if (wert === '') return
    const strainId = Number(wert)
    patch({ toepfe: Array.from({ length: topfzahl }, (_, i) => ({ topf: i + 1, strainId })) })
  }

  if (topfzahl <= 0) {
    return (
      <V1Section title="Töpfe & Sorten">
        <p className="gw-toepfe-hinweis">
          Wähle oben ein Hydro-System — dann stehen hier seine Töpfe, und du kannst
          jedem seine Sorte geben.
        </p>
      </V1Section>
    )
  }

  return (
    <V1Section title="Töpfe & Sorten">
      <div className="gw-toepfe" data-audit="grow-toepfe">
        <div className="gw-toepfe-kopf">
          {/* Die Zahl steht hier, nicht im Feld „Pflanzen" darueber: belegte
              Toepfe SIND die Pflanzen. Zwei Stellen fuer dieselbe Zahl laufen
              auseinander — das ist in diesem Projekt dreimal passiert. */}
          <span className="gw-toepfe-zahl">
            {belegung.length - ausserhalb.length} von {topfzahl} {topfzahl === 1 ? 'Topf' : 'Töpfen'} belegt
          </span>
          {sorten.length > 0 && (
            <label className="gw-toepfe-alle">
              <span>alle auf</span>
              <select value="" onChange={(event) => alleAuf(event.target.value)} aria-label="Alle Töpfe auf eine Sorte setzen">
                <option value="">— wählen —</option>
                {sorten.map((sorte) => <option key={sorte.id} value={sorte.id}>{sorte.name}</option>)}
              </select>
            </label>
          )}
        </div>

        <ul className="gw-toepfe-liste">
          {Array.from({ length: topfzahl }, (_, i) => i + 1).map((topf) => (
            <li key={topf} className={sorteFuer(topf) == null ? 'gw-topf is-leer' : 'gw-topf'}>
              <span className="gw-topf-nr">Topf {topf}</span>
              <select
                value={sorteFuer(topf) ?? ''}
                onChange={(event) => void setzeTopf(topf, event.target.value)}
                aria-label={`Sorte in Topf ${topf}`}
              >
                <option value="">— leer —</option>
                {sorten.map((sorte) => <option key={sorte.id} value={sorte.id}>{sorte.name}</option>)}
              </select>
            </li>
          ))}
        </ul>

        {ausserhalb.length > 0 && (
          <p className="gw-toepfe-hinweis is-warn">
            {ausserhalb.length === 1
              ? `Eine Pflanze steht auf Topf ${ausserhalb[0].topf} — den gibt es in `
              : `${ausserhalb.length} Pflanzen stehen auf Töpfen ${ausserhalb.map((e) => e.topf).join(', ')} — die gibt es in `}
            {hydro?.name ?? 'diesem System'} nicht. Entferne sie unter „Pflanzen &amp; Sorten"
            am Grow, oder wähle wieder ein System mit genug Töpfen.
          </p>
        )}

        {sorten.length === 0 && (
          <p className="gw-toepfe-hinweis">
            Noch keine Sorte in der Bibliothek — leg sie unter „Sorten &amp; Pheno" an,
            dann kannst du sie hier den Töpfen zuordnen.
          </p>
        )}
      </div>
    </V1Section>
  )
}

/**
 * Der Kopf des Grows: Name, Sorte, Pflanzenzahl.
 *
 * Die Sorte kommt aus der Bibliothek, statt frei getippt zu werden. Vorher
 * stand hier nur ein Textfeld — die Sorten-Tabelle musste ihre Laeufe deshalb
 * ueber Namensgleichheit suchen, und ein Tippfehler liess einen Lauf aus der
 * Statistik verschwinden. Wer eine Sorte waehlt, uebernimmt automatisch
 * Zuechter und Bluetewochen; „frei eintragen" bleibt fuer alles, was (noch)
 * nicht in der Bibliothek steht.
 */
function RunStep({ form, patch, strains, erfasstePflanzen, belegteToepfe, pflanzenzahl }: { form: GrowUpsertPayload; patch: (value: Partial<GrowUpsertPayload>) => void; strains: StrainDto[]; erfasstePflanzen: number; belegteToepfe: number; pflanzenzahl: number | null }) {
  const sorten = [...strains].sort((a, b) => a.name.localeCompare(b.name, 'de'))
  // Was der letzte Bibliotheks-Klick bei den Bluetewochen eingetragen hat.
  // Beim Wechsel auf eine andere Sorte wird nur genau das ersetzt \u2014 sonst
  // stuenden unter Sorte B noch die Wochen von Sorte A, als waeren sie eigene.
  const autofill = useRef<{ min: number | null; max: number | null } | null>(null)

  function waehleSorte(wert: string) {
    if (wert === '') {
      // Freie Eingabe: die Verknuepfung faellt weg, der Text bleibt stehen.
      patch({ strainId: null })
      return
    }
    const sorte = sorten.find((item) => String(item.id) === wert)
    if (!sorte) return
    // Die Bluetewochen treiben den Zeitstrahl \u2014 aus der Bibliothek sind sie
    // verlaesslicher als aus dem Gedaechtnis. Eigene Angaben bleiben stehen;
    // nur leere Felder und der Autofill der vorigen Sorte werden gefuellt.
    const min = form.breederFlowerWeeksMin == null || form.breederFlowerWeeksMin === autofill.current?.min
      ? sorte.flowerWeeksMin
      : form.breederFlowerWeeksMin
    const max = form.breederFlowerWeeksMax == null || form.breederFlowerWeeksMax === autofill.current?.max
      ? sorte.flowerWeeksMax
      : form.breederFlowerWeeksMax
    autofill.current = { min: sorte.flowerWeeksMin ?? null, max: sorte.flowerWeeksMax ?? null }
    patch({
      strainId: sorte.id,
      strain: sorte.name,
      breeder: sorte.breeder ?? form.breeder,
      breederFlowerWeeksMin: min,
      breederFlowerWeeksMax: max,
    })
  }

  return (
    <V1Section title="Run">
      <div className="v1-form-grid grow-form-grid">
        <V1Field label="Grow-Name" wide>
          <input value={form.name} onChange={(event) => patch({ name: event.target.value })} placeholder="Purple Lemonade RDWC" />
        </V1Field>

        {/* „Hauptsorte", nicht mehr „Sorte": seit dem 31.08.2026 steht die
            Sorte je Topf im Abschnitt „Töpfe & Sorten" weiter unten. Diese
            hier trägt die Blütewochen des Laufs und die Statistik — und ist
            der Rückfall für Töpfe ohne eigene Angabe. Der alte Hinweis
            schickte den Nutzer auf eine ANDERE Seite; genau das war die
            Beschwerde des Testers. */}
        <V1Field label="Hauptsorte" hint={sorten.length === 0 ? 'Noch keine Sorte in der Bibliothek — unter „Sorten & Pheno" anlegen.' : 'Trägt Blütewochen und Statistik des Laufs. Mehrere Sorten? Die stehen unten je Topf.'}>
          <select value={form.strainId != null ? String(form.strainId) : ''} onChange={(event) => waehleSorte(event.target.value)}>
            <option value="">— frei eintragen —</option>
            {sorten.map((sorte) => (
              <option key={sorte.id} value={sorte.id}>{sorte.name}{sorte.breeder ? ` \u00b7 ${sorte.breeder}` : ''}</option>
            ))}
          </select>
        </V1Field>

        {form.strainId == null && (
          <V1Field label="Sorte (frei)">
            <input value={form.strain ?? ''} onChange={(event) => patch({ strain: event.target.value })} placeholder="z. B. Purple Lemonade" />
          </V1Field>
        )}

        <V1Field label="Züchter">
          <input value={form.breeder ?? ''} onChange={(event) => patch({ breeder: event.target.value })} />
        </V1Field>

        {/* Sind Pflanzen EINZELN erfasst, sind sie die Wahrheit ueber die Zahl
            — der Server zieht plantCount seit dem 25.08.2026 nach. Ein
            beschreibbares Feld daneben behauptete etwas anderes: eingetragen,
            gespeichert, danach stand wieder die alte Zahl da. Gefunden von
            e2e/formularfelder-kommen-an.spec.ts. */}
        {/* Eine Wahrheit ueber die Zahl.
            Belegte Toepfe SIND die Pflanzen — steht unten eine Belegung, ist
            das Feld hier nur noch Anzeige. Zwei beschreibbare Stellen fuer
            dieselbe Zahl laufen auseinander; genau das war der Fehler, den
            e2e/formularfelder-kommen-an.spec.ts am 25.08.2026 gefunden hat. */}
        <V1Field
          label="Pflanzen"
          hint={erfasstePflanzen > 0
            ? `${erfasstePflanzen} einzeln erfasst — die Zahl folgt der Karte „Pflanzen & Sorten" am Grow.`
            : belegteToepfe > 0
              ? 'Folgt den belegten Töpfen unten.'
              : undefined}
        >
          <input
            type="number"
            min="1"
            value={pflanzenzahl ?? ''}
            readOnly={erfasstePflanzen > 0 || belegteToepfe > 0}
            onChange={(event) => patch({ plantCount: toNullableInt(event.target.value) })}
          />
        </V1Field>

        <V1Field label="Samen-Typ">
          <select value={form.seedType} onChange={(event) => patch({ seedType: event.target.value as SeedType })}>
            {seedTypes.map((value) => <option key={value} value={value}>{samenName(value)}</option>)}
          </select>
        </V1Field>

        <V1Field label="Startmaterial">
          <select value={form.startMaterial} onChange={(event) => patch({ startMaterial: event.target.value as StartMaterial, entryPoint: (event.target.value === 'Clone' ? 'Veg' : form.entryPoint) as GrowEntryPoint })}>
            {startMaterials.map((value) => <option key={value} value={value}>{materialName(value)}</option>)}
          </select>
        </V1Field>
      </div>
    </V1Section>
  )
}


function TentStep({ tents, selectedId, onSelect }: { tents: TentDto[]; selectedId: number | null; onSelect: (id: number) => void }) {
  if (tents.length === 0) return <V1Empty title="Kein Zelt angelegt" action={<V1LinkButton to="/zelte/new" variant="primary">Zelt anlegen</V1LinkButton>} />
  return <V1Section title="Zelt"><div className="grow-select-grid">{tents.map((tent) => <button type="button" key={tent.id} className={classNames('grow-select-card', selectedId === tent.id && 'active')} onClick={() => onSelect(tent.id)}><span className="grow-card-topline"><strong>{tent.name}</strong><V1Badge tone={tent.status === 'Active' ? 'ok' : 'neutral'}>{tent.status === 'Active' ? 'aktiv' : 'archiviert'}</V1Badge></span><span className="grow-card-meta">{zeltZweckName(tent.tentType)} · {formatTentSize(tent)}</span><span className="grow-card-facts"><b>{tent.activeGrowCount} {tent.activeGrowCount === 1 ? 'Grow' : 'Grows'}</b><b>{tent.activeSetupCount} {tent.activeSetupCount === 1 ? 'Setup' : 'Setups'}</b></span></button>)}</div></V1Section>
}

function HydroStep({ setups, exactCount, selectedId, onSelect, tent }: { setups: HydroSetupDto[]; exactCount: number; selectedId: number | null; onSelect: (setup: HydroSetupDto) => void; tent: TentDto | null }) {
  if (setups.length === 0) return <V1Empty title="Kein Hydro-Setup vorhanden" text="Lege zuerst ein DWC/RDWC-System an." action={<V1LinkButton to="/hydro/new" variant="primary">Hydro anlegen</V1LinkButton>} />
  return <V1Section title="Hydro">{tent && exactCount === 0 && <V1Alert title="Kein Setup direkt am Zelt" message="Es gibt aktive Hydro-Setups, aber keines ist diesem Zelt zugeordnet. Du kannst eines wählen oder zuerst die Zeltzuordnung im Hydro-Setup korrigieren." tone="warn" />}<div className="grow-select-grid">{setups.map((setup) => <button type="button" key={setup.id} className={classNames('grow-select-card', selectedId === setup.id && 'active')} onClick={() => onSelect(setup)}><span className="grow-card-topline"><strong>{setup.name}</strong><V1Badge tone="accent">{setup.hydroStyle}</V1Badge></span><span className="grow-card-meta">{setup.tentName ?? 'ohne Zelt'} · {aufstellungName(setup.layoutType)}</span><span className="grow-card-facts"><b>{setup.potCount ?? 1} Sites</b><b>{formatLiters(setup.totalVolumeLiters)}</b><b>{setup.hasChiller ? 'Chiller' : 'ohne Chiller'}</b></span></button>)}</div></V1Section>
}

function TimeStep({ form, patch }: { form: GrowUpsertPayload; patch: (value: Partial<GrowUpsertPayload>) => void }) {
  return <V1Section title="Zeit"><div className="v1-form-grid grow-form-grid"><V1Field label="Startdatum *" hint="Tag 1 des Grows — daran haengen Phasen, Tageszaehlung und Zeitstrahl. Leer heisst heute.">
    <input type="date" required value={form.startDate} onChange={(event) => patch({ startDate: event.target.value })} />
  </V1Field><V1Field label="Startpunkt"><select value={form.entryPoint} onChange={(event) => patch({ entryPoint: event.target.value as GrowEntryPoint })}>{entryPoints.map((value) => <option key={value} value={value}>{einstiegName(value)}</option>)}</select></V1Field>{/* Nur anbieten, wo es auch ankommt: der Server nimmt „Tage in Phase" nur
    ausserhalb der Keimung und nur fuer Nicht-Autoflower an
    (GrowFormViewModel.NeedsDaysInPhase). Vorher stand das Feld immer da
    und wurde still verworfen — dieselbe Bauart wie beim Flipdatum, gefunden
    von e2e/formularfelder-kommen-an.spec.ts. */}
{form.entryPoint !== 'Germination' && form.seedType !== 'Autoflower' && (
  <V1Field label="Tage in Phase"><input type="number" min="0" value={form.daysAlreadyInPhase ?? ''} onChange={(event) => patch({ daysAlreadyInPhase: toNullableInt(event.target.value) })} /></V1Field>
)}
{/* Eine Autoflower hat keine Phasen-Tage, sondern ein Alter seit der
    Keimung — der Server kennt das Feld laengst, das Formular bot es nie an.
    Ohne diese Zeile konnte ein Autoflower-Grower sein Alter nirgends
    eintragen. */}
{form.seedType === 'Autoflower' && (
  <V1Field label="Tage seit Keimung" hint="Bei Autoflowern zählt das Alter, nicht die Phase.">
    <input type="number" min="0" value={form.autoflowerDaysSinceGermination ?? ''} onChange={(event) => patch({ autoflowerDaysSinceGermination: toNullableInt(event.target.value) })} />
  </V1Field>
)}{form.seedType !== 'Autoflower' && (
    <V1Field label="Veg-Dauer geplant (Tage)" hint={vegHinweis(form)}>
      <input
        type="number" min="1" max="365"
        value={form.plannedVegDays ?? ''}
        placeholder="z. B. 28"
        onChange={(event) => patch({ plannedVegDays: toNullableInt(event.target.value) })}
      />
    </V1Field>
  )}{form.seedType !== 'Autoflower' && <V1Field label="Flipdatum" hint="Erst ausfüllen, wenn wirklich geflippt wurde."><input type="date" value={form.flipDate ?? ''} onChange={(event) => patch({ flipDate: event.target.value })} /></V1Field>}<V1Field label="Status"><select value={form.status} onChange={(event) => patch({ status: event.target.value as GrowStatus })}>{statuses.map((value) => <option key={value} value={value}>{statusName(value)}</option>)}</select></V1Field></div></V1Section>
}

/**
 * Der Termin, der sich aus der geplanten Veg-Dauer ergibt.
 *
 * Ohne ihn muesste man im Kopf rechnen -- und genau diese Rechnung macht der
 * Zeitstrahl spaeter auch. Sie hier zu zeigen ist die Probe darauf.
 */
function vegHinweis(form: GrowUpsertPayload): string {
  if (form.plannedVegDays == null || form.plannedVegDays <= 0) {
    return 'Leer lassen, wenn du nach Augenmass flippst \u2014 dann zeigt der Zeitstrahl keinen Termin.'
  }
  const start = new Date(form.startDate)
  if (Number.isNaN(start.getTime())) return 'Flip nach dieser Dauer ab Bewurzelung.'
  const flip = new Date(start.getTime() + form.plannedVegDays * 86_400_000)
  const datum = new Intl.DateTimeFormat('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric' }).format(flip)
  return `Flip am ${datum}, wenn ab Start gerechnet wird \u2014 mit Bewurzelungsdatum entsprechend spaeter.`
}

function ProgramStep({ programs, selected, custom, setCustom, selectProgram, patch }: { programs: NutrientProgramDto[]; selected: string; custom: string; setCustom: (value: string) => void; selectProgram: (key: string | null) => void; patch: (value: Partial<GrowUpsertPayload>) => void }) {
  // Fork AI (Grow-Plan): Karten sagen, wie viel das Programm mitbringt;
  // eigene Programme stehen für sich.
  const karte = (program: NutrientProgramDto) => {
    const d = deckung(program)
    return (
      <button key={program.key} type="button" className={classNames('program-card', (selected === program.name || selected === program.key) && 'active')} onClick={() => { setCustom(''); selectProgram(program.key); patch({ nutrients: program.name }) }}>
        <span className="grow-card-topline"><strong>{program.name}</strong><V1Badge tone="accent">{program.manufacturer}</V1Badge></span>
        <span className="program-summary">{program.summary}</span>
        <span className={classNames('program-deckung', `ist-${d.stufe}`)} data-audit="programm-deckung">{d.text}</span>
      </button>
    )
  }
  const mitgeliefert = programs.filter((p) => !istEigenesProgramm(p.key))
  const eigene = programs.filter((p) => istEigenesProgramm(p.key))
  return (
    <V1Section title="Programm">
      <div className="program-grid">{mitgeliefert.map(karte)}</div>
      <h3 className="program-gruppe">Eigene Programme</h3>
      {eigene.length > 0
        ? <div className="program-grid">{eigene.map(karte)}</div>
        : <p className="program-hinweis">Noch keine. Sie entstehen, wenn du im Plan „Auch ins Programm“ wählst.</p>}
      <p className="program-hinweis">
        Der Grow bekommt eine eigene Kopie des Programms als Plan — spätere Änderungen am Programm ändern diesen Grow nicht.
      </p>
      <div className="grow-custom-program"><V1Field label="Anderes Programm (nur Name, ohne Werte)"><input value={custom} onChange={(event) => { setCustom(event.target.value); selectProgram(null); patch({ nutrients: event.target.value || null }) }} placeholder="Eigene Mischung" /></V1Field></div>
    </V1Section>
  )
}

function Summary({ form, tent, hydro, program, custom }: { form: GrowUpsertPayload; tent: TentDto | null; hydro: HydroSetupDto | null; program: NutrientProgramDto | null; custom: string }) {
  return <V1Card className="grow-summary-card"><span className="v1-card-kicker">Grow-Basis</span><h2>{form.name || 'Neuer Grow'}</h2><div className="grow-summary-list"><span><b>Zelt</b>{tent?.name ?? 'offen'}</span><span><b>Hydro</b>{hydro?.name ?? 'offen'}</span><span><b>Programm</b>{program?.name || custom || form.nutrients || 'offen'}</span></div></V1Card>
}

function formatTentSize(tent: TentDto) { return !tent.widthCm && !tent.depthCm && !tent.tentHeightCm ? 'Größe offen' : `${tent.widthCm ?? '–'}×${tent.depthCm ?? '–'}×${tent.tentHeightCm ?? '–'} cm` }
function validateStep(step: number, form: GrowUpsertPayload, hydro: HydroSetupDto | null) { if (step === 1 && !form.name.trim()) return 'Bitte Grow-Namen eingeben.'; if (step === 2 && !form.tentId) return 'Bitte Zelt wählen.'; if (step === 3 && !hydro) return 'Bitte Hydro-Setup wählen.'; return null }

export default GrowSetupPage
