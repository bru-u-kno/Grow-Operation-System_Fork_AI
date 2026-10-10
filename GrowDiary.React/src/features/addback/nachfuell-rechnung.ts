import type { AddbackLogKind, WaterSource } from '../../types'
import { istUnlesbar, zahlOderNull } from '../../zahlenfeld'
import { NACHFUELL_ART, tagebuchZeile, zahl, type TagebuchWerte } from '../vorgang/ablauf-rechnung'

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
