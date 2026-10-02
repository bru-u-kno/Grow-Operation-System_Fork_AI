import { useEffect, useState } from 'react'
import { apiFetch, ApiRequestError } from '../api'
import { zahlZuText } from '../features/water/wasser-zahlen'
import { zahlOderNull } from '../zahlenfeld'
import { V1Alert, V1Button, V1Card, V1Field, V1Page, V1Section, V1Skeleton } from '../components/v1'
import './water.css'

type WaterProfile = {
  sourceLabel: string
  conductivityUsCm: number | null
  ph: number | null
  totalHardnessDh: number | null
  carbonateHardnessDh: number | null
  calciumMgL: number | null
  magnesiumMgL: number | null
  sodiumMgL: number | null
  nitrateMgL: number | null
  sulfateMgL: number | null
  chlorideMgL: number | null
  disinfection: string | null
  treatedConductivityUsCm: number | null
  treatedPh: number | null
  updatedAtUtc?: string
}

/** Ein bewerteter Wert — mit der Quelle, aus der die Schwelle stammt. */
type AmpelPunkt = {
  feld: string
  label: string
  stufe: 'gut' | 'hinweis' | 'warnung'
  wert: string
  aussage: string
  quelle: string
}

type Ampel = {
  stufe: 'gut' | 'hinweis' | 'warnung'
  zusammenfassung: string
  punkte: AmpelPunkt[]
}

/** Die Felder als Text-Entwürfe — Komma erlaubt, leer erlaubt. */
type Draft = Record<keyof Omit<WaterProfile, 'sourceLabel' | 'disinfection' | 'updatedAtUtc'>, string> & {
  sourceLabel: string
  disinfection: string
}

const leer: Draft = {
  sourceLabel: '',
  conductivityUsCm: '',
  ph: '',
  totalHardnessDh: '',
  carbonateHardnessDh: '',
  calciumMgL: '',
  magnesiumMgL: '',
  sodiumMgL: '',
  nitrateMgL: '',
  sulfateMgL: '',
  chlorideMgL: '',
  treatedConductivityUsCm: '',
  treatedPh: '',
  disinfection: '',
}

/**
 * Das Leitungswasser-Profil — die Werte aus dem Trinkwasserbericht der Stadt.
 *
 * Die Feldnamen folgen einem echten Bericht (Jahresmittelwerte eines
 * Wasserversorgers), nicht einer Wunschliste: wer sein PDF daneben legt, findet
 * jede Zahl unter demselben Namen. Alles ist optional — Berichte unterscheiden
 * sich, und ein halb gefülltes Profil ist besser als ein abgewiesenes.
 *
 * Werte unter der Nachweisgrenze („<0,01") trägt man als 0 ein oder lässt das
 * Feld leer — für den Grow sind beide dasselbe: nichts, was zählt.
 */
function WaterProfilePage() {
  const [draft, setDraft] = useState<Draft>(leer)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [updatedAt, setUpdatedAt] = useState<string | null>(null)
  const [ampel, setAmpel] = useState<Ampel | null>(null)

  // 204 heisst „noch nichts erfasst" — kein Fehler, nur keine Ampel.
  async function ladeAmpel(signal?: AbortSignal) {
    try {
      setAmpel(await apiFetch<Ampel>('/api/water-profile/rating', { signal }))
    } catch {
      setAmpel(null)
    }
  }

  useEffect(() => {
    const controller = new AbortController()
    async function load() {
      try {
        const profile = await apiFetch<WaterProfile>('/api/water-profile', { signal: controller.signal })
        if (controller.signal.aborted) return
        setDraft({
          sourceLabel: profile.sourceLabel ?? '',
          conductivityUsCm: toDraft(profile.conductivityUsCm),
          ph: toDraft(profile.ph),
          totalHardnessDh: toDraft(profile.totalHardnessDh),
          carbonateHardnessDh: toDraft(profile.carbonateHardnessDh),
          calciumMgL: toDraft(profile.calciumMgL),
          magnesiumMgL: toDraft(profile.magnesiumMgL),
          sodiumMgL: toDraft(profile.sodiumMgL),
          nitrateMgL: toDraft(profile.nitrateMgL),
          sulfateMgL: toDraft(profile.sulfateMgL),
          chlorideMgL: toDraft(profile.chlorideMgL),
          treatedConductivityUsCm: toDraft(profile.treatedConductivityUsCm),
          treatedPh: toDraft(profile.treatedPh),
          disinfection: profile.disinfection ?? '',
        })
        setUpdatedAt(profile.updatedAtUtc && profile.updatedAtUtc !== '0001-01-01T00:00:00' ? profile.updatedAtUtc : null)
        await ladeAmpel(controller.signal)
      } catch (caught) {
        if (!controller.signal.aborted) setError(formatError(caught))
      } finally {
        if (!controller.signal.aborted) setLoading(false)
      }
    }
    void load()
    return () => controller.abort()
  }, [])

  function set(key: keyof Draft, value: string) {
    setDraft((current) => ({ ...current, [key]: value }))
  }

  async function save() {
    setSaving(true)
    setError(null)
    setMessage(null)
    try {
      const saved = await apiFetch<WaterProfile>('/api/water-profile', {
        method: 'PUT',
        body: JSON.stringify({
          sourceLabel: draft.sourceLabel.trim(),
          conductivityUsCm: toNumber(draft.conductivityUsCm),
          ph: toNumber(draft.ph),
          totalHardnessDh: toNumber(draft.totalHardnessDh),
          carbonateHardnessDh: toNumber(draft.carbonateHardnessDh),
          calciumMgL: toNumber(draft.calciumMgL),
          magnesiumMgL: toNumber(draft.magnesiumMgL),
          sodiumMgL: toNumber(draft.sodiumMgL),
          nitrateMgL: toNumber(draft.nitrateMgL),
          sulfateMgL: toNumber(draft.sulfateMgL),
          chlorideMgL: toNumber(draft.chlorideMgL),
          treatedConductivityUsCm: toNumber(draft.treatedConductivityUsCm),
          treatedPh: toNumber(draft.treatedPh),
          disinfection: draft.disinfection.trim() || null,
        }),
      })
      setUpdatedAt(saved.updatedAtUtc ?? null)
      await ladeAmpel()
      setMessage('Wasserprofil gespeichert. Es fließt ab jetzt in den Lagebericht und die Wasserfrage der Abläufe ein.')
    } catch (caught) {
      setError(formatError(caught))
    } finally {
      setSaving(false)
    }
  }

  const startEc = toNumber(draft.conductivityUsCm)
  const haerte = toNumber(draft.totalHardnessDh)

  return (
    <V1Page
      eyebrow="Einrichtung"
      title="Wasser"
      subtitle="Dein Ausgangswasser — aus der Leitung und, wenn du aufbereitest, nach Osmose oder Entsalzung. Die Werte aus dem Trinkwasserbericht deiner Stadt stehen meist als PDF beim Versorger."
    >
      {error && <V1Alert title="Fehler" message={error} tone="warn" />}
      {message && <V1Alert message={message} tone="ok" />}

      {loading ? <V1Skeleton rows={4} label="Lade Wasserprofil" /> : (
        <>
          <V1Section title="Dein Wasser auf einen Blick">
            <V1Card>
              <div className="wp-summary" data-audit="water-summary">
                <div>
                  <strong>{startEc !== null ? `EC ${(startEc / 1000).toLocaleString('de-DE', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}` : 'EC —'}</strong>
                  <span>bringt dein Wasser schon mit, bevor ein Tropfen Dünger drin ist</span>
                </div>
                <div>
                  <strong>{haerte !== null ? `${haerte.toLocaleString('de-DE')} °dH` : '— °dH'}</strong>
                  <span>{haerte === null ? 'Gesamthärte' : haerte < 8.4 ? 'weich — deine Weichwasser-Mischreihenfolge passt' : haerte <= 14 ? 'mittel' : 'hart — Mischen mit RO erwägen'}</span>
                </div>
              </div>
            </V1Card>
          </V1Section>

          {ampel && (
            <V1Section title="Taugt dein Wasser?">
              <V1Card>
                <div className="wp-ampel" data-audit="water-rating" data-stufe={ampel.stufe}>
                  <p className="wp-ampel-summary">
                    <span className={`wp-dot wp-dot--${ampel.stufe}`} aria-hidden="true" />
                    {ampel.zusammenfassung}
                  </p>
                  <ul className="wp-ampel-list">
                    {ampel.punkte.map((punkt) => (
                      <li key={punkt.feld} className={`wp-ampel-item wp-ampel-item--${punkt.stufe}`}>
                        <div className="wp-ampel-head">
                          <span className={`wp-dot wp-dot--${punkt.stufe}`} aria-hidden="true" />
                          <strong>{punkt.label}</strong>
                          <span className="wp-ampel-wert">{punkt.wert}</span>
                        </div>
                        <p className="wp-ampel-aussage">{punkt.aussage}</p>
                        <p className="wp-ampel-quelle">Schwelle: {punkt.quelle}</p>
                      </li>
                    ))}
                  </ul>
                  <p className="wp-ampel-fuss">
                    Bewertet wird nur, was du eingetragen hast. Calcium, Magnesium und Nitrat
                    bekommen bewusst kein Urteil — im Kreislauf liefert sie dein Dünger, nicht
                    dein Wasser; sie zählen nur mit.
                  </p>
                </div>
              </V1Card>
            </V1Section>
          )}

          <V1Section title="Werte aus dem Bericht">
            <V1Card>
              <div className="v1-form-grid">
                <V1Field label="Quelle des Berichts" wide hint="Damit du in einem Jahr noch weißt, woher die Zahlen sind — z. B. „EBW Solingen, Werk Glüder, Jahresmittel 2025“. Steht dein Stadtteil bei einem anderen Werk, nimm dessen Spalte.">
                  <input value={draft.sourceLabel} onChange={(e) => set('sourceLabel', e.target.value)} placeholder="Wasserversorger, Werk, Jahr" />
                </V1Field>
                <V1Field label="Elektrische Leitfähigkeit (µS/cm)" hint="Im Bericht bei 25 °C. 276 µS/cm = EC 0,28.">
                  <input inputMode="decimal" value={draft.conductivityUsCm} onChange={(e) => set('conductivityUsCm', e.target.value)} placeholder="z. B. 276" />
                </V1Field>
                <V1Field label="pH">
                  <input inputMode="decimal" value={draft.ph} onChange={(e) => set('ph', e.target.value)} placeholder="z. B. 7,9" />
                </V1Field>
                <V1Field label="Gesamthärte (°dH)" hint="Unter 8,4 gilt als weich, über 14 als hart.">
                  <input inputMode="decimal" value={draft.totalHardnessDh} onChange={(e) => set('totalHardnessDh', e.target.value)} placeholder="z. B. 5,6" />
                </V1Field>
                <V1Field label="Karbonathärte (°dH)" hint="Der pH-Puffer: je höher, desto mehr pH-Minus wirst du brauchen.">
                  <input inputMode="decimal" value={draft.carbonateHardnessDh} onChange={(e) => set('carbonateHardnessDh', e.target.value)} placeholder="z. B. 4,1" />
                </V1Field>
                <V1Field label="Calcium (mg/L)">
                  <input inputMode="decimal" value={draft.calciumMgL} onChange={(e) => set('calciumMgL', e.target.value)} placeholder="z. B. 32" />
                </V1Field>
                <V1Field label="Magnesium (mg/L)">
                  <input inputMode="decimal" value={draft.magnesiumMgL} onChange={(e) => set('magnesiumMgL', e.target.value)} placeholder="z. B. 4,6" />
                </V1Field>
                <V1Field label="Natrium (mg/L)">
                  <input inputMode="decimal" value={draft.sodiumMgL} onChange={(e) => set('sodiumMgL', e.target.value)} placeholder="z. B. 8,7" />
                </V1Field>
                <V1Field label="Nitrat (mg/L)">
                  <input inputMode="decimal" value={draft.nitrateMgL} onChange={(e) => set('nitrateMgL', e.target.value)} placeholder="z. B. 10,3" />
                </V1Field>
                <V1Field label="Sulfat (mg/L)">
                  <input inputMode="decimal" value={draft.sulfateMgL} onChange={(e) => set('sulfateMgL', e.target.value)} placeholder="z. B. 16" />
                </V1Field>
                <V1Field label="Chlorid (mg/L)">
                  <input inputMode="decimal" value={draft.chlorideMgL} onChange={(e) => set('chlorideMgL', e.target.value)} placeholder="z. B. 16" />
                </V1Field>
                {/* Feedback des Testers: wer eine Osmoseanlage faehrt, setzt
                    nicht mit dem Berichtswasser an, sondern mit dem dahinter.
                    Beides gehoert ins Profil. */}
                <V1Field label="EC nach deiner Aufbereitung (µS/cm)" hint="Was aus deiner Osmose/Entsalzung kommt — selbst gemessen. Damit setzt du wirklich an.">
                  <input inputMode="decimal" value={draft.treatedConductivityUsCm} onChange={(e) => set('treatedConductivityUsCm', e.target.value)} placeholder="z. B. 12" />
                </V1Field>
                <V1Field label="pH nach deiner Aufbereitung">
                  <input inputMode="decimal" value={draft.treatedPh} onChange={(e) => set('treatedPh', e.target.value)} placeholder="z. B. 6,5" />
                </V1Field>
                <V1Field label="Desinfektion" hint="Steht meist bei den Aufbereitungsstoffen — z. B. Chlordioxid oder Chlor.">
                  <input value={draft.disinfection} onChange={(e) => set('disinfection', e.target.value)} placeholder="z. B. Chlordioxid" />
                </V1Field>
              </div>
              <div className="v1-form-actions">
                <V1Button onClick={() => void save()} disabled={saving}>{saving ? 'Speichere…' : 'Speichern'}</V1Button>
                {updatedAt && <span className="wp-updated">Zuletzt gespeichert: {new Date(updatedAt).toLocaleDateString('de-DE')}</span>}
              </div>
            </V1Card>
          </V1Section>

          <V1Section title="Was damit passiert">
            <V1Card>
              <ul className="wp-effects">
                <li>Der <strong>Lagebericht</strong> (Berater-Mappe und Claude-Anbindung) bekommt einen Abschnitt „Ausgangswasser“ — ein EC vor dem Düngen wird dann als Wasser erkannt, nicht als Rest-Salz.</li>
                <li>Die Frage <strong>„Womit mischst du an?“</strong> beim Start eines Ablaufs ist vorausgewählt — aus der Wasserquelle des Grows.</li>
                <li>Werte unter der Nachweisgrenze („&lt;0,01“) kannst du weglassen oder als 0 eintragen.</li>
              </ul>
            </V1Card>
          </V1Section>
        </>
      )}
    </V1Page>
  )
}

// Geschrieben wird mit wasser-zahlen.ts, gelesen mit der einen Leseregel aus
// zahlenfeld.ts — samt der Tausenderpunkt-Falle, die hier frueher aus
// 1234 µS/cm beim Speichern 1,234 machte.
function toDraft(value: number | null | undefined) {
  return zahlZuText(value)
}

function toNumber(value: string): number | null {
  return zahlOderNull(value)
}

function formatError(caught: unknown) {
  return caught instanceof ApiRequestError ? caught.message : caught instanceof Error ? caught.message : 'Unbekannter Fehler.'
}

export default WaterProfilePage
