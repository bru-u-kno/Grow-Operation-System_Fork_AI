import AutomationPage from './AutomationPage'
import ZielwertePage from './ZielwertePage'
import { TabbedCollectionPage } from './TabbedCollectionPage'
import { PlanReiter } from '../features/zielwerte/PlanReiter'
import { MeldungenReiter } from '../features/meldungen/MeldungenReiter'

/**
 * Regeln & Automatik: EINE Seite mit den vier Bereichen als Tabs, in der
 * Reihenfolge des Entwurfs — Grenzwerte zuerst, denn das ist der Bereich,
 * den man im Alltag anfasst.
 *
 * Der Untertitel nennt ausdruecklich die beiden Automatiken, die NICHT hier
 * sitzen. Wer Automatik sucht, kommt auf die Seite, die so heisst — und schloss
 * bisher aus ihrem Inhalt, dass es die anderen nicht gibt.
 *
 * Sorten und Archiv sind keine Tab-Sammlungen mehr: der Entwurf legt
 * Bibliothek + Pheno-Hunt bzw. Ertragstabelle + Vergleich auf je eine Seite.
 */
export function RulesCollectionPage() {
  // Fork AI (forkai.121): Grenzwerte und Benachrichtigungen sind nach
  // „Ziele & Meldungen“ gezogen — hier bleiben die Auto-Messungen.
  return (
    <TabbedCollectionPage
      eyebrow="Betrieb / Auto-Messungen"
      title="Auto-Messungen"
      subtitle="Wann Grow OS von selbst misst. Grenzwerte und Benachrichtigungen findest du unter Ziele & Meldungen."
      tabs={[
        { key: 'automatik', label: 'Auto-Messungen', render: () => <AutomationPage /> },
      ]}
    />
  )
}

/**
 * Fork AI (forkai.67): Zielwerte — Auskunft und die beiden Editoren dahinter.
 *
 * Die Frage „was gilt gerade" und die Frage „wo ändere ich das" gehören
 * zusammen: der erste Reiter beantwortet die eine und verweist für die andere
 * auf Profile und Grenzwerte — die nun einen Reiter weiter liegen statt zwei
 * Menüpunkte entfernt. Der Feed-Chart fehlt hier bewusst: er wohnt in der
 * Wissensdatenbank und ist mehr als Zielwerte (Dosiermengen, Spülen), das
 * Herausbrechen wäre der grössere Eingriff als der Nutzen.
 */
export function ZielwerteCollectionPage() {
  return (
    <TabbedCollectionPage
      eyebrow="Betrieb"
      title="Ziele & Meldungen"
      subtitle="Was gerade gilt, der Plan deines Grows und wer dir Bescheid gibt — an einer Stelle."
      tabs={[
        { key: 'jetzt', label: 'Werte', render: () => <ZielwertePage /> },
        { key: 'plan', label: 'Plan', render: () => <PlanReiter /> },
        { key: 'meldungen', label: 'Meldungen', render: () => <MeldungenReiter /> },
      ]}
    />
  )
}
