/* Texte des Hinweises „Home Assistant antwortet nicht" — getrennt von der Komponente (eine Datei mit Komponente exportiert nur Komponenten). */
export type HaStummWeiter = 'regelung' | 'zeitplan'

export function haStummText(eingerichtet: boolean, weiter: HaStummWeiter = 'regelung'): { titel: string; text: string } {
  if (!eingerichtet) {
    return {
      titel: 'Home Assistant ist noch nicht verbunden',
      text: 'Sobald die Verbindung steht, siehst du hier die Werte. Eingerichtet ist noch nichts.',
    }
  }
  return {
    titel: 'Home Assistant antwortet nicht',
    text: weiter === 'zeitplan'
      ? 'Die Werte sind der letzte bekannte Stand. Der Zeitplan läuft im Controller weiter.'
      : 'Die Werte sind der letzte bekannte Stand. Die Regelung läuft in Home Assistant unabhängig weiter.',
  }
}
