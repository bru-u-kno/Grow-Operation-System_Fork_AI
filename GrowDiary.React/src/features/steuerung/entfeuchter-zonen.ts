/**
 * Fork AI (A-014): Die drei Zonen der Entfeuchter-Seiten — im Ziel, knapp
 * daneben, deutlich daneben. Reiner Rechenteil ohne React.
 *
 * <b>Anlass (Bru, 07.10.2026).</b> Die Schwellen auf der Seite waren farblich
 * nicht zu unterscheiden: man sah nicht, ob man im Zielbereich, daneben oder im
 * kritischen Bereich liegt. Farbe allein reicht nicht — die Seite nennt die Zone
 * immer auch als Wort (`ZONEN_WORT`).
 */

export type Zone = 'ziel' | 'knapp' | 'kritisch'

export const ZONEN_WORT: Record<Zone, string> = {
  ziel: 'im Ziel',
  knapp: 'knapp daneben',
  kritisch: 'deutlich daneben',
}

/** So viele Prozentpunkte über dem Ziel gilt die Luftfeuchte noch als „knapp daneben". */
export const RF_KNAPP_PUNKTE = 4

/** Die schlechtere von zwei Zonen; `null` zählt als „keine Aussage". */
export function schlechtereZone(a: Zone | null, b: Zone | null): Zone | null {
  const rang: Record<Zone, number> = { ziel: 0, knapp: 1, kritisch: 2 }
  if (a == null) return b
  if (b == null) return a
  return rang[a] >= rang[b] ? a : b
}

/** CSS-Klasse für den Messpunkt und die Zahl darüber; leer im Ziel (das ist die Grundfarbe). */
export function zonenKlasse(zone: Zone | null): string {
  return zone === 'knapp' ? 'is-knapp' : zone === 'kritisch' ? 'is-kritisch' : ''
}

/** Alarmton der Lagemeldung oben auf der Seite. */
export function zonenTon(zone: Zone): 'ok' | 'warn' | 'critical' {
  return zone === 'ziel' ? 'ok' : zone === 'knapp' ? 'warn' : 'critical'
}
