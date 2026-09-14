import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch } from '../../api'
import type { HydroSetupDto, PlantInstanceDto } from '../../types'
import { pflanzenRolleName } from '../../deutsche-woerter'
import { istAutomatischerName, pflanzenName } from './pflanzen-namen'
import { V1Card, V1LinkButton, V1Section } from '../../components/v1'

/**
 * Die Pflanzen dieses Grows — jede mit ihrer eigenen Sorte.
 *
 * Aus dem Feld: „Ich habe 3 Sorten im Zelt stehen, kann dem Grow aber nur eine
 * zuweisen." Das Modell konnte es die ganze Zeit — `PlantInstance` trägt eine
 * eigene StrainId je Pflanze —, es gab nur keine Stelle, an der man es sieht
 * und pflegt. Der Grow behält seine Hauptsorte für Listen und Strahl; wer
 * gemischt fährt, trägt es hier ein.
 */
export function GrowPlantsCard({ growId, growPlantCount, systemId, onSorten, onAnzahl }: {
  growId: number
  growPlantCount: number | null
  /**
   * Das Hydro-System des Grows — daraus kommt die Zahl der Töpfe.
   *
   * Ohne sie konnte die Karte beliebig viele Pflanzen anlegen: gemeldet wurde
   * „du kannst mehr Sorten angeben, als es Töpfe gibt", belegt mit acht
   * Pflanzen in einem Vier-Topf-System. Die Sperre sitzt im Backend; hier
   * steht sie, damit der Knopf gar nicht erst einlädt.
   */
  systemId: number | null
  /**
   * Meldet die Sorten der Pflanzen nach oben — die Detailseite ersetzt damit
   * ihre „Sorte"-Kachel durch „gemischt", statt bei einem Mehrsorten-Grow
   * eine einzelne Hauptsorte zu behaupten.
   */
  onSorten?: (sorten: string[]) => void
  /**
   * Meldet, wie viele Pflanzen wirklich erfasst sind.
   *
   * <b>Eine Wahrheit je Zahl.</b> Die Kachel „Pflanzen" zeigte bisher
   * <c>grow.plantCount</c> — die Zahl aus dem Grow-Formular. Im gemeldeten
   * Fall stand dort 6, während acht Pflanzen einzeln erfasst waren. Sind
   * Pflanzen einzeln erfasst, sind sie die Wahrheit.
   */
  onAnzahl?: (anzahl: number) => void
}) {
  const [plants, setPlants] = useState<PlantInstanceDto[]>([])
  // Zusammen abgelegt, nicht getrennt: sonst zeigt die Karte nach einem
  // Systemwechsel kurz die Topfzahl des VORIGEN Systems.
  const [systemToepfe, setSystemToepfe] = useState<{ systemId: number, potCount: number | null } | null>(null)
  const [fehler, setFehler] = useState<string | null>(null)
  const [geladen, setGeladen] = useState(false)

  async function laden(signal?: AbortSignal) {
    const pflanzen = await apiFetch<PlantInstanceDto[]>(`/api/plants?growId=${growId}`, { signal })
    if (signal?.aborted) return
    setPlants(pflanzen)
    onSorten?.([...new Set(pflanzen.map((p) => p.strainName).filter((n): n is string => !!n))])
    onAnzahl?.(pflanzen.length)
  }

  useEffect(() => {
    const controller = new AbortController()
    async function start() {
      try {
        await laden(controller.signal)
      } catch {
        /* Ohne Pflanzen-API bleibt die Karte leer. */
      } finally {
        if (!controller.signal.aborted) setGeladen(true)
      }
    }
    void start()
    return () => controller.abort()
    // eslint-disable-next-line react-hooks/exhaustive-deps -- laden haengt nur an growId
  }, [growId])

  // Die Topfzahl kommt aus dem Hydro-System und nirgendwo sonst — dieselbe
  // Zahl, die die Draufsicht an ihre Sites zeichnet.
  useEffect(() => {
    if (systemId == null) return
    const controller = new AbortController()
    apiFetch<HydroSetupDto>(`/api/hydro-setups/${systemId}`, { signal: controller.signal })
      .then((system) => setSystemToepfe({ systemId, potCount: system.potCount ?? null }))
      .catch(() => setSystemToepfe({ systemId, potCount: null }))
    return () => controller.abort()
  }, [systemId])

  /** Die Topfzahl — nur, wenn sie zu DIESEM System gehoert. */
  const toepfe = systemId != null && systemToepfe?.systemId === systemId
    ? systemToepfe.potCount
    : null

  /** Wie viele Töpfe noch frei sind — null heisst „System kennt keine Töpfe". */
  const freiePlaetze = toepfe == null ? null : Math.max(0, toepfe - plants.length)

  /** Ein Topf, der schon jemandem gehört — die Meldung nennt beide. */
  const doppelteToepfe = useMemo(() => {
    const zaehler = new Map<number, number>()
    for (const p of plants) {
      if (p.siteIndex == null) continue
      zaehler.set(p.siteIndex, (zaehler.get(p.siteIndex) ?? 0) + 1)
    }
    return [...zaehler.entries()].filter(([, n]) => n > 1).map(([topf]) => topf)
  }, [plants])

  /**
   * Die Töpfe des Systems mit ihrem Bewohner — die Grundlage der Auswahl.
   *
   * Kennt das System keine Topfzahl, bleibt wenigstens das, was vergeben ist:
   * sonst verschwände bei einem Grow ohne Hydro-System der Topf ganz, und man
   * käme an eine falsch gesetzte Nummer nicht mehr heran.
   */
  const topfAuswahl = useMemo(() => {
    const bewohner = new Map<number, string>()
    for (const p of plants) {
      if (p.siteIndex == null) continue
      bewohner.set(p.siteIndex, p.strainName ?? 'ohne Sorte')
    }
    const hoechste = Math.max(toepfe ?? 0, ...[...bewohner.keys()], 0)
    return Array.from({ length: hoechste }, (_, i) => ({
      topf: i + 1,
      belegtVon: bewohner.get(i + 1) ?? null,
    }))
  }, [plants, toepfe])

  /** „3× RS11 · 1× Purple Lemonade" — der Satz, der den Mischgrow beschreibt. */
  const verteilung = useMemo(() => {
    const zaehler = new Map<string, number>()
    for (const plant of plants) {
      const name = plant.strainName ?? 'ohne Sorte'
      zaehler.set(name, (zaehler.get(name) ?? 0) + 1)
    }
    return [...zaehler.entries()]
      .sort((a, b) => b[1] - a[1])
      .map(([name, anzahl]) => `${anzahl}× ${name}`)
      .join(' · ')
  }, [plants])

  /**
   * EIN Feld ändern, alle anderen unverändert mitschicken.
   *
   * Das PUT überschreibt ALLE Felder. Die erste Fassung zählte sie von Hand
   * auf — und hätte beim nächsten neuen Feld genau das getan, was beim
   * `siteIndex` fast passiert wäre: es stillschweigend genullt, sobald jemand
   * die Sorte wechselt. Deshalb wird jetzt das ganze DTO kopiert und nur das
   * eine Feld ersetzt.
   */
  async function feldAendern(plant: PlantInstanceDto, aenderung: Partial<PlantInstanceDto>, was: string) {
    setFehler(null)
    try {
      await apiFetch(`/api/plants/${plant.id}`, {
        method: 'PUT',
        body: JSON.stringify({ ...plant, ...aenderung }),
      })
      await laden()
    } catch (caught) {
      setFehler(caught instanceof Error ? caught.message : `${was} konnte nicht geändert werden.`)
    }
  }

  /**
   * Der Topf ab 1 — leer heisst „kein Topf zugeordnet".
   *
   * <b>Der Name zieht mit.</b> Wer eine Pflanze von Topf 3 auf Topf 5 setzt,
   * hat danach nicht „Pflanze 3" auf Topf 5 stehen. Ausdrücklich verlangt:
   * „dass er automatisch durchzählt und wenn sich was ändert, er die Zahl
   * automatisch zieht."
   *
   * Ein selbst vergebener Name bleibt unberührt — nur die automatischen
   * („Pflanze 3", „Topf 3") wandern mit. Sonst überschriebe ein Topfwechsel
   * einen Namen, den jemand mit Absicht gesetzt hat.
   */
  function topfAendern(plant: PlantInstanceDto, wert: string) {
    const zahl = wert.trim() === '' ? null : Math.trunc(Number(wert))
    if (zahl != null && (!Number.isFinite(zahl) || zahl < 1)) return
    const aenderung: Partial<PlantInstanceDto> = { siteIndex: zahl }
    if (istAutomatischerName(plant.label)) aenderung.label = pflanzenName(zahl)
    return feldAendern(plant, aenderung, 'Topf')
  }

  if (!geladen) return null

  return (
    <V1Section title="Pflanzen & Sorten">
      <V1Card>
        <div data-audit="grow-plants">
          {fehler && <p className="gp-fehler">{fehler}</p>}

          {plants.length === 0 ? (
            <p className="gc-facts">
              {growPlantCount != null && growPlantCount > 0
                ? `Der Grow zählt ${growPlantCount} Pflanzen, aber keine ist einzeln erfasst. `
                : ''}
              Leg die Pflanzen einzeln an, wenn du <strong>mehrere Sorten</strong> in einem Zelt fährst —
              dann trägt jede ihre eigene.
            </p>
          ) : (
            <>
              {verteilung && <p className="gc-facts">Im Zelt: {verteilung}</p>}
              {/* Die Topfbilanz steht ÜBER der Liste, weil sie die Frage
                  beantwortet, die beim Hinzufügen als Nächstes kommt. */}
              {toepfe != null && (
                <p className={`gp-bilanz ${plants.length > toepfe ? 'zuviel' : ''}`}>
                  {plants.length > toepfe
                    ? `${plants.length} Pflanzen bei ${toepfe} Töpfen — ${plants.length - toepfe} zu viel.`
                    : `${plants.length} von ${toepfe} Töpfen belegt.`}
                </p>
              )}
              {doppelteToepfe.length > 0 && (
                <p className="gp-fehler">
                  {doppelteToepfe.length === 1
                    ? `Topf ${doppelteToepfe[0]} ist doppelt belegt.`
                    : `Doppelt belegt: Topf ${doppelteToepfe.join(', ')}.`}{' '}
                  Ein Topf trägt eine Pflanze.
                </p>
              )}
              <ul className="gp-liste">
                {plants.map((plant) => (
                  <li key={plant.id}>
                    {/* <b>Der Topf IST die Kennung der Zeile.</b> Vorher stand
                        hier „Pflanze 1" und daneben „TOPF 1" — dieselbe Zahl
                        zweimal, was nach zwei Angaben aussieht. Gemeldet als
                        „etwas komisch"; die Regel dazu lautet „es soll nichts
                        doppelt sein".

                        Ein selbst vergebener Name bleibt sichtbar — nur die
                        automatischen („Pflanze 3") fallen weg, weil sie nichts
                        sagen, was der Topf daneben nicht schon sagt. */}
                    {/* Nur ein SELBST vergebener Name steht hier. Der
                        automatische fiel weg: „Topf 1" neben dem Auswahlfeld,
                        das „Topf 1" zeigt, ist dieselbe Angabe zweimal — und
                        genau das war gemeldet. */}
                    {!istAutomatischerName(plant.label) && (
                      <span className="gp-label">{plant.label}</span>
                    )}
                    {plant.plantRole !== 'Production' && (
                      <em className="gp-rolle">{pflanzenRolleName(plant.plantRole)}</em>
                    )}
                    {/* <b>Gewaehlt, nicht getippt.</b> Ein Zahlenfeld liess
                        eine belegte Nummer zu und meldete den Fehler erst
                        danach — dabei weiss die App, welche Toepfe frei sind.
                        Belegte stehen mit ihrem Bewohner dabei, damit ein
                        Tausch sichtbar ist statt einer Sperre. */}
                    <label className="gp-topf">
                      <select
                        value={plant.siteIndex ?? ''}
                        onChange={(event) => void topfAendern(plant, event.target.value)}
                        aria-label={`Topf von ${istAutomatischerName(plant.label)
                          ? `Pflanze in Topf ${plant.siteIndex ?? '–'}` : plant.label}`}
                      >
                        <option value="">— kein Topf —</option>
                        {topfAuswahl.map((eintrag) => (
                          <option key={eintrag.topf} value={eintrag.topf}>
                            Topf {eintrag.topf}
                            {eintrag.belegtVon != null && eintrag.topf !== plant.siteIndex
                              ? ` · belegt (${eintrag.belegtVon})` : ''}
                          </option>
                        ))}
                      </select>
                    </label>
                    {/* `gp-sorte`: seit der Topf ebenfalls ein Auswahlfeld ist,
                        gibt es zwei je Zeile — und ein Zugriff ueber die
                        Position trifft das falsche. Genau daran ist eine
                        bestehende Pruefung gescheitert. */}
                    {/* forkai.105: Nur noch Anzeige. Belegen und Leeren liegen
                        seit dieser Fassung an einer Stelle — im Formular unter
                        „Töpfe & Sorten". Zwei Karten mit fast gleichem Namen,
                        von denen nur eine entfernen konnte, waren die Falle. */}
                    <span className="gp-sorte-text">{plant.strainName ?? 'Ohne Sorte'}</span>
                  </li>
                ))}
              </ul>
            </>
          )}

          <div className="gp-neu">
            <V1LinkButton to={`/grows/${growId}/bearbeiten`}>Töpfe & Sorten bearbeiten</V1LinkButton>
          </div>
          {/* Ein gesperrter Knopf ohne Grund ist ein kaputter Knopf. */}
          {freiePlaetze === 0 && (
            <p className="gp-voll">
              Alle {toepfe} Töpfe sind belegt. Entferne eine Pflanze, oder gib dem
              System unter <Link to="/hydro">Hydro-Systeme</Link> mehr Sites.
            </p>
          )}

          {/* Der vergessene Weg — existiert seit der Klon-Phase, wusste nur niemand mehr. */}
          <p className="gp-hinweis">
            Eigene Stecklinge? Ein Setup vom Typ <strong>Mutter</strong> unter{' '}
            <Link to="/zelte">Zelte &amp; Räume</Link> kann Klone direkt von einer Mutterpflanze erzeugen —
            samt Abstammung.
          </p>
        </div>
      </V1Card>
    </V1Section>
  )
}
