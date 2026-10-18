import { useEffect, useState } from 'react'
import { apiFetch } from '../../api'
import { V1Alert, V1Card, V1LinkButton, V1Section } from '../../components/v1'

type AbsenkWoche = { bluetewocheAb1: number; tagC: number; nachtC: number; erreicht: boolean }
type Absenkplan = {
  wochen: AbsenkWoche[]
  heuteTagC: number | null
  heuteNachtC: number | null
  aktuelleWoche: number | null
  herkunft: string
  luecke: string | null
}
type Kuehler = { enabled: boolean; switchEntityId: string | null }
type NightRamp = {
  enabled: boolean
  floorC: number | null
  hardFloorC: number
  targetEntityId: string | null
  plan: Absenkplan
  chiller: Kuehler | null
}

const grad = (wert: number) => `${wert.toLocaleString('de-DE', { maximumFractionDigits: 1 })}\u00a0°C`

/**
 * Der Stand des Crop Steering an diesem Grow — und der Weg zur Seite.
 *
 * <para><b>Warum hier kein Formular mehr steht.</b> Bis beta.51 konnte man die
 * Rampe an zwei Stellen einstellen: hier und nirgends sonst. Mit der Seite
 * `/cropsteering` wären es zwei geworden — dieselbe Hauptaktion zweimal, und
 * genau das ist in `CLAUDE.md` als Befund und nicht als Feature notiert. Diese
 * Karte zeigt deshalb nur noch, was gilt, und führt zum Einstellen weiter.</para>
 */
export function NightRampCard({ growId }: { growId: number }) {
  const [rampe, setRampe] = useState<NightRamp | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    async function laden() {
      try {
        const geladen = await apiFetch<NightRamp>(`/api/grows/${growId}/night-ramp`, { signal: controller.signal })
        if (controller.signal.aborted) return
        setRampe(geladen)
      } catch {
        /* Ohne Rampe bleibt die Karte einfach leer. */
      }
    }
    void laden()
    return () => controller.abort()
  }, [growId])

  if (!rampe) return null
  const { plan, chiller } = rampe

  return (
    <V1Section
      title="Crop Steering"
      action={<V1LinkButton to={`/cropsteering?growId=${growId}`}>Einstellen</V1LinkButton>}
    >
      <V1Card>
        <div data-audit="night-ramp-stand">
          {/* Die Erklärung, warum das der Steuerungshebel ist, steht auf der
              Seite selbst. Hier wäre sie der längste Absatz einer Karte, die
              nur den Stand zeigen soll — und zweimal derselbe Satz driftet. */}
          {plan.heuteTagC != null && plan.heuteNachtC != null ? (
            <p className="nr-state">
              Heute gilt: <strong>{grad(plan.heuteTagC)}</strong> am Tag,{' '}
              <strong>{grad(plan.heuteNachtC)}</strong> in der Nacht
              {plan.aktuelleWoche != null ? ` (Blütewoche ${plan.aktuelleWoche})` : ''}.
            </p>
          ) : (
            <V1Alert tone="neutral" message={plan.luecke ?? 'Noch kein Plan — es fehlt der Flip in die Blüte.'} />
          )}

          <p className="nr-state">
            {rampe.enabled
              ? rampe.targetEntityId
                ? `Sollwert wird an ${rampe.targetEntityId} geschrieben, bei Licht an und Licht aus.`
                : 'Absenkung an, aber ohne Zielgerät — es wird nur geplant, nichts geschaltet.'
              : 'Absenkung aus. Der Plan ist Vorschau.'}
            {chiller?.enabled && chiller.switchEntityId
              ? ` Der Kühler wird über ${chiller.switchEntityId} geschaltet.`
              : ' Der Kühler wird nicht geschaltet.'}
          </p>
        </div>
      </V1Card>
    </V1Section>
  )
}
