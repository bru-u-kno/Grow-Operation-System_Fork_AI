import { useState } from 'react'
import { V1Alert, V1Button, V1Section } from '../../components/v1'
import { useHomeAssistantFrame } from '../../useHomeAssistantFrame'
import { inZwischenablage, waehleKarte, yamlBlock, VORGESCHLAGENER_PANEL } from './titelleiste-logik'

/**
 * „Ohne HA-Titelleiste“ — die Hilfe für alle, die den Streifen oben nicht wollen (Entwurf `titelleiste-hilfe`,
 * 19.10.2026).
 *
 * Die Leiste gehört Home Assistant; ein Add-on kann sie nicht abschalten. Nur die HACS-Integration „Ingress“ legt ein
 * Panel ohne Leiste an, und das braucht einen Eintrag in der `configuration.yaml` mit der Kennung des Add-ons. Die App
 * schreibt nichts dorthin und installiert nichts: Sie zeigt den fertigen Text mit der richtigen Kennung. Je nach Lage
 * erscheint die Anleitung, ein Hinweis auf das vorhandene Panel, eine kurze Bestätigung oder gar nichts.
 */
export function TitelleisteHilfe({ slug, panel }: { slug: string | null; panel: string | null | undefined }) {
  const frame = useHomeAssistantFrame()
  const [kopiert, setKopiert] = useState(false)
  const [fehler, setFehler] = useState(false)

  const karte = waehleKarte({ inHomeAssistant: frame.ruecksprungMoeglich, kopfleiste: !frame.vollbild, panel })
  if (karte === 'keine' || !slug) return null

  async function kopieren() {
    const geklappt = await inZwischenablage(yamlBlock(slug!))
    setKopiert(geklappt)
    setFehler(!geklappt)
    if (geklappt) window.setTimeout(() => setKopiert(false), 2000)
  }

  if (karte === 'fertig') {
    return (
      <V1Section title="Ohne HA-Titelleiste">
        <V1Alert tone="ok" title="Eingerichtet." message={`Die App läuft ohne die Titelleiste von Home Assistant. Dein Panel: /${panel}`} />
      </V1Section>
    )
  }

  if (karte === 'oeffnen') {
    return (
      <V1Section title="Ohne HA-Titelleiste">
        <V1Alert
          tone="warn"
          title="Du hast schon ein Panel ohne Titelleiste."
          message={`Öffne Cultivate Control in der Seitenleiste über den Eintrag, den du dafür eingerichtet hast (Adresse /${panel}). Den normalen Eintrag des Add-ons kannst du in der Add-on-Seite unter „In der Seitenleiste anzeigen“ ausschalten.`}
        />
      </V1Section>
    )
  }

  return (
    <V1Section title="Ohne HA-Titelleiste">
      <div className="mo-side" data-audit="titelleiste-hilfe">
        <V1Alert
          tone="warn"
          title="Über der App liegt die Titelleiste von Home Assistant."
          message="Das kostet am Handy Platz. Alles funktioniert auch so; wer die Leiste loswerden will, richtet einmal ein eigenes Panel ein."
        />
        <ol className="mo-steps">
          <li>
            <b>Integration installieren.</b> In HACS „Ingress“ (<code>lovelylain/hass_ingress</code>) herunterladen und Home
            Assistant neu starten. <a href="https://github.com/lovelylain/hass_ingress" target="_blank" rel="noreferrer">Zur Integration</a>
          </li>
          <li>
            <b>Diesen Block in die <code>configuration.yaml</code> einfügen.</b> Gibt es dort schon einen Eintrag unter
            <code> ingress:</code> für diese App, ändere nur seine Zeile <code>url:</code> auf <code>{slug}</code>.
          </li>
          <li>
            <b>Home Assistant neu starten.</b> In der Seitenleiste erscheint „Cultivate Control“ ohne Titelleiste. Den normalen
            Eintrag des Add-ons kannst du dann ausblenden (Add-on-Seite → „In der Seitenleiste anzeigen“ aus).
          </li>
        </ol>
        <pre className="mo-yaml" data-audit="titelleiste-yaml">{yamlBlock(slug)}</pre>
        <div className="mo-url">
          <V1Button variant="primary" onClick={() => void kopieren()}>{kopiert ? 'Kopiert' : 'In die Zwischenablage kopieren'}</V1Button>
          <span className="v1-muted">Die Kennung <code>{slug}</code> ist die dieser Installation. Der Name des Panels ({VORGESCHLAGENER_PANEL}) ist frei wählbar.</span>
        </div>
        {fehler && <V1Alert tone="warn" message="Die Zwischenablage war nicht erreichbar — markier den Block und kopier ihn von Hand." />}
      </div>
    </V1Section>
  )
}
