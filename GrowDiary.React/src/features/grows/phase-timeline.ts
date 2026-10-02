/**
 * Der Zeitstrahl eines Grows: Keim → Veg → Blüte → Ernte.
 *
 * Wird auf Live, in der Grow-Liste und im Grow-Detail gezeichnet — deshalb
 * liegt die Rechnung hier und nicht dreifach in den Seiten. Dieselbe Zahl an
 * mehreren Stellen zu pflegen ist in diesem Projekt schon dreimal
 * schiefgegangen.
 *
 * Der Strahl zeigt den **Plan** und wo man **heute** darin steht:
 *
 *   Keim    Start → Bewurzelt/Gekeimt (steht das nicht fest, entfällt die Phase)
 *   Veg     bis zum Flip; davor bis zum geplanten Flip (Bewurzelung + plannedVegDays)
 *   Blüte   Flip + Blütewochen des Breeders
 *
 * Vorher fehlte die geplante Veg-Dauer. Ohne sie konnte der Strahl vor dem
 * Flip nichts als „Veg, 68 Tage und läuft" sagen — kein Ziel, kein Ende, keine
 * Ernteschätzung. Der Entwurf zeigt dagegen „Flip geplant 04.08.", also war
 * die Absicht immer, den Plan zu sehen. Wer keine Veg-Dauer angibt, bekommt
 * weiterhin den offenen Strahl; erfunden wird nichts.
 */

import { phaseName } from '../../deutsche-woerter'

export type PhaseState = 'done' | 'current' | 'planned'

/** Kennung der Phase im Strahl — nicht der Text auf dem Schirm, dafür {@link PHASEN_ANZEIGE}. */
export type PhaseName = 'Keim' | 'Sämling' | 'Veg' | 'Blüte' | 'Trocknen' | 'Aushärten'

/**
 * Wie die Phasen auf dem Schirm heißen — aus `phaseName`, wo es eine GrowStage gibt.
 *
 * <b>Warum (02.10.2026).</b> Der Strahl führte eigene Namen, und für eine
 * Phase wich er ab: auf der Karte stand „Veg Tag 64", während `phaseName`
 * dieselbe Phase „Wachstum" nennt. Aufgefallen ist es erst, als ein Grow
 * zufällig im Wachstum stand — der Demobestand hat keinen. Zwei Namen für eine
 * Phase laufen auseinander; also verweisen statt abtippen.
 */
export const PHASEN_ANZEIGE: Record<PhaseName, string> = {
  // Die Keimung ist keine GrowStage (der Server führt sie nicht als Phase).
  Keim: 'Keim',
  Sämling: phaseName('Seedling'),
  Veg: phaseName('Veg'),
  Blüte: phaseName('Flower'),
  Trocknen: phaseName('Dry'),
  Aushärten: phaseName('Cure'),
}

const WACHSTUM = PHASEN_ANZEIGE.Veg

export type Phase = {
  /** Kennung der Phase; der Name auf dem Schirm steht in {@link PHASEN_ANZEIGE}. */
  name: PhaseName
  label: string
  /** Kurzfassung für enge Stellen wie die Grow-Karten: „Wachstum 22/28". */
  short: string
  days: number
  state: PhaseState
  /** Nur gesetzt, solange die Phase läuft: 0–1 des geplanten Anteils. */
  progress?: number
  /** Der wievielte Tag in dieser Phase heute ist; nur für die laufende. */
  dayInPhase?: number
}

/**
 * Beschriftung fuer den Balken: die Zahl nach VORN.
 *
 * Im Zeitstrahl stand „TROCKNE…" statt „Trocknen 10 T" — ausgerechnet die
 * Zahl, um die es geht, fiel weg, und zwar auf JEDER Schreibtischbreite. Der
 * Balken darf nicht breiter werden (seine Laenge IST die Dauer, das ist die
 * Aussage der ganzen Achse), also muss die Reihenfolge sich aendern: steht
 * die Zahl vorn, faellt beim Kuerzen immer der Name und nie die Zahl — auf
 * jeder Breite, ohne eine einzige weitere Regel.
 *
 * Abschnitte ohne bekannte Dauer bleiben, wie sie sind: sie werden gar nicht
 * gekuerzt (`flex-grow: 0`), und „— Keim" waere nur seltsam.
 */
export function balkenText(kurz: string, tage: number): string {
  if (tage === 0) return kurz
  const luecke = kurz.indexOf(' ')
  if (luecke <= 0) return kurz
  return kurz.slice(luecke + 1) + ' ' + kurz.slice(0, luecke)
}

export type PhaseTimeline = {
  phases: Phase[]
  dates: { start: string; flip: string; harvest: string; ready: string }
  /** Woher die Dauern für Trocknen und Aushärten stammen — gehört an die Zahl. */
  readyNote: string
  /** true, sobald der Flip nur geplant und noch nicht erfolgt ist. */
  flipIsPlanned: boolean
  /** Tage bis zum geplanten Flip; negativ heißt überfällig. Null ohne Plan. */
  daysToFlip: number | null
}

/** Nur die Felder, die die Rechnung braucht — GrowSummary und GrowDetail passen beide. */
export type PhaseTimelineInput = {
  startDate: string | null
  flipDate?: string | null
  germinatedAt?: string | null
  rootedAt?: string | null
  /** Wann der Sämling zur Veg wurde — beobachtet, nicht gerechnet. */
  vegStartedAt?: string | null
  /** Klone haben keine Sämlingsphase: bewurzelt heisst vegetativ. */
  startMaterial?: string | null
  /**
   * Autoflower oder nicht.
   *
   * Ohne diese Angabe kam der Strahl bei einem Autoflower nie in die Blüte: er
   * las den Blütebeginn ausschließlich aus `flipDate`, und ein Autoflower hat
   * keines. Der Server rechnet dort längst anders (28 Tage nach der Keimung,
   * <c>GrowStageResolver</c>) — auf demselben Bildschirm stand also „Veg Tag
   * 70" neben Blüte-Zielwerten.
   */
  seedType?: string | null
  /**
   * Wo der Lauf eingestiegen ist, und wie viele Tage er in dieser Phase schon
   * hinter sich hatte, als er angelegt wurde.
   *
   * Wer einen laufenden Grow einträgt, gibt beides an — der Strahl hat es
   * bisher ignoriert und ab dem Anlegedatum bei null angefangen. Die App zählte
   * damit intern anders, als sie anzeigte.
   */
  entryPoint?: string | null
  daysAlreadyInPhase?: number | null
  /** Nach so vielen Tagen ohne Eintrag gilt der Sämling als durch (Schätzung). */
  seedlingDays?: number
  plannedVegDays?: number | null
  breederFlowerWeeksMin?: number | null
  breederFlowerWeeksMax?: number | null
  /** Wann wirklich geerntet wurde — ab da laufen Trocknen und Aushärten echt. */
  endDate?: string | null
}

/**
 * Trocknen und Aushärten: die Zeit nach der Ernte.
 *
 * Der Strahl endete bisher an der Ernte — und damit vor der Frage, die den
 * Betreiber wirklich umtreibt: wann ist es fertig? Zwischen „geerntet" und
 * „rauchbar" liegen Wochen, und die standen nirgends.
 *
 * Die Dauern sind belegt, nicht geschätzt:
 * - Trocknen: 7–14 Tage bei 58–62 % rF; hier gerechnet mit 10.
 * - Aushärten: 14 Tage Minimum, 30–60 Tage der eigentliche Bereich; hier 30.
 * Quellen: budtrainer.com „The 62% RH Jar Curing Guide", atmosiscience.com
 * „How Long & How to Burp Cannabis Jars".
 *
 * Beide sind Richtwerte und keine Termine — wer länger aushärtet, macht nichts
 * falsch. Deshalb steht die Herkunft als `readyNote` mit im Ergebnis.
 */
export const TROCKNEN_TAGE = 10
export const AUSHAERTEN_TAGE = 30
const READY_NOTE =
  `Rechnung: ${TROCKNEN_TAGE} Tage Trocknen (Bereich 7–14) und ${AUSHAERTEN_TAGE} Tage Aushärten `
  + '(Minimum 14, üblich 30–60) nach der Ernte. Richtwerte aus der Curing-Literatur, keine Termine.'

const TAG = 86_400_000
const EMPTY: PhaseTimeline = {
  phases: [],
  dates: { start: '—', flip: '—', harvest: '—', ready: '—' },
  readyNote: READY_NOTE,
  flipIsPlanned: false,
  daysToFlip: null,
}

function parse(value: string | null | undefined): Date | null {
  if (!value) return null
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? null : date
}

/** Ganze Tage zwischen zwei Zeitpunkten, mindestens 1 — eine Phase dauert nie 0 Tage. */
function tage(von: number, bis: number): number {
  return Math.max(1, Math.round((bis - von) / TAG))
}

export function buildPhaseTimeline(grow: PhaseTimelineInput | null, jetzt = Date.now()): PhaseTimeline {
  const start = parse(grow?.startDate ?? null)
  if (!grow || !start) return EMPTY

  const flip = parse(grow.flipDate)
  // Bewurzelt schlägt gekeimt: ab da hört das Keimen auf.
  const keimEnde = parse(grow.rootedAt) ?? parse(grow.germinatedAt)

  // Der Sämling: zwischen Keimung und den ersten echten Blättern. Er stand
  // vorher nicht im Strahl, obwohl die Zielwerte ihn längst kannten — der
  // Balken sagte „Veg Tag 8", die Kacheln zeigten Sämlings-Ziele. Zwei
  // Phasenmodelle nebeneinander, und keins verriet das andere.
  //
  // Der Übergang hängt nicht am Kalender, sondern am Aussehen: echte gezackte
  // Blätter statt der zwei runden Keimblätter, dickerer Stängel, regelmäßig
  // neue Blattpaare. Steht kein Eintrag, wird geschätzt — und das steht dann
  // auch dran.
  // Mitgebrachte Tage verschieben den Beginn der Einstiegsphase nach hinten:
  // wer „Veg, seit 20 Tagen" eintraegt, steht heute bei Veg Tag 20, nicht bei
  // Tag 1. Genauso rechnet der Server (GrowStageResolver).
  const mitgebracht = Math.max(0, grow.daysAlreadyInPhase ?? 0)
  const saemlingStart = (keimEnde?.getTime() ?? start.getTime())
    - (grow.entryPoint === 'Seedling' ? mitgebracht * TAG : 0)
  const saemlingTage = grow.seedlingDays ?? 14
  const vegEingetragen = parse(grow.vegStartedAt)
  const vegGeschaetzt = new Date(saemlingStart + saemlingTage * TAG)
  // Ein Klon hat nie Keimblätter gehabt: bewurzelt heisst vegetativ, die
  // Sämlingsphase entfällt komplett.
  const istKlon = grow.startMaterial === 'Clone'
  const vegBeginn = istKlon
    ? new Date(saemlingStart)
    : (vegEingetragen ?? (grow.entryPoint === 'Veg' && mitgebracht > 0
        ? new Date(start.getTime() - mitgebracht * TAG)
        : vegGeschaetzt))
  const imSaemling = !istKlon && flip == null && jetzt < vegBeginn.getTime()

  // Blütedauer aus den Breeder-Angaben; ohne sie der übliche Richtwert von acht
  // Wochen. Das Erntedatum trägt deshalb ein „~" — es ist eine Schätzung.
  const bluetewochen = grow.breederFlowerWeeksMax ?? grow.breederFlowerWeeksMin ?? 8
  const bluetetage = bluetewochen * 7

  // Veg beginnt, wo der Sämling endet.
  const vegStart = vegBeginn.getTime()

  // Der Flip: entweder erfolgt, oder aus der geplanten Veg-Dauer errechnet.
  // Die Dauer zählt ab Beginn der Veg-Phase — gefragt war „wie lange will ich
  // in der Veg bleiben", nicht „wie lange ab Aussaat".
  const geplanterFlip = grow.plannedVegDays != null && grow.plannedVegDays > 0
    ? new Date(vegStart + grow.plannedVegDays * TAG)
    : null
  // Autoflower kennen keinen Flip — sie gehen nach Tagen in die Bluete. 28 Tage
  // nach der Keimung, derselbe Richtwert wie in GrowStageResolver; ohne diesen
  // Rueckfall blieb der Strahl fuer immer in der Veg.
  const istAutoflower = grow.seedType === 'Autoflower'
  const autoBlueteStart = istAutoflower
    ? new Date((keimEnde?.getTime() ?? saemlingStart) + 28 * TAG)
    : null
  const flipFuerRechnung = flip ?? autoBlueteStart ?? geplanterFlip
  const inBluete = flipFuerRechnung != null && jetzt >= flipFuerRechnung.getTime()
    && (flip != null || istAutoflower)
  // Geplant ist alles, was noch nicht passiert ist — auch ein fest gesetztes
  // Datum in der Zukunft. Vorher stand unter dem Strahl "Geflippt 06.08.",
  // obwohl der 06.08. erst kommt.
  const flipIsPlanned = flipFuerRechnung != null && !inBluete

  const harvest = flipFuerRechnung ? new Date(flipFuerRechnung.getTime() + bluetetage * TAG) : null

  // Alle drei Phasen erscheinen IMMER. Vorher fehlten Keim und Blüte, solange
  // kein Bewurzelungsdatum und kein Flip erfasst war — dann stand da ein
  // einzelner Balken „Veg", und wo man im Lauf steckt, war nicht zu sehen.
  // `days: 0` heißt „Dauer unbekannt": die Anzeige gibt dem Abschnitt dann nur
  // einen schmalen Streifen, statt eine Länge zu behaupten.
  const phases: Phase[] = []

  // ---------- Keim ----------
  if (keimEnde && keimEnde.getTime() > start.getTime()) {
    const dauer = tage(start.getTime(), keimEnde.getTime())
    phases.push({ name: 'Keim', label: `Keim ${dauer} T`, short: `Keim ${dauer} T`, days: dauer, state: 'done' })
  } else {
    phases.push({ name: 'Keim', label: 'Keim · nicht erfasst', short: 'Keim —', days: 0, state: 'done' })
  }

  // ---------- Sämling ----------
  if (!istKlon) {
    const dauer = tage(saemlingStart, vegBeginn.getTime())
    const gelaufen = tage(saemlingStart, Math.min(jetzt, vegBeginn.getTime()))
    const geschaetzt = vegEingetragen == null
    phases.push(imSaemling
      ? {
          name: 'Sämling',
          label: geschaetzt ? `Sämling · Tag ${gelaufen} (geschätzt)` : `Sämling · Tag ${gelaufen}`,
          short: `Sämling ${gelaufen}`,
          days: dauer,
          state: 'current',
          dayInPhase: gelaufen,
        }
      : {
          name: 'Sämling',
          label: `Sämling ${dauer} T`,
          short: `Sämling ${dauer} T`,
          days: dauer,
          state: 'done',
        })
  }

  // ---------- Veg ----------
  // `flipFuerRechnung`, nicht `flip`: bei einem Autoflower gibt es kein
  // Flip-Datum, wohl aber einen Bluetebeginn (28 Tage nach der Keimung). Mit
  // `flip` blieb die Veg fuer immer die laufende Phase.
  if (inBluete && flipFuerRechnung) {
    const dauer = tage(vegStart, flipFuerRechnung.getTime())
    phases.push({ name: 'Veg', label: `${WACHSTUM} ${dauer} T`, short: `${WACHSTUM} ${dauer} T`, days: dauer, state: 'done' })
  } else {
    const gelaufen = tage(vegStart, jetzt)
    const geplant = flipFuerRechnung ? tage(vegStart, flipFuerRechnung.getTime()) : null
    phases.push(imSaemling
      ? {
          // Noch im Sämling: die Veg steht bevor, sie läuft nicht.
          name: 'Veg',
          label: geplant ? `${WACHSTUM} · ${geplant} T geplant` : `${WACHSTUM} · offen`,
          short: geplant ? `${WACHSTUM} ${geplant} T` : `${WACHSTUM} —`,
          days: geplant ?? 0,
          state: 'planned',
        }
      : {
          name: 'Veg',
          label: geplant ? `${WACHSTUM} · Tag ${gelaufen} von ${geplant}` : `${WACHSTUM} · Tag ${gelaufen}`,
          short: geplant ? `${WACHSTUM} ${gelaufen}/${geplant}` : `${WACHSTUM} ${gelaufen} T`,
          days: geplant ?? gelaufen,
          state: 'current',
          progress: geplant ? Math.min(1, gelaufen / geplant) : undefined,
          dayInPhase: gelaufen,
        })
  }

  // ---------- Blüte ----------
  if (inBluete && flipFuerRechnung) {
    const tagInBluete = Math.floor((jetzt - flipFuerRechnung.getTime()) / TAG) + 1
    phases.push({
      name: 'Blüte',
      label: `Blüte · Tag ${tagInBluete} von ${bluetetage}`,
      short: `Blüte ${tagInBluete}/${bluetetage}`,
      days: bluetetage,
      state: 'current',
      progress: Math.min(1, tagInBluete / bluetetage),
      dayInPhase: tagInBluete,
    })
  } else if (flipFuerRechnung) {
    phases.push({ name: 'Blüte', label: `Blüte ${bluetetage} T geplant`, short: `Blüte ${bluetetage} T`, days: bluetetage, state: 'planned' })
  } else {
    phases.push({ name: 'Blüte', label: 'Blüte · offen', short: 'Blüte —', days: 0, state: 'planned' })
  }

  // ---------- Trocknen und Aushaerten ----------
  // Ab hier endete der Strahl. „Geerntet" ist aber nicht „fertig": erst nach
  // Trocknen und Aushaerten ist der Lauf wirklich durch, und genau danach
  // fragt man, wenn man vor dem Zelt steht.
  const geerntet = parse(grow.endDate)
  const trockenStart = geerntet ?? harvest
  const trockenEnde = trockenStart ? new Date(trockenStart.getTime() + TROCKNEN_TAGE * TAG) : null
  const fertig = trockenEnde ? new Date(trockenEnde.getTime() + AUSHAERTEN_TAGE * TAG) : null

  // Nur ein WIRKLICH geernteter Grow laeuft durch diese Phasen; vorher sind sie
  // Vorschau. Sonst stuende „Trocknen Tag 3" an einem Grow, der noch bluet.
  const imTrocknen = geerntet !== null && jetzt < (trockenEnde?.getTime() ?? 0)
  const imAushaerten = geerntet !== null && trockenEnde !== null && jetzt >= trockenEnde.getTime()

  if (imTrocknen && geerntet) {
    const tagImTrocknen = Math.floor((jetzt - geerntet.getTime()) / TAG) + 1
    phases.push({
      name: 'Trocknen',
      label: `Trocknen · Tag ${tagImTrocknen} von ${TROCKNEN_TAGE}`,
      short: `Trocknen ${tagImTrocknen}/${TROCKNEN_TAGE}`,
      days: TROCKNEN_TAGE,
      state: 'current',
      progress: Math.min(1, tagImTrocknen / TROCKNEN_TAGE),
      dayInPhase: tagImTrocknen,
    })
  } else {
    phases.push({
      name: 'Trocknen',
      label: `Trocknen ${TROCKNEN_TAGE} T`,
      short: `Trocknen ${TROCKNEN_TAGE} T`,
      days: TROCKNEN_TAGE,
      state: imAushaerten ? 'done' : 'planned',
    })
  }

  if (imAushaerten && trockenEnde) {
    const tagImCure = Math.floor((jetzt - trockenEnde.getTime()) / TAG) + 1
    phases.push({
      name: 'Aushärten',
      label: `Aushärten · Tag ${tagImCure} von ${AUSHAERTEN_TAGE}`,
      short: `Aushärten ${tagImCure}/${AUSHAERTEN_TAGE}`,
      days: AUSHAERTEN_TAGE,
      state: 'current',
      progress: Math.min(1, tagImCure / AUSHAERTEN_TAGE),
      dayInPhase: tagImCure,
    })
  } else {
    phases.push({
      name: 'Aushärten',
      label: `Aushärten ${AUSHAERTEN_TAGE} T`,
      short: `Aushärten ${AUSHAERTEN_TAGE} T`,
      days: AUSHAERTEN_TAGE,
      state: 'planned',
    })
  }

  return {
    phases,
    dates: {
      start: shortDate(start),
      flip: shortDate(flipFuerRechnung),
      harvest: shortDate(geerntet ?? harvest),
      ready: shortDate(fertig),
    },
    readyNote: READY_NOTE,
    flipIsPlanned,
    daysToFlip: flipIsPlanned && flipFuerRechnung
      ? Math.round((flipFuerRechnung.getTime() - jetzt) / TAG)
      : null,
  }
}

/** „20.05." — Intl setzt den Punkt am Ende bei de-DE selbst. */
export function shortDate(value: Date | null): string {
  return value ? new Intl.DateTimeFormat('de-DE', { day: '2-digit', month: '2-digit' }).format(value) : '—'
}

/**
 * Die Beschriftung des Flip-Termins: „Flip geplant 09.06. · in 8 T",
 * „Geflippt 20.05." oder „Flip überfällig seit 3 T".
 *
 * Steht hier und nicht in den Seiten, weil Live und Grow-Detail denselben
 * Strahl zeichnen. Vorher stand dort schlicht immer „Flip geplant" — auch
 * lange nach dem Flip, und ohne Plan sogar „Flip geplant —", was nach einem
 * fehlenden Wert aussah statt nach einer offenen Entscheidung.
 */
export function flipLabel(geplant: boolean, tage: number | null, datum: string): string {
  if (datum === '—') return 'Flip offen'
  if (!geplant) return `Geflippt ${datum}`
  if (tage == null) return `Flip geplant ${datum}`
  if (tage < 0) return `Flip überfällig seit ${Math.abs(tage)} T`
  if (tage === 0) return 'Flip heute geplant'
  return `Flip geplant ${datum} · in ${tage} T`
}

/**
 * Die Kurzform für Kartenköpfe: „Wachstum Tag 20", „Blüte Tag 22".
 *
 * Kommt aus demselben Strahl wie alles andere. Die Grow-Karten hatten dafür
 * eine eigene Rechnung, die ab Startdatum zählte — also die Keimzeit
 * mitzählte — und jede laufende Phase „Veg" nannte.
 */
export function currentPhaseLabel(timeline: PhaseTimeline): string | null {
  const laufend = timeline.phases.find((phase) => phase.state === 'current')
  if (!laufend || laufend.dayInPhase == null) return null
  return `${PHASEN_ANZEIGE[laufend.name]} Tag ${laufend.dayInPhase}`
}
