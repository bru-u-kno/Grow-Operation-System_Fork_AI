import { useCallback, useEffect, useState } from 'react'
import { apiFetch, formatApiError } from '../../api'
import { V1Alert, V1Badge, V1Button, V1Card, V1Section, V1Skeleton, type Tone } from '../../components/v1'
import {
  automationen, bausteinModule, fehlendText, helferUndRechenwerte, reife, summe, wuerdeSchreiben, zaehleFehlend,
  type Phase2, type Reife,
} from './bereitstellen-ablauf'
import type { Bestandsaufnahme, SteuerungAuswahl } from './steuerung-typen'

/**
 * Fork AI (A-016, Etappe 3): Schritt 3 — bereitstellen, was in Home Assistant noch fehlt.
 *
 * Nur Steuerungen, bei denen alle Pflichtgeräte zugeordnet sind, werden angefasst: ohne sie bliebe ein Rechenwert
 * stumm bei seinem Ausweichwert, und eine Automation schaltete auf ein Gerät, das nicht da ist. Die anderen stehen
 * als „unvollständig" da, mit dem Weg zurück zum Zuordnen.
 *
 * <b>Zwei Zustimmungen.</b> Helfer und Rechenwerte legt der Knopf sofort an (sie schalten nichts). Die Automationen
 * werden erst gezeigt — mit dem, was neu entsteht und was von Hand Gebautes unangetastet bleibt — und dann auf eine
 * zweite Bestätigung geschrieben: an ihrem Ende hängt ein Ventil oder ein Kompressor.
 */

const REIFE_TEXT: Record<Reife, { text: string; ton: Tone }> = {
  'ohne-bausteine': { text: 'fertig', ton: 'ok' },
  unvollstaendig: { text: 'Pflichtgerät fehlt', ton: 'warn' },
  'ha-stumm': { text: 'Home Assistant antwortet nicht', ton: 'warn' },
  bereit: { text: 'bereit', ton: 'ok' },
  anlegen: { text: 'bereit zum Anlegen', ton: 'accent' },
}

const STAND_LESBAR: Record<string, string> = {
  Angelegt: 'wird angelegt', Erneuert: 'wird erneuert', Fremd: 'bleibt unangetastet',
  OhneGeraet: 'entfällt', Fehlgeschlagen: 'fehlgeschlagen',
}

export function Bereitstellen({ zusatz }: { zusatz: boolean }) {
  const [auswahl, setAuswahl] = useState<SteuerungAuswahl | null>(null)
  const [bestaende, setBestaende] = useState<Record<string, Bestandsaufnahme | null>>({})
  const [laedt, setLaedt] = useState(true)
  const [arbeitet, setArbeitet] = useState(false)
  const [fehler, setFehler] = useState<string | null>(null)
  const [meldung, setMeldung] = useState<string | null>(null)
  const [vorschau, setVorschau] = useState<Phase2[] | null>(null)

  /** Stand frisch lesen: Auswahl (Pflichtgeräte) und je Modul der Bestand. */
  const lesen = useCallback(async (signal?: AbortSignal) => {
    const a = await apiFetch<SteuerungAuswahl>('/api/steuerung/auswahl', { signal })
    const alle = [...new Set(bausteinModule(a.eintraege.filter((e) => e.gewaehlt), zusatz))]
    const stande: Record<string, Bestandsaufnahme | null> = {}
    await Promise.all(alle.map(async (m) => {
      stande[m] = await apiFetch<Bestandsaufnahme>(`/api/steuerung/${m}/bestand`, { signal }).catch(() => null)
    }))
    return { a, stande }
  }, [zusatz])

  useEffect(() => {
    const controller = new AbortController()
    void lesen(controller.signal)
      .then(({ a, stande }) => { if (!controller.signal.aborted) { setAuswahl(a); setBestaende(stande) } })
      .catch((caught) => { if (!controller.signal.aborted) setFehler(formatApiError(caught, 'Der Stand konnte nicht gelesen werden.')) })
      .finally(() => { if (!controller.signal.aborted) setLaedt(false) })
    return () => controller.abort()
  }, [lesen])

  const gewaehlt = (auswahl?.eintraege ?? []).filter((e) => e.gewaehlt)
  const mitReife = gewaehlt.map((e) => {
    const mods = bausteinModule([e], zusatz)
    return { e, mods, r: reife({ ...e, module: mods }, bestaende) }
  })
  // Angefasst wird nur, was vollständig zugeordnet ist.
  const zuTun = mitReife.filter((x) => x.r === 'anlegen' || x.r === 'bereit').flatMap((x) => x.mods)
  const fehltGesamt = summe(zuTun.map((m) => zaehleFehlend(bestaende[m])))
  const nichtsMehr = fehltGesamt.helfer + fehltGesamt.rechenwerte + fehltGesamt.automationen === 0

  async function neuLesen() {
    const { a, stande } = await lesen()
    setAuswahl(a)
    setBestaende(stande)
  }

  /** Phase 1: Helfer und Rechenwerte — danach die Vorschau der Automationen. */
  async function bereitstellen() {
    setArbeitet(true)
    setFehler(null)
    setMeldung(null)
    setVorschau(null)
    try {
      const r = await helferUndRechenwerte(zuTun, bestaende, apiFetch)
      const angelegt = r.reduce((n, x) => n + x.helfer + x.rechenwerte, 0)
      const nicht = r.reduce((n, x) => n + x.nicht, 0)
      const kaputt = r.filter((x) => x.fehler)
      const teile = [`${angelegt} Helfer und Rechenwerte angelegt`]
      if (nicht > 0) teile.push(`${nicht} nicht — Einzelheiten stehen im Protokoll`)
      for (const k of kaputt) teile.push(`${k.modul}: ${k.fehler}`)
      setMeldung(`${teile.join('. ')}.`)
      await neuLesen()
      // Die Automationen nur ZEIGEN — geschrieben wird erst nach der zweiten Zustimmung.
      const stand = await lesen()
      setVorschau(await automationen(zuTun.filter((m) => zaehleFehlend(stand.stande[m]).automationen > 0 || (stand.stande[m]?.veraltet ?? 0) > 0), true, apiFetch))
    } catch (caught) {
      setFehler(formatApiError(caught, 'Das Bereitstellen hat nicht geklappt.'))
    } finally {
      setArbeitet(false)
    }
  }

  /** Phase 2: die Automationen wirklich schreiben. */
  async function automationenAnlegen() {
    if (!vorschau) return
    setArbeitet(true)
    setFehler(null)
    try {
      const r = await automationen(vorschau.map((p) => p.modul), false, apiFetch)
      const geschrieben = r.reduce((n, p) => n + (p.bilanz?.angelegt ?? 0), 0)
      const fremd = r.reduce((n, p) => n + (p.bilanz?.fremd ?? 0), 0)
      const nicht = r.reduce((n, p) => n + (p.bilanz?.fehlgeschlagen ?? 0), 0)
      const kaputt = r.filter((p) => p.fehler)
      const teile = [`${geschrieben} ${geschrieben === 1 ? 'Automation' : 'Automationen'} geschrieben`]
      if (fremd > 0) teile.push(`${fremd} von Hand gebaute blieben unangetastet`)
      if (nicht > 0) teile.push(`${nicht} nicht`)
      for (const k of kaputt) teile.push(`${k.modul}: ${k.fehler}`)
      setMeldung(`${teile.join(', ')}.`)
      setVorschau(null)
      await neuLesen()
    } catch (caught) {
      setFehler(formatApiError(caught, 'Das Anlegen der Automationen hat nicht geklappt.'))
    } finally {
      setArbeitet(false)
    }
  }

  if (laedt) return <V1Skeleton rows={4} label="Der Stand wird gelesen" />
  if (!auswahl) return <V1Alert tone="critical" message={fehler ?? 'Der Stand konnte nicht gelesen werden.'} />

  return (
    <V1Section title="Bereitstellen">
      {fehler && <V1Alert tone="critical" message={fehler} />}
      {meldung && <V1Alert tone="ok" message={meldung} />}
      <V1Card>
        {mitReife.map(({ e, mods, r }) => {
          const fehlt = summe(mods.map((m) => zaehleFehlend(bestaende[m])))
          const t = REIFE_TEXT[r]
          return (
            <div className="st-feldzeile" key={e.kennung}>
              <span className="st-etikett">
                {e.titel}
                <small>
                  {r === 'ohne-bausteine' ? 'Nur Zuordnung, nichts anzulegen.'
                    : r === 'unvollstaendig' ? `${e.pflichtZugeordnet} von ${e.pflichtGesamt} Pflichtgeräten zugeordnet — erst zuordnen, dann bereitstellen.`
                    : r === 'anlegen' ? fehlendText(fehlt)
                    : r === 'bereit' ? 'Alles ist in Home Assistant angelegt.'
                    : 'Ohne Antwort ist nicht zu erkennen, was schon da ist.'}
                </small>
              </span>
              <V1Badge tone={t.ton}>{t.text}</V1Badge>
            </div>
          )
        })}
      </V1Card>

      {vorschau ? (
        <V1Card>
          <h3 className="st-gruppe">Diese Automationen würden geschrieben</h3>
          {vorschau.flatMap((p) => p.bilanz?.einzeln ?? []).map((a) => (
            <div className="st-feldzeile" key={a.kennung}>
              <span className="st-etikett">
                {a.titel ?? a.name}
                <small>{a.hinweis ?? STAND_LESBAR[a.stand] ?? a.stand}</small>
              </span>
              <span className="st-nurlesen">{STAND_LESBAR[a.stand] ?? a.stand}</span>
            </div>
          ))}
          {vorschau.filter((p) => p.fehler).map((p) => <V1Alert key={p.modul} tone="critical" message={`${p.modul}: ${p.fehler}`} />)}
          <p className="st-hinweis">
            Sie schalten Geräte — bei CO₂ ein Ventil, beim Kühler einen Kompressor. Der vorhandene Stand wird vorher gesichert,
            danach wird nachgesehen, ob Home Assistant sie wirklich geladen hat. Von Hand Gebautes bleibt unangetastet.
          </p>
          <div className="st-knopfleiste">
            <V1Button variant="ghost" disabled={arbeitet} onClick={() => setVorschau(null)}>Nicht anlegen</V1Button>
            <V1Button variant="primary" disabled={arbeitet || wuerdeSchreiben(vorschau) === 0} onClick={() => void automationenAnlegen()}>
              {arbeitet ? 'Schreibt …' : wuerdeSchreiben(vorschau) === 0 ? 'Nichts zu schreiben' : 'Automationen jetzt anlegen'}
            </V1Button>
          </div>
        </V1Card>
      ) : (
        <div className="st-knopfleiste">
          <V1Button variant="primary" disabled={arbeitet || zuTun.length === 0} onClick={() => void bereitstellen()}>
            {arbeitet ? 'Legt an …' : zuTun.length > 0 && nichtsMehr ? 'Alles ist angelegt — Automationen prüfen' : 'Alles Gewählte bereitstellen'}
          </V1Button>
        </div>
      )}
      {zuTun.length === 0 && <p className="st-hinweis">Es gibt noch nichts bereitzustellen — ordne zuerst die Pflichtgeräte zu.</p>}
    </V1Section>
  )
}
