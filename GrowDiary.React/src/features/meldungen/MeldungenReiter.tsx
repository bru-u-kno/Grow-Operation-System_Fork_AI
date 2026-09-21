import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch } from '../../api'
import { V1Badge, V1Card, V1Section } from '../../components/v1'
import NotificationsPage from '../../pages/NotificationsPage'

type HaWaechter = { entityId: string; name: string; aktiv: boolean; zweck: string | null; bekannt: boolean }

type Zielwerte = { werte: Array<{ name: string; meldet: boolean; regel: { aktiv: boolean } | null }> }

/**
 * Fork AI (Umbau „Ziele & Meldungen“, Schritt 4): alle Absender an einer Stelle.
 *
 * Oben der Stand der Grenzwerte (wie viele scharf sind, welche gerade melden),
 * darunter die bestehende Benachrichtigungsseite unverändert in ihrer Bedienung,
 * unten die Wächter, die Home Assistant selbst schickt — nur zur Ansicht, weil
 * sie gerade dann melden sollen, wenn Grow OS steht.
 */
export function MeldungenReiter() {
  const [waechter, setWaechter] = useState<HaWaechter[] | null>(null)
  const [ziele, setZiele] = useState<Zielwerte | null>(null)

  useEffect(() => {
    async function laden() {
      const [w, z] = await Promise.all([
        apiFetch<HaWaechter[]>('/api/meldungen/ha-waechter').catch(() => null),
        apiFetch<Zielwerte>('/api/zielwerte').catch(() => null),
      ])
      setWaechter(w)
      setZiele(z)
    }
    void laden()
  }, [])

  const scharf = ziele?.werte.filter((w) => w.regel?.aktiv).length ?? 0
  const melden = ziele?.werte.filter((w) => w.meldet) ?? []

  return (
    <div data-audit="meldungen-reiter">
      {ziele && (
        <V1Section title="Grenzwerte gerade">
          <V1Card tone={melden.length > 0 ? 'warn' : 'neutral'}>
            <p style={{ margin: 0, display: 'flex', justifyContent: 'space-between', gap: 8, alignItems: 'center' }}>
              <span>
                <strong>{scharf} Werte überwacht</strong>
                {melden.length > 0
                  ? ` · gerade außerhalb: ${melden.map((w) => w.name).join(', ')}`
                  : ' · keiner liegt gerade außerhalb'}
              </span>
              {/* Fork AI (forkai.133): Sprung zurück — ab wann gemeldet wird, steht dort. */}
              <Link to="/grenzwerte" className="pk-link" data-audit="push-zu-grenzwerten">Grenzwerte ›</Link>
            </p>
          </V1Card>
        </V1Section>
      )}

      <NotificationsPage />

      <V1Section title="Aus Home Assistant · nur Ansicht">
        {waechter == null && <V1Card>Home Assistant ist gerade nicht erreichbar.</V1Card>}
        {waechter?.length === 0 && <V1Card>In Home Assistant gibt es keine Wächter-Automation.</V1Card>}
        <div style={{ display: 'grid', gap: 12 }} data-audit="ha-waechter">
          {waechter?.map((w) => (
            <V1Card key={w.entityId}>
              <p style={{ margin: 0, display: 'flex', justifyContent: 'space-between', gap: 8 }}>
                <strong>{w.name}</strong>
                <V1Badge tone={w.aktiv ? 'accent' : 'warn'}>{w.aktiv ? 'an' : 'aus'}</V1Badge>
              </p>
              <p className="rc2-measurement-note" style={{ margin: '6px 0 0' }}>
                {w.zweck ?? 'Automation in Home Assistant.'} · {w.entityId}
              </p>
            </V1Card>
          ))}
        </div>
        {waechter && waechter.length > 0 && (
          <p className="rc2-measurement-note">
            Diese Wächter senden direkt aus Home Assistant, damit sie auch melden, wenn Grow OS gerade steht.
            Ändern kannst du sie in Home Assistant unter Automationen.
          </p>
        )}
      </V1Section>
    </div>
  )
}
