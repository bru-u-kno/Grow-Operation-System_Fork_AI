import { useEffect, useMemo, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { V1Alert, V1Button, V1Card, V1Page, V1Section, V1Skeleton, V1Switch } from '../../components/v1'
import { RollenReiter } from '../geraete/RollenReiter'
import { Bereitstellen } from './Bereitstellen'
import { rollenModule } from './bereitstellen-ablauf'
import { useEntfeuchtungEinrichtung } from './useEntfeuchtungEinrichtung'
import { useSteuerungAuswahl } from './useSteuerungAuswahl'
import './steuerung.css'

/**
 * Fork AI (A-016, Etappe 3): Der Einrichtungs-Assistent — drei Schritte von „Was habe ich?" bis „Bereit".
 *
 * <ol>
 * <li><b>Geräte:</b> welche Steuerungen der Nutzer hat. Was nicht angekreuzt ist, wird nie angelegt.</li>
 * <li><b>Zuordnen:</b> welche Entität seines Home Assistant welche Aufgabe übernimmt (die Rollen-Zuordnung der Geräteseite).</li>
 * <li><b>Bereitstellen:</b> was in Home Assistant noch fehlt, wird angelegt — Automationen erst nach Vorschau und Zustimmung.</li>
 * </ol>
 *
 * Der Schritt steht in der Adresse (`?schritt=2`): Zurück im Browser geht einen Schritt zurück, und ein Neuladen
 * landet nicht wieder bei 1. Jederzeit wieder aufrufbar — auch für ein neues Gerät später.
 */

const SCHRITTE = [
  { nr: 1, titel: 'Geräte' },
  { nr: 2, titel: 'Zuordnen' },
  { nr: 3, titel: 'Bereitstellen' },
] as const

export default function EinrichtungSeite() {
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const schrittRoh = Number(params.get('schritt'))
  const schritt = schrittRoh >= 1 && schrittRoh <= 3 ? schrittRoh : 1
  const zu = (nr: number) => setParams({ schritt: String(nr) })

  const { auswahl, fehler, arbeitet, umschalten } = useSteuerungAuswahl()
  const { einrichtung, fehler: zusatzFehler, zusatzSetzen } = useEntfeuchtungEinrichtung()
  const zusatz = einrichtung?.zusatzVorhanden === true

  // Schritt 2: Änderungen, die noch nicht gespeichert sind, gingen mit Weiter verloren.
  const [ungespeichert, setUngespeichert] = useState(false)

  const gewaehlt = useMemo(() => (auswahl?.eintraege ?? []).filter((e) => e.gewaehlt), [auswahl])

  // Ein neuer Schritt beginnt oben — nicht dort, wo der Knopf des letzten stand.
  useEffect(() => { window.scrollTo({ top: 0 }) }, [schritt])

  // Schritt 2/3 ohne Auswahl ergeben nichts — dann zurück zu 1.
  useEffect(() => {
    if (auswahl && gewaehlt.length === 0 && schritt > 1) setParams({ schritt: '1' }, { replace: true })
  }, [auswahl, gewaehlt.length, schritt, setParams])

  return (
    <V1Page eyebrow="Steuerung" title="Steuerungen einrichten" subtitle="Sag, was du hast — der Rest wird nicht angelegt">
      <ol className="st-schritte" aria-label="Schritte">
        {SCHRITTE.map((s) => (
          <li key={s.nr} className={s.nr === schritt ? 'is-jetzt' : s.nr < schritt ? 'is-fertig' : ''} aria-current={s.nr === schritt ? 'step' : undefined}>
            <b>{s.nr}</b> {s.titel}
          </li>
        ))}
      </ol>

      {!auswahl ? (
        <V1Skeleton rows={4} label="Steuerungen werden geladen" />
      ) : schritt === 1 ? (
        <V1Section title="Welche Steuerungen hast du?">
          {(fehler ?? zusatzFehler) && <V1Alert tone="critical" message={(fehler ?? zusatzFehler)!} />}
          <V1Card>
            {auswahl.eintraege.map((e) => (
              <div key={e.kennung}>
                <V1Switch label={e.titel} checked={e.gewaehlt} hint={e.beschreibung} onChange={(v) => void umschalten(e.kennung, v)} />
                {e.kennung === 'entfeuchter' && e.gewaehlt && (
                  <div className="st-eingerueckt">
                    <V1Switch
                      label="Zweiter Entfeuchter"
                      checked={zusatz}
                      hint="Ein Folgegerät neben dem ersten — es hilft nur dazu und geht nie zugleich mit ihm aus."
                      onChange={(v) => void zusatzSetzen(v)}
                    />
                  </div>
                )}
              </div>
            ))}
          </V1Card>
          <p className="st-hinweis">
            Abwählen blendet eine Steuerung nur aus — die Automationen in Home Assistant laufen weiter, gelöscht wird nichts.
          </p>
          <div className="st-knopfleiste">
            <V1Button variant="ghost" onClick={() => navigate('/steuerung')}>Abbrechen</V1Button>
            <V1Button variant="primary" disabled={gewaehlt.length === 0 || arbeitet} onClick={() => zu(2)}>Weiter</V1Button>
          </div>
          {gewaehlt.length === 0 && <p className="st-hinweis">Wähle mindestens eine Steuerung aus.</p>}
        </V1Section>
      ) : schritt === 2 ? (
        <>
          <RollenReiter nurModule={rollenModule(gewaehlt, zusatz)} schlank onUngespeichert={setUngespeichert} />
          <p className="st-hinweis">Pflichtgeräte sind nötig, damit die Steuerung bereitgestellt wird; optionale darfst du leer lassen.</p>
          {ungespeichert && <V1Alert tone="warn" message="Du hast Änderungen noch nicht gespeichert. Erst Speichern, dann Weiter." />}
          <div className="st-knopfleiste">
            <V1Button variant="ghost" onClick={() => zu(1)}>Zurück</V1Button>
            <V1Button variant="primary" disabled={ungespeichert} onClick={() => zu(3)}>Weiter</V1Button>
          </div>
        </>
      ) : (
        <>
          <Bereitstellen zusatz={zusatz} />
          <div className="st-knopfleiste">
            <V1Button variant="ghost" onClick={() => zu(2)}>Zurück</V1Button>
            <Link className="v1-button is-secondary" to="/steuerung">Zur Übersicht</Link>
          </div>
        </>
      )}
    </V1Page>
  )
}
