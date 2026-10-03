import { ApiRequestError } from '../../api'
import { KI_STUFEN, kiStufeName } from '../../deutsche-woerter'
import type { KiStufe } from '../../types'
import { feldText, istLeer, unlesbarMeldung, unlesbareFelder, zahlOderNull } from '../../zahlenfeld'

/**
 * Zugriff für KI-Assistenten (A-003) — alles, was die Oberfläche entscheidet,
 * ohne React. Steht hier, damit es ohne Browser prüfbar ist
 * (`ki-zugriff.test.tsx`): ob ein Haken gesetzt wird, ob eine Warnung offen
 * ist, was aus einem getippten Höchstwert wird.
 */

/**
 * Stufen, die nur nach einem bestätigten Warnhinweis angehakt werden.
 *
 * Geräte schalten wirkt sofort an der Anlage (Licht, Klima, Dosierpumpe);
 * Verwaltung kann Einstellungen ändern, Sicherungen zurückspielen und
 * Stammdaten löschen. Beides soll niemand im Vorbeigehen freigeben.
 */
export const RISKANTE_STUFEN: readonly KiStufe[] = ['GeraeteSchalten', 'Verwaltung']

/** Vorbelegung eines neuen Schlüssels — wie im Bauplan: nur Dokumentieren. */
export const VORBELEGUNG: readonly KiStufe[] = ['Dokumentieren']

/** Was die jeweilige Stufe erlaubt, in einem halben Satz — steht unter dem Häkchen. */
export const STUFEN_ERKLAERUNG: Record<KiStufe, string> = {
  Dokumentieren: 'Messungen, Journal, Aufgaben abhaken, Wartung, Kosten, Einkaufsliste',
  GrowPlanen: 'Phase wechseln, Zielwerte, Misch-, Licht- und Wochenplan, Pflanzen und Sorten',
  GeraeteSchalten: 'Licht, Klima und Dosierpumpen sofort auslösen',
  Verwaltung: 'Einstellungen, Sicherungen, Import und Export, Stammdaten löschen',
}

/** Der Warnhinweis vor dem Freigeben einer riskanten Stufe. */
export const STUFEN_WARNUNG: Partial<Record<KiStufe, string>> = {
  GeraeteSchalten:
    'Mit „Geräte schalten" kann der Assistent Licht, Klima und Dosierpumpen sofort auslösen — '
    + 'auch wenn niemand hinsieht. Ein Missverständnis kann den Pflanzen schaden. '
    + 'Die Höchstwerte unten gelten zusätzlich.',
  Verwaltung:
    'Mit „Verwaltung" kann der Assistent Einstellungen ändern, Sicherungen zurückspielen, '
    + 'Daten importieren und Stammdaten löschen. Vor dem Zurückspielen, Importieren und Löschen '
    + 'legt Grow OS eine Sicherung an. Schlüssel verwalten kann er nie.',
}

export function istRiskant(stufe: KiStufe): boolean {
  return RISKANTE_STUFEN.includes(stufe)
}

/* ------------------------------------------------------------------ */
/* Häkchen mit Warnhinweis                                             */
/* ------------------------------------------------------------------ */

/**
 * Die Häkchen eines Schlüssels — und ob gerade ein Warnhinweis offen ist.
 *
 * `offeneWarnung` ist die riskante Stufe, die angeklickt, aber noch nicht
 * bestätigt wurde. Solange sie offen ist, steht sie NICHT in `auswahl`:
 * ohne Bestätigung bleibt der Haken aus.
 */
export interface StufenWahl {
  auswahl: KiStufe[]
  offeneWarnung: KiStufe | null
}

export function stufenWahl(auswahl: readonly KiStufe[] = VORBELEGUNG): StufenWahl {
  return { auswahl: sortiert(auswahl), offeneWarnung: null }
}

/** Ein Häkchen wurde angeklickt. */
export function stufeAnklicken(wahl: StufenWahl, stufe: KiStufe, an: boolean): StufenWahl {
  if (!an) {
    return {
      auswahl: wahl.auswahl.filter((s) => s !== stufe),
      offeneWarnung: wahl.offeneWarnung === stufe ? null : wahl.offeneWarnung,
    }
  }
  if (wahl.auswahl.includes(stufe)) return wahl
  if (istRiskant(stufe)) return { auswahl: wahl.auswahl, offeneWarnung: stufe }
  return { auswahl: sortiert([...wahl.auswahl, stufe]), offeneWarnung: wahl.offeneWarnung }
}

/** „Freigeben" im Warnhinweis — erst jetzt kommt der Haken. */
export function warnungBestaetigen(wahl: StufenWahl): StufenWahl {
  if (wahl.offeneWarnung == null) return wahl
  return { auswahl: sortiert([...wahl.auswahl, wahl.offeneWarnung]), offeneWarnung: null }
}

/** „Nicht freigeben" — der Haken bleibt aus. */
export function warnungAblehnen(wahl: StufenWahl): StufenWahl {
  return { auswahl: wahl.auswahl, offeneWarnung: null }
}

function sortiert(stufen: readonly KiStufe[]): KiStufe[] {
  return KI_STUFEN.filter((s) => stufen.includes(s))
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

/** Die Auswahl der Rückfrage-Regel — Wert über die Leitung: `null` oder der Stufenname. */
export const RUECKFRAGE_OPTIONEN: Array<{ wert: KiStufe | null; text: string }> = [
  { wert: null, text: 'nie' },
  ...KI_STUFEN.map((stufe) => ({ wert: stufe, text: `ab ${kiStufeName(stufe)}` })),
]

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
  rueckfrage: 'rueckfrageabstufe',
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
