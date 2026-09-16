/**
 * Fork AI (forkai.115, F-013): Zustände einer Zeile „Übergabe an Home Assistant".
 * Müssen wörtlich mit `WochenplanSyncService` im Backend übereinstimmen.
 */
export const ZUSTAND_FOLGT_PLAN = 'folgt dem Plan'

/** Das CO₂-Ziel schreibt die CO₂-Steuerung (Prozentstaffel aus dem Wochenwert), nicht der Sync. */
export const ZUSTAND_CO2_STEUERUNG = 'über CO₂-Steuerung'

/** Nur „von dir gesetzt" ist eine Handänderung, die man freigeben kann. */
export function istHandgesetzt(zustand: string): boolean {
  return zustand !== ZUSTAND_FOLGT_PLAN && zustand !== ZUSTAND_CO2_STEUERUNG
}
