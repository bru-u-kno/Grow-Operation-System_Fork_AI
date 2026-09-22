/**
 * Fork AI (forkai.134): Der Weg in den Reiter „Rollen" der Geräteseite, auf
 * Wunsch mit vorgewählter Steuerung. Eine Stelle für alle Links — sonst zeigt
 * jeder wieder auf die erste Steuerung (F-027).
 */
export function rollenPfad(modul?: string | null): string {
  return modul ? `/geraete?reiter=rollen&modul=${encodeURIComponent(modul)}` : '/geraete?reiter=rollen'
}
