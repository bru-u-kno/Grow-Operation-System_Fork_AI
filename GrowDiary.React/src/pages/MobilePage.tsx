import { useEffect, useState } from 'react'
import { apiFetch } from '../api'
import { V1Alert, V1Button, V1Field, V1Section, V1Skeleton } from '../components/v1'
import { buildPanelUrl, judgeHost } from '../features/mobile/mobile-link'
import { QrCode } from '../features/mobile/QrCode'
import '../features/mobile/mobile.css'

/**
 * Grow OS auf den Startbildschirm des Handys.
 *
 * Der Weg, der naheliegt, ist der falsche: die Adresse aus der Adresszeile
 * kopieren. Sie enthält ein Ingress-Token, das pro Anfrage wechselt — das
 * Lesezeichen ist am nächsten Tag tot. Stabil ist der Seitenleisten-Pfad, und
 * der ist schlicht der Add-on-Slug, den der Server beim Supervisor erfragt.
 *
 * Was diese Seite ehrlicherweise NICHT liefert: eine App ohne Home-Assistant-
 * Rahmen. Grow OS läuft ausschliesslich hinter dem Ingress, und dort gehört die
 * äussere Seite Home Assistant. Das steht unten auch so da.
 *
 * Fork AI (forkai.133): kein eigener Menüpunkt mehr, sondern der Reiter
 * „App einrichten" der Seite „Handy" (Plan · Grenzwerte · Handy).
 */

type MobileAccess = {
  available: boolean
  slug: string | null
  panelPath: string | null
  reason: string | null
}

export function AppEinrichten() {
  const [access, setAccess] = useState<MobileAccess | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [host, setHost] = useState('')
  const [kopiert, setKopiert] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    async function load() {
      try {
        const data = await apiFetch<MobileAccess>('/api/system/mobile-access', { signal: controller.signal })
        if (controller.signal.aborted) return
        setAccess(data)
        // Die Adresse, unter der dieser Browser gerade verbunden ist, ist die
        // beste erste Vermutung — von dort kommt schliesslich diese Seite.
        setHost(window.location.origin)
      } catch (caught) {
        if (!controller.signal.aborted) setError(caught instanceof Error ? caught.message : 'Konnte den Panel-Pfad nicht laden.')
      } finally {
        if (!controller.signal.aborted) setLoading(false)
      }
    }
    void load()
    return () => controller.abort()
  }, [])

  // Beides ist reines Zusammensetzen von Zeichenketten — der React-Compiler
  // merkt sich das von selbst, ein useMemo von Hand streitet nur mit ihm.
  const url = access?.panelPath ? buildPanelUrl(host, access.panelPath) : null
  const verdict = host.trim() === '' ? null : judgeHost(host)

  async function kopieren() {
    if (!url) return
    try {
      await navigator.clipboard.writeText(url)
      setKopiert(true)
      window.setTimeout(() => setKopiert(false), 2000)
    } catch {
      setError('Die Zwischenablage war nicht erreichbar — markier die Adresse und kopier sie von Hand.')
    }
  }

  return (
    <>
      {error && <V1Alert message={error} tone="warn" />}

      {loading ? <V1Skeleton rows={3} label="Lade Panel-Pfad" /> : !access?.available ? (
        <V1Alert
          tone="neutral"
          message={access?.reason ?? 'Grow OS läuft hier nicht als Home-Assistant-Add-on.'} />
      ) : (
        <>
          <V1Section title="Scannen — mit der Kamera des Handys">
            <div className="mo-scan">
              <div className="mo-qr">
                {url ? <QrCode value={url} label={`QR-Code zu ${url}`} /> : (
                  <div className="mo-qr-empty">Trag rechts eine Adresse ein.</div>
                )}
              </div>

              <div className="mo-side">
                <ol className="mo-steps">
                  <li>Code mit der Handy-Kamera scannen — der Browser öffnet Home Assistant.</li>
                  <li>Dort einmal anmelden, falls du auf dem Handy noch nicht angemeldet bist.</li>
                  <li>Im Browser-Menü <b>„Zum Home-Bildschirm hinzufügen"</b> wählen.</li>
                </ol>

                <V1Alert
                  tone="neutral"
                  message={'Voraussetzung: am Add-on muss „In Seitenleiste anzeigen" eingeschaltet sein. '
                    + 'Nur dann legt Home Assistant überhaupt eine Adresse für Grow OS an — sonst führt der Code auf eine leere Seite.'} />

                <V1Field
                  label="Adresse von Home Assistant"
                  hint="So, wie du Home Assistant im Netzwerk erreichst. Eine feste IP ist am zuverlässigsten.">
                  <input
                    value={host}
                    onChange={(event) => { setHost(event.target.value); setError(null) }}
                    placeholder="http://192.168.178.68:8123"
                    inputMode="url"
                    aria-label="Adresse von Home Assistant" />
                </V1Field>

                {verdict?.warning && (
                  <V1Alert tone={verdict.usable ? 'neutral' : 'warn'} message={verdict.warning} />
                )}

                {url && (
                  <div className="mo-url">
                    <code>{url}</code>
                    <V1Button onClick={() => void kopieren()}>{kopiert ? 'Kopiert' : 'Kopieren'}</V1Button>
                  </div>
                )}
              </div>
            </div>
          </V1Section>

          <V1Section title="Warum nicht einfach die Adresse aus der Adresszeile">
            <p className="mo-note">
              Weil sie nicht hält. Grow OS wird über den Ingress von Home Assistant ausgeliefert, und
              dieser Pfad trägt ein Token, das sich bei jeder Anfrage ändert. Als Lesezeichen abgelegt
              führt er morgen ins Leere. Der Code oben zeigt stattdessen auf den Seitenleisten-Eintrag
              <code>{access.panelPath}</code> — dieselbe Adresse, die du bekommst, wenn du Grow OS in
              der Seitenleiste von Home Assistant anklickst. Die bleibt.
            </p>
            <p className="mo-note">
              Was dabei bleibt: der schmale Rahmen von Home Assistant um Grow OS herum, und das Icon
              auf dem Startbildschirm gehört Home Assistant, nicht uns. Beides liegt daran, dass die
              äussere Seite Home Assistant gehört — und dass Home Assistant die Anmeldung besitzt.
              Eine eigene Kachel ohne diesen Rahmen ginge nur mit einem eigenen Port, und dann bräuchte
              Grow OS eine eigene Anmeldung.
            </p>
          </V1Section>
        </>
      )}
    </>
  )
}

export default AppEinrichten
