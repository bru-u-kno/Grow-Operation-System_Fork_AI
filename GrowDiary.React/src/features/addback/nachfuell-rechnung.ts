import type { AddbackLogKind, WaterSource } from '../../types'
import { ankerphaseName } from '../../deutsche-woerter'
import { istUnlesbar, zahlOderNull } from '../../zahlenfeld'
import type { Ankerphase } from '../../types'
import { aenderung, NACHFUELL_ART, tagebuchZeile, zahl, type TagebuchWerte, type Werte } from '../vorgang/ablauf-rechnung'

/**
 * Das vereinfachte Addback (A-006, Etappe 3, 10.10.2026) — die Regeln ohne Oberfläche.
 *
 * Die Vorschläge (Mischplan, Wasser-EC, EC-Ziel) rechnet das Backend; die
 * Mischrechnung des Tanks und die Tagebuchzeile stehen in `../vorgang/ablauf-rechnung`
 * und werden hier nicht noch einmal gebaut. Was hier steht, ist nur das, was dem
 * Formular allein gehört: welche Art ein Fall ist, welcher Artikel zu einem Namen
 * passt, wie voll der Tank danach ist und wann die Nachmessung fällt.
 */

/** Was der Nutzer gemacht hat — die drei Auswahlfelder oben auf der Seite. */
export type Modus = 'wasser' | 'mix' | 'zusatz'

/** Welche Art das Backend dazu speichert: Wasser, Wasser mit Dünger, Zugaben ohne Wasser. */
export const ART_VON_MODUS: Record<Modus, AddbackLogKind> = { wasser: 'TopOff', mix: 'Addback', zusatz: 'Correction' }

/** Womit aufgefüllt wurde. „eigen" sind selbst eingetragene Werte — gespeichert wird dann Leitungswasser mit eigenem EC. */
export type WasserWahl = WaterSource | 'eigen'

/** Texte der drei Fälle: Name, Unterzeile. Einmal hier, damit Seite und Prüfung dasselbe lesen. */
export const MODI: ReadonlyArray<{ modus: Modus; titel: string; hinweis: string }> = [
  { modus: 'wasser', titel: 'Nur Wasser', hinweis: 'Aufgefüllt, ohne Dünger. Der Normalfall.' },
  { modus: 'mix', titel: 'Wasser + Dünger & Zusätze', hinweis: 'Aufgefüllt und gedüngt. Dünger nach Mischplan, Zusätze wie Purolyt dazu.' },
  { modus: 'zusatz', titel: 'Nur Zusätze (ohne Wasser)', hinweis: 'Dünger, Purolyt, pH-Korrektur, Cannaboost …' },
]

/** Ein Name ohne Groß/Klein und ohne Leerraum am Rand — wie das Backend Artikelnamen vergleicht. */
export function normalisiert(name: string): string {
  return name.trim().replace(/\s+/g, ' ').toLowerCase()
}

/** Der Artikel, der zu diesem Namen gehört — oder `null`. Leere Namen passen auf nichts. */
export function artikelFuerName<T extends { id: number; name: string }>(artikel: readonly T[], name: string): T | null {
  const gesucht = normalisiert(name)
  if (gesucht === '') return null
  return artikel.find((a) => normalisiert(a.name) === gesucht) ?? null
}

/**
 * Wie voll der Tank vor und nach dem Nachfüllen ist.
 *
 * Es gibt keinen Pegelsensor in der Anlage; deshalb gilt die Annahme, die schon der
 * Wasserwechsel-Ablauf trifft: **danach ist der Tank wieder voll** (Anlagevolumen),
 * und vorher fehlten genau die nachgefüllten Liter. Wer weniger einfüllt, trägt den
 * Füllstand danach selbst ein.
 *
 * `null`, solange das Anlagevolumen fehlt oder der Füllstand nicht lesbar ist. Sind es
 * mehr Liter als der Tank danach fasst, gibt es kein „vorher" — dann `vorher: null`.
 */
export function fuellstand(anlageLiter: number | null, danachText: string, liter: number | null): { danach: number | null; vorher: number | null } {
  if (istUnlesbar(danachText)) return { danach: null, vorher: null }
  const danach = zahlOderNull(danachText) ?? anlageLiter
  if (danach == null || danach <= 0) return { danach: null, vorher: null }
  if (liter == null) return { danach, vorher: danach }
  return { danach, vorher: liter <= danach ? danach - liter : null }
}

/** Wann die Nachmessung fällt: Zeitpunkt (Ortszeit `yyyy-MM-ddTHH:mm`) plus Minuten. `null` bei unlesbarer Eingabe. */
export function nachmessungZeit(zeitpunktLokal: string, minuten: number | null): Date | null {
  if (minuten == null || !Number.isInteger(minuten) || minuten < 1 || minuten > 240) return null
  const start = new Date(zeitpunktLokal)
  if (Number.isNaN(start.getTime())) return null
  return new Date(start.getTime() + minuten * 60_000)
}

/** „13:34 Uhr" — oder mit Tag, wenn es nicht heute ist: „09.10., 13:34 Uhr". */
export function uhrzeitText(zeit: Date, jetzt: Date = new Date()): string {
  const uhr = new Intl.DateTimeFormat('de-DE', { hour: '2-digit', minute: '2-digit' }).format(zeit)
  if (zeit.toDateString() === jetzt.toDateString()) return `${uhr} Uhr`
  const tag = new Intl.DateTimeFormat('de-DE', { day: '2-digit', month: '2-digit' }).format(zeit)
  return `${tag}, ${uhr} Uhr`
}

/** Wie alt ein Sensorwert ist, in ganzen Minuten (nie negativ). */
export function alterMinuten(zeitUtc: string, jetzt: Date = new Date()): number {
  return Math.max(0, Math.round((jetzt.getTime() - new Date(zeitUtc).getTime()) / 60_000))
}

/** „vor 4 min", „vor 2 Std." — für die Live-Anzeige. */
export function alterText(minuten: number): string {
  if (minuten < 1) return 'gerade eben'
  if (minuten < 90) return `vor ${minuten} min`
  return `vor ${Math.round(minuten / 60)} Std.`
}

/**
 * Selbst eingetragene Wasserwerte als eine Zeile für Notiz und Tagebuch.
 *
 * Gespeichert wird Leitungswasser mit eigenem EC (`wasserEcMsCm`); pH, Härte und Temperatur
 * haben am Eintrag kein eigenes Feld und stehen deshalb hier im Text, damit sie nicht verloren gehen.
 */
export function eigenesWasserZeile(w: { ec: string; ph: string; haerte: string; temp: string }): string | null {
  const teil = (name: string, text: string, einheit: string) => {
    const wert = zahlOderNull(text)
    return wert == null ? null : `${name} ${zahl(wert, Number.isInteger(wert) ? 0 : wert < 10 ? 2 : 1)}${einheit}`
  }
  const teile = [teil('EC', w.ec, '\u00a0mS/cm'), teil('pH', w.ph, ''), teil('Härte', w.haerte, '\u00a0°dH'), teil('Temperatur', w.temp, '\u00a0°C')]
    .filter((t): t is string => t != null)
  return teile.length === 0 ? null : `Wasser (eigene Werte): ${teile.join(' · ')}`
}

/**
 * Die Tagebuchzeile für alle drei Fälle.
 *
 * Mit Wasser ist es die Zeile des Ablaufs („Nachfüllen 18 L Leitungswasser"); ohne Wasser
 * gibt es keine Liter, deshalb heißt die Zeile „Zusätze" und führt nur die Zugaben und die Notiz.
 * Gebaut an einer Stelle, damit Vorschau und gespeicherter Text nicht auseinanderlaufen.
 */
export function nachfuellTagebuch(modus: Modus, w: TagebuchWerte): { titel: string; text: string } {
  if (modus !== 'zusatz') return tagebuchZeile({ ...w, titel: NACHFUELL_ART[ART_VON_MODUS[modus]] })
  const zeile = tagebuchZeile({ ...w, liter: 0, titel: 'Zusätze' })
  return { titel: 'Zusätze zugegeben', text: zeile.text }
}

/**
 * Die Mengen, die wirklich zugegeben werden: abgewählte Zeilen zählen 0.
 *
 * Die Erwartung für den EC rechnet mit der Dosis der Grunddünger (`anteilPlanDosis`); eine abgewählte Zeile darf dort nicht
 * mit ihrem Vorschlag weiterzählen, sonst steht „≈ 1,80“, obwohl der Dünger gar nicht hineinkam (Befund des Prüfers, 10.10.2026).
 */
export function wirksameWerte(eigen: Werte, abgewaehlt: ReadonlySet<string>): Werte {
  return { ...eigen, ...Object.fromEntries([...abgewaehlt].map((schluessel) => [schluessel, '0'])) }
}

/**
 * Tag und Woche für den Kasten „Dein Plan heute" — aus dem Phasenanker des Grows.
 *
 * Die Woche steht nur hier und nirgends sonst im Kasten (Bru, 10.10.2026: „die Woche wird doppelt
 * erwähnt"); das Label der Plan-Spalte bleibt deshalb weg. `null`, wenn der Anker keine Tageszahl hat.
 */
export function planChips(anker: { phase: Ankerphase; tagInPhase: number; wocheInPhase: number } | null | undefined): { tag: string; woche: string } | null {
  if (anker == null || !(anker.tagInPhase > 0) || !(anker.wocheInPhase > 0)) return null
  const woche = anker.phase === 'Bluete' ? `Blütewoche ${anker.wocheInPhase}` : `Woche ${anker.wocheInPhase} ${ankerphaseName(anker.phase)}`
  return { tag: `Tag ${anker.tagInPhase}`, woche }
}

/** „Jetzt im Tank: EC 1,18 · Ziel 1,20 → 0,02 darunter" — oder `null`, wenn eine der beiden Zahlen fehlt. */
export function zielAbstandEc(ec: number | null, ziel: number | null): string | null {
  if (ec == null || ziel == null) return null
  const abstand = Number((ec - ziel).toFixed(2))
  const lage = abstand === 0 ? 'im Ziel' : `${zahl(Math.abs(abstand), 2)} ${abstand < 0 ? 'darunter' : 'darüber'}`
  return `Jetzt im Tank: EC ${zahl(ec, 2)} · Ziel ${zahl(ziel, 2)} → ${lage}`
}

/**
 * Wo der pH gegen das Band des Plans liegt. `null`, wenn der Wert oder eine Grenze fehlt — und bei
 * einem Punktziel (Min = Max, etwa 5,8): ohne Breite gäbe es „darüber" schon bei 5,86 (Befund an der Demo-App, 10.10.2026).
 */
export function phLage(ph: number | null, min: number | null, max: number | null): 'im Ziel' | 'darunter' | 'darüber' | null {
  if (ph == null || min == null || max == null || min === max) return null
  return ph < min ? 'darunter' : ph > max ? 'darüber' : 'im Ziel'
}

/** Was die neue Lösung im Tank am EC ändert: „Wirkung −0,21 – EC sinkt". Auf zwei Stellen gerundet, wie angezeigt. */
export function ecWirkung(vorher: number | null, nachher: number | null): { text: string; richtung: 'gleich' | 'sinkt' | 'steigt' } | null {
  const text = aenderung(vorher, nachher, 2)
  if (text == null || vorher == null || nachher == null) return null
  const diff = Number((nachher - vorher).toFixed(2))
  if (diff === 0) return { text: `Wirkung ${text} – EC bleibt`, richtung: 'gleich' }
  return diff < 0 ? { text: `Wirkung ${text} – EC sinkt`, richtung: 'sinkt' } : { text: `Wirkung ${text} – EC steigt`, richtung: 'steigt' }
}

/** Das pH-Ziel als Text: „5,8–6,2", bei einem Punktziel nur „5,8". `null`, wenn der Plan keins nennt. */
export function phZielText(min: number | null, max: number | null): string | null {
  if (min == null || max == null) return null
  return min === max ? zahl(min, 1) : `${zahl(min, 1)}–${zahl(max, 1)}`
}
