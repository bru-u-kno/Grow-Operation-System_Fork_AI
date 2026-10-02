/* src/features/steuerung/feld-fehler.ts */
import { ApiRequestError } from '../../api'

/**
 * Feldfehler der Steuerungsseiten (Chiller, Entfeuchter, Zuluft).
 *
 * Die Seiten lesen ihre Fehler mit dem C#-Namen (`feldFehler.MindestpauseMin`).
 *
 * **Zwei Fehler, ein Anlass (Prüfung 01.10.2026):**
 * - Ein geleertes Zahlenfeld wurde zu `0` (`Number('')` ist `0`) — und das
 *   Backend nimmt 0 an. Eine geleerte Mindestpause hob den Kompressorschutz
 *   auf, „Zelt-Minimum" wurde 0 °C, gemeldet wurde „Gespeichert.".
 * - Antwortete das Backend mit Feldfehlern, wurde nie ein Feld markiert: die
 *   Seiten suchten `caught.fields`, die Fehler stehen aber in
 *   `payload.fieldErrors` — kleingeschrieben und als Liste.
 */
export type FeldFehler = Record<string, string>

/** C#-Schreibweise: erster Buchstabe groß. */
const gross = (name: string) => name.charAt(0).toUpperCase() + name.slice(1)

/** Zahlenfelder des Entwurfs, die leer (NaN) sind — sonst `null`. */
export function leereZahlenfelder(entwurf: object): FeldFehler | null {
  const leer = Object.entries(entwurf)
    .filter(([, wert]) => typeof wert === 'number' && !Number.isFinite(wert))
    .map(([name]) => [gross(name), 'Bitte eine Zahl eintragen.'] as const)
  return leer.length > 0 ? Object.fromEntries(leer) : null
}

/** Die Feldfehler einer Backend-Antwort, mit C#-Namen — oder `null`. */
export function feldFehlerAus(caught: unknown): FeldFehler | null {
  const roh = caught instanceof ApiRequestError ? caught.payload?.fieldErrors : undefined
  if (!roh) return null
  const felder = Object.entries(roh)
    .map(([name, texte]) => [gross(name), (texte ?? []).filter((t) => t.trim().length > 0).join(' ')] as const)
    .filter(([, text]) => text.length > 0)
  return felder.length > 0 ? Object.fromEntries(felder) : null
}

/** Der Wert eines Zahlenfelds: leer wird NaN (nie 0), Unlesbares ändert nichts. */
export function zahlAusFeld(text: string): number | null {
  if (text.trim() === '') return Number.NaN
  const wert = Number(text)
  return Number.isFinite(wert) ? wert : null
}

/**
 * Der Fehlertext für eine Grenze mit zwei Modi („Plan +" Abstand oder „Fest").
 *
 * **Der Befund (Prüfbericht 01.10.2026):** Wer im Modus „Fest" den festen Wert
 * leert und dann auf „Plan +" stellt, bekam beim Speichern „Bitte eine Zahl
 * eintragen." an einer Zeile, deren sichtbares Feld gefüllt ist — das leere
 * Feld blendet der gewählte Modus aus. Der Text sagt deshalb, welches Feld es
 * ist und unter welchem Modus es steht.
 *
 * **Warum nicht einfach den Modus umschalten.** Der Modus ist eine Einstellung,
 * die der Nutzer gewählt hat — die Seite stellte sie beim Speichern still um,
 * und mit dem nächsten Speichern ginge sie so nach Home Assistant.
 *
 * @param gewaehlt Der gewählte Modus.
 * @param felder Je Modus: der Name des Felds (wie es auf der Seite heißt), der
 *   Name des Modus (wie auf seinem Knopf) und der Fehler aus `feldFehler`.
 * @returns Der Text für die Markierung der Zeile — `undefined` ohne Fehler.
 */
export function modusFehler<M extends string>(
  gewaehlt: M,
  felder: Record<M, { feld: string; modus: string; fehler?: string }>,
): string | undefined {
  const hier = felder[gewaehlt]
  if (hier.fehler) return hier.fehler
  const dort = (Object.keys(felder) as M[])
    .filter((m) => m !== gewaehlt)
    .map((m) => felder[m])
    .find((f) => f.fehler)
  if (!dort?.fehler) return undefined
  return `${dort.feld}: ${dort.fehler.replace(/\.$/, '')} — steht unter „${dort.modus}" und ist ausgeblendet, solange „${hier.modus}" gewählt ist.`
}

/**
 * Der Entwurf für Anzeigetexte: ein gerade geleertes Zahlenfeld (NaN) zeigt
 * dort den zuletzt gespeicherten Wert — sonst stünde „Schwelle NaN" oder
 * „3 von NaN" in einer Kachel. Die Eingabefelder selbst nehmen weiter den
 * rohen Entwurf, damit sie leer bleiben.
 */
export function ohneLuecken<T extends object>(entwurf: T, gespeichert: T): T {
  const ergebnis = { ...entwurf } as Record<string, unknown>
  for (const [name, wert] of Object.entries(entwurf)) {
    if (typeof wert === 'number' && !Number.isFinite(wert)) {
      ergebnis[name] = (gespeichert as Record<string, unknown>)[name]
    }
  }
  return ergebnis as T
}

