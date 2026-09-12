import { Navigate, Route, Routes, useLocation } from 'react-router-dom'
import AddbackHubPage from './pages/AddbackHubPage'
import AddbackPage from './pages/AddbackPage'
import { GrowScopedSectionPage } from './pages/GrowScopedSectionPage'
import GettingStartedPage from './pages/GettingStartedPage'
import DosingPage from './pages/DosingPage'
import SetpointProfilesPage from './pages/SetpointProfilesPage'
import { CropSteeringPage } from './pages/CropSteeringPage'
import { AcTestPage } from './pages/AcTestPage'
import MobilePage from './pages/MobilePage'
import DosingPumpSetupPage from './pages/DosingPumpSetupPage'
import GrowDetailPage from './pages/GrowDetailPage'
import GrowsPage from './pages/GrowsPage'
import GrowSetupPage from './pages/GrowSetupPage'
import WasserwechselPage from './pages/WasserwechselPage'
import HardwarePage from './pages/HardwarePage'
import HarvestPage from './pages/HarvestPage'
import HomeAssistantPage from './pages/HomeAssistantPage'
import HydroDetailPage from './pages/HydroDetailPage'
import HydroPage from './pages/HydroPage'
import HydroEditorPage from './features/hydro/HydroEditorPage'
import KnowledgePage from './pages/KnowledgePage'
import ShoppingListPage from './pages/ShoppingListPage'
import CuringPage from './pages/CuringPage'
import KostenPage from './pages/KostenPage'
import SteuerungPage from './pages/SteuerungPage'
import GeraetePage from './pages/GeraetePage'
import SteuerungGeraetePage from './pages/SteuerungGeraetePage'
import LiveDashboardPage from './pages/LiveDashboardPage'
import ManualMeasurementPage from './pages/ManualMeasurementPage'
import MeasurementEditPage from './pages/MeasurementEditPage'
import MobileActionPage from './pages/MobileActionPage'
import ReleasePage from './pages/ReleasePage'
import SettingsPage from './pages/SettingsPage'
import TentDetailPage from './pages/TentDetailPage'
import TentsPage from './pages/TentsPage'
// Reihenfolge zaehlt: die Konventionen zuerst, dann die Seitenregeln, die sie
// fuer ihre Seite praezisieren. Alle ungeschichtet, wie sie es in rc2-overrides
// auch waren.
import './styles/conventions.css'
import './features/tents/tents.css'
import './features/measurement/measurement.css'
import './features/grows/grows.css'
import './features/addback/addback.css'
import './features/live/live-rc2.css'
import './features/grows/grows-rc2.css'
import './styles/primitives-rc2.css'
import './styles/widgets.css'
// Fork AI: Titelzeile, Icon-Leiste, Erfassen-Blatt. Zuletzt, damit die Masse
// dieser Datei die aus shell.css ueberschreiben.
import './styles/forkai-shell.css'

import { AppShell } from './AppShell'
import { legacyRedirects } from './navigation'
import { useNavCounts } from './useNavCounts'
import { RulesCollectionPage } from './pages/collections'
import AdvisorPage from './pages/AdvisorPage'
import WaterProfilePage from './pages/WaterProfilePage'
import StrainsPage from './pages/StrainsPage'
import ArchivePage from './pages/ArchivePage'

/**
 * Weiterleitung, die die Adresszeile nicht halbiert.
 *
 * `<Navigate to="/regeln" />` wirft die Suchparameter weg. Für die Grow-Detailseite,
 * die auf `/automatik?growId=3` verlinkt, hiesse das: richtige Seite, falscher Grow —
 * und weil die Seite dann einfach den ersten Grow zeigt, sieht das nicht nach einem
 * Fehler aus. Das Ziel bringt sein eigenes `?tab=` mit, deshalb werden beide
 * Parametersätze zusammengeführt statt einer überschrieben.
 */
function LegacyRedirect({ to }: { to: string }) {
  const { search } = useLocation()
  const [path, targetQuery] = to.split('?')
  const params = new URLSearchParams(targetQuery ?? '')
  for (const [key, value] of new URLSearchParams(search)) {
    if (!params.has(key)) params.set(key, value)
  }
  const query = params.toString()
  return <Navigate to={query ? `${path}?${query}` : path} replace />
}

function App() {
  const counts = useNavCounts()

  return (
    <AppShell counts={counts}>
      <Routes>
          <Route path="/" element={<LiveDashboardPage />} />
          <Route path="/live" element={<Navigate to="/" replace />} />
          <Route path="/addback" element={<AddbackHubPage />} />
          <Route path="/wasserwechsel" element={<WasserwechselPage />} />
          <Route path="/aufgaben" element={<MobileActionPage />} />
          <Route path="/action" element={<Navigate to="/aufgaben" replace />} />
          <Route path="/grows" element={<GrowsPage />} />
          <Route path="/grows/new" element={<GrowSetupPage />} />
          <Route path="/messung" element={<ManualMeasurementPage />} />
          <Route path="/messungen/new" element={<Navigate to="/messung" replace />} />
          <Route path="/grows/:growId" element={<GrowDetailPage />} />
          <Route path="/grows/:growId/setup" element={<GrowSetupPage />} />
          <Route path="/grows/:growId/addback" element={<AddbackPage />} />
          <Route path="/grows/:growId/harvest" element={<HarvestPage />} />
          <Route path="/grows/measurements/:measurementId/edit" element={<MeasurementEditPage />} />
          <Route path="/zelte" element={<TentsPage />} />
          <Route path="/zelte/new" element={<TentsPage />} />
          <Route path="/zelte/:tentId" element={<TentDetailPage />} />
          <Route path="/hydro" element={<HydroPage />} />
          {/* Anlegen und Bearbeiten laufen ueber eine Seite statt fuenf Wizard-Schritte. */}
          <Route path="/hydro/new" element={<HydroEditorPage />} />
          <Route path="/hydro/:id/edit" element={<HydroEditorPage />} />
          <Route path="/hydro/:setupId" element={<HydroDetailPage />} />
          <Route path="/home-assistant" element={<HomeAssistantPage />} />
          <Route path="/handy" element={<MobilePage />} />
          <Route path="/messungen" element={<GrowScopedSectionPage title="Messungen" section="measurements" />} />
          <Route path="/diagnose" element={<GrowScopedSectionPage title="Diagnose" section="diagnosis" />} />
          <Route path="/journal" element={<GrowScopedSectionPage title="Journal & Fotos" section="journal" />} />
          <Route path="/sops" element={<GrowScopedSectionPage title="SOPs" section="sops" />} />
          <Route path="/wissen" element={<KnowledgePage />} />
          <Route path="/einkaufsliste" element={<ShoppingListPage />} />
          <Route path="/aushaerten" element={<CuringPage />} />
          <Route path="/release" element={<ReleasePage />} />
          <Route path="/settings" element={<SettingsPage />} />
          <Route path="/start" element={<GettingStartedPage />} />
          <Route path="/einstellungen" element={<Navigate to="/settings" replace />} />
        
          <Route path="/sensoren" element={<HardwarePage />} />

          <Route path="/sollwerte" element={<SetpointProfilesPage />} />
          <Route path="/cropsteering" element={<CropSteeringPage />} />
          <Route path="/kosten" element={<KostenPage />} />
          <Route path="/steuerung" element={<SteuerungPage />} />
          <Route path="/steuerung/geraete" element={<SteuerungGeraetePage />} />
          <Route path="/geraete" element={<GeraetePage />} />
          <Route path="/steuerung/:modul" element={<SteuerungPage />} />
          <Route path="/ac-test" element={<AcTestPage />} />
          <Route path="/wasser" element={<WaterProfilePage />} />

          {/* Dosierung: Liste, neu, bearbeiten. Reihenfolge zaehlt — „neu" muss
              vor „:pumpId" stehen, sonst wird es als Id gelesen. */}
          <Route path="/dosierung" element={<DosingPage />} />
          <Route path="/dosierung/neu" element={<DosingPumpSetupPage />} />
          <Route path="/dosierung/:pumpId" element={<DosingPumpSetupPage />} />

          {/* Sammelseiten: verwandte Bereiche unter Tabs statt als eigene Menuepunkte. */}
          <Route path="/regeln" element={<RulesCollectionPage />} />
          <Route path="/sorten" element={<StrainsPage />} />
          <Route path="/berater" element={<AdvisorPage />} />
          <Route path="/archiv" element={<ArchivePage />} />

          {/* Alte Pfade bleiben gueltig — Lesezeichen und Links aus HA-Dashboards. */}
          {Object.entries(legacyRedirects).map(([from, to]) => (
            <Route key={from} path={from} element={<LegacyRedirect to={to} />} />
          ))}
        </Routes>
    </AppShell>
  )
}

export default App
