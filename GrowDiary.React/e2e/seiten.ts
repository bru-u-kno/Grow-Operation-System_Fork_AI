import { readFileSync } from 'node:fs'

/**
 * Alle Seiten, die eine Oberflächen-Prüfung ansehen soll — an einer Stelle.
 *
 * <b>Der Anlass.</b> Vier Prüfungen pflegten vier abgetippte Seitenlisten, und
 * jede war anders lang. In der Zahlen-Prüfung fehlte `/messung` (Einzahl) —
 * genau dort stand das gerechnete VPD als „1.00 kPa" und blieb monatelang
 * stehen. Eine handgeschriebene Liste kann nur an dem scheitern, was schon
 * draufsteht; das ist dieselbe Regel wie „Zählungen statt Listen" in CLAUDE.md,
 * nur eine Ebene höher.
 *
 * <b>Woher die Liste kommt.</b> Aus `src/navigation.ts` — dem Menü, das die App
 * wirklich anzeigt. Kommt ein Menüpunkt dazu, prüfen ihn alle Prüfungen sofort
 * mit, ohne dass jemand daran denkt.
 *
 * <b>Was hinzukommt.</b> Detailseiten haben keine Menü-Zeile, gehören aber zu
 * den inhaltsreichsten der App. Sie stehen unten ausgeschrieben — mit der Id
 * aus dem Demobestand, der genau ein Zelt, ein System und einen laufenden Grow
 * anlegt.
 */

/** Die Seiten aus dem Menü — die Wahrheit steht in `navigation.ts`. */
function ausDemMenue(): string[] {
  const quelltext = readFileSync(new URL('../src/navigation.ts', import.meta.url), 'utf8')
  const pfade = [...quelltext.matchAll(/\{\s*to:\s*'([^']+)'/g)].map((treffer) => treffer[1])

  if (pfade.length < 15) {
    throw new Error(
      `Nur ${pfade.length} Menü-Einträge in navigation.ts gefunden — `
      + 'die Prüfungen liefen damit fast leer. Hat sich die Schreibweise geändert?',
    )
  }

  return pfade
}

/**
 * Detailseiten, die kein Menü hat.
 *
 * Die Ids stammen aus dem Demobestand: ein Zelt, ein Hydro-System, ein
 * laufender Grow. Fehlt der Bestand, meldet die Prüfung das über
 * `darfUeberspringen` — sie erfindet sich keine leere Seite.
 */
const DETAILSEITEN = [
  '/grows/1',
  '/grows/new',
  '/grows/1/setup',
  '/zelte/1',
  '/hydro/1',
  '/messungen',
  '/diagnose',
  '/journal',
  '/sops',

  // Diese drei stehen nicht im Menü, sind aber volle Seiten: die Einstellungen
  // hängen am Zahnrad, „Erste Schritte" am ersten Start, „Release & Daten" an
  // der Sicherung.
  '/einstellungen',
  '/start',
  '/release',

  /* Die Formulare und die zweite Ebene. Sie fehlten bis zum 01.09.2026 alle
     sieben — auf /grows/1/harvest standen die Summen deshalb monatelang
     englisch („21.5 g" unter einem Feld, in dem „21,5" stand). Gehalten wird
     die Liste jetzt von src/seitenliste-vollstaendig.node.test.ts: sie zählt
     die Routen aus App.tsx ab. */
  '/messungen/new',
  '/grows/1/harvest',
  '/grows/1/addback',
  '/zelte/new',
  '/hydro/new',
  '/dosierung/neu',
  '/dosierung/1',

  // Fork AI (forkai.20): die Detailseite einer Steuerung. Die Übersicht
  // `/steuerung` kommt aus dem Menü; diese hier trägt die Zahlenfelder und
  // gehört deshalb in die Zahlen- und Handy-Prüfung.
  '/steuerung/co2',
  // `/steuerung/1` ist der Pfad, den die Routenprüfung für `:modul` bildet.
  // Er zeigt bewusst eine Leerseite statt ersatzweise CO₂ — auch das gehört
  // geprüft.
  '/steuerung/1',
]

/** Jede Seite genau einmal. */
export const ALLE_SEITEN: string[] = [...new Set([...ausDemMenue(), ...DETAILSEITEN])]

/**
 * Seiten, die für Text-Prüfungen nichts hergeben — mit Grund.
 *
 * Kein Freibrief: hier steht nur, was gar keinen eigenen Inhalt hat.
 */
export const OHNE_TEXTPRUEFUNG: Record<string, string> = {
  '/handy': 'Zeigt einen QR-Code und vier Sätze Anleitung; der Code ist ein Bild.',
  '/release': 'Export/Import-Werkzeuge — Dateinamen und Versionen sind bewusst technisch.',
}

/** Was eine Text-Prüfung ansehen soll. */
export const TEXTSEITEN: string[] = ALLE_SEITEN.filter((pfad) => !(pfad in OHNE_TEXTPRUEFUNG))
