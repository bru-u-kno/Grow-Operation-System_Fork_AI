import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch } from '../../api'
import { resolveUrl } from '../../base'
import { V1Alert, V1Card, V1Section } from '../../components/v1'
import { GrowScopePicker } from '../grow-scope/GrowScopePicker'
import { useSelectedGrow } from '../grow-scope/useSelectedGrow'
import './mappe.css'

/**
 * Reiter „Mappe" der Seite KI-Assistent — vorher eine eigene Seite unter
 * Wissen (`/berater`). Fork AI (04.10.2026): alles zur eigenen KI steht jetzt
 * an einer Stelle; die Mappe ist dort der Weg ganz ohne Verbindung.
 *
 * Vorher stand das unten auf der Grow-Seite, hinter allem anderen. Das ist
 * dieselbe Art von Verstecken, die schon einmal aufgeräumt wurde: Wer nicht
 * weiß, dass es etwas gibt, sucht auch nicht danach.
 *
 * Anweisung und Prüffragen stehen hier offen, nicht nur in der ZIP-Datei. Sie
 * sind das, was den Berater begrenzt — ungelesene Grenzen sind keine.
 */

type Pruefung = {
  titel: string
  frage: string
  richtig: string
  durchgefallen: string
  kuerzel: string | null
  hinweis: string | null
}

type Mappe = {
  anweisung: string
  pruefungen: Pruefung[]
  dateien: { name: string; inhalt: string }[]
}

export function MappeReiter() {
  const { grows, growId, setGrowId, loading, error } = useSelectedGrow()
  const [mappe, setMappe] = useState<Mappe | null>(null)
  const [ladefehler, setLadefehler] = useState<string | null>(null)
  const [kopiert, setKopiert] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    apiFetch<Mappe>('/api/agent-export/mappe', { signal: controller.signal })
      .then(setMappe)
      .catch((caught) => {
        if (!controller.signal.aborted) {
          setLadefehler(caught instanceof Error ? caught.message : 'Der Inhalt der Mappe ließ sich nicht laden.')
        }
      })
    return () => controller.abort()
  }, [])

  async function anweisungKopieren() {
    if (!mappe) return
    try {
      await navigator.clipboard.writeText(mappe.anweisung)
      setKopiert(true)
      window.setTimeout(() => setKopiert(false), 2500)
    } catch {
      // Ohne Zwischenablage-Recht bleibt der Text unten trotzdem lesbar.
      setLadefehler('Die Zwischenablage ist gesperrt — markier den Text unten und kopiere ihn von Hand.')
    }
  }

  const grow = grows.find((item) => String(item.id) === String(growId)) ?? null

  return (
    <>
      <div className="ab-kopf">
        <p className="ab-text">
          Die Mappe packt das Fachwissen deiner Anlage in eine Datei — die gibst du einem KI-Assistenten
          deiner Wahl, wenn du einen willst. Das geht mit jedem Plan, auch dem kostenlosen.
        </p>
        <GrowScopePicker grows={grows} growId={growId} onChange={setGrowId} />
      </div>
      {error && <V1Alert message={error} tone="critical" />}
      {ladefehler && <V1Alert message={ladefehler} tone="warn" />}

      <V1Section title="Die Mappe">
        <V1Card>
          <p className="ab-text">
            Die Mappe ist ein ZIP mit neun Textdateien. Darin: der aktuelle Stand deines Grows und
            das komplette Fachwissen von Grow OS — Abläufe, Behandlungen samt Dosierung, Symptome,
            Regeln und Sollwerte. Hängst du diese Dateien bei ChatGPT, Claude oder einem lokalen
            Modell an, antwortet der Assistent auf Grundlage dieses Materials statt aus dem
            Internet.
          </p>
          <p className="ab-text">
            Du brauchst dafür keinen Schlüssel in Grow OS zu hinterlegen, und Grow OS verschickt
            nichts. Der Assistent kann nichts schalten — Dosierung und Automatik laufen weiter nur
            hier.
          </p>

          {loading && <p className="ab-text">Lade die Grows…</p>}
          {!loading && grow === null && (
            <p className="ab-text">Leg zuerst einen Grow an — ohne ihn gibt es keinen Lagebericht.</p>
          )}

          {grow && (
            <div className="v1-action-row">
              <a className="v1-button is-primary" href={resolveUrl(`/api/agent-export/grows/${grow.id}/paket`)}>
                Mappe herunterladen
              </a>
              <a className="v1-button" href={resolveUrl(`/api/agent-export/grows/${grow.id}/download`)}>
                Nur den Lagebericht
              </a>
              <Link className="v1-button" to={`/grows/${grow.id}`}>Zum Grow</Link>
            </div>
          )}
        </V1Card>
      </V1Section>

      {mappe && (
        <>
          <V1Section title="So benutzt du sie">
            <V1Card>
              <ol className="ab-schritte">
                <li>Mappe herunterladen und das ZIP entpacken.</li>
                <li>Einen neuen Chat öffnen und <b>alle neun Dateien</b> anhängen. Wer öfter fragt,
                  legt besser ein Projekt an: bei ChatGPT ein eigenes GPT, bei Claude ein Projekt.</li>
                <li>Die Anweisung weiter unten kopieren und als Erstes einfügen. In einem Projekt
                  gehört sie in das Feld für die Systemanweisung.</li>
                <li>Die vier Prüffragen stellen und die Antworten mit den Musterlösungen
                  vergleichen. Erst danach Fragen zu deiner Anlage.</li>
                <li>Ändert sich der Stand, lädst du die Mappe neu herunter — sie enthält immer die
                  Werte vom Zeitpunkt des Herunterladens.</li>
              </ol>
              <table className="ab-dateien">
                <tbody>
                  {mappe.dateien.map((datei) => (
                    <tr key={datei.name}>
                      <th scope="row">{datei.name}</th>
                      <td>{datei.inhalt}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </V1Card>
          </V1Section>

          <V1Section title="Prüffragen: teste den Assistenten">
            <V1Card>
              <p className="ab-text">
                KI-Assistenten klingen bei einer erfundenen Antwort genauso sicher wie bei einer
                belegten. Man hört den Unterschied nicht — man muss ihn prüfen. Diese vier Fragen
                haben eine bekannte richtige Antwort. Beantwortet dein Assistent sie falsch,
                probier ein anderes Modell aus, bevor du dich auf ihn verlässt.
              </p>
            </V1Card>
            <div className="ab-fragen">
              {mappe.pruefungen.map((pruefung, index) => (
                <V1Card key={pruefung.titel}>
                  <div className="ab-frage-kopf">
                    <span className="ab-nummer">{index + 1}</span>
                    <strong>{pruefung.titel}</strong>
                  </div>
                  <blockquote className="ab-frage">{pruefung.frage}</blockquote>
                  <p className="ab-text"><b>Richtig:</b> {pruefung.richtig}</p>
                  <p className="ab-text ab-schlecht"><b>Durchgefallen:</b> {pruefung.durchgefallen}</p>
                  {pruefung.kuerzel && (
                    <p className="ab-beleg">Beleg im Wissen: <code>{pruefung.kuerzel}</code></p>
                  )}
                  {pruefung.hinweis && <p className="ab-text ab-hinweis">{pruefung.hinweis}</p>}
                </V1Card>
              ))}
            </div>
          </V1Section>

          <V1Section title="Die Anweisung">
            <V1Card>
              <p className="ab-text">
                Dieser Text sagt dem Assistenten, welche Rolle er hat und was er nicht darf: keine
                Werte erfinden, deine eigenen Sollwerte nicht überstimmen, nichts schalten und bei
                fehlenden Angaben nachfragen statt raten. Er liegt auch als Datei in der Mappe.
              </p>
              <div className="v1-action-row">
                <button type="button" className="v1-button" onClick={() => void anweisungKopieren()}>
                  {kopiert ? 'Kopiert' : 'Anweisung kopieren'}
                </button>
              </div>
              <pre className="ab-anweisung">{mappe.anweisung}</pre>
            </V1Card>
          </V1Section>
        </>
      )}
    </>
  )
}
