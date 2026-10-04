import { ApiRequestError } from '../../api'
import { KI_STUFEN, kiStufeName } from '../../deutsche-woerter'
import type { KiStufe } from '../../types'
import { feldText, istLeer, unlesbarMeldung, unlesbareFelder, zahlOderNull } from '../../zahlenfeld'

/**
 * Zugriff für KI-Assistenten (A-003) — alles, was die Oberfläche entscheidet,
 * ohne React. Steht hier, damit es ohne Browser prüfbar ist
 * (`ki-zugriff.test.tsx`): welcher Zustand eine Stufe hat, ob eine Warnung
 * offen ist, was aus einem getippten Höchstwert wird.
 */

/**
 * Stufen, die nur nach einem bestätigten Warnhinweis freigegeben werden.
 *
 * Geräte schalten wirkt sofort an der Anlage (Licht, Klima, Dosierpumpe);
 * Verwaltung kann Einstellungen ändern, Sicherungen anlegen und
 * Stammdaten löschen. Beides soll niemand im Vorbeigehen freigeben.
 */
export const RISKANTE_STUFEN: readonly KiStufe[] = ['GeraeteSchalten', 'Verwaltung']

/** Vorbelegung eines neuen Schlüssels — wie im Bauplan: nur Dokumentieren, frei. */
export const VORBELEGUNG: readonly KiStufe[] = ['Dokumentieren']

/** Was die jeweilige Stufe erlaubt, in einem halben Satz — steht neben dem Umschalter. */
export const STUFEN_ERKLAERUNG: Record<KiStufe, string> = {
  Dokumentieren: 'Messungen, Journal, Aufgaben abhaken, Wartung, Kosten, Einkaufsliste',
  GrowPlanen: 'Phase wechseln, Zielwerte, Misch-, Licht- und Wochenplan, Pflanzen und Sorten',
  GeraeteSchalten: 'Licht, Klima und Dosierpumpen sofort auslösen',
  Verwaltung: 'Einstellungen, Sicherung anlegen, Import und Export, Stammdaten löschen',
}

/** Der Warnhinweis vor dem Freigeben einer riskanten Stufe. */
export const STUFEN_WARNUNG: Partial<Record<KiStufe, string>> = {
  GeraeteSchalten:
    'Mit „Geräte schalten" kann der Assistent Licht, Klima und Dosierpumpen sofort auslösen — '
    + 'auch wenn niemand hinsieht. Ein Missverständnis kann den Pflanzen schaden. '
    + 'Die Höchstwerte oben gelten zusätzlich.',
  Verwaltung:
    'Mit „Verwaltung" kann der Assistent Einstellungen ändern, Sicherungen anlegen, '
    + 'Daten importieren und Stammdaten löschen. Vor dem Importieren und Löschen '
    + 'legt Grow OS eine Sicherung an. Über die Zelt- und Steuerungseinstellungen kann er '
    + 'auch Dienste in Home Assistant auslösen — gib das nur einem Assistenten, dem du '
    + 'wie dir selbst vertraust. Schlüssel verwalten kann er nie.',
}

export function istRiskant(stufe: KiStufe): boolean {
  return RISKANTE_STUFEN.includes(stufe)
}

/* ------------------------------------------------------------------ */
/* Drei Zustände je Stufe, mit Warnhinweis (Fork AI, A-005, 03.10.2026) */
/* ------------------------------------------------------------------ */

/**
 * Was ein Schlüssel bei einer Stufe darf.
 *
 * - `gesperrt`: Grow OS lehnt ab.
 * - `rueckfrage`: freigegeben, der Assistent soll aber vorher fragen.
 * - `frei`: freigegeben, ohne Rückfrage.
 *
 * Über die Leitung: gesperrt = nicht in `stufen`; mit Rückfrage = in `stufen`
 * und in `rueckfrageBei`; frei = nur in `stufen`.
 */
export type StufenZustand = 'gesperrt' | 'rueckfrage' | 'frei'

/** Die drei Zustände in Anzeigereihenfolge, mit dem Wortlaut des Umschalters. */
export const ZUSTAENDE: ReadonlyArray<{ wert: StufenZustand; text: string }> = [
  { wert: 'gesperrt', text: 'Gesperrt' },
  { wert: 'rueckfrage', text: 'Mit Rückfrage' },
  { wert: 'frei', text: 'Frei' },
]

/** Der Wortlaut eines Zustands im Schild der Schlüsselliste: „Dokumentieren · frei". */
export const ZUSTAND_IM_SCHILD: Record<Exclude<StufenZustand, 'gesperrt'>, string> = {
  rueckfrage: 'mit Rückfrage',
  frei: 'frei',
}

/**
 * Die Zustände eines Schlüssels — und ob gerade ein Warnhinweis offen ist.
 *
 * `offeneWarnung` ist die riskante Stufe, die von Gesperrt auf Mit Rückfrage
 * oder Frei gestellt, aber noch nicht bestätigt wurde, samt dem gewünschten
 * Zustand. Solange sie offen ist, bleibt die Stufe in `zustaende` GESPERRT.
 */
export interface StufenWahl {
  zustaende: Record<KiStufe, StufenZustand>
  offeneWarnung: { stufe: KiStufe; ziel: Exclude<StufenZustand, 'gesperrt'> } | null
}

/** Aus dem, was über die Leitung kommt. Eine Rückfrage für eine gesperrte Stufe gibt es nicht. */
export function stufenWahl(stufen: readonly string[] = VORBELEGUNG, rueckfrageBei: readonly string[] = []): StufenWahl {
  const zustaende = {} as Record<KiStufe, StufenZustand>
  for (const stufe of KI_STUFEN) {
    zustaende[stufe] = !stufen.includes(stufe) ? 'gesperrt' : rueckfrageBei.includes(stufe) ? 'rueckfrage' : 'frei'
  }
  return { zustaende, offeneWarnung: null }
}

/**
 * Ein Zustand wurde gewählt.
 *
 * Eine riskante Stufe von Gesperrt auf Mit Rückfrage oder Frei: erst der
 * Warnhinweis, die Stufe bleibt gesperrt. Zwischen Mit Rückfrage und Frei
 * fragt niemand — die Stufe ist schon freigegeben. Zurück auf Gesperrt geht
 * immer und schliesst einen offenen Hinweis dieser Stufe.
 */
export function zustandWaehlen(wahl: StufenWahl, stufe: KiStufe, ziel: StufenZustand): StufenWahl {
  const offen = wahl.offeneWarnung?.stufe === stufe ? null : wahl.offeneWarnung
  const bisher = wahl.zustaende[stufe]
  if (ziel !== 'gesperrt' && bisher === 'gesperrt' && istRiskant(stufe)) {
    return { zustaende: wahl.zustaende, offeneWarnung: { stufe, ziel } }
  }
  if (bisher === ziel) return { zustaende: wahl.zustaende, offeneWarnung: offen }
  return { zustaende: { ...wahl.zustaende, [stufe]: ziel }, offeneWarnung: offen }
}

/** „Freigeben" im Warnhinweis — erst jetzt gilt der gewählte Zustand. */
export function warnungBestaetigen(wahl: StufenWahl): StufenWahl {
  if (wahl.offeneWarnung == null) return wahl
  const { stufe, ziel } = wahl.offeneWarnung
  return { zustaende: { ...wahl.zustaende, [stufe]: ziel }, offeneWarnung: null }
}

/** „Nicht freigeben" — die Stufe bleibt gesperrt. */
export function warnungAblehnen(wahl: StufenWahl): StufenWahl {
  return { zustaende: wahl.zustaende, offeneWarnung: null }
}

/** Die Wahl als Rumpf: freigegebene Stufen und die mit Rückfrage, in fester Reihenfolge. */
export function anfrageAus(wahl: StufenWahl): { stufen: KiStufe[]; rueckfrageBei: KiStufe[] } {
  return {
    stufen: KI_STUFEN.filter((s) => wahl.zustaende[s] !== 'gesperrt'),
    rueckfrageBei: KI_STUFEN.filter((s) => wahl.zustaende[s] === 'rueckfrage'),
  }
}

/**
 * Die Schilder eines Schlüssels: je freigegebener Stufe „Name · frei" bzw.
 * „Name · mit Rückfrage". Gesperrte Stufen erscheinen nicht.
 */
export function stufenSchilder(stufen: readonly string[], rueckfrageBei: readonly string[]): Array<{ stufe: KiStufe; zustand: Exclude<StufenZustand, 'gesperrt'>; text: string }> {
  return KI_STUFEN.filter((s) => stufen.includes(s)).map((stufe) => {
    const zustand = rueckfrageBei.includes(stufe) ? 'rueckfrage' as const : 'frei' as const
    return { stufe, zustand, text: `${kiStufeName(stufe)} · ${ZUSTAND_IM_SCHILD[zustand]}` }
  })
}

/* ------------------------------------------------------------------ */
/* Anzeige                                                             */
/* ------------------------------------------------------------------ */

/** Die Stufen eines Schlüssels als deutsche Liste — nie die Bezeichner. */
export function stufenText(stufen: readonly string[]): string {
  if (stufen.length === 0) return 'keine Stufe — nur lesen'
  return KI_STUFEN.filter((s) => stufen.includes(s))
    .concat(stufen.filter((s) => !KI_STUFEN.includes(s as KiStufe)) as KiStufe[])
    .map(kiStufeName)
    .join(' · ')
}

/**
 * Der Anfang des Schlüssels zum Wiedererkennen: „gok_ab12cd34…".
 *
 * Das Backend schickt laut Bauplan die acht Zeichen NACH `gok_`. Kommt es doch
 * mit Vorsilbe, wird sie nicht doppelt gesetzt; mehr als acht Zeichen zeigt
 * die Liste nie — die Liste ist kein Ort für einen Schlüssel.
 */
export function praefixAnzeige(praefix: string): string {
  const ohne = praefix.startsWith('gok_') ? praefix.slice(4) : praefix
  return `gok_${ohne.slice(0, 8)}…`
}

/* ------------------------------------------------------------------ */
/* Höchstwerte                                                         */
/* ------------------------------------------------------------------ */

export interface HoechstwerteEntwurf {
  maxDosisMl: string
  maxBefehleJeStunde: string
}

export function hoechstwerteEntwurf(werte: { maxDosisMlJeBefehl: number; maxSchaltbefehleJeStunde: number }): HoechstwerteEntwurf {
  return { maxDosisMl: feldText(werte.maxDosisMlJeBefehl), maxBefehleJeStunde: feldText(werte.maxSchaltbefehleJeStunde) }
}

/** Feldnamen wie im Vertrag (C#), kleingeschrieben — so werden Feldfehler zugeordnet. */
export const FELD = {
  maxDosis: 'maxdosismljebefehl',
  maxBefehle: 'maxschaltbefehlejestunde',
  rueckfrage: 'rueckfragebei',
  name: 'name',
  stufen: 'stufen',
} as const

export type HoechstwerteGelesen =
  | { ok: true; werte: { maxDosisMlJeBefehl: number; maxSchaltbefehleJeStunde: number } }
  | { ok: false; meldung: string; felder: Record<string, string> }

/**
 * Die getippten Höchstwerte lesen — deutsche Schreibweise („2,5").
 *
 * Leer und unlesbar werden NICHT still zu 0 oder zur Vorbelegung: eine
 * Höchstgrenze von 0 ml hiesse „nie dosieren", eine stille Vorbelegung hiesse
 * eine Grenze, die niemand gewählt hat. Beides meldet die Seite.
 */
export function hoechstwerteLesen(entwurf: HoechstwerteEntwurf): HoechstwerteGelesen {
  const felder: Record<string, string> = {}
  const unlesbar = unlesbarMeldung(unlesbareFelder([
    [entwurf.maxDosisMl, 'Höchstens ml je Dosierbefehl'],
    [entwurf.maxBefehleJeStunde, 'Höchstens Befehle je Stunde'],
  ]))

  const dosis = zahlOderNull(entwurf.maxDosisMl)
  const befehle = zahlOderNull(entwurf.maxBefehleJeStunde)

  if (istLeer(entwurf.maxDosisMl) || dosis == null) felder[FELD.maxDosis] = 'Bitte eine Zahl eintragen.'
  else if (dosis <= 0) felder[FELD.maxDosis] = 'Muss größer als 0 sein.'

  if (istLeer(entwurf.maxBefehleJeStunde) || befehle == null) felder[FELD.maxBefehle] = 'Bitte eine Zahl eintragen.'
  else if (!Number.isInteger(befehle)) felder[FELD.maxBefehle] = 'Bitte eine ganze Zahl eintragen.'
  else if (befehle < 0) felder[FELD.maxBefehle] = 'Darf nicht negativ sein.'

  if (Object.keys(felder).length > 0 || dosis == null || befehle == null) {
    return { ok: false, meldung: unlesbar ?? 'Bitte die markierten Felder prüfen.', felder }
  }
  return { ok: true, werte: { maxDosisMlJeBefehl: dosis, maxSchaltbefehleJeStunde: befehle } }
}

/* ------------------------------------------------------------------ */
/* Feldfehler aus der API                                              */
/* ------------------------------------------------------------------ */

/**
 * Feldfehler einer Antwort, je Feld — zugeordnet über den LETZTEN Teil des
 * Namens, kleingeschrieben und ohne Index.
 *
 * Der Vertrag legt die Schreibweise nicht fest: „Hoechstwerte.MaxDosisMlJeBefehl",
 * „hoechstwerte.maxDosisMlJeBefehl" und „Stufen[0]" kommen je nach Prüfstelle
 * vor. So landet jede Fassung am selben Feld.
 */
export function feldFehlerJeFeld(caught: unknown): Record<string, string> {
  const roh = caught instanceof ApiRequestError ? caught.payload?.fieldErrors : undefined
  if (!roh) return {}
  const raus: Record<string, string> = {}
  for (const [name, texte] of Object.entries(roh)) {
    const text = (texte ?? []).filter((t) => t.trim().length > 0).join(' ')
    if (!text) continue
    const schluessel = (name.split('.').pop() ?? name).replace(/\[\d+\]$/, '').toLowerCase()
    raus[schluessel] = raus[schluessel] ? `${raus[schluessel]} ${text}` : text
  }
  return raus
}

/**
 * Die Meldung über dem Formular: zugeordnete Feldfehler stehen am Feld, alle
 * anderen hier — damit keiner verschwindet.
 */
export function sammelmeldung(caught: unknown, sichtbareFelder: readonly string[], ersatz: string): string {
  const felder = feldFehlerJeFeld(caught)
  const namen = Object.keys(felder)
  if (namen.length === 0) return caught instanceof Error ? caught.message : ersatz
  const markiert = namen.some((name) => sichtbareFelder.includes(name))
  const uebrig = namen.filter((name) => !sichtbareFelder.includes(name)).map((name) => felder[name])
  return [...(markiert ? ['Bitte die markierten Felder prüfen.'] : []), ...uebrig].join(' ')
}
