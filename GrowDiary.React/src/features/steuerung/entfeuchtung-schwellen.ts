import type { EntfeuchterEinstellungen } from './steuerung-typen'

/**
 * Fork AI (A-021): Die Einschalt-Schwelle der Luftfeuchte und ihr Abstand zur Ausschalt-Schwelle —
 * Rechenteil ohne React.
 *
 * <b>Eine Schwelle statt vier.</b> Die Seite zeigt „Einschalten über" (ein Wert für Tag und Nacht,
 * auf Wunsch getrennt) und leitet „Ausschalten unter" daraus ab: EIN minus Abstand. Gespeichert
 * werden weiter die vier Felder (EIN/AUS, Tag/Nacht) — das Backend und die Regelung in Home
 * Assistant kennen nur diese. Der Fork rechnet AUS erst neu, wenn der Nutzer EIN oder den Abstand
 * anfasst; wer nichts ändert, behält seine gespeicherten Werte.
 */

/** Untergrenze der Schwellen, wie das Backend sie prüft (30 bis 90 %). */
export const SCHWELLE_MIN = 30
export const SCHWELLE_MAX = 90

type Schwellen = Pick<EntfeuchterEinstellungen, 'feuchteEinTag' | 'feuchteAusTag' | 'feuchteEinNacht' | 'feuchteAusNacht'>

/** Auf eine Nachkommastelle, damit 50 − 4 nie als 46,00000001 gespeichert wird. */
const rund = (w: number) => Math.round(w * 10) / 10

/** AUS aus EIN und Abstand — nie unter der Untergrenze des Backends. */
export function ausAusEin(ein: number, abstandProzent: number): number {
  return Math.max(SCHWELLE_MIN, rund(ein - abstandProzent))
}

/** Stehen Tag und Nacht verschieden? Dann zeigt die Seite beide Felder von Anfang an. */
export function schwellenGetrennt(s: Pick<Schwellen, 'feuchteEinTag' | 'feuchteEinNacht'>): boolean {
  return s.feuchteEinTag !== s.feuchteEinNacht
}

/**
 * Die Felder nach einer Änderung an EIN. Ohne „getrennt" gilt der Wert für Tag und Nacht;
 * mit „getrennt" ändert sich nur die gewählte Tageszeit.
 */
export function einSchwelleSetzen(
  aktuell: Schwellen,
  abstandProzent: number,
  neu: { wert: number; tageszeit: 'tag' | 'nacht' | 'beide' },
): Schwellen {
  const tag = neu.tageszeit === 'nacht' ? aktuell.feuchteEinTag : neu.wert
  const nacht = neu.tageszeit === 'tag' ? aktuell.feuchteEinNacht : neu.wert
  // Nur das AUS der angefassten Tageszeit wird neu gerechnet: ein bewusst anders gesetztes AUS der anderen Seite bleibt
  // (Prüfer, 13.10.2026: sonst hebt eine Änderung am Tag das wirksame Nacht-AUS still an).
  return {
    feuchteEinTag: tag,
    feuchteAusTag: neu.tageszeit === 'nacht' ? aktuell.feuchteAusTag : ausAusEin(tag, abstandProzent),
    feuchteEinNacht: nacht,
    feuchteAusNacht: neu.tageszeit === 'tag' ? aktuell.feuchteAusNacht : ausAusEin(nacht, abstandProzent),
  }
}

/**
 * Das AUS, bei dem Home Assistant wirklich ausschaltet: das gespeicherte, aber nie näher als der Abstand unter EIN
 * (die Vorlage nimmt das Kleinere). Bei den Vorgaben des Backends (EIN 60, AUS 57, Abstand 4) sind das 56, nicht 57.
 */
export function ausWirksam(ein: number, aus: number, abstandProzent: number): number {
  return Math.min(aus, ausAusEin(ein, abstandProzent))
}

/** Die AUS-Felder nach einer Änderung am Abstand (EIN bleibt). */
export function abstandAnwenden(aktuell: Schwellen, abstandProzent: number): Schwellen {
  return {
    feuchteEinTag: aktuell.feuchteEinTag,
    feuchteAusTag: ausAusEin(aktuell.feuchteEinTag, abstandProzent),
    feuchteEinNacht: aktuell.feuchteEinNacht,
    feuchteAusNacht: ausAusEin(aktuell.feuchteEinNacht, abstandProzent),
  }
}

/** Kleinster EIN-Wert, bei dem AUS noch über der Untergrenze des Backends liegt. */
export function einMinimum(abstandProzent: number): number {
  return SCHWELLE_MIN + Math.ceil(abstandProzent) + 1
}

/** Das kleinste Maß an Wartezeit auf Außenluft, das zu „Einschalten nach" passt — das Backend verlangt es. */
export function aussenluftNachEinschalten(aussenluftMin: number, einschaltMin: number): number {
  // Ein geleertes Feld gibt NaN weiter — Math.max(5, NaN) wäre NaN und bliebe es, auch wenn „Einschalten nach" längst wieder
  // gefüllt ist; das Speichern bliebe gesperrt, und die Markierung säße am falschen Feld (E2E-Befund 13.10.2026).
  if (!Number.isFinite(einschaltMin)) return aussenluftMin
  return Math.max(aussenluftMin, einschaltMin)
}

/**
 * Die Schwellen bei „Arbeitsbereich aus dem Plan": EIN = Plan-Obergrenze minus Vorlauf, AUS = EIN minus Abstand.
 *
 * <b>Spiegel zum Backend</b> (`EntfeuchterSteuerungService.ArbeitsbereichAusPlan`): dort wird gerechnet und geschrieben, hier nur
 * die Vorschau vor dem Speichern. Gleiche Regeln — EIN in der Spanne 31 + ⌈Abstand⌉ bis 90, auf eine Nachkommastelle gerundet;
 * beide Seiten haben dieselben Beispiele in ihren Tests.
 */
export function arbeitsbereichAusPlan(planObergrenze: number, vorlaufProzent: number, abstandProzent: number): { ein: number; aus: number } {
  const kleinstes = 31 + Math.ceil(abstandProzent)
  const ein = rund(Math.min(SCHWELLE_MAX, Math.max(kleinstes, planObergrenze - vorlaufProzent)))
  return { ein, aus: Math.max(SCHWELLE_MIN, rund(ein - abstandProzent)) }
}

/**
 * Die vier Schwellen bei „Arbeitsbereich aus dem Plan", Tag und Nacht einzeln: Der Plan nennt für die Nacht eine eigene Obergrenze
 * der Luftfeuchte, und die Entfeuchter sollen sie nachts genauso einhalten wie die Tages-Obergrenze am Tag. Ohne Nachtgrenze
 * gilt nachts die des Tages.
 *
 * <b>Spiegel zum Backend</b> (`EntfeuchterSteuerungService.ArbeitsbereichAusPlan(e, rhTag, rhNacht)`).
 */
export function schwellenAusPlan(planTag: number, planNacht: number | null, vorlaufProzent: number, abstandProzent: number): Schwellen {
  const tag = arbeitsbereichAusPlan(planTag, vorlaufProzent, abstandProzent)
  const nacht = planNacht != null && Number.isFinite(planNacht) ? arbeitsbereichAusPlan(planNacht, vorlaufProzent, abstandProzent) : tag
  return { feuchteEinTag: tag.ein, feuchteAusTag: tag.aus, feuchteEinNacht: nacht.ein, feuchteAusNacht: nacht.aus }
}

/**
 * Die Felder nach einer Änderung am Abstand: bei „Arbeitsbereich aus dem Plan" rechnet EIN mit neu (die Spanne hängt vom
 * Abstand ab), sonst bleibt EIN und nur AUS verschiebt sich.
 */
export function schwellenNachAbstand(
  aktuell: Schwellen & { arbeitsbereichQuelle?: 'fest' | 'plan'; vorlaufProzent?: number },
  abstandProzent: number,
  planObergrenze: number | null,
  planObergrenzeNacht: number | null = null,
): Schwellen {
  if (aktuell.arbeitsbereichQuelle === 'plan' && planObergrenze != null && Number.isFinite(abstandProzent)) {
    return schwellenAusPlan(planObergrenze, planObergrenzeNacht, aktuell.vorlaufProzent ?? 4, abstandProzent)
  }
  return abstandAnwenden(aktuell, abstandProzent)
}
