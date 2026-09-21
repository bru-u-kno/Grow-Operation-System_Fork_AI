import { istLeer, zahlOderNull } from '../../zahlenfeld'

/**
 * Fork AI (F-004): Wochenwerte bearbeiten — die Rechnung ohne Oberfläche.
 *
 * Der Entwurf hält den **getippten Text** je Feld, nicht die Zahl: „1," ist
 * beim Tippen ein Zwischenstand und darf nicht zu 1 werden, solange der
 * Nutzer noch schreibt.
 */

export type WochenwertFeld = {
  feld: string
  bezeichnung: string
  einheit: string
  min: number
  max: number
  schritt: number
  wert: number | null
  plan: number | null
  geaendert: boolean
}

export type WochenwertSpalte = {
  id: string
  label: string
  stage: string
  woche: number | null
  istJetzt: boolean
  felder: WochenwertFeld[]
}

export type Wochenwerte = {
  growId: number
  programmId: string
  programmName: string
  andereGrows: number
  spalten: WochenwertSpalte[]
}

export type Aenderung = { spalteId: string; feld: string; wert: number | null }

/** Schlüssel eines Feldes im Entwurf. */
export type Entwurf = Record<string, string>

export const schluessel = (spalteId: string, feld: string) => `${spalteId}|${feld}`

/**
 * Wie die Felder auf dem Display zusammenstehen. Spannen (von/bis) teilen sich
 * eine Zelle — so bleiben es neun Werte, wie im freigegebenen Mockup.
 *
 * Fork AI (forkai.130): `nacht` markiert Gruppen, die bei „Nachts gelten die
 * Tageswerte" ruhen — sie zeigen dann nur den Tageswert (`tag`).
 */
export const GRUPPEN: ReadonlyArray<{ titel: string; felder: readonly string[]; nacht?: { tag: string } }> = [
  { titel: 'EC', felder: ['ecTarget'] },
  { titel: 'EC-Band', felder: ['ecMin', 'ecMax'] },
  { titel: 'pH', felder: ['phMin', 'phMax'] },
  { titel: 'Wasser Tag', felder: ['waterTempDayC'] },
  { titel: 'Wasser Nacht', felder: ['waterTempNightC'] },
  { titel: 'RH max', felder: ['rhMax'] },
  { titel: 'RH max Nacht', felder: ['rhMaxNight'], nacht: { tag: 'rhMax' } },
  { titel: 'Luft', felder: ['airTempC'] },
  { titel: 'Luft Nacht', felder: ['airTempNightC'], nacht: { tag: 'airTempC' } },
  { titel: 'VPD', felder: ['vpdMin', 'vpdMax'] },
  { titel: 'CO₂', felder: ['co2Min', 'co2Max'] },
  { titel: 'PPFD', felder: ['ppfdMin', 'ppfdMax'] },
  { titel: 'ORP', felder: ['orpMin', 'orpMax'] },
]

/** Spannen, deren „von" nicht über dem „bis" liegen darf. */
const PAARE: ReadonlyArray<readonly [string, string]> = [
  ['ecMin', 'ecMax'],
  ['orpMin', 'orpMax'],
  ['phMin', 'phMax'],
  ['vpdMin', 'vpdMax'],
  ['co2Min', 'co2Max'],
  ['ppfdMin', 'ppfdMax'],
]

const nahe = (a: number, b: number) => Math.abs(a - b) < 1e-9

/** Zahl als deutscher Text, ohne Tausenderpunkt — so, wie man sie tippt. */
export function alsText(wert: number | null | undefined): string {
  if (wert == null) return ''
  return wert.toLocaleString('de-DE', { maximumFractionDigits: 2, useGrouping: false })
}

/** Der Text, der gerade im Feld steht: Entwurf, sonst der gespeicherte Wert. */
export function feldText(entwurf: Entwurf, spalteId: string, feld: WochenwertFeld): string {
  return entwurf[schluessel(spalteId, feld.feld)] ?? alsText(feld.wert)
}

/** Welcher Wert nach dem Speichern gälte — leer heißt: der Plan. */
export function wirksamerWert(entwurf: Entwurf, spalteId: string, feld: WochenwertFeld): number | null {
  const text = entwurf[schluessel(spalteId, feld.feld)]
  if (text === undefined) return feld.wert
  if (istLeer(text)) return feld.plan
  return zahlOderNull(text)
}

/** Weicht das Feld nach dem Speichern vom Plan ab? */
export function weichtAb(entwurf: Entwurf, spalteId: string, feld: WochenwertFeld): boolean {
  const wert = wirksamerWert(entwurf, spalteId, feld)
  if (wert == null || feld.plan == null) return wert !== feld.plan
  return !nahe(wert, feld.plan)
}

/**
 * Die Änderungen, die tatsächlich abgeschickt werden.
 *
 * Ein Entwurf, der dem gespeicherten Stand entspricht, ist keine Änderung —
 * sonst zählt der Speicherbalken Felder, die man nur angetippt hat. Ein Wert
 * gleich dem Plan geht als `null` raus: das löscht die Abweichung, statt eine
 * Kopie des Planwerts abzulegen.
 */
export function aenderungen(werte: Wochenwerte, entwurf: Entwurf): Aenderung[] {
  const liste: Aenderung[] = []
  for (const spalte of werte.spalten) {
    for (const feld of spalte.felder) {
      const text = entwurf[schluessel(spalte.id, feld.feld)]
      if (text === undefined) continue
      const neu = wirksamerWert(entwurf, spalte.id, feld)
      if (neu === null && !istLeer(text)) continue // unlesbar — meldet `fehler`
      const gleichGespeichert = neu == null ? feld.wert == null : feld.wert != null && nahe(neu, feld.wert)
      if (gleichGespeichert) continue
      const gleichPlan = neu == null || (feld.plan != null && nahe(neu, feld.plan))
      liste.push({ spalteId: spalte.id, feld: feld.feld, wert: gleichPlan ? null : neu })
    }
  }
  return liste
}

/** Sätze für alles, was so nicht gespeichert werden kann — leer heißt: in Ordnung. */
export function fehler(werte: Wochenwerte, entwurf: Entwurf): string[] {
  const saetze: string[] = []
  for (const spalte of werte.spalten) {
    const nachName = new Map(spalte.felder.map((f) => [f.feld, f]))
    for (const feld of spalte.felder) {
      const text = entwurf[schluessel(spalte.id, feld.feld)]
      if (text === undefined || istLeer(text)) continue
      const zahl = zahlOderNull(text)
      if (zahl === null) {
        saetze.push(`${spalte.label}: „${text}" ist bei ${feld.bezeichnung} keine Zahl.`)
      } else if (zahl < feld.min || zahl > feld.max) {
        saetze.push(`${spalte.label}: ${feld.bezeichnung} muss zwischen ${alsText(feld.min)} und ${alsText(feld.max)} liegen.`)
      }
    }
    for (const [von, bis] of PAARE) {
      const a = nachName.get(von)
      const b = nachName.get(bis)
      if (!a || !b) continue
      const va = wirksamerWert(entwurf, spalte.id, a)
      const vb = wirksamerWert(entwurf, spalte.id, b)
      if (va != null && vb != null && va > vb + 1e-9) {
        saetze.push(`${spalte.label}: ${a.bezeichnung} (${alsText(va)}) liegt über ${b.bezeichnung} (${alsText(vb)}).`)
      }
    }
  }
  return saetze
}

/** Zustand einer Woche für die Punktreihe. */
export function punkt(werte: Wochenwerte, entwurf: Entwurf, spalte: WochenwertSpalte) {
  const offen = aenderungen(werte, entwurf).some((a) => a.spalteId === spalte.id)
  const eigen = spalte.felder.some((f) => weichtAb(entwurf, spalte.id, f))
  return { offen, eigen }
}

/** Mit welcher Woche die Seite aufgeht: der laufenden, sonst der ersten. */
export function startIndex(werte: Wochenwerte): number {
  const i = werte.spalten.findIndex((s) => s.istJetzt)
  return i < 0 ? 0 : i
}

/** Entwurf, der alle Felder einer Gruppe auf den Plan zurücksetzt. */
export function aufPlan(entwurf: Entwurf, spalte: WochenwertSpalte, felder: readonly string[]): Entwurf {
  const neu = { ...entwurf }
  for (const name of felder) {
    const feld = spalte.felder.find((f) => f.feld === name)
    if (feld) neu[schluessel(spalte.id, name)] = alsText(feld.plan)
  }
  return neu
}
