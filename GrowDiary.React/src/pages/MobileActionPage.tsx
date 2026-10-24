import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch } from '../api'
import { wegZurRoutine } from '../features/changeouts/routine-weg'
import type { CuringJar } from '../features/curing/curing-typen'
import { faelligText } from '../features/curing/curing-typen'
import type { CalibrationEventDto, GrowSummary, GrowTaskDto, HardwareItemDto, MaintenanceEventDto, RiskEventDto, SopInstanceDto } from '../types'
import { V1Alert, V1Page, V1Skeleton } from '../components/v1'
import { classNames } from '../utils'
import { RiskActionCard } from '../features/risks/RiskActionCard'
import { KlappBereich } from '../components/Klapp'
import { istOffen, istWiederOkay } from '../features/risks/risiko-zeit'
import { TASKS_CHANGED_EVENT } from '../useNavCounts'

type FaelligeRoutine = { sopId: string; name: string; severity: string; tageSeit: number; intervallTage: number; meldung: string }
type PumpBefund = { schluessel: string; name: string; stufe: string; meldung: string; herkunft: string }
type PumpZeltLage = { tentId: number; tentName: string; befunde: PumpBefund[] }
type WartungsPunkt = { bereich: string; titel: string; stufe: string; meldung: string; herkunft: string }
type ActionState = { grows: GrowSummary[]; risks: RiskEventDto[]; tasks: GrowTaskDto[]; maintenance: MaintenanceEventDto[]; calibration: CalibrationEventDto[]; sops: SopInstanceDto[]; hardware: HardwareItemDto[]; dueByGrow: Array<{ grow: GrowSummary; items: FaelligeRoutine[] }>; pumpen: PumpZeltLage[]; wartung: WartungsPunkt[]; glaeser: CuringJar[]; issues: string[]; geladenUm: number }
const initial: ActionState = { grows: [], risks: [], tasks: [], maintenance: [], calibration: [], sops: [], hardware: [], dueByGrow: [], pumpen: [], wartung: [], glaeser: [], issues: [], geladenUm: 0 }
const riskRank: Record<string, number> = { Critical: 0, Warning: 1, Info: 2 }

function MobileActionPage() {
  const [state, setState] = useState<ActionState>(initial)
  const [loading, setLoading] = useState(true)
  const [refresh, setRefresh] = useState(0)
  const [notice, setNotice] = useState<string | null>(null)
  const [erledigt, setErledigt] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    async function load() {
      setLoading(true)
      const issues: string[] = []
      const dueBeforeUtc = new Date(Date.now() + 3 * 24 * 60 * 60 * 1000).toISOString()
      const safe = async <T,>(label: string, path: string, fallback: T): Promise<T> => {
        try { return await apiFetch<T>(path, { signal: controller.signal }) } catch { if (!controller.signal.aborted) issues.push(label); return fallback }
      }
      const [grows, risks, maintenance, calibration, hardware] = await Promise.all([
        safe<GrowSummary[]>('Grows', '/api/grows?archived=false', []),
        safe<RiskEventDto[]>('Risiken', '/api/risk-events', []),
        safe<MaintenanceEventDto[]>('Wartung', `/api/maintenance-events?dueBeforeUtc=${encodeURIComponent(dueBeforeUtc)}`, []),
        safe<CalibrationEventDto[]>('Kalibrierung', `/api/calibration-events?dueBeforeUtc=${encodeURIComponent(dueBeforeUtc)}`, []),
        safe<HardwareItemDto[]>('Hardware', '/api/hardware-items', []),
      ])
      // Die stehende Pumpe zuerst: das ist die einzige Meldung hier, bei der
      // Stunden ueber den ganzen Lauf entscheiden.
      const pumpen = await safe<PumpZeltLage[]>('Pumpen', '/api/pump-watch', [])
      // Verschleiss, Pruefung, Sicherung — die Termine, die aus den Angaben am
      // Geraet selbst folgen und die frueher niemand las.
      const wartung = await safe<WartungsPunkt[]>('Wartung fällig', '/api/maintenance-due', [])
      // Die Glaeser im Schrank. Ohne Grow-Filter, und das ist der Punkt: nach
      // der Ernte gilt ein Grow als beendet, das Aushaerten faengt aber genau
      // dann erst an. Wer hier nach laufenden Grows filtert, sieht nie ein Glas.
      const glaeser = await safe<CuringJar[]>('Aushaerten', '/api/curing/jars', [])
      const activeGrows = grows.filter((grow) => grow.status === 'Running' || grow.status === 'Planning')
      const taskLists = await Promise.all(activeGrows.map((grow) => safe<GrowTaskDto[]>(`Tasks ${grow.id}`, `/api/grows/${grow.id}/tasks`, [])))
      const sopLists = await Promise.all(activeGrows.map((grow) => safe<SopInstanceDto[]>(`SOP ${grow.id}`, `/api/sop-instances?growId=${grow.id}`, [])))
      // Die ueberfaelligen Routinen aus den Zeitplaenen des Wissens — der Teil,
      // der frueher nur in der Mappe stand und den niemand las.
      const dueLists = await Promise.all(activeGrows.map((grow) => safe<FaelligeRoutine[]>(`Fällig ${grow.id}`, `/api/grows/${grow.id}/due-sops`, [])))
      const dueByGrow = activeGrows
        .map((grow, index) => ({ grow, items: dueLists[index] ?? [] }))
        .filter((entry) => entry.items.length > 0)
      if (controller.signal.aborted) return
      setState({ grows, risks, maintenance: maintenance.filter((item) => item.status === 'Planned'), calibration: calibration.filter((item) => item.status === 'Planned'), tasks: taskLists.flat().filter((task) => task.status === 'Open'), sops: sopLists.flat().filter((sop) => sop.status === 'Active'), hardware, dueByGrow, pumpen, wartung, glaeser, issues, geladenUm: Date.now() })
      setLoading(false)
    }
    void load()
    return () => controller.abort()
  }, [refresh])

  // `state.risks` enthaelt ALLE Risiken: die offenen werden Karten, die vor
  // kurzem vom Abgleich geschlossenen zeigen „wieder okay", und alle zusammen
  // liefern die Zahl der Wiederholungen. Eine Uhr fuer alle Karten.
  const jetzt = state.geladenUm
  const risks = useMemo(() => state.risks.filter(istOffen).sort((a, b) => (riskRank[a.severity] ?? 9) - (riskRank[b.severity] ?? 9)), [state.risks])
  const wiederOkay = useMemo(
    () => state.risks.filter((risk) => istWiederOkay(risk, jetzt)).sort((a, b) => (b.resolvedAtUtc ?? '').localeCompare(a.resolvedAtUtc ?? '')),
    [state.risks, jetzt],
  )
  const rissKontext = (risk: RiskEventDto) =>
    risk.growId
      // Der Grow-Name sagt nur etwas, wenn es mehr als einen gibt.
      ? (state.grows.length > 1 ? getGrowName(state.grows, risk.growId) : undefined)
      : risk.hardwareItemId ? getHardwareName(state.hardware, risk.hardwareItemId) : risk.tentId ? `Zelt #${risk.tentId}` : 'System'
  /**
   * Eine Aufgabe abhaken.
   *
   * Das ging zwischenzeitlich nirgends mehr: die Aufgabe liess sich anlegen,
   * stand hier als Termin — und blieb bis in alle Ewigkeit offen, weil die
   * einzige Stelle mit dem Erledigt-Knopf beim Umbau des Journals wegfiel.
   */
  async function taskErledigen(taskId: number, titel: string) {
    setErledigt(`task-${taskId}`)
    try {
      await apiFetch(`/api/tasks/${taskId}/status`, { method: 'PATCH', body: JSON.stringify({ status: 'Done' }) })
      setNotice(`„${titel}“ erledigt.`)
      window.dispatchEvent(new Event(TASKS_CHANGED_EVENT))
      setRefresh((current) => current + 1)
    } catch (caught) {
      setNotice(caught instanceof Error ? caught.message : 'Aufgabe konnte nicht abgehakt werden.')
    } finally {
      setErledigt(null)
    }
  }

  /**
   * Eine Aufgabe löschen statt abhaken — für Termine, die aus Versehen
   * entstanden sind. Der DELETE-Endpunkt war fertig gebaut (samt Audit-Log),
   * nur bot ihn die Oberfläche nirgends an.
   */
  async function taskLoeschen(taskId: number, titel: string) {
    setErledigt(`task-${taskId}`)
    try {
      await apiFetch(`/api/tasks/${taskId}`, { method: 'DELETE' })
      setNotice(`„${titel}“ gelöscht.`)
      window.dispatchEvent(new Event(TASKS_CHANGED_EVENT))
      setRefresh((current) => current + 1)
    } catch (caught) {
      setNotice(caught instanceof Error ? caught.message : 'Aufgabe konnte nicht gelöscht werden.')
    } finally {
      setErledigt(null)
    }
  }

  const planPruefen = state.grows.filter((grow) => (grow.planUngeprueft ?? []).length > 0)
  const termine = buildTermine(state)
  /**
   * Acht Termine auf einen Blick, der Rest einen Klick weit.
   *
   * Ohne Deckel wurde die Karte so hoch, dass daneben eine halbe Bildschirm-
   * hoehe Leere stand (gemessen 463 bis 524 px). Mit Deckel und ohne Hinweis
   * waren neunzehn Termine unsichtbar. Also beides: deckeln UND sagen.
   */
  const [alleTermine, setAlleTermine] = useState(false)
  const termineSichtbar = alleTermine ? termine : termine.slice(0, 8)
  const termineVersteckt = termine.length - termineSichtbar.length
  const wartung = buildWartung(state)
  const critCount = risks.filter((risk) => risk.severity === 'Critical').length
  const warnCount = risks.filter((risk) => risk.severity === 'Warning').length

  const handleRiskChanged = (message: string) => {
    setNotice(message)
    setRefresh((current) => current + 1)
  }

  return (
    <V1Page
      eyebrow="Jetzt / Aufgaben"
      title="Was jetzt zu tun ist"
      subtitle="Risiken zuerst, dann Termine. Jede Zeile hat genau eine Hauptaktion — nichts, das man erst suchen muss."
    >
      {state.issues.length > 0 && <V1Alert title="Teilweise offline" message={state.issues.join(' · ')} tone="warn" />}
      {notice && <V1Alert title="Erledigt" message={notice} tone="ok" />}

      {loading ? <V1Skeleton tiles={3} rows={4} label="Lade Aufgaben" /> : (
        <div className="af-cols" data-audit="open-action-list">
          {/* Ganz oben, vor allem anderen: eine stehende Luftpumpe kostet in
              rund zwei Tagen den ganzen Lauf. Alles darunter kann warten. */}
          {state.pumpen.some((zelt) => zelt.befunde.some((b) => b.stufe !== 'ok')) && (
            <section className="ls-panel af-col" data-audit="pump-watch-section">
              <div className="ls-panel-head">
                <span className="ls-label">Pumpen</span>
              </div>
              <div className="ls-panel-body">
                {state.pumpen.map((zelt) => (
                  <div key={zelt.tentId} className="af-due-grow">
                    <div className="co-row-title">{zelt.tentName}</div>
                    {zelt.befunde.filter((b) => b.stufe !== 'ok').map((befund) => (
                      <div key={befund.schluessel} className="co-row">
                        <span className={befund.stufe === 'kritisch' ? 'co-row-text is-due' : 'co-row-text'}>
                          {befund.meldung}
                          <em className="af-pump-source">{befund.herkunft}</em>
                        </span>
                      </div>
                    ))}
                  </div>
                ))}
              </div>
            </section>
          )}

          {/* Ueberfaellige Routinen aus den Zeitplaenen des Wissens. Vor den
              Risiken: wer den Wasserwechsel nachholt, verhindert das Risiko,
              statt es spaeter zu bestaetigen. Im Expertenmodus kommt die Liste
              leer zurueck und das Panel verschwindet von selbst. */}
          {state.dueByGrow.length > 0 && (
            <section className="ls-panel af-col" data-audit="due-sops-section">
              <div className="ls-panel-head">
                <span className="ls-label">Fällige Routinen</span>
              </div>
              {/* KEIN `ls-panel-body` darum: dessen 14 px Innenabstand kaemen
                  zu den 14 px hinzu, die `.co-row` schon selbst mitbringt —
                  Text und Trennlinien standen 14 px weiter innen als bei den
                  drei Nachbarkarten. */}
              <div className="af-due-list">
                {state.dueByGrow.map(({ grow, items }) => (
                  <div key={grow.id} className="af-due-grow">
                    <div className="co-row-title">{grow.name}</div>
                    {items.map((item) => {
                      // Der Knopf fuehrt dorthin, wo die Routine erledigt wird —
                      // nicht zur Grow-Seite, auf der sie nur noch einmal steht.
                      const weg = wegZurRoutine(item.sopId)
                      return (
                        <div key={item.sopId} className="co-row">
                          <span className={item.severity === 'critical' ? 'co-row-text is-due' : 'co-row-text'}>{item.meldung}</span>
                          <div className="co-row-end">
                            <Link className="ls-btn is-small" to={weg?.to ?? `/grows/${grow.id}`}>{weg?.aktion ?? 'Öffnen'}</Link>
                          </div>
                        </div>
                      )
                    })}
                  </div>
                ))}
              </div>
            </section>
          )}
          {/* Angehängte Blütewochen, deren Werte nur von der Vorwoche kopiert sind:
              der Plan führt weniger Wochen, als Sorte und Flush verlangen. Die Karte
              steht, bis die Woche im Plan als geprüft markiert ist — berechnet, nicht
              gespeichert, also nichts, das liegen bleibt. */}
          {planPruefen.length > 0 && (
            <section className="ls-panel af-col" data-audit="plan-pruefen-section">
              <div className="ls-panel-head">
                <span className="ls-label">Plan prüfen</span>
              </div>
              <div className="af-due-list">
                {planPruefen.map((grow) => (
                  <div key={grow.id} className="af-due-grow">
                    <div className="co-row-title">{grow.name}</div>
                    {grow.planUngeprueft.map((woche) => (
                      <div key={woche.id} className="co-row">
                        <span className="co-row-text">
                          {woche.label} prüfen — die Werte sind aus der Vorwoche übernommen. Stimmen sie für diese Woche?
                        </span>
                        <div className="co-row-end">
                          <Link className="ls-btn is-small" to={`/plan?grow=${grow.id}&woche=${encodeURIComponent(woche.id)}`}>Zum Plan</Link>
                        </div>
                      </div>
                    ))}
                  </div>
                ))}
              </div>
            </section>
          )}
          {/* Risiken zuerst — mit ihrer einen Hauptaktion. Die Karten bringen
              Bestätigen/Erledigen bereits mit. */}
          <section className="ls-panel af-col" data-audit="risk-action-section">
            <div className="ls-panel-head">
              <span className="ls-label">Risiken</span>
              {critCount > 0 && <span className="af-count is-crit">{critCount} kritisch</span>}
              {warnCount > 0 && <span className="af-count is-warn">{warnCount} Warnung</span>}
              {risks.length === 0 && <span className="ls-panel-meta">nichts offen</span>}
            </div>
            {risks.length === 0 ? (
              <div className="ls-panel-body"><p>Keine offenen Risiken im Bestand.</p></div>
            ) : (
              <div className="af-risks">
                {risks.map((risk) => (
                  <RiskActionCard
                    key={risk.id}
                    risk={risk}
                    context={rissKontext(risk)}
                    history={state.risks}
                    jetzt={jetzt}
                    onChanged={handleRiskChanged}
                  />
                ))}
              </div>
            )}
            {wiederOkay.length > 0 && (
              <KlappBereich
                titel="Wieder okay"
                kurz={wiederOkay.length === 1 ? '1 Risiko' : `${wiederOkay.length} Risiken`}
                merken="aufgaben:wieder-okay"
                className="af-wieder-okay"
                audit="risk-wieder-okay"
              >
                <div className="af-risks">
                  {wiederOkay.map((risk) => (
                    <RiskActionCard
                      key={risk.id}
                      risk={risk}
                      context={rissKontext(risk)}
                      history={state.risks}
                      jetzt={jetzt}
                      onChanged={handleRiskChanged}
                    />
                  ))}
                </div>
              </KlappBereich>
            )}
          </section>

          <section className="ls-panel af-col" data-audit="af-termine">
            <div className="ls-panel-head">
              <span className="ls-label">Termine</span>
              {/* Der Zaehler sagt, was er ZEIGT. Vorher stand hier „27 offen"
                  ueber genau acht Zeilen, und die uebrigen neunzehn waren
                  weder zu sehen noch zu erreichen. */}
              <span className="ls-panel-meta">
                {termineVersteckt > 0 ? `${termineSichtbar.length} von ${termine.length} offen` : `${termine.length} offen`}
              </span>
            </div>
            {termine.length === 0 ? (
              <div className="ls-panel-body"><p>Keine offenen Termine.</p></div>
            ) : (
              <ul className="af-rows">{termineSichtbar.map((item) => (
                <AfRow
                  key={item.id}
                  item={item}
                  busy={erledigt === `task-${item.taskId ?? 0}`}
                  onDone={item.taskId != null ? () => void taskErledigen(item.taskId!, item.title) : undefined}
                  onDelete={item.taskId != null ? () => void taskLoeschen(item.taskId!, item.title) : undefined}
                />
              ))}</ul>
            )}
            {termineVersteckt > 0 && (
              <button type="button" className="ls-btn is-small" style={{ margin: 12 }} onClick={() => setAlleTermine(true)}>
                weitere {termineVersteckt} anzeigen
              </button>
            )}
          </section>

          {/* Die Glaeser. Steht bei den Aufgaben, weil das Lueften eine ist —
              und weil der zugehoerige Grow laengst „beendet" heisst und
              deshalb in keiner anderen Liste dieser Seite mehr auftaucht. */}
          {state.glaeser.length > 0 && (
            <section className="ls-panel af-col" data-audit="af-curing">
              <div className="ls-panel-head">
                <span className="ls-label">Aushärten</span>
                <span className="ls-panel-meta">
                  {(() => {
                    const dran = state.glaeser.filter((g) => g.duty.level === 'Due' || g.duty.level === 'Overdue').length
                    return dran > 0 ? `${dran} dran` : `${state.glaeser.length} im Glas`
                  })()}
                </span>
              </div>
              <ul className="af-rows">
                {state.glaeser
                  .slice()
                  .sort((a, b) => ({ Overdue: 0, Due: 1, Ok: 2, Finished: 3 })[a.duty.level] - ({ Overdue: 0, Due: 1, Ok: 2, Finished: 3 })[b.duty.level])
                  .map((glas) => (
                    <li key={glas.id} className="af-row">
                      <span className={`af-badge is-${glas.duty.level === 'Overdue' ? 'critical' : glas.duty.level === 'Due' ? 'warn' : 'ok'}`}>
                        {glas.duty.level === 'Overdue' ? 'ÜBERFÄLLIG' : glas.duty.level === 'Due' ? 'HEUTE' : 'OK'}
                      </span>
                      <span className="af-title">{glas.label} · {glas.growName}</span>
                      <span className="af-meta">{faelligText(glas.duty)}</span>
                      <Link className="ls-btn is-small" to="/aushaerten">Öffnen</Link>
                    </li>
                  ))}
              </ul>
            </section>
          )}

          <section className="ls-panel af-col" data-audit="af-wartung">
            <div className="ls-panel-head">
              <span className="ls-label">Wartung</span>
              <span className="ls-panel-meta">{wartung.length} fällig</span>
            </div>
            {wartung.length === 0 ? (
              <div className="ls-panel-body"><p>Nichts fällig — Sensoren und Technik sind versorgt.</p></div>
            ) : (
              <ul className="af-rows">{wartung.map((item) => <AfRow key={item.id} item={item} />)}</ul>
            )}
          </section>
        </div>
      )}
    </V1Page>
  )
}

/** `note` traegt die Herkunft: woher ein Termin kommt, gehoert an den Termin. */
type AfItem = { id: string; when: string; due: boolean; title: string; to: string; action: string; taskId?: number; note?: string; vonKi?: string }

function AfRow({ item, busy, onDone, onDelete }: { item: AfItem; busy?: boolean; onDone?: () => void; onDelete?: () => void }) {
  return (
    <li className="af-row">
      <span className={classNames('af-when', item.due && 'is-due')}>{item.when}</span>
      <span className="af-title">
        {item.title}
        {item.vonKi && <span className="af-ki" title={`Angelegt von ${item.vonKi}`}>von KI</span>}
        {item.note && <em className="af-pump-source">{item.note}</em>}
      </span>
      {/* Eine Hauptaktion je Zeile: eine Aufgabe hakt man ab, einen laufenden
          SOP setzt man fort. Löschen ist die leise Zweitaktion für Termine,
          die nie hätten sein sollen — erledigt lügt da, weg ist ehrlich. */}
      {/* Die beiden Knoepfe in EINEM Behaelter. Vorher brachen sie einzeln um:
          bei 1440 px stand das Loesch-Kreuz 269 px links und 56 px unter
          seinem „Erledigt" — naeher an der naechsten Aufgabe als an der
          eigenen. Jetzt brechen sie nur gemeinsam. */}
      {onDone ? (
        <span className="af-tasten">
          <button type="button" className="ls-btn is-small is-primary" disabled={busy} onClick={onDone}>
            {busy ? '…' : 'Erledigt'}
          </button>
          {onDelete && (
            <button type="button" className="ls-btn is-small" disabled={busy} onClick={onDelete} aria-label={`„${item.title}" löschen`}>
              ✕
            </button>
          )}
        </span>
      ) : (
        <Link className="ls-btn is-small" to={item.to}>{item.action}</Link>
      )}
    </li>
  )
}

/** Aufgaben und laufende SOPs — das, was einen Zeitpunkt hat. */
function buildTermine(state: ActionState): AfItem[] {
  return [
    ...state.tasks.map((task) => {
      const when = dueShort(task.dueAtUtc)
      return {
        id: `task-${task.id}`,
        taskId: task.id,
        vonKi: task.createdByAssistant ?? undefined,
        when: when.label,
        due: when.due || task.priority === 'Critical',
        title: task.title,
        to: task.growId ? `/journal?growId=${task.growId}` : '/journal',
        action: 'Öffnen',
      }
    }),
    ...state.sops.map((sop) => {
      const when = dueShort(sop.nextStepDueAtUtc ?? sop.dueAtUtc)
      return {
        id: `sop-${sop.id}`,
        when: when.label,
        due: when.due,
        title: sop.sopName,
        to: sop.growId ? `/sops?growId=${sop.growId}` : '/sops',
        action: 'Weiter',
      }
    }),
  ].sort((a, b) => Number(b.due) - Number(a.due) || a.title.localeCompare(b.title))
}

/** Wartung, Kalibrierung und Hardware, die Aufmerksamkeit braucht. */
function buildWartung(state: ActionState): AfItem[] {
  const rows: AfItem[] = [
    // Was sich aus den Angaben am Geraet selbst ergibt — Lebensdauer,
    // Pruefintervall, Alter der Sicherung. Zuvor stand das nur da.
    ...state.wartung.map((punkt) => ({
      id: `due-${punkt.bereich}-${punkt.titel}`,
      when: punkt.stufe === 'kritisch' ? 'jetzt' : 'bald',
      due: punkt.stufe === 'kritisch',
      title: punkt.meldung,
      to: punkt.bereich === 'Sicherung' ? '/einstellungen' : '/sensoren',
      action: 'Öffnen',
      note: punkt.herkunft,
    })),
    ...state.maintenance.map((event) => {
      const when = dueInDays(event.dueAtUtc)
      return { id: `maintenance-${event.id}`, when: when.label, due: when.due, title: event.title, to: '/sensoren', action: 'Öffnen' }
    }),
    ...state.calibration.map((event) => {
      const when = dueInDays(event.dueAtUtc)
      return { id: `calibration-${event.id}`, when: when.label, due: when.due, title: event.title, to: '/sensoren', action: 'Öffnen' }
    }),
    ...state.hardware
      .filter((item) => item.status === 'Offline' || item.status === 'MaintenanceDue' || (isMappingExpected(item) && !item.haEntityId))
      .map((item) => ({
        id: `hardware-${item.id}`,
        when: 'jetzt',
        due: item.status === 'Offline' || item.criticality === 'Critical',
        title: `${item.name} · ${item.status === 'Offline' ? 'offline' : isMappingExpected(item) && !item.haEntityId ? 'kein Mapping' : 'Wartung fällig'}`,
        to: '/sensoren',
        action: 'Öffnen',
      })),
  ]
  return rows.sort((a, b) => Number(b.due) - Number(a.due) || a.title.localeCompare(b.title)).slice(0, 8)
}

/** „heute", Wochentag oder Datum — kurz genug für die Spalte. */
function dueShort(iso: string | null | undefined): { label: string; due: boolean } {
  if (!iso) return { label: 'offen', due: false }
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return { label: 'offen', due: false }
  const now = new Date()
  const startOfToday = new Date(now.getFullYear(), now.getMonth(), now.getDate())
  const diffDays = Math.floor((date.getTime() - startOfToday.getTime()) / 86_400_000)
  if (diffDays <= 0) return { label: 'heute', due: true }
  if (diffDays < 7) return { label: new Intl.DateTimeFormat('de-DE', { weekday: 'short' }).format(date), due: false }
  return { label: new Intl.DateTimeFormat('de-DE', { day: '2-digit', month: '2-digit' }).format(date), due: false }
}

/** „-1 T" / „7 T" — Wartung denkt in Tagen, nicht in Uhrzeiten. */
function dueInDays(iso: string | null | undefined): { label: string; due: boolean } {
  if (!iso) return { label: '—', due: false }
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return { label: '—', due: false }
  const days = Math.floor((date.getTime() - Date.now()) / 86_400_000)

  // „-6 T" stand unter der Ueberschrift „Wartung — 4 faellig", und niemand
  // liest ein Minuszeichen als „seit sechs Tagen ueberfaellig". Ein Termin,
  // der vorbei ist, heisst ueberfaellig; „0 T" heisst heute.
  if (days < 0) return { label: 'überfällig', due: true }
  if (days === 0) return { label: 'heute', due: true }
  return { label: `${days} T`, due: false }
}

// A "mapping missing" warning only makes sense for FIXED sensors that are supposed to
// deliver live values via Home Assistant. Handheld meters (e.g. a BlueLab pen) and
// equipment (pumps, chillers) are never mapped — nagging them was wrong.
function isMappingExpected(item: HardwareItemDto) {
  return item.deviceKind === 'FixedSensor'
}

function getGrowName(grows: GrowSummary[], id: number | null) { return id == null ? 'Grow offen' : grows.find((grow) => grow.id === id)?.name ?? `Grow #${id}` }
function getHardwareName(items: HardwareItemDto[], id: number | null) { return id == null ? 'Hardware offen' : items.find((item) => item.id === id)?.name ?? `Hardware #${id}` }
export default MobileActionPage
