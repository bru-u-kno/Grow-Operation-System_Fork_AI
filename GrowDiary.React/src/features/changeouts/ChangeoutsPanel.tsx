import { useEffect, useState, type FormEvent } from 'react'
import { apiFetch, ApiRequestError } from '../../api'
import type { ChangeoutDto, ChangeoutKind, CreateChangeoutRequest } from '../../types'
import { V1Alert, V1Button, V1Card, V1Empty, V1Field, V1Section, V1Skeleton } from '../../components/v1'
import { formatDateTime, formatNumber } from '../../utils'
import { unlesbarMeldung, unlesbareFelder, zahlOderNull } from '../../zahlenfeld'
import './changeouts.css'

type FormState = {
  kind: ChangeoutKind
  /** Wann der Wechsel WAR — nicht, wann er eingetragen wird. */
  performedAtLocal: string
  percentChanged: string
  volumeChangedLiters: string
  ecBefore: string
  ecAfter: string
  phBefore: string
  phAfter: string
  notes: string
}

/** „2026-08-28T14:30" — jetzt, im Format des Eingabefelds. */
function jetztLokal(): string {
  const jetzt = new Date()
  const versatz = jetzt.getTimezoneOffset() * 60_000
  return new Date(jetzt.getTime() - versatz).toISOString().slice(0, 16)
}

const emptyForm: FormState = {
  kind: 'Partial',
  performedAtLocal: jetztLokal(),
  percentChanged: '',
  volumeChangedLiters: '',
  ecBefore: '',
  ecAfter: '',
  phBefore: '',
  phAfter: '',
  notes: '',
}

function pair(before: number | null, after: number | null): string {
  if (before == null && after == null) return '–'
  return `${formatNumber(before, 2)} → ${formatNumber(after, 2)}`
}

/**
 * Die erfassten Wasserwechsel eines Grows — Liste und Eintrag.
 *
 * @param offenBeiStart Formular gleich aufgeklappt. Die eigene Seite nutzt das,
 *   wenn der Wechsel fällig ist: wer deswegen hier landet, will eintragen und
 *   nicht erst einen Knopf suchen.
 * @param onGespeichert Meldet nach oben, dass sich etwas geändert hat — die
 *   Seite lädt damit ihren Stand neu. Ohne das stünde oben „vor 9 Tagen",
 *   während unten der eben nachgetragene Wechsel von gestern steht.
 */
export function ChangeoutsPanel({ growId, growName, offenBeiStart = false, onGespeichert, leerHinweis }: {
  growId: number
  growName: string
  offenBeiStart?: boolean
  onGespeichert?: () => void
  /**
   * Was statt „noch kein Wasserwechsel" steht, wenn die Liste leer ist.
   *
   * <b>Gefunden am laufenden Stand (31.08.2026).</b> Oben stand „0 Tage seit
   * dem letzten Wechsel", unten „Noch kein Wasserwechsel" — beides richtig,
   * zusammen ein Widerspruch. Die Liste kennt nur ihre eigene Tabelle; der
   * letzte Wechsel kann auch aus einer Messung stammen. Wer das weiss, sagt es
   * hier, statt die Liste eine Auskunft geben zu lassen, die sie nicht hat.
   */
  leerHinweis?: string
}) {
  const [items, setItems] = useState<ChangeoutDto[]>([])
  const [loading, setLoading] = useState(true)
  const [open, setOpen] = useState(offenBeiStart)
  const [saving, setSaving] = useState(false)
  const [form, setForm] = useState<FormState>(emptyForm)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [refresh, setRefresh] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    async function run() {
      try {
        const data = await apiFetch<ChangeoutDto[]>(`/api/grows/${growId}/changeouts`, { signal: controller.signal })
        if (controller.signal.aborted) return
        setItems([...data].sort((a, b) => b.performedAtUtc.localeCompare(a.performedAtUtc)))
        setError(null)
      } catch (caught) {
        if (controller.signal.aborted) return
        setError(caught instanceof ApiRequestError ? caught.message : 'Wasserwechsel konnten nicht geladen werden.')
      } finally {
        if (!controller.signal.aborted) setLoading(false)
      }
    }
    void run()
    return () => controller.abort()
  }, [growId, refresh])

  const patch = (next: Partial<FormState>) => setForm((current) => ({ ...current, ...next }))

  /**
   * Einen Eintrag zuruecknehmen.
   *
   * Es gab keinen Weg zurueck — und seit die Mahnung diese Tabelle liest,
   * legt ein Fehlgriff sie fuer eine Woche still. Mit Rueckfrage, weil ein
   * verlorener Eintrag nicht wiederherzustellen ist.
   */
  async function entfernen(eintrag: ChangeoutDto) {
    const wann = formatDateTime(eintrag.performedAtUtc)
    if (!window.confirm(`Wasserwechsel vom ${wann} wirklich entfernen?`)) return
    setError(null)
    setNotice(null)
    try {
      await apiFetch(`/api/grows/${growId}/changeouts/${eintrag.id}`, { method: 'DELETE' })
      setNotice('Wasserwechsel entfernt.')
      setRefresh((value) => value + 1)
      onGespeichert?.()
    } catch (caught) {
      setError(caught instanceof ApiRequestError ? caught.message : 'Entfernen fehlgeschlagen.')
    }
  }

  async function submit(event: FormEvent) {
    event.preventDefault()
    // Bisher wurde Unlesbares still zu `null` — der Wechsel stand ohne den Wert
    // da, mit Erfolgsmeldung.
    const unlesbar = unlesbarMeldung(unlesbareFelder([
      [form.percentChanged, 'Anteil (%)'], [form.volumeChangedLiters, 'Menge (L)'],
      [form.ecBefore, 'EC vorher'], [form.ecAfter, 'EC nachher'],
      [form.phBefore, 'pH vorher'], [form.phAfter, 'pH nachher'],
    ]))
    if (unlesbar) { setError(unlesbar); setNotice(null); return }
    setSaving(true)
    setError(null)
    setNotice(null)
    try {
      /* <b>Der Zeitpunkt, den der Nutzer angibt.</b> Bis zum 28.08.2026 gab
         es kein Feld dafür, und jeder Eintrag landete auf „jetzt" — wer
         sonntags wechselte und dienstags eintrug, hatte danach einen Wechsel
         vom Dienstag in der Historie, und die Rechnung „letzter Wechsel vor N
         Tagen" zählte ab dem falschen Tag. Ausdrücklich gemeldet: „wenn das
         vor tagen passiert ist, dass man das nachtragen kann."

         Das Backend konnte es die ganze Zeit (`CreateChangeoutRequest`
         trägt `PerformedAtUtc`) — es fragte nur niemand danach.

         Als UTC geschickt, weil das Feld einen LOKALEN Zeitpunkt liefert. Ohne
         die Umrechnung läge ein Wechsel um 01:00 in Mitteleuropa einen Tag zu
         früh — dieselbe Falle wie bei den Datumsfeldern des Grows. */
      const body: CreateChangeoutRequest = {
        kind: form.kind,
        performedAtUtc: form.performedAtLocal
          ? new Date(form.performedAtLocal).toISOString()
          : null,
        percentChanged: zahlOderNull(form.percentChanged),
        volumeChangedLiters: zahlOderNull(form.volumeChangedLiters),
        ecBefore: zahlOderNull(form.ecBefore),
        ecAfter: zahlOderNull(form.ecAfter),
        phBefore: zahlOderNull(form.phBefore),
        phAfter: zahlOderNull(form.phAfter),
        notes: form.notes.trim() || null,
      }
      await apiFetch<ChangeoutDto>(`/api/grows/${growId}/changeouts`, { method: 'POST', body: JSON.stringify(body) })
      setForm(emptyForm)
      setOpen(false)
      setNotice('Wasserwechsel gespeichert.')
      setRefresh((value) => value + 1)
      onGespeichert?.()
    } catch (caught) {
      setError(caught instanceof ApiRequestError ? caught.message : 'Speichern fehlgeschlagen.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <V1Section
      title="Wasserwechsel"
      className="changeouts-section"
      action={<V1Button variant={open ? 'secondary' : 'primary'} onClick={() => { setOpen((value) => !value); setNotice(null) }}>{open ? 'Abbrechen' : 'Wechsel erfassen'}</V1Button>}
    >
      {notice && <V1Alert title="Gespeichert" message={notice} tone="ok" />}
      {error && <V1Alert message={error} tone="warn" />}

      {open && (
        <V1Card className="changeouts-form-card">
          <form onSubmit={submit} className="changeouts-form" data-audit="changeout-form">
            {/* Ganz oben: die Frage „wann war das" kommt vor jeder Zahl. */}
            <V1Field label="Wann" hint="Vorbelegt mit jetzt — für einen Nachtrag zurückstellen.">
              <input
                type="datetime-local"
                value={form.performedAtLocal}
                max={jetztLokal()}
                onChange={(event) => patch({ performedAtLocal: event.target.value })}
              />
            </V1Field>
            <V1Field label="Art">
              <select value={form.kind} onChange={(event) => {
                const kind = event.target.value as ChangeoutKind
                // A full change is 100% by definition — fix the share and lock the field.
                patch({ kind, percentChanged: kind === 'Full' ? '100' : form.percentChanged })
              }}>
                <option value="Partial">Teilwechsel</option>
                <option value="Full">Komplettwechsel</option>
              </select>
            </V1Field>
            <V1Field label="Anteil (%)"><input inputMode="decimal" value={form.percentChanged} onChange={(event) => patch({ percentChanged: event.target.value })} placeholder="z. B. 50" disabled={form.kind === 'Full'} /></V1Field>
            <V1Field label="Menge (L)"><input inputMode="decimal" value={form.volumeChangedLiters} onChange={(event) => patch({ volumeChangedLiters: event.target.value })} placeholder="z. B. 40" /></V1Field>
            <V1Field label="EC vorher"><input inputMode="decimal" value={form.ecBefore} onChange={(event) => patch({ ecBefore: event.target.value })} placeholder="mS/cm" /></V1Field>
            <V1Field label="EC nachher"><input inputMode="decimal" value={form.ecAfter} onChange={(event) => patch({ ecAfter: event.target.value })} placeholder="mS/cm" /></V1Field>
            <V1Field label="pH vorher"><input inputMode="decimal" value={form.phBefore} onChange={(event) => patch({ phBefore: event.target.value })} placeholder="z. B. 5,8" /></V1Field>
            <V1Field label="pH nachher"><input inputMode="decimal" value={form.phAfter} onChange={(event) => patch({ phAfter: event.target.value })} placeholder="z. B. 5,9" /></V1Field>
            <V1Field label="Notiz" wide><input value={form.notes} onChange={(event) => patch({ notes: event.target.value })} placeholder="Beobachtung, Grund …" /></V1Field>
            <div className="changeouts-form-actions">
              <V1Button type="submit" variant="primary" disabled={saving}>{saving ? 'Speichert…' : 'Wasserwechsel speichern'}</V1Button>
            </div>
          </form>
        </V1Card>
      )}

      {loading ? (
        <V1Skeleton rows={3} label="Lade Wasserwechsel" />
      ) : items.length === 0 ? (
        <V1Empty
          title="Noch nichts über dieses Formular erfasst"
          text={leerHinweis ?? `Für ${growName} ist hier noch kein Wechsel eingetragen.`}
        />
      ) : (
        <div className="v1-list" data-audit="changeout-list">
          {items.map((item) => (
            <div key={item.id} className="v1-list-row">
              <strong>{formatDateTime(item.performedAtUtc)}</strong>
              <span>
                {item.kind === 'Full' ? 'Komplettwechsel' : 'Teilwechsel'}
                {item.percentChanged != null ? ` · ${formatNumber(item.percentChanged, 0)}%` : ''}
                {item.volumeChangedLiters != null ? ` · ${formatNumber(item.volumeChangedLiters, 1)} L` : ''}
                {` · EC ${pair(item.ecBefore, item.ecAfter)} · pH ${pair(item.phBefore, item.phAfter)}`}
                {item.notes ? ` · ${item.notes}` : ''}
              </span>
              {/* Deutsch, seit die Seite im Hauptmenue steht. „FULL"/„PART"
                  fielen der Wort-Pruefung nicht auf, weil sie nicht woertlich
                  die Enum-Werte sind — eine Abkuerzung entkommt jeder Zaehlung,
                  die nach dem Original sucht. Gefunden vom Pruefer. */}
              <em>{item.kind === 'Full' ? 'ganz' : 'teils'}</em>
              <button
                type="button"
                className="ls-btn is-small changeouts-weg"
                onClick={() => void entfernen(item)}
                aria-label={`Wasserwechsel vom ${formatDateTime(item.performedAtUtc)} entfernen`}
                title="Entfernen"
              >Entfernen</button>
            </div>
          ))}
        </div>
      )}
    </V1Section>
  )
}
