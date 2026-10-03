import type { Tone } from '../../components/v1'
import type { KiProtokollEintragDto } from '../../types'

/**
 * „Was die KI zuletzt getan hat" (A-003, Fork AI 03.10.2026) — was die Liste
 * aus einem Protokolleintrag macht, ohne React. Geprüft in
 * `ki-protokoll.test.tsx`.
 *
 * Auf dem Schirm steht nie `POST /api/grows/1/measurements` und nie ein
 * Fehlercode wie `ki_stufe_fehlt`, sondern „Messung eingetragen" und
 * „abgewiesen: Stufe fehlt".
 */

/**
 * Der Weg, den die Liste liest. Ohne `anzahl`: wie viele Einträge kommen,
 * entscheidet das Backend (`KiZugriffApiController.ProtokollVorgabe`) — eine
 * Zahl, eine Stelle.
 */
export function protokollWeg(schluesselId: number | null): string {
  const weg = '/api/settings/ki-zugriff/protokoll'
  return schluesselId == null ? weg : `${weg}?schluesselId=${schluesselId}`
}

/* ------------------------------------------------------------------ */
/* Aktion                                                              */
/* ------------------------------------------------------------------ */

/**
 * Pfad-Muster → deutsches Wort. Jedes Muster steht für eine echte Route des
 * Backends; das prüft `ki-protokoll.test.tsx` gegen die Controller — ein
 * Muster, das keine Route trifft, wäre eine Zuordnung, die nie greift.
 *
 * Die Reihenfolge zählt: das erste passende gewinnt.
 *
 * Zwei Formen je Aktion: `getan`, wenn sie ausgeführt wurde, `versuch`, wenn
 * nicht — „Licht geschaltet · abgewiesen" wäre ein Widerspruch auf einer Zeile.
 */
export const AKTIONEN: ReadonlyArray<{ methode: string; muster: RegExp; getan: string; versuch: string }> = [
  // Dokumentieren
  { methode: 'POST', muster: /^\/api\/grows\/\d+\/measurements$/, getan: 'Messung eingetragen', versuch: 'Messung eintragen' },
  { methode: 'PUT', muster: /^\/api\/measurements\/\d+$/, getan: 'Messung geändert', versuch: 'Messung ändern' },
  { methode: 'DELETE', muster: /^\/api\/measurements\/\d+$/, getan: 'Messung gelöscht', versuch: 'Messung löschen' },
  { methode: 'POST', muster: /^\/api\/measurements\/\d+\/photos$/, getan: 'Foto zur Messung', versuch: 'Foto zur Messung' },
  { methode: 'POST', muster: /^\/api\/grows\/\d+\/journal$/, getan: 'Journal-Eintrag', versuch: 'Journal-Eintrag' },
  { methode: 'DELETE', muster: /^\/api\/journal\/\d+$/, getan: 'Journal-Eintrag gelöscht', versuch: 'Journal-Eintrag löschen' },
  { methode: 'POST', muster: /^\/api\/grows\/\d+\/tasks$/, getan: 'Aufgabe angelegt', versuch: 'Aufgabe anlegen' },
  { methode: 'PATCH', muster: /^\/api\/tasks\/\d+\/status$/, getan: 'Aufgabe abgehakt oder geöffnet', versuch: 'Aufgabe abhaken oder öffnen' },
  { methode: 'DELETE', muster: /^\/api\/tasks\/\d+$/, getan: 'Aufgabe gelöscht', versuch: 'Aufgabe löschen' },
  { methode: 'POST', muster: /^\/api\/grows\/\d+\/addback\/logs$/, getan: 'Nachfüllen eingetragen', versuch: 'Nachfüllen eintragen' },
  { methode: 'POST', muster: /^\/api\/grows\/\d+\/addback\/calculate$/, getan: 'Nachfüllen berechnet', versuch: 'Nachfüllen berechnen' },
  { methode: 'POST', muster: /^\/api\/grows\/\d+\/changeouts$/, getan: 'Wasserwechsel eingetragen', versuch: 'Wasserwechsel eintragen' },
  { methode: 'DELETE', muster: /^\/api\/grows\/\d+\/changeouts\/\d+$/, getan: 'Wasserwechsel gelöscht', versuch: 'Wasserwechsel löschen' },
  { methode: 'POST', muster: /^\/api\/maintenance-events$/, getan: 'Wartung eingetragen', versuch: 'Wartung eintragen' },
  { methode: 'POST', muster: /^\/api\/maintenance-events\/\d+\/complete$/, getan: 'Wartung erledigt', versuch: 'Wartung abschließen' },
  { methode: 'POST', muster: /^\/api\/calibration-events$/, getan: 'Kalibrierung eingetragen', versuch: 'Kalibrierung eintragen' },
  { methode: 'POST', muster: /^\/api\/calibration-events\/\d+\/complete$/, getan: 'Kalibrierung erledigt', versuch: 'Kalibrierung abschließen' },
  { methode: 'POST', muster: /^\/api\/risk-events\/\d+\/acknowledge$/, getan: 'Meldung quittiert', versuch: 'Meldung quittieren' },
  { methode: 'POST', muster: /^\/api\/risk-events\/\d+\/resolve$/, getan: 'Meldung erledigt', versuch: 'Meldung erledigen' },
  { methode: 'POST', muster: /^\/api\/kosten\/[a-z-]+$/, getan: 'Kosten eingetragen', versuch: 'Kosten eintragen' },
  { methode: 'POST', muster: /^\/api\/curing\/jars\/\d+\/readings$/, getan: 'Messung am Glas', versuch: 'Messung am Glas' },
  // Grow planen
  { methode: 'POST', muster: /^\/api\/grows\/\d+\/actions\/flip-to-flower$/, getan: 'Auf Blüte umgestellt', versuch: 'Auf Blüte umstellen' },
  { methode: 'POST', muster: /^\/api\/grows\/\d+\/actions\/confirm-[a-z-]+$/, getan: 'Phasenwechsel bestätigt', versuch: 'Phasenwechsel bestätigen' },
  // Geräte schalten
  { methode: 'POST', muster: /^\/api\/dosing\/pumps\/\d+\/dose$/, getan: 'Pumpe dosiert', versuch: 'Pumpe dosieren' },
  { methode: 'POST', muster: /^\/api\/dosing\/pumps\/\d+\/stop$/, getan: 'Pumpe gestoppt', versuch: 'Pumpe stoppen' },
  { methode: 'POST', muster: /^\/api\/dosing\/pumps\/\d+\/calibration\/run$/, getan: 'Pumpe Probelauf', versuch: 'Pumpe Probelauf' },
  { methode: 'POST', muster: /^\/api\/steuerung\/licht\/befehl$/, getan: 'Licht geschaltet', versuch: 'Licht schalten' },
  { methode: 'POST', muster: /^\/api\/steuerung\/[a-z-]+\/probe$/, getan: 'Probeschaltung', versuch: 'Probeschaltung' },
  { methode: 'POST', muster: /^\/api\/ac-test\/\d+\/stufe$/, getan: 'Klima-Stufe gesetzt', versuch: 'Klima-Stufe setzen' },
  // Verwaltung
  { methode: 'POST', muster: /^\/api\/system\/backup$/, getan: 'Sicherung angelegt', versuch: 'Sicherung anlegen' },
]

/**
 * Ein kurzer Pfad für alles Unbekannte: ohne `/api/`, höchstens 40 Zeichen.
 * Lieber gekürzt als quer über das Telefon.
 */
export function kurzerPfad(pfad: string): string {
  const ohne = pfad.replace(/^\/api\//, '').replace(/^\//, '')
  return ohne.length > 40 ? `${ohne.slice(0, 39)}…` : ohne
}

/** Was getan — oder versucht — wurde, in Worten. */
export function aktionText(eintrag: Pick<KiProtokollEintragDto, 'art' | 'methode' | 'pfad' | 'erfolg'>): string {
  if (eintrag.art === 'ki-sicherung-vorher') return 'Sicherung vor der Aktion'
  if (eintrag.art === 'ki-adresse-gesperrt') return 'Zu viele falsche Schlüssel'

  const methode = (eintrag.methode ?? '').toUpperCase()
  const pfad = eintrag.pfad ?? ''
  const treffer = AKTIONEN.find((a) => a.methode === methode && a.muster.test(pfad))
  if (treffer) return eintrag.erfolg ? treffer.getan : treffer.versuch

  if (!methode || !pfad) return 'Unbekannte Anfrage'
  if (methode === 'GET') return `${eintrag.erfolg ? 'Gelesen' : 'Lesen'}: ${kurzerPfad(pfad)}`
  return `${methode} ${kurzerPfad(pfad)}`
}

/* ------------------------------------------------------------------ */
/* Ergebnis                                                            */
/* ------------------------------------------------------------------ */

export interface Ergebnis {
  text: string
  ton: Tone
}

/** Fehlercode der Sperre → Schild. Codes aus `KiZugriffSperre.cs` und `DosingApiController.cs`. */
const NACH_CODE: Record<string, Ergebnis> = {
  ki_stufe_fehlt: { text: 'abgewiesen: Stufe fehlt', ton: 'warn' },
  ki_kein_zugriff: { text: 'abgewiesen: nie erlaubt', ton: 'warn' },
  ki_nicht_eingestuft: { text: 'abgewiesen: nicht freigegeben', ton: 'warn' },
  ki_zugriff_aus: { text: 'abgewiesen: Zugriff aus', ton: 'warn' },
  ki_schluessel_ungueltig: { text: 'abgewiesen: falscher Schlüssel', ton: 'critical' },
  ki_zu_viele_versuche: { text: 'Adresse vorübergehend gesperrt', ton: 'critical' },
  ki_hoechstwert: { text: 'Höchstwert erreicht', ton: 'warn' },
  ki_sicherung_fehlgeschlagen: { text: 'nicht ausgeführt: Sicherung fehlgeschlagen', ton: 'critical' },
}

/**
 * Das Schild zum Eintrag — erst nach Fehlercode, dann nach Status.
 *
 * Ein gesperrter Schlüssel antwortet mit demselben Code wie ein falscher
 * (`ki_schluessel_ungueltig`); nur bei ihm kennt das Protokoll aber den
 * Schlüssel. Daran wird er hier unterschieden.
 */
export function ergebnisSchild(eintrag: Pick<KiProtokollEintragDto, 'fehlercode' | 'status' | 'erfolg' | 'schluesselId' | 'art'>): Ergebnis {
  const code = eintrag.fehlercode
  if (code === 'ki_schluessel_ungueltig' && eintrag.schluesselId != null) {
    return { text: 'abgewiesen: Schlüssel gesperrt', ton: 'critical' }
  }
  if (code && NACH_CODE[code]) return NACH_CODE[code]

  // Einträge aus forkai.163 tragen keinen Code — die Art sagt dann, was war.
  if (!code && eintrag.art === 'ki-zugriff-aus') return NACH_CODE.ki_zugriff_aus
  if (!code && eintrag.art === 'ki-schluessel-abgewiesen') return NACH_CODE.ki_schluessel_ungueltig
  if (!code && eintrag.art === 'ki-adresse-gesperrt') return NACH_CODE.ki_zu_viele_versuche

  const status = eintrag.status
  if (status == null) return eintrag.erfolg ? { text: 'erledigt', ton: 'ok' } : { text: 'nicht erledigt', ton: 'warn' }
  if (status < 400) return { text: 'erledigt', ton: 'ok' }
  if (status === 400 || status === 422) return { text: 'abgelehnt: Eingabe fehlerhaft', ton: 'warn' }
  if (status === 401) return NACH_CODE.ki_schluessel_ungueltig
  if (status === 403) return { text: 'abgewiesen', ton: 'warn' }
  if (status === 404) return { text: 'nicht gefunden', ton: 'warn' }
  if (status === 409) return { text: 'abgelehnt: passt nicht zum Stand', ton: 'warn' }
  if (status === 429) return { text: 'Höchstwert erreicht', ton: 'warn' }
  if (status >= 500) return { text: 'fehlgeschlagen', ton: 'critical' }
  return { text: `abgelehnt (${status})`, ton: 'warn' }
}

/** Wer es war — auch wenn kein Schlüssel erkannt wurde. */
export function schluesselText(eintrag: Pick<KiProtokollEintragDto, 'schluesselName' | 'schluesselId'>): string {
  if (eintrag.schluesselName) return eintrag.schluesselName
  if (eintrag.schluesselId != null) return 'gelöschter Schlüssel'
  return 'ohne gültigen Schlüssel'
}
