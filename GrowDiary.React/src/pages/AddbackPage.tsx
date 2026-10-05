import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { apiFetch, ApiRequestError } from '../api'
import { V1Alert, V1Empty, V1Page, V1Skeleton } from '../components/v1'
import { NachfuellListe } from '../features/addback/NachfuellListe'
import { WasserwechselStand } from '../features/changeouts/WasserwechselStand'
import { GrowScopePicker } from '../features/grow-scope/GrowScopePicker'
import { useSelectedGrow } from '../features/grow-scope/useSelectedGrow'
import { vorbelegungAusLink } from '../features/vorgang/ablauf-rechnung'
import { VorgangAblauf } from '../features/vorgang/VorgangAblauf'
import type { AddbackLogDto, AddbackVorgangDto, WasserwechselStandDto } from '../types'
import { formatDateTime, formatNumber } from '../utils'

/**
 * Addback — das Nachfüllen als ein Ablauf (A-006, Etappe 3, 05.10.2026).
 *
 * <b>Vorher</b> gab es hier zwei Seiten: eine Übersicht (`/addback`) und einen
 * Assistenten je Grow (`/grows/:id/addback`) mit Stamm-EC, Komponentenliste in
 * der Notiz und ohne Verbrauch, Messung oder Tagebuch. Wer nachfüllte, trug
 * an drei Stellen ein.
 *
 * <b>Jetzt</b> derselbe Ablauf wie beim Wasserwechsel (`VorgangAblauf`):
 * vorher (Sensor oder Hand, DO/ORP von Hand), nachfüllen (Liter nach
 * Wasserart, Zugaben als Vorschlag aus dem Mischplan auf die nachgefüllten
 * Liter, ↺/↶, „+ Produkt", Buchen-Häkchen, Wasser wird gebucht), nachher,
 * speichern — Addback-Eintrag, Messungen, Verbrauch und Tagebuchzeile in einer
 * Transaktion. Der Addback-Rechner steht im Schritt „Nachfüllen", nicht auf
 * einer zweiten Seite: eine Hauptaktion, ein Weg. `/grows/:id/addback` leitet
 * hierher weiter.
 *
 * <b>Links.</b> `?vorgang=<id>` hebt einen Vorgang in der Liste hervor,
 * `?schritt=2` öffnet den Ablauf auf einem Schritt, und die Vorbelegung
 * (`?zeitpunkt=…&ecVorher=…&ecNachher=…&liter=…`, siehe `vorbelegungAusLink`)
 * ist der Weg vom Grow-Tagebuch („Nachfüllen eintragen" an einer Auffälligkeit).
 */
export default function AddbackPage() {
  const { grows, growId, setGrowId, loading, error } = useSelectedGrow()
  const grow = grows.find((item) => String(item.id) === String(growId)) ?? null
  const [suche] = useSearchParams()

  // Die Vorbelegung gilt einmal: für den Grow aus dem Link und den ersten Ablauf.
  const [vorbelegung] = useState(() => vorbelegungAusLink(suche))
  const [vorbelegtFuer] = useState(() => suche.get('growId'))
  const startSchritt = Number(suche.get('schritt'))
  const markiert = Number(suche.get('vorgang')) || null

  const [eintraege, setEintraege] = useState<AddbackLogDto[]>([])
  const [vorgaenge, setVorgaenge] = useState<AddbackVorgangDto[]>([])
  const [listeLaedt, setListeLaedt] = useState(true)
  const [listeFehler, setListeFehler] = useState<string | null>(null)
  const [stand, setStand] = useState<WasserwechselStandDto | null>(null)
  const [neuGeladen, setNeuGeladen] = useState(0)
  // Nach dem Speichern beginnt ein frischer Vorgang — neu gerechnet, nichts gemerkt (Bru).
  const [ablaufNummer, setAblaufNummer] = useState(0)
  const [gespeichert, setGespeichert] = useState<{ text: string; hinweis: string | null } | null>(null)

  const aktiveId = grow?.id ?? null
  useEffect(() => {
    if (aktiveId == null) return
    const controller = new AbortController()
    void (async () => {
      try {
        const [logs, liste, wechsel] = await Promise.all([
          apiFetch<AddbackLogDto[]>(`/api/grows/${aktiveId}/addback/logs`, { signal: controller.signal }),
          apiFetch<AddbackVorgangDto[]>(`/api/grows/${aktiveId}/addback/vorgaenge`, { signal: controller.signal }),
          // Der Stand des Wasserwechsels — gerechnet im Backend, damit hier und auf
          // /wasserwechsel nicht zwei verschiedene Zahlen stehen können.
          apiFetch<WasserwechselStandDto>(`/api/grows/${aktiveId}/changeouts/stand`, { signal: controller.signal }).catch(() => null),
        ])
        if (controller.signal.aborted) return
        setEintraege(logs)
        setVorgaenge(liste)
        setStand(wechsel)
        setListeFehler(null)
      } catch (caught) {
        if (!controller.signal.aborted) setListeFehler(caught instanceof ApiRequestError ? caught.message : 'Die Einträge konnten nicht geladen werden.')
      } finally {
        if (!controller.signal.aborted) setListeLaedt(false)
      }
    })()
    return () => controller.abort()
  }, [aktiveId, neuGeladen])

  const letzter = [...eintraege].sort((a, b) => b.performedAtUtc.localeCompare(a.performedAtUtc))[0] ?? null
  const markiertFehlt = markiert != null && !listeLaedt && !vorgaenge.some((v) => v.id === markiert)

  return (
    <V1Page
      eyebrow="Jetzt"
      title="Addback"
      subtitle="Nachfüllen als ein Ablauf: vorher, nachfüllen, nachher. Verbrauch und Tagebuch gehen mit."
      action={<GrowScopePicker grows={grows} growId={growId} onChange={setGrowId} />}
    >
      {error && <V1Alert message={error} tone="critical" />}
      {listeFehler && <V1Alert message={listeFehler} tone="warn" />}
      {markiertFehlt && <V1Alert message="Dieses Nachfüllen gibt es nicht mehr — es wurde entfernt." tone="neutral" />}

      {loading ? (
        <V1Skeleton rows={4} label="Lade Addback" />
      ) : grows.length === 0 ? (
        <V1Empty
          title="Kein aktiver Grow"
          text="Nachfüllen gehört zu einem laufenden Grow. Leg zuerst einen an."
          action={<Link className="ls-btn is-primary" to="/grows/new">Grow anlegen</Link>}
        />
      ) : !grow ? null : (
        <>
          <div className="co-strip" data-audit="addback-status">
            <div className="co-cell">
              <div className="co-cell-label">pH</div>
              <div className="co-cell-value is-lg">{formatNumber(grow.latestReservoirPh, 2)}</div>
            </div>
            <div className="co-cell">
              <div className="co-cell-label">EC</div>
              <div className="co-cell-value is-lg">{formatNumber(grow.latestReservoirEc, 2)}<span className="co-unit">mS/cm</span></div>
            </div>
            <div className="co-cell">
              <div className="co-cell-label">Zuletzt nachgefüllt</div>
              <div className="co-cell-value is-md">{letzter ? formatDateTime(letzter.performedAtUtc) : '–'}</div>
            </div>
            <div className="co-cell">
              <div className="co-cell-label">Erfasst</div>
              <div className="co-cell-value is-md">{eintraege.length} {eintraege.length === 1 ? 'Eintrag' : 'Einträge'}</div>
            </div>
          </div>

          {gespeichert && <V1Alert title="Gespeichert" message={gespeichert.text} tone="ok" />}
          {gespeichert?.hinweis && <V1Alert message={gespeichert.hinweis} tone="warn" />}

          <div className="ww-ablauf-section">
            <VorgangAblauf
              art="addback"
              key={`${grow.id}-${ablaufNummer}`}
              growId={grow.id}
              vorbelegung={ablaufNummer === 0 && (vorbelegtFuer == null || vorbelegtFuer === String(grow.id)) ? vorbelegung : null}
              startSchritt={ablaufNummer === 0 && [1, 2, 3, 4].includes(startSchritt) ? startSchritt as 1 | 2 | 3 | 4 : 1}
              onGespeichert={(teile, hinweis) => {
                setGespeichert({ text: `Nachfüllen gespeichert — mit ${teile}.`, hinweis })
                setAblaufNummer((wert) => wert + 1)
                setNeuGeladen((wert) => wert + 1)
                window.scrollTo({ top: 0 })
              }}
            />
          </div>

          {/* Nachfüllen und Wechseln werden zusammen gelesen — der Stand steht hier,
              das Formular nur auf /wasserwechsel (eine Handlung, ein Weg). */}
          <section className="ls-panel" data-audit="addback-wasserwechsel">
            <div className="ls-panel-head">
              <span className="ls-label">Wasserwechsel</span>
              <Link className="ls-panel-meta ww-weg" to={`/wasserwechsel?growId=${grow.id}`}>eintragen →</Link>
            </div>
            <div className="ls-panel-body">
              {stand ? <WasserwechselStand stand={stand} /> : <p>Stand wird geladen …</p>}
            </div>
          </section>

          <NachfuellListe
            growId={grow.id}
            growName={grow.name}
            eintraege={eintraege}
            vorgaenge={vorgaenge}
            laedt={listeLaedt}
            markiert={markiert}
            onGeaendert={() => { setGespeichert(null); setNeuGeladen((wert) => wert + 1) }}
          />
        </>
      )}
    </V1Page>
  )
}
