import { useMemo, useState } from 'react'

import { V1Sheet } from './V1Sheet'

/**
 * Fork AI (forkai.35): Eine Auswahl, die aussieht wie der Rest des Forks.
 *
 * <b>Warum nicht das native Select.</b> Android zeichnet Feld UND Aufklappliste
 * nach Systemvorgabe — grauer Kasten, Systemschrift, Systempfeil, egal was das
 * Thema sagt. Auf einer dunklen Seite fällt das auf wie ein Fremdkörper.
 *
 * <b>Was es dafür kann.</b> Suche (bei vierzehn Geräten angenehm, bei vierzig
 * unverzichtbar), Gruppen, eine Unterzeile je Eintrag und einen
 * hervorgehobenen Eintrag für den Rückweg.
 *
 * <b>Wann trotzdem das native Select.</b> Bei zwei, drei festen Möglichkeiten
 * („Tag/Nacht") ist ein Blatt zu viel Aufwand für zu wenig Entscheidung. Diese
 * Auswahl ist für Listen, in denen man sucht.
 */

export type V1Option = {
  wert: string
  text: string
  /** Zweite Zeile, etwa die Herkunft eines Ports. */
  hinweis?: string | null
  /** Überschrift, unter der der Eintrag einsortiert wird. */
  gruppe?: string | null
  /** Hebt den Eintrag hervor — gedacht für den Rückweg („wie Home Assistant es meldet"). */
  betont?: boolean
}

export function V1Select({ label, ariaLabel, wert, optionen, onWahl, titel, unterzeile, platzhalter, disabled }: {
  /** Sichtbare Beschriftung. Leer lassen, wenn sie schon daneben steht. */
  label: string
  ariaLabel?: string
  wert: string
  optionen: V1Option[]
  onWahl: (wert: string) => void
  /** Titel des Blatts; ohne ihn steht dort die Beschriftung. */
  titel?: string
  unterzeile?: string
  platzhalter?: string
  disabled?: boolean
}) {
  const [offen, setOffen] = useState(false)
  const [suche, setSuche] = useState('')

  const gewaehlt = optionen.find((option) => option.wert === wert)

  const gefiltert = useMemo(() => {
    const begriff = suche.trim().toLowerCase()
    if (begriff === '') return optionen
    return optionen.filter((option) =>
      option.betont
      || option.text.toLowerCase().includes(begriff)
      || (option.hinweis ?? '').toLowerCase().includes(begriff))
  }, [optionen, suche])

  // Gruppen in der Reihenfolge, in der sie vorkommen — die Liste bestimmt sie,
  // nicht das Alphabet: „Rubriken" vor „Geräte" ist eine Aussage.
  const gruppen = useMemo(() => {
    const reihenfolge: Array<string | null> = []
    for (const option of gefiltert) {
      const gruppe = option.gruppe ?? null
      if (!reihenfolge.includes(gruppe)) reihenfolge.push(gruppe)
    }
    return reihenfolge.map((gruppe) => ({
      gruppe,
      eintraege: gefiltert.filter((option) => (option.gruppe ?? null) === gruppe),
    }))
  }, [gefiltert])

  return (
    <>
      <label className="v1-field v1-select">
        {label !== '' && <span>{label}</span>}
        <button
          type="button"
          className="v1-select-feld"
          disabled={disabled}
          aria-haspopup="dialog"
          aria-label={ariaLabel ?? label}
          onClick={() => { setSuche(''); setOffen(true) }}
        >
          <span className={gewaehlt ? undefined : 'is-leer'}>
            {gewaehlt?.text ?? platzhalter ?? 'Bitte wählen'}
          </span>
          <em aria-hidden="true">⌄</em>
        </button>
      </label>

      <V1Sheet open={offen} onClose={() => setOffen(false)} title={titel ?? label} subtitle={unterzeile}>
        {optionen.length > 6 && (
          <input
            className="v1-select-suche"
            value={suche}
            autoFocus
            onChange={(event) => setSuche(event.target.value)}
            placeholder="Suchen …"
            aria-label="In der Auswahl suchen"
          />
        )}

        <div className="v1-select-liste">
          {gruppen.map(({ gruppe, eintraege }) => (
            <div key={gruppe ?? 'ohne'}>
              {gruppe && <p className="v1-select-gruppe">{gruppe}</p>}
              {eintraege.map((option) => (
                <button
                  key={option.wert}
                  type="button"
                  role="option"
                  aria-selected={option.wert === wert}
                  className={[
                    'v1-select-eintrag',
                    option.wert === wert ? 'is-gewaehlt' : '',
                    option.betont ? 'is-betont' : '',
                  ].filter(Boolean).join(' ')}
                  onClick={() => { setOffen(false); onWahl(option.wert) }}
                >
                  <span>
                    {option.text}
                    {option.hinweis && <small>{option.hinweis}</small>}
                  </span>
                  {option.wert === wert && <em aria-hidden="true">✓</em>}
                </button>
              ))}
            </div>
          ))}

          {gefiltert.length === 0 && <p className="v1-select-leer">Nichts gefunden.</p>}
        </div>
      </V1Sheet>
    </>
  )
}
