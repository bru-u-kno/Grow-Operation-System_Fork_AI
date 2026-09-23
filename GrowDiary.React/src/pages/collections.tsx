import AutomationPage from './AutomationPage'
import ZielwertePage from './ZielwertePage'
import { TabbedCollectionPage } from './TabbedCollectionPage'
import { PlanReiter } from '../features/zielwerte/PlanReiter'
import { MeldungenReiter } from '../features/meldungen/MeldungenReiter'
import { WochenZeile } from '../features/zielwerte/WochenZeile'
import { AppEinrichten } from './MobilePage'
import { V1Page } from '../components/v1'
import { KontextSprung, PlanKette, PushStand } from '../features/zielwerte/PlanKette'

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
      subtitle="Wann Grow OS von selbst misst. Grenzwerte und Push aufs Handy haben eigene Menüpunkte."
      tabs={[
        { key: 'automatik', label: 'Auto-Messungen', render: () => <AutomationPage /> },
      ]}
    />
  )
}

/**
 * Fork AI (forkai.133): „Ziele & Meldungen" ist in drei Menüpunkte zerlegt —
 * Plan (Pflanzen), Grenzwerte (Betrieb), Handy (Einrichtung). Eine Seite mit drei
 * Reitern brauchte einen Oberbegriff, und keiner passte; „Alarm" und „Meldung"
 * waren obendrein verwechselbar (Bru, 22.09.2026). Die drei Seiten sind über die
 * Kette „1 · Plan › 2 · Grenzwerte › 3 · Handy" und Sprünge an Ort und Stelle
 * verbunden.
 */
export function PlanSeite() {
  return (
    <V1Page eyebrow="Pflanzen" title="Plan" subtitle="Die Ziele deines Grows, Woche für Woche.">
      <PlanKette aktiv="plan" />
      <WochenZeile />
      <PlanReiter />
    </V1Page>
  )
}

export function GrenzwerteSeite() {
  return (
    <V1Page eyebrow="Betrieb" title="Grenzwerte" subtitle="Ab wann ein Wert kritisch ist — je Messgröße, tags und nachts.">
      <PlanKette aktiv="grenzwerte" />
      <PushStand />
      <WochenZeile />
      <ZielwertePage />
      <KontextSprung text="Ziele dieser Woche ändern" to="/plan" label="Plan" audit="grenzwerte-zum-plan" />
    </V1Page>
  )
}

export function HandySeite() {
  return (
    <TabbedCollectionPage
      eyebrow="Einrichtung"
      title="Handy"
      subtitle="Was aufs Handy kommt — und wie Grow OS aufs Handy kommt."
      kopf={<PlanKette aktiv="handy" />}
      tabs={[
        { key: 'push', label: 'Push', render: () => <MeldungenReiter /> },
        { key: 'app', label: 'App einrichten', render: () => <AppEinrichten /> },
      ]}
    />
  )
}
