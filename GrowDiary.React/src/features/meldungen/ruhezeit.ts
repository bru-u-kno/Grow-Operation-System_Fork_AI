/**
 * Die Ruhezeit der Benachrichtigungen: zwei volle Stunden, „von" und „bis".
 *
 * **Der Fehler davor (Durchsicht 02.10.2026):** gelesen wurde nur eine nackte
 * Zahl. „22:00" oder „22 Uhr" wurde still `null` — die Ruhezeit war gelöscht,
 * und die Seite meldete „Gespeichert.". Nachts kamen dann wieder Nachrichten.
 *
 * Jetzt werden die üblichen Schreibweisen gelesen; was sich nicht lesen lässt,
 * wird gemeldet statt verworfen (dasselbe Muster wie `istUnlesbar` in
 * `zahlenfeld.ts`).
 *
 * Dass eine Ruhezeit nur mit BEIDEN Grenzen gilt und nicht bei gleichen
 * Stunden, ist die Regel des Backends (`NotificationSettings.IsQuietHour`) —
 * hier wird sie nur rechtzeitig gesagt.
 */

export type Stunde = { stunde: number | null; fehler?: undefined } | { stunde?: undefined; fehler: string }

/** „22", „22:00", „22.00", „22 Uhr", „22:00 Uhr", „7h" — oder leer. */
export function stundeAusFeld(text: string): Stunde {
  const roh = text.trim().toLowerCase()
  if (roh === '') return { stunde: null }
  const treffer = /^(\d{1,2})(?:\s*[:.]\s*(\d{2}))?\s*(?:uhr|h)?$/.exec(roh)
  if (!treffer) return { fehler: `„${text.trim()}" ist keine Uhrzeit. Bitte eine volle Stunde eintragen, etwa „22" oder „22:00".` }
  const stunde = Number(treffer[1])
  const minute = treffer[2] == null ? 0 : Number(treffer[2])
  if (stunde > 24 || minute > 59 || (stunde === 24 && minute !== 0)) {
    return { fehler: `„${text.trim()}" gibt es nicht. Bitte eine Stunde von 0 bis 23 eintragen.` }
  }
  if (minute !== 0) return { fehler: 'Ruhezeiten gehen nur in vollen Stunden — bitte etwa „22" oder „22:00" eintragen.' }
  return { stunde: stunde === 24 ? 0 : stunde }
}

export type Ruhezeit =
  | { start: number | null; ende: number | null; fehler?: undefined }
  | { start?: undefined; ende?: undefined; fehler: { von?: string; bis?: string } }

/** Beide Felder zusammen — mit den Fällen, in denen das Backend keine Ruhezeit hätte. */
export function ruhezeitAusFeldern(von: string, bis: string): Ruhezeit {
  const a = stundeAusFeld(von)
  const b = stundeAusFeld(bis)
  if (a.fehler != null || b.fehler != null) {
    return { fehler: { ...(a.fehler != null ? { von: a.fehler } : {}), ...(b.fehler != null ? { bis: b.fehler } : {}) } }
  }
  if (a.stunde == null && b.stunde != null) return { fehler: { von: 'Bitte auch „Von" eintragen — mit nur einer Grenze gilt keine Ruhezeit.' } }
  if (a.stunde != null && b.stunde == null) return { fehler: { bis: 'Bitte auch „Bis" eintragen — mit nur einer Grenze gilt keine Ruhezeit.' } }
  if (a.stunde != null && a.stunde === b.stunde) return { fehler: { bis: '„Von" und „Bis" sind gleich — so gilt keine Ruhezeit. Zum Abschalten beide Felder leeren.' } }
  return { start: a.stunde ?? null, ende: b.stunde ?? null }
}
