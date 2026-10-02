import type { CalibrationEventDto, HardwareItemDto, MaintenanceEventDto } from '../../types'

/**
 * Die Zeilen des Wartungs-Reiters: was wann fällig ist, je Gerät.
 *
 * **Woher die Frist kommt — und warum sie hier nicht erfunden wird.**
 * Schließt jemand eine Wartung oder Kalibrierung ab, rechnet das Backend den
 * Folgetermin (`NextDueAtUtc`, aus dem Intervall am Gerät) und legt dafür einen
 * geplanten Eintrag mit `DueAtUtc` an (`HardwareRepository.CompleteMaintenanceEvent`
 * bzw. `CompleteCalibrationEvent`). Diese Daten sind die Wahrheit; sie werden
 * hier nur gelesen:
 *
 * 1. Ein geplanter Eintrag (`status === 'Planned'`) trägt seine Frist selbst.
 * 2. Sonst gilt der Folgetermin des letzten abgeschlossenen Eintrags.
 * 3. Erst wenn es keinen gibt, zählt das Einbaudatum plus Intervall — dieselbe
 *    Regel wie `WartungDueService` („ohne Prüfeintrag zählt das Einbaudatum").
 *
 * **Der Fehler davor (Durchsicht 02.10.2026):** gerechnet wurde nur Einbaudatum
 * plus Intervall. Eine pH-Sonde, die vor drei Tagen kalibriert wurde, stand als
 * „überfällig · 106 T" da; Kalibrierungen wurden gar nicht geladen, abgesagte
 * Termine galten als offen, ausgemusterte Geräte zählten mit.
 *
 * Das Backend liefert die Frist (noch) nicht je Gerät — `api/maintenance-due`
 * nennt nur, was schon überfällig ist, und richtet sich nach der
 * Begleitungsstufe. Deshalb steht Schritt 3 hier ein zweites Mal.
 */
export type WartungsZeile = {
  schluessel: string
  titel: string
  geraet: string
  faellig: Date | null
  /** Ohne Frist: der Eintrag steht nur zur Information da. */
  ohneFrist: boolean
}

const TAG = 24 * 60 * 60 * 1000

type Art = 'wartung' | 'kalibrierung'
type Eintrag = {
  id: number
  hardwareItemId: number
  title: string
  status: string
  dueAtUtc: string | null
  performedAtUtc: string | null
  nextDueAtUtc: string | null
}

/** Wann der letzte abgeschlossene Eintrag einer Art war — und was er als nächsten Termin nennt. */
function letzterAbschluss(eintraege: Eintrag[], abgeschlossen: string[]): Eintrag | null {
  return eintraege
    .filter((e) => abgeschlossen.includes(e.status) && e.performedAtUtc != null)
    .reduce<Eintrag | null>((neuester, e) => (
      neuester == null || (e.performedAtUtc ?? '') > (neuester.performedAtUtc ?? '') ? e : neuester
    ), null)
}

export function wartungsZeilen(
  teile: HardwareItemDto[],
  wartungen: MaintenanceEventDto[],
  kalibrierungen: CalibrationEventDto[],
  geraeteNamen: Map<number, string>,
): WartungsZeile[] {
  // Ausgemusterte Geräte sind keine Arbeit mehr — weder ihre Intervalle noch
  // ihre liegengebliebenen Termine.
  const aktiv = new Map(teile.filter((teil) => teil.status !== 'Retired').map((teil) => [teil.id, teil]))
  const name = (id: number) => geraeteNamen.get(id) ?? aktiv.get(id)?.name ?? 'Unbekannt'

  const arten: Array<{ art: Art; eintraege: Eintrag[]; abgeschlossen: string[]; intervall: (t: HardwareItemDto) => number | null; titel: string }> = [
    { art: 'wartung', eintraege: wartungen, abgeschlossen: ['Completed'], intervall: (t) => t.inspectionIntervalDays, titel: 'Prüfen' },
    // Eine gescheiterte Kalibrierung hat trotzdem stattgefunden; das Backend
    // plant auch nach ihr den Folgetermin.
    { art: 'kalibrierung', eintraege: kalibrierungen, abgeschlossen: ['Completed', 'Failed'], intervall: (t) => t.calibrationIntervalDays, titel: 'Kalibrieren' },
  ]

  const zeilen: WartungsZeile[] = []
  for (const { art, eintraege, abgeschlossen, intervall, titel } of arten) {
    const eigene = eintraege.filter((e) => aktiv.has(e.hardwareItemId))

    // 1. Geplante Einträge tragen ihre Frist selbst.
    const geplant = eigene.filter((e) => e.status === 'Planned')
    for (const e of geplant) {
      zeilen.push({
        schluessel: `${art}-${e.id}`,
        titel: e.title,
        geraet: name(e.hardwareItemId),
        faellig: e.dueAtUtc ? new Date(e.dueAtUtc) : null,
        ohneFrist: e.dueAtUtc == null,
      })
    }

    // 2./3. Geräte mit Intervall, aber ohne geplanten Eintrag dieser Art.
    const mitTermin = new Set(geplant.map((e) => e.hardwareItemId))
    for (const teil of aktiv.values()) {
      const tage = intervall(teil)
      if (tage == null || tage <= 0 || mitTermin.has(teil.id)) continue
      const zuletzt = letzterAbschluss(eigene.filter((e) => e.hardwareItemId === teil.id), abgeschlossen)
      let faellig: Date | null = null
      if (zuletzt?.nextDueAtUtc) faellig = new Date(zuletzt.nextDueAtUtc)
      else if (zuletzt?.performedAtUtc) faellig = new Date(new Date(zuletzt.performedAtUtc).getTime() + tage * TAG)
      else if (teil.installedAtUtc) faellig = new Date(new Date(teil.installedAtUtc).getTime() + tage * TAG)
      zeilen.push({
        schluessel: `${art}-t-${teil.id}`,
        titel,
        geraet: name(teil.id),
        faellig,
        ohneFrist: faellig == null,
      })
    }
  }

  return zeilen.sort((a, b) => {
    if (a.faellig && b.faellig) return a.faellig.getTime() - b.faellig.getTime()
    if (a.faellig) return -1
    if (b.faellig) return 1
    return a.geraet.localeCompare(b.geraet)
  })
}
