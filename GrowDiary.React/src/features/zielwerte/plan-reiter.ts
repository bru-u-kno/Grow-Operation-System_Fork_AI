import { istLeer, zahlOderNull } from '../../zahlenfeld'
import { alsText, type Aenderung, type WochenwertSpalte } from '../wochenplan/wochenwerte-bearbeiten'

/**
 * Fork AI (Grow-Plan, Schritt 3b): die Rechenseite des Reiters „Plan“ —
 * Dosierung einer Woche, Speicheranfragen und Texte fürs Änderungsbuch.
 * Die Zielwerte rechnet weiter `wochenwerte-bearbeiten.ts`.
 */

export type Zutat = { component: string; minMlPerLiter: number; maxMlPerLiter: number }

export type PlanSpalte = { id: string; label: string; stage: string; week: number | null; items: Zutat[] }

export type PlanStand = {
  growId: number
  stand: string
  programmId: string
  programmName: string
  eigenesProgrammId: string | null
  eigenesProgrammName: string | null
  vermerk: string | null
  angelegtUtc: string
  chart: { columns: PlanSpalte[] }
}

export type BuchEintrag = {
  id: number
  zeitUtc: string
  art: string
  spalteId: string | null
  feld: string | null
  alt: string | null
  neu: string | null
  ziel: string | null
  grund: string | null
}

export type DosisZeile = { name: string; ml: string; entfernt: boolean; neu: boolean }

/** Spalten-Id → Zeilen; nur Wochen, an denen etwas angefasst wurde. */
export type DosisEntwurf = Record<string, DosisZeile[]>

export type PlanAnfrage = {
  spalteId: string
  werte: Array<{ feld: string; wert: number | null }>
  dosierung: Array<{ komponente: string; mlProLiter: number }> | null
  auchInsProgramm: boolean
  programmName: string | null
  grund: string | null
}

const nahe = (a: number, b: number) => Math.abs(a - b) < 1e-9

export function zeilenAus(items: readonly Zutat[]): DosisZeile[] {
  return items.map((i) => ({ name: i.component, ml: alsText(i.minMlPerLiter), entfernt: false, neu: false }))
}

/** Die Zeilen, die nach dem Speichern gelten würden. */
export function aktiveZeilen(zeilen: readonly DosisZeile[]): DosisZeile[] {
  return zeilen.filter((z) => !z.entfernt && !(z.neu && istLeer(z.name) && istLeer(z.ml)))
}

export function dosisGeaendert(items: readonly Zutat[], zeilen: readonly DosisZeile[]): boolean {
  const aktiv = aktiveZeilen(zeilen)
  if (aktiv.length !== items.length) return true
  return aktiv.some((z, i) => {
    const ml = zahlOderNull(z.ml)
    return z.name.trim() !== items[i].component || ml == null || !nahe(ml, items[i].minMlPerLiter)
  })
}

export function dosisFehler(label: string, zeilen: readonly DosisZeile[]): string[] {
  const saetze: string[] = []
  const namen = new Set<string>()
  for (const z of aktiveZeilen(zeilen)) {
    const name = z.name.trim()
    if (name === '') {
      saetze.push(`${label}: eine Zutat hat keinen Namen.`)
      continue
    }
    const klein = name.toLocaleLowerCase('de-DE')
    if (namen.has(klein)) saetze.push(`${label}: „${name}“ steht zweimal in der Dosierung.`)
    namen.add(klein)
    const ml = zahlOderNull(z.ml)
    if (ml == null || ml < 0 || ml > 50) saetze.push(`${label}: ${name} braucht eine Menge zwischen 0 und 50 ml/l.`)
  }
  return saetze
}

/** Menge für das Anlagenvolumen, gerundet auf ganze ml — oder null ohne Volumen. */
export function mengeFuerVolumen(ml: string, volumenLiter: number | null): string | null {
  const wert = zahlOderNull(ml)
  if (wert == null || volumenLiter == null || volumenLiter <= 0) return null
  return `${Math.round(wert * volumenLiter).toLocaleString('de-DE')} ml`
}

/** Je geänderter Woche eine Anfrage — Werte und Dosierung zusammen. */
export function anfragen(
  werte: readonly Aenderung[],
  dosis: DosisEntwurf,
  spalten: readonly PlanSpalte[],
  wahl: { auchInsProgramm: boolean; programmName: string; grund: string },
): PlanAnfrage[] {
  const ids = new Set<string>(werte.map((w) => w.spalteId))
  for (const s of spalten) {
    const zeilen = dosis[s.id]
    if (zeilen && dosisGeaendert(s.items, zeilen)) ids.add(s.id)
  }
  return spalten
    .filter((s) => ids.has(s.id))
    .map((s) => {
      const zeilen = dosis[s.id]
      return {
        spalteId: s.id,
        werte: werte.filter((w) => w.spalteId === s.id).map((w) => ({ feld: w.feld, wert: w.wert })),
        dosierung: zeilen && dosisGeaendert(s.items, zeilen)
          ? aktiveZeilen(zeilen).map((z) => ({ komponente: z.name.trim(), mlProLiter: zahlOderNull(z.ml) ?? 0 }))
          : null,
        auchInsProgramm: wahl.auchInsProgramm,
        programmName: wahl.auchInsProgramm && wahl.programmName.trim() !== '' ? wahl.programmName.trim() : null,
        grund: wahl.grund.trim() === '' ? null : wahl.grund.trim(),
      }
    })
}

/** Lesbare Zeilen für das Speichern-Blatt. */
export function aenderungsZeilen(
  werte: readonly Aenderung[],
  dosis: DosisEntwurf,
  spalten: readonly PlanSpalte[],
  wochen: readonly WochenwertSpalte[],
): Array<{ woche: string; text: string }> {
  const zeilen: Array<{ woche: string; text: string }> = []
  for (const w of werte) {
    const woche = wochen.find((s) => s.id === w.spalteId)
    const feld = woche?.felder.find((f) => f.feld === w.feld)
    const alt = alsText(feld?.wert) || '–'
    const neu = w.wert == null ? `${alsText(feld?.plan) || '–'} (Start)` : alsText(w.wert)
    zeilen.push({ woche: woche?.label ?? w.spalteId, text: `${feld?.bezeichnung ?? w.feld} ${alt} → ${neu}` })
  }
  for (const s of spalten) {
    const entwurf = dosis[s.id]
    if (!entwurf || !dosisGeaendert(s.items, entwurf)) continue
    const vorher = new Map(s.items.map((i) => [i.component.toLocaleLowerCase('de-DE'), i.minMlPerLiter]))
    const aktiv = aktiveZeilen(entwurf)
    const nachher = new Set(aktiv.map((z) => z.name.trim().toLocaleLowerCase('de-DE')))
    for (const i of s.items) {
      if (!nachher.has(i.component.toLocaleLowerCase('de-DE'))) {
        zeilen.push({ woche: s.label, text: `${i.component} ${alsText(i.minMlPerLiter)} → entfernt` })
      }
    }
    for (const z of aktiv) {
      const alt = vorher.get(z.name.trim().toLocaleLowerCase('de-DE'))
      const ml = zahlOderNull(z.ml)
      if (alt == null) zeilen.push({ woche: s.label, text: `${z.name.trim()} neu · ${alsText(ml)} ml/l` })
      else if (ml != null && !nahe(alt, ml)) zeilen.push({ woche: s.label, text: `${z.name.trim()} ${alsText(alt)} → ${alsText(ml)} ml/l` })
    }
  }
  return zeilen
}

/** Ein Eintrag des Änderungsbuchs als Satz. */
export function buchText(e: BuchEintrag, feldName: (feld: string) => string): string {
  const zahl = (t: string | null) => (t == null ? null : alsText(Number(t)))
  switch (e.art) {
    case 'angelegt':
      return `Plan angelegt · ${e.neu ?? ''}`.trim()
    case 'wert':
      return `${feldName(e.feld ?? '')} ${zahl(e.alt) ?? '–'} → ${zahl(e.neu) ?? '–'}`
    case 'dosierung':
      if (e.alt == null) return `${e.feld} neu · ${zahl(e.neu)} ml/l`
      if (e.neu == null) return `${e.feld} ${zahl(e.alt)} → entfernt`
      return `${e.feld} ${zahl(e.alt)} → ${zahl(e.neu)} ml/l`
    case 'eingefroren':
      return `Plan eingefroren${e.neu ? ` · ${e.neu}` : ''}`
    case 'wiedergeoeffnet':
      return 'Grow wieder geöffnet — Plan wieder bearbeitbar'
    case 'programmwechsel':
      return `Programmwechsel ${e.alt ?? ''} → ${e.neu ?? ''}`
    case 'alsprogramm':
      return `Als Programm gespeichert · ${e.neu ?? ''}`
    default:
      return e.art
  }
}
