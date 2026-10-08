import { useEffect, useMemo, useState } from 'react'
import { apiFetch } from '../../api'
import { V1Alert, V1Button, V1Page, V1Skeleton, V1Tabs } from '../../components/v1'
import { zahl } from './entfeuchter-band'
import { aenderungsListe, planHinweisZeigen, tempGrenzen } from './entfeuchter-zusatz'
import { ENTFEUCHTUNG_REITER, zusatzVorhanden } from './entfeuchtung-modell'
import type { EntfeuchtungReiter } from './entfeuchtung-modell'
import { EinrichtungTab } from './EntfeuchtungEinrichtung'
import { RegelTab } from './EntfeuchtungRegel'
import { SchutzTab } from './EntfeuchtungSchutz'
import { UeberblickTab } from './EntfeuchtungUeberblick'
import { tempMax } from './entfeuchter-band'
import { useFehlerZeigen } from './fehler-reiter'
import { SteuerungWechsel } from './SteuerungWechsel'
import { useEntfeuchterHaupt } from './useEntfeuchterHaupt'
import { useEntfeuchterZusatz } from './useEntfeuchterZusatz'
import type { EntfeuchterNamen, SteuerungModul } from './steuerung-typen'
import './steuerung.css'

/**
 * Fork AI (A-015, Mockup freigegeben von Bru am 08.10.2026): Steuerung › Entfeuchtung.
 *
 * <b>Ein Eintrag für alle Entfeuchter.</b> Hauptentfeuchter und Zusatz-Entfeuchter
 * stehen auf einer Seite mit den Reitern Überblick · Regel · Schutz · Einrichtung.
 * Der Überblick ist nur zum Lesen; Einstellbares steht in Regel und Schutz, nach
 * Regeln geordnet (je Regel dieselben Felder für jedes Gerät).
 *
 * <b>Gespeichert wird weiter über die zwei bekannten Wege</b> — der Hauptentfeuchter
 * schreibt alle seine Helfer, der Zusatz nur die geänderten Felder (A-009). Ein Knopf
 * „Speichern" ruft beide nacheinander auf.
 */
export default function EntfeuchtungSeite({ module, aktiv, onWechsel }: {
  module: SteuerungModul[]
  aktiv: string
  onWechsel: (kennung: string) => void
}) {
  const [reiter, setReiter] = useState<EntfeuchtungReiter>('ueberblick')
  const fehlerZeigen = useFehlerZeigen(reiter, setReiter, ENTFEUCHTUNG_REITER)
  const h = useEntfeuchterHaupt()
  const z = useEntfeuchterZusatz()
  const [namen, setNamen] = useState<EntfeuchterNamen | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    void apiFetch<EntfeuchterNamen>('/api/steuerung/entfeuchter-namen', { signal: controller.signal })
      .then((n) => { if (!controller.signal.aborted) setNamen(n) })
      .catch(() => undefined)
    return () => controller.abort()
  }, [])

  const hatZusatz = zusatzVorhanden(z.seite?.live)
  const zusatzModell = hatZusatz ? z : null
  const haupt = namen?.fuehrung.anzeigename ?? z.seite?.live.fuehrungName ?? 'Entfeuchter'
  const zusatz = namen?.zusatz.anzeigename ?? z.seite?.live.zusatzName ?? 'Zusatz-Entfeuchter'

  const geaendert = h.geaendert || (hatZusatz && z.geaendert)
  const arbeitet = h.arbeitet || z.arbeitet

  const speichern = async () => {
    let ok = true
    if (h.geaendert) ok = (await h.speichern()) && ok
    if (hatZusatz && z.geaendert) ok = (await z.speichern()) && ok
    // Beide lesen dieselben Helfer (Höchsttemperatur): nach dem Speichern den anderen Stand frisch holen, damit
    // kein veralteter Entwurf später etwas zurückschreibt.
    await Promise.all([h.auffrischen(), hatZusatz ? z.auffrischen() : Promise.resolve()])
    if (!ok) fehlerZeigen()
  }

  const zeilen = useMemo(() => {
    if (!hatZusatz || !z.geladen || !z.anzeige || !z.seite) return []
    return aenderungsListe(z.geladen, z.anzeige, { tag: z.seite.live.planLuftTagC, nacht: z.seite.live.planLuftNachtC })
  }, [hatZusatz, z.geladen, z.anzeige, z.seite])

  if ((h.laedt && !h.seite) || (z.laedt && !z.seite)) {
    return <V1Page eyebrow="Betrieb" title="Entfeuchtung" vorKopf={<SteuerungWechsel module={module} aktiv={aktiv} onWechsel={onWechsel} />}><V1Skeleton rows={4} tiles={1} label="Wird geladen" /></V1Page>
  }
  if (!h.seite || !h.entwurf || !h.anzeige) {
    return (
      <V1Page eyebrow="Betrieb" title="Entfeuchtung" vorKopf={<SteuerungWechsel module={module} aktiv={aktiv} onWechsel={onWechsel} />}>
        {h.fehler && <V1Alert tone="critical" message={h.fehler} />}
      </V1Page>
    )
  }

  const hl = h.seite.live
  const zl = hatZusatz ? z.seite?.live ?? null : null
  const a = h.anzeige
  const aktivMax = hl.tagPhase === false
    ? tempMax(a.tempMaxNachtModus, a.tempMaxNachtAbstandK, a.tempMaxNachtFestC, hl.planLuftNachtC)
    : tempMax(a.tempMaxTagModus, a.tempMaxTagAbstandK, a.tempMaxTagFestC, hl.planLuftTagC)
  const grenzen = hatZusatz && z.anzeige ? tempGrenzen(z.anzeige, aktivMax) : null
  const pausiert = Boolean(zl && grenzen && zl.tempC != null && zl.tempC > grenzen.folgeAus && z.geladen && z.geladen.hilfe !== 'aus' && z.geladen.automatikAktiv && zl.zusatzAn !== true)

  const subtitle = hatZusatz
    ? 'Haupt- und Zusatz-Entfeuchter arbeiten zusammen — geregelt in Home Assistant'
    : 'Trotec am RDWC-Zelt — geregelt in Home Assistant'
  const fehler = h.fehler ?? (hatZusatz ? z.fehler : null)
  const meldung = h.meldung ?? (hatZusatz ? z.meldung : null)

  return (
    <V1Page
      eyebrow="Betrieb"
      title="Entfeuchtung"
      subtitle={subtitle}
      vorKopf={<SteuerungWechsel module={module} aktiv={aktiv} onWechsel={onWechsel} />}
      action={geaendert ? <V1Button variant="primary" onClick={speichern} disabled={arbeitet}>{arbeitet ? 'Speichert …' : 'Speichern'}</V1Button> : undefined}
    >
      {fehler && <V1Alert tone="critical" message={fehler} />}
      {meldung && <V1Alert tone={meldung === 'Gespeichert.' ? 'ok' : 'warn'} message={meldung} />}
      {!hl.haErreichbar && <V1Alert title="Home Assistant antwortet nicht" message="Die Werte sind der letzte bekannte Stand. Die Regelung läuft dort weiter." />}
      {hl.portOnline === false && <V1Alert tone="warn" title={`${haupt} offline`} message="Der Port meldet sich nicht. Geschaltet wird erst, wenn er wieder da ist." />}
      {hl.automatikAn === false && <V1Alert tone="warn" title={`Automatik aus: ${haupt}`} message={`Die Regelung ist angehalten. ${haupt} bleibt, wie er gerade steht.`} />}
      {zl && zl.zusatzOnline === false && <V1Alert tone="warn" title={`${zusatz} ist offline`} message="Die Steckdose meldet sich nicht. Geschaltet wird erst, wenn sie wieder da ist." />}
      {zl && zl.automatikAn === false && <V1Alert tone="warn" title={`Automatik aus: ${zusatz}`} message={`Die Regelung ist angehalten. ${zusatz} bleibt, wie er gerade steht.`} />}
      {zl && planHinweisZeigen(zl) && (
        <V1Alert tone="warn" title="Plan unvollständig" message="Der Plan liefert weder ein VPD-Ziel noch eine Luftfeuchte — der Fork findet keine Größe, nach der der Zusatz schalten könnte. Bitte den Plan ergänzen." />
      )}
      {zl && zl.ziehtNichts === true && (
        <V1Alert tone="warn" title={`${zusatz} zieht nichts`} message={`Der Shelly ist an, das Gerät nimmt aber ${zl.leistungW == null ? 'kaum etwas' : `nur ${zahl(zl.leistungW, 0)} W`} auf. Tank voll oder Gerät ausgeschaltet?`} />
      )}
      {pausiert && grenzen && (
        <V1Alert tone="warn" title={`${zusatz} pausiert: Zelt zu warm`} message={`Er bleibt aus, bis das Zelt wieder unter ${zahl(grenzen.wiederEin)} °C liegt.`} />
      )}

      {zeilen.length > 0 && (
        <div className="ez-aender" data-audit="zusatz-aenderungen">
          <b>Wird gespeichert — nur das:</b>
          <ul>
            {zeilen.map((zeile) => (
              <li key={zeile.feld}>
                {zeile.label}: {zeile.von} → <b>{zeile.nach}</b>
                {zeile.folge && <small> ({zeile.folge})</small>}
              </li>
            ))}
          </ul>
          Alles andere bleibt, wie es ist.
        </div>
      )}

      <V1Tabs items={ENTFEUCHTUNG_REITER} active={reiter} onChange={setReiter} label="Bereich" insBild />

      {reiter === 'ueberblick' && <UeberblickTab h={h} z={zusatzModell} haupt={haupt} zusatz={zusatz} />}
      {reiter === 'regel' && <RegelTab h={h} z={zusatzModell} haupt={haupt} zusatz={zusatz} />}
      {reiter === 'schutz' && <SchutzTab h={h} z={zusatzModell} haupt={haupt} zusatz={zusatz} />}
      {reiter === 'einrichtung' && <EinrichtungTab h={h} z={zusatzModell} haupt={haupt} zusatz={zusatz} />}
    </V1Page>
  )
}
