import { defineConfig, devices } from '@playwright/test'
import { geraeteFuer } from './e2e/geraete/geraete'

// Smoke-focused Playwright config. Runs the built app via `vite preview` on a fixed
// port so the tests exercise the real production bundle. The backend/API is not
// running, so pages render their loading/error/empty states — which is exactly what
// we want to smoke-test: every route must render without an uncaught render crash.
const PORT = 4173

// GROW_OS_URL zeigt auf eine LAUFENDE App mit Backend — dann wird kein
// eigener Server gestartet. Ohne die Variable bleibt alles wie bisher: der
// statische Server ohne API.
//
// Nötig geworden fuer `demobestand-vollstaendig.spec.ts`: die Pruefung, ob
// eine Seite im Demobestand leer dasteht, ergibt ohne Backend keinen Sinn —
// dort ist jede Seite leer. Ein erster Anlauf hat 23 von 24 Faellen still
// uebersprungen, weil die Adresse gar nicht ankam.
const LAUFENDE_APP = process.env.GROW_OS_URL
const BASE_URL = LAUFENDE_APP ?? `http://localhost:${PORT}`

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['github'], ['list']] : 'list',
  use: {
    baseURL: BASE_URL,
    trace: 'on-first-retry',
  },
  projects: [
    // Die Gerätematrix läuft nur in ihren eigenen Projekten (`GERAETE=1`, siehe e2e/geraete/geraete.ts), nicht zusätzlich hier.
    { name: 'chromium', use: { ...devices['Desktop Chrome'] }, testIgnore: /(geraetematrix|umbruch)\.spec\.ts/ },
    // Die Umbruchprüfung läuft nur mit `UMBRUCH=1` (rot, solange es Funde gibt; siehe e2e/umbruch.spec.ts).
    ...(process.env.UMBRUCH ? [{ name: 'umbruch', use: { ...devices['Desktop Chrome'] }, testMatch: /umbruch\.spec\.ts/ }] : []),
    ...geraeteFuer(process.env.GERAETE).map((g) => ({ name: `geraet-${g.name}`, use: g.use, testMatch: /geraetematrix\.spec\.ts/ })),
  ],
  // Gegen eine laufende App keinen zweiten Server hochziehen.
  webServer: LAUFENDE_APP ? undefined : {
    // Build the real production bundle (into GrowDiary.Web/wwwroot), then serve it
    // through a static server that injects <base href="/"> exactly like the backend.
    command: `npm run build && node e2e/preview-server.mjs ${PORT}`,
    url: BASE_URL,
    reuseExistingServer: !process.env.CI,
    timeout: 180_000,
  },
})
