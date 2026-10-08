import { V1Alert, V1Button, V1Card, V1LinkButton } from '../../components/v1'
import { rollenPfad } from '../geraete/rollenPfad'
import { GeraeteZeile } from './EntfeuchtungBausteine'
import type { Ctx } from './EntfeuchtungBausteine'
import { Klappkachel } from './Klappkachel'
import type { EntfeuchterZusatzLive } from './steuerung-typen'
import './steuerung.css'

/**
 * Fork AI (A-015): Reiter „Einrichtung" — welche Entfeuchter es gibt und wie sie arbeiten.
 *
 * <b>Der Nutzer sagt, wie viele Entfeuchter er hat</b> (Bru, 08.10.2026): Der Hauptentfeuchter ist
 * immer da; der Zusatz-Entfeuchter lässt sich an- und abwählen. Danach richten sich die Seite und
 * die Steuerungs-Übersicht. Ohne Angabe schließt der Fork es aus der Zuordnung in „Geräte & Entitäten".
 * Die Regelung in Home Assistant wird dabei NICHT angefasst — wer den Zusatz abwählt, obwohl er
 * physisch läuft, bekommt einen Hinweis.
 *
 * <b>Die Geräte selbst (Namen, Zuordnung) pflegt Bru nur in „Geräte & Entitäten".</b>
 * Eine Frage nach dem Standort (Zelt/außerhalb) gibt es nicht: für die Regelung spielt sie keine Rolle.
 *
 * <b>Stand heute:</b> zusammen arbeiten (einer führt, der andere hilft) ist die einzige Regelung.
 * „Getrennt" braucht eine zweite Regelvariante in Home Assistant (A-015, Stufe 3).
 */
export function EinrichtungTab({ h, z, haupt, zusatz, zusatzLive, zusatzGesagt, zusatzAuto, arbeitet, fehler, onZusatz }: Ctx & {
  zusatzLive: EntfeuchterZusatzLive | null
  /** Was der Nutzer gesagt hat: true/false, oder null (nichts gesagt, die Zuordnung entscheidet). */
  zusatzGesagt: boolean | null
  /** Was die Zuordnung ergibt. */
  zusatzAuto: boolean
  arbeitet: boolean
  fehler: string | null
  onZusatz: (wert: boolean | null) => void
}) {
  const hs = h.seite!
  const zs = z?.seite ?? null
  const hatZusatz = zusatzGesagt ?? zusatzAuto
  const zusatzLaeuft = zusatzLive?.zusatzAn != null
  return (
    <>
      <Klappkachel titel="Welche Entfeuchter hast du?" zusammenfassung={hatZusatz ? `2 Entfeuchter: ${haupt} · ${zusatz}` : `1 Entfeuchter: ${haupt}`}>
        <V1Card>
          <p className="st-hinweis">Hier sagst du, wie viele Entfeuchter du hast. Danach richten sich diese Seite und die Übersicht der Steuerungen. Namen und Zuordnung der Geräte pflegst du in <b>Geräte &amp; Entitäten</b>.</p>
          {fehler && <V1Alert tone="critical" message={fehler} />}
          <label className="v1-switch">
            <input type="checkbox" checked disabled readOnly />
            <span><strong>{haupt}</strong><small>Hauptentfeuchter — immer dabei</small></span>
          </label>
          <label className="v1-switch">
            <input type="checkbox" checked={hatZusatz} disabled={arbeitet} onChange={(e) => onZusatz(e.target.checked)} />
            <span><strong>{zusatz}</strong><small>Zusatz-Entfeuchter — hilft dem Hauptentfeuchter</small></span>
          </label>
          <div className="st-feldzeile">
            <span className="st-etikett">
              {zusatzGesagt == null ? 'Der Fork schließt es aus der Zuordnung' : 'Du hast es ausdrücklich gesagt'}
              <small>{zusatzGesagt == null ? `Gefunden: ${zusatzAuto ? 'ein Zusatz-Entfeuchter' : 'kein Zusatz-Entfeuchter'}.` : 'Zurück auf „automatisch" lässt den Fork wieder nach der Zuordnung entscheiden.'}</small>
            </span>
            {zusatzGesagt != null && <V1Button variant="ghost" onClick={() => onZusatz(null)} disabled={arbeitet}>Automatisch</V1Button>}
          </div>
          {!hatZusatz && zusatzLaeuft && (
            <V1Alert
              tone="warn"
              title="Der Zusatz-Entfeuchter läuft in Home Assistant weiter"
              message="Abwählen blendet ihn nur aus; seine Regelung wird hier nicht angehalten. Wenn es ihn nicht mehr gibt, schalte seine Automatik in Home Assistant aus."
            />
          )}
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
