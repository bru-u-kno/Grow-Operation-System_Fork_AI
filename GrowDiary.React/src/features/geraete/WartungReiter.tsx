import { useEffect, useState } from 'react'

import { apiFetch, formatApiError } from '../../api'
import { V1Alert, V1Card, V1Empty, V1LinkButton, V1Skeleton } from '../../components/v1'

/**
 * Fork AI (forkai.41): Wartung — nach Gerät statt nach Eintrag.
 *
 * <b>Warum hier.</b> „Sensoren & Wartung" listet Einträge; wer wissen will, was
 * am Bluelab ansteht, sucht sie zusammen. Dieser Reiter dreht die Sortierung um
 * und nimmt die Geräte mit, die gar keine Entität haben — CO₂-Flasche,
 * Verschleißteile.
 *
 * <b>Nur lesend.</b> Erfasst und geändert wird weiter auf „Sensoren & Wartung";
 * jede Zeile führt dorthin. Damit bleibt die Originalseite unberührt, und
 * Weiterentwicklungen des Originals kommen ohne Handarbeit an.
 *
 * <b>Woher die Fristen kommen.</b> Aus dem Backend
 * (`WartungDueService.FristenRechnen`, `api/maintenance-due/fristen`) — dieselbe
 * Rechnung, aus der auch die Wartungs-Erinnerung liest. Bis 02.10.2026 rechnete
 * dieser Reiter selbst (`wartung-zeilen.ts`), und das Backend dieselbe Frage
 * anders. Hier wird nur noch angezeigt, nichts gerechnet; auch die Reihenfolge
 * kommt von dort.
 */

/** Eine Frist aus `api/maintenance-due/fristen` (`WartungsFrist`). */
type WartungsFrist = {
  schluessel: string
  art: 'wartung' | 'kalibrierung'
  titel: string
  hardwareItemId: number
  geraet: string
  faelligUtc: string | null
  quelle: string
  ohneFrist: boolean
}

const TAG = 24 * 60 * 60 * 1000

function tageBis(datum: Date): number {
  return Math.round((datum.getTime() - Date.now()) / TAG)
}

function frist(datum: Date | null): string {
  if (!datum) return 'ohne Frist'
  const tage = tageBis(datum)
  if (tage < 0) return `überfällig · ${Math.abs(tage)} T`
  if (tage === 0) return 'heute'
  if (tage === 1) return 'morgen'
  return `in ${tage} T`
}

export function WartungReiter({ geraeteNamen }: { geraeteNamen: Map<number, string> }) {
  const [fristen, setFristen] = useState<WartungsFrist[] | null>(null)
  const [fehler, setFehler] = useState<string | null>(null)

  useEffect(() => {
    const abbruch = new AbortController()
    void (async () => {
      try {
        setFristen(await apiFetch<WartungsFrist[]>('/api/maintenance-due/fristen', { signal: abbruch.signal }))
      } catch (caught) {
        if (!abbruch.signal.aborted) setFehler(formatApiError(caught, 'Die Wartung konnte nicht geladen werden.'))
      }
    })()
    return () => abbruch.abort()
  }, [])

  if (fehler) return <V1Alert tone="critical" message={fehler} />
  if (!fristen) return <V1Skeleton rows={5} label="Wartung wird geladen" />

  if (fristen.length === 0) {
    return (
      <V1Empty
        title="Nichts zu tun"
        text="Sobald ein Gerät ein Kalibrier- oder Prüfintervall trägt, steht es hier — nach Fälligkeit sortiert."
      />
    )
  }

  const zeilen = fristen.map((f) => ({
    ...f,
    // Der Name, unter dem das Gerät auf dieser Seite steht — sonst der aus dem Geräte-Eintrag.
    name: geraeteNamen.get(f.hardwareItemId) ?? f.geraet,
    faellig: f.faelligUtc ? new Date(f.faelligUtc) : null,
  }))
  const offen = zeilen.filter((zeile) => zeile.faellig != null && tageBis(zeile.faellig) <= 7)
  const spaeter = zeilen.filter((zeile) => !offen.includes(zeile))

  return (
    <>
      {[{ titel: 'Fällig', liste: offen }, { titel: 'Später', liste: spaeter }]
        .filter((gruppe) => gruppe.liste.length > 0)
        .map((gruppe) => (
          <V1Card key={gruppe.titel}>
            <div className="gr-sec"><span>{gruppe.titel}</span><b>{gruppe.liste.length}</b></div>
            {gruppe.liste.map((zeile) => {
              const tage = zeile.faellig ? tageBis(zeile.faellig) : null
              return (
                <div key={zeile.schluessel} className="gr-wartung">
                  <span className="nm">
                    <b>{zeile.titel}</b>
                    <small>{zeile.name}</small>
                  </span>
                  <span className={tage != null && tage <= 7 ? 'gr-frist is-faellig' : 'gr-frist'}>
                    {frist(zeile.faellig)}
                  </span>
                </div>
              )
            })}
          </V1Card>
        ))}

      <p className="gr-fuss">
        Erfasst und geändert wird weiter unter „Sensoren &amp; Wartung" — hier stehen dieselben
        Einträge, nur nach Gerät und Fälligkeit sortiert.{' '}
        <V1LinkButton to="/sensoren" variant="ghost">Zu Sensoren &amp; Wartung</V1LinkButton>
      </p>
    </>
  )
}
