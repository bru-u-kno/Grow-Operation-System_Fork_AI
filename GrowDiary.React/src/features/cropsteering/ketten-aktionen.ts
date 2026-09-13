/**
 * Was der Nutzer bei einem gerissenen Kettenglied TUN kann — je Schlüssel
 * eine Aktion.
 *
 * **Der Anlass.** Ein Nutzer stand vor der Crop-Steering-Seite und wusste
 * nicht, wie er die Steuerung anschalten soll. Die Kette *sagte*, was fehlt
 * („Der Schalter unten steht auf aus") — aber der Schalter, die Untergrenze
 * und das Zielgerät liegen verstreut weiter unten, der Flip auf einer ganz
 * anderen Seite. Jetzt trägt jedes gerissene Glied einen Knopf, der direkt
 * hinführt.
 *
 * **Die Schlüssel kommen aus dem Backend** (`KettenSchluessel.cs`), nicht aus
 * den deutschen Titeln: eine Zuordnung am Wortlaut wäre beim nächsten
 * Umformulieren still tot. `ketten-aktionen.node.test.ts` zählt, dass jeder
 * Schlüssel, den das Backend vergeben kann, hier eine Aktion hat.
 */

/** Sprungziele auf der Crop-Steering-Seite selbst. */
export type KettenAnker = 'night-ramp' | 'untergrenze' | 'kuehler'

export type KettenAktion =
  /** Scrollt zum Formularblock auf derselben Seite und hebt ihn kurz hervor. */
  | { art: 'anker'; ziel: KettenAnker; label: string }
  /** Führt auf eine andere Seite; `{growId}` wird ersetzt. */
  | { art: 'weg'; ziel: string; label: string }
  /** Erfüllt oder bewusst ohne Knopf — mit ausgeschriebenem Grund. */
  | { art: 'keine'; grund: string }

export const KETTEN_AKTIONEN: Record<string, KettenAktion> = {
  'absenkung': { art: 'anker', ziel: 'night-ramp', label: 'Zum Schalter' },
  'zielgeraet': { art: 'anker', ziel: 'night-ramp', label: 'Zum Zielgerät' },
  'kuehler-steuerung': { art: 'anker', ziel: 'kuehler', label: 'Zum Kühler-Schalter' },
  'steckdose': { art: 'anker', ziel: 'kuehler', label: 'Zur Steckdose' },
  'verbindung': { art: 'weg', ziel: '/home-assistant', label: 'Zur Einrichtung' },

  'plan-untergrenze-zu-hoch': { art: 'anker', ziel: 'untergrenze', label: 'Zur Untergrenze' },
  // Fork AI (forkai.67): die Profile sind der Reiter „Profile" der Zielwerte.
  'plan-ohne-profil': { art: 'weg', ziel: '/zielwerte?tab=profile', label: 'Zu den Sollwert-Profilen' },
  'plan-vor-dem-flip': { art: 'weg', ziel: '/grows/{growId}', label: 'Flip eintragen' },
  'plan-abgeschaltet': { art: 'anker', ziel: 'night-ramp', label: 'Zum Schalter' },

  'plan-steht': { art: 'keine', grund: 'Der Plan steht — an diesem Glied gibt es nichts zu tun.' },
}
