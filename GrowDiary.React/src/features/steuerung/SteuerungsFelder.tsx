import type { ReactNode } from 'react'
import type { TempMaxModus } from './steuerung-typen'
import { zahl } from './entfeuchter-band'
import { modusFehler, zahlAusFeld } from './feld-fehler'
import './steuerung.css'

/**
 * Felder, die mehrere Steuerungsseiten teilen.
 *
 * <b>Herkunft.</b> Bis A-009 (06.10.2026) lagen `TempMaxBlock` und `Zahl` in
 * `EntfeuchterDetail.tsx`. Die Seite „Zusatz-Entfeuchter" zeigt und ändert
 * dieselben Höchsttemperatur-Felder (ENTSCHEIDUNGEN.md, Punkt 5) — ein zweites
 * Formular dafür wäre ein zweiter Ort, der auseinanderläuft.
 */

/** Was die Seite „Empfohlen: …" nennt, samt Weg zurück. */
export type Empfehlung = {
  text: string
  /** Steht das Feld schon auf der Empfehlung? Dann ein Häkchen statt des Knopfs. */
  gleich: boolean
  onZuruecksetzen: () => void
}

/** Temperatur max. für Tag oder Nacht: „Plan +" Abstand oder „Fest". */
export function TempMaxBlock({ titel, ariaLabel, planText, modus, abstand, fest, plan, ergebnis, onModus, onAbstand, onFest, fehlerAbstand, fehlerFest, empfohlen }: {
  titel: string
  /** Name der Wahlgruppe für Vorlese-Programme; ohne Angabe „Temperatur max. {titel}". */
  ariaLabel?: string
  planText: string
  modus: TempMaxModus
  abstand: number
  fest: number
  plan: number | null
  ergebnis: number
  onModus: (m: TempMaxModus) => void
  onAbstand: (v: number) => void
  onFest: (v: number) => void
  fehlerAbstand?: string
  fehlerFest?: string
  /** Optional: die Zeile „Empfohlen: …" mit Zurücksetzen (Seite Zusatz-Entfeuchter). */
  empfohlen?: Empfehlung | null
}) {
  // Ein Fehler am Feld, das der Modus ausblendet, nennt Feld und Modus.
  const fehler = modusFehler(modus, {
    plan: { feld: 'Abstand zum Plan', modus: 'Plan +', fehler: fehlerAbstand },
    fest: { feld: 'Fester Wert', modus: 'Fest', fehler: fehlerFest },
  })
  return (
    <div className={empfohlen && !empfohlen.gleich ? 'ef-tempmax is-geaendert' : 'ef-tempmax'}>
      <div className="st-feldzeile">
        <span className="st-etikett">
          {titel}
          <small>{planText}</small>
          {fehler && <span className="st-fehler">{fehler}</span>}
        </span>
        <span className="ef-stufen" role="radiogroup" aria-label={ariaLabel ?? `Temperatur max. ${titel}`}>
          <button type="button" role="radio" className="st-chip" aria-checked={modus === 'plan'} aria-current={modus === 'plan'} onClick={() => onModus('plan')}>Plan +</button>
          <button type="button" role="radio" className="st-chip" aria-checked={modus === 'fest'} aria-current={modus === 'fest'} onClick={() => onModus('fest')}>Fest</button>
        </span>
      </div>
      {modus === 'plan' ? (
        <>
          <Zahl label="Abstand zum Plan" hinweis="Wandert mit der Planwoche." einheit="K" wert={abstand} min={0} max={15} schritt={0.5} onChange={onAbstand} />
          <p className="ef-formel">
            {plan == null ? `Ohne Plan: fester Wert ${zahl(fest)} °C` : `${zahl(plan)} + ${zahl(abstand)} = `}
            {plan != null && <b>{zahl(ergebnis)} °C</b>}
          </p>
        </>
      ) : (
        <Zahl label="Fester Wert" hinweis="Bleibt, egal welche Woche." einheit="°C" wert={fest} min={15} max={35} schritt={0.5} onChange={onFest} />
      )}
      {empfohlen && (
        <p className={empfohlen.gleich ? 'ez-empf' : 'ez-empf is-abweichend'}>
          {empfohlen.gleich ? `${empfohlen.text} ✓` : empfohlen.text}
          {!empfohlen.gleich && <button type="button" onClick={empfohlen.onZuruecksetzen}>zurücksetzen</button>}
        </p>
      )}
    </div>
  )
}

/** Ein Zahlenfeld im Box-Modus — kein Schieberegler, am Telefon trifft man damit keinen Wert. */
export function Zahl({ label, hinweis, einheit, wert, min, max, schritt, onChange, fehler, ruht }: {
  label: string
  hinweis: ReactNode
  einheit?: string
  wert: number
  min: number
  max: number
  schritt: number
  onChange: (wert: number) => void
  fehler?: string
  /** A-014: gerade ohne Wirkung — blass und gesperrt, statt ein Feld zu zeigen, das nichts bewirkt. */
  ruht?: boolean
}) {
  return (
    <div className={ruht ? 'st-feldzeile is-ruht' : 'st-feldzeile'}>
      <span className="st-etikett">
        {label}
        <small>{hinweis}</small>
        {fehler && <span className="st-fehler">{fehler}</span>}
      </span>
      <span className="st-eingaben">
        <input
          type="number"
          inputMode="decimal"
          min={min}
          max={max}
          step={schritt}
          aria-label={label}
          disabled={ruht}
          value={Number.isFinite(wert) ? wert : ''}
          onChange={(e) => {
            const neu = zahlAusFeld(e.target.value)
            if (neu != null) onChange(neu)
          }}
        />
        {einheit && <span className="st-einheit">{einheit}</span>}
      </span>
    </div>
  )
}
