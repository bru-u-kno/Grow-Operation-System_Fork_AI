import { useState, type ReactNode } from 'react'
import { apiFetch, formatApiError } from '../../api'
import { V1Alert, V1Button, V1Card, V1Section } from '../../components/v1'
import type { Bestandsaufnahme } from './steuerung-typen'

/**
 * Fork AI: Was eine Steuerung in Home Assistant braucht — und was davon fehlt.
 *
 * <b>Herkunft.</b> Stand bis 02.10.2026 nur auf der CO₂-Seite, fest auf
 * `co2` verdrahtet. Die Kühler-Seite zeigte den Bestand gar nicht — auch nicht
 * die Lücke „Kühler ohne Aus" (Sollwert-Eingang ohne Steckdose), die
 * `GET /api/steuerung/chiller/bestand` seither meldet. Jetzt lesen beide Seiten
 * dieselben Bauteile; die Endpunkte (`{modul}/bestand`, `/helfer`,
 * `/rechenwerte`, `/automationen`) sind ohnehin je Modul.
 */

/** Was der Vorschau-Lauf über die Automationen meldet. */
type AutoBilanz = {
  angelegt: number
  fremd: number
  fehlgeschlagen: number
  /** `titel` ist der Name aus der Vorlage; `name` nur der Dateiname, zum Abgleich. */
  einzeln: Array<{ kennung: string; name: string; titel: string | null; stand: string; hinweis: string | null }>
}

/**
 * Fork AI (forkai.45): Die Bauteil-Arten in Klartext.
 *
 * Die Namen aus dem Katalog sind die Namen der Helfer in Home Assistant — gut
 * zum Anlegen, aber nichts, was jemand lesen will. Auf der Seite steht deshalb
 * der Zweck, nicht der Bezeichner.
 */
const STAND_LESBAR: Record<string, string> = {
  Angelegt: 'wird angelegt',
  Erneuert: 'wird erneuert',
  Fremd: 'bleibt unangetastet',
  OhneGeraet: 'entfällt',
  Fehlgeschlagen: 'fehlgeschlagen',
}

const ART_LESBAR: Record<string, string> = {
  Zahl: 'Einstellwert',  // steht in der breiten Zeile, nicht in einer engen Spalte
  Schalter: 'Schalter',
  Zeitpunkt: 'Zeitstempel',
  Zaehler: 'Zähler',
  RechenSensor: 'Rechenwert',
  RechenSchalter: 'Rechenwert',
  Mittelwert: 'Mittelwert',
  Automation: 'Automation',
}

/**
 * Die Meldung oben: was fehlt, was nicht zugeordnet ist und welche Funktion
 * dadurch ausfällt.
 *
 * Auch ohne fehlendes Bauteil, wenn eine Funktion ausfällt — die Lücke „Kühler
 * ohne Aus" hängt an der Art des Geräts, nicht an einem fehlenden Helfer, und
 * wäre sonst bei vollständigem Bestand unsichtbar.
 */
export function BestandHinweis({ bestand }: { bestand: Bestandsaufnahme | null }) {
  if (!bestand?.haErreichbar) return null
  const { fehlt, da, fehlendeRollen, ausgefalleneFunktionen } = bestand
  if (fehlt === 0 && fehlendeRollen.length === 0 && ausgefalleneFunktionen.length === 0) return null

  const titel = fehlt > 0
    ? `Noch nicht vollständig eingerichtet — ${fehlt} von ${fehlt + da} Bauteilen fehlen`
    : fehlendeRollen.length > 0
      ? 'Noch nicht vollständig eingerichtet'
      : ausgefalleneFunktionen.length === 1
        ? 'Eingerichtet — mit einer Lücke'
        : `Eingerichtet — mit ${ausgefalleneFunktionen.length} Lücken`
  return (
    <V1Alert
      tone="warn"
      title={titel}
      message={[
        fehlendeRollen.length > 0 ? `Nicht zugeordnet: ${fehlendeRollen.join(', ')}.` : null,
        ausgefalleneFunktionen.join(' '),
      ].filter(Boolean).join(' ')}
    />
  )
}

/**
 * Der Abschnitt zum Anlegen: fehlende Helfer und Rechenwerte, dann die
 * Automationen in einem eigenen Schritt — erst zeigen, dann anlegen.
 *
 * Steht auch bei einer veralteten Fassung da (01.10.2026) — sonst bekäme eine
 * bestehende Installation eine reparierte Vorlage nie angeboten.
 *
 * @param schaltet Was die Automationen schalten — steht im Hinweis zum eigenen Schritt.
 * @param children Was eine Steuerung zusätzlich braucht (die CO₂-Seite: die
 *   Probeschaltung des Ventils). Steht als letzte Karte im Abschnitt.
 */
export function BestandAbschnitt({ modul, schaltet, bestand, neuLaden, children }: {
  modul: string
  /** Was die Automationen schalten, im Akkusativ: „das Ventil", „den Kühler". */
  schaltet: string
  bestand: Bestandsaufnahme | null
  neuLaden: () => Promise<void>
  children?: ReactNode
}) {
  const [legtAn, setLegtAn] = useState(false)
  const [anlegeMeldung, setAnlegeMeldung] = useState<string | null>(null)
  const [vorschau, setVorschau] = useState<AutoBilanz | null>(null)

  if (!bestand || (bestand.fehlt === 0 && bestand.veraltet === 0)) return null

  /**
   * Die fehlenden Helfer anlegen lassen.
   *
   * Nur die einfachen Arten — Zahlen, Schalter, Zeitstempel, Zähler. Was danach
   * noch fehlt, sind Rechen-Sensoren und Automationen; die brauchen andere Wege
   * und bleiben in der Liste stehen, bis es sie gibt.
   */
  const helferAnlegen = async () => {
    setLegtAn(true)
    setAnlegeMeldung(null)
    try {
      // Erst die Helfer, dann die Rechenwerte: die Rechenwerte lesen die
      // Helfer, und ein Rechenwert vor seinem Helfer stünde kurz auf „nicht
      // verfügbar".
      const helfer = await apiFetch<{ angelegt: number; fehlgeschlagen: number }>(
        `/api/steuerung/${modul}/helfer`, { method: 'POST' },
      )
      const rechen = await apiFetch<{ angelegt: number; fehlgeschlagen: number; ohneGeraet: string[] }>(
        `/api/steuerung/${modul}/rechenwerte`, { method: 'POST' },
      )

      const teile = [`${helfer.angelegt + rechen.angelegt} angelegt`]
      const daneben = helfer.fehlgeschlagen + rechen.fehlgeschlagen
      if (daneben > 0) teile.push(`${daneben} nicht — Einzelheiten stehen im Protokoll`)
      if (rechen.ohneGeraet.length > 0) {
        teile.push(`ohne zugeordnetes Gerät übersprungen: ${rechen.ohneGeraet.join(', ')}`)
      }
      setAnlegeMeldung(`${teile.join(', ')}.`)
      await neuLaden()
    } catch (caught) {
      setAnlegeMeldung(formatApiError(caught, 'Anlegen fehlgeschlagen.'))
    } finally {
      setLegtAn(false)
    }
  }

  /**
   * Erst zeigen, dann anlegen.
   *
   * Die Automationen bekommen einen eigenen Schritt mit eigener Zustimmung —
   * an ihrem Ende hängt ein Ventil an einer Gasflasche oder ein Kompressor.
   * Der Vorschau-Lauf schreibt nichts.
   */
  const automationenZeigen = async () => {
    setLegtAn(true)
    setAnlegeMeldung(null)
    try {
      setVorschau(await apiFetch<AutoBilanz>(`/api/steuerung/${modul}/automationen?vorschau=true`, { method: 'POST' }))
    } catch (caught) {
      setAnlegeMeldung(formatApiError(caught, 'Vorschau fehlgeschlagen.'))
    } finally {
      setLegtAn(false)
    }
  }

  const automationenAnlegen = async () => {
    setLegtAn(true)
    try {
      const bilanz = await apiFetch<AutoBilanz>(`/api/steuerung/${modul}/automationen`, { method: 'POST' })
      setVorschau(null)
      setAnlegeMeldung(
        `${bilanz.angelegt} Automationen geschrieben`
        + (bilanz.fremd > 0 ? `, ${bilanz.fremd} von Hand gebaute blieben unangetastet` : '')
        + (bilanz.fehlgeschlagen > 0 ? `, ${bilanz.fehlgeschlagen} nicht` : '') + '.',
      )
      await neuLaden()
    } catch (caught) {
      setAnlegeMeldung(formatApiError(caught, 'Anlegen fehlgeschlagen.'))
    } finally {
      setLegtAn(false)
    }
  }

  return (
    <V1Section title={bestand.fehlt > 0 ? 'Was in Home Assistant fehlt' : 'Neue Fassung der Automationen'}>
      {bestand.fehlt > 0 && (
        <V1Card>
          {bestand.bauteile.filter((b) => b.stand === 'Fehlt').map((b) => (
            <div className="st-feldzeile" key={b.entityId}>
              <span className="st-etikett">
                {b.zweck}
                <small>
                  {ART_LESBAR[b.art] ?? b.art} · {b.pflicht ? 'wird gebraucht' : (b.ohneDas ?? 'optional')}
                </small>
              </span>
            </div>
          ))}
          <p className="st-hinweis">
            Diese Objekte gehören zur Steuerung selbst, nicht zu deinen Geräten. Einstellwerte, Schalter,
            Zeitstempel, Zähler und Rechenwerte legt der Fork an; die Automationen folgen.
          </p>
          {anlegeMeldung && <p className="st-hinweis">{anlegeMeldung}</p>}
          <V1Button variant="primary" onClick={helferAnlegen} disabled={legtAn}>
            {legtAn ? 'Legt an …' : 'Fehlende anlegen'}
          </V1Button>
        </V1Card>
      )}

      <V1Card>
        <div className="st-feldzeile">
          <span className="st-etikett">
            Automationen
            <small>
              Sie schalten {schaltet}. Deshalb ein eigener Schritt — erst zeigen, dann anlegen.
              Von Hand gebaute bleiben unangetastet.
            </small>
          </span>
        </div>
        {bestand.bauteile.filter((b) => b.stand === 'Veraltet').map((b) => (
          <div className="st-feldzeile" key={b.entityId}>
            <span className="st-etikett">
              {b.zweck}
              <small>Von Fork AI angelegt, aber in einer älteren Fassung. Neu anlegen ersetzt sie.</small>
            </span>
          </div>
        ))}

        {vorschau ? (
          <>
            {vorschau.einzeln.map((a) => (
              <div className="st-feldzeile" key={a.kennung}>
                <span className="st-etikett">
                  {a.titel ?? a.name}
                  <small>{a.hinweis ?? STAND_LESBAR[a.stand] ?? a.stand}</small>
                </span>
                <span className="st-nurlesen">{STAND_LESBAR[a.stand] ?? a.stand}</span>
              </div>
            ))}
            <p className="st-hinweis">
              Der vorhandene Stand wird vorher gesichert. Danach wird nachgesehen, ob Home Assistant
              die Automationen wirklich geladen hat.
            </p>
            <V1Button variant="primary" onClick={automationenAnlegen} disabled={legtAn}>
              {legtAn ? 'Schreibt …' : 'Jetzt anlegen'}
            </V1Button>
            <V1Button variant="ghost" onClick={() => setVorschau(null)} disabled={legtAn}>
              Abbrechen
            </V1Button>
          </>
        ) : (
          <V1Button variant="ghost" onClick={automationenZeigen} disabled={legtAn}>
            Zeigen, was angelegt würde
          </V1Button>
        )}
        {/* Ohne fehlende Helfer gibt es die Karte darüber nicht — die Meldung steht dann hier. */}
        {bestand.fehlt === 0 && anlegeMeldung && <p className="st-hinweis">{anlegeMeldung}</p>}
      </V1Card>

      {children}
    </V1Section>
  )
}
