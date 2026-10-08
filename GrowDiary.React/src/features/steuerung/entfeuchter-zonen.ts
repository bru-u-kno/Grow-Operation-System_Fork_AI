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

/** So viel Kelvin unter der Höchsttemperatur beginnt „knapp daneben" (ohne eigenen Zusatz-Abstand). */
export const TEMP_KNAPP_K = 1

const gueltig = (w: number | null | undefined): w is number => w != null && Number.isFinite(w)

/**
 * Zone der Luftfeuchte. Ziel ist die Plan-Luftfeuchte (Obergrenze); bis
 * {@link RF_KNAPP_PUNKTE} Punkte darüber ist es „knapp", darüber „deutlich daneben".
 * Zu trocken ist hier kein Befund: Der Entfeuchter ist dafür nicht zuständig.
 */
export function feuchteZone(ist: number | null | undefined, ziel: number | null | undefined): Zone | null {
  if (!gueltig(ist) || !gueltig(ziel)) return null
  if (ist <= ziel) return 'ziel'
  return ist <= ziel + RF_KNAPP_PUNKTE ? 'knapp' : 'kritisch'
}

/**
 * Zone der Temperatur: bis `zielBis` im Ziel, bis zur Höchsttemperatur `max`
 * knapp, darüber deutlich daneben (dort sind beide Entfeuchter aus).
 */
export function temperaturZone(ist: number | null | undefined, max: number, zielBis: number = max - TEMP_KNAPP_K): Zone | null {
  if (!gueltig(ist)) return null
  if (ist <= zielBis) return 'ziel'
  return ist <= max ? 'knapp' : 'kritisch'
}

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

export type ZonenStrecke = { zone: Zone; links: number; breite: number }

/**
 * Die drei Farbstrecken einer Skala in Prozent der Bandbreite. `ziel` ist das
 * Ende der grünen, `knapp` das Ende der gelben Strecke; beide werden auf die
 * Skala begrenzt, damit nichts über den Rand ragt.
 */
export function zonenStrecken(von: number, bis: number, ziel: number, knapp: number): ZonenStrecke[] {
  const pos = (w: number) => Math.round(Math.max(0, Math.min(100, ((w - von) / (bis - von)) * 100)) * 10) / 10
  const z = pos(ziel)
  const k = Math.max(z, pos(knapp))
  return [
    { zone: 'ziel' as const, links: 0, breite: z },
    { zone: 'knapp' as const, links: z, breite: k - z },
    { zone: 'kritisch' as const, links: k, breite: 100 - k },
  ].filter((s) => s.breite > 0)
}

/** Ab so vielen Prozent der Bandbreite Abstand stehen zwei Beschriftungen nebeneinander (Handy: ≈ 40 px). */
export const MARKEN_MINDESTABSTAND = 14

/**
 * In welche Zeile jede Marke kommt: dicht beieinander liegende Marken wandern in eine zweite Zeile,
 * damit sich ihre Beschriftungen nie überdecken. Gibt je Marke 0 (oben) oder 1 (unten) zurück.
 */
export function markenZeilen(positionen: number[]): number[] {
  const reihenfolge = positionen.map((p, i) => ({ p, i })).sort((a, b) => a.p - b.p)
  const zuletzt = [-Infinity, -Infinity]
  const zeile: number[] = positionen.map(() => 0)
  for (const { p, i } of reihenfolge) {
    const z = p - zuletzt[0] >= MARKEN_MINDESTABSTAND ? 0 : p - zuletzt[1] >= MARKEN_MINDESTABSTAND ? 1 : 0
    zeile[i] = z
    zuletzt[z] = p
  }
  return zeile
}
