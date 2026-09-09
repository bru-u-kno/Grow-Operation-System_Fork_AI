import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { apiFetch, formatApiError } from '../api'
import { V1Alert, V1Button, V1Card, V1Empty, V1Field, V1Page, V1Section, V1Skeleton, V1Stat, V1Tabs } from '../components/v1'
import { LAGER, euro, growOptionen, tage } from '../features/kosten/kosten-typen'
import type { EntitaetTest, KostenAnschaffung, KostenArtikel, KostenNachfuellung, KostenSeite, StromQuelle, Zaehlerstand } from '../features/kosten/kosten-typen'
import { formatDate, formatDateTime, formatNumber, toLocalInputValue } from '../utils'
import { feldText, istLeer, istUnlesbar, zahlOderNull } from '../zahlenfeld'
import { phaseName } from '../deutsche-woerter'
import '../features/kosten/kosten.css'

/**
 * Fork AI (forkai.6): Was der laufende Grow kostet — Strom und das, was
 * aufgebraucht wird.
 *
 * <b>Der Anlass (07.09.2026).</b> Eine neue 10-kg-CO₂-Flasche. Die Frage war
 * nicht „wo notiere ich das“, sondern „wie lange hält sie und was kostet mich
 * das je Tag“. Dafür gab es keinen Ort: das Archiv rechnet den Strom aus
 * Lampen-Watt, und ein Journal-Eintrag weiß nichts von der Flasche davor.
 *
 * <b>Zwei Quellen, ehrlich benannt.</b> Der Strom kommt vom kWh-Zähler in Home
 * Assistant (die DECT-Steckdose vor dem Zelt), festgehalten bei Grow-Start,
 * jedem Phasenwechsel und einmal am Tag. Die Verbrauchsartikel kommen von
 * Hand: Datum, Menge, Preis. Alles andere — Laufzeit, Prognose, Euro je Tag —
 * ist Rechnung und steht als solche da.
 */
type Reiter = 'strom' | 'verbrauch' | 'anschaffungen' | 'durchgaenge'
const REITER: Reiter[] = ['strom', 'verbrauch', 'anschaffungen', 'durchgaenge']
type Formular = 'artikel' | 'nachfuellung' | 'anschaffung'
/** Welches Formular auf welchem Reiter wohnt. */
const FORMULAR_REITER: Record<Formular, Reiter> = { artikel: 'verbrauch', nachfuellung: 'verbrauch', anschaffung: 'anschaffungen' }

function KostenPage() {
  const [params, setParams] = useSearchParams()
  const angefragt = params.get('tab')
  const reiter: Reiter = REITER.includes(angefragt as Reiter) ? (angefragt as Reiter) : 'strom'
  const [growId, setGrowId] = useState<number | null>(null)
  const [seite, setSeite] = useState<KostenSeite | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [refresh, setRefresh] = useState(0)
  // Mehrere Formulare dürfen gleichzeitig offen sein — jedes auf seinem Reiter.
  const [offen, setOffen] = useState<Set<Formular>>(() => new Set())
  const [nachfuellungArtikelId, setNachfuellungArtikelId] = useState<number | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    async function load() {
      try {
        const query = growId != null ? `?growId=${growId}` : ''
        const geladen = await apiFetch<KostenSeite>(`/api/kosten${query}`, { signal: controller.signal })
        if (!controller.signal.aborted) { setSeite(geladen); setError(null) }
      } catch (caught) {
        if (!controller.signal.aborted) setError(formatApiError(caught, 'Kosten konnten nicht geladen werden.'))
      } finally {
        if (!controller.signal.aborted) setLoading(false)
      }
    }
    void load()
    return () => controller.abort()
  }, [growId, refresh])

  const neuLaden = useCallback((text?: string) => {
    if (text) setNotice(text)
    setRefresh((n) => n + 1)
  }, [])

  function reiterWechseln(ziel: Reiter) {
    const next = new URLSearchParams(params)
    next.set('tab', ziel)
    // replace: Reiterwechsel soll den Zurück-Knopf nicht vollmüllen.
    setParams(next, { replace: true })
  }

  /** Die drei Knöpfe oben: ein Tipp wechselt auf den Reiter und öffnet das Formular, ein zweiter schließt es. */
  function formularUmschalten(f: Formular, artikelId?: number) {
    const ziel = FORMULAR_REITER[f]
    if (artikelId != null) setNachfuellungArtikelId(artikelId)
    setOffen((alt) => {
      const neu = new Set(alt)
      if (neu.has(f) && reiter === ziel && artikelId == null) neu.delete(f)
      else neu.add(f)
      return neu
    })
    if (reiter !== ziel) reiterWechseln(ziel)
  }
  function formularSchliessen(f: Formular) {
    setOffen((alt) => { const neu = new Set(alt); neu.delete(f); return neu })
  }

  const knopf = (f: Formular, text: string, audit: string) => (
    <V1Button
      variant={offen.has(f) ? 'primary' : 'secondary'}
      onClick={() => formularUmschalten(f)}
      disabled={!seite || (f === 'nachfuellung' && seite.artikel.length === 0)}
      audit={audit}
    >
      {text}{offen.has(f) ? ' ▴' : ''}
    </V1Button>
  )

  return (
    <V1Page
      eyebrow="Betrieb / Kosten"
      title="Kosten"
      subtitle="Strom vom Zähler, was nachgekauft wird und was angeschafft wurde — je Durchgang, je Tag, je Pflanze. Prognosen sind Rechnung aus der Vergangenheit, keine Messung."
      action={
        <div className="v1-action-row">
          {knopf('artikel', 'Artikel anlegen', 'kosten-artikel-anlegen')}
          {knopf('nachfuellung', 'Nachfüllung erfassen', 'kosten-nachfuellung-erfassen')}
          {knopf('anschaffung', 'Anschaffung erfassen', 'kosten-anschaffung-erfassen')}
        </div>
      }
    >
      {error && <V1Alert message={error} tone="critical" />}
      {notice && <V1Alert message={notice} tone="ok" />}

      {loading || !seite ? (
        <V1Skeleton tiles={4} label="Lade Kosten" />
      ) : (
        <>
          <Zusammenfassung seite={seite} />

          <V1Tabs
            items={[
              { value: 'strom' as Reiter, label: 'Strom', meta: seite.strom.eurSeitStart != null ? euro(seite.strom.eurSeitStart) : null, audit: 'kosten-tab-strom' },
              { value: 'verbrauch' as Reiter, label: 'Verbrauch', meta: `${seite.artikel.length}`, audit: 'kosten-tab-verbrauch' },
              { value: 'anschaffungen' as Reiter, label: 'Anschaffungen', meta: `${seite.anschaffungen.length}`, audit: 'kosten-tab-anschaffungen' },
              { value: 'durchgaenge' as Reiter, label: 'Durchgänge', meta: `${seite.durchgaenge.filter((d) => d.laeuft).length}`, audit: 'kosten-tab-durchgaenge' },
            ]}
            active={reiter}
            onChange={reiterWechseln}
            label="Bereich"
          />

          {reiter === 'strom' && <StromAbschnitt seite={seite} onChanged={neuLaden} onError={setError} />}

          {reiter === 'verbrauch' && (
            <>
              {offen.has('artikel') && (
                <FormularHuelle titel="Artikel anlegen" onClose={() => formularSchliessen('artikel')}>
                  <ArtikelForm seite={seite} onDone={(text) => { formularSchliessen('artikel'); neuLaden(text) }} onError={setError} />
                </FormularHuelle>
              )}
              {offen.has('nachfuellung') && seite.artikel.length > 0 && (
                <FormularHuelle titel="Nachfüllung erfassen" onClose={() => formularSchliessen('nachfuellung')}>
                  <NachfuellungForm
                    seite={seite}
                    vorbelegtArtikelId={nachfuellungArtikelId ?? seite.artikel[0].id}
                    onDone={(text) => { formularSchliessen('nachfuellung'); neuLaden(text) }}
                    onCancel={() => formularSchliessen('nachfuellung')}
                    onError={setError}
                  />
                </FormularHuelle>
              )}

              <V1Section title="Verbrauchsartikel" action={<V1Button onClick={() => formularUmschalten('artikel')}>Artikel anlegen</V1Button>}>
                {seite.artikel.length === 0 ? (
                  <V1Card>
                    <V1Empty
                      title="Noch kein Verbrauchsartikel."
                      text="Ein Artikel ist etwas, das leer wird und nachgekauft wird — CO₂-Flasche, Dünger, pH-Down. Lege ihn an, dann erfasst du jede Füllung mit Datum, Menge und Preis."
                    />
                  </V1Card>
                ) : (
                  <div className="co-grid" data-audit="kosten-artikel">
                    {seite.artikel.map((artikel) => (
                      <ArtikelKarte
                        key={artikel.id}
                        artikel={artikel}
                        seite={seite}
                        onErfassen={() => formularUmschalten('nachfuellung', artikel.id)}
                        onChanged={neuLaden}
                        onError={setError}
                      />
                    ))}
                  </div>
                )}
              </V1Section>

              <NachfuellungenTabelle liste={seite.nachfuellungen} onChanged={neuLaden} onError={setError} />
            </>
          )}

          {reiter === 'anschaffungen' && (
            <>
              {offen.has('anschaffung') && (
                <FormularHuelle titel="Anschaffung erfassen" onClose={() => formularSchliessen('anschaffung')}>
                  <AnschaffungForm seite={seite} onDone={(text) => { formularSchliessen('anschaffung'); neuLaden(text) }} onCancel={() => formularSchliessen('anschaffung')} onError={setError} />
                </FormularHuelle>
              )}
              <AnschaffungenTabelle seite={seite} onErfassen={() => formularUmschalten('anschaffung')} onChanged={neuLaden} onError={setError} />
            </>
          )}

          {reiter === 'durchgaenge' && <Durchgaenge seite={seite} aktiv={growId} onWahl={setGrowId} />}
        </>
      )}
    </V1Page>
  )
}

/** Ein offenes Formular: Kopfzeile mit Titel und ▴ zum Einklappen, rollt beim Öffnen ins Bild. */
function FormularHuelle({ titel, onClose, children }: { titel: string; onClose: () => void; children: ReactNode }) {
  const huelle = useRef<HTMLDivElement>(null)
  // Nicht sofort beim Mount: wenn der Klick gleichzeitig den Reiter wechselt,
  // rendert der Router den neuen Reiter erst im nächsten Zug — ein sofortiger
  // Sprung landet dann im Leeren (Bru, 09.09.: „erst der zweite Klick springt").
  // Zwei Frames später steht die Seite, dann rollen wir.
  useEffect(() => {
    let raf2 = 0
    const raf1 = requestAnimationFrame(() => {
      raf2 = requestAnimationFrame(() => huelle.current?.scrollIntoView({ block: 'start', behavior: 'smooth' }))
    })
    return () => { cancelAnimationFrame(raf1); cancelAnimationFrame(raf2) }
  }, [])
  return (
    <section className="v1-section scroll-ziel" ref={huelle}>
      <header className="v1-section-head">
        <h2>{titel}</h2>
        <V1Button variant="ghost" onClick={onClose} audit={`kosten-form-zu`}>▴</V1Button>
      </header>
      <div className="v1-section-body">{children}</div>
    </section>
  )
}

// ------------------------------------------------------------- Kopf

function Zusammenfassung({ seite }: { seite: KostenSeite }) {
  const { grow, summe } = seite
  const strom = summe.stromEur ?? 0
  const artikel = summe.artikelEur
  const anschaffungen = summe.anschaffungenEur
  const gesamt = summe.gesamtEur
  const stromAnteil = gesamt > 0 ? (strom / gesamt) * 100 : 0
  const artikelAnteil = gesamt > 0 ? (artikel / gesamt) * 100 : 0
  const anschaffungenAnteil = gesamt > 0 ? (anschaffungen / gesamt) * 100 : 0

  return (
    <>
      <V1Section title={grow ? `Durchgang ${grow.name}` : 'Kein laufender Grow'}>
        <V1Card className="ko-hero">
          {grow ? (
            <p>
              <Link to={`/grows/${grow.id}`}>{grow.name}</Link> · {grow.phase} · Tag {grow.tag} · seit {formatDate(grow.startDate)}
              {grow.endDate && <> · beendet {formatDate(grow.endDate)}</>}
            </p>
          ) : (
            <p>Ohne laufenden Grow gibt es nichts zu summieren. Die Artikel und Zählerstände bleiben erhalten.</p>
          )}

          <div className="ko-gesamt" data-audit="kosten-gesamt">
            <strong>{euro(gesamt)}</strong>
            <span>seit Start{summe.proTagEur != null && <> · Ø {euro(summe.proTagEur)} je Tag</>}</span>
          </div>

          <div className="ko-split" role="img" aria-label={`Strom ${formatNumber(stromAnteil, 0)} %, Verbrauchsartikel ${formatNumber(artikelAnteil, 0)} %, Anschaffungen ${formatNumber(anschaffungenAnteil, 0)} %`}>
            <i className="is-strom" style={{ width: `${stromAnteil}%` }} />
            <i className="is-artikel" style={{ width: `${artikelAnteil}%` }} />
            <i className="is-anschaffung" style={{ width: `${anschaffungenAnteil}%` }} />
          </div>
          <div className="ko-legende">
            <span className="is-strom">Strom {euro(summe.stromEur)}</span>
            <span className="is-artikel">Verbrauchsartikel {euro(artikel)}</span>
            <span className="is-anschaffung">Anschaffungen {euro(anschaffungen)}</span>
          </div>
          {summe.prognoseErnteEur != null && (
            <p>Prognose bis zur Ernte ≈ {euro(summe.prognoseErnteEur)} — {summe.prognoseHinweis}</p>
          )}
        </V1Card>
      </V1Section>

      {/* Fakten-Leiste wie im Grow-Detail: dieselbe Leiste, dieselben Linien. */}
      <section className="v1-kpi-grid" data-audit="kosten-summe">
        <V1Stat label="Strom" value={euro(summe.stromEur)} hint={seite.strom.kwhSeitStart != null ? `${formatNumber(seite.strom.kwhSeitStart, 0)} kWh` : seite.strom.eingerichtet ? 'noch keine Differenz' : 'keine Quelle'} />
        <V1Stat label="Verbrauchsartikel" value={euro(artikel)} hint={`${seite.nachfuellungen.filter((f) => grow && f.growId === grow.id).length} Nachfüllungen`} />
        <V1Stat label="Anschaffungen" value={euro(anschaffungen)} hint={`${seite.anschaffungen.filter((x) => grow && x.growId === grow.id).length} Positionen im Grow`} />
        <V1Stat label="Je Pflanze" value={euro(summe.proPflanzeEur)} hint={grow?.pflanzen ? `${grow.pflanzen} Pflanzen, bisher` : 'Pflanzenzahl im Grow eintragen'} />
      </section>
    </>
  )
}

// ------------------------------------------------------------- Strom

function StromAbschnitt({ seite, onChanged, onError }: { seite: KostenSeite; onChanged: (text?: string) => void; onError: (text: string) => void }) {
  const { strom } = seite
  const [quelleOffen, setQuelleOffen] = useState(!strom.eingerichtet)

  return (
    <V1Section title="Strom" action={<V1Button onClick={() => setQuelleOffen((v) => !v)} audit="kosten-strom-quelle">{quelleOffen ? 'Quelle schließen' : 'Strom-Quelle einstellen'}</V1Button>}>
      <div className="ko-stapel">
        <section className="v1-kpi-grid" data-audit="kosten-strom">
          <V1Stat label="Leistung jetzt" value={strom.leistungW != null ? formatNumber(strom.leistungW, 0) : '–'} unit="W" hint={strom.leistungEntityId ? (strom.leistungW != null ? 'aus Home Assistant' : 'kein Wert von Home Assistant') : 'keine Leistungs-Entität gewählt'} />
          <V1Stat label="Verbrauch" value={strom.kwhSeitStart != null ? formatNumber(strom.kwhSeitStart, 0) : '–'} unit="kWh" hint="seit Start des Grows" />
          <V1Stat label="Ø je Tag" value={strom.kwhProTag != null ? formatNumber(strom.kwhProTag, 1) : '–'} unit="kWh" hint={strom.eurProTag != null ? `${euro(strom.eurProTag)} je Tag` : null} />
          <V1Stat label="Preis" value={strom.preisCentProKwh != null ? formatNumber(strom.preisCentProKwh / 100, 2) : '–'} unit="€/kWh" hint={strom.preisCentProKwh != null ? 'aus den Einstellungen' : 'in den Einstellungen hinterlegen'} />
        </section>

        <p className="ko-hint">{strom.hinweis}{strom.preisCentProKwh == null && <> <Link to="/einstellungen">Zu den Einstellungen.</Link></>}</p>

        {strom.phasen.length > 0 && (
          <div className="ko-tabelle-huelle">
            <table className="ko-tabelle" data-audit="kosten-phasen">
              <thead>
                <tr>
                  <th scope="col">Phase</th>
                  <th scope="col">Dauer</th>
                  <th scope="col">kWh</th>
                  <th scope="col">kWh/Tag</th>
                  <th scope="col">Kosten</th>
                </tr>
              </thead>
              <tbody>
                {strom.phasen.map((p, i) => (
                  <tr key={`${p.phase}-${i}`} className={p.laeuft ? 'is-aktuell' : undefined}>
                    <th scope="row">{p.label}{p.laeuft && <span className="ls-pill">läuft</span>}</th>
                    <td>{tage(p.tage)}</td>
                    <td>{formatNumber(p.kwh, 0)}</td>
                    <td>{p.tage >= 0.5 ? formatNumber(p.kwh / p.tage, 1) : '–'}</td>
                    <td>{euro(p.eur)}</td>
                  </tr>
                ))}
                {strom.kwhSeitStart != null && (
                  <tr className="is-summe">
                    <th scope="row">Gesamt</th>
                    <td>{strom.ersterStandUtc && strom.letzterStandUtc ? tage((new Date(strom.letzterStandUtc).getTime() - new Date(strom.ersterStandUtc).getTime()) / 86_400_000) : '–'}</td>
                    <td>{formatNumber(strom.kwhSeitStart, 0)}</td>
                    <td>{strom.kwhProTag != null ? formatNumber(strom.kwhProTag, 1) : '–'}</td>
                    <td>{euro(strom.eurSeitStart)}</td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        )}

        {strom.zaehlerStart != null && (
          <p className="ko-hint">
            Zähler bei Grow-Start {formatNumber(strom.zaehlerStart, 1)} kWh ({formatDateTime(strom.ersterStandUtc)}), zuletzt {formatNumber(strom.zaehlerAktuell, 1)} kWh ({formatDateTime(strom.letzterStandUtc)}).
          </p>
        )}

        {quelleOffen && <StromQuelleForm quelle={{ zaehlerEntityId: strom.zaehlerEntityId, leistungEntityId: strom.leistungEntityId }} onChanged={(text) => { setQuelleOffen(false); onChanged(text) }} onError={onError} />}
      </div>
    </V1Section>
  )
}

/**
 * Welche Entitäten den Strom liefern. Kein Auswahlmenü über alle HA-Entitäten:
 * das sind bei dieser Anlage über tausend, und die Kennung steht in HA am
 * Gerät. Stattdessen „Prüfen“ — der Wert, den HA gerade meldet, sagt mehr als
 * jede Liste.
 */
function StromQuelleForm({ quelle, onChanged, onError }: { quelle: StromQuelle; onChanged: (text: string) => void; onError: (text: string) => void }) {
  const [zaehler, setZaehler] = useState(quelle.zaehlerEntityId ?? '')
  const [leistung, setLeistung] = useState(quelle.leistungEntityId ?? '')
  const [befund, setBefund] = useState<Record<string, EntitaetTest>>({})
  const [busy, setBusy] = useState(false)
  const [staende, setStaende] = useState<Zaehlerstand[] | null>(null)

  /** Der Stand von jetzt — ohne auf den Takt des Workers zu warten. */
  async function jetztFesthalten() {
    setBusy(true)
    try {
      const stand = await apiFetch<Zaehlerstand | null>('/api/kosten/zaehlerstand', { method: 'POST' })
      if (stand) onChanged(`Zählerstand festgehalten: ${formatNumber(stand.kwh, 1)} kWh.`)
      else onError('Kein Wert vom Zähler — Entität prüfen und Home Assistant-Verbindung ansehen.')
    } catch (caught) {
      onError(formatApiError(caught, 'Zählerstand konnte nicht festgehalten werden.'))
    } finally {
      setBusy(false)
    }
  }

  async function staendeLaden() {
    try {
      setStaende(await apiFetch<Zaehlerstand[]>('/api/kosten/zaehlerstaende'))
    } catch (caught) {
      onError(formatApiError(caught, 'Zählerstände konnten nicht geladen werden.'))
    }
  }

  async function pruefen(entityId: string) {
    const id = entityId.trim()
    if (!id) return
    setBusy(true)
    try {
      const test = await apiFetch<EntitaetTest>(`/api/kosten/entitaet?entityId=${encodeURIComponent(id)}`)
      setBefund((alt) => ({ ...alt, [id]: test }))
    } catch (caught) {
      onError(formatApiError(caught, 'Entität konnte nicht geprüft werden.'))
    } finally {
      setBusy(false)
    }
  }

  async function speichern() {
    setBusy(true)
    try {
      await apiFetch<StromQuelle>('/api/kosten/strom-quelle', {
        method: 'PUT',
        body: JSON.stringify({ zaehlerEntityId: zaehler.trim() || null, leistungEntityId: leistung.trim() || null }),
      })
      onChanged(zaehler.trim() ? 'Strom-Quelle gespeichert — der erste Zählerstand ist festgehalten.' : 'Strom-Quelle entfernt.')
    } catch (caught) {
      onError(formatApiError(caught, 'Strom-Quelle konnte nicht gespeichert werden.'))
    } finally {
      setBusy(false)
    }
  }

  function befundText(id: string): string | null {
    const b = befund[id.trim()]
    if (!b) return null
    if (!b.gefunden) return 'In Home Assistant nicht gefunden.'
    const wert = b.wert != null ? `${formatNumber(b.wert, 2)}${b.einheit ? ` ${b.einheit}` : ''}` : (b.state ?? '–')
    return `${b.name ?? b.entityId}: ${wert}`
  }

  return (
    <V1Card className="ko-quelle"><div className="ko-form-inhalt" data-audit="kosten-strom-quelle-form">
      <div className="v1-form-grid">
        <V1Field label="kWh-Zähler (Entität)" hint={befundText(zaehler) ?? 'Gesamtzähler der Steckdose vor dem Zelt, z. B. sensor.…_total_energy. Muss nur steigen; ein Reset wird erkannt.'} wide>
          <input type="text" value={zaehler} onChange={(e) => setZaehler(e.target.value)} placeholder="sensor.fritz_dect_210_1_total_energy" spellCheck={false} />
        </V1Field>
        <V1Field label="Leistung (Entität, optional)" hint={befundText(leistung) ?? 'Nur für die Anzeige „Leistung jetzt“.'} wide>
          <input type="text" value={leistung} onChange={(e) => setLeistung(e.target.value)} placeholder="sensor.fritz_dect_210_1_power_consumption" spellCheck={false} />
        </V1Field>
      </div>
      <div className="v1-form-actions">
        <V1Button onClick={() => { void pruefen(zaehler); void pruefen(leistung) }} disabled={busy || (!zaehler.trim() && !leistung.trim())}>Prüfen</V1Button>
        <V1Button variant="primary" onClick={() => void speichern()} disabled={busy} audit="kosten-strom-quelle-speichern">Speichern</V1Button>
        {quelle.zaehlerEntityId && <V1Button onClick={() => void jetztFesthalten()} disabled={busy} audit="kosten-zaehlerstand-jetzt">Zählerstand jetzt festhalten</V1Button>}
        {quelle.zaehlerEntityId && <V1Button variant="ghost" onClick={() => void staendeLaden()} disabled={busy}>Zählerstände anzeigen</V1Button>}
        <span className="ko-hint">Der Preis je kWh steht in den <Link to="/einstellungen">Einstellungen</Link> — derselbe wie im Archiv.</span>
      </div>
      {staende && (
        <div className="ko-tabelle-huelle">
          <table className="ko-tabelle" data-audit="kosten-zaehlerstaende">
            <thead><tr><th scope="col">Zeitpunkt</th><th scope="col">kWh</th><th scope="col">Anlass</th><th scope="col">Phase</th></tr></thead>
            <tbody>
              {[...staende].reverse().slice(0, 30).map((z) => (
                <tr key={z.id}>
                  <td>{formatDateTime(z.zeitpunktUtc)}</td>
                  <td>{formatNumber(z.kwh, 1)}</td>
                  <th scope="row">{anlassText(z.anlass)}</th>
                  <td>{z.phase ? phaseName(z.phase) : '–'}</td>
                </tr>
              ))}
              {staende.length === 0 && <tr><td colSpan={4}>Noch kein Stand festgehalten.</td></tr>}
            </tbody>
          </table>
        </div>
      )}
      </div>
    </V1Card>
  )
}

/** Die Anlässe aus `ZaehlerAnlass` — deutsch, wie alles andere hier. */
function anlassText(anlass: Zaehlerstand['anlass']): string {
  switch (anlass) {
    case 'GrowStart': return 'Grow-Start'
    case 'Phase': return 'Phasenwechsel'
    case 'Manuell': return 'von Hand'
    default: return 'Tagestakt'
  }
}

// ------------------------------------------------------ Verbrauchsartikel

function ArtikelKarte({ artikel, seite, onErfassen, onChanged, onError }: { artikel: KostenArtikel; seite: KostenSeite; onErfassen: () => void; onChanged: (text?: string) => void; onError: (text: string) => void }) {
  const [busy, setBusy] = useState(false)
  const [bearbeiten, setBearbeiten] = useState(false)
  const a = artikel.aktuell

  if (bearbeiten) {
    return <ArtikelForm artikel={artikel} seite={seite} onDone={(text) => { setBearbeiten(false); onChanged(text) }} onError={onError} onCancel={() => setBearbeiten(false)} />
  }

  async function leerMarkieren() {
    if (!a) return
    if (!window.confirm(`„${artikel.name}“ jetzt als leer markieren? Die Laufzeit dieser Füllung wird damit festgeschrieben.`)) return
    setBusy(true)
    try {
      await apiFetch(`/api/kosten/nachfuellungen/${a.id}/leer`, { method: 'POST', body: JSON.stringify({}) })
      onChanged(`${artikel.name}: Füllung als leer markiert.`)
    } catch (caught) {
      onError(formatApiError(caught, 'Als leer markieren fehlgeschlagen.'))
    } finally {
      setBusy(false)
    }
  }

  async function loeschen() {
    if (!window.confirm(`„${artikel.name}“ wirklich löschen? Alle ${artikel.anzahlFuellungen} Nachfüllungen dazu gehen mit verloren.`)) return
    setBusy(true)
    try {
      await apiFetch(`/api/kosten/artikel/${artikel.id}`, { method: 'DELETE' })
      onChanged(`${artikel.name} gelöscht.`)
    } catch (caught) {
      onError(formatApiError(caught, 'Löschen fehlgeschlagen.'))
    } finally {
      setBusy(false)
    }
  }

  return (
    <V1Card className={`ko-artikel${a ? ' is-offen' : ''}`}>
      <div className="ko-artikel-inhalt" data-audit="kosten-artikel-karte">
      <div className="ko-artikel-kopf">
        <strong>{artikel.name}</strong>
        {a ? <span className="ls-pill">Tag {a.tag}{a.prognoseTage != null && <> von ≈ {Math.round(a.prognoseTage)}</>}</span> : <span className="ls-pill is-plan">leer</span>}
      </div>
      {(artikel.hersteller || artikel.produkt || artikel.preisEur != null) && (
        <p className="ko-artikel-fakten">
          {[artikel.hersteller, artikel.produkt].filter(Boolean).join(' · ')}
          {artikel.preisEur != null && <>{(artikel.hersteller || artikel.produkt) ? ' · ' : ''}{euro(artikel.preisEur)} je {artikel.gebinde != null ? `${formatNumber(artikel.gebinde, 2)} ${artikel.einheit}` : 'Packung'}</>}
        </p>
      )}

      {a ? (
        <>
          <p className="ko-artikel-fakten">
            {formatNumber(a.menge, 2)} {artikel.einheit} seit {formatDate(a.zeitpunktUtc)}
            {a.kostenEur != null && <> · {euro(a.kostenEur)}</>}
            {a.eurProTag != null && <> · {euro(a.eurProTag)} je Tag</>}
          </p>
          {a.fuellstandProzent != null ? (
            <>
              <div className="ko-balken" role="img" aria-label={`Geschätzt noch ${formatNumber(a.fuellstandProzent, 0)} %`}><i style={{ width: `${a.fuellstandProzent}%` }} /></div>
              <p className="ko-artikel-prognose">leer ≈ {formatDate(a.prognoseLeerAmUtc)} — geschätzt aus den letzten Laufzeiten (Ø {tage(artikel.mittlereLaufzeitTage)}), nicht gewogen.</p>
            </>
          ) : (
            <p className="ko-artikel-prognose">Erste Füllung — eine Prognose gibt es, sobald eine Füllung als leer markiert wurde.</p>
          )}
        </>
      ) : (
        <p className="ko-artikel-fakten">
          {artikel.anzahlFuellungen === 0 ? 'Noch keine Füllung erfasst.' : `${artikel.anzahlFuellungen} Füllungen bisher, Ø ${tage(artikel.mittlereLaufzeitTage)}.`}
        </p>
      )}

      <div className="ko-artikel-aktionen">
        <button type="button" className="ls-btn is-small is-primary" disabled={busy} onClick={onErfassen}>Nachfüllung erfassen</button>
        {a && <button type="button" className="ls-btn is-small" disabled={busy} onClick={() => void leerMarkieren()}>Als leer markieren</button>}
        <button type="button" className="ls-btn is-small" disabled={busy} onClick={() => setBearbeiten(true)}>Bearbeiten</button>
        <button type="button" className="ls-btn is-small is-ghost" disabled={busy} onClick={() => void loeschen()}>Löschen</button>
      </div>
      </div>
    </V1Card>
  )
}

/**
 * Hersteller und Produkt mit Vorschlägen aus dem Bestand (forkai.11). Native
 * `<datalist>`: tippt man „c", bietet der Browser „Canna" an — ohne eigenes
 * Dropdown, funktioniert in der HA-App am Telefon. Beim Speichern gleicht das
 * Backend die Schreibweise zusätzlich an („canna" → „Canna"), damit aus einem
 * Tippfehler kein zweiter Hersteller wird.
 */
function HerstellerProduktFelder({ id, seite, hersteller, produkt, onHersteller, onProdukt, herstellerPlatzhalter, produktPlatzhalter }: {
  id: string
  seite: KostenSeite
  hersteller: string
  produkt: string
  onHersteller: (v: string) => void
  onProdukt: (v: string) => void
  herstellerPlatzhalter?: string
  produktPlatzhalter?: string
}) {
  const h = hersteller.trim().toLowerCase()
  // Produkte des getippten Herstellers zuerst; ohne Hersteller alle.
  const produkte = h
    ? seite.produkte.filter((p) => (p.hersteller ?? '').toLowerCase() === h)
    : seite.produkte
  return (
    <>
      <V1Field label="Hersteller" hint={seite.hersteller.length > 0 ? 'bekannte Hersteller werden beim Tippen vorgeschlagen' : undefined}>
        <input type="text" list={`${id}-hersteller`} value={hersteller} onChange={(e) => onHersteller(e.target.value)} placeholder={herstellerPlatzhalter} autoComplete="off" />
        <datalist id={`${id}-hersteller`}>
          {seite.hersteller.map((x) => <option key={x} value={x} />)}
        </datalist>
      </V1Field>
      <V1Field label="Produktbezeichnung">
        <input type="text" list={`${id}-produkt`} value={produkt} onChange={(e) => onProdukt(e.target.value)} placeholder={produktPlatzhalter} autoComplete="off" />
        <datalist id={`${id}-produkt`}>
          {(produkte.length > 0 ? produkte : seite.produkte).map((p) => <option key={`${p.hersteller ?? ''}|${p.produkt}`} value={p.produkt}>{p.hersteller ?? undefined}</option>)}
        </datalist>
      </V1Field>
    </>
  )
}

/** Anlegen oder — mit `artikel` — Bearbeiten; dieselben Felder, derselbe Vertrag. */
function ArtikelForm({ artikel, seite, onDone, onError, onCancel }: { artikel?: KostenArtikel; seite: KostenSeite; onDone: (text: string) => void; onError: (text: string) => void; onCancel?: () => void }) {
  const einheiten = seite.einheiten
  const [name, setName] = useState(artikel?.name ?? '')
  const [hersteller, setHersteller] = useState(artikel?.hersteller ?? '')
  const [produkt, setProdukt] = useState(artikel?.produkt ?? '')
  const [einheit, setEinheit] = useState(artikel?.einheit ?? einheiten[0] ?? 'kg')
  const [gebinde, setGebinde] = useState(feldText(artikel?.gebinde))
  const [preis, setPreis] = useState(feldText(artikel?.preisEur))
  const [notiz, setNotiz] = useState(artikel?.notiz ?? '')
  const [busy, setBusy] = useState(false)

  async function speichern() {
    if (istUnlesbar(gebinde)) { onError('Gebindegröße ist keine Zahl.'); return }
    if (istUnlesbar(preis)) { onError('Preis ist keine Zahl.'); return }
    setBusy(true)
    try {
      await apiFetch(artikel ? `/api/kosten/artikel/${artikel.id}` : '/api/kosten/artikel', {
        method: artikel ? 'PUT' : 'POST',
        body: JSON.stringify({
          name: name.trim(),
          hersteller: hersteller.trim() || null,
          produkt: produkt.trim() || null,
          preisEur: zahlOderNull(preis),
          einheit: einheit.trim() || 'kg',
          gebinde: zahlOderNull(gebinde),
          notiz: notiz.trim() || null,
          aktiv: artikel?.aktiv ?? true,
        }),
      })
      onDone(artikel ? `${name.trim()} gespeichert.` : `${name.trim()} angelegt — jetzt die erste Füllung erfassen.`)
    } catch (caught) {
      onError(formatApiError(caught, 'Artikel konnte nicht angelegt werden.'))
    } finally {
      setBusy(false)
    }
  }

  return (
    <V1Card className="ko-form"><div className="ko-form-inhalt" data-audit="kosten-artikel-form">
      <div className="v1-form-grid">
        <V1Field label="Anzeigename" hint="so heißt der Artikel in Karten, Tabellen und im Journal" wide>
          <input type="text" value={name} onChange={(e) => setName(e.target.value)} placeholder="CO₂-Flasche 10 kg" />
        </V1Field>
        <HerstellerProduktFelder id={`artikel-${artikel?.id ?? 'neu'}`} seite={seite} hersteller={hersteller} produkt={produkt} onHersteller={setHersteller} onProdukt={setProdukt} herstellerPlatzhalter="Linde, Canna …" produktPlatzhalter="Kohlendioxid E290, Aqua Vega A …" />
        <V1Field label="Einheit" hint="wie du die Menge nennst">
          <select value={einheit} onChange={(e) => setEinheit(e.target.value)}>
            {einheiten.map((e) => <option key={e} value={e}>{e}</option>)}
          </select>
        </V1Field>
        <V1Field label="Inhalt je Packung" hint="eine volle Flasche, ein Kanister, ein Beutel — belegt die Menge beim Erfassen vor">
          <input type="text" inputMode="decimal" value={gebinde} onChange={(e) => setGebinde(e.target.value)} placeholder="10" />
        </V1Field>
        <V1Field label="Preis je Packung (€)" hint="belegt die Kosten beim Erfassen vor; pro Füllung änderbar">
          <input type="text" inputMode="decimal" value={preis} onChange={(e) => setPreis(e.target.value)} placeholder="36,75" />
        </V1Field>
        <V1Field label="Notiz" wide>
          <input type="text" value={notiz} onChange={(e) => setNotiz(e.target.value)} placeholder="Tauschflasche, Lieferant …" />
        </V1Field>
      </div>
      <div className="v1-form-actions">
        <V1Button variant="primary" onClick={() => void speichern()} disabled={busy || istLeer(name)} audit="kosten-artikel-speichern">{artikel ? 'Speichern' : 'Artikel anlegen'}</V1Button>
        {onCancel && <V1Button onClick={onCancel} disabled={busy}>Abbrechen</V1Button>}
      </div>
      </div>
    </V1Card>
  )
}

/**
 * Eine Füllung erfassen. „Vorherige damit als leer markieren“ ist vorbelegt:
 * wer eine neue Flasche anschließt, hat die alte abgehängt — und genau dieser
 * Zeitpunkt macht aus der alten Füllung eine Laufzeit.
 */
function NachfuellungForm({ seite, vorbelegtArtikelId, onDone, onCancel, onError }: {
  seite: KostenSeite
  vorbelegtArtikelId: number
  onDone: (text: string) => void
  onCancel: () => void
  onError: (text: string) => void
}) {
  const artikel = seite.artikel
  const [artikelId, setArtikelId] = useState<number>(artikel.some((a) => a.id === vorbelegtArtikelId) ? vorbelegtArtikelId : (artikel[0]?.id ?? 0))
  const gewaehlt = artikel.find((a) => a.id === artikelId)
  const [zeitpunkt, setZeitpunkt] = useState(toLocalInputValue())
  const [menge, setMenge] = useState(gewaehlt?.gebinde != null ? feldText(gewaehlt.gebinde) : '')
  const [kosten, setKosten] = useState(gewaehlt?.preisEur != null ? feldText(gewaehlt.preisEur) : '')
  const [notiz, setNotiz] = useState('')
  const [vorherigeLeer, setVorherigeLeer] = useState(true)
  const [journal, setJournal] = useState(true)
  const [busy, setBusy] = useState(false)
  const growOpts = growOptionen(seite)
  const [fuerGrow, setFuerGrow] = useState<string>(seite.grow && growOpts.some((o) => o.value === String(seite.grow!.id)) ? String(seite.grow.id) : (growOpts[0]?.value ?? LAGER))

  useEffect(() => {
    const a = artikel.find((x) => x.id === artikelId)
    if (a?.gebinde != null) setMenge(feldText(a.gebinde))
    if (a?.preisEur != null) setKosten(feldText(a.preisEur))
  }, [artikelId, artikel])

  const mengeZahl = zahlOderNull(menge)
  const kostenZahl = zahlOderNull(kosten)
  const offen = gewaehlt?.aktuell ?? null
  const vorschau = useMemo(() => {
    const teile: string[] = []
    if (mengeZahl != null && mengeZahl > 0 && kostenZahl != null) teile.push(`${euro(kostenZahl / mengeZahl)} je ${gewaehlt?.einheit ?? 'Einheit'}`)
    if (offen && vorherigeLeer) {
      const laufzeit = (new Date(zeitpunkt).getTime() - new Date(offen.zeitpunktUtc).getTime()) / 86_400_000
      if (laufzeit > 0) {
        teile.push(`vorherige Füllung ${tage(laufzeit)}`)
        if (offen.kostenEur != null) teile.push(`${euro(offen.kostenEur / laufzeit)} je Tag`)
      }
    }
    return teile.join(' · ')
  }, [mengeZahl, kostenZahl, offen, vorherigeLeer, zeitpunkt, gewaehlt])

  async function speichern() {
    if (istUnlesbar(menge) || istUnlesbar(kosten)) { onError('Menge oder Kosten sind keine Zahl.'); return }
    if (mengeZahl == null || mengeZahl <= 0) { onError('Menge fehlt.'); return }
    setBusy(true)
    try {
      await apiFetch('/api/kosten/nachfuellungen', {
        method: 'POST',
        body: JSON.stringify({
          artikelId,
          zeitpunkt: zeitpunkt ? new Date(zeitpunkt).toISOString() : null,
          menge: mengeZahl,
          kostenEur: kostenZahl,
          growId: fuerGrow === LAGER ? null : Number(fuerGrow),
          ohneGrow: fuerGrow === LAGER,
          notiz: notiz.trim() || null,
          vorherigeLeer,
          journal: journal && fuerGrow !== LAGER,
        }),
      })
      onDone(`${gewaehlt?.name ?? 'Artikel'}: ${formatNumber(mengeZahl, 2)} ${gewaehlt?.einheit ?? ''} erfasst${kostenZahl != null ? ` für ${euro(kostenZahl)}` : ''}.`)
    } catch (caught) {
      onError(formatApiError(caught, 'Nachfüllung konnte nicht gespeichert werden.'))
    } finally {
      setBusy(false)
    }
  }

  return (
    <V1Card className="ko-form"><div className="ko-form-inhalt" data-audit="kosten-nachfuellung-form">
        <div className="v1-form-grid">
          <V1Field label="Artikel" wide>
            <select value={artikelId} onChange={(e) => setArtikelId(Number(e.target.value))}>
              {artikel.map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}
            </select>
          </V1Field>
          <V1Field label="Zeitpunkt">
            <input type="datetime-local" value={zeitpunkt} onChange={(e) => setZeitpunkt(e.target.value)} />
          </V1Field>
          <V1Field label={`Menge${gewaehlt ? ` (${gewaehlt.einheit})` : ''}`}>
            <input type="text" inputMode="decimal" value={menge} onChange={(e) => setMenge(e.target.value)} placeholder="10" />
          </V1Field>
          <V1Field label="Kosten (€)" hint={gewaehlt?.preisEur != null ? `vorbelegt mit dem Packungspreis ${euro(gewaehlt.preisEur)}` : 'was die Füllung gekostet hat; leer, wenn unbekannt'}>
            <input type="text" inputMode="decimal" value={kosten} onChange={(e) => setKosten(e.target.value)} placeholder="34,90" />
          </V1Field>
          <V1Field label="Für Grow" hint="alle laufenden Grows oder Lager, wenn es noch keinem Durchgang gehört">
            <select value={fuerGrow} onChange={(e) => setFuerGrow(e.target.value)}>
              {growOpts.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
            </select>
          </V1Field>
          <V1Field label="Notiz" wide>
            <input type="text" value={notiz} onChange={(e) => setNotiz(e.target.value)} placeholder="Lieferant, Flaschennummer …" />
          </V1Field>
        </div>

        <label className="ko-check">
          <input type="checkbox" checked={vorherigeLeer} onChange={(e) => setVorherigeLeer(e.target.checked)} disabled={!offen} />
          <span>Vorherige Füllung damit als leer markieren{offen ? ` (seit ${formatDate(offen.zeitpunktUtc)})` : ' — es läuft keine'}</span>
        </label>
        <label className="ko-check">
          <input type="checkbox" checked={journal && fuerGrow !== LAGER} onChange={(e) => setJournal(e.target.checked)} disabled={fuerGrow === LAGER} />
          <span>Journal-Eintrag im gewählten Grow anlegen</span>
        </label>

        {vorschau && <p className="ko-vorschau" data-audit="kosten-vorschau">Ergibt: {vorschau}</p>}

        <div className="v1-form-actions">
          <V1Button variant="primary" onClick={() => void speichern()} disabled={busy || !gewaehlt} audit="kosten-nachfuellung-speichern">Speichern</V1Button>
          <V1Button onClick={onCancel} disabled={busy}>Abbrechen</V1Button>
        </div>
        </div>
      </V1Card>
  )
}

function NachfuellungenTabelle({ liste, onChanged, onError }: { liste: KostenNachfuellung[]; onChanged: (text?: string) => void; onError: (text: string) => void }) {
  const [busy, setBusy] = useState(false)

  async function loeschen(f: KostenNachfuellung) {
    if (!window.confirm(`Nachfüllung ${f.artikelName} vom ${formatDate(f.zeitpunktUtc)} löschen?`)) return
    setBusy(true)
    try {
      await apiFetch(`/api/kosten/nachfuellungen/${f.id}`, { method: 'DELETE' })
      onChanged('Nachfüllung gelöscht.')
    } catch (caught) {
      onError(formatApiError(caught, 'Löschen fehlgeschlagen.'))
    } finally {
      setBusy(false)
    }
  }

  if (liste.length === 0) return null

  return (
    <V1Section title="Nachfüllungen">
      <V1Card className="ko-stapel">
        <div className="ko-tabelle-huelle">
          <table className="ko-tabelle" data-audit="kosten-nachfuellungen">
            <thead>
              <tr>
                <th scope="col">Datum</th>
                <th scope="col">Artikel</th>
                <th scope="col">Menge</th>
                <th scope="col">Kosten</th>
                <th scope="col">Laufzeit</th>
                <th scope="col">€/Tag</th>
                <th scope="col">Grow</th>
                <th scope="col"><span className="sr-only">Aktion</span></th>
              </tr>
            </thead>
            <tbody>
              {liste.map((f) => (
                <tr key={f.id} className={f.leerAmUtc == null ? 'is-aktuell' : undefined}>
                  <td>{formatDate(f.zeitpunktUtc)}</td>
                  <th scope="row">{f.artikelName}{f.notiz && <small>{f.notiz}</small>}</th>
                  <td>{formatNumber(f.menge, 2)} {f.einheit}</td>
                  <td>{euro(f.kostenEur)}</td>
                  <td>{f.leerAmUtc == null ? <span className="ls-pill">läuft</span> : tage(f.laufzeitTage)}</td>
                  <td>{euro(f.eurProTag)}</td>
                  <td>{f.growId != null ? <Link to={`/grows/${f.growId}`}>{f.growName ?? f.growId}</Link> : '–'}</td>
                  <td><button type="button" className="ls-btn is-small is-ghost" disabled={busy} onClick={() => void loeschen(f)}>Löschen</button></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </V1Card>
    </V1Section>
  )
}

// ------------------------------------------------------ Anschaffungen (forkai.9)

/** Anlegen oder — mit `vorhanden` — Bearbeiten einer Anschaffung. */
function AnschaffungForm({ seite, vorhanden, onDone, onCancel, onError }: {
  seite: KostenSeite
  vorhanden?: KostenAnschaffung
  onDone: (text: string) => void
  onCancel: () => void
  onError: (text: string) => void
}) {
  const growOpts = growOptionen(seite)
  const vorbelegtGrow = vorhanden
    ? (vorhanden.growId != null ? String(vorhanden.growId) : LAGER)
    : (seite.grow && growOpts.some((o) => o.value === String(seite.grow!.id)) ? String(seite.grow.id) : (growOpts[0]?.value ?? LAGER))
  const [name, setName] = useState(vorhanden?.name ?? '')
  const [hersteller, setHersteller] = useState(vorhanden?.hersteller ?? '')
  const [produkt, setProdukt] = useState(vorhanden?.produkt ?? '')
  const [datum, setDatum] = useState(vorhanden ? toLocalInputValue(new Date(vorhanden.datumUtc)).slice(0, 10) : toLocalInputValue().slice(0, 10))
  const [stueck, setStueck] = useState(vorhanden ? String(vorhanden.stueck) : '1')
  const [preis, setPreis] = useState(vorhanden ? feldText(vorhanden.einzelpreisEur) : '')
  const [fuerGrow, setFuerGrow] = useState<string>(vorbelegtGrow)
  const [notiz, setNotiz] = useState(vorhanden?.notiz ?? '')
  const [alsHardware, setAlsHardware] = useState(false)
  const [journal, setJournal] = useState(true)
  const [busy, setBusy] = useState(false)

  const stueckZahl = zahlOderNull(stueck)
  const preisZahl = zahlOderNull(preis)
  const gesamt = stueckZahl != null && preisZahl != null ? stueckZahl * preisZahl : null

  async function speichern() {
    if (istUnlesbar(stueck) || istUnlesbar(preis)) { onError('Stück oder Einzelpreis sind keine Zahl.'); return }
    if (stueckZahl == null || stueckZahl < 1) { onError('Stückzahl fehlt.'); return }
    if (preisZahl == null) { onError('Einzelpreis fehlt.'); return }
    setBusy(true)
    try {
      await apiFetch(vorhanden ? `/api/kosten/anschaffungen/${vorhanden.id}` : '/api/kosten/anschaffungen', {
        method: vorhanden ? 'PUT' : 'POST',
        body: JSON.stringify({
          name: name.trim(),
          hersteller: hersteller.trim() || null,
          produkt: produkt.trim() || null,
          datum: datum ? new Date(`${datum}T12:00:00`).toISOString() : null,
          stueck: Math.round(stueckZahl),
          einzelpreisEur: preisZahl,
          growId: fuerGrow === LAGER ? null : Number(fuerGrow),
          ohneGrow: fuerGrow === LAGER,
          notiz: notiz.trim() || null,
          alsHardware: !vorhanden && alsHardware,
          journal: !vorhanden && journal && fuerGrow !== LAGER,
        }),
      })
      onDone(vorhanden ? `${name.trim()} gespeichert.` : `${name.trim()} erfasst${gesamt != null ? ` — ${euro(gesamt)}` : ''}.`)
    } catch (caught) {
      onError(formatApiError(caught, 'Anschaffung konnte nicht gespeichert werden.'))
    } finally {
      setBusy(false)
    }
  }

  return (
    <V1Card className="ko-form"><div className="ko-form-inhalt" data-audit="kosten-anschaffung-form">
      <div className="v1-form-grid">
        <V1Field label="Anzeigename" wide>
          <input type="text" value={name} onChange={(e) => setName(e.target.value)} placeholder="Erntescheren" />
        </V1Field>
        <HerstellerProduktFelder id={`anschaffung-${vorhanden?.id ?? 'neu'}`} seite={seite} hersteller={hersteller} produkt={produkt} onHersteller={setHersteller} onProdukt={setProdukt} herstellerPlatzhalter="Fiskars, AC Infinity …" produktPlatzhalter="Micro-Tip Pruning Snips …" />
        <V1Field label="Datum">
          <input type="date" value={datum} onChange={(e) => setDatum(e.target.value)} />
        </V1Field>
        <V1Field label="Stück">
          <input type="text" inputMode="numeric" value={stueck} onChange={(e) => setStueck(e.target.value)} placeholder="1" />
        </V1Field>
        <V1Field label="Einzelpreis (€)">
          <input type="text" inputMode="decimal" value={preis} onChange={(e) => setPreis(e.target.value)} placeholder="4,90" />
        </V1Field>
        <V1Field label="Für Grow" hint="alle laufenden Grows oder Lager, wenn es noch keinem Durchgang gehört">
          <select value={fuerGrow} onChange={(e) => setFuerGrow(e.target.value)}>
            {growOpts.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
          </select>
        </V1Field>
        <V1Field label="Notiz" wide>
          <input type="text" value={notiz} onChange={(e) => setNotiz(e.target.value)} placeholder="Shop, Bestellnummer …" />
        </V1Field>
      </div>

      {!vorhanden && (
        <>
          <label className="ko-check">
            <input type="checkbox" checked={alsHardware} onChange={(e) => setAlsHardware(e.target.checked)} />
            <span>Auch als Hardware-Artikel anlegen<small>legt unter Sensoren &amp; Wartung einen Eintrag an (Lebensdauer, Wartung) — für Werkzeug meist unnötig, für Technik sinnvoll</small></span>
          </label>
          <label className="ko-check">
            <input type="checkbox" checked={journal && fuerGrow !== LAGER} onChange={(e) => setJournal(e.target.checked)} disabled={fuerGrow === LAGER} />
            <span>Journal-Eintrag im gewählten Grow anlegen</span>
          </label>
        </>
      )}

      {gesamt != null && <p className="ko-vorschau" data-audit="kosten-anschaffung-vorschau">Ergibt: {euro(gesamt)}</p>}

      <div className="v1-form-actions">
        <V1Button variant="primary" onClick={() => void speichern()} disabled={busy || istLeer(name)} audit="kosten-anschaffung-speichern">Speichern</V1Button>
        <V1Button onClick={onCancel} disabled={busy}>Abbrechen</V1Button>
      </div>
      </div>
    </V1Card>
  )
}

function AnschaffungenTabelle({ seite, onErfassen, onChanged, onError }: { seite: KostenSeite; onErfassen: () => void; onChanged: (text?: string) => void; onError: (text: string) => void }) {
  const [busy, setBusy] = useState(false)
  const [bearbeiten, setBearbeiten] = useState<KostenAnschaffung | null>(null)
  const liste = seite.anschaffungen
  const imGrow = seite.grow ? liste.filter((a) => a.growId === seite.grow!.id) : []

  async function loeschen(a: KostenAnschaffung) {
    if (!window.confirm(`„${a.name}“ vom ${formatDate(a.datumUtc)} löschen?${a.hardwareItemId != null ? ' Der Hardware-Artikel dazu bleibt bestehen.' : ''}`)) return
    setBusy(true)
    try {
      await apiFetch(`/api/kosten/anschaffungen/${a.id}`, { method: 'DELETE' })
      onChanged(`${a.name} gelöscht.`)
    } catch (caught) {
      onError(formatApiError(caught, 'Löschen fehlgeschlagen.'))
    } finally {
      setBusy(false)
    }
  }

  return (
    <V1Section title="Anschaffungen" action={<V1Button onClick={onErfassen}>Anschaffung erfassen</V1Button>}>
      <V1Card className="ko-stapel">
        <p className="ko-hint">Was gekauft wurde und bleibt: Werkzeug, Technik, Zubehör. Wird nicht leer, hat keine Laufzeit — zählt einmal, im Grow, dem du es zuordnest. „Lager“ zählt in keinen Durchgang.</p>
        {bearbeiten && (
          <AnschaffungForm seite={seite} vorhanden={bearbeiten} onDone={(text) => { setBearbeiten(null); onChanged(text) }} onCancel={() => setBearbeiten(null)} onError={onError} />
        )}
        {liste.length === 0 ? (
          <V1Empty title="Noch keine Anschaffung." text="Erfasse Werkzeug oder Technik mit Datum, Stückzahl und Einzelpreis — und ordne es einem Grow zu oder lege es ins Lager." />
        ) : (
          <div className="ko-tabelle-huelle">
            <table className="ko-tabelle" data-audit="kosten-anschaffungen">
              <thead>
                <tr>
                  <th scope="col">Datum</th>
                  <th scope="col">Artikel</th>
                  <th scope="col">Stück</th>
                  <th scope="col">Einzelpreis</th>
                  <th scope="col">Gesamt</th>
                  <th scope="col">Grow</th>
                  <th scope="col"><span className="sr-only">Aktion</span></th>
                </tr>
              </thead>
              <tbody>
                {liste.map((a) => (
                  <tr key={a.id} className={seite.grow && a.growId === seite.grow.id ? 'is-aktuell' : undefined}>
                    <td>{formatDate(a.datumUtc)}</td>
                    <th scope="row">{a.name}{(a.hersteller || a.produkt) && <small>{[a.hersteller, a.produkt].filter(Boolean).join(' · ')}</small>}{a.notiz && <small>{a.notiz}</small>}</th>
                    <td>{a.stueck}</td>
                    <td>{euro(a.einzelpreisEur)}</td>
                    <td>{euro(a.gesamtEur)}</td>
                    <td>{a.growId != null ? <Link to={`/grows/${a.growId}`}>{a.growName ?? a.growId}</Link> : <span className="ls-pill is-plan">Lager</span>}</td>
                    <td>
                      <button type="button" className="ls-btn is-small" disabled={busy} onClick={() => setBearbeiten(a)}>Bearbeiten</button>{' '}
                      <button type="button" className="ls-btn is-small is-ghost" disabled={busy} onClick={() => void loeschen(a)}>Löschen</button>
                    </td>
                  </tr>
                ))}
                {seite.grow && imGrow.length > 0 && (
                  <tr className="is-summe">
                    <td></td>
                    <th scope="row">Summe im Durchgang {seite.grow.name}</th>
                    <td>{imGrow.reduce((n, a) => n + a.stueck, 0)}</td>
                    <td></td>
                    <td>{euro(seite.summe.anschaffungenEur)}</td>
                    <td></td>
                    <td></td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        )}
      </V1Card>
    </V1Section>
  )
}

// -------------------------------------------------------- Durchgänge

function Durchgaenge({ seite, aktiv, onWahl }: { seite: KostenSeite; aktiv: number | null; onWahl: (growId: number | null) => void }) {
  const liste = seite.durchgaenge.filter((d) => d.gesamtEur != null || d.laeuft)
  if (liste.length === 0) return null
  const gezeigt = aktiv ?? seite.grow?.id ?? null

  return (
    <V1Section title="Durchgänge">
      <V1Card className="ko-stapel">
        <div className="ko-tabelle-huelle">
          <table className="ko-tabelle" data-audit="kosten-durchgaenge">
            <thead>
              <tr>
                <th scope="col">Grow</th>
                <th scope="col">Zeitraum</th>
                <th scope="col">Strom</th>
                <th scope="col">Verbrauch</th>
                <th scope="col">Anschaffungen</th>
                <th scope="col">Gesamt</th>
              </tr>
            </thead>
            <tbody>
              {liste.map((d) => (
                <tr key={d.growId} className={d.growId === gezeigt ? 'is-aktuell' : undefined}>
                  <th scope="row"><button type="button" className="ko-link" onClick={() => onWahl(d.growId)}>{d.name}</button>{d.laeuft && <span className="ls-pill">läuft</span>}</th>
                  <td>{formatDate(d.startDate)} – {d.endDate ? formatDate(d.endDate) : 'heute'}</td>
                  <td>{euro(d.stromEur)}</td>
                  <td>{euro(d.artikelEur)}</td>
                  <td>{euro(d.anschaffungenEur)}</td>
                  <td>{euro(d.gesamtEur)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <p className="ko-hint">Strom gibt es nur für Durchgänge, in deren Laufzeit Zählerstände fallen — also ab dem Tag, an dem die Quelle eingerichtet wurde.</p>
      </V1Card>
    </V1Section>
  )
}

export default KostenPage
