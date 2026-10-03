/**
 * Die Masse der Zeichenflächen im Verlaufsdiagramm, in echten Pixeln.
 *
 * Eine eigene Datei, weil zwei Stellen sie brauchen: die Flächen selbst
 * (`verlauf-flaechen.tsx`) und die Kachel, die Licht-Spur und Zeitachse genau
 * unter die Zeitachse des Diagramms legen muss. Zwei abgetippte Ränder liefen
 * nach der ersten Änderung um ein paar Pixel auseinander.
 */
export type Rand = { t: number; r: number; b: number; l: number }

/** „Zusammen": alle Kurven in einem Bild. */
export const ZUSAMMEN = { hoehe: 210, rand: { t: 8, r: 6, b: 22, l: 6 } as Rand }

/** Eine Zeile von „Einzeln". Rechts Platz für die Grenzen des Zielbands. */
export const ZEILE = { hoehe: 58, rand: { t: 6, r: 42, b: 6, l: 2 } as Rand, kopf: 104, luecke: 8 }

/** Der Fokus auf einen Wert, mit echter y-Achse links. */
export const FOKUS = { hoehe: 240, rand: { t: 20, r: 8, b: 24, l: 46 } as Rand }
