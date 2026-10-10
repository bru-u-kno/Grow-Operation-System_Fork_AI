import './artikel-zeile.css'

/** Ein Verbrauchsartikel, so weit ihn die Zeile braucht. */
export type Artikel = { id: number; name: string; einheit: string; aktiv: boolean }

/** Die offene Rückfrage „als neuen Verbrauchsartikel anlegen?" — für genau eine Zeile (`schluessel`). */
export type Frage = { schluessel: string; name: string; einheit: string }

const EINHEITEN = ['ml', 'L', 'g', 'kg', 'Stück']

/**
 * Die Statuszeile unter einer Zugabe: gibt es den Artikel, wird gebucht; gibt es ihn nicht, steht das da
 * — und der Knopf „Als Artikel anlegen …" öffnet die Rückfrage. Angelegt wird erst nach „Ja, anlegen".
 */
export function ArtikelZeile({ name, menge, einheit, einheitFest, artikel, aus, frage, frageFehler, auswahl, onWahl, onBuchen, onFrage, onEinheit, onAnlegen, onNein }: {
  name: string
  menge: string | null
  einheit: string
  /** Feste Einheit (Wasser: Liter) — dann gibt es dort nichts zu wählen. */
  einheitFest?: string
  artikel: Artikel | null
  aus: boolean
  frage: Frage | null
  frageFehler: string | null
  /** Vorhandene Artikel, falls der Plan-Name einem anderen Artikel gehört. */
  auswahl?: Artikel[]
  onWahl?: (artikelId: number) => void
  onBuchen: (an: boolean) => void
  onFrage: () => void
  onEinheit: (einheit: string) => void
  onAnlegen: () => void
  onNein: () => void
}) {
  const zusatz = menge ? ` · ${menge}` : ''
  if (artikel) {
    return (
      <div className="az-ab ok" data-audit="nachfuellen-artikel-da">
        <span>✓ Artikel „{artikel.name}“ vorhanden{zusatz}</span>
        <label><input type="checkbox" checked={!aus} onChange={(e) => onBuchen(e.target.checked)} /> {aus ? 'wird nicht gebucht' : 'wird als Verbrauch gebucht'}</label>
      </div>
    )
  }
  return (
    <>
      <div className="az-ab fehlt" data-audit="nachfuellen-artikel-fehlt">
        <span>Noch kein Artikel „{name}“{zusatz} — {frage ? 'bisher wird nichts gebucht.' : 'wird nicht gebucht, nur im Tagebuch notiert.'}</span>
        {!frage && <button type="button" onClick={onFrage}>Als Artikel anlegen …</button>}
        {!frage && auswahl && auswahl.length > 0 && onWahl && (
          <select className="az-wahl" value="" aria-label={`Vorhandenen Artikel für ${name} wählen`} onChange={(e) => e.target.value && onWahl(Number(e.target.value))}>
            <option value="">oder vorhandenen wählen …</option>
            {auswahl.map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}
          </select>
        )}
      </div>
      {frage && (
        <div className="az-frage" role="group" aria-label="Neuen Verbrauchsartikel anlegen">
          <strong>„{frage.name}“ als neuen Verbrauchsartikel anlegen?</strong>
          <div className="zl">
            <label htmlFor={`nf-einheit-${frage.schluessel}`}>Einheit</label>
            {einheitFest
              ? <b id={`nf-einheit-${frage.schluessel}`}>{einheitFest}</b>
              : (
                <select id={`nf-einheit-${frage.schluessel}`} value={frage.einheit || einheit} onChange={(e) => onEinheit(e.target.value)}>
                  {EINHEITEN.map((e) => <option key={e}>{e}</option>)}
                </select>
              )}
          </div>
          <span className="az-klein">Der Artikel entsteht ohne Preis. Die Kosten trägst du später unter Kosten nach. Diese Zugabe wird sofort mitgebucht.</span>
          {frageFehler && <span className="az-fehler" role="alert">{frageFehler}</span>}
          <div className="zl">
            <button type="button" onClick={onAnlegen}>Ja, anlegen</button>
            <button type="button" className="sek" onClick={onNein}>Nicht jetzt</button>
          </div>
        </div>
      )}
    </>
  )
}
