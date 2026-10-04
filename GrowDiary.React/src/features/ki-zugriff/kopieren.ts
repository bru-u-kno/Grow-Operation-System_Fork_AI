/**
 * Kopieren ohne Zwischenablage-Schnittstelle: im Home-Assistant-Rahmen ohne
 * HTTPS fehlt `navigator.clipboard` oft. Wirft, wenn auch das abgelehnt wird —
 * dann sagt die Oberfläche, dass von Hand kopiert werden muss.
 */
export function altesKopieren(text: string) {
  const feld = document.createElement('textarea')
  feld.value = text
  feld.setAttribute('readonly', '')
  feld.style.position = 'fixed'
  feld.style.opacity = '0'
  document.body.appendChild(feld)
  feld.select()
  const ok = document.execCommand('copy')
  feld.remove()
  if (!ok) throw new Error('Kopieren abgelehnt')
}
