import KiZugriffAbschnitt from '../ki-zugriff/KiZugriffAbschnitt'
import { TabbedCollectionPage } from '../../pages/TabbedCollectionPage'
import { KiVerbinden } from './KiVerbinden'
import { MappeReiter } from './MappeReiter'

/**
 * KI-Assistent — die eine Stelle für alles, was mit einer eigenen KI zu tun hat
 * (Fork AI, 04.10.2026, Wunsch von Bru: „im Menü gibt es etwas mit KI und auch
 * unter Einstellungen — das ist irreführend").
 *
 * Vorher verteilt auf drei Orte: die Mappe unter Wissen (`/berater`), den
 * Zugriff für KI-Assistenten unten in den Einstellungen und die Seite des Grow
 * MCP in der Seitenleiste von Home Assistant. Wie man einen Assistenten
 * überhaupt verbindet, stand nirgends in Grow OS.
 *
 * In Grow OS steckt weiterhin keine KI: die Seite verbindet den Assistenten
 * des Bedieners mit Grow OS, sie bringt keinen mit.
 */
export function KiAssistentSeite() {
  return (
    <TabbedCollectionPage
      eyebrow="Einrichtung"
      title="KI-Assistent"
      subtitle="Grow OS rechnet selbst. Hier verbindest du deinen eigenen Assistenten — Claude oder ChatGPT, mit deinem Konto — und legst fest, was er darf."
      tabs={[
        { key: 'verbinden', label: 'Verbinden', render: () => <KiVerbinden /> },
        { key: 'zugriff', label: 'Zugriff & Schlüssel', render: () => <KiZugriffAbschnitt /> },
        { key: 'mappe', label: 'Mappe', render: () => <MappeReiter /> },
      ]}
    />
  )
}
