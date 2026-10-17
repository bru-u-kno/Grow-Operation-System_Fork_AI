import { devices, type PlaywrightTestConfig } from '@playwright/test'

/**
 * Die Gerätematrix — welche Endgeräte die Oberflächenprüfung nachstellt.
 *
 * <b>Der Anlass (05.10.2026).</b> Bis dahin sah jede Prüfung die App nur in
 * Desktop-Chromium, ab und zu auf 390 px verkleinert. Der Nutzer öffnet sie aber in
 * der HA-App am Handy, und andere Benutzer mit iPhone (WKWebView, also
 * WebKit), Tablet oder Firefox sahen gar nichts davon. F-049 (Sprung unter die
 * Kopfleiste landet in der HA-App zu tief) war in Chromium unsichtbar.
 *
 * Plan und Begründung: Vault „Home Assistant/Fork AI/2026-10-05 Geräteprüfung".
 *
 * Jedes Gerät ist ein Playwright-Projekt. Die Projekte laufen nur, wenn
 * `GERAETE` gesetzt ist — sonst bliebe der gewohnte Lauf (`npx playwright
 * test`) auf dem Rechner ohne WebKit/Firefox stehen.
 *
 * - `GERAETE=1` — die **Chromium-Geräte** (Android-Handys, Android-Tablets, Desktop). Lokal und auf
 *   der VM lauffähig, denn dort ist Chromium installiert.
 * - `GERAETE=alle` — zusätzlich **WebKit** (iPhone, iPad) und **Firefox**. Dafür müssen die Browser
 *   installiert sein (`npx playwright install webkit firefox`); auf der VM geht das ohne sudo nicht.
 *   Das ist der zweite Schritt des Plans (Vault „Home Assistant/Fork AI/2026-10-05 Geräteprüfung").
 */
export type Geraet = {
  /** Projektname — steht im Bericht und im Pfad der Bildschirmfotos. */
  name: string
  /** Wofür das Gerät steht. */
  steht_fuer: string
  use: NonNullable<PlaywrightTestConfig['use']>
}

const chromiumTablet = (breite: number, hoehe: number) => ({
  ...devices['Galaxy Tab S4'],
  viewport: { width: breite, height: hoehe },
  screen: { width: breite, height: hoehe },
})

export const GERAETE: Geraet[] = [
  { name: 'android-360', steht_fuer: 'kleines Android-Handy (Galaxy S8, Chromium)', use: { ...devices['Galaxy S8'] } },
  { name: 'android-412', steht_fuer: 'großes Android-Handy (Pixel 7, Chromium)', use: { ...devices['Pixel 7'] } },
  { name: 'iphone-375', steht_fuer: 'kleines iPhone (SE 3. Gen., WebKit wie die HA-iOS-App)', use: { ...devices['iPhone SE (3rd gen)'] } },
  { name: 'iphone-430', steht_fuer: 'großes iPhone (15 Pro Max, WebKit)', use: { ...devices['iPhone 15 Pro Max'] } },
  { name: 'tablet-768-webkit', steht_fuer: 'iPad hochkant', use: { ...devices['iPad Mini'] } },
  { name: 'tablet-1024-webkit', steht_fuer: 'iPad quer', use: { ...devices['iPad Mini landscape'] } },
  { name: 'tablet-768-chromium', steht_fuer: 'Android-Tablet hochkant', use: chromiumTablet(768, 1024) },
  { name: 'tablet-1024-chromium', steht_fuer: 'Android-Tablet quer', use: chromiumTablet(1024, 768) },
  { name: 'desktop-1280-chromium', steht_fuer: 'Laptop, Chrome/Edge', use: { ...devices['Desktop Chrome'] } },
  { name: 'desktop-1920-chromium', steht_fuer: 'großer Bildschirm, Chrome/Edge', use: { ...devices['Desktop Chrome'], viewport: { width: 1920, height: 1080 } } },
  { name: 'desktop-1280-firefox', steht_fuer: 'Laptop, Firefox', use: { ...devices['Desktop Firefox'] } },
  { name: 'desktop-1920-firefox', steht_fuer: 'großer Bildschirm, Firefox', use: { ...devices['Desktop Firefox'], viewport: { width: 1920, height: 1080 } } },
]

/**
 * Wie die Seite gezeigt wird. Nicht jede Kombination — vier Fassungen je Seite
 * und Gerät decken jede Achse einmal ab, ohne die Laufzeit zu verachtfachen.
 */
export type Fassung = {
  name: string
  thema: 'light' | 'dark'
  /** Schriftgröße in Prozent der Grundeinstellung. */
  schrift: number
  /** Im HA-Rahmen (iframe wie über Ingress) statt direkt. */
  haRahmen: boolean
}

export const FASSUNGEN: Fassung[] = [
  { name: 'hell', thema: 'light', schrift: 100, haRahmen: false },
  { name: 'dunkel', thema: 'dark', schrift: 100, haRahmen: false },
  // „Große Schrift" in den Bedienungshilfen: Android reicht sie als Textzoom an
  // die WebView der HA-App weiter, Chrome und Firefox haben eine eigene
  // Schriftgröße. Beides hebt die Grundschrift — die App rechnet durchweg in rem
  // (`--fs-*` in tokens.css), also wächst jede Schrift mit.
  { name: 'gross', thema: 'light', schrift: 200, haRahmen: false },
  { name: 'ha-rahmen', thema: 'dark', schrift: 100, haRahmen: true },
]

/** Engines, nach denen die CI die Matrix auf Rechner verteilt. */
export function engine(geraet: Geraet): 'chromium' | 'webkit' | 'firefox' {
  return (geraet.use.defaultBrowserType ?? 'chromium') as 'chromium' | 'webkit' | 'firefox'
}

/** Welche Geräte zu `GERAETE` gehören: `1` nur Chromium, `alle` auch WebKit und Firefox. */
export function geraeteFuer(auswahl: string | undefined): Geraet[] {
  if (!auswahl) return []
  return auswahl === 'alle' ? GERAETE : GERAETE.filter((g) => engine(g) === 'chromium')
}
