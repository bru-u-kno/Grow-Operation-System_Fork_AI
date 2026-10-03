import { useEffect, useRef, useState } from 'react'
import { resolveUrl } from '../../base'
import type { TentDto } from '../../types'
import { classNames } from '../../utils'
import { KlappTitel } from './Einklappen'

/**
 * Die Kamera als Bühne, wie im Entwurf: Kopfzeile mit Entity und Alter des
 * Bildes, darunter das Bild in voller Breite.
 *
 * Mehrere Kameras liegen als Umschaltleiste darunter — drei gleich große Karten
 * wären drei kleine Bilder, und bei einer Kamera schaut man auf Details.
 *
 * Das zuletzt gültige Bild bleibt stehen, wenn ein Abruf scheitert. Der Server
 * hält es ohnehin vor; eine leere Fläche wäre die schlechtere Auskunft, weil das
 * Zelt ja weiterläuft. Das gilt nur je Kamera: jedes Bild trägt die Kamera, von
 * der es stammt, und die Bühne zeigt nur Bilder der gerade gewählten — sonst
 * stünde nach dem Umschalten das Bild der vorigen unter dem neuen Namen.
 */
export function CameraPanel({ tent, onReload, zu = false, onUmschalten }: {
  tent: TentDto | null
  onReload?: () => void
  /**
   * Fork AI: eingeklappt holt die Karte KEIN Bild — der Abruf laeuft sonst
   * jede Sekunde weiter, und am Telefon kostet genau das Daten und Akku.
   */
  zu?: boolean
  onUmschalten?: () => void
}) {
  const cameras = tent?.cameras?.length ? tent.cameras : (tent?.cameraEntityId ? [tent.cameraEntityId] : [])
  const [active, setActive] = useState(0)
  const [frame, setFrame] = useState<{ camera: string; src: string; capturedAt: string | null; live: boolean } | null>(null)
  const [failedCamera, setFailedCamera] = useState<string | null>(null)
  const [reloadKey, setReloadKey] = useState(0)
  const urlRef = useRef<string | null>(null)
  const current = cameras[Math.min(active, cameras.length - 1)]

  useEffect(() => {
    if (!tent || !current || zu) return
    let alive = true
    let timer: number | undefined

    async function loop() {
      try {
        const response = await fetch(resolveUrl(`/api/live/tents/${tent!.id}/camera?entity=${encodeURIComponent(current!)}&t=${Date.now()}`))
        if (!alive) return
        if (response.ok) {
          const blob = await response.blob()
          if (!alive) return
          const next = URL.createObjectURL(blob)
          if (urlRef.current) URL.revokeObjectURL(urlRef.current)
          urlRef.current = next
          setFrame({
            camera: current!,
            src: next,
            capturedAt: response.headers.get('X-Camera-Captured-At'),
            live: response.headers.get('X-Camera-Live') !== 'false',
          })
          setFailedCamera(null)
        } else {
          setFailedCamera(current!)
        }
      } catch {
        if (alive) setFailedCamera(current!)
      } finally {
        // Der nächste Abruf startet erst, wenn der vorige durch ist — eine
        // langsame Kamera wird dadurch seltener aktualisiert statt abgebrochen.
        if (alive) timer = window.setTimeout(loop, 1000)
      }
    }

    void loop()
    return () => { alive = false; if (timer !== undefined) window.clearTimeout(timer) }
  }, [tent, current, reloadKey, zu])

  useEffect(() => () => { if (urlRef.current) URL.revokeObjectURL(urlRef.current) }, [])

  const shown = frame && frame.camera === current ? frame : null
  const failed = failedCamera === current

  return (
    <article className={classNames('ls-panel', 'ls-cam', zu && 'is-zu')} data-audit="live-camera">
      <div className="ls-panel-head">
        {onUmschalten
          ? <KlappTitel zu={zu} onUmschalten={onUmschalten}><span className="ls-label">Kamera</span></KlappTitel>
          : <span className="ls-label">Kamera</span>}
        <span className="ls-panel-meta">
          {zu
            ? 'eingeklappt · lädt kein Bild'
            : current ? `${current}${shown?.capturedAt ? ` · ${ageLabel(shown.capturedAt)}` : ''}` : 'keine gemappt'}
        </span>
        {current && !zu && (
          <button
            type="button"
            className="ls-btn is-small"
            onClick={() => { setReloadKey((key) => key + 1); onReload?.() }}
          >
            Neu laden
          </button>
        )}
      </div>

      {/* Ohne zugeordnete Kamera schrumpft die Buehne: 260 px grauer Klotz
          fuer den Satz „keine gemappt“ schoben auf dem Telefon alles
          darunter aus dem Bild. */}
      {!zu && <>
      <div className={`ls-cam-stage${cameras.length === 0 ? ' is-empty' : ''}`}>
        {shown ? (
          <img src={shown.src} alt={`Kamerabild ${current}`} />
        ) : (
          <div className="ls-cam-empty">
            {cameras.length === 0
              ? 'Keine Kamera zugeordnet — im Home-Assistant-Setup verbinden.'
              : failed ? 'Kein Bild — in Home Assistant erreichbar?' : 'Lädt …'}
          </div>
        )}
        {shown && !shown.live && <span className="ls-cam-stale">veraltet</span>}
      </div>

      {cameras.length > 1 && (
        <div className="ls-cam-strip" role="tablist" aria-label="Kamera wählen">
          {cameras.map((camera, index) => (
            <button
              key={camera}
              type="button"
              role="tab"
              aria-selected={index === active}
              className={classNames('ls-cam-tab', index === active && 'active')}
              onClick={() => setActive(index)}
            >
              <span className="n">{index + 1}</span>
              {shortName(camera, index)}
            </button>
          ))}
        </div>
      )}
      </>}
    </article>
  )
}

/**
 * „camera.hauptzelt" → „Hauptzelt", „camera.pflanze_2" → „Pflanze 2".
 *
 * Erst nahm ich nur das letzte Wort — bei `camera.pflanze_2` blieb davon „2"
 * uebrig, und im Streifen standen Ziffern statt Namen. Der ganze Rest hinter dem
 * Praefix ist der Name; er ist kurz genug.
 */
function shortName(entityId: string, index: number): string {
  const raw = entityId.replace(/^(camera|image)\./i, '').replace(/[_-]+/g, ' ').trim()
  if (!raw) return `Kamera ${index + 1}`
  // Jedes Wort gross anfangen: „pflanze 2“ ist ein Bezeichner, „Pflanze 2“ ein Name.
  return raw
    .split(' ')
    .map((wort) => (wort ? wort.charAt(0).toUpperCase() + wort.slice(1) : wort))
    .join(' ')
}

function ageLabel(iso: string): string {
  const captured = new Date(iso).getTime()
  if (Number.isNaN(captured)) return ''
  const seconds = Math.max(0, Math.round((Date.now() - captured) / 1000))
  if (seconds < 90) return `vor ${seconds} s`
  const minutes = Math.round(seconds / 60)
  return minutes < 90 ? `vor ${minutes} min` : `vor ${Math.round(minutes / 60)} h`
}
