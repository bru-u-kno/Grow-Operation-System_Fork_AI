/**
 * Wie lange das Licht noch so bleibt, wie es ist.
 *
 * Gerechnet wird in der Oberfläche und nicht auf dem Server: eine fertige
 * Restzeit altert zwischen zwei Abrufen und stünde nach fünf Minuten falsch da.
 * Der Server liefert nur die beiden Schaltzeiten, diese Datei macht daraus jede
 * Minute neu einen Satz.
 */

/**
 * Eine Schaltzeit `HH:mm` als Stunde und Minute — oder null, wenn sie keine ist.
 *
 * Eine Stelle für das Lesen, weil zwei Abnehmer dieselbe Zeit brauchen: die
 * Restzeit auf der Licht-Kachel und die Dunkelphasen im Verlaufsdiagramm.
 * Liest die eine „8:00" und die andere nicht, zeigt die Kachel eine Restzeit
 * und das Diagramm keine Nacht.
 */
export function uhrzeitLesen(hhmm: string | null | undefined): { stunde: number; minute: number } | null {
  const treffer = /^(\d{1,2}):(\d{2})$/.exec((hhmm ?? '').trim())
  if (!treffer) return null

  const stunde = Number(treffer[1])
  const minute = Number(treffer[2])
  if (stunde > 23 || minute > 59) return null
  return { stunde, minute }
}

/** Die nächste Uhrzeit `HH:mm` nach `jetzt` — auch über Mitternacht hinweg. */
export function naechsterZeitpunkt(jetzt: Date, hhmm: string): Date | null {
  const zeit = uhrzeitLesen(hhmm)
  if (!zeit) return null

  const ziel = new Date(jetzt)
  ziel.setHours(zeit.stunde, zeit.minute, 0, 0)
  // Schon vorbei heisst: morgen. Ohne diesen Fall zeigt die Kachel abends um
  // 21 Uhr „vor 16 Stunden" statt „in 8 Stunden".
  if (ziel.getTime() <= jetzt.getTime()) ziel.setDate(ziel.getDate() + 1)
  return ziel
}

/**
 * „3 h 25 min", „48 min", „gleich".
 *
 * Dieselbe Schreibweise wie auf der Licht-Steuerung — zwei Schreibweisen für
 * dieselbe Angabe in einer App lesen sich wie zwei verschiedene Angaben.
 */
export function dauerInWorten(millisekunden: number): string {
  const minutenGesamt = Math.round(millisekunden / 60000)
  if (minutenGesamt <= 0) return 'gleich'

  const stunden = Math.floor(minutenGesamt / 60)
  const minuten = minutenGesamt % 60
  if (stunden === 0) return `${minuten} min`
  return `${stunden} h ${minuten} min`
}

/**
 * Die Restdauer bis zum nächsten Schalten: „3 h 25 min", „gleich".
 *
 * `anJetzt` sagt, was das Licht gerade tut — daraus folgt, welche der beiden
 * Zeiten die nächste ist. Ohne die passende Zeit gibt es keine Zeile: eine
 * Restzeit zu raten wäre schlimmer als keine zu zeigen.
 *
 * Die Richtung steht nicht dabei: ob als Nächstes an- oder ausgeschaltet wird,
 * sagt schon der Wert der Kachel („Aus"), und die Uhrzeiten stehen in der Zeile
 * darüber. Dreimal dasselbe in zwei Zeilen ist keine Auskunft, sondern Lärm.
 */
export function restdauer(
  jetzt: Date,
  anJetzt: boolean,
  onAt: string | null | undefined,
  offAt: string | null | undefined,
): string | null {
  const ziel = anJetzt ? offAt : onAt
  if (!ziel) return null

  const zeitpunkt = naechsterZeitpunkt(jetzt, ziel)
  if (zeitpunkt == null) return null

  /* Nur die Dauer, ohne Beiwerk.
   *
   * „Nächster Wechsel: in 5 h 52 min" brach in der schmalen Kachel um, und was
   * auf der zweiten Zeile uebrigblieb, war „min". Das Wort davor setzt die
   * Kachel selbst — so kann sie die Dauer als Block zusammenhalten und nur
   * davor umbrechen. */
  return dauerInWorten(zeitpunkt.getTime() - jetzt.getTime())
}
