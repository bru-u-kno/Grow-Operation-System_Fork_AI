import type { HomeAssistantEntity } from '../../types'

/**
 * Fork AI (forkai.236, A-019 / Lücke 5 aus A-017): Welche Home-Assistant-Entitäten zu einer Aufgabe passen.
 *
 * <b>Warum.</b> Die Auswahl je Rolle ("Zeltfühler · Feuchte", "CO₂-Sensor") listete alle Entitäten alphabetisch —
 * Backup-Zustände und Sonnenzeiten zuerst, der passende Fühler irgendwo mittendrin. Bei einer Anlage mit
 * Hunderten Entitäten ist das für einen Fremden mühsam. Diese Datei sortiert nur VOR; sie ordnet nie zu und
 * blendet nichts aus.
 *
 * <b>Reine Funktion.</b> Keine Komponente, kein Netz: Eingabe sind die Entitäten (wie `GET /api/home-assistant/entities`
 * sie liefert) und ein Kriterium (Domains, Einheit, Rollenname), Ausgabe zwei Listen. Das Frontend hat beide Seiten
 * der Rechnung schon (die Rolle mit `domains`/`einheit`/`label` aus `/api/steuerung/geraete`, die Entität mit
 * `unitOfMeasurement`/`deviceClass`) — ein Backend-Umweg brächte keinen neuen Wert und keinen Vertrag.
 *
 * <b>Rangfolge (jede Stufe nur unter der vorigen):</b>
 * <ol>
 *   <li><b>Passt</b> heißt: Domain stimmt UND — hat die Rolle eine Einheit — Einheit/Geräteklasse stimmen
 *       (Geräteklasse und Einheit zusammen oder allein; eine Entität mit anderer Geräteklasse oder Einheit passt
 *       nicht, so fallen die vielen Batterie-%-Sensoren heraus). Ohne Klasse und ohne Einheit zählt nur der Name.
 *       Hat die Rolle keine Einheit (Schalter, Zustände, Stufen), muss der Name zu einem Begriff der Rolle passen.</li>
 *   <li>Innerhalb "Passt": erst die belastbarere Passung (Einheit/Klasse + Name, dann Klasse allein, dann nur Name, zuletzt
 *       nur Einheit — siehe `RANG`), dann mehr Namens-Treffer, dann Klasse vor nur Einheit, dann alphabetisch.</li>
 *   <li>"Alle anderen": zuerst Entitäten mit passender Domain, zuletzt, was Home Assistant selbst liefert und nie
 *       Messwert einer Anlage ist (Sonne, Sicherung); sonst alphabetisch.</li>
 * </ol>
 *
 * <b>Begriffe.</b> Der Rollenname ("Zeltfühler · Feuchte") wird auf Begriffe abgebildet (Zelt, Feuchte), jeder Begriff
 * hat deutsche und englische Wörter. Nur allgemeine Wörter, keine Namen aus einer bestimmten Anlage.
 */

export type Kriterium = {
  /** Domains, die die Rolle erlaubt (`sensor`, `switch` …). */
  domains: readonly string[]
  /** Einheit der Rolle (`%`, `°C` …); null bei Schalt- und Zustandsrollen. */
  einheit?: string | null
  /** Rollenname — daraus werden die Begriffe gelesen, wenn `begriffe` fehlt. */
  label: string
  /** Begriffe ausdrücklich vorgeben (Messgrößen-Auswahl); sonst aus `label` gelesen. */
  begriffe?: readonly string[]
}

/**
 * Welche Einheit(en) und Geräteklasse(n) von Home Assistant zu einer Einheit der Rolle gehören.
 * Klein gehalten: nur Einheiten, die eine Rolle des Katalogs wirklich führt (der Mengenwächter
 * `entitaets-passung-katalog.node.test.ts` liest den Katalog und schlägt an, wenn eine neue Einheit fehlt).
 */
export const EINHEIT_PASSUNG: Readonly<Record<string, { einheiten: readonly string[]; klassen: readonly string[] }>> = {
  '%': { einheiten: ['%'], klassen: ['humidity'] },
  '°C': { einheiten: ['°c'], klassen: ['temperature'] },
  'ppm': { einheiten: ['ppm'], klassen: ['carbon_dioxide'] },
  // VPD hat in Home Assistant keine eigene Geräteklasse; Vorlagen-Sensoren tragen oft `pressure`.
  'kPa': { einheiten: ['kpa'], klassen: ['pressure'] },
  // kW wird von der Strom-Kachel auf W umgerechnet (StromKachel.cs), darf also vorgeschlagen werden.
  'W': { einheiten: ['w', 'kw'], klassen: ['power'] },
  'kWh': { einheiten: ['kwh'], klassen: ['energy'] },
  'mS/cm': { einheiten: ['ms/cm', 'µs/cm', 'us/cm'], klassen: ['conductivity'] },
}

type Begriff = {
  id: string
  /** Wörter, normalisiert (ASCII, klein). Ein Wort mit "=" davor gilt nur als ganzes Wort, sonst als Wortteil. */
  woerter: readonly string[]
  /** Wörter, die nur im Rollennamen auf den Begriff schließen lassen, nicht in Entitätsnamen. */
  nurImRollennamen?: readonly string[]
  /**
   * Allgemeine Funktionswörter (schalten, Status, Stufe …) zählen halb: "Kühler-Steckdose" ist für
   * "Entfeuchter · schalten" ein schwächerer Treffer als "Entfeuchter Modus". Gegenstände und Orte zählen voll.
   */
  schwach?: boolean
}

export const BEGRIFFE: readonly Begriff[] = [
  { id: 'feuchte', woerter: ['=feuchte', 'luftfeucht', 'feuchtigkeit', 'humidit', '=humid', '=rh', '=rlf'] },
  { id: 'temperatur', woerter: ['temperatur', 'temperature', '=temp'] },
  { id: 'co2', woerter: ['=co2', 'kohlendioxid', 'dioxid'] },
  { id: 'vpd', woerter: ['=vpd', 'dampfdruck', 'vapor', 'vapour'] },
  { id: 'ppfd', woerter: ['=ppfd', '=par'] },
  { id: 'zelt', woerter: ['zelt', '=tent', 'growtent', 'growbox', '=grow', 'canopy', '=box'] },
  { id: 'aussen', woerter: ['aussen', 'outside', 'outdoor', 'draussen', 'exterior'] },
  { id: 'keller', woerter: ['keller', 'basement', 'cellar'] },
  { id: 'wasser', woerter: ['wasser', 'water', 'nahrloesung', 'nahrlosung', 'naehrloesung', 'nutrient', 'reservoir', 'rdwc', '=tank'] },
  { id: 'stand', woerter: ['wasserstand', 'fuellstand', 'level', 'pegel'] },
  { id: 'licht', woerter: ['licht', '=light', '=lights', 'lampe', '=lamp', '=led', 'leuchte'] },
  { id: 'entfeuchter', woerter: ['entfeucht', 'dehumid', '=dehum'], nurImRollennamen: ['fuehrungsgeraet'] },
  { id: 'zusatz', woerter: ['zusatz', 'zweit', 'second', '=extra', '=aux', '=2'] },
  { id: 'zuluft', woerter: ['zuluft', 'intake', 'inlet', '=supply'] },
  { id: 'abluft', woerter: ['abluft', 'exhaust', 'extract'] },
  { id: 'luefter', woerter: ['luefter', '=fan', 'ventilator'] },
  { id: 'kuehler', woerter: ['kuehl', 'chiller', 'cooler', 'cooling'] },
  { id: 'ventil', woerter: ['ventil', 'valve', 'magnetventil', 'solenoid'] },
  { id: 'leistung', woerter: ['leistung', '=power', '=watt'] },
  { id: 'energie', woerter: ['energie', 'energy', 'verbrauch', 'consumption'] },
  { id: 'stufe', woerter: ['stufe', '=speed', 'drehzahl'], schwach: true },
  { id: 'sollwert', woerter: ['sollwert', 'setpoint', '=target', '=soll'], schwach: true },
  { id: 'schalter', woerter: ['schalt', 'steckdose', 'socket', '=plug', 'outlet', 'relay', 'relais'], schwach: true },
  { id: 'status', woerter: ['status', '=online', 'connect', 'verbunden', 'erreichbar'], schwach: true },
  { id: 'bedarf', woerter: ['bedarf', 'demand', '=need'], schwach: true },
  { id: 'zeit', woerter: ['=zeit', '=time', 'schedule', 'zeitplan'], schwach: true },
  { id: 'ein', woerter: ['=ein', '=on', '=start', 'beginn'], schwach: true },
  { id: 'aus', woerter: ['=aus', '=off', '=ende', '=end', '=stop'], schwach: true },
  { id: 'skript', woerter: ['skript', 'script', 'grenze', 'limit'] },
  { id: 'ph', woerter: ['=ph'] },
  { id: 'ec', woerter: ['=ec', 'leitfaehig', 'conductiv', '=tds'] },
  { id: 'unten', woerter: ['=unten', '=low', 'lower', '=min', 'minimum'], schwach: true },
  { id: 'oben', woerter: ['=oben', '=high', 'upper', '=max', 'maximum'], schwach: true },
  { id: 'orp', woerter: ['=orp', 'redox'] },
  { id: 'sauerstoff', woerter: ['sauerstoff', 'oxygen', '=do'] },
  { id: 'pumpe', woerter: ['pumpe', '=pump'] },
  { id: 'usv', woerter: ['=usv', '=ups', 'batterie'] },
]

/** ASCII, klein, Umlaute ausgeschrieben, Satzzeichen zu Leerzeichen: "CO₂ Zelt" → "co2 zelt". */
export function normal(text: string): string {
  return text.toLowerCase()
    .replace(/₂/g, '2')
    .replace(/ä/g, 'ae').replace(/ö/g, 'oe').replace(/ü/g, 'ue').replace(/ß/g, 'ss')
    .replace(/[^a-z0-9]+/g, ' ')
    .trim()
}

function wortTrifft(tokens: readonly string[], wort: string): boolean {
  if (wort.startsWith('=')) {
    const ganz = wort.slice(1)
    return tokens.includes(ganz)
  }
  return tokens.some((token) => token.includes(wort))
}

/** Die Begriffe, die ein Rollenname nennt ("Zeltfühler · Feuchte" → zelt, feuchte). */
export function begriffeAusRollenname(label: string): string[] {
  const tokens = normal(label).split(' ').filter(Boolean)
  return BEGRIFFE
    .filter((b) => [...b.woerter, ...(b.nurImRollennamen ?? [])].some((w) => wortTrifft(tokens, w)))
    .map((b) => b.id)
}

export type Vorbereitet = {
  entity: HomeAssistantEntity
  tokens: string[]
  name: string
  einheit: string | null
  klasse: string | null
  /** Was Home Assistant selbst liefert und nie ein Messwert der Anlage ist. */
  system: boolean
}

/** Pro Entität einmal rechnen, nicht pro Rolle und Eingabe. */
export function vorbereiten(entity: HomeAssistantEntity): Vorbereitet {
  const objekt = entity.entityId.includes('.') ? entity.entityId.slice(entity.entityId.indexOf('.') + 1) : entity.entityId
  const text = normal(`${entity.friendlyName ?? ''} ${objekt}`)
  const tokens = text.split(' ').filter(Boolean)
  const einheit = (entity.unitOfMeasurement ?? '').trim().toLowerCase()
  const klasse = (entity.deviceClass ?? '').trim().toLowerCase()
  const idTokens = normal(objekt).split(' ')
  return {
    entity,
    tokens,
    name: (entity.friendlyName ?? entity.entityId).toLowerCase(),
    einheit: einheit === '' ? null : einheit,
    klasse: klasse === '' ? null : klasse,
    system: entity.domain === 'sun' || idTokens[0] === 'sun' || idTokens.includes('backup'),
  }
}

/** Begriffe, die einen Ort benennen: wer "Zeltfühler" sucht, will nicht den Kellerfühler vorn sehen. */
const ORTE = ['zelt', 'aussen', 'keller', 'wasser']

function trifft(e: Vorbereitet, id: string): boolean {
  const b = BEGRIFFE.find((x) => x.id === id)
  return !!b && b.woerter.some((w) => wortTrifft(e.tokens, w))
}

/**
 * Wie gut der Name passt: `treffer` = Gewicht der genannten Begriffe der Rolle, die der Name trägt (voll 2, Funktionswörter 1). `strafe` = Orte, die der Name
 * nennt, die Rolle aber nicht ("Außen Luftfeuchte" bei "Zeltfühler · Feuchte"; "Zelt" nur, wenn die Rolle einen
 * anderen Ort nennt). `punkte` = treffer − 2 × strafe ordnet; treffer allein entscheidet bei Gleichstand.
 */
function zaehleName(e: Vorbereitet, begriffIds: readonly string[]): { treffer: number; punkte: number } {
  const gewicht = (id: string) => (BEGRIFFE.find((x) => x.id === id)?.schwach ? 1 : 2)
  const treffer = begriffIds.filter((id) => trifft(e, id)).reduce((summe, id) => summe + gewicht(id), 0)
  const gefragt = begriffIds.filter((id) => ORTE.includes(id))
  const strafe = ORTE.filter((o) => !gefragt.includes(o) && (gefragt.length > 0 || o !== 'zelt') && trifft(e, o)).length
  return { treffer, punkte: treffer - 2 * strafe }
}

/**
 * Wie belastbar die Passung ist (größer = besser). Einheit/Klasse UND Name sind sicher. Eine Geräteklasse allein sagt, was
 * die Entität misst. Nur der Name trägt, wenn Klasse und Einheit fehlen (Vorlagen-Sensoren). Eine bloße Einheit ohne Klasse
 * und ohne Namens-Treffer ist am schwächsten: "%" trägt auch Batterie, Signal und Helligkeit.
 */
const RANG = { EINHEIT_UND_NAME: 3, NUR_KLASSE: 2, NUR_NAME: 1, NUR_EINHEIT: 0 } as const

type Bewertung = { rang: number; treffer: number; punkte: number; klasse: number }

/** null = passt nicht (kommt unter "Alle anderen"). */
function bewerte(e: Vorbereitet, k: Kriterium, begriffIds: readonly string[]): Bewertung | null {
  if (!k.domains.includes(e.entity.domain)) return null
  const { treffer, punkte } = zaehleName(e, begriffIds)
  const regel = k.einheit ? EINHEIT_PASSUNG[k.einheit] : undefined

  if (!regel) {
    // Ohne Einheit entscheidet allein der Name; ohne Namens-Treffer ist alles in der Domain nur "möglich".
    return treffer > 0 ? { rang: RANG.NUR_NAME, treffer, punkte, klasse: 0 } : null
  }

  if (e.klasse) {
    // Eine Geräteklasse sagt, was die Entität misst: andere Klasse (z. B. battery) = anderes Ding.
    if (!regel.klassen.includes(e.klasse)) return null
    if (e.einheit && !regel.einheiten.includes(e.einheit)) return null
    return { rang: treffer > 0 ? RANG.EINHEIT_UND_NAME : RANG.NUR_KLASSE, treffer, punkte, klasse: 1 }
  }
  if (e.einheit) {
    if (!regel.einheiten.includes(e.einheit)) return null
    return { rang: treffer > 0 ? RANG.EINHEIT_UND_NAME : RANG.NUR_EINHEIT, treffer, punkte, klasse: 0 }
  }
  // Weder Klasse noch Einheit (Vorlagen-Sensoren, Fremdintegrationen): nur der Name kann helfen.
  return treffer > 0 ? { rang: RANG.NUR_NAME, treffer, punkte, klasse: 0 } : null
}

const nachName = (a: Vorbereitet, b: Vorbereitet) => a.name.localeCompare(b.name, 'de')

export type Rangfolge = {
  passt: HomeAssistantEntity[]
  andere: HomeAssistantEntity[]
}

/** Teilt die Entitäten in "passt" (geordnet) und "andere" (geordnet). Nichts geht verloren, nichts wird doppelt geführt. */
export function rangiere(entities: readonly Vorbereitet[], k: Kriterium): Rangfolge {
  const begriffIds = k.begriffe ?? begriffeAusRollenname(k.label)
  const passt: Array<{ e: Vorbereitet; b: Bewertung }> = []
  const rest: Array<{ e: Vorbereitet; domainOk: boolean; treffer: number }> = []
  for (const e of entities) {
    const b = bewerte(e, k, begriffIds)
    if (b) passt.push({ e, b })
    else rest.push({ e, domainOk: k.domains.includes(e.entity.domain), treffer: zaehleName(e, begriffIds).punkte })
  }
  passt.sort((x, y) =>
    y.b.rang - x.b.rang || y.b.punkte - x.b.punkte || y.b.treffer - x.b.treffer || y.b.klasse - x.b.klasse || nachName(x.e, y.e))
  rest.sort((x, y) =>
    Number(x.e.system) - Number(y.e.system)
    || Number(y.domainOk) - Number(x.domainOk)
    || y.treffer - x.treffer
    || nachName(x.e, y.e))
  return { passt: passt.map((p) => p.e.entity), andere: rest.map((r) => r.e.entity) }
}
