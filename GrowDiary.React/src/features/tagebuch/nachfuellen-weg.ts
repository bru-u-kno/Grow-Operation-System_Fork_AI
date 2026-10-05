import type { TagebuchSprungDto } from '../../types'
import { feldText } from '../../zahlenfeld'
import { sprungSatz } from './tagebuch-modell'

/**
 * „Nachfüllen eintragen" an einer Auffälligkeit — die EINE Stelle, die
 * entscheidet, wohin der Knopf führt und was vorbelegt wird.
 *
 * Heute öffnet er das kleine Formular in der Zeile (POST Nachfüllen ins
 * Addback-Protokoll). Sobald Addback als Vorgang mit vorbelegbarer Adresse da
 * ist (Branch `feat/addback-vorgang`), liefert `nachfuellenWeg` stattdessen
 * `{ art: 'adresse', to }` — `AuffaelligAktionen` folgt dann der Adresse,
 * sonst ändert sich nichts. Die Vorbelegung bleibt dieselbe.
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

export type NachfuellenWeg = { art: 'formular' } | { art: 'adresse'; to: string }

/** Wohin „Nachfüllen eintragen" führt. Umstellen auf den Addback-Vorgang: nur hier. */
export function nachfuellenWeg(growId: string, vorbelegung: NachfuellenVorbelegung): NachfuellenWeg {
  void growId
  void vorbelegung
  return { art: 'formular' }
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
