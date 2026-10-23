import { useEffect, useState } from 'react'
import { apiFetch } from '../../api'
import { KlappKopf } from '../../components/Klapp'
import { V1Card, V1Section } from '../../components/v1'

type Vorschlag = { id: string; name: string; art: string }
type Beobachtung = {
  id: string
  name: string
  moeglicheUrsachen: string[]
  selbstPruefen: string[]
  vorschlaege: Vorschlag[]
}
type Gruppe = { bereich: string; frage: string; beobachtungen: Beobachtung[] }

/**
 * Die Diagnose von der Pflanze her.
 *
 * Der Rest dieser Seite stellt auf Zahlen ab — pH-Geschwindigkeit,
 * EC-Verhalten, Sauerstoff, ORP. Wer ein gelbes Blatt sieht, findet dort
 * nichts. Dabei liegen die Symptome und Behandlungen längst im Wissen; man
 * musste nur wissen, wonach man sucht. Genau diese Voraussetzung nimmt dieser
 * Weg weg: hinschauen, anklicken, lesen.
 *
 * Keine KI. Drei Fragen und eine Liste.
 */
export function ObservationGuide() {
  const [gruppen, setGruppen] = useState<Gruppe[] | null>(null)
  const [bereich, setBereich] = useState<string | null>(null)
  const [offen, setOffen] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    apiFetch<Gruppe[]>('/api/observations', { signal: controller.signal })
      .then((geladen) => { if (!controller.signal.aborted) setGruppen(geladen) })
      .catch(() => { /* Ohne Wissen kein Wegweiser. */ })
    return () => controller.abort()
  }, [])

  if (!gruppen || gruppen.length === 0) return null
  const gewaehlt = gruppen.find((gruppe) => gruppe.bereich === bereich) ?? null

  return (
    <V1Section title="Mir gefällt was nicht">
      <V1Card>
        <div data-audit="observation-guide">
          <p className="gc-facts">
            Alles darüber kommt aus Messwerten. Hier fängst du bei dem an, was du <strong>siehst</strong>.
          </p>

          <div className="og-bereiche">
            {gruppen.map((gruppe) => (
              <button
                key={gruppe.bereich}
                type="button"
                className={`ls-btn is-small${gruppe.bereich === bereich ? ' is-primary' : ''}`}
                onClick={() => { setBereich(gruppe.bereich === bereich ? null : gruppe.bereich); setOffen(null) }}
              >
                {gruppe.bereich}
              </button>
            ))}
          </div>

          {gewaehlt && (
            <>
              <p className="og-frage">{gewaehlt.frage}</p>
              <ul className="og-liste">
                {gewaehlt.beobachtungen.map((beobachtung) => (
                  <li key={beobachtung.id}>
                    <KlappKopf
                      className="og-eintrag"
                      zu={offen !== beobachtung.id}
                      onUmschalten={() => setOffen(offen === beobachtung.id ? null : beobachtung.id)}
                      titel={beobachtung.name}
                      titelNormal
                      kurz={`${beobachtung.moeglicheUrsachen.length} ${beobachtung.moeglicheUrsachen.length === 1 ? 'Ursache' : 'Ursachen'} · ${beobachtung.vorschlaege.length} Mittel`}
                    />

                    {offen === beobachtung.id && (
                      <div className="og-detail">
                        <div className="og-block">
                          <span className="og-block-head">Woran es liegen kann</span>
                          <ul>{beobachtung.moeglicheUrsachen.map((ursache) => <li key={ursache}>{ursache}</li>)}</ul>
                        </div>

                        {beobachtung.selbstPruefen.length > 0 && (
                          <div className="og-block">
                            <span className="og-block-head">Selbst prüfen</span>
                            <ul>{beobachtung.selbstPruefen.map((pruefung) => <li key={pruefung}>{pruefung}</li>)}</ul>
                          </div>
                        )}

                        <div className="og-block">
                          <span className="og-block-head">Was dagegen hilft</span>
                          <ul>
                            {beobachtung.vorschlaege.map((vorschlag) => (
                              <li key={vorschlag.id}>
                                {vorschlag.name} <em className="og-art">{vorschlag.art}</em>
                              </li>
                            ))}
                          </ul>
                        </div>
                      </div>
                    )}
                  </li>
                ))}
              </ul>
            </>
          )}
        </div>
      </V1Card>
    </V1Section>
  )
}
