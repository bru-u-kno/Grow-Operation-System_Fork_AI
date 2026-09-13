/**
 * Wie lange das Licht noch so bleibt, wie es ist.
 *
 * Gerechnet wird in der Oberfläche und nicht auf dem Server: eine fertige
 * Restzeit altert zwischen zwei Abrufen und stünde nach fünf Minuten falsch da.
 * Der Server liefert nur die beiden Schaltzeiten, diese Datei macht daraus jede
 * Minute neu einen Satz.
 */

/** Die nächste Uhrzeit `HH:mm` nach `jetzt` — auch über Mitternacht hinweg. */
export function naechsterZeitpunkt(jetzt: Date, hhmm: string): Date | null {
  const treffer = /^(\d{1,2}):(\d{2})$/.exec(hhmm.trim())
  if (!treffer) return null

  const stunde = Number(treffer[1])
  const minute = Number(treffer[2])
  if (stunde > 23 || minute > 59) return null

  const ziel = new Date(jetzt)
  ziel.setHours(stunde, minute, 0, 0)
  // Schon vorbei heisst: morgen. Ohne diesen Fall zeigt die Kachel abends um
  // 21 Uhr „vor 16 Stunden" statt „in 8 Stunden".
  if (ziel.getTime() <= jetzt.getTime()) ziel.setDate(ziel.getDate() + 1)
  return ziel
}

/** „3 Std 25 Min", „48 Min", „gleich". */
export function dauerInWorten(millisekunden: number): string {
  const minutenGesamt = Math.round(millisekunden / 60000)
  if (minutenGesamt <= 0) return 'gleich'

  const stunden = Math.floor(minutenGesamt / 60)
  const minuten = minutenGesamt % 60
  if (stunden === 0) return `${minuten} Min`
  if (minuten === 0) return `${stunden} Std`
  return `${stunden} Std ${minuten} Min`
}

/**
 * Die Zeile unter den Schaltzeiten: „noch 3 Std 25 Min bis an".
 *
 * `anJetzt` sagt, was das Licht gerade tut — daraus folgt, welche der beiden
 * Zeiten die nächste ist. Ohne die passende Zeit gibt es keine Zeile: eine
 * Restzeit zu raten wäre schlimmer als keine zu zeigen.
 */
export function restzeitText(
  jetzt: Date,
  anJetzt: boolean,
  onAt: string | null | undefined,
  offAt: string | null | undefined,
): string | null {
  const ziel = anJetzt ? offAt : onAt
  if (!ziel) return null

  const zeitpunkt = naechsterZeitpunkt(jetzt, ziel)
  if (zeitpunkt == null) return null

  const richtung = anJetzt ? 'aus' : 'an'
  return `noch ${dauerInWorten(zeitpunkt.getTime() - jetzt.getTime())} bis ${richtung}`
}
