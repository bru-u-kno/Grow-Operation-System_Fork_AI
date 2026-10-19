/**
 * Die Hilfe „Ohne HA-Titelleiste“ auf der Seite Handy → App einrichten.
 *
 * Home Assistant zeichnet über jedes Add-on seine Titelleiste; ein Add-on kann sie nicht abschalten. Nur die
 * HACS-Integration „Ingress“ (lovelylain/hass_ingress) legt ein eigenes Panel ohne Leiste an. Die App erkennt, ob die
 * Leiste da ist und ob es das Panel schon gibt, und zeigt dazu die passende Karte. Hier steht nur die Entscheidung
 * und der fertige Text — angezeigt wird er in `TitelleisteHilfe.tsx`.
 */

export type Karte = 'einrichten' | 'oeffnen' | 'fertig' | 'keine'

export type Lage = {
  /** true, wenn die App in Home Assistant steckt (iframe der gleichen Herkunft). */
  inHomeAssistant: boolean
  /** true, wenn Home Assistant seine Titelleiste über der App zeichnet. */
  kopfleiste: boolean
  /** Name des Ingress-Panels ohne Titelleiste für diese App, oder null. */
  panel: string | null | undefined
}

/**
 * Welche Karte gilt?
 *
 * - Leiste da, kein Panel → Anleitung zum Einrichten.
 * - Panel da, Leiste trotzdem da → die App wurde über den normalen Eintrag geöffnet: sagen, wo das Panel zu finden ist.
 * - Panel da, Leiste weg → eingerichtet, nur eine grüne Notiz.
 * - Sonst (nicht in Home Assistant, oder keine Leiste und kein Panel) → keine Karte.
 */
export function waehleKarte(lage: Lage): Karte {
  if (!lage.inHomeAssistant) return 'keine'
  if (lage.panel) return lage.kopfleiste ? 'oeffnen' : 'fertig'
  return lage.kopfleiste ? 'einrichten' : 'keine'
}

/** Der Name, den die Anleitung für das neue Panel vorschlägt (Adresse und Eintrag in der Seitenleiste). */
export const VORGESCHLAGENER_PANEL = 'cultivate-control'

/**
 * Der Block für die `configuration.yaml`, mit der Kennung dieser Installation.
 * `slug` ist der Add-on-Slug aus dem Supervisor, etwa `00d580e3_cultivate_control`.
 */
export function yamlBlock(slug: string, panel: string = VORGESCHLAGENER_PANEL): string {
  return [
    'ingress:',
    `  ${panel}:`,
    '    work_mode: hassio',
    `    url: ${slug}`,
    '    ui_mode: normal',
    '    title: Cultivate Control',
    '    icon: mdi:sprout',
  ].join('\n')
}

/**
 * In die Zwischenablage kopieren — auch dort, wo `navigator.clipboard` fehlt.
 *
 * Im Ingress einer Anlage ohne HTTPS (http://192.168…) gibt es die Clipboard-API nicht; dann hilft nur der alte Weg
 * über ein verstecktes Textfeld. Gibt zurück, ob etwas kopiert wurde.
 */
export async function inZwischenablage(text: string): Promise<boolean> {
  try {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(text)
      return true
    }
  } catch {
    // weiter mit dem Rückweg
  }
  try {
    const feld = document.createElement('textarea')
    feld.value = text
    feld.setAttribute('readonly', '')
    feld.style.position = 'fixed'
    feld.style.opacity = '0'
    document.body.appendChild(feld)
    feld.select()
    const ok = document.execCommand('copy')
    document.body.removeChild(feld)
    return ok
  } catch {
    return false
  }
}
