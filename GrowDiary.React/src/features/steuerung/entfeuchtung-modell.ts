import type { EntfeuchterLive, EntfeuchterZusatzLive, SteuerungModul } from './steuerung-typen'

/**
 * Fork AI (A-015): Rechenteil der Seite „Entfeuchtung" — ohne React.
 *
 * Die Seite fasst den Hauptentfeuchter und den Zusatz-Entfeuchter unter EINEM
 * Eintrag zusammen. Das Backend kennt weiter zwei Module (`entfeuchter`,
 * `entfeuchter-zusatz`: Probelauf, Rollen, Namen hängen daran); zusammengefasst
 * wird erst in der Oberfläche.
 */

export type EntfeuchtungReiter = 'ueberblick' | 'regel' | 'schutz' | 'einrichtung'

export const ENTFEUCHTUNG_REITER: Array<{ value: EntfeuchtungReiter; label: string }> = [
  { value: 'ueberblick', label: 'Überblick' },
  { value: 'regel', label: 'Regel' },
  { value: 'schutz', label: 'Schutz' },
  { value: 'einrichtung', label: 'Einrichtung' },
]

export const ENTFEUCHTUNG_KENNUNG = 'entfeuchtung'
const HAUPT = 'entfeuchter'
const ZUSATZ = 'entfeuchter-zusatz'

/**
 * Die Steuerungen für Übersicht und Chip-Leiste: die zwei Entfeuchter-Module
 * werden EIN Eintrag „Entfeuchtung" an der Stelle des ersten.
 *
 * <b>Status:</b> „warn", sobald eines warnt; „an", sobald eines läuft; sonst „aus".
 */
export function mitEntfeuchtung(module: SteuerungModul[], zusatzGesagt: boolean | null = null): SteuerungModul[] {
  const haupt = module.find((m) => m.kennung === HAUPT)
  if (!haupt) return module
  const zusatz = module.find((m) => m.kennung === ZUSATZ)
  // Hat der Nutzer es gesagt (Einrichtung), gilt das; sonst entscheidet, ob für den Zusatz ein Zustand bekannt ist.
  const zusatzDa = zusatz != null && (zusatzGesagt ?? zusatz.unterzeile !== 'Zustand unbekannt')

  const status = [haupt.status, zusatzDa ? zusatz!.status : 'aus'].includes('warn') ? 'warn'
    : [haupt.status, zusatzDa ? zusatz!.status : 'aus'].includes('an') ? 'an' : 'aus'
  const zusammen: SteuerungModul = {
    kennung: ENTFEUCHTUNG_KENNUNG,
    titel: 'Entfeuchtung',
    status,
    // Kurz halten: die Zeile ist am Handy nur zwei Spalten breit. Die Namen stehen auf der Seite selbst.
    kurz: zusatzDa ? 'Haupt- und Zusatz-Entfeuchter' : haupt.kurz,
    wert: haupt.wert,
    unterzeile: zusatzDa ? `${haupt.unterzeile.split(' · ')[0]} · Zusatz ${zusatz!.unterzeile}` : haupt.unterzeile,
    hatDetail: haupt.hatDetail,
  }
  const ergebnis: SteuerungModul[] = []
  for (const m of module) {
    if (m.kennung === HAUPT) ergebnis.push(zusammen)
    else if (m.kennung !== ZUSATZ) ergebnis.push(m)
  }
  return ergebnis
}

/**
 * Gibt es einen Zusatz-Entfeuchter? Home Assistant antwortet, kennt aber
 * keinen Zustand für die Steckdose und keinen Online-Zustand → es ist keiner
 * zugeordnet. Ohne Antwort von Home Assistant bleibt es beim Vorhandenen
 * (nichts verstecken, was nur gerade nicht erreichbar ist).
 */
export function zusatzVorhanden(live: EntfeuchterZusatzLive | null | undefined): boolean {
  if (!live) return false
  if (!live.haErreichbar) return true
  return live.zusatzAn != null || live.zusatzOnline != null || live.leistungW != null
}

export type GeraeteStatus = 'läuft' | 'aus' | 'wartet' | 'offline' | 'Automatik aus' | 'unbekannt'

/** Der Status des Hauptentfeuchters — nur ein Wort, keine Leistung. */
export function hauptStatus(live: EntfeuchterLive): GeraeteStatus {
  if (live.portOnline === false) return 'offline'
  if (live.automatikAn === false) return 'Automatik aus'
  if (live.portAn === true) return 'läuft'
  if (live.portAn === false) return 'aus'
  return 'unbekannt'
}

/** Der Status des Zusatzes: „wartet", solange das Zelt zu warm ist und er auf sein Wieder-EIN wartet. */
export function zusatzStatus(live: EntfeuchterZusatzLive, wartetAufTemperatur: boolean): GeraeteStatus {
  if (live.zusatzOnline === false) return 'offline'
  if (live.automatikAn === false) return 'Automatik aus'
  if (live.zusatzAn === true) return 'läuft'
  if (live.zusatzAn === false) return wartetAufTemperatur ? 'wartet' : 'aus'
  return 'unbekannt'
}
