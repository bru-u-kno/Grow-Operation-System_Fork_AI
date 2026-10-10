import { useEffect, useState } from 'react'
import { apiFetch } from '../../api'
import { V1Card } from '../../components/v1'
import type { GeraetZeile } from '../steuerung/steuerung-typen'
import { BLUELAB_SCHLUESSEL, bluelabLage, type BluelabStand, type SchrittStatus } from './bluelab-anleitung'
import '../steuerung/steuerung.css'

/**
 * Fork AI (A-016, Etappe 6): Die Bluelab-Anleitung — nach Mockup `bluelab-anleitung.html` freigegeben.
 *
 * Oben im Reiter „Rollen", wenn die Steuerung „Bluelab" gewählt ist. Eine Checkliste mit Häkchen aus dem echten Stand:
 * was der Nutzer in Home Assistant braucht (sechs Alarm-Zahlen, ein Skript), ob es zugeordnet und gefunden ist und ob
 * die Übertragung läuft. Der Fork legt davon nichts an — es hängt an Zugangsdaten zum Hersteller, die er nie sieht.
 * Ist alles in Ordnung, klappt sie zu einer Zeile zu.
 */

const zeit = (iso: string) => new Date(iso).toLocaleString('de-DE', { dateStyle: 'medium', timeStyle: 'short' })

const ZEICHEN: Record<SchrittStatus, string> = { ok: '✓', warn: '!', offen: '' }

export function BluelabAnleitung({ zeilen }: { zeilen: readonly GeraetZeile[] }) {
  const [stand, setStand] = useState<BluelabStand | null>(null)
  // null = noch nicht angefasst: dann entscheidet der Stand (fertig → zu, sonst offen)
  const [aufgeklappt, setAufgeklappt] = useState<boolean | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    void apiFetch<BluelabStand>('/api/steuerung/bluelab', { signal: controller.signal })
      .then((s) => { if (!controller.signal.aborted) setStand(s) })
      .catch(() => undefined)
    return () => controller.abort()
  }, [])

  const lage = bluelabLage(zeilen, stand, zeit)
  const offen = aufgeklappt ?? !lage.fertig

  return (
    <V1Card>
      <div className="st-anleitung" data-audit="bluelab-anleitung">
        <button type="button" className="st-anleitung-kopf" aria-expanded={offen} onClick={() => setAufgeklappt(!offen)}>
          <b>Bluelab Guardian: Alarmgrenzen ans Gerät übertragen</b>
          <span className={lage.fertig ? 'st-anleitung-stand is-ok' : 'st-anleitung-stand'}>
            {lage.fertig ? 'eingerichtet ✓' : 'optional'}
          </span>
        </button>
        {offen && (
          <>
            <p className="st-hinweis">
              Der Fork schickt seine Grenzwerte (pH, EC, Wassertemperatur) an die Alarmgrenzen deines Guardian — damit das Gerät
              nicht mit veralteten Grenzen Alarm schlägt. Dafür braucht es zwei Dinge in Home Assistant, die der Fork nicht
              für dich anlegt.
            </p>
            <ol className="st-schrittliste">
              {lage.schritte.map((s) => (
                <li key={s.nr} className={`is-${s.status}`}>
                  <span className="st-schritt-zeichen" aria-hidden="true">{ZEICHEN[s.status] || s.nr}</span>
                  <span>
                    <b>{s.titel}</b>
                    <small>{s.text}</small>
                    {s.nr === 2 && (
                      <details>
                        <summary>Was das Skript tun muss</summary>
                        <small>
                          Es bekommt zum Beispiel <code>setting_key = setting.ph_high_alarm</code> und <code>value = 6.2</code> und
                          setzt diesen Wert beim Hersteller. Die sechs Schlüssel:{' '}
                          {BLUELAB_SCHLUESSEL.map((k, i) => <span key={k}>{i > 0 && ' · '}<code>{k}</code></span>)}.
                          Deine Zugangsdaten zum Hersteller gehören ins Skript oder in die Geheimnisse von Home Assistant —
                          der Fork sieht und speichert sie nie.
                        </small>
                      </details>
                    )}
                  </span>
                </li>
              ))}
            </ol>
          </>
        )}
      </div>
    </V1Card>
  )
}
