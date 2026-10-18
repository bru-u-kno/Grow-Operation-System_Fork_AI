import { useState } from 'react'
import { Link } from 'react-router-dom'
import type { MeasurementDto } from '../../types'
import { formatDateTime, formatNumber } from '../../utils'
import type { GrowDetailSection } from './grow-detail-model'
import { V1LinkButton } from '../../components/v1'
import { useAbBreite } from '../../breite'
import { phaseName } from '../../deutsche-woerter'
import { bilanzKurz, bilanzSatz, herkunftWort, stellenFuer, urteilFuer, urteilKlasse, urteilSatz, urteilZeichen, wertUrteil } from './mess-urteil'
import type { MeasurementAssessmentReportDto } from '../../types'
import './grow-detail-legacy.css'

/**
 * Diese Seite ist das PROTOKOLL — sie zeigt Messungen, sie nimmt keine auf.
 *
 * Die vier Formular-Eigenschaften sind mit dem zweiten Messformular
 * weggefallen. Wer eintragen will, geht auf /messung: dort stehen 31 Felder
 * statt 9, dazu die Live-Prüfung, das Foto und der Addback.
 */
type GrowDetailMeasurementsSectionProps = {
  activeSection: GrowDetailSection
  measurements: MeasurementDto[]
  selectedMeasurementId: number | null
  onSelectMeasurement: (measurementId: number | null) => void
}

export function GrowDetailMeasurementsSection({
  activeSection,
  measurements,
  selectedMeasurementId,
  onSelectMeasurement,
  beurteilung,
}: GrowDetailMeasurementsSectionProps & { beurteilung?: MeasurementAssessmentReportDto | null }) {
  /**
   * Die Liste war auf 15 gedeckelt, der Zaehler daneben zeigte trotzdem die
   * volle Zahl: „19 gesamt" ueber genau 15 Zeilen, ohne Hinweis darauf, dass
   * mehr da ist, und ohne Weg dorthin. Das ist keine Anzeigefrage, das ist
   * verlorene Auskunft. Jetzt sagt der Zaehler, was er zeigt, und der Rest ist
   * einen Klick entfernt.
   */
  /**
   * Ab 1000 px eine Tabelle statt einer Zeitachse.
   *
   * In der Zeitachse steckten pH, EC, Lufttemperatur und Feuchte in zwei
   * Saetzen — 70 bis 80 Prozent der Zeile blieben leer, und die Liste
   * brauchte trotzdem anderthalb Bildschirme. Als Tabelle stehen die Werte
   * untereinander und sind vergleichbar.
   *
   * Warum nicht per CSS: das sind zwei verschiedene Auszeichnungen. Beide in
   * die Seite zu schreiben und eine auszublenden hiesse, jede Messung doppelt
   * in den Baum zu setzen.
   */
  const alsTabelle = useAbBreite(1000)
  const [alleZeigen, setAlleZeigen] = useState(false)
  const GEZEIGT = 15
  const sichtbare = alleZeigen ? measurements : measurements.slice(0, GEZEIGT)
  const versteckt = measurements.length - sichtbare.length
  const isVisible = activeSection === 'measurements'

  return (
    <>
      {/* Hier stand „Verlauf“ als Abschnitts-Beschriftung — und direkt
          darunter nochmal „Verlauf“ als Karten-Titel. Zweimal dasselbe Wort
          untereinander, aus einer Umbenennung entstanden. Der Abschnitt hat
          genau EINE Karte, die trägt ihren Titel selbst. */}
      <div className="card" style={{ marginBottom: 14, display: isVisible ? undefined : 'none' }}>
        <div className="card-header">
          <span className="card-title">Verlauf</span>
          <span className="text-muted" style={{ fontSize: 'var(--fs-text)' }}>{versteckt > 0 ? `${sichtbare.length} von ${measurements.length}` : `${measurements.length} gesamt`}</span>
        </div>
        {beurteilung && beurteilung.checkedValueCount > 0 && (
          <p className="gd-mess-bilanz">
            {bilanzSatz(beurteilung)}
            <em>geprüft gegen {beurteilung.profileLabel}, Phase je Messung — Stand heute</em>
          </p>
        )}
        {measurements.length === 0 ? (
          <div className="empty-hint">Noch keine Messungen vorhanden.</div>
        ) : alsTabelle ? (
          <div className="co-table-wrap">
            <div className="co-table" style={{ gridTemplateColumns: '1.2fr .8fr .5fr .5fr .6fr .6fr .5fr .8fr' }}>
              <div className="co-th">Zeitpunkt</div>
              <div className="co-th">Phase</div>
              <div className="co-th">pH</div>
              <div className="co-th">EC</div>
              <div className="co-th">Wasser</div>
              <div className="co-th">Luft</div>
              <div className="co-th">rF</div>
              <div className="co-th" />
              {sichtbare.map((measurement) => {
                const zeile = urteilFuer(beurteilung ?? null, measurement.id)
                // Eine Zelle mit Urteil: Farbe UND Zeichen, dazu der
                // ausgeschriebene Satz fuer Vorlesegeraete. Ohne Urteil
                // bleibt sie schlicht — nicht gemessen darf nie aussehen
                // wie in Ordnung.
                /* Die Stellenzahl steht NICHT hier: sie kommt aus
                   `stellenFuer`, aus derselben Tabelle, die auch der
                   Vorlese-Satz benutzt. Vorher stand sie an beiden Stellen
                   getrennt — der Satz gar nicht, er nahm die rohe Zahl. */
                const zelle = (metrik: string, wert: number | null) => {
                  const stellen = stellenFuer(metrik)
                  const u = wertUrteil(zeile, metrik)
                  if (!u || wert == null) return <div className="co-td">{formatNumber(wert, stellen)}</div>
                  return (
                    <div className={urteilKlasse(u.verdict)} title={urteilSatz(u)}>
                      {formatNumber(wert, stellen)}
                      {urteilZeichen(u.verdict) && <span aria-hidden="true"> {urteilZeichen(u.verdict)}</span>}
                      <span className="sr-only">{urteilSatz(u)}</span>
                    </div>
                  )
                }
                return (
                <div
                  key={measurement.id}
                  /* `display: contents`: die Zellen muessen direkte Kinder
                     des Rasters bleiben, sonst fallen sie aus den Spalten. */
                  className={selectedMeasurementId === measurement.id ? 'gd-mess-zeile is-gewaehlt' : 'gd-mess-zeile'}
                  onClick={() => onSelectMeasurement(measurement.id)}
                >
                  {/* Die Herkunft steht IN der Namenszelle, nicht in einer
                      neunten Spalte: die Tabelle hat acht feste Spuren, und
                      unter 1000 px gibt es sie gar nicht. */}
                  <div className="co-td is-name">
                    {formatDateTime(measurement.takenAt)}
                    <em className="gd-mess-herkunft">{herkunftWort(measurement.source)}</em>
                  </div>
                  <div className="co-td is-muted">
                    {phaseName(measurement.stage)}
                    {/* Weicht die gerechnete Phase ab, stehen beide da. Die
                        gespeicherte still zu ueberschreiben waere schlimmer:
                        im Bestand laeuft sie stellenweise rueckwaerts. */}
                    {zeile?.computedStage && zeile.computedStage !== measurement.stage && (
                      <em className="gd-mess-herkunft" title={`Der Lauf war an dem Tag in Phase ${phaseName(zeile.computedStage)}`}>≠ {phaseName(zeile.computedStage)}</em>
                    )}
                  </div>
                  {zelle('ph', measurement.reservoirPh)}
                  {zelle('ec', measurement.reservoirEc)}
                  {zelle('water-temp', measurement.reservoirWaterTempC)}
                  {zelle('air-temp', measurement.airTemperatureC)}
                  {zelle('humidity', measurement.humidityPercent)}
                  <div className="co-td">
                    <Link className="ls-btn is-small" to={`/grows/measurements/${measurement.id}/edit`} onClick={(event) => event.stopPropagation()}>Bearbeiten</Link>
                  </div>
                </div>
                )
              })}
            </div>
          </div>
        ) : (
          sichtbare.map((measurement) => {
            const kurz = bilanzKurz(urteilFuer(beurteilung ?? null, measurement.id))
            return (
            <div
              key={measurement.id}
              className="timeline-item"
              style={{ cursor: 'pointer', padding: '12px 16px', background: selectedMeasurementId === measurement.id ? 'var(--surface2)' : undefined }}
              onClick={() => onSelectMeasurement(measurement.id)}
            >
              <div className="tl-dot-col">
                <div className="tl-dot measurement" />
                <div className="tl-line" />
              </div>
              <div style={{ flex: '1 1 9rem', minWidth: 0 }}>
                <div className="tl-title">{phaseName(measurement.stage)} · pH {formatNumber(measurement.reservoirPh, stellenFuer('ph'))} · EC {formatNumber(measurement.reservoirEc, stellenFuer('ec'))}</div>
                <div className="tl-sub">{formatNumber(measurement.airTemperatureC, stellenFuer('air-temp'))}°C · {formatNumber(measurement.humidityPercent, stellenFuer('humidity'))}% rF · {herkunftWort(measurement.source)}{kurz ? ' · ' + kurz : ''}</div>
              </div>
              <div style={{ display: 'grid', gap: 6, justifyItems: 'end' }}>
                <div className="tl-time">{formatDateTime(measurement.takenAt)}</div>
                <Link className="btn" to={`/grows/measurements/${measurement.id}/edit`} onClick={(event) => event.stopPropagation()}>Bearbeiten</Link>
              </div>
            </div>
            )
          })
        )}
        {versteckt > 0 && (
          <button type="button" className="ls-btn is-small" style={{ margin: 12 }} onClick={() => setAlleZeigen(true)}>
            weitere {versteckt} anzeigen
          </button>
        )}
      </div>

      {/* HIER STAND EIN ZWEITES MESSFORMULAR — mit 9 Feldern, waehrend
          /messung 31 hat, dazu Live-Pruefung, Foto und Addback.

          Es war eine schlechtere Kopie derselben Handlung, und seit
          /messungen im Menue steht, standen beide Wege direkt nebeneinander:
          „Messen" und „Messungen". Wer den falschen erwischte, bekam ein
          Formular ohne Pruefung.

          Diese Seite ist das PROTOKOLL. Eintragen gehoert auf /messung. */}
      <div className="v1-action-row" style={{ margin: '0 0 14px', display: isVisible ? undefined : 'none' }}>
        <V1LinkButton to="/messung" variant="primary">Neue Messung eintragen</V1LinkButton>
      </div>
    </>
  )
}
