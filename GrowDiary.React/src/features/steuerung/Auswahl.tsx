import { useState } from 'react'
import { V1Alert, V1Button, V1Card, V1Section, V1Switch } from '../../components/v1'
import type { SteuerungAuswahl, SteuerungKarte } from './steuerung-typen'

/**
 * Fork AI (A-016): Steuerungen, die der Nutzer noch nicht hat — mit einem Knopf, sie einzurichten.
 *
 * „Einrichten" wählt die Steuerung aus und öffnet ihre Seite; dort werden die Geräte zugeordnet und
 * fehlende Bausteine in Home Assistant angelegt. Gelöscht oder geschaltet wird hier nichts.
 */
export function NichtEingerichtet({ karten, arbeitet, onEinrichten }: {
  karten: SteuerungKarte[]
  arbeitet: boolean
  onEinrichten: (kennung: string) => void
}) {
  if (karten.length === 0) return null
  return (
    <V1Section title="Nicht eingerichtet">
      <div className="st-liste">
        {karten.map((k) => (
          <div className="st-zeile st-zeile-leer" key={k.kennung}>
            <span className="st-zeile-links">
              <i className="st-punkt is-leer" aria-hidden="true" />
              <span className="st-titel">
                {k.titel}
                <small>{k.beschreibung}</small>
              </span>
            </span>
            <V1Button variant="secondary" onClick={() => onEinrichten(k.kennung)} disabled={arbeitet}>
              Einrichten
            </V1Button>
          </div>
        ))}
      </div>
    </V1Section>
  )
}

/**
 * Die Auswahl der Steuerungen: je eine Zeile zum Ein- und Ausschalten. Hinter einem Knopf, weil sie nur
 * selten gebraucht wird — wer alles hat, sieht sie nie.
 */
export function AuswahlFeld({ auswahl, fehler, onUmschalten }: {
  auswahl: SteuerungAuswahl | null
  fehler: string | null
  onUmschalten: (kennung: string, gewaehlt: boolean) => void
}) {
  const [offen, setOffen] = useState(false)
  if (!auswahl) return null

  return (
    <div className="st-auswahl">
      <V1Button variant="ghost" onClick={() => setOffen((o) => !o)}>
        {offen ? 'Auswahl schließen' : 'Meine Steuerungen wählen'}
      </V1Button>
      {offen && (
        <V1Card>
          <p className="st-hinweis">
            Hier stehen nur Steuerungen, die du hast. Abwählen blendet eine Steuerung aus — die Regelung in
            Home Assistant läuft weiter, gelöscht wird nichts.
          </p>
          {fehler && <V1Alert tone="critical" message={fehler} />}
          {auswahl.eintraege.map((e) => (
            <V1Switch
              key={e.kennung}
              label={e.titel}
              checked={e.gewaehlt}
              onChange={(v) => onUmschalten(e.kennung, v)}
              hint={e.gewaehlt && e.pflichtGesamt > 0
                ? `${e.beschreibung} · ${e.pflichtZugeordnet} von ${e.pflichtGesamt} nötigen Geräten zugeordnet`
                : e.beschreibung}
            />
          ))}
        </V1Card>
      )}
    </div>
  )
}
