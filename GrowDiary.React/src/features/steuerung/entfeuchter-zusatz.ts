/**
 * A-009: Rechenteil der Seite „Zusatz-Entfeuchter" — ohne React, damit er sich
 * ohne Browser prüfen lässt.
 *
 * <b>Warum so viel davon rein ist.</b> Am 06.10.2026 hat ein Speichern
 * unbemerkt die Tag-Grenze von 26,5 auf 29 °C zurückgesetzt: die Seite schickte
 * den ganzen Entwurf, und ein Feld, das der Nutzer nie angefasst hatte, trug
 * einen alten Wert. Deshalb steht hier, was geschickt wird
 * ({@link aenderungBilden}), was der gelbe Kasten davor ankündigt
 * ({@link aenderungsListe}) und wie ein Entwurf einen frischen Stand überlebt
 * ({@link entwurfNachfuehren}) — jeweils aus demselben Vergleich mit dem
 * zuletzt geladenen Stand.
 */
import { tempMax, zahl } from './entfeuchter-band'
import type {
  EntfeuchterName,
  EntfeuchterNamen,
  EntfeuchterNamenAenderung,
  EntfeuchterZusatzAenderung,
  EntfeuchterZusatzEinstellungen,
  EntfeuchterZusatzLive,
  TempMaxModus,
  ZusatzHilfe,
  ZusatzMeldung,
} from './steuerung-typen'

type Einstellungen = EntfeuchterZusatzEinstellungen

// ------------------------------------------------------------ Hilfsstärke

/** Die fünf Einzelwerte, die eine Hilfsstärke setzt (ENTSCHEIDUNGEN.md, Punkt 3). */
export type Voreinstellung = {
  folgeAbstandK: number
  wiederEinAbstandK: number
  vpdHystereseKpa: number
  zuschaltVerzoegerungMin: number
  mindestpauseMin: number
}

export type HilfeStufe = 'sparsam' | 'normal' | 'kraeftig'

/** Wortlaut aus ENTSCHEIDUNGEN.md: sparsam {1,5; 1; 0,25; 20; 15} · normal {1; 1; 0,15; 10; 10} · kräftig {0,5; 0,5; 0,10; 5; 10}. */
export const HILFE_VORGABEN: Record<HilfeStufe, Voreinstellung> = {
  sparsam: { folgeAbstandK: 1.5, wiederEinAbstandK: 1, vpdHystereseKpa: 0.25, zuschaltVerzoegerungMin: 20, mindestpauseMin: 15 },
  normal: { folgeAbstandK: 1, wiederEinAbstandK: 1, vpdHystereseKpa: 0.15, zuschaltVerzoegerungMin: 10, mindestpauseMin: 10 },
  kraeftig: { folgeAbstandK: 0.5, wiederEinAbstandK: 0.5, vpdHystereseKpa: 0.1, zuschaltVerzoegerungMin: 5, mindestpauseMin: 10 },
}

/** Die Stufen in der Reihenfolge der Knöpfe; „eigene Werte" ist keine Wahl, sondern ein Befund. */
/** Die drei Stufen für „Wie ruhig soll er schalten?“ (VPD-Abstand, kPa). */
export const VPD_STUFEN: ReadonlyArray<{ wert: number; label: string }> = [
  { wert: 0.1, label: 'knapp' },
  { wert: 0.15, label: 'normal' },
  { wert: 0.25, label: 'ruhig' },
]

export const HILFE_STUFEN: ReadonlyArray<{ wert: 'aus' | HilfeStufe; label: string }> = [
  { wert: 'aus', label: 'aus' },
  { wert: 'sparsam', label: 'sparsam' },
  { wert: 'normal', label: 'normal' },
  { wert: 'kraeftig', label: 'kräftig' },
]

const VORGABE_FELDER = Object.keys(HILFE_VORGABEN.normal) as Array<keyof Voreinstellung>

/** Die Hilfsstärke, die zu den Einzelwerten passt — sonst `eigene`. */
export function hilfeErkennen(e: Voreinstellung): HilfeStufe | 'eigene' {
  const treffer = (Object.keys(HILFE_VORGABEN) as HilfeStufe[])
    .find((stufe) => VORGABE_FELDER.every((feld) => gleich(e[feld], HILFE_VORGABEN[stufe][feld])))
  return treffer ?? 'eigene'
}

/**
 * Eine Hilfsstärke wählen.
 *
 * <b>Sparsam, normal, kräftig</b> setzen die fünf Einzelwerte. <b>Aus</b> lässt
 * sie so, wie sie gespeichert sind: wer von „sparsam" auf „aus" zurückspringt,
 * will nur „aus" speichern und nicht nebenbei die Einzelwerte von „sparsam".
 */
export function hilfeWaehlen(entwurf: Einstellungen, geladen: Einstellungen, hilfe: 'aus' | HilfeStufe): Einstellungen {
  if (hilfe === 'aus') {
    const alt = Object.fromEntries(VORGABE_FELDER.map((feld) => [feld, geladen[feld]]))
    return { ...entwurf, ...alt, hilfe: 'aus' }
  }
  return { ...entwurf, ...HILFE_VORGABEN[hilfe], hilfe }
}

/**
 * Nach dem Ändern eines Einzelwerts: passt er (mit den anderen) zu einer
 * Hilfsstärke, heißt sie so, sonst „eigene Werte". Steht die Hilfe auf „aus",
 * bleibt sie dort — der Zusatz ist aus, was auch immer darunter steht.
 */
export function hilfeNachEinzelwert(entwurf: Einstellungen): Einstellungen {
  if (entwurf.hilfe === 'aus') return entwurf
  return { ...entwurf, hilfe: hilfeErkennen(entwurf) }
}

/** „Wie stark soll er helfen?" in Worten — aus der Tabelle, damit Text und Wert nie auseinanderlaufen. */
export function hilfeText(hilfe: ZusatzHilfe, fuehrungName: string): string {
  if (hilfe === 'aus') return `Nur ${fuehrungName} entfeuchtet. Der Zusatz bleibt aus.`
  if (hilfe === 'eigene') return 'Du hast unter Erweitert eigene Werte eingestellt.'
  const v = HILFE_VORGABEN[hilfe]
  const aus = `geht ${zahl(v.folgeAbstandK)} K früher aus`
  const zu = `nach ${v.zuschaltVerzoegerungMin} min zu`
  if (hilfe === 'sparsam') return `Hilft selten: ${aus} und schaltet erst ${zu}. Wenig Wärme im Zelt.`
  if (hilfe === 'kraeftig') return `Hilft viel: ${aus} und schaltet ${zu}. Mehr Entfeuchtung, mehr Wärme im Zelt.`
  return `Geht ${zahl(v.folgeAbstandK)} K früher aus und schaltet ${zu}. Guter Mittelweg.`
}

// ------------------------------------------------------------ Empfehlung

/** Empfohlener Aufschlag zur Plan-Luft (K), aus dem freigegebenen Mockup (Bru, 06.10.2026): 6,5 tagsüber, 9 nachts. */
export const EMPFOHLEN_ABSTAND_K = { tag: 6.5, nacht: 9 } as const

/** Empfohlene Hilfsstärke. */
export const EMPFOHLEN_HILFE: HilfeStufe = 'normal'

/**
 * „Empfohlen: …" zur Höchsttemperatur, mit dem Feld, das „zurücksetzen" ändert.
 *
 * Im Modus „Plan +" ist es der Aufschlag, im Modus „fest" die Temperatur, die
 * dieser Aufschlag mit dem Plan ergäbe — ohne Plan gibt es nichts zu empfehlen
 * (`null`), statt eine Zahl zu erfinden.
 */
export function hoechstEmpfehlung(
  art: 'tag' | 'nacht',
  e: Einstellungen,
  planC: number | null,
): { text: string; gleich: boolean; feld: keyof Einstellungen; wert: number } | null {
  const rec = EMPFOHLEN_ABSTAND_K[art]
  const Art = art === 'tag' ? 'Tag' : 'Nacht'
  const modus = e[`tempMax${Art}Modus`]
  if (modus === 'plan') {
    return { text: `Empfohlen: +${zahl(rec)} K`, gleich: gleich(e[`tempMax${Art}AbstandK`], rec), feld: `tempMax${Art}AbstandK`, wert: rec }
  }
  if (planC == null) return null
  const ziel = tempMax('plan', rec, 0, planC)
  return { text: `Empfohlen: ${zahl(ziel)} °C`, gleich: gleich(e[`tempMax${Art}FestC`], ziel), feld: `tempMax${Art}FestC`, wert: ziel }
}

// ------------------------------------------------------------ Vergleichen

/** Zahlen gleich, ohne an Gleitkomma-Rauschen zu scheitern; ein leeres Feld (NaN) ist nie gleich. */
export function gleich(a: number, b: number): boolean {
  if (!Number.isFinite(a) || !Number.isFinite(b)) return false
  return Math.abs(a - b) < 1e-9
}

const gleichWert = (a: unknown, b: unknown): boolean =>
  typeof a === 'number' && typeof b === 'number' ? gleich(a, b) : a === b

/** Alle Felder von `einstellungen` ohne die Gruppe `meldung`. */
const EINFACHE_FELDER = [
  'hilfe', 'automatikAktiv', 'tagbetriebErlauben', 'nachtDurchlaufen', 'vpdHystereseKpa', 'zuschaltVerzoegerungMin',
  'folgeAbstandK', 'wiederEinAbstandK', 'mindestlaufzeitMin', 'mindestpauseMin', 'ablauf',
  'tempMaxTagModus', 'tempMaxTagAbstandK', 'tempMaxTagFestC', 'tempMaxNachtModus', 'tempMaxNachtAbstandK', 'tempMaxNachtFestC',
] as const satisfies ReadonlyArray<keyof Einstellungen>

const MELDUNG_FELDER = ['aktiv', 'grenzeW', 'dauerMin', 'wiederholungH'] as const satisfies ReadonlyArray<keyof ZusatzMeldung>

/**
 * Der PUT-Körper: NUR die Felder, die vom geladenen Stand abweichen.
 *
 * <b>Hilfsstärke.</b> „eigene Werte" ist ein Befund des Servers aus den
 * Einzelwerten, keine Wahl, und wird nur geschickt, wenn der Zusatz vorher auf
 * „aus" stand. Eine gewählte Stufe geht mit den Einzelwerten, die sie gesetzt
 * hat, soweit sie sich geändert haben.
 */
export function aenderungBilden(geladen: Einstellungen, entwurf: Einstellungen): EntfeuchterZusatzAenderung {
  const koerper: Record<string, unknown> = {}
  for (const feld of EINFACHE_FELDER) {
    if (gleichWert(geladen[feld], entwurf[feld])) continue
    // „eigene Werte" ist ein Befund des Servers. Nur wer aus „aus" kommt, muss es
    // nennen — sonst bliebe der Zusatz mit den neuen Einzelwerten aus.
    if (feld === 'hilfe' && entwurf.hilfe === 'eigene' && geladen.hilfe !== 'aus') continue
    koerper[feld] = entwurf[feld]
  }
  const meldung: Record<string, unknown> = {}
  for (const feld of MELDUNG_FELDER) {
    if (!gleichWert(geladen.meldung[feld], entwurf.meldung[feld])) meldung[feld] = entwurf.meldung[feld]
  }
  if (Object.keys(meldung).length > 0) koerper.meldung = meldung
  return koerper as EntfeuchterZusatzAenderung
}

/** Hat der Entwurf etwas, das gespeichert werden müsste? */
export function istGeaendert(geladen: Einstellungen, entwurf: Einstellungen): boolean {
  return Object.keys(aenderungBilden(geladen, entwurf)).length > 0
}

/**
 * Ein frischer Stand vom Server, ein angefangener Entwurf.
 *
 * Felder, die der Nutzer nicht angefasst hat (Entwurf = alter Stand), folgen dem
 * neuen Stand; angefasste Felder bleiben. Sonst wäre ein Wert, der inzwischen
 * woanders geändert wurde (Seite „Entfeuchter", Home Assistant), im Entwurf
 * noch der alte — und das nächste Speichern würde ihn zurückschreiben, genau
 * der Fehler vom 06.10.2026.
 */
export function entwurfNachfuehren(alt: Einstellungen, neu: Einstellungen, entwurf: Einstellungen): Einstellungen {
  const ergebnis: Record<string, unknown> = { ...neu }
  for (const feld of EINFACHE_FELDER) {
    if (!gleichWert(alt[feld], entwurf[feld])) ergebnis[feld] = entwurf[feld]
  }
  const meldung: Record<string, unknown> = { ...neu.meldung }
  for (const feld of MELDUNG_FELDER) {
    if (!gleichWert(alt.meldung[feld], entwurf.meldung[feld])) meldung[feld] = entwurf.meldung[feld]
  }
  ergebnis.meldung = meldung
  return ergebnis as Einstellungen
}

/** Leere (NaN) Zahlenfelder, einschließlich der Gruppe `meldung` — mit dem Namen, den ein Feldfehler des Servers trüge. */
export function leereFelder(entwurf: Einstellungen): Record<string, string> | null {
  const leer: Array<[string, string]> = []
  for (const feld of EINFACHE_FELDER) {
    const wert = entwurf[feld]
    if (typeof wert === 'number' && !Number.isFinite(wert)) leer.push([feld.charAt(0).toUpperCase() + feld.slice(1), 'Bitte eine Zahl eintragen.'])
  }
  for (const feld of MELDUNG_FELDER) {
    const wert = entwurf.meldung[feld]
    if (typeof wert === 'number' && !Number.isFinite(wert)) leer.push([`Meldung.${feld.charAt(0).toUpperCase()}${feld.slice(1)}`, 'Bitte eine Zahl eintragen.'])
  }
  return leer.length > 0 ? Object.fromEntries(leer) : null
}

/** Der Entwurf für Anzeigetexte: ein gerade geleertes Feld zeigt dort den gespeicherten Wert. */
export function ohneLueckenZusatz(entwurf: Einstellungen, geladen: Einstellungen): Einstellungen {
  const ergebnis: Record<string, unknown> = { ...entwurf }
  for (const feld of EINFACHE_FELDER) {
    const wert = entwurf[feld]
    if (typeof wert === 'number' && !Number.isFinite(wert)) ergebnis[feld] = geladen[feld]
  }
  const meldung: Record<string, unknown> = { ...entwurf.meldung }
  for (const feld of MELDUNG_FELDER) {
    const wert = entwurf.meldung[feld]
    if (typeof wert === 'number' && !Number.isFinite(wert)) meldung[feld] = geladen.meldung[feld]
  }
  ergebnis.meldung = meldung
  return ergebnis as Einstellungen
}

/**
 * Der Feldfehler des Servers zu einem Feld. Geschachtelte Felder (`meldung.grenzeW`)
 * kommen je nach Schreibweise des Servers als `Meldung.GrenzeW`, `Meldung.grenzeW`
 * oder `MeldungGrenzeW` — gesucht wird in allen, damit keine Markierung ausbleibt.
 */
export function fehlerZu(felder: Record<string, string>, feld: keyof Einstellungen | `meldung.${keyof ZusatzMeldung}`): string | undefined {
  const gross = (t: string) => t.charAt(0).toUpperCase() + t.slice(1)
  const [gruppe, innen] = feld.split('.')
  const kandidaten = innen
    ? [`${gross(gruppe)}.${gross(innen)}`, `${gross(gruppe)}.${innen}`, `${gross(gruppe)}${gross(innen)}`, gross(innen)]
    : [gross(feld)]
  const treffer = kandidaten.find((name) => felder[name] != null)
  return treffer ? felder[treffer] : undefined
}

/** Feldfehler, die keines der Felder der Seite trifft — sie gehören in die Meldung oben, sonst sähe sie niemand. */
export function unbekannteFehler(felder: Record<string, string>): string[] {
  const namen = [...EINFACHE_FELDER, ...MELDUNG_FELDER.map((f) => `meldung.${f}` as const)]
  return Object.entries(felder)
    .filter(([name]) => !namen.some((feld) => fehlerZu({ [name]: 'x' }, feld) != null))
    .map(([name, text]) => `${name}: ${text}`)
}

// ------------------------------------------------------- Der gelbe Kasten

export type AenderungsZeile = {
  /** Feldname im Körper, auch für Tests und Schlüssel. */
  feld: string
  label: string
  von: string
  nach: string
  /** Was die Wahl nebenbei setzt (Hilfsstärke → Einzelwerte). */
  folge?: string
}

const aufAus = (an: boolean) => (an ? 'an' : 'aus')
const modusText = (m: TempMaxModus) => (m === 'plan' ? 'Plan +' : 'fest')

/** Name der Hilfsstärke in der Anzeige. */
export function hilfeLabel(h: ZusatzHilfe): string {
  return h === 'kraeftig' ? 'kräftig' : h === 'eigene' ? 'eigene Werte' : h
}

/** Beschriftung, Einheit und Stellen der Einzelwerte — eine Stelle für Kasten und Oberfläche. */
const EINZEL: Array<{ feld: keyof Voreinstellung; label: string; einheit: string; stellen: number }> = [
  { feld: 'folgeAbstandK', label: 'Zusatz geht früher aus', einheit: 'K', stellen: 1 },
  { feld: 'wiederEinAbstandK', label: 'Wieder einschalten erst', einheit: 'K', stellen: 1 },
  { feld: 'vpdHystereseKpa', label: 'VPD-Abstand', einheit: 'kPa', stellen: 2 },
  { feld: 'zuschaltVerzoegerungMin', label: 'Zuschalten nach', einheit: 'min', stellen: 0 },
  { feld: 'mindestpauseMin', label: 'Mindestpause', einheit: 'min', stellen: 0 },
]

/**
 * Was „Wird gespeichert — nur das:" auflistet — genau die Felder aus
 * {@link aenderungBilden}, in Worten. Zum Körper passt jede Zeile, zu jedem
 * Feld im Körper gibt es eine Zeile (die Einzelwerte einer gewählten Hilfsstärke
 * stehen als `folge` in deren Zeile).
 *
 * @param plan Plan-Luft Tag/Nacht (°C) für die Höchsttemperatur im Modus „Plan +".
 */
export function aenderungsListe(
  geladen: Einstellungen,
  entwurf: Einstellungen,
  plan: { tag: number | null; nacht: number | null },
): AenderungsZeile[] {
  const zeilen: AenderungsZeile[] = []
  const anders = (feld: keyof Einstellungen) => !gleichWert(geladen[feld], entwurf[feld])

  // Höchsttemperatur: Tag und Nacht.
  for (const art of ['Tag', 'Nacht'] as const) {
    const wort = art === 'Tag' ? 'tagsüber' : 'nachts'
    const planC = art === 'Tag' ? plan.tag : plan.nacht
    const modusFeld = `tempMax${art}Modus` as const
    const abstandFeld = `tempMax${art}AbstandK` as const
    const festFeld = `tempMax${art}FestC` as const
    const max = (e: Einstellungen) => tempMax(e[modusFeld], e[abstandFeld], e[festFeld], planC)
    if (anders(modusFeld)) {
      zeilen.push({ feld: modusFeld, label: `Höchsttemperatur ${wort}`, von: `${modusText(geladen[modusFeld])} (${zahl(max(geladen))} °C)`, nach: `${modusText(entwurf[modusFeld])} (${zahl(max(entwurf))} °C)` })
    }
    if (anders(abstandFeld)) {
      zeilen.push({
        feld: abstandFeld, label: `Aufschlag ${wort}`,
        von: `+${zahl(geladen[abstandFeld])}`, nach: `+${zahl(entwurf[abstandFeld])} K`,
        folge: entwurf[modusFeld] === 'plan' ? `Höchsttemperatur ${zahl(max(entwurf))} °C` : 'gilt erst im Modus „Plan +"',
      })
    }
    if (anders(festFeld)) {
      zeilen.push({
        feld: festFeld, label: `Höchsttemperatur ${wort} (fest)`,
        von: zahl(geladen[festFeld]), nach: `${zahl(entwurf[festFeld])} °C`,
        folge: entwurf[modusFeld] === 'fest' ? undefined : 'gilt erst im Modus „fest"',
      })
    }
  }

  // Hilfsstärke samt den Einzelwerten, die sie setzt.
  const stufeGewaehlt = entwurf.hilfe !== 'aus' && entwurf.hilfe !== 'eigene' && anders('hilfe')
  const einzelAnders = EINZEL.filter((e) => anders(e.feld))
  if (anders('hilfe')) {
    const folge = stufeGewaehlt
      ? einzelAnders.map((e) => `${e.label}: ${zahl(geladen[e.feld], e.stellen)} → ${zahl(entwurf[e.feld], e.stellen)} ${e.einheit}`).join(' · ')
      : undefined
    zeilen.push({ feld: 'hilfe', label: 'Hilfsstärke', von: hilfeLabel(geladen.hilfe), nach: hilfeLabel(entwurf.hilfe), folge: folge || undefined })
  }
  if (!stufeGewaehlt) {
    for (const e of einzelAnders) {
      zeilen.push({ feld: e.feld, label: e.label, von: zahl(geladen[e.feld], e.stellen), nach: `${zahl(entwurf[e.feld], e.stellen)} ${e.einheit}` })
    }
  }

  if (anders('automatikAktiv')) zeilen.push({ feld: 'automatikAktiv', label: 'Automatik', von: aufAus(geladen.automatikAktiv), nach: aufAus(entwurf.automatikAktiv) })
  if (anders('nachtDurchlaufen')) zeilen.push({ feld: 'nachtDurchlaufen', label: 'Nachts durchlaufen', von: aufAus(geladen.nachtDurchlaufen), nach: aufAus(entwurf.nachtDurchlaufen) })
  if (anders('tagbetriebErlauben')) zeilen.push({ feld: 'tagbetriebErlauben', label: 'Auch tagsüber entfeuchten', von: aufAus(geladen.tagbetriebErlauben), nach: aufAus(entwurf.tagbetriebErlauben) })
  if (anders('mindestlaufzeitMin')) zeilen.push({ feld: 'mindestlaufzeitMin', label: 'Mindestlaufzeit', von: zahl(geladen.mindestlaufzeitMin, 0), nach: `${zahl(entwurf.mindestlaufzeitMin, 0)} min` })
  if (anders('ablauf')) zeilen.push({ feld: 'ablauf', label: 'Kondenswasser-Ablauf', von: ablaufText(geladen.ablauf), nach: ablaufText(entwurf.ablauf) })

  const m = (feld: keyof ZusatzMeldung) => !gleichWert(geladen.meldung[feld], entwurf.meldung[feld])
  if (m('aktiv')) zeilen.push({ feld: 'meldung.aktiv', label: 'Meldung „zieht nichts"', von: aufAus(geladen.meldung.aktiv), nach: aufAus(entwurf.meldung.aktiv) })
  if (m('grenzeW')) zeilen.push({ feld: 'meldung.grenzeW', label: 'Meldung unter', von: zahl(geladen.meldung.grenzeW, 0), nach: `${zahl(entwurf.meldung.grenzeW, 0)} W` })
  if (m('dauerMin')) zeilen.push({ feld: 'meldung.dauerMin', label: 'Meldung nach', von: zahl(geladen.meldung.dauerMin, 0), nach: `${zahl(entwurf.meldung.dauerMin, 0)} min` })
  if (m('wiederholungH')) zeilen.push({ feld: 'meldung.wiederholungH', label: 'Meldung wiederholen alle', von: zahl(geladen.meldung.wiederholungH, 0), nach: `${zahl(entwurf.meldung.wiederholungH, 0)} h` })
  return zeilen
}

export function ablaufText(a: 'tank' | 'schlauch'): string {
  return a === 'tank' ? 'Tank' : 'Ablaufschlauch'
}

// ------------------------------------------------------- Höchsttemperatur

export type TempGrenzen = { max: number; folgeAus: number; wiederEin: number }

/**
 * Die Temperaturgrenzen aus dem Entwurf (so sieht man vor dem Speichern, was
 * sich ändert): Höchsttemperatur = Plan + Aufschlag bzw. fester Wert; der Zusatz
 * geht `folgeAbstandK` früher aus und kommt `wiederEinAbstandK` darunter wieder.
 */
export function tempGrenzen(
  e: Pick<Einstellungen, 'folgeAbstandK' | 'wiederEinAbstandK'>,
  maxC: number,
): TempGrenzen {
  const folgeAus = round1(maxC - e.folgeAbstandK)
  return { max: maxC, folgeAus, wiederEin: round1(folgeAus - e.wiederEinAbstandK) }
}

const round1 = (w: number) => Math.round(w * 10) / 10
const round2 = (w: number) => Math.round(w * 100) / 100

// ------------------------------------------------------------------ Bänder

export type ZonenArt = 'ein' | 'aus'
export type MarkeArt = 'ein' | 'aus' | 'ziel' | 'max'

export type ZusatzBand = {
  titel: string
  kurz: string
  von: number
  bis: number
  stellen: number
  einheit: string
  zonen: Array<{ art: ZonenArt; ab: number; bis: number }>
  marken: Array<{ wert: number; pos: number; label: string; art: MarkeArt }>
  /** Position des Istwerts in Prozent; `null` ohne Messwert. */
  ist: number | null
  istWert: number | null
  istWarm: boolean
  /** Die Schalt-Schwellen des Bands (VPD oder Feuchte); für die Texte darunter. */
  ein: number | null
  aus: number | null
}

const pos = (von: number, bis: number, w: number) => Math.round(Math.max(0, Math.min(100, ((w - von) / (bis - von)) * 100)) * 10) / 10

function bandBauen(
  o: Pick<ZusatzBand, 'titel' | 'kurz' | 'stellen' | 'einheit'> & {
    von: number
    bis: number
    zonen: Array<{ art: ZonenArt; ab: number; bis: number }>
    marken: Array<{ wert: number; label: string; art: MarkeArt }>
    ist: number | null
    istWarm?: boolean
    ein?: number
    aus?: number
  },
): ZusatzBand {
  return {
    titel: o.titel, kurz: o.kurz, von: o.von, bis: o.bis, stellen: o.stellen, einheit: o.einheit,
    zonen: o.zonen.map((z) => ({ art: z.art, ab: Math.max(o.von, z.ab), bis: Math.min(o.bis, z.bis) })),
    marken: o.marken.map((m) => ({ ...m, pos: pos(o.von, o.bis, m.wert) })),
    ist: o.ist == null ? null : pos(o.von, o.bis, o.ist),
    istWert: o.ist,
    istWarm: o.istWarm ?? false,
    ein: o.ein ?? null,
    aus: o.aus ?? null,
  }
}

/**
 * Das VPD-Band (tagsüber): EIN unter Ziel − Abstand, AUS über Ziel + Abstand.
 *
 * Mit bekanntem Ziel rechnet das Band mit dem Abstand aus dem Entwurf — so
 * wandern die Marken, während man ihn ändert. Ohne Ziel gelten die Schwellen,
 * die der Server gemeldet hat; fehlen auch sie, gibt es kein Band (`null`)
 * statt einer erfundenen Skala.
 */
export function vpdBand(live: Pick<EntfeuchterZusatzLive, 'vpdZiel' | 'vpdEinSchwelle' | 'vpdAusSchwelle' | 'vpd'>, abstandKpa: number): ZusatzBand | null {
  // Auf zwei Stellen gerundet: 1,4 + 0,15 ist in Gleitkomma 1,5499999999999998.
  const ein = live.vpdZiel != null ? round2(live.vpdZiel - abstandKpa) : live.vpdEinSchwelle
  const aus = live.vpdZiel != null ? round2(live.vpdZiel + abstandKpa) : live.vpdAusSchwelle
  if (ein == null || aus == null) return null
  const alle = [ein, aus, live.vpd, live.vpdZiel].filter((w): w is number => w != null && Number.isFinite(w))
  const von = Math.floor((Math.min(...alle) - 0.3) * 10) / 10
  const bis = Math.ceil((Math.max(...alle) + 0.3) * 10) / 10
  const marken: Array<{ wert: number; label: string; art: MarkeArt }> = [{ wert: ein, label: 'EIN', art: 'ein' }]
  if (live.vpdZiel != null) marken.push({ wert: live.vpdZiel, label: 'Plan', art: 'ziel' })
  marken.push({ wert: aus, label: 'AUS', art: 'aus' })
  return bandBauen({
    titel: live.vpdZiel != null ? `VPD · Plan-Ziel ${zahl(live.vpdZiel, 2)}` : 'VPD',
    kurz: `EIN unter ${zahl(ein, 2)} · AUS über ${zahl(aus, 2)}`,
    von, bis, stellen: 2, einheit: 'kPa',
    zonen: [{ art: 'ein', ab: von, bis: ein }, { art: 'aus', ab: aus, bis }],
    marken, ist: live.vpd, ein, aus,
  })
}

/** Das Feuchte-Band (nachts, oder wenn der Plan kein VPD liefert): EIN über der oberen, AUS unter der unteren Schwelle. */
export function feuchteBand(live: Pick<EntfeuchterZusatzLive, 'feuchteEinProzent' | 'feuchteAusProzent' | 'feuchteProzent'>): ZusatzBand | null {
  const { feuchteEinProzent: ein, feuchteAusProzent: aus } = live
  if (ein == null || aus == null) return null
  const alle = [ein, aus, live.feuchteProzent].filter((w): w is number => w != null && Number.isFinite(w))
  const von = Math.floor(Math.min(...alle) - 4)
  const bis = Math.ceil(Math.max(...alle) + 4)
  return bandBauen({
    titel: 'Luftfeuchte · Plan',
    kurz: `EIN über ${zahl(ein, 0)} · AUS unter ${zahl(aus, 0)}`,
    von, bis, stellen: 0, einheit: '%',
    zonen: [{ art: 'aus', ab: von, bis: aus }, { art: 'ein', ab: ein, bis }],
    marken: [{ wert: aus, label: 'AUS', art: 'aus' }, { wert: ein, label: 'EIN', art: 'ein' }],
    ist: live.feuchteProzent, ein, aus,
  })
}

/**
 * Das Temperatur-Band: der Zusatz geht früher aus als das führende Gerät
 * (`folgeAus` < `max`) und kommt erst unter `wiederEin` zurück. Ist das Zelt
 * wärmer als `folgeAus`, steht der Istwert im Warnton.
 */
export function temperaturBand(g: TempGrenzen, tempC: number | null, fuehrungName: string, zusatzName: string): ZusatzBand {
  let von = g.max - 4
  let bis = g.max + 2
  if (tempC != null && Number.isFinite(tempC)) {
    von = Math.min(von, Math.floor(tempC - 1))
    bis = Math.max(bis, Math.ceil(tempC + 1))
  }
  return bandBauen({
    titel: 'Temperatur im Zelt',
    kurz: `${zusatzName} aus ${zahl(g.folgeAus)} · ${fuehrungName} aus ${zahl(g.max)}`,
    von, bis, stellen: 1, einheit: '°C',
    zonen: [{ art: 'ein', ab: von, bis: g.wiederEin }, { art: 'aus', ab: g.folgeAus, bis: g.max }],
    marken: [
      { wert: g.wiederEin, label: 'an', art: 'ein' },
      { wert: g.folgeAus, label: 'Zusatz', art: 'max' },
      { wert: g.max, label: 'Haupt', art: 'max' },
    ],
    ist: tempC,
    istWarm: tempC != null && tempC > g.folgeAus,
  })
}

/**
 * Zeigt die Seite „Plan unvollständig"? Nie zusammen mit „Home Assistant
 * antwortet nicht": ohne Verbindung sind die Plan-Werte nur nicht angekommen —
 * unvollständig ist dann nichts, und zwei Warnungen für eine Ursache wären eine
 * zu viel.
 */
export function planHinweisZeigen(live: Pick<EntfeuchterZusatzLive, 'haErreichbar' | 'planUnvollstaendig'>): boolean {
  return live.haErreichbar && live.planUnvollstaendig === true
}

// ------------------------------------------------------------- Zustandswort

export type Zustand = { text: string; ton: 'an' | 'warn' | 'neutral' }

/**
 * Das Wort rechts oben in der Statuskarte. Es sagt, was gerade IST — nach dem
 * gespeicherten Stand (`geladen`), nicht nach dem Entwurf — und sagt „unbekannt",
 * wenn Home Assistant den Zustand nicht liefert.
 */
export function zustandWort(live: EntfeuchterZusatzLive, geladen: Pick<Einstellungen, 'hilfe' | 'automatikAktiv'>, zuWarm: boolean): Zustand {
  if (live.automatikAn === false || !geladen.automatikAktiv) return { text: 'Automatik aus', ton: 'neutral' }
  if (geladen.hilfe === 'aus') return { text: 'Zusatz aus (Hilfe aus)', ton: 'neutral' }
  if (live.zusatzOnline === false) return { text: 'offline', ton: 'warn' }
  // „Zieht nichts" hängt allein am Befund des Servers (er rechnet Dauer und Meldung.aktiv mit ein).
  if (live.ziehtNichts === true) return { text: 'an, zieht nichts', ton: 'warn' }
  if (live.zusatzAn === true) return { text: 'entfeuchtet', ton: 'an' }
  if (live.zusatzAn === false) return zuWarm ? { text: 'zu warm — aus', ton: 'warn' } : { text: 'bereit', ton: 'neutral' }
  return { text: 'Zustand unbekannt', ton: 'neutral' }
}

/**
 * Das erste Band der Statuskarte: tagsüber die Schaltgröße, die der Fork gewählt
 * hat (VPD, sonst Plan-Feuchte); nachts mit „Nachts durchlaufen" die
 * Plan-Feuchte. Weiß der Fork keine Schaltgröße („Plan unvollständig"), gibt es
 * kein Band — keine erfundene Skala.
 */
export function ersteBand(
  live: EntfeuchterZusatzLive,
  e: Pick<Einstellungen, 'nachtDurchlaufen' | 'vpdHystereseKpa'>,
): { band: ZusatzBand | null; art: 'vpd' | 'feuchte' | null } {
  const nacht = live.tagPhase === false
  if (nacht && e.nachtDurchlaufen) return { band: feuchteBand(live), art: 'feuchte' }
  if (live.schaltgroesse === 'vpd') return { band: vpdBand(live, e.vpdHystereseKpa), art: 'vpd' }
  if (live.schaltgroesse === 'feuchte') return { band: feuchteBand(live), art: 'feuchte' }
  return { band: null, art: null }
}

/** Der Hinweis unter den Bändern — was als Nächstes passiert. Nur mit Werten, die es gibt; sonst leer. */
export function statusHinweis(
  live: EntfeuchterZusatzLive,
  e: Pick<Einstellungen, 'mindestlaufzeitMin' | 'mindestpauseMin' | 'nachtDurchlaufen' | 'meldung'>,
  g: TempGrenzen,
  eins: { band: ZusatzBand | null; art: 'vpd' | 'feuchte' | null },
): string {
  if (live.ziehtNichts === true) {
    return `Zieht nichts: der Shelly meldet an, die Leistung liegt unter ${zahl(e.meldung.grenzeW, 0)} W. Tank voll oder Gerät ausgeschaltet?`
  }
  if (live.tagPhase === false && e.nachtDurchlaufen) {
    return `Nachts durchlaufen: er läuft, bis das Zelt über ${zahl(g.folgeAus)} °C steigt, und startet wieder unter ${zahl(g.wiederEin)} °C.`
  }
  const { band, art } = eins
  if (!band || art !== 'vpd' || band.ein == null || band.aus == null) return ''
  if (live.zusatzAn === true) {
    return `Läuft, bis das VPD über ${zahl(band.aus, 2)} kPa steigt — frühestens nach ${zahl(e.mindestlaufzeitMin, 0)} min. Dazwischen bleibt er, wie er ist.`
  }
  if (live.zusatzAn === false) {
    return `Steht, bis das VPD unter ${zahl(band.ein, 2)} kPa fällt (nach ${zahl(e.mindestpauseMin, 0)} min Pause) und das Zelt kühler als ${zahl(g.wiederEin)} °C ist.`
  }
  return ''
}

// ------------------------------------------------------------------ Namen

/** Der Name, der für ein Gerät gilt: eingetippt, sonst die Vorgabe aus Home Assistant. */
export function wirksamerName(eingabe: string, vorgabe: string): string {
  const t = eingabe.trim()
  return t === '' ? vorgabe : t
}

/**
 * Der PUT-Körper der Namen — nur ein Name, der sich wirklich ändert.
 * Leer geschickt heißt „zurück auf die Vorgabe".
 */
export function namenAenderung(geladen: EntfeuchterNamen, entwurf: { fuehrung: string; zusatz: string }): EntfeuchterNamenAenderung {
  const koerper: EntfeuchterNamenAenderung = {}
  for (const rolle of ['fuehrung', 'zusatz'] as const) {
    const alt: EntfeuchterName = geladen[rolle]
    if (wirksamerName(entwurf[rolle], alt.vorgabe) !== alt.anzeigename) koerper[rolle] = entwurf[rolle].trim()
  }
  return koerper
}
