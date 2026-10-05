import { useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch, formatApiError } from '../../api'
import type { PhotoAssetDto, TagebuchEreignisDto, TagebuchNotizDto, TagebuchPostenDto, TagebuchWechselDto } from '../../types'
import { zahl } from '../live/verlauf-modell'
import { AuffaelligAktionen } from './AuffaelligAktionen'
import { NotizBearbeiten } from './NotizBearbeiten'
import { VORGANG_ZEILEN, addbackArtName, aenderung, notizEtikett, wasserName, zellen, type Ton } from './tagebuch-modell'

/** Eine Zeile im Tag: Uhrzeit links, getaggter Inhalt rechts — wie im Journal-Strom. */
export function EreignisZeile({ growId, e, onGeaendert }: {
  growId: string
  e: TagebuchEreignisDto
  onGeaendert: () => void | Promise<void>
}) {
  return (
    <div className={`js-row tb-row is-${e.art}`} data-audit={`tagebuch-ereignis-${e.art}`} data-schluessel={e.schluessel}>
      <div className="js-when">{e.uhrzeit}</div>
      <div className="js-content">
        <Inhalt growId={growId} e={e} onGeaendert={onGeaendert} />
      </div>
    </div>
  )
}

function Kopf({ tag, ton, titel, herkunft, aktionen }: { tag: string; ton: Ton; titel: string; herkunft?: string | null; aktionen?: ReactNode }) {
  return (
    <div className="js-headline">
      <span className={`js-tag is-${ton}`}>{tag}</span>
      {titel && <strong>{titel}</strong>}
      {herkunft && <span className="tb-herkunft">{herkunft}</span>}
      {aktionen && <span className="js-aktionen">{aktionen}</span>}
    </div>
  )
}

function Inhalt({ growId, e, onGeaendert }: { growId: string; e: TagebuchEreignisDto; onGeaendert: () => void | Promise<void> }) {
  switch (e.art) {
    case 'messung': {
      const m = e.messung!
      const herkunft = m.herkunft === 'sensor' ? 'Sensor' : m.herkunft === 'import' ? 'importiert' : 'von Hand'
      return (
        <>
          <Kopf
            tag="Messwert"
            ton="muted"
            titel={e.titel}
            herkunft={herkunft}
            aktionen={<Link className="js-weg js-bearbeiten" to={`/grows/measurements/${m.id}/edit`} aria-label={`Messung von ${e.uhrzeit} Uhr bearbeiten`}>Bearbeiten</Link>}
          />
          {m.loesungswechsel && <p className="tb-vermerk">Lösungswechsel</p>}
          <WerteZellen zellenListe={zellen(m.werte)} />
          {m.abgleich.length > 0 && <Abgleich eintraege={m.abgleich} />}
          {m.notiz && <p>{m.notiz}</p>}
          <Posten liste={e.posten} titel="Zugaben" />
          <Fotos fotos={e.fotos} />
        </>
      )
    }
    case 'wechsel':
      return (
        <>
          <Kopf tag="Wasserwechsel" ton="info" titel={e.titel} herkunft="Vorgang" />
          <Vorgang growId={growId} w={e.wechsel!} posten={e.posten} onGeaendert={onGeaendert} />
          <Fotos fotos={e.fotos} />
        </>
      )
    case 'addback':
      return <AddbackInhalt growId={growId} e={e} onGeaendert={onGeaendert} />
    case 'dosierung': {
      const d = e.dosis!
      const werte = d.vorher != null && d.nachher != null ? ` · ${zahl(d.vorher, 2)} → ${zahl(d.nachher, 2)}` : ''
      return (
        <>
          <Kopf tag="Dosierung" ton="info" titel={e.titel} herkunft={d.automatisch ? 'automatisch' : 'von Hand'} />
          <p className="tb-zahlen">{zahl(d.ml, 1)} ml{werte}</p>
        </>
      )
    }
    case 'notiz':
    case 'meilenstein':
      return <NotizInhalt notiz={e.notiz!} titel={e.titel} fotos={e.fotos} onGeaendert={onGeaendert} />
    case 'foto':
      return (
        <>
          <Kopf tag="Foto" ton="accent" titel={e.titel} />
          <Fotos fotos={e.fotos} />
        </>
      )
    case 'verbrauch':
      return (
        <>
          <Kopf tag="Verbrauch" ton="muted" titel={e.titel} />
          <Posten liste={e.posten} />
        </>
      )
    case 'auffaellig': {
      const befunde = e.auffaellig!.befunde
      const [erster, ...weitere] = befunde
      return (
        <>
          <Kopf tag="Auffällig" ton="warn" titel={e.titel} herkunft="vom Sensor erkannt" />
          <p>
            Von {zahl(erster.vorher, erster.nachkomma)} auf {zahl(erster.nachher, erster.nachkomma)}
            {erster.einheit ? ` ${erster.einheit}` : ''} zwischen {erster.beginnUhrzeit} und {erster.endeUhrzeit} Uhr
            {weitere.map((b) => `, ${b.name} von ${zahl(b.vorher, b.nachkomma)} auf ${zahl(b.nachher, b.nachkomma)}`).join('')}
            {' '}— dazu ist nichts eingetragen. Nachgefüllt?
          </p>
          <p className="tb-regel">{erster.regel}</p>
          <AuffaelligAktionen growId={growId} befunde={befunde} onGespeichert={onGeaendert} />
        </>
      )
    }
  }
}

function WerteZellen({ zellenListe }: { zellenListe: ReturnType<typeof zellen> }) {
  if (zellenListe.length === 0) return null
  return (
    <div className="tb-zellen">
      {zellenListe.map((z) => (
        <div key={z.name} className="tb-zelle">
          <small>{z.name}</small>
          <b>{z.wert}{z.einheit && <i> {z.einheit}</i>}</b>
        </div>
      ))}
    </div>
  )
}

function Abgleich({ eintraege }: { eintraege: NonNullable<TagebuchEreignisDto['messung']>['abgleich'] }) {
  const daneben = eintraege.filter((a) => !a.passt)
  const sensor = eintraege.map((a) => `${a.name} ${zahl(a.sensor, a.nachkomma)}`).join(' · ')
  const titel = 'Bis ±0,1 pH, ±0,1 EC und ±1 °C gilt es als „passt" — so genau misst eine Dauersonde laut Hersteller (Bluelab Guardian).'
  if (daneben.length === 0) {
    return <p className="tb-abgleich" title={titel}>✓ Sensor zur selben Zeit: {sensor} — passt</p>
  }
  const abweichung = daneben
    .map((a) => `${a.name} um ${zahl(Math.abs(a.hand - a.sensor), a.nachkomma)}`)
    .join(', ')
  return <p className="tb-abgleich is-daneben" title={titel}>Sensor zur selben Zeit: {sensor} — weicht ab ({abweichung}). Sonde oder Messgerät prüfen.</p>
}

function Posten({ liste, titel }: { liste: TagebuchPostenDto[]; titel?: string }) {
  if (liste.length === 0) return null
  return (
    <div className="tb-posten" role="list" aria-label={titel ?? 'Gebucht'}>
      {liste.map((p, i) => (
        <div key={`${p.name}-${i}`} role="listitem">
          <span>{p.name}</span>
          <b>{zahl(p.menge, p.menge % 1 === 0 ? 0 : 2)}{p.einheit ? ` ${p.einheit}` : ''}</b>
        </div>
      ))}
    </div>
  )
}

function Fotos({ fotos }: { fotos: PhotoAssetDto[] }) {
  if (fotos.length === 0) return null
  return (
    <div className="js-photos">
      {fotos.map((foto) => (
        <figure key={foto.id} className="js-photo">
          <img src={foto.relativePath} alt={foto.caption ?? `Foto ${foto.id}`} loading="lazy" />
        </figure>
      ))}
    </div>
  )
}

/** Der Wasserwechsel als Vorgang: vorher | nachher | Änderung, Zugaben, Notiz. */
function Vorgang({ growId, w, posten, onGeaendert }: { growId: string; w: TagebuchWechselDto; posten: TagebuchPostenDto[]; onGeaendert: () => void | Promise<void> }) {
  const wasser = wasserName(w.wasser)
  return (
    <div className="tb-vorgang">
      <div className="tb-vt" role="table" aria-label="Vorher und nachher">
        <div className="tb-vt-kopf" role="row">
          <span role="columnheader"><i className="tb-unsichtbar">Wert</i></span>
          <span role="columnheader"><i className="tb-lang">vorher</i><i className="tb-kurz">vor</i></span>
          <span role="columnheader"><i className="tb-lang">nachher</i><i className="tb-kurz">nach</i></span>
          <span role="columnheader"><i className="tb-lang">Änderung</i><i className="tb-kurz" title="Änderung">±</i></span>
        </div>
        {VORGANG_ZEILEN.map((z) => {
          const a = w.vorher[z.feld]
          const b = w.nachher[z.feld]
          const diff = aenderung(a, b, z.nachkomma)
          return (
            <div key={z.feld} className="tb-vt-zeile" role="row">
              <span className="tb-vt-name" role="rowheader">{z.name}{z.einheit && <small>{z.einheit}</small>}</span>
              <span role="cell">{a != null ? zahl(a, z.nachkomma) : '—'}</span>
              <span role="cell"><b>{b != null ? zahl(b, z.nachkomma) : '—'}</b></span>
              {a == null && b == null
                ? <span role="cell" className="tb-vt-diff tb-vt-leer">nicht gemessen</span>
                : <span role="cell" className={`tb-vt-diff${diff ? ` is-${diff.richtung}` : ''}`}>{diff?.text ?? ''}</span>}
            </div>
          )
        })}
      </div>
      {(wasser || w.liter != null) && (
        <p className="tb-zahlen">
          {[w.komplett ? 'Komplettwechsel' : 'Teilwechsel',
            w.liter != null ? `${zahl(w.liter, w.liter % 1 === 0 ? 0 : 1)} L` : null,
            w.prozent != null && !w.komplett ? `${zahl(w.prozent, 0)} %` : null,
            wasser,
            w.wasserEc != null ? `Wasser-EC ${zahl(w.wasserEc, 2)}` : null].filter(Boolean).join(' · ')}
        </p>
      )}
      <Posten liste={posten} titel="Zugaben" />
      <p className="tb-gebucht">
        {posten.length > 0 && <>✓ {posten.length === 1 ? '1 Posten' : `${posten.length} Posten`} im Verbrauch gebucht · </>}
        <Link className="tb-link" to={`/wasserwechsel?growId=${growId}`}>Vorgang öffnen</Link>
      </p>
      {w.notiz && <p className="tb-notiz">{w.notiz}</p>}
      {w.journal && <NotizImVorgang notiz={w.journal} onGeaendert={onGeaendert} />}
    </div>
  )
}

/** Der Journaleintrag eines Wechsels — im Vorgang, mit Bearbeiten und Entfernen. */
function NotizImVorgang({ notiz, onGeaendert }: { notiz: TagebuchNotizDto; onGeaendert: () => void | Promise<void> }) {
  return (
    <div className="tb-vorgang-notiz">
      <NotizInhalt notiz={notiz} titel={notiz.titel ?? ''} fotos={[]} onGeaendert={onGeaendert} eingebettet />
    </div>
  )
}

function NotizInhalt({ notiz, titel, fotos, onGeaendert, eingebettet }: {
  notiz: TagebuchNotizDto
  titel: string
  fotos: PhotoAssetDto[]
  onGeaendert: () => void | Promise<void>
  eingebettet?: boolean
}) {
  const [bearbeiten, setBearbeiten] = useState(false)
  const [fehler, setFehler] = useState<string | null>(null)
  const etikett = notizEtikett(notiz)
  const name = titel || etikett.tag

  async function entfernen() {
    if (!window.confirm(`Eintrag „${name}" wirklich entfernen?`)) return
    try {
      await apiFetch(`/api/journal/${notiz.id}`, { method: 'DELETE' })
      await onGeaendert()
    } catch (caught) {
      setFehler(formatApiError(caught, 'Eintrag konnte nicht entfernt werden.'))
    }
  }

  return (
    <>
      <Kopf
        tag={eingebettet ? 'Journal' : etikett.tag}
        ton={eingebettet ? 'muted' : etikett.ton}
        titel={titel}
        herkunft={notiz.automatisch ? 'automatisch' : null}
        aktionen={(
          <>
            <button type="button" className="js-weg js-bearbeiten" aria-expanded={bearbeiten} disabled={bearbeiten}
              aria-label={`Eintrag „${name}" bearbeiten`} onClick={() => setBearbeiten(true)}>Bearbeiten</button>
            <button type="button" className="js-weg" aria-label={`Eintrag „${name}" entfernen`} onClick={() => void entfernen()}>Entfernen</button>
          </>
        )}
      />
      {!bearbeiten && notiz.text && <p>{notiz.text}</p>}
      {fehler && <p className="tb-fehler" role="alert">{fehler}</p>}
      {bearbeiten && (
        <NotizBearbeiten
          notiz={notiz}
          onAbbrechen={() => setBearbeiten(false)}
          onFertig={async () => { setBearbeiten(false); await onGeaendert() }}
        />
      )}
      <Fotos fotos={fotos} />
    </>
  )
}

function AddbackInhalt({ growId, e, onGeaendert }: { growId: string; e: TagebuchEreignisDto; onGeaendert: () => void | Promise<void> }) {
  const a = e.addback!
  const [fehler, setFehler] = useState<string | null>(null)
  const teile = [
    a.ecVorher != null || a.ecNachher != null ? `EC ${zahl(a.ecVorher, 2)} → ${zahl(a.ecNachher, 2)}` : null,
    a.phVorher != null || a.phNachher != null ? `pH ${zahl(a.phVorher, 2)} → ${zahl(a.phNachher, 2)}` : null,
    wasserName(a.wasser),
  ].filter(Boolean)

  async function entfernen() {
    if (!window.confirm(`„${e.titel}" von ${e.uhrzeit} Uhr wirklich entfernen?`)) return
    try {
      await apiFetch(`/api/grows/${growId}/addback/logs/${a.id}`, { method: 'DELETE' })
      await onGeaendert()
    } catch (caught) {
      setFehler(formatApiError(caught, 'Eintrag konnte nicht entfernt werden.'))
    }
  }

  return (
    <>
      <Kopf
        tag={addbackArtName(a.art)}
        ton="info"
        titel={e.titel}
        aktionen={<button type="button" className="js-weg" aria-label={`${e.titel} von ${e.uhrzeit} Uhr entfernen`} onClick={() => void entfernen()}>Entfernen</button>}
      />
      {teile.length > 0 && <p className="tb-zahlen">{teile.join(' · ')}</p>}
      {a.notiz && <p>{a.notiz}</p>}
      {fehler && <p className="tb-fehler" role="alert">{fehler}</p>}
    </>
  )
}
