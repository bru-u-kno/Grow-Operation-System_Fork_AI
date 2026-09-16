import AutomationPage from './AutomationPage'
import SetpointProfilesPage from './SetpointProfilesPage'
import ZielwertePage from './ZielwertePage'
import AlertsPage from './AlertsPage'
import NotificationsPage from './NotificationsPage'
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
  return (
    <TabbedCollectionPage
      eyebrow="Betrieb / Regeln"
      title="Regeln & Automatik"
      subtitle="Grenzwerte, Auto-Messungen und Benachrichtigungen an einem Ort. Zwei Automatiken sitzen dort, wo sie wirken: die Dosierung bei den Pumpen und die Wassertemperatur unter Crop Steering."
      tabs={[
        { key: 'grenzwerte', label: 'Grenzwerte', render: () => <AlertsPage /> },
        { key: 'automatik', label: 'Auto-Messungen', render: () => <AutomationPage /> },
        { key: 'push', label: 'Benachrichtigungen', render: () => <NotificationsPage /> },
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
      title="Zielwerte"
      subtitle="Was gerade gilt, woher es kommt und wo man es ändert. Vier Quellen stehen hintereinander — jede spätere sticht die früheren."
      tabs={[
        { key: 'jetzt', label: 'Werte', render: () => <ZielwertePage /> },
        { key: 'plan', label: 'Plan', render: () => <PlanReiter /> },
        { key: 'meldungen', label: 'Meldungen', render: () => <MeldungenReiter /> },
        { key: 'profile', label: 'Profile', render: () => <SetpointProfilesPage /> },
        { key: 'grenzwerte', label: 'Grenzwerte', render: () => <AlertsPage /> },
      ]}
    />
  )
}
