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
  /** Wie viel seit dieser Füllung gebucht wurde. */
  verbraucht: number
  /** `gemessen` aus gebuchten Verbräuchen, `geschaetzt` aus früheren Laufzeiten. */
  quelle: string
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
  /**
   * Wohin die Kosten zählen (forkai.90).
   * true = die Füllung ist lagerneutral, erst der gebuchte Verbrauch trifft den
   * Durchgang. false = die Füllung zählt voll im Durchgang, dem sie zugeordnet
   * ist (bisheriges Verhalten, Voreinstellung).
   */
  aufGrowBuchen: boolean
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
  /** forkai.157: null = einmalig im Grow; sonst über so viele Monate auf alle laufenden Grows verteilt */
  nutzungsdauerMonate: number | null
  /** Nur Grows in diesem Zelt tragen einen Anteil; null = alle */
  tentId: number | null
  zeltName: string | null
  /** Vorzeitig außer Betrieb; der Rest fällt auf die Grows dieses Tages */
  ausgemustertAmUtc: string | null
  /** Wo der Preis bisher gelandet ist — null bei einmaligen Anschaffungen */
  verteilung: KostenVerteilung | null
  /** Was die Anschaffung den gezeigten Grow kostet: einmalig voll, verteilt sein Anteil */
  imGrowEur: number
}

/** forkai.157: Spiegel von KostenVerteilung (KostenSeiteService.cs). Verteilt + Leerlauf + Offen = Gesamtpreis. */
export type KostenVerteilung = {
  /** Letzter Tag der Nutzungsdauer, einschließlich */
  letzterTag: string
  ausgemustertTag: string | null
  eurProTag: number
  verteiltEur: number
  /** Tage ohne laufenden Grow — die trägt niemand */
  leerlaufEur: number
  /** Bei Ausmusterung umgelegter Rest */
  restwertEur: number
  /** Noch nicht verteilt, weil die Tage erst kommen */
  offenEur: number
  anzahlGrows: number
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
  /** Bekannte Hersteller aus Artikeln, Anschaffungen und Hardware — für den Vorschlag beim Tippen */
  hersteller: string[]
  /** Bekannte Produkte mit Hersteller — für den Vorschlag beim Tippen */
  produkte: Array<{ hersteller: string | null; produkt: string }>
  /** forkai.157: Zelte für „nur Grows in diesem Zelt" — auch archivierte, an denen noch eine Anschaffung hängen kann */
  zelte: Array<{ id: number; name: string; archiviert: boolean }>
  /** forkai.157: Höchste Nutzungsdauer in Monaten — vom Backend, damit die Grenze an einer Stelle steht */
  maxNutzungsdauerMonate: number
}

/** „Lager" — ausdrücklich keinem Grow zugeordnet. Als Select-Wert, weil ein <option> keinen null-Wert tragen kann. */
export const LAGER = 'lager'

/**
 * forkai.157: „Auf alle Grows verteilen" — die Anschaffung gehört keinem
 * einzelnen Grow, sondern verteilt sich über ihre Nutzungsdauer.
 */
export const VERTEILT = 'verteilt'

/**
 * Monate addieren wie .NET `DateTime.AddMonths` — am Monatsende wird gekappt:
 * 31.01. + 1 Monat = 28.02., nicht 03.03. wie `Date.setMonth`. Die Vorschau im
 * Formular muss dieselbe Tageszahl ergeben wie AnschaffungVerteilung im Backend,
 * sonst nennt sie einen anderen Betrag je Tag als die Tabelle danach.
 */
export function plusMonate(datum: Date, monate: number): Date {
  const ziel = new Date(datum.getFullYear(), datum.getMonth() + monate, 1, datum.getHours(), datum.getMinutes())
  const letzterTag = new Date(ziel.getFullYear(), ziel.getMonth() + 1, 0).getDate()
  ziel.setDate(Math.min(datum.getDate(), letzterTag))
  return ziel
}

/** Nutzungsdauer lesbar: „3 Jahre", „18 Monate", „1 Jahr". */
export function dauerText(monate: number): string {
  if (monate % 12 === 0) return monate === 12 ? '1 Jahr' : `${monate / 12} Jahre`
  return monate === 1 ? '1 Monat' : `${monate} Monate`
}

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
