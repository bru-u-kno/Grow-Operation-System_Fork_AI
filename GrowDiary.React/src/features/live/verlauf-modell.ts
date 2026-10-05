import { schaltzeitAmTag, uhrzeitLesen } from './licht-restzeit'
import { tagKurz } from '../steuerung/steuerung-typen'

/**
 * Die Rechnung hinter dem Verlaufsdiagramm der Live-Seite — ohne React, ohne DOM.
 *
 * Alles, was hier steht, ist in `verlauf-modell.test.ts` geprüft. Die
 * Komponente (`Verlaufsdiagramm.tsx`) zeichnet nur, was diese Datei ausrechnet:
 * welcher Ausschnitt, welche Teilung, wo die Nacht liegt, wo die Linie eine
 * Lücke hat und was unter dem Zeiger steht.
 *
 * Alle Zeiten sind Millisekunden seit 1970 (UTC), angezeigt wird in der
 * Ortszeit des Browsers — wie überall in der App (siehe `licht-restzeit.ts`).
 */

export const MINUTE = 60_000
export const STUNDE = 60 * MINUTE
export const TAG = 24 * STUNDE

/** Enger als eine Viertelstunde zeigt das Diagramm nichts mehr, was die Rohwerte (alle 5 min) hergeben. */
export const MIN_BREITE = 15 * MINUTE
/** Weiter als sieben Tage reichen die Rohwerte nicht (`SensorHistoryApiController`). */
export const MAX_BREITE = 7 * TAG

export type Fenster = { von: number; bis: number }
export type Punkt = { t: number; v: number }

export const ZEITRAEUME = [
  { id: '1h', name: '1 Std', breite: STUNDE },
  { id: '6h', name: '6 Std', breite: 6 * STUNDE },
  { id: '24h', name: '24 Std', breite: TAG },
  { id: '7t', name: '7 Tage', breite: 7 * TAG },
] as const

export type ZeitraumId = (typeof ZEITRAEUME)[number]['id']

/**
 * Die Kurvenfarben — die einzige Tabelle dafür in der App.
 *
 * Übernommen aus dem früheren `HistoryChart.tsx`. Die Wassertemperatur hatte
 * dort dasselbe Rot wie die Lufttemperatur; zusammen eingeschaltet waren die
 * zwei Linien nicht zu unterscheiden. Sie ist jetzt orange (wie im Entwurf,
 * den der Nutzer am 03.10.2026 gewählt hat), der Wasserstand bekommt ein
 * eigenes Grün statt einer Reservefarbe.
 */
export const KURVEN_FARBEN: Readonly<Record<string, string>> = {
  temperature: '#ef4444',
  humidity: '#6366f1',
  vpd: '#22c55e',
  co2: '#ec4899',
  ppfd: '#f59e0b',
  'reservoir-ph': '#0ea5e9',
  'reservoir-ec': '#a855f7',
  'reservoir-temp': '#f97316',
  'reservoir-level': '#65a30d',
  orp: '#14b8a6',
  'dissolved-oxygen': '#64748b',
  // Die Luft außerhalb des Zelts: dunkler als die übrigen, damit sie neben den
  // Innenwerten nicht verschwimmt. Ausgesucht über den Farbabstand zu allen
  // elf oben (CIE-Lab ≥ 22, so weit wie die engsten Paare der Tabelle) und den
  // Kontrast in BEIDEN Themen, gemessen am gebauten Stand gegen den Grund der
  // Kurven (hell rgb(246,249,246), dunkel rgba(14,22,19,.92)): hell
  // 4,6 / 3,5 / 4,7, dunkel 3,7 / 5,0 / 3,7 — alle drei über 3:1 für Linien.
  'outside-temperature': '#a16207',
  'outside-humidity': '#3b82f6',
  'outside-vpd': '#15803d',
}

const RESERVE = ['#8b5cf6', '#0891b2', '#d97706', '#be123c', '#4d7c0f']

export function kurvenFarbe(key: string, index: number): string {
  return KURVEN_FARBEN[key] ?? RESERVE[index % RESERVE.length]
}

/* ---------------------------------------------------------------------------
 * Fenster: Ausschnitt begrenzen, blättern, zoomen
 * ------------------------------------------------------------------------- */

/**
 * Den Ausschnitt auf das Erlaubte bringen: 15 min bis 7 Tage breit, und
 * innerhalb der Daten.
 *
 * Wird die Breite beschnitten, bleibt die Mitte stehen — sonst springt der
 * Ausschnitt beim Zoomen an den Rand, unter dem Finger weg.
 */
export function begrenzeFenster(fenster: Fenster, grenzen: Fenster): Fenster {
  const spanne = Math.max(0, grenzen.bis - grenzen.von)
  const hoechstens = Math.max(MIN_BREITE, Math.min(MAX_BREITE, spanne))
  const gewuenscht = fenster.bis - fenster.von
  const breite = Math.min(Math.max(Number.isFinite(gewuenscht) ? gewuenscht : hoechstens, MIN_BREITE), hoechstens)

  let von = breite === gewuenscht ? fenster.von : (fenster.von + fenster.bis) / 2 - breite / 2
  let bis = von + breite
  if (bis > grenzen.bis) { bis = grenzen.bis; von = bis - breite }
  // Nur nach rechts schieben, wenn es dort Platz gibt — bei weniger Daten als
  // 15 min ragt das Fenster lieber links ins Leere als rechts über „jetzt".
  if (von < grenzen.von && breite <= spanne) { von = grenzen.von; bis = von + breite }
  return { von, bis }
}

/** Der gewählte Zeitraum, rechtsbündig an den neuesten Daten. */
export function fensterFuerZeitraum(id: ZeitraumId, grenzen: Fenster): Fenster {
  const breite = ZEITRAEUME.find((zeitraum) => zeitraum.id === id)?.breite ?? TAG
  return begrenzeFenster({ von: grenzen.bis - breite, bis: grenzen.bis }, grenzen)
}

/** ◀ / ▶: um eine ganze Fensterbreite. */
export function blaettern(fenster: Fenster, richtung: -1 | 1, grenzen: Fenster): Fenster {
  const breite = fenster.bis - fenster.von
  return begrenzeFenster({ von: fenster.von + richtung * breite, bis: fenster.bis + richtung * breite }, grenzen)
}

/** Darf noch geblättert werden? Eine Sekunde Spiel gegen Rundung. */
export function kannBlaettern(fenster: Fenster, grenzen: Fenster): { zurueck: boolean; vor: boolean } {
  return { zurueck: fenster.von > grenzen.von + 1000, vor: fenster.bis < grenzen.bis - 1000 }
}

/**
 * Ein Fenster der Breite `breite`, in dem `ankerZeit` an der Stelle
 * `ankerAnteil` (0 = linker Rand, 1 = rechter Rand) steht.
 *
 * Damit rechnen beide Zoom-Arten: das Mausrad (Anker = Zeit unter der Maus)
 * und die Zwei-Finger-Geste (Anker = Zeit, die zu Beginn zwischen den Fingern
 * lag, an der Stelle, an der der Mittelpunkt der Finger jetzt ist — so
 * verschiebt dieselbe Geste auch).
 */
export function fensterUmAnker(ankerZeit: number, ankerAnteil: number, breite: number, grenzen: Fenster): Fenster {
  // Erst die Breite begrenzen, und zwar UM DEN ANKER — sonst kippt das Bild
  // beim Anschlag an 15 min zur Mitte hin, unter dem Finger weg.
  const hoechstens = Math.max(MIN_BREITE, Math.min(MAX_BREITE, grenzen.bis - grenzen.von))
  const breiteNeu = Math.min(Math.max(breite, MIN_BREITE), hoechstens)
  const von = ankerZeit - ankerAnteil * breiteNeu
  return begrenzeFenster({ von, bis: von + breiteNeu }, grenzen)
}

/** Mausrad: `faktor` > 1 zoomt heraus, < 1 hinein — um die Zeit unter der Maus. */
export function zoomUm(fenster: Fenster, zeit: number, faktor: number, grenzen: Fenster): Fenster {
  const breite = fenster.bis - fenster.von
  const anteil = breite > 0 ? (zeit - fenster.von) / breite : 0.5
  return fensterUmAnker(zeit, anteil, breite * faktor, grenzen)
}

/** Welcher Zeitraum-Knopf passt zur Breite? Nach dem Blättern bleibt „6 Std" so markiert. */
export function zeitraumBeiBreite(fenster: Fenster): ZeitraumId | null {
  const breite = fenster.bis - fenster.von
  return ZEITRAEUME.find((zeitraum) => Math.abs(zeitraum.breite - breite) < 1000)?.id ?? null
}

/* ---------------------------------------------------------------------------
 * Texte: Spanne, Uhrzeit, Zahl
 * ------------------------------------------------------------------------- */

const UHR = new Intl.DateTimeFormat('de-DE', { hour: '2-digit', minute: '2-digit' })

export function uhrzeit(t: number): string {
  return UHR.format(new Date(t))
}

/** `2026-10-03` in Ortszeit — für `tagKurz`, der genau diese Form liest. */
function lokalesDatum(t: number): string {
  const d = new Date(t)
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

/** „Sa 03.10." — dieselbe Schreibweise wie die Tagesliste der Steuerung. */
export function tagText(t: number): string {
  return tagKurz(lokalesDatum(t))
}

function gleicherTag(a: number, b: number): boolean {
  return lokalesDatum(a) === lokalesDatum(b)
}

/**
 * Die Spanne über dem Diagramm, als Teile, die je in einem Stück bleiben.
 *
 * Gewünscht vom Nutzer am 03.10.2026, weil „Fr., 02.10. 08:18 – Sa., 03.10.
 * 08:18" seltsam aussah:
 * - gleicher Kalendertag: `Sa 03.10. · 00:18 –` / `06:18`
 * - über Mitternacht, höchstens zwei Tage: `Fr 02.10. 08:18 –` / `Sa 03.10. 08:18`
 * - länger: `Sa 26.09. –` / `Sa 03.10.` (ohne Uhrzeit)
 *
 * Der Strich steht am ENDE des ersten Teils: bricht die Zeile, steht der
 * Bis-Teil ganz in Zeile zwei und die erste Zeile endet sichtbar offen.
 */
export function spannenTeile(fenster: Fenster): string[] {
  const { von, bis } = fenster
  if (gleicherTag(von, bis)) return [`${tagText(von)} · ${uhrzeit(von)} –`, uhrzeit(bis)]
  if (bis - von <= 2 * TAG + 1000) return [`${tagText(von)} ${uhrzeit(von)} –`, `${tagText(bis)} ${uhrzeit(bis)}`]
  return [`${tagText(von)} –`, tagText(bis)]
}

/**
 * Ein Messwert mit fester Stellenzahl, deutsch — „24,6", „1020", „–".
 *
 * Ohne Tausenderpunkt, wie überall in der App (`MetricTile`, Befund B8 in
 * `docs/pruefung-2026-10-01.md`): die Kachel daneben schreibt „1020 ppm".
 */
export function zahl(wert: number | null | undefined, stellen: number): string {
  if (wert == null || !Number.isFinite(wert)) return '–'
  return wert.toLocaleString('de-DE', { minimumFractionDigits: stellen, maximumFractionDigits: stellen, useGrouping: false })
}

/** Eine Grenze an der Achse: nur so viele Stellen wie nötig — „5,8", „1,25", „26". */
export function grenzText(wert: number, stellen: number): string {
  return wert.toLocaleString('de-DE', { maximumFractionDigits: stellen, useGrouping: false })
}

/* ---------------------------------------------------------------------------
 * Zeitachse
 * ------------------------------------------------------------------------- */

type TeilungsArt = 'uhr' | 'uhrOderTag' | 'tag'

/** Beschriftungsabstand nach Breite des Ausschnitts: 15 min / 1 h / 4 h / 12 h / Tag. */
export function teilung(breite: number): { schrittMinuten: number; art: TeilungsArt } {
  if (breite <= 2 * STUNDE) return { schrittMinuten: 15, art: 'uhr' }
  if (breite <= 8 * STUNDE) return { schrittMinuten: 60, art: 'uhr' }
  if (breite <= 30 * STUNDE) return { schrittMinuten: 240, art: 'uhr' }
  if (breite <= 3 * TAG) return { schrittMinuten: 720, art: 'uhrOderTag' }
  return { schrittMinuten: 1440, art: 'tag' }
}

/** Breite eines Achsentexts in px — 11 px Monospace, 0,6 em je Zeichen, mit Reserve. */
export function textBreite(text: string): number {
  return text.length * 6.9
}

export type AchsenMarke = { t: number; x: number; text: string }

/**
 * Die Marken der Zeitachse, in Ortszeit ausgerichtet (4-h-Marken auf 0, 4, 8 …
 * Uhr, Tagesmarken auf Mitternacht) und so ausgedünnt, dass zwei Texte
 * mindestens 52 px auseinander stehen und keiner über den Rand ragt.
 */
export function achsenMarken(
  fenster: Fenster, plotLinks: number, plotBreite: number, gesamtBreite: number, mindestAbstand = 52,
): AchsenMarke[] {
  const breite = fenster.bis - fenster.von
  if (breite <= 0 || plotBreite <= 0) return []
  const { schrittMinuten, art } = teilung(breite)
  const x = (t: number) => plotLinks + ((t - fenster.von) / breite) * plotBreite

  const marken: AchsenMarke[] = []
  const tag = new Date(fenster.von)
  tag.setHours(0, 0, 0, 0)
  let letzteRechts = -Infinity
  // Tageweise in Ortszeit, damit eine Zeitumstellung die Marken nicht verschiebt.
  for (let sicherung = 0; tag.getTime() <= fenster.bis && sicherung < 400; sicherung++) {
    for (let minute = 0; minute < 1440; minute += schrittMinuten) {
      const zeitpunkt = new Date(tag.getFullYear(), tag.getMonth(), tag.getDate(), 0, minute).getTime()
      if (zeitpunkt < fenster.von || zeitpunkt > fenster.bis) continue
      const mitternacht = minute === 0
      const text = art === 'tag' || (art === 'uhrOderTag' && mitternacht) ? tagText(zeitpunkt) : uhrzeit(zeitpunkt)
      const xt = x(zeitpunkt)
      const halb = textBreite(text) / 2
      if (xt - halb < 0 || xt + halb > gesamtBreite) continue
      if (xt - halb < letzteRechts + Math.max(0, mindestAbstand - 2 * halb)) continue
      if (marken.length > 0 && xt - marken[marken.length - 1].x < mindestAbstand) continue
      marken.push({ t: zeitpunkt, x: xt, text })
      letzteRechts = xt + halb
    }
    tag.setDate(tag.getDate() + 1)
  }
  return marken
}

/**
 * Runde Teilstriche für eine echte y-Achse (Fokus-Ansicht): 1, 2 oder 5 mal
 * eine Zehnerpotenz, so dass vier bis sechs Striche entstehen.
 */
export function yTeilung(min: number, max: number): { unten: number; oben: number; schritt: number; marken: number[] } {
  let lo = min
  let hi = max
  if (!(hi > lo)) { lo -= 1; hi += 1 }
  const roh = (hi - lo) / 4
  const potenz = 10 ** Math.floor(Math.log10(roh))
  const schritt = [1, 2, 5, 10].map((f) => f * potenz).find((s) => s >= roh) ?? 10 * potenz
  const unten = Math.floor(lo / schritt) * schritt
  const oben = Math.ceil(hi / schritt) * schritt
  const marken: number[] = []
  for (let v = unten; v <= oben + schritt / 1000; v += schritt) marken.push(Number(v.toFixed(6)))
  return { unten, oben, schritt, marken }
}

/* ---------------------------------------------------------------------------
 * Licht und Dunkelphase
 * ------------------------------------------------------------------------- */

function zusammenlegen(stuecke: Fenster[]): Fenster[] {
  const sortiert = [...stuecke].sort((a, b) => a.von - b.von)
  const raus: Fenster[] = []
  for (const stueck of sortiert) {
    const letztes = raus[raus.length - 1]
    if (letztes && stueck.von <= letztes.bis) letztes.bis = Math.max(letztes.bis, stueck.bis)
    else raus.push({ ...stueck })
  }
  return raus
}

/**
 * Die Lichtphasen im Ausschnitt nach dem Lichtplan (`lightOnAt`/`lightOffAt`
 * der Licht-Kachel), oder null, wenn kein Plan bekannt ist.
 *
 * Die Uhrzeiten gelten in der Zone, deren Versatz `versatzMinuten` nennt
 * (`lightUtcOffsetMinutes`, siehe `schaltzeitAmTag`) — nicht in der des
 * Browsers. Liegt „aus" vor „an" (z. B. an 20:00, aus 08:00), brennt das
 * Licht über Mitternacht — das Stück reicht dann in den nächsten Tag.
 *
 * Bewusst EIN Zyklus für alle Tage im Ausschnitt: der Server kennt nur den
 * gelernten Zyklus von jetzt. Wurde innerhalb der sieben Tage umgestellt
 * (Flip auf 12/12), liegen die Streifen vor dem Wechsel an der neuen Stelle.
 */
export function lichtPhasen(
  fenster: Fenster, an: string | null | undefined, aus: string | null | undefined, versatzMinuten?: number | null,
): Fenster[] | null {
  const ein = uhrzeitLesen(an)
  const ab = uhrzeitLesen(aus)
  if (!ein || !ab) return null
  const einMin = ein.stunde * 60 + ein.minute
  const abMin = ab.stunde * 60 + ab.minute
  if (einMin === abMin) return null

  const stuecke: Fenster[] = []
  for (let tage = -1; tage < 400; tage++) {
    const von = schaltzeitAmTag(fenster.von, ein, versatzMinuten, tage)
    if (von > fenster.bis) break
    const bis = schaltzeitAmTag(fenster.von, ab, versatzMinuten, abMin > einMin ? tage : tage + 1)
    const a = Math.max(von, fenster.von)
    const b = Math.min(bis, fenster.bis)
    if (b > a) stuecke.push({ von: a, bis: b })
  }
  return zusammenlegen(stuecke)
}

/** Die Dunkelphasen im Ausschnitt — je Nacht EIN Stück, nicht viele kleine. */
export function dunkelphasen(
  fenster: Fenster, an: string | null | undefined, aus: string | null | undefined, versatzMinuten?: number | null,
): Fenster[] {
  const licht = lichtPhasen(fenster, an, aus, versatzMinuten)
  if (!licht) return []
  const raus: Fenster[] = []
  let anfang = fenster.von
  for (const stueck of licht) {
    if (stueck.von > anfang) raus.push({ von: anfang, bis: stueck.von })
    anfang = Math.max(anfang, stueck.bis)
  }
  if (anfang < fenster.bis) raus.push({ von: anfang, bis: fenster.bis })
  return raus
}

/* ---------------------------------------------------------------------------
 * Zielband
 * ------------------------------------------------------------------------- */

export type Ziele = {
  targetMin?: number | null
  targetMax?: number | null
  targetDayMin?: number | null
  targetDayMax?: number | null
  targetNightMin?: number | null
  targetNightMax?: number | null
}

export type Band = { min: number | null; max: number | null }

function band(min: number | null | undefined, max: number | null | undefined): Band | null {
  const lo = min ?? null
  const hi = max ?? null
  return lo == null && hi == null ? null : { min: lo, max: hi }
}

function gleich(a: Band | null, b: Band | null): boolean {
  return a != null && b != null && a.min === b.min && a.max === b.max
}

/**
 * Welches Zielband gilt in dieser Phase?
 *
 * Kennt die Kachel Tag- und Nachtbänder, gilt je Phase das eigene (fehlt es,
 * keins). Nur ohne Tag/Nacht gilt `targetMin`/`targetMax` — sonst NICHT als
 * Ersatz: der Server legt dort das Band der GERADE gültigen Phase ab
 * (`KachelZiele.ZieleSetzen`), kein Ganztagsband.
 */
export function zielbandFuer(ziele: Ziele | null | undefined, phase: 'tag' | 'nacht'): Band | null {
  if (!ziele) return null
  const tag = band(ziele.targetDayMin, ziele.targetDayMax)
  const nacht = band(ziele.targetNightMin, ziele.targetNightMax)
  if (tag || nacht) return phase === 'tag' ? tag : nacht
  return band(ziele.targetMin, ziele.targetMax)
}

/**
 * Unterscheiden sich Tag- und Nachtziel? Dann braucht das Band den Lichtplan;
 * ohne ihn wäre jede Zuordnung geraten.
 */
export function zielBrauchtLichtplan(ziele: Ziele | null | undefined): boolean {
  if (!ziele) return false
  const tag = band(ziele.targetDayMin, ziele.targetDayMax)
  const nacht = band(ziele.targetNightMin, ziele.targetNightMax)
  return tag != null && nacht != null && !gleich(tag, nacht)
}

export type BandStueck = Fenster & Band

/**
 * Das Zielband als Stücke über die Zeit: mit Lichtplan je Licht- und
 * Dunkelphase das passende. Ohne Lichtplan nur, wenn Tag und Nacht dasselbe
 * Ziel haben (oder es nur eins gibt) — dann ganztags; unterscheiden sie sich,
 * gibt es KEIN Band. Lieber nichts als ein Band zur falschen Stunde.
 */
export function zielbandStuecke(fenster: Fenster, ziele: Ziele | null | undefined, licht: Fenster[] | null): BandStueck[] {
  if (!ziele) return []
  const tagBand = zielbandFuer(ziele, 'tag')
  const nachtBand = zielbandFuer(ziele, 'nacht')
  if (!licht) {
    if (zielBrauchtLichtplan(ziele)) return []
    const eins = tagBand ?? nachtBand
    return eins ? [{ ...fenster, ...eins }] : []
  }
  const raus: BandStueck[] = []
  let anfang = fenster.von
  const schiebe = (von: number, bis: number, b: Band | null) => { if (b && bis > von) raus.push({ von, bis, ...b }) }
  for (const stueck of licht) {
    schiebe(anfang, stueck.von, nachtBand)
    schiebe(stueck.von, stueck.bis, tagBand)
    anfang = Math.max(anfang, stueck.bis)
  }
  schiebe(anfang, fenster.bis, nachtBand)
  return raus
}

/**
 * Was die große Zahl im Fokus über das Ziel sagt: „im Ziel (18–24 °C)",
 * „über dem Ziel (bis 50 %)", „unter dem Ziel (ab 18 °C)", „Soll 23 °C".
 * Null ohne Wert oder ohne Band.
 */
export function zielUrteil(wert: number | null, gilt: Band | null, stellen: number, einheit: string | null): { text: string; imZiel: boolean } | null {
  if (wert == null || !gilt) return null
  const e = einheit ? ` ${einheit}` : ''
  if (gilt.min != null && gilt.min === gilt.max) return { text: `Soll ${grenzText(gilt.min, stellen)}${e}`, imZiel: false }
  const bereich = gilt.min != null && gilt.max != null ? `${grenzText(gilt.min, stellen)}–${grenzText(gilt.max, stellen)}`
    : gilt.min != null ? `ab ${grenzText(gilt.min, stellen)}`
      : `bis ${grenzText(gilt.max as number, stellen)}`
  if (gilt.max != null && wert > gilt.max) return { text: `über dem Ziel (${bereich}${e})`, imZiel: false }
  if (gilt.min != null && wert < gilt.min) return { text: `unter dem Ziel (${bereich}${e})`, imZiel: false }
  return { text: `im Ziel (${bereich}${e})`, imZiel: true }
}

/* ---------------------------------------------------------------------------
 * Punkte: Lücken, Zeiger, Ausschnitt, Statistik, Quelle
 * ------------------------------------------------------------------------- */

function median(werte: number[]): number {
  const s = [...werte].sort((a, b) => a - b)
  const mitte = Math.floor(s.length / 2)
  return s.length % 2 ? s[mitte] : (s[mitte - 1] + s[mitte]) / 2
}

/**
 * Wie weit darf der Abstand nach Punkt `i` sein, ehe er eine Lücke ist?
 *
 * Drei Takte — und der Takt ist der ÖRTLICHE: der Median der bis zu vier
 * Abstände links und rechts. Ein Takt über alles ginge schief, sobald zwei
 * Auflösungen in einer Reihe stehen (der Testbestand hat zwei Tage im
 * Viertelstundentakt und davor stündliche Werte): jede Stunde wäre dann eine
 * Lücke.
 */
export function lueckenGrenze(punkte: readonly Punkt[], i: number): number {
  const nachbarn: number[] = []
  for (let j = Math.max(0, i - 4); j <= Math.min(punkte.length - 2, i + 4); j++) {
    if (j !== i) nachbarn.push(punkte[j + 1].t - punkte[j].t)
  }
  return nachbarn.length === 0 ? Infinity : 3 * median(nachbarn)
}

/** Die Linie in zusammenhängende Stücke geteilt — eine Lücke wird nicht überbrückt. */
export function abschnitte(punkte: readonly Punkt[]): Punkt[][] {
  const raus: Punkt[][] = []
  let aktuell: Punkt[] = []
  for (let i = 0; i < punkte.length; i++) {
    aktuell.push(punkte[i])
    if (i < punkte.length - 1 && punkte[i + 1].t - punkte[i].t > lueckenGrenze(punkte, i)) {
      raus.push(aktuell)
      aktuell = []
    }
  }
  if (aktuell.length) raus.push(aktuell)
  return raus
}

function naechsterIndex(punkte: readonly Punkt[], t: number): number {
  let lo = 0
  let hi = punkte.length - 1
  while (hi - lo > 1) {
    const mitte = (lo + hi) >> 1
    if (punkte[mitte].t < t) lo = mitte
    else hi = mitte
  }
  return Math.abs(punkte[lo].t - t) <= Math.abs(punkte[hi].t - t) ? lo : hi
}

/**
 * Der Punkt unter dem Zeiger — oder null, wenn der Zeiger in einer Lücke
 * steht. Eine Zahl von vor zwei Stunden unter einer Uhrzeit, zu der der Sensor
 * schwieg, wäre eine falsche Auskunft.
 */
export function amZeiger(punkte: readonly Punkt[], t: number): Punkt | null {
  if (punkte.length === 0) return null
  const i = naechsterIndex(punkte, t)
  const erlaubt = punkte.length < 2 ? Infinity : lueckenGrenze(punkte, Math.min(i, punkte.length - 2)) / 2
  return Math.abs(punkte[i].t - t) <= erlaubt ? punkte[i] : null
}

/** Der letzte Punkt einer Reihe, oder null. */
export function letzterPunkt(punkte: readonly Punkt[]): Punkt | null {
  return punkte.length ? punkte[punkte.length - 1] : null
}

/**
 * Die Punkte im Ausschnitt, dazu je ein Nachbar links und rechts — so reicht
 * die Linie bis an den Rand, statt einen Takt davor aufzuhören.
 */
export function imFenster(punkte: readonly Punkt[], fenster: Fenster): Punkt[] {
  let erster = punkte.findIndex((p) => p.t >= fenster.von)
  if (erster < 0) return punkte.length ? [punkte[punkte.length - 1]] : []
  erster = Math.max(0, erster - 1)
  let letzter = erster
  while (letzter < punkte.length - 1 && punkte[letzter].t <= fenster.bis) letzter++
  return punkte.slice(erster, letzter + 1)
}

/** Max / Min / Ø im Ausschnitt (streng innerhalb), oder null ohne Werte. */
export function statistik(punkte: readonly Punkt[], fenster: Fenster): { max: number; min: number; mittel: number } | null {
  let max = -Infinity
  let min = Infinity
  let summe = 0
  let anzahl = 0
  for (const p of punkte) {
    if (p.t < fenster.von || p.t > fenster.bis || !Number.isFinite(p.v)) continue
    if (p.v > max) max = p.v
    if (p.v < min) min = p.v
    summe += p.v
    anzahl++
  }
  return anzahl === 0 ? null : { max, min, mittel: summe / anzahl }
}

/**
 * Woher die Punkte kommen: aus den schon geladenen 24 h (`useTentSparklines`)
 * oder aus den 7 Tagen, die die Kachel bei Bedarf nachlädt.
 *
 * `tagVon` ist der älteste Punkt der 24-h-Daten. Fehlen sie, braucht es die 7 Tage.
 */
export function quelleWaehlen(fenster: Fenster, tagVon: number | null): '24h' | '7t' {
  // Eine halbe Stunde Spiel: die 24 h beginnen beim ersten Punkt NACH „jetzt
  // minus 24 h", das Fenster „24 Std" eine Spur davor. Ohne das Spiel lüde
  // schon die Grundansicht jedes Mal die ganze Woche nach.
  return tagVon != null && fenster.von >= tagVon - QUELLE_SPIEL ? '24h' : '7t'
}

/** Wie weit ein Fenster vor den 24-h-Daten beginnen darf, ehe es die 7 Tage braucht. */
export const QUELLE_SPIEL = 30 * MINUTE

/**
 * Die 7 Tage und die frischeren 24 h zusammen: die 7 Tage werden einmal
 * geladen und altern, die 24 h kommen alle fünf Minuten neu. Wo beide etwas
 * haben, gilt also die 24-h-Fassung.
 */
export function zusammenfuehren(woche: readonly Punkt[] | undefined, tag: readonly Punkt[] | undefined): Punkt[] {
  const frisch = tag ?? []
  if (!woche?.length) return [...frisch]
  if (!frisch.length) return [...woche]
  const ab = frisch[0].t
  return [...woche.filter((p) => p.t < ab), ...frisch]
}

/**
 * Die Spanne der Daten, die gerade da sind — vom ältesten bis zum neuesten Punkt.
 *
 * Solange nur die 24 h geladen sind, sind das die 24 h: Leiste und Ausschnitt
 * zeigen keinen leeren Vorlauf, der wie ein Fehler aussieht. Wer weiter
 * zurück will (◀ am Rand, „7 Tage", Ziehen an den linken Rand), löst das
 * Nachladen aus, und die Spanne wächst auf sieben Tage.
 */
export function datenGrenzen(reihen: ReadonlyArray<readonly Punkt[]>): Fenster | null {
  let von = Infinity
  let bis = -Infinity
  for (const reihe of reihen) {
    if (!reihe.length) continue
    von = Math.min(von, reihe[0].t)
    bis = Math.max(bis, reihe[reihe.length - 1].t)
  }
  if (!Number.isFinite(bis)) return null
  return { von, bis }
}

/** So weit zurück reichen die Rohwerte höchstens — die Grenze für Blättern vor dem Nachladen. */
export function wochenGrenzen(grenzen: Fenster): Fenster {
  return { von: Math.min(grenzen.von, grenzen.bis - MAX_BREITE), bis: grenzen.bis }
}

/**
 * Die Abschnitte (siehe `abschnitte`) auf den Ausschnitt beschnitten, je mit
 * einem Nachbarn links und rechts. Die Abschnitte werden einmal je Datenstand
 * gerechnet — der Median in `lueckenGrenze` lief sonst bei jedem Zeichnen über
 * alle Punkte aller Kurven.
 */
export function abschnitteImFenster(stuecke: readonly Punkt[][], fenster: Fenster): Punkt[][] {
  const raus: Punkt[][] = []
  for (const stueck of stuecke) {
    if (!stueck.length || stueck[stueck.length - 1].t < fenster.von || stueck[0].t > fenster.bis) continue
    const teil = imFenster(stueck, fenster)
    if (teil.length) raus.push(teil)
  }
  return raus
}

/**
 * Eine Werteskala in Pixel: `oben` und `unten` sind die Pixelränder, die Werte
 * füllen sie bis auf einen kleinen Rand. Eine flache Reihe steht in der Mitte.
 */
export function skala(min: number, max: number, oben: number, unten: number, rand = 6): (v: number) => number {
  const hub = max - min
  if (!(hub > 1e-9)) return () => (oben + unten) / 2
  return (v) => oben + rand + (1 - (v - min) / hub) * (unten - oben - 2 * rand)
}

/** Ein SVG-Pfad aus Abschnitten — jedes Stück beginnt mit M, Lücken bleiben offen. */
export function pfad(stuecke: readonly Punkt[][], x: (t: number) => number, y: (v: number) => number): string {
  return stuecke
    .map((stueck) => stueck.map((p, i) => `${i ? 'L' : 'M'}${x(p.t).toFixed(1)} ${y(p.v).toFixed(1)}`).join(' '))
    .join(' ')
}

/**
 * Der Speicherplatz der Auswahl — mit einem Fingerabdruck der Kachel-Werte.
 *
 * Ändert jemand im Anpassen-Modus, welche Werte die Kachel zeigt, gilt deren
 * neue Auswahl. Ohne den Fingerabdruck hätte eine einmal gemerkte Auswahl
 * jede spätere Änderung der Kachel für immer überstimmt.
 */
export const speicherSchluessel = (tileId: string, metricKeys: readonly string[]) =>
  `growos.verlauf.${tileId}.${[...metricKeys].sort().join(',')}`
