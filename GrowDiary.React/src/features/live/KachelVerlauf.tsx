import { useEffect, useRef, type ReactNode } from 'react'
import type { MetricPayload } from '../../types'
import type { HistoryPoint } from '../../components/SensorChart'
import { Verlaufsdiagramm } from './Verlaufsdiagramm'
import { kachelPlatz, kachelVerlaufId, zeilenEnde, type OffeneKachel } from './kachel-zeile'

/**
 * Eine Kachelzeile, in die der Verlauf der offenen Kachel nach dem Ende ihrer
 * Bildschirmzeile eingeschoben wird (`kachel-zeile.ts`).
 *
 * Jeder Platz muss ein Element mit `key` und `data-kachel-platz` sein. Ändert
 * die Zeile ihre Größe (Telefon gedreht, Kachel eingeklappt), wird neu
 * gemessen — im Rückruf des Beobachters, nicht beim Zeichnen.
 */
export function KachelZeile({ className, plaetze, offen, onNeuGemessen, verlauf, nachher }: {
  className: string
  plaetze: ReactNode[]
  /** Die offene Kachel dieses Bereichs, falls sie in ihm steht. */
  offen: OffeneKachel | null
  onNeuGemessen: (offen: OffeneKachel) => void
  verlauf: ReactNode
  /** Was nach allen Kacheln kommt — die Ablagefläche im Anpassen-Modus. */
  nachher?: ReactNode
}) {
  const zeileRef = useRef<HTMLDivElement | null>(null)
  const offenRef = useRef(offen)
  const meldenRef = useRef(onNeuGemessen)
  useEffect(() => {
    offenRef.current = offen
    meldenRef.current = onNeuGemessen
  })

  useEffect(() => {
    const element = zeileRef.current
    if (!element || typeof ResizeObserver === 'undefined') return
    const ro = new ResizeObserver(() => {
      const jetzt = offenRef.current
      if (!jetzt) return
      const platz = kachelPlatz(jetzt.kennung)
      const nach = platz && element.contains(platz) ? zeilenEnde(platz) : null
      if (nach != null && nach !== jetzt.nach) meldenRef.current({ kennung: jetzt.kennung, nach, insBild: false })
    })
    ro.observe(element)
    return () => ro.disconnect()
  }, [])

  const nach = offen && verlauf ? Math.min(offen.nach, plaetze.length - 1) : null
  return (
    <div ref={zeileRef} className={className}>
      {plaetze.flatMap((platz, i) => (i === nach ? [platz, verlauf] : [platz]))}
      {nachher}
    </div>
  )
}

/**
 * Der Verlauf einer angetippten Kachel — das große Verlaufsdiagramm, mit
 * diesem einen Wert im Fokus. Ersetzt das kleine Liniendiagramm, das hier
 * bisher stand.
 *
 * Steht als volle Zeile IN der Kachelzeile (`flex-basis: 100%`), damit er
 * genau unter der Zeile der Kachel umbricht. Nicht modal: die Nachbarkacheln
 * bleiben zum Vergleich im Bild.
 */
export function KachelVerlauf({
  kennung, metricKey, label, tentId, metricsByKey, trends, onSchliessen, insBild, onImBild,
}: {
  /** Kennung des Platzes der Kachel (`data-kachel-platz`). */
  kennung: string
  metricKey: string
  label: string
  tentId: number | null
  metricsByKey: Map<string, MetricPayload>
  trends: Map<string, HistoryPoint[]>
  onSchliessen: () => void
  /** Nach einem Tipp: einmal ins Bild rollen (siehe `OffeneKachel`). */
  insBild: boolean
  /** Meldet, dass gerollt wurde — danach rollt ein Wiedereinhängen nicht mehr. */
  onImBild: () => void
}) {
  const ref = useRef<HTMLDivElement | null>(null)

  useEffect(() => {
    const element = ref.current
    if (!element || !insBild) return
    onImBild()
    // Nur scrollen, wenn der Anfang nicht ohnehin gut im Bild liegt: wer am
    // Rechner unter einer Kachel der ersten Zeile aufklappt, soll nicht
    // durch die Seite geschoben werden.
    const oben = element.getBoundingClientRect().top
    if (oben < 0 || oben > window.innerHeight * 0.65) {
      element.scrollIntoView({ behavior: 'smooth', block: 'start' })
    }
  }, [insBild, onImBild])

  // Der Knopf nimmt sich beim Schliessen selbst weg — ohne das hier stünde der
  // Tastaturfokus danach auf <body>, und man begänne oben auf der Seite
  // (Befund des Prüfers). Zurück auf die Kachel, die den Verlauf geöffnet hat.
  const schliessen = () => {
    kachelPlatz(kennung)?.querySelector<HTMLElement>('.gos-metric.is-clickable')?.focus({ preventScroll: true })
    onSchliessen()
  }

  return (
    <div ref={ref} id={kachelVerlaufId(kennung)} data-kachel-verlauf="" className="ls-kachel-verlauf scroll-ziel" data-audit="metric-detail">
      <div className="ls-kachel-verlauf-kopf">
        <span className="ls-chart-head">Verlauf · {label}</span>
        <button type="button" className="ls-kachel-verlauf-zu" onClick={schliessen} data-audit="metric-detail-schliessen">
          <span aria-hidden="true">×</span> Schließen
        </button>
      </div>
      <Verlaufsdiagramm
        tileId={`kachel-verlauf:${metricKey}`}
        tentId={tentId}
        metricKeys={[metricKey]}
        metricsByKey={metricsByKey}
        tag={trends}
        nurWert={metricKey}
      />
    </div>
  )
}
