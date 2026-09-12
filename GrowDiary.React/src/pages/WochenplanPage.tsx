import { useEffect, useState } from 'react'
import { apiFetch } from '../api'
import { classNames } from '../utils'
import { V1Alert, V1Page, V1Section, V1Skeleton } from '../components/v1'
import '../features/wochenplan/wochenplan.css'

/**
 * Fork AI: der Wochenplan eines laufenden Durchgangs.
 *
 * Seit forkai.46 wirken die Wochenwerte überall — EC, pH und das Klima kommen
 * aus der Spalte des Düngeprogramms, nicht aus der Phase. Zu SEHEN waren sie
 * nirgends am Stück: EC und pH im Mischplan, das Klima nur indirekt über die
 * Kacheln. Diese Seite zeigt beides zusammen, plus die Anker, aus denen sich
 * die Woche ergibt.
 *
 * Kein eigener Rechenweg: alles kommt aus `/api/wochenplan`, das dieselbe
 * Spaltenauswahl benutzt wie Kacheln und Alarme.
 */

type Woche = {
  id: string
  label: string
  stage: string
  woche: number | null
  istJetzt: boolean
  wirdGehalten: boolean
  ec: string | null
  ph: string | null
  wasser: string | null
  vpd: string | null
  rh: string | null
  luft: string | null
  co2: string | null
  ppfd: string | null
  dosierung: string | null
}

type Plan = {
  growId: number
  growName: string
  sorte: string | null
  programmName: string
  wochenZieleAktiv: boolean
  vegiStart: string | null
  flip: string | null
  erntefenster: string | null
  jetztLabel: string | null
  haltehinweis: string | null
  wochen: Woche[]
}

function Wert({ name, wert }: { name: string; wert: string | null }) {
  if (!wert) return null
  return (
    <div className="wp-zelle">
      <div className="wp-zelle-n">{name}</div>
      <div className="wp-zelle-v">{wert}</div>
    </div>
  )
}

function WochenplanPage() {
  const [plaene, setPlaene] = useState<Plan[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    async function laden() {
      try {
        setPlaene(await apiFetch<Plan[]>('/api/wochenplan'))
      } catch {
        setError('Der Wochenplan konnte nicht geladen werden.')
      } finally {
        setLoading(false)
      }
    }
    void laden()
  }, [])

  if (loading) return <V1Skeleton rows={6} label="Lade Wochenplan" />

  return (
    <V1Page
      eyebrow="Betrieb"
      title="Wochenplan"
      subtitle="Was das Düngeprogramm für diese Woche vorgibt. Die Woche ergibt sich aus den Ankern — verschiebst du den Flip, rutscht alles mit."
    >
      {error && <V1Alert message={error} tone="critical" />}

      {!error && plaene.length === 0 && (
        <V1Alert
          message="Kein laufender Durchgang mit Düngeprogramm. Der Wochenplan gehört zu einem Grow, nicht zum Zelt."
          tone="neutral"
        />
      )}

      {plaene.map((plan) => {
        const jetzt = plan.wochen.find((w) => w.istJetzt)
        return (
          <div key={plan.growId} data-audit={`wochenplan-${plan.growId}`}>
            <V1Section title={`${plan.programmName} · ${plan.growName}`}>
              {/* Ohne den Haken am Addback gilt der Plan nur fürs Anmischen.
                  Das gehört an den Anfang: sonst liest man hier Zahlen und
                  wundert sich, dass die Kacheln andere zeigen. */}
              {!plan.wochenZieleAktiv && (
                <V1Alert
                  message="Dieser Grow benutzt die Wochen-Ziele nicht. Die Werte unten gelten fürs Anmischen, nicht für Kacheln und Alarme — umstellen kannst du das auf der Addback-Seite."
                  tone="neutral"
                />
              )}

              <section className={classNames('wp-jetzt', !jetzt && 'ist-leer')}>
                <div className="wp-kopf">Diese Woche</div>
                <p className="wp-titel">
                  <b>{plan.jetztLabel ?? 'keine passende Spalte'}</b>
                  {plan.sorte && <span className="wp-leise"> · {plan.sorte}</span>}
                </p>
                {plan.haltehinweis && <p className="wp-halt">{plan.haltehinweis}</p>}

                <div className="wp-anker">
                  {plan.vegiStart && (
                    <span className="wp-pill">
                      Vegi-Start <b>{plan.vegiStart}</b>
                    </span>
                  )}
                  {plan.flip && (
                    <span className="wp-pill">
                      Flip <b>{plan.flip}</b>
                    </span>
                  )}
                  {plan.erntefenster && (
                    <span className="wp-pill">
                      Ernte <b>{plan.erntefenster}</b>
                    </span>
                  )}
                </div>

                {jetzt && (
                  <div className="wp-gitter">
                    <Wert name="EC" wert={jetzt.ec} />
                    <Wert name="pH" wert={jetzt.ph} />
                    <Wert name="Wasser T/N" wert={jetzt.wasser} />
                    <Wert name="VPD" wert={jetzt.vpd} />
                    <Wert name="RH" wert={jetzt.rh} />
                    <Wert name="Luft" wert={jetzt.luft} />
                    <Wert name="CO₂" wert={jetzt.co2} />
                    <Wert name="PPFD" wert={jetzt.ppfd} />
                  </div>
                )}
                {jetzt?.dosierung && <p className="wp-dosis">Anmischen: {jetzt.dosierung}</p>}
              </section>
            </V1Section>

            <V1Section title="Verlauf">
              <div className="wp-liste">
                {plan.wochen.map((woche) => (
                  <div key={woche.id} className={classNames('wp-zeile', woche.istJetzt && 'ist-jetzt')}>
                    <span className="wp-zeile-l">
                      {woche.label}
                      {woche.wirdGehalten && <span className="wp-leise"> · gehalten</span>}
                    </span>
                    <span className="wp-zeile-w">
                      {[woche.ec && `EC ${woche.ec}`, woche.wasser, woche.rh]
                        .filter(Boolean)
                        .join(' · ')}
                    </span>
                  </div>
                ))}
              </div>
            </V1Section>
          </div>
        )
      })}
    </V1Page>
  )
}

export default WochenplanPage
