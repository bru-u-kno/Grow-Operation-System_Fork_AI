/**
 * Die Adresse, die auf dem Handy landen soll.
 *
 * Der Server kann sie nicht liefern: er kennt Home Assistant nur als
 * `http://supervisor/core` und weiss nicht, unter welchem Namen du ihn
 * aufrufst. Der Browser weiss es — er ist gerade darüber verbunden. Also
 * liefert der Server nur den stabilen Pfad, und hier wird die Adresse gebaut.
 */

export type HostVerdict =
  | { usable: true; warning: null }
  | { usable: false; warning: string }
  | { usable: true; warning: string }

/**
 * Taugt diese Adresse für ein anderes Gerät?
 *
 * `localhost` zeigt auf dem Handy auf das Handy — der Code wäre wertlos, und
 * das merkt man erst nach dem Scannen. `.local` ist mDNS: auf iOS zuverlässig,
 * auf Android oft nicht. Beides ist einen Satz wert, bevor jemand scannt.
 */
export function judgeHost(origin: string): HostVerdict {
  let host: string
  try {
    host = new URL(origin).hostname.toLowerCase()
  } catch {
    return { usable: false, warning: 'Diese Adresse ist keine gültige URL.' }
  }

  if (host === 'localhost' || host === '127.0.0.1' || host === '::1' || host === '[::1]') {
    return {
      usable: false,
      warning: 'Diese Adresse gilt nur auf diesem Rechner. Auf dem Handy zeigt sie ins Leere — trag unten die Adresse ein, unter der du Home Assistant im Netzwerk erreichst.',
    }
  }

  if (host.endsWith('.local')) {
    return {
      usable: true,
      warning: 'Ein .local-Name funktioniert auf iPhones fast immer, auf Android-Geräten oft nicht. Klappt es nicht, trag unten die IP-Adresse ein.',
    }
  }

  return { usable: true, warning: null }
}

/**
 * Setzt Herkunft und Panel-Pfad zusammen.
 *
 * Der Pfad kommt vom Server (`/app/<slug>`), die Herkunft aus dem
 * Browser oder von Hand. Eine von Hand getippte Adresse hat oft einen
 * Schrägstrich am Ende oder gar kein Schema — beides wird hier geradegezogen,
 * statt es dem Nutzer als „ungültig" vor die Füsse zu werfen.
 */
export function buildPanelUrl(origin: string, panelPath: string): string | null {
  const roh = origin.trim()
  if (roh === '' || panelPath.trim() === '') return null

  const mitSchema = /^https?:\/\//i.test(roh) ? roh : `http://${roh}`

  try {
    const url = new URL(mitSchema)
    // Nur Schema, Host und Port uebernehmen: wer die Adresse aus der Adresszeile
    // kopiert, bringt den halben Ingress-Pfad mit, und der ist genau das, was
    // hier nicht gebraucht wird.
    return `${url.protocol}//${url.host}${panelPath}`
  } catch {
    return null
  }
}
