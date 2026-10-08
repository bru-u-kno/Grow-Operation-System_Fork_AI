import { V1Card, V1LinkButton } from '../../components/v1'
import { rollenPfad } from '../geraete/rollenPfad'
import { GeraeteZeile } from './EntfeuchtungBausteine'
import type { Ctx } from './EntfeuchtungBausteine'
import { Klappkachel } from './Klappkachel'
import './steuerung.css'

/**
 * Fork AI (A-015): Reiter „Einrichtung" — welche Entfeuchter es gibt und wie sie arbeiten.
 *
 * <b>Die Geräte kommen aus „Geräte & Entitäten"</b> (Namen und Zuordnung pflegt
 * Bru nur dort). Hier steht, was der Fork dort gefunden hat. Eine Frage nach dem
 * Standort (Zelt/außerhalb) gibt es nicht: für die Regelung spielt sie keine Rolle.
 *
 * <b>Stand heute:</b> zusammen arbeiten (einer führt, der andere hilft) ist die
 * einzige Regelung. „Getrennt" braucht eine zweite Regelvariante in Home
 * Assistant (Aufgabe A-015, Stufe 3) und steht deshalb als gesperrter Schalter da.
 */
export function EinrichtungTab({ h, z, haupt, zusatz }: Ctx) {
  const hs = h.seite!
  const zs = z?.seite ?? null
  return (
    <>
      <Klappkachel titel="Welche Entfeuchter hast du?" zusammenfassung={z ? `2 Entfeuchter: ${haupt} · ${zusatz}` : `1 Entfeuchter: ${haupt}`}>
        <V1Card>
          <p className="st-hinweis">Die Geräte kommen aus <b>Geräte &amp; Entitäten</b> — Namen und Zuordnung pflegst du nur dort. Hier siehst du, was der Fork gefunden hat.</p>
          <label className="v1-switch"><input type="checkbox" checked disabled readOnly /><span><strong>{haupt}</strong><small>{z ? 'Haupt-Entfeuchter — führt' : 'Entfeuchter'}</small></span></label>
          {z && <label className="v1-switch"><input type="checkbox" checked disabled readOnly /><span><strong>{zusatz}</strong><small>Zusatz-Entfeuchter — hilft</small></span></label>}
          <div className="st-feldzeile">
            <span className="st-etikett">Noch ein Entfeuchter?<small>Erst in Geräte &amp; Entitäten anlegen und zuordnen.</small></span>
            <V1LinkButton to={rollenPfad('entfeuchter')} variant="ghost">Geräte &amp; Entitäten ›</V1LinkButton>
          </div>
        </V1Card>
      </Klappkachel>

      {z && (
        <Klappkachel titel="Wie arbeiten die Entfeuchter?" zusammenfassung="zusammen — einer führt, der andere hilft">
          <V1Card>
            <label className="v1-switch ef-schalter">
              <input type="checkbox" checked disabled readOnly />
              <span>
                <strong>Der Zusatz hilft dem Haupt-Entfeuchter</strong>
                <small>
                  <span className="ef-zeile"><b>An:</b> {haupt} führt, {zusatz} springt zu, wenn er eine Weile läuft, und geht früher aus. Beide teilen die Höchsttemperatur.</span>
                  <span className="ef-zeile"><b>Aus:</b> Jeder regelt sich selbst. Das gibt es noch nicht — es braucht eine zweite Regelvariante in Home Assistant.</span>
                </small>
              </span>
            </label>
            <p className="ef-folge"><b>So arbeiten beide heute.</b> Beide Geräte dürfen nie gleichzeitig ausgehen, außer das Zelt ist zu warm.</p>
          </V1Card>
        </Klappkachel>
      )}

      <Klappkachel titel="Zuordnung" zusammenfassung={`${hs.geraeteZugeordnet} / ${hs.geraeteGesamt}${zs ? ` · ${zs.geraeteZugeordnet} / ${zs.geraeteGesamt}` : ''}`} offen={false}>
        <V1Card>
          <GeraeteZeile zugeordnet={hs.geraeteZugeordnet} gesamt={hs.geraeteGesamt} pfad={rollenPfad('entfeuchter')} label={haupt} />
          {zs && <GeraeteZeile zugeordnet={zs.geraeteZugeordnet} gesamt={zs.geraeteGesamt} pfad={rollenPfad('entfeuchter-zusatz')} label={zusatz} />}
        </V1Card>
      </Klappkachel>
    </>
  )
}
