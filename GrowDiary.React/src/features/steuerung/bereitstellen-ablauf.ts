import type { Bestandsaufnahme } from './steuerung-typen'

/**
 * Fork AI (A-016, Etappe 3): Der Ablauf „Bereitstellen" — ohne React.
 *
 * Die Endpunkte gibt es je Modul seit forkai.45 (`/helfer`, `/rechenwerte`, `/automationen`); der Assistent
 * fährt sie nur in der richtigen Reihenfolge für alle gewählten Steuerungen. Einrichtung in Phasen:
 * <b>erst Helfer, dann Rechenwerte</b> (sie lesen die Helfer), <b>danach Vorschau der Automationen und erst nach
 * Zustimmung das Anlegen</b> (an ihrem Ende hängt ein Ventil oder ein Kompressor).
 *
 * Ein Fehler bei einem Modul hält die anderen nicht auf — er steht im Ergebnis, nicht in einer Ausnahme.
 */

/** Das Zusatzgerät des Entfeuchters gehört nur dazu, wenn der Nutzer es gesagt hat. */
const ZUSATZ = 'entfeuchter-zusatz'

/** Module, deren Geräte zugeordnet werden (auch die Lampe) — ohne das Zusatzgerät, wenn es keins gibt. */
export function rollenModule(gewaehlt: Array<{ rollenmodule: string[] }>, zusatz: boolean): string[] {
  return gewaehlt.flatMap((e) => e.rollenmodule).filter((m) => zusatz || m !== ZUSATZ)
}

/** Module, für die Bausteine in Home Assistant angelegt werden, in Anlege-Reihenfolge. */
export function bausteinModule(gewaehlt: Array<{ module: string[] }>, zusatz: boolean): string[] {
  return gewaehlt.flatMap((e) => e.module).filter((m) => zusatz || m !== ZUSATZ)
}

export type Aufruf = <T>(pfad: string, optionen?: { method?: string }) => Promise<T>

const HELFER = new Set(['Zahl', 'Schalter', 'Zeitpunkt', 'Zaehler'])
const RECHENWERTE = new Set(['RechenSensor', 'RechenSchalter', 'Mittelwert'])

export type Fehlend = { helfer: number; rechenwerte: number; automationen: number }

/** Was in Home Assistant noch fehlt, nach Art — nur Bauteile, die dort wirklich fehlen (nicht „entfällt", nicht „veraltet"). */
export function zaehleFehlend(bestand: Bestandsaufnahme | null | undefined): Fehlend {
  const z: Fehlend = { helfer: 0, rechenwerte: 0, automationen: 0 }
  for (const b of bestand?.bauteile ?? []) {
    if (b.stand !== 'Fehlt') continue
    if (HELFER.has(b.art)) z.helfer += 1
    else if (RECHENWERTE.has(b.art)) z.rechenwerte += 1
    else if (b.art === 'Automation') z.automationen += 1
  }
  return z
}

export function summe(liste: Fehlend[]): Fehlend {
  return liste.reduce((a, b) => ({
    helfer: a.helfer + b.helfer, rechenwerte: a.rechenwerte + b.rechenwerte, automationen: a.automationen + b.automationen,
  }), { helfer: 0, rechenwerte: 0, automationen: 0 })
}

/** Ein Satz: „13 Helfer · 3 Rechenwerte · 1 Automation" — leer, wenn nichts fehlt. */
export function fehlendText(f: Fehlend): string {
  const teile = [
    f.helfer > 0 ? `${f.helfer} ${f.helfer === 1 ? 'Helfer' : 'Helfer'}` : null,
    f.rechenwerte > 0 ? `${f.rechenwerte} ${f.rechenwerte === 1 ? 'Rechenwert' : 'Rechenwerte'}` : null,
    f.automationen > 0 ? `${f.automationen} ${f.automationen === 1 ? 'Automation' : 'Automationen'}` : null,
  ]
  return teile.filter(Boolean).join(' · ')
}

export type Phase1 = { modul: string; helfer: number; rechenwerte: number; nicht: number; fehler: string | null }

type Bilanz = { angelegt: number; fehlgeschlagen: number }

/**
 * Helfer und Rechenwerte aller Module anlegen — in der übergebenen Reihenfolge, je Modul erst Helfer, dann Rechenwerte.
 * Module, bei denen davon nichts fehlt, werden nicht angefragt.
 */
export async function helferUndRechenwerte(
  module: string[],
  bestaende: Record<string, Bestandsaufnahme | null>,
  aufruf: Aufruf,
): Promise<Phase1[]> {
  const ergebnis: Phase1[] = []
  for (const modul of module) {
    const fehlt = zaehleFehlend(bestaende[modul])
    const r: Phase1 = { modul, helfer: 0, rechenwerte: 0, nicht: 0, fehler: null }
    try {
      if (fehlt.helfer > 0) {
        const b = await aufruf<Bilanz>(`/api/steuerung/${modul}/helfer`, { method: 'POST' })
        r.helfer = b.angelegt
        r.nicht += b.fehlgeschlagen
      }
      if (fehlt.rechenwerte > 0) {
        const b = await aufruf<Bilanz>(`/api/steuerung/${modul}/rechenwerte`, { method: 'POST' })
        r.rechenwerte = b.angelegt
        r.nicht += b.fehlgeschlagen
      }
    } catch (caught) {
      r.fehler = caught instanceof Error ? caught.message : String(caught)
    }
    ergebnis.push(r)
  }
  return ergebnis
}

export type AutoEintrag = { kennung: string; name: string; titel: string | null; stand: string; hinweis: string | null }
export type AutoBilanz = { angelegt: number; fremd: number; fehlgeschlagen: number; einzeln: AutoEintrag[] }
export type Phase2 = { modul: string; bilanz: AutoBilanz | null; fehler: string | null }

/** Automationen aller Module — `vorschau=true` schreibt nichts und sagt nur, was geschähe. */
export async function automationen(module: string[], vorschau: boolean, aufruf: Aufruf): Promise<Phase2[]> {
  const ergebnis: Phase2[] = []
  for (const modul of module) {
    try {
      const bilanz = await aufruf<AutoBilanz>(`/api/steuerung/${modul}/automationen${vorschau ? '?vorschau=true' : ''}`, { method: 'POST' })
      ergebnis.push({ modul, bilanz, fehler: null })
    } catch (caught) {
      ergebnis.push({ modul, bilanz: null, fehler: caught instanceof Error ? caught.message : String(caught) })
    }
  }
  return ergebnis
}

/** Wie viele Automationen die Vorschau tatsächlich schreiben würde (angelegt oder erneuert). */
export function wuerdeSchreiben(vorschau: Phase2[]): number {
  return vorschau.reduce((n, p) => n + (p.bilanz?.einzeln.filter((e) => e.stand === 'Angelegt' || e.stand === 'Erneuert').length ?? 0), 0)
}

export type Reife = 'ohne-bausteine' | 'unvollstaendig' | 'bereit' | 'anlegen' | 'ha-stumm'

/**
 * Wie weit eine Steuerung ist:
 * <ul>
 * <li>`unvollstaendig` — es fehlen Pflichtgeräte; bereitgestellt wird dann nichts (erst zuordnen)</li>
 * <li>`ohne-bausteine` — die Lampe: Zuordnung genügt</li>
 * <li>`ha-stumm` — Home Assistant antwortet nicht, es lässt sich nicht sagen, was fehlt</li>
 * <li>`bereit` — nichts fehlt; `anlegen` — es fehlen Bausteine</li>
 * </ul>
 */
export function reife(
  eintrag: { pflichtZugeordnet: number; pflichtGesamt: number; module: string[] },
  bestaende: Record<string, Bestandsaufnahme | null>,
): Reife {
  if (eintrag.pflichtZugeordnet < eintrag.pflichtGesamt) return 'unvollstaendig'
  if (eintrag.module.length === 0) return 'ohne-bausteine'
  const stande = eintrag.module.map((m) => bestaende[m])
  if (stande.some((b) => !b || !b.haErreichbar)) return 'ha-stumm'
  return stande.some((b) => (b?.fehlt ?? 0) > 0) ? 'anlegen' : 'bereit'
}
