import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { apiFetch, ApiRequestError } from '../api'
import { V1Alert, V1Card, V1Empty, V1Page, V1Skeleton } from '../components/v1'
import { ChangeoutsPanel } from '../features/changeouts/ChangeoutsPanel'
import { teileText } from '../features/wasserwechsel/ablauf-rechnung'
import { WasserwechselStand } from '../features/changeouts/WasserwechselStand'
import { GrowScopePicker } from '../features/grow-scope/GrowScopePicker'
import { useSelectedGrow } from '../features/grow-scope/useSelectedGrow'
import { WasserwechselAblauf } from '../features/wasserwechsel/WasserwechselAblauf'
import type { WasserwechselStandDto, WasserwechselVorgangDto } from '../types'
import '../features/changeouts/changeouts.css'

/**
 * Der Wasserwechsel — eine eigene Seite, weil er eine eigene Handlung ist.
 *
 * <b>Der Anlass (31.08.2026).</b> Gemeldet: „der User findet den Wasserwechsel
 * nicht wirklich, das ist sehr umständlich von uns gelöst, weil er hat jetzt
 * einen gemacht und will den eintragen und zurückdatieren."
 *
 * Der Weg dorthin war: Menü „Addback" → scrollen → dritter Abschnitt →
 * „Wechsel erfassen". Das Wort „Wasserwechsel" stand auf dem ganzen Weg
 * nirgends, im Hauptmenü überhaupt nicht — und wer es in die Suche tippte,
 * landete auf der Aufgabenseite, weil das Wort dort als Schlagwort stand.
 *
 * <b>Warum eine eigene Seite und kein zweites Formular.</b> Die Regel dieses
 * Projekts sagt: führt eine neue Seite dieselbe Hauptaktion wie eine andere,
 * ist das ein Befund und kein Feature. Deshalb ist das Formular hierher
 * <b>umgezogen</b>; auf /addback steht jetzt nur noch der Stand mit einem Weg
 * hierher. Nachfüllen und Wechseln sind zwei Handlungen — Wasser dazugeben ist
 * nicht Wasser austauschen.
 *
 * <b>Seit A-006 (05.10.2026)</b> steht hier der Ablauf in vier Schritten
 * (`WasserwechselAblauf`): ein Speichern legt Wechsel, Messung vorher und
 * nachher, Verbrauch und Tagebuchzeile als einen Vorgang an. Das alte
 * Formular ist weg; die Liste darunter zeigt die bisherigen Wechsel, Altdaten
 * eingeschlossen. `?vorgang=<id>` hebt einen Vorgang hervor (Link aus dem
 * Tagebuch).
 */
export default function WasserwechselPage() {
  const { grows, growId, setGrowId, loading, error } = useSelectedGrow()
  const grow = grows.find((item) => String(item.id) === String(growId)) ?? null

  const [stand, setStand] = useState<WasserwechselStandDto | null>(null)
  const [standFehler, setStandFehler] = useState<string | null>(null)
  const [neuGeladen, setNeuGeladen] = useState(0)
  // Nach dem Speichern beginnt ein frischer Vorgang — neu gerechnet, nichts gemerkt (Bru).
  const [ablaufNummer, setAblaufNummer] = useState(0)
  const [gespeichert, setGespeichert] = useState<{ text: string; hinweis: string | null } | null>(null)
  const [suche] = useSearchParams()
  const markiert = Number(suche.get('vorgang')) || null
  // `?schritt=2` öffnet den Ablauf auf einem Schritt — für Links und für die
  // Oberflächen-Prüfungen, die sonst nur Schritt 1 sähen (e2e/seiten.ts).
  const startSchritt = Number(suche.get('schritt'))
  // Ein Link auf einen Vorgang (aus dem Tagebuch): gibt es ihn noch? Ein
  // geloeschter Vorgang soll das sagen, statt still nichts hervorzuheben.
  const [fehlendeVorgaenge, setFehlendeVorgaenge] = useState<number[]>([])
  const vorgangFehlt = markiert != null && fehlendeVorgaenge.includes(markiert)

  const growId2 = grow?.id ?? null
  useEffect(() => {
    if (growId2 == null) return
    const controller = new AbortController()
    async function laden(id: number) {
      try {
        const daten = await apiFetch<WasserwechselStandDto>(
          `/api/grows/${id}/changeouts/stand`, { signal: controller.signal })
        if (controller.signal.aborted) return
        setStand(daten)
        setStandFehler(null)
      } catch (caught) {
        if (controller.signal.aborted) return
        setStandFehler(caught instanceof ApiRequestError ? caught.message : 'Stand konnte nicht geladen werden.')
      }
    }
    void laden(growId2)
    return () => controller.abort()
  }, [growId2, neuGeladen])

  useEffect(() => {
    if (growId2 == null || markiert == null) return
    const controller = new AbortController()
    apiFetch<WasserwechselVorgangDto>(`/api/grows/${growId2}/wasserwechsel/${markiert}`, { signal: controller.signal })
      .then(() => setFehlendeVorgaenge((alt) => alt.filter((id) => id !== markiert)))
      .catch((caught) => {
        if (!controller.signal.aborted && caught instanceof ApiRequestError && caught.status === 404) {
          setFehlendeVorgaenge((alt) => [...alt, markiert])
        }
      })
    return () => controller.abort()
  }, [growId2, markiert, neuGeladen])

  return (
    <V1Page
      eyebrow="Jetzt"
      title="Wasserwechsel"
      subtitle="Ein Ablauf: vorher, ansetzen, nachher. Verbrauch und Tagebuch gehen mit."
      action={<GrowScopePicker grows={grows} growId={growId} onChange={setGrowId} />}
    >
      {error && <V1Alert message={error} tone="critical" />}
      {standFehler && <V1Alert message={standFehler} tone="warn" />}
      {vorgangFehlt && <V1Alert message="Diesen Wasserwechsel gibt es nicht mehr — er wurde entfernt." tone="neutral" />}

      {loading ? (
        <V1Skeleton rows={4} label="Lade Wasserwechsel" />
      ) : grows.length === 0 ? (
        <V1Empty
          title="Kein aktiver Grow"
          text="Der Wasserwechsel gehört zu einem laufenden Grow. Leg zuerst einen an."
        />
      ) : !grow ? null : (
        <>
          {stand && (
            <V1Card className="ww-stand-card">
              <WasserwechselStand stand={stand} />
            </V1Card>
          )}

          {gespeichert && <V1Alert title="Gespeichert" message={gespeichert.text} tone="ok" />}
          {gespeichert?.hinweis && <V1Alert message={gespeichert.hinweis} tone="warn" />}

          <div className="ww-ablauf-section">
            <WasserwechselAblauf
              key={`${grow.id}-${ablaufNummer}`}
              growId={grow.id}
              stand={stand}
              startSchritt={ablaufNummer === 0 && [1, 2, 3, 4].includes(startSchritt) ? startSchritt as 1 | 2 | 3 | 4 : 1}
              onGespeichert={(vorgang, hinweis) => {
                setGespeichert({ text: `Wasserwechsel gespeichert — mit ${teileText(vorgang)}.`, hinweis })
                setAblaufNummer((wert) => wert + 1)
                // Ein neuer Wechsel verschiebt den Stand — sonst stuenden oben
                // 9 Tage, waehrend unten der Eintrag von eben steht.
                setNeuGeladen((wert) => wert + 1)
                window.scrollTo({ top: 0 })
              }}
            />
          </div>

          <ChangeoutsPanel
            growId={grow.id}
            growName={grow.name}
            neuLaden={neuGeladen}
            markiert={markiert}
            onGeaendert={() => { setGespeichert(null); setNeuGeladen((wert) => wert + 1) }}
            leerHinweis={leerHinweis(stand)}
          />
        </>
      )}
    </V1Page>
  )
}

/**
 * Was in der leeren Liste steht — ohne der Zahl darüber zu widersprechen.
 *
 * Ein Wechsel kann auf zwei Wegen belegt sein: als Häkchen an einer Messung
 * oder als Eintrag hier. Steht oben „vor 0 Tagen" und unten „noch kein
 * Wasserwechsel", ist beides wahr und der Nutzer trotzdem verwirrt — genau so
 * sah die Seite am 31.08.2026 bei der ersten Sicht aus.
 */
function leerHinweis(stand: WasserwechselStandDto | null): string | undefined {
  if (stand?.zuletztUtc == null) return undefined
  const wann = new Date(stand.zuletztUtc).toLocaleDateString('de-DE')
  return `Der letzte belegte Wechsel (${wann}) kommt aus einer älteren Messung — dort war „Lösungswechsel" angehakt. `
    + 'Er zählt weiter; hier eingetragen ist noch keiner.'
}
