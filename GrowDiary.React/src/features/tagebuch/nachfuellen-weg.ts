import type { TagebuchSprungDto } from '../../types'
import { feldText, zahlOderNull } from '../../zahlenfeld'
import { sprungSatz } from './tagebuch-modell'

/**
 * „Nachfüllen eintragen" an einer Auffälligkeit — die EINE Stelle, die
 * entscheidet, wohin der Knopf führt und was vorbelegt wird.
 *
 * Seit A-006 Etappe 3 führt er in den Nachfüll-Ablauf auf `/addback`
 * (ein Vorgang: Eintrag, Messung vorher/nachher, Verbrauch, Tagebuchzeile),
 * vorbelegt über die Adresse — `vorbelegungAusLink` in
 * `features/vorgang/ablauf-rechnung.ts` liest sie. Das kleine Formular in der
 * Zeile, das nur einen Addback-Eintrag schrieb, entfällt: ein Weg, nicht zwei.
 */

/** Was vorbelegt wird — Zeitpunkt in Ortszeit der Anlage, Zahlen mit Komma. */
export type NachfuellenVorbelegung = {
  /** Der erste ruhige Wert nach dem Sprung, UTC (ISO). */
  zeitpunktUtc: string
  /** Derselbe Zeitpunkt als yyyy-MM-ddTHH:mm in Ortszeit der Anlage — für Felder, die der Server als Ortszeit liest. */
  zeitpunktOrtszeit: string
  liter: string
  ecVorher: string
  ecNachher: string
  phVorher: string
  phNachher: string
  notiz: string
}

export type NachfuellenWeg = { art: 'adresse'; to: string }

/** Wohin „Nachfüllen eintragen" führt: der Nachfüll-Ablauf, vorbelegt. Umstellen: nur hier. */
export function nachfuellenWeg(growId: string, vorbelegung: NachfuellenVorbelegung): NachfuellenWeg {
  const suche = new URLSearchParams({ growId, zeitpunkt: vorbelegung.zeitpunktUtc })
  const zahlen: Array<[string, string]> = [
    ['liter', vorbelegung.liter], ['ecVorher', vorbelegung.ecVorher], ['ecNachher', vorbelegung.ecNachher],
    ['phVorher', vorbelegung.phVorher], ['phNachher', vorbelegung.phNachher],
  ]
  // Feldtext („1,75") über die eine Leseregel der App, in die Adresse in Maschinenform („1.75").
  for (const [name, feld] of zahlen) {
    const wert = zahlOderNull(feld)
    if (wert != null) suche.set(name, String(wert))
  }
  // Die Werte kommen vom Sensor (die Auffälligkeit ist ein Sensorsprung).
  suche.set('quelle', 'sensor')
  if (vorbelegung.notiz.trim() !== '') suche.set('notiz', vorbelegung.notiz)
  return { art: 'adresse', to: `/addback?${suche.toString()}` }
}

function wert(befunde: TagebuchSprungDto[], messgroesse: string, seite: 'vorher' | 'nachher'): string {
  const b = befunde.find((x) => x.messgroesse === messgroesse)
  return b ? feldText(Number(b[seite].toFixed(2))) : ''
}

/** Hat der Wasserstand in Litern mitgesprungen, ist das die Menge. */
function literAusPegel(befunde: TagebuchSprungDto[]): string {
  const pegel = befunde.find((b) => b.messgroesse === 'reservoir-level' && b.nachher > b.vorher)
  return pegel ? feldText(Math.round((pegel.nachher - pegel.vorher) * 10) / 10) : ''
}

export function nachfuellenVorbelegung(befunde: TagebuchSprungDto[]): NachfuellenVorbelegung {
  return {
    zeitpunktUtc: befunde[0].endeUtc,
    zeitpunktOrtszeit: befunde[0].endeOrtszeit,
    liter: literAusPegel(befunde),
    ecVorher: wert(befunde, 'reservoir-ec', 'vorher'),
    ecNachher: wert(befunde, 'reservoir-ec', 'nachher'),
    phVorher: wert(befunde, 'reservoir-ph', 'vorher'),
    phNachher: wert(befunde, 'reservoir-ph', 'nachher'),
    notiz: `Nachgetragen aus dem Tagebuch: ${befunde.map(sprungSatz).join(' · ')}.`,
  }
}
