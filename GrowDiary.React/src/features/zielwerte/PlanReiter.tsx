import { useCallback, useEffect, useMemo, useRef, useState, type PointerEvent } from 'react'
import { useSearchParams } from 'react-router-dom'
import { apiFetch, formatApiError } from '../../api'
import { V1Sheet } from '../../components/V1Sheet'
import { V1Alert, V1Badge, V1Button, V1Field, V1Section, V1Skeleton, V1Switch, V1Tabs } from '../../components/v1'
import { classNames } from '../../utils'
import {
  GRUPPEN, aenderungen, alsText, aufPlan, feldText, fehler, punkt, schluessel, startIndex, weichtAb,
  type Entwurf, type Wochenwerte,
} from '../wochenplan/wochenwerte-bearbeiten'
import '../wochenplan/wochenplan.css'
import {
  aenderungsZeilen, anfragen, buchText, dosisFehler, dosisGeaendert, mengeFuerVolumen, nachtAbweichend, nachtWieTagFuer, zeilenAus,
  type BuchEintrag, type DosisEntwurf, type DosisZeile, type PlanStand,
} from './plan-reiter'
import { wochenIndex } from './wochen-zeile'

const WISCH_SCHWELLE = 50

type Daten = {
  growId: number
  growName: string
  werte: Wochenwerte
  arbeit: PlanStand
  start: PlanStand | null
  buch: BuchEintrag[]
  volumen: number | null
}

/**
 * Fork AI (Grow-Plan, Schritt 3b): der Reiter „Plan“ — alle Wochen des Plans
 * dieses Grows. Eine Woche im Bild (wie „Werte bearbeiten“), dazu die
 * Dosierung, ein Speichern-Blatt mit der Wahl „nur dieser Grow / auch ins
 * Programm“ und das Änderungsbuch.
 */
export function PlanReiter() {
  const [daten, setDaten] = useState<Daten | null>(null)
  const [ohnePlan, setOhnePlan] = useState<string | null>(null)
  const [laden, setLaden] = useState(true)
  const [index, setIndex] = useState(0)
  const [entwurf, setEntwurf] = useState<Entwurf>({})
  const [dosis, setDosis] = useState<DosisEntwurf>({})
  const [blatt, setBlatt] = useState(false)
  const [insProgramm, setInsProgramm] = useState(false)
  const [programmName, setProgrammName] = useState('')
  const [grund, setGrund] = useState('')
  const [speichert, setSpeichert] = useState(false)
  const [meldung, setMeldung] = useState<{ text: string; ton: 'ok' | 'critical' | 'neutral' } | null>(null)
  const wischStart = useRef<{ x: number; y: number } | null>(null)
  const [params, setParams] = useSearchParams()
  const gewuenschteWoche = params.get('woche')

  const neuLaden = useCallback(async (ersterAufruf: boolean) => {
    const ziel = await apiFetch<{ growId: number | null; growName: string | null; eigenerPlan?: boolean }>('/api/zielwerte')
    if (ziel.growId == null) {
      setOhnePlan('Kein laufender Grow — ein Plan gehört zu einem Grow.')
      return
    }
    if (!ziel.eigenerPlan) {
      setOhnePlan('Dieser Grow hat noch keinen eigenen Plan. Er entsteht, sobald am Grow ein Düngeprogramm gewählt ist.')
      return
    }
    const id = ziel.growId
    const [werte, arbeit, start, buch, misch] = await Promise.all([
      apiFetch<Wochenwerte>(`/api/wochenplan/werte/${id}`),
      apiFetch<PlanStand>(`/api/grows/${id}/plan`),
      apiFetch<PlanStand>(`/api/grows/${id}/plan?stand=start`).catch(() => null),
      apiFetch<BuchEintrag[]>(`/api/grows/${id}/plan/buch`),
      apiFetch<{ volumenLiter: number | null }>(`/api/grows/${id}/mixing-plan`).catch(() => null),
    ])
    setDaten({ growId: id, growName: ziel.growName ?? `Grow ${id}`, werte, arbeit, start, buch, volumen: misch?.volumenLiter ?? null })
    if (ersterAufruf) setIndex(startIndex(werte))
  }, [])

  useEffect(() => {
    async function laden() {
      try {
        await neuLaden(true)
      } catch (caught) {
        setMeldung({ text: formatApiError(caught, 'Der Plan konnte nicht geladen werden.'), ton: 'critical' })
      } finally {
        setLaden(false)
      }
    }
    void laden()
  }, [neuLaden])

  // Fork AI (forkai.125): Das Wochen-Blatt über den Reitern öffnet den Plan an
  // einer bestimmten Woche (?woche=…). Übernommen wird der Wunsch beim Rendern
  // (kein setState im Effekt); der Effekt nimmt ihn danach nur aus der
  // Adresszeile. Ohne das hätte ein zweiter Tipp auf dieselbe Woche keine
  // Wirkung mehr, nachdem man inzwischen weitergeblättert hat.
  const [uebernommen, setUebernommen] = useState<string | null>(null)
  if (!gewuenschteWoche && uebernommen !== null) setUebernommen(null)
  if (daten && gewuenschteWoche && gewuenschteWoche !== uebernommen) {
    setUebernommen(gewuenschteWoche)
    const ziel = wochenIndex(daten.werte.spalten, gewuenschteWoche)
    if (ziel !== null) setIndex(ziel)
  }
  useEffect(() => {
    if (!daten || !gewuenschteWoche) return
    const next = new URLSearchParams(params)
    next.delete('woche')
    setParams(next, { replace: true })
  }, [daten, gewuenschteWoche, params, setParams])

  const offeneWerte = useMemo(() => (daten ? aenderungen(daten.werte, entwurf) : []), [daten, entwurf])
  const spalten = daten?.arbeit.chart.columns ?? []
  const offeneDosis = spalten.filter((s) => dosis[s.id] && dosisGeaendert(s.items, dosis[s.id])).map((s) => s.id)
  const zeilen = daten ? aenderungsZeilen(offeneWerte, dosis, spalten, daten.werte.spalten) : []
  const probleme = daten
    ? [...fehler(daten.werte, entwurf), ...spalten.flatMap((s) => (dosis[s.id] ? dosisFehler(s.label, dosis[s.id]) : []))]
    : []

  if (laden) return <V1Skeleton rows={6} label="Lade Plan" />
  if (ohnePlan) return <V1Alert tone="neutral" message={ohnePlan} />
  if (!daten) return meldung ? <V1Alert tone={meldung.ton} message={meldung.text} /> : null

  const woche = daten.werte.spalten[index]
  const planSpalte = spalten.find((s) => s.id === woche.id)
  const startSpalte = daten.start?.chart.columns.find((s) => s.id === woche.id)
  const letzte = daten.werte.spalten.length - 1
  const dosisZeilen: DosisZeile[] = dosis[woche.id] ?? zeilenAus(planSpalte?.items ?? [])
  const feldName = (feld: string) =>
    daten.werte.spalten[0]?.felder.find((f) => f.feld === feld)?.bezeichnung ?? feld
  const wochenName = (id: string | null) => daten.werte.spalten.find((s) => s.id === id)?.label ?? id ?? ''
  const eigenesName = daten.arbeit.eigenesProgrammName

  const blaettern = (richtung: -1 | 1) => setIndex((i) => Math.min(letzte, Math.max(0, i + richtung)))

  // Fork AI (forkai.130): „Nachts gelten die Tageswerte" — Standard und je Woche.
  const wieTag = nachtWieTagFuer(daten.arbeit, woche.id)
  const weichtAbVomStandard = nachtAbweichend(daten.arbeit, woche.id)
  async function nachtSetzen(body: { standard?: boolean; spalteId?: string; woche?: boolean | null }) {
    if (!daten) return
    try {
      const neu = await apiFetch<PlanStand>(`/api/grows/${daten.growId}/plan/nacht`, { method: 'POST', body: JSON.stringify(body) })
      setDaten({ ...daten, arbeit: neu })
      setMeldung(null)
    } catch (caught) {
      setMeldung({ text: formatApiError(caught, 'Die Nacht-Einstellung konnte nicht gespeichert werden.'), ton: 'critical' })
    }
  }

  function setzZeilen(neu: DosisZeile[]) {
    setDosis((alt) => ({ ...alt, [woche.id]: neu }))
    setMeldung(null)
  }

  function wischBeginn(event: PointerEvent<HTMLDivElement>) {
    const ziel = event.target as HTMLElement
    if (ziel.closest('button')) return
    if (event.pointerType === 'mouse' && ziel.closest('input')) return
    wischStart.current = { x: event.clientX, y: event.clientY }
    event.currentTarget.setPointerCapture?.(event.pointerId)
  }

  function wischEnde(event: PointerEvent<HTMLDivElement>) {
    const start = wischStart.current
    wischStart.current = null
    if (!start) return
    const dx = event.clientX - start.x
    const dy = event.clientY - start.y
    if (Math.abs(dx) < WISCH_SCHWELLE || Math.abs(dx) < Math.abs(dy) * 1.5) return
    blaettern(dx < 0 ? 1 : -1)
  }

  function blattOeffnen() {
    setInsProgramm(false)
    setProgrammName(eigenesName ?? `${daten!.arbeit.programmName} (eigen)`)
    setGrund('')
    setBlatt(true)
  }

  async function speichern() {
    if (!daten) return
    setSpeichert(true)
    setMeldung(null)
    const liste = anfragen(offeneWerte, dosis, spalten, { auchInsProgramm: insProgramm, programmName, grund })
    let gespeichert = 0
    let programm: string | null = null
    try {
      for (const anfrage of liste) {
        const antwort = await apiFetch<{ aenderungen: number; programmName: string | null }>(`/api/grows/${daten.growId}/plan`, {
          method: 'POST',
          body: JSON.stringify(anfrage),
        })
        gespeichert += antwort.aenderungen
        programm = antwort.programmName ?? programm
      }
      setEntwurf({})
      setDosis({})
      setBlatt(false)
      await neuLaden(false)
      setMeldung({
        ton: 'ok',
        text: `${gespeichert === 1 ? '1 Änderung' : `${gespeichert} Änderungen`} gespeichert`
          + (programm ? ` — auch im Programm „${programm}“.` : ` — nur für ${daten.growName}.`),
      })
    } catch (caught) {
      // Frühere Wochen dieser Runde sind schon gespeichert: neu laden, damit
      // der Entwurf nicht mehr zeigt, was längst im Plan steht.
      await neuLaden(false).catch(() => undefined)
      setMeldung({ ton: 'critical', text: formatApiError(caught, 'Speichern hat nicht geklappt.') })
      setBlatt(false)
    } finally {
      setSpeichert(false)
    }
  }

  const anzahl = zeilen.length

  return (
    <div className="wp-bearbeiten" data-audit="plan-reiter">
      <p className="pr-kopf">
        {daten.growName} · Plan aus <b>{daten.arbeit.programmName}</b>
        {daten.start?.vermerk && <V1Badge tone="warn">{daten.start.vermerk}</V1Badge>}
      </p>

      <div className="pr-nacht-schalter" data-audit="plan-nacht-standard">
        <V1Switch
          label="Nachts gelten die Tageswerte"
          checked={Boolean(daten.arbeit.nachtWieTag)}
          onChange={(an) => void nachtSetzen({ standard: an })}
          hint="Standard für alle Wochen dieses Grows (Luft und Luftfeuchte). Einzelne Wochen kannst du abweichend einstellen."
        />
      </div>

      <div className="wp-blatt" onPointerDown={wischBeginn} onPointerUp={wischEnde} onPointerCancel={() => (wischStart.current = null)}>
        <div className="wp-blatt-kopf">
          <button type="button" className="wp-pfeil" aria-label="Vorige Woche" disabled={index === 0} onClick={() => blaettern(-1)}>‹</button>
          <div className="wp-blatt-titel">
            <b>{woche.label}</b>
            {woche.istJetzt && <V1Badge tone="accent">läuft</V1Badge>}
          </div>
          <button type="button" className="wp-pfeil" aria-label="Nächste Woche" disabled={index === letzte} onClick={() => blaettern(1)}>›</button>
        </div>

        <div className="pr-nacht-wahl" role="radiogroup" aria-label={`Nachtwerte ${woche.label}`} data-audit="plan-nacht-woche">
          <button type="button" role="radio" aria-checked={!weichtAbVomStandard} className={classNames('pr-nacht-knopf', !weichtAbVomStandard && 'ist-aktiv')}
            onClick={() => void nachtSetzen({ spalteId: woche.id, woche: null })}>
            Nacht wie Standard
          </button>
          <button type="button" role="radio" aria-checked={weichtAbVomStandard} className={classNames('pr-nacht-knopf', weichtAbVomStandard && 'ist-aktiv')}
            onClick={() => void nachtSetzen({ spalteId: woche.id, woche: !daten.arbeit.nachtWieTag })}>
            {daten.arbeit.nachtWieTag ? 'eigene Nachtwerte' : 'nachts wie tags'}
          </button>
        </div>

        <div className="wp-gitter wp-gitter-edit">
          {GRUPPEN.map((gruppe) => {
            const felder = gruppe.felder
              .map((name) => woche.felder.find((f) => f.feld === name))
              .filter((f) => f !== undefined)
            if (felder.length === 0) return null
            if (gruppe.nacht && wieTag) {
              // Ruht: nachts gilt der Tageswert — zeigen, welcher, statt ein Feld anzubieten.
              const tagFeld = woche.felder.find((f) => f.feld === gruppe.nacht?.tag)
              const tagText = tagFeld ? (feldText(entwurf, woche.id, tagFeld) || alsText(tagFeld.plan)) : ''
              return (
                <div key={gruppe.titel} className="wp-zelle pr-wie-tag" data-audit={`plan-${felder[0].feld}-wie-tag`}>
                  <div className="wp-zelle-n">{gruppe.titel}</div>
                  <div className="pr-wie-tag-wert">= {tagText || '–'}{felder[0].einheit ? ` ${felder[0].einheit}` : ''}</div>
                  <div className="wp-plan">wie tags</div>
                </div>
              )
            }
            const eigen = felder.some((f) => weichtAb(entwurf, woche.id, f))
            return (
              <div key={gruppe.titel} className={classNames('wp-zelle', eigen && 'ist-eigen')}>
                <div className="wp-zelle-n">
                  {gruppe.titel}
                  {felder[0].einheit && <span className="wp-einheit"> {felder[0].einheit}</span>}
                </div>
                <div className="wp-eingaben">
                  {felder.map((f, i) => (
                    <span key={f.feld} className="wp-eingabe-paar">
                      {i > 0 && <span className="wp-leise">–</span>}
                      <input
                        className="wp-eingabe"
                        type="text"
                        inputMode="decimal"
                        aria-label={`${woche.label}: ${f.bezeichnung}`}
                        data-audit={`plan-${f.feld}`}
                        value={feldText(entwurf, woche.id, f)}
                        placeholder={alsText(f.plan) || (gruppe.nacht ? 'wie tags' : 'eintragen')}
                        onChange={(e) => { setEntwurf((alt) => ({ ...alt, [schluessel(woche.id, f.feld)]: e.target.value })); setMeldung(null) }}
                      />
                    </span>
                  ))}
                </div>
                <div className="wp-plan">
                  {gruppe.nacht && felder.every((f) => f.plan == null)
                    ? 'leer = wie tags'
                    : <>Start {felder.map((f) => alsText(f.plan) || '–').join('–')}</>}
                  {eigen && (
                    <>
                      {' · '}
                      <button type="button" className="wp-plan-zurueck" onClick={() => setEntwurf((alt) => aufPlan(alt, woche, gruppe.felder))}>
                        zurück
                      </button>
                    </>
                  )}
                </div>
              </div>
            )
          })}
        </div>
      </div>

      <div className="wp-punkte" role="tablist" aria-label="Wochen">
        {daten.werte.spalten.map((s, i) => {
          const zustand = punkt(daten.werte, entwurf, s)
          return (
            <button
              key={s.id}
              type="button"
              role="tab"
              aria-selected={i === index}
              aria-label={s.label}
              title={s.label}
              className={classNames(
                'wp-punkt',
                i === index && 'ist-aktiv',
                s.istJetzt && 'ist-jetzt',
                zustand.eigen && 'ist-eigen',
                nachtAbweichend(daten.arbeit, s.id) && 'hat-nacht-abweichung',
                (zustand.offen || offeneDosis.includes(s.id)) && 'ist-offen',
              )}
              onClick={() => setIndex(i)}
            />
          )
        })}
      </div>

      <V1Section title="Dosierung · ml je Liter">
        <div className="pr-dosis" data-audit="plan-dosierung">
          {dosisZeilen.length === 0 && <p className="wp-leise">Für diese Woche ist keine Dosierung hinterlegt.</p>}
          {dosisZeilen.map((z, i) => {
            const aendern = (teil: Partial<DosisZeile>) => setzZeilen(dosisZeilen.map((alt, j) => (j === i ? { ...alt, ...teil } : alt)))
            const start = startSpalte?.items.find((s) => s.component === z.name)
            return (
              <div key={i} className={classNames('pr-zeile', z.entfernt && 'ist-entfernt', z.neu && 'ist-neu')}>
                {z.neu ? (
                  <input className="wp-eingabe pr-name" type="text" aria-label="Zutat" placeholder="Zutat"
                    value={z.name} onChange={(e) => aendern({ name: e.target.value })} />
                ) : (
                  <span className="pr-name">
                    {z.name}
                    {start && <small> · Start {alsText(start.minMlPerLiter)}</small>}
                  </span>
                )}
                <input className="wp-eingabe pr-ml" type="text" inputMode="decimal" aria-label={`${z.name || 'Zutat'} ml je Liter`}
                  disabled={z.entfernt} value={z.ml} onChange={(e) => aendern({ ml: e.target.value })} />
                <span className="pr-menge">{z.entfernt ? '' : mengeFuerVolumen(z.ml, daten.volumen) ?? ''}</span>
                <button type="button" className="wp-plan-zurueck pr-x"
                  aria-label={z.entfernt ? `${z.name} wiederherstellen` : `${z.name || 'Zutat'} entfernen`}
                  onClick={() => (z.neu ? setzZeilen(dosisZeilen.filter((_, j) => j !== i)) : aendern({ entfernt: !z.entfernt }))}>
                  {z.entfernt ? '↺' : '✕'}
                </button>
              </div>
            )
          })}
          <button type="button" className="pr-zutat" data-audit="plan-zutat"
            onClick={() => setzZeilen([...dosisZeilen, { name: '', ml: '', entfernt: false, neu: true }])}>
            + Zutat hinzufügen
          </button>
          {daten.volumen != null && (
            <p className="wp-leise">Mengen rechts für {alsText(daten.volumen)} l Anlagenvolumen.</p>
          )}
        </div>
      </V1Section>

      <p className="wp-leise pr-hinweis">
        Alarm-Toleranzen stellst du je Messgröße im Reiter „Werte“ ein — sie folgen dem Plan von selbst.
      </p>

      {probleme.length > 0 && <V1Alert tone="critical" message={probleme.join(' ')} />}
      {meldung && <V1Alert tone={meldung.ton} message={meldung.text} />}

      <div className="wp-speicherbalken" data-audit="plan-speicherbalken">
        <span className="wp-speicherbalken-zahl">
          {anzahl === 0 ? 'Keine Änderungen' : `${anzahl} Änderung${anzahl === 1 ? '' : 'en'}`}
        </span>
        <V1Button variant="ghost" onClick={() => { setEntwurf({}); setDosis({}) }} disabled={speichert || anzahl === 0}>
          Verwerfen
        </V1Button>
        <V1Button variant="primary" onClick={blattOeffnen} disabled={speichert || anzahl === 0 || probleme.length > 0} audit="plan-speichern">
          Speichern
        </V1Button>
      </div>

      <V1Section title={`Änderungsbuch · ${daten.buch.length}`}>
        <div className="pr-buch" data-audit="plan-buch">
          {daten.buch.map((e) => (
            <div key={e.id} className="pr-eintrag">
              <span className="pr-wann">{new Date(e.zeitUtc).toLocaleString('de-DE', { dateStyle: 'short', timeStyle: 'short' })}</span>
              <div>
                <b>{e.spalteId ? wochenName(e.spalteId) : 'Plan'}</b>
                <span>{buchText(e, feldName)}</span>
                {e.grund && <em>„{e.grund}“</em>}
                <span className="pr-pills">
                  {e.ziel?.startsWith('programm:') && <V1Badge tone="accent">auch im Programm</V1Badge>}
                </span>
              </div>
            </div>
          ))}
        </div>
      </V1Section>

      <V1Sheet
        open={blatt}
        onClose={() => !speichert && setBlatt(false)}
        title="Änderungen speichern"
        subtitle={`${anzahl} Änderung${anzahl === 1 ? '' : 'en'}`}
        footer={(
          <div className="wb-fuss">
            <span />
            <V1Button variant="ghost" onClick={() => setBlatt(false)} disabled={speichert}>Abbrechen</V1Button>
            <V1Button variant="primary" onClick={() => void speichern()} disabled={speichert || (insProgramm && programmName.trim() === '')} audit="plan-speichern-bestaetigen">
              {speichert ? 'Speichert …' : 'Speichern'}
            </V1Button>
          </div>
        )}
      >
        <section className="wb-block">
          <h3>Was sich ändert</h3>
          {zeilen.map((z, i) => (
            <div key={i} className="wb-zeile"><span>{z.woche}</span><span>{z.text}</span></div>
          ))}
        </section>
        <section className="wb-block">
          <h3>Speichern für</h3>
          <V1Tabs
            label="Speichern für"
            items={[{ value: 'grow', label: `Nur ${daten.growName}` }, { value: 'programm', label: 'Auch ins Programm' }]}
            active={insProgramm ? 'programm' : 'grow'}
            onChange={(wert) => setInsProgramm(wert === 'programm')}
          />
          <p className="wb-hinweis">
            {insProgramm
              ? eigenesName
                ? `Geht zusätzlich in dein Programm „${eigenesName}“ — künftige Grows können es wählen.`
                : `${daten.arbeit.programmName} ist mitgeliefert und bleibt unverändert. Es entsteht ein eigenes Programm, das künftige Grows wählen können.`
              : 'Das Programm bleibt, wie es ist.'}
          </p>
          {insProgramm && !eigenesName && (
            <V1Field label="Name des eigenen Programms">
              <input value={programmName} onChange={(e) => setProgrammName(e.target.value)} maxLength={80} />
            </V1Field>
          )}
        </section>
        <section className="wb-block">
          <V1Field label="Warum? (freiwillig)" hint="Steht im Änderungsbuch und später im Archiv.">
            <input value={grund} onChange={(e) => setGrund(e.target.value)} maxLength={300} />
          </V1Field>
        </section>
      </V1Sheet>
    </div>
  )
}
