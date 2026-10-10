import type { GeraetZeile } from '../steuerung/steuerung-typen'

/**
 * Fork AI (A-016, Etappe 6): Die Bluelab-Anleitung — Rechenteil ohne React.
 *
 * Der Fork überträgt seine Grenzwerte (pH, EC, Wassertemperatur) an die Alarmgrenzen eines Bluelab Guardian
 * (`BluelabGrenzenService`). Das braucht zweierlei in Home Assistant, das dem Nutzer gehört: sechs Alarm-Zahlen als
 * `number`-Entitäten und ein Skript, das eine Grenze beim Hersteller setzt. Der Fork legt beides NICHT an (es hängt an
 * Zugangsdaten); diese Datei sagt, was davon schon da ist.
 */

/** Die sechs Alarm-Rollen — dieselben wie `BluelabGrenzenService.Zuordnung`. */
export const BLUELAB_ALARME = ['ph_low', 'ph_high', 'ec_low', 'ec_high', 'temp_low', 'temp_high'] as const
export const BLUELAB_SKRIPT = 'skript'

/** Die Edenic-Schlüssel, die das Skript als `setting_key` bekommt — Spiegel von `BluelabGrenzenService.Zuordnung`. */
export const BLUELAB_SCHLUESSEL = [
  'setting.ph_low_alarm', 'setting.ph_high_alarm',
  'setting.ec_low_alarm', 'setting.ec_high_alarm',
  'setting.temp_low_alarm', 'setting.temp_high_alarm',
] as const

export type BluelabStand = { eingerichtet: boolean; zuletztUtc: string | null; fehler: string | null }

export type SchrittStatus = 'ok' | 'offen' | 'warn'
export type BluelabSchritt = { nr: number; titel: string; text: string; status: SchrittStatus }

export type BluelabLage = {
  schritte: BluelabSchritt[]
  /** Alles da und keine Störung — dann klappt die Anleitung zu. */
  fertig: boolean
}

function zeile(zeilen: readonly GeraetZeile[], rolle: string): GeraetZeile | undefined {
  return zeilen.find((z) => z.rolle === rolle)
}

/**
 * Wie weit die Einrichtung ist.
 *
 * @param zeilen die Rollen-Zeilen des Moduls `bluelab` (Zuordnung und ob die Entität in Home Assistant gefunden wurde)
 * @param stand  der Stand der Übertragung (`GET /api/steuerung/bluelab`), `null` wenn er sich nicht lesen ließ
 * @param zeit   formatiert einen Zeitpunkt für die Anzeige
 */
export function bluelabLage(zeilen: readonly GeraetZeile[], stand: BluelabStand | null, zeit: (iso: string) => string): BluelabLage {
  const alarme = BLUELAB_ALARME.map((r) => zeile(zeilen, r))
  const gefunden = alarme.filter((z) => z?.gefunden).length
  const zugeordnet = alarme.filter((z) => z && z.eingetragen.trim() !== '').length
  const skript = zeile(zeilen, BLUELAB_SKRIPT)
  const skriptDa = !!skript?.gefunden
  const skriptGesetzt = !!skript && skript.eingetragen.trim() !== ''

  const alarmeOk = gefunden === BLUELAB_ALARME.length
  const s1: BluelabSchritt = {
    nr: 1,
    titel: 'Die sechs Alarm-Zahlen in Home Assistant',
    status: alarmeOk ? 'ok' : zugeordnet > gefunden ? 'warn' : 'offen',
    text: alarmeOk ? '6 von 6 zugeordnet und gefunden.'
      : zugeordnet > gefunden ? `${zugeordnet} von 6 zugeordnet, aber nur ${gefunden} in Home Assistant gefunden — stimmen die Entitäten noch?`
      : 'Eine Integration für deinen Guardian muss die Alarmgrenzen als number-Entitäten liefern (pH unten/oben, EC unten/oben, Temperatur unten/oben).'
        + ` Zugeordnet ${zugeordnet} von 6.`,
  }
  const s2: BluelabSchritt = {
    nr: 2,
    titel: 'Ein Skript, das eine Grenze im Gerät setzt',
    status: skriptDa ? 'ok' : skriptGesetzt ? 'warn' : 'offen',
    text: skriptDa ? `${skript!.eingetragen} gefunden.`
      : skriptGesetzt ? `${skript!.eingetragen} ist zugeordnet, aber in Home Assistant nicht gefunden — gibt es das Skript noch?`
      : 'Ins Gerät schreiben kann die Integration nicht; das geht über die Cloud des Herstellers. Dafür braucht es ein Skript mit den Feldern setting_key und value. Nicht zugeordnet.',
  }
  const beides = alarmeOk && skriptDa
  const s3: BluelabSchritt = {
    nr: 3,
    titel: 'Skript und Alarm-Zahlen hier zuordnen',
    status: beides ? 'ok' : 'offen',
    text: beides ? 'Alles zugeordnet.' : 'Unten, in den Zeilen dieses Reiters; danach „Speichern".',
  }
  const gestoert = !!stand?.fehler
  const s4: BluelabSchritt = {
    nr: 4,
    titel: 'Übertragung',
    status: gestoert ? 'warn' : beides && stand?.zuletztUtc ? 'ok' : 'offen',
    text: gestoert ? `Übertragung gestört: ${stand!.fehler}`
      : beides && stand?.zuletztUtc ? `Zuletzt geschrieben: ${zeit(stand.zuletztUtc)} · keine Störung.`
      : beides ? 'Noch nichts geschrieben — der Fork prüft alle 5 Minuten und schreibt, was abweicht (je Wert höchstens alle 30 Minuten).'
      : 'Sobald zugeordnet, schreibt der Fork alle 5 Minuten, was abweicht — je Wert höchstens alle 30 Minuten.',
  }

  const schritte = [s1, s2, s3, s4]
  return { schritte, fertig: beides && !gestoert && !!stand?.zuletztUtc }
}
