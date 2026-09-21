import { useEffect, useState } from 'react'
import { apiFetch, formatApiError } from '../../api'
import { V1Sheet } from '../../components/V1Sheet'
import { V1Alert, V1Button, V1Field, V1Switch, V1Tabs } from '../../components/v1'
import { classNames } from '../../utils'
import type { TentAlertRulesDto } from '../../types/alert'
import {
  UEBERGABE_JE_METRIK, alarmGeaendert, entwurfAus, planAenderungen, pruefen,
  regelnMitAenderung, zahlText, type AlarmRegel, type Entwurf, type PlanFeld,
} from './wert-blatt'
import { TAG_NACHT_ROLLEN, deutscheWoche, engesBand, planKurz, planWaereText, planwertText, weichtVomPlanAb, zeilenStand } from './tag-nacht'
import { nachtWieTagFuer, type PlanStand } from './plan-reiter'

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

  // Fork AI (forkai.130): Tag- und Nachtzeilen für Werte, die der Plan Tag/Nacht führt.
  const tagNacht = entwurf.quelle === 'Fest' ? TAG_NACHT_ROLLEN[wert.key] : undefined
  const [wieTag, setWieTag] = useState<boolean | null>(null)
  const [warnung, setWarnung] = useState<string | null>(null)
  useEffect(() => {
    if (!tagNacht || !spalteId) return
    let aus = false
    apiFetch<PlanStand>(`/api/grows/${growId}/plan`)
      .then((stand) => { if (!aus) setWieTag(nachtWieTagFuer(stand, spalteId)) })
      .catch(() => { if (!aus) setWieTag(false) })
    return () => { aus = true }
  }, [tagNacht, growId, spalteId])
  const tagStand = tagNacht ? zeilenStand(uebergabe, tagNacht.tag) : null
  const nachtStand = tagNacht ? zeilenStand(uebergabe, tagNacht.nacht) : null
  // Nacht gilt gerade, wenn die aktuellen Alarmgrenzen die Nachtgrenzen sind und sich vom Tag unterscheiden.
  const nachtJetzt = Boolean(wert.regel) && wert.alarmVon === wert.regel?.nachtMin && wert.alarmBis === wert.regel?.nachtMax
    && (wert.regel?.min !== wert.regel?.nachtMin || wert.regel?.max !== wert.regel?.nachtMax)

  async function zurueckZumPlan(rollen: readonly string[], zeile: string) {
    setFehler(null)
    try {
      for (const rolle of rollen) {
        await apiFetch(`/api/wochenplan/freigeben/${encodeURIComponent(rolle)}`, { method: 'POST' })
      }
      onGespeichert(`${wert.name} · ${zeile}: folgt wieder dem Plan.`)
    } catch (caught) {
      setFehler(formatApiError(caught, 'Zurück zum Plan hat nicht geklappt.'))
    }
  }

  const setz = (teil: Partial<Entwurf>) => setEntwurf((alt) => ({ ...alt, ...teil }))
  const setzPlan = (feld: string, text: string) =>
    setEntwurf((alt) => ({ ...alt, plan: { ...alt.plan, [feld]: text } }))

  async function speichern() {
    const problem = pruefen(felder, entwurf)
    if (problem) {
      setFehler(problem)
      return
    }
    // Fork AI (forkai.130): nur bei offensichtlich unpassenden Grenzen einmal nachfragen.
    if (tagNacht && !warnung) {
      const eng = engesBand(wieTag ? 'Tag und Nacht' : 'Tag', entwurf.min, entwurf.max, wert.einheit)
        ?? (wieTag ? null : engesBand('Nacht', entwurf.nachtMin ?? '', entwurf.nachtMax ?? '', wert.einheit))
      if (eng) {
        setWarnung(eng)
        return
      }
    }
    setWarnung(null)
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
        // „Tag und Nacht": die Nacht bekommt dieselben Grenzen wie der Tag.
        const gespeichert = tagNacht && wieTag ? { ...entwurf, nachtMin: entwurf.min, nachtMax: entwurf.max } : entwurf
        await apiFetch(`/api/alerts/tents/${zeltId}`, {
          method: 'PUT',
          body: JSON.stringify({ rules: regelnMitAenderung(aktuell.rules, wert.key, gespeichert) }),
        })
        // Eine geänderte Abweichung soll sofort gelten, nicht erst beim Tageslauf um 06:00.
        if (tagNacht?.abweichung) await apiFetch('/api/wochenplan/uebergeben', { method: 'POST' })
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
            {speichert ? 'Speichert…' : warnung ? 'Trotzdem speichern' : 'Speichern'}
          </V1Button>
        </div>
      )}
    >
      {fehler && <V1Alert message={fehler} tone="critical" />}
      {warnung && <V1Alert message={warnung} tone="warn" />}

      <section className="wb-block">
        <h3>{woche ? `Ziel · ${deutscheWoche(woche)}` : 'Ziel'}</h3>
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
        {tagNacht ? (
          <div className="tn-block" data-audit="alarm-tag-nacht">
            {(tagStand?.planVon != null || tagStand?.planBis != null) && (
              <p className="tn-plan">
                Plan{woche ? <> · <b>{deutscheWoche(woche)}</b></> : null}
                {planKurz(tagStand, nachtStand, wieTag, wert.einheit) ? ` · ${planKurz(tagStand, nachtStand, wieTag, wert.einheit)}` : ''}
              </p>
            )}
            {tagNacht.abweichung && (
              <div className="tn-abweichung">
                <V1Field label="Erlaubte Abweichung ± K" hint="So weit darf der Wert vom Planwert abweichen, bevor gemeldet wird.">
                  <input inputMode="decimal" value={entwurf.toleranz} placeholder="3" onChange={(e) => { setz({ toleranz: e.target.value }); setWarnung(null) }} />
                </V1Field>
              </div>
            )}
            <TagNachtZeile
              titel={wieTag ? 'Tag und Nacht' : 'Tag'}
              jetzt={!nachtJetzt && !wieTag}
              von={entwurf.min}
              bis={entwurf.max}
              onVon={(v) => { setz({ min: v }); setWarnung(null) }}
              onBis={(v) => { setz({ max: v }); setWarnung(null) }}
              stand={tagStand}
              einheit={wert.einheit}
              onZurueck={() => void zurueckZumPlan(wieTag ? [...tagNacht.tag, ...tagNacht.nacht] : tagNacht.tag, wieTag ? 'Tag und Nacht' : 'Tag')}
            />
            {wieTag === false && (
              <TagNachtZeile
                titel="Nacht"
                jetzt={nachtJetzt}
                von={entwurf.nachtMin ?? ''}
                bis={entwurf.nachtMax ?? ''}
                onVon={(v) => { setz({ nachtMin: v }); setWarnung(null) }}
                onBis={(v) => { setz({ nachtMax: v }); setWarnung(null) }}
                stand={nachtStand}
                einheit={wert.einheit}
                onZurueck={() => void zurueckZumPlan(tagNacht.nacht, 'Nacht')}
              />
            )}
            {wieTag && <p className="wb-hinweis">Nachts gelten die Tageswerte — ändern im Reiter „Plan“.</p>}
          </div>
        ) : entwurf.quelle === 'Fest' ? (
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
        {!tagNacht && entwurf.quelle === 'Fest' && (wert.regel?.nachtMin != null || wert.regel?.nachtMax != null) && (
          <p className="wb-hinweis">
            Nachts gilt {zahlText(wert.regel?.nachtMin)}–{zahlText(wert.regel?.nachtMax)} — das zieht der Plan nach.
          </p>
        )}
        {!tagNacht && (wert.alarmVon != null || wert.alarmBis != null) && (
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

/**
 * Fork AI (forkai.130): eine Zeile „Tag" oder „Nacht" — zwei Felder, eine
 * Statuszeile, eine kurze Zeile zum Planwert. Nichts darf umbrechen (Mockup Stand 4).
 */
function TagNachtZeile({ titel, jetzt, von, bis, onVon, onBis, stand, einheit, onZurueck }: {
  titel: string
  jetzt: boolean
  von: string
  bis: string
  onVon: (wert: string) => void
  onBis: (wert: string) => void
  stand: ReturnType<typeof zeilenStand> | null
  einheit: string | null
  onZurueck: () => void
}) {
  // Auch eine gerade geänderte, noch nicht gespeicherte Zahl ist schon ein eigener Wert.
  const eigen = weichtVomPlanAb(stand, von, bis)
  const klein = stand?.folgtPlan == null ? null : eigen ? planWaereText(stand) : planwertText(stand, einheit)
  return (
    <div className={classNames('tn-zeile', jetzt && 'ist-jetzt')} data-audit={`alarm-zeile-${titel}`}>
      <div className="tn-kopf">
        <b>{titel}</b>
        {jetzt && <span className="tn-jetzt">gilt gerade</span>}
      </div>
      <div className="wb-felder">
        <V1Field label="melden unter">
          <input inputMode="decimal" aria-label={`${titel}: melden unter`} value={von} onChange={(e) => onVon(e.target.value)} />
        </V1Field>
        <V1Field label="melden über">
          <input inputMode="decimal" aria-label={`${titel}: melden über`} value={bis} onChange={(e) => onBis(e.target.value)} />
        </V1Field>
      </div>
      {stand?.folgtPlan != null && (
        <div className="tn-status">
          {eigen
            ? <><span className="tn-eigen">● eigener Wert</span><button type="button" className="tn-zurueck" onClick={onZurueck}>Zurück zum Plan</button></>
            : <span className="tn-ok">✓ folgt dem Plan</span>}
        </div>
      )}
      {klein && <p className="tn-klein">{klein}</p>}
    </div>
  )
}
