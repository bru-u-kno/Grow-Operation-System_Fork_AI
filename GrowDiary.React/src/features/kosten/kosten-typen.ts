/* Fork AI (forkai.6): Typen der Kosten-Seite — Spiegel von
   GrowDiary.Web/Services/KostenSeiteService.cs (KostenSeite) und
   Api/Controllers/KostenApiController.cs. */

export type KostenGrowInfo = {
  id: number
  name: string
  startDate: string
  endDate: string | null
  tag: number
  phase: string
  pflanzen: number | null
}

export type KostenSumme = {
  gesamtEur: number
  stromEur: number | null
  artikelEur: number
  anschaffungenEur: number
  proTagEur: number | null
  proPflanzeEur: number | null
  prognoseErnteEur: number | null
  prognoseHinweis: string | null
}

export type KostenPhase = {
  phase: string
  label: string
  vonUtc: string
  bisUtc: string
  tage: number
  kwh: number
  eur: number | null
  laeuft: boolean
}

export type KostenStrom = {
  eingerichtet: boolean
  zaehlerEntityId: string | null
  leistungEntityId: string | null
  preisCentProKwh: number | null
  leistungW: number | null
  kwhSeitStart: number | null
  eurSeitStart: number | null
  kwhProTag: number | null
  eurProTag: number | null
  zaehlerStart: number | null
  zaehlerAktuell: number | null
  ersterStandUtc: string | null
  letzterStandUtc: string | null
  hinweis: string
  phasen: KostenPhase[]
}

export type KostenFuellungAktuell = {
  id: number
  zeitpunktUtc: string
  menge: number
  kostenEur: number | null
  tag: number
  prognoseTage: number | null
  prognoseLeerAmUtc: string | null
  fuellstandProzent: number | null
  eurProTag: number | null
}

export type KostenArtikel = {
  id: number
  /** Anzeigename */
  name: string
  hersteller: string | null
  produkt: string | null
  /** Preis eines vollen Gebindes — belegt die Kosten beim Erfassen vor */
  preisEur: number | null
  einheit: string
  gebinde: number | null
  tentId: number | null
  notiz: string | null
  aktiv: boolean
  aktuell: KostenFuellungAktuell | null
  anzahlFuellungen: number
  mittlereLaufzeitTage: number | null
  summeEurImGrow: number
}

export type KostenNachfuellung = {
  id: number
  artikelId: number
  artikelName: string
  einheit: string
  zeitpunktUtc: string
  menge: number
  kostenEur: number | null
  leerAmUtc: string | null
  laufzeitTage: number | null
  eurProTag: number | null
  growId: number | null
  growName: string | null
  notiz: string | null
}

export type KostenAnschaffung = {
  id: number
  name: string
  hersteller: string | null
  produkt: string | null
  datumUtc: string
  stueck: number
  einzelpreisEur: number
  gesamtEur: number
  growId: number | null
  growName: string | null
  notiz: string | null
  hardwareItemId: number | null
}

export type KostenDurchgang = {
  growId: number
  name: string
  startDate: string
  endDate: string | null
  laeuft: boolean
  stromEur: number | null
  artikelEur: number
  anschaffungenEur: number
  gesamtEur: number | null
}

export type KostenSeite = {
  grow: KostenGrowInfo | null
  summe: KostenSumme
  strom: KostenStrom
  artikel: KostenArtikel[]
  nachfuellungen: KostenNachfuellung[]
  anschaffungen: KostenAnschaffung[]
  durchgaenge: KostenDurchgang[]
  /** Erlaubte Einheiten für Verbrauchsartikel — vom Backend, damit beide Seiten dieselbe Liste haben */
  einheiten: string[]
}

/** „Lager" — ausdrücklich keinem Grow zugeordnet. Als Select-Wert, weil ein <option> keinen null-Wert tragen kann. */
export const LAGER = 'lager'

/** Auswahl „Für Grow": alle laufenden Grows plus Lager. */
export function growOptionen(seite: KostenSeite): Array<{ value: string; label: string }> {
  const laufend = seite.durchgaenge.filter((d) => d.laeuft).map((d) => ({ value: String(d.growId), label: d.name }))
  return [...laufend, { value: LAGER, label: 'Lager — noch keinem Grow zugeordnet' }]
}

export type StromQuelle = {
  zaehlerEntityId: string | null
  leistungEntityId: string | null
}

export type Zaehlerstand = {
  id: number
  zeitpunktUtc: string
  kwh: number
  anlass: 'Tag' | 'GrowStart' | 'Phase' | 'Manuell'
  growId: number | null
  phase: string | null
}

export type EntitaetTest = {
  entityId: string
  gefunden: boolean
  state: string | null
  wert: number | null
  einheit: string | null
  name: string | null
}

/** Euro mit zwei Nachkommastellen, deutsch. `null` → Gedankenstrich. */
export function euro(wert: number | null | undefined): string {
  if (wert == null || Number.isNaN(wert)) return '–'
  return `${new Intl.NumberFormat('de-DE', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(wert)} €`
}

/** Tage als ganze Zahl — „42 d“, nie „41,98 d“. */
export function tage(wert: number | null | undefined): string {
  if (wert == null || Number.isNaN(wert)) return '–'
  return `${Math.round(wert)} d`
}
