import { V1Alert } from '../../components/v1'
/**
 * Fork AI (A-017, Lücke 8): Der Hinweis „Home Assistant antwortet nicht" — EINE Komponente für alle Steuerungs-Seiten.
 *
 * <b>Der Anlass.</b> Sechs Stellen (Übersicht, CO₂, Licht, Entfeuchtung, Zuluft, Wasserkühler) trugen fünf verschiedene
 * Fassungen desselben Hinweises. Alle behaupteten „Die Regelung läuft … weiter" — in einer leeren Installation, in der noch
 * nichts eingerichtet ist und Home Assistant (noch) nicht antwortet, stimmt das nicht. Jetzt gibt es einen Text je Zustand.
 *
 * <b>„Eingerichtet" heißt hier:</b> Die Seite weiß von mindestens einer zugeordneten Entität oder einer gewählten Steuerung.
 * Mehr kann sie ohne Home Assistant nicht wissen (ob Helfer und Regelung dort wirklich liegen, sagt nur Home Assistant);
 * deshalb behauptet der Text für den eingerichteten Fall nur, was dann gilt, und für den leeren nur, was sicher stimmt.
 *
 * `weiter` benennt, was bei Verbindungsverlust von allein weiterläuft: eine Regelung in Home Assistant — oder, bei der
 * Lampe, der Zeitplan im Controller.
 */
import { haStummText, type HaStummWeiter } from './ha-stumm-text'

export function HaStummHinweis({ haErreichbar, eingerichtet, weiter = 'regelung' }: { haErreichbar: boolean; eingerichtet: boolean; weiter?: HaStummWeiter }) {
  if (haErreichbar) return null
  const { titel, text } = haStummText(eingerichtet, weiter)
  return <V1Alert title={titel} message={text} />
}
