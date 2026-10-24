import ts from 'typescript'

/**
 * Deutsche Wörter ohne Umlaut („Zelt waehlen“, „fuer“, „geloescht“) in Texten, die ein Mensch liest.
 *
 * <b>Der Anlass (24.10.2026).</b> Ein Durchgang durch die ganze App fand rund 105 Meldungen im Server
 * und eine Seite in der Oberfläche mit ae/oe/ue/ss statt ä/ö/ü/ß. Der Test aus cc.265 sah nur die
 * Controller und eine feste Wortliste; Dienste, KI-Werkzeuge und Oberfläche blieben außen vor.
 *
 * <b>Wie.</b> Eine Wortliste von Wortanfängen (keine Regel „ae → ä“: „Israel“, „neue“, „Steuerung“,
 * „Quelle“, „true“ wären falsch). Gelesen werden nur Zeichenketten und JSX-Text mit Leerzeichen
 * darin — Schlüssel, Klassen und Bezeichner (`ueberblick`, `laeuftGerade`) bleiben unberührt.
 * Dieselbe Liste dient dem Test und dem einmaligen Umschreiben.
 */

/** Wortanfang in ASCII → deutsche Schreibung (klein). Längere Einträge stehen vor kürzeren. */
const STAMM: ReadonlyArray<readonly [string, string]> = [
  ['ueberfaellig', 'überfällig'], ['zaehlerstaend', 'zählerständ'], ['zurueckgerechnet', 'zurückgerechnet'],
  ['naehrloesung', 'nährlösung'], ['unvollstaendig', 'unvollständig'], ['unveraendert', 'unverändert'],
  ['kulturunabhaengig', 'kulturunabhängig'], ['abhaengig', 'abhängig'], ['ausgefuehr', 'ausgeführ'],
  ['zusaetzlich', 'zusätzlich'], ['vollstaendig', 'vollständig'], ['ausdruecklich', 'ausdrücklich'],
  ['kompatibilitaet', 'kompatibilität'], ['veroeffentlicht', 'veröffentlicht'], ['umwaelzpumpe', 'umwälzpumpe'],
  ['einschaltverzoegerung', 'einschaltverzögerung'], ['verzoeger', 'verzöger'], ['foerderleistung', 'förderleistung'],
  ['quarantaene', 'quarantäne'], ['schaedling', 'schädling'], ['oberflaeche', 'oberfläche'],
  ['messgroess', 'messgröß'], ['zielgeraet', 'zielgerät'], ['wasserkuehler', 'wasserkühler'],
  ['aufgefuell', 'aufgefüll'], ['zugehoer', 'zugehör'], ['hoechst', 'höchst'], ['geaender', 'geänder'],
  ['gewaehl', 'gewähl'], ['gewuensch', 'gewünsch'], ['bestaetig', 'bestätig'], ['unbestaetig', 'unbestätig'],
  ['begruend', 'begründ'], ['eingeschaetzt', 'eingeschätzt'], ['uebernomm', 'übernomm'], ['rueckwaerts', 'rückwärts'],
  ['aufzaehlung', 'aufzählung'], ['naechtlich', 'nächtlich'],
  ['belueftung', 'belüftung'], ['praefix', 'präfix'], ['kuerzel', 'kürzel'], ['entitaet', 'entität'],
  ['duenger', 'dünger'], ['bluetewoche', 'blütewoche'], ['bluetezeit', 'blütezeit'], ['bluetetag', 'blütetag'], ['bluetephase', 'blütephase'],
  ['fuenf', 'fünf'], ['gegenueber', 'gegenüber'], ['tageszaehl', 'tageszähl'], ['integritaet', 'integrität'], ['liess', 'ließ'], ['gross', 'groß'], ['saemling', 'sämling'], ['glaeser', 'gläser'], ['blaetter', 'blätter'],
  ['auffaellig', 'auffällig'], ['ungueltig', 'ungültig'], ['gueltig', 'gültig'], ['unmoeglich', 'unmöglich'],
  ['moeglich', 'möglich'], ['noetig', 'nötig'], ['benoetig', 'benötig'], ['eintraeg', 'einträg'],
  ['waehrend', 'während'], ['waehl', 'wähl'], ['naechst', 'nächst'], ['faellig', 'fällig'], ['faellt', 'fällt'],
  ['laeuft', 'läuft'], ['laeufe', 'läufe'], ['laesst', 'lässt'], ['haengt', 'hängt'], ['haengen', 'hängen'],
  ['behaelt', 'behält'], ['haelt', 'hält'], ['haelfte', 'hälfte'], ['aehnlich', 'ähnlich'], ['waermer', 'wärmer'],
  ['waere', 'wäre'], ['zaehl', 'zähl'], ['staend', 'ständ'], ['baender', 'bänder'], ['zustaend', 'zuständ'],
  ['ergaenz', 'ergänz'], ['aender', 'änder'], ['aelter', 'älter'], ['aelteste', 'älteste'], ['uebrig', 'übrig'],
  ['ueber', 'über'], ['fuellung', 'füllung'], ['fuellstand', 'füllstand'], ['fuell', 'füll'], ['fuehr', 'führ'],
  ['fuer', 'für'], ['wofuer', 'wofür'], ['dafuer', 'dafür'], ['drueber', 'drüber'], ['pruef', 'prüf'],
  ['geraet', 'gerät'], ['kuehl', 'kühl'], ['kuerz', 'kürz'], ['laengst', 'längst'], ['frueher', 'früher'],
  ['spaeter', 'später'], ['zurueck', 'zurück'], ['rueck', 'rück'], ['geloesch', 'gelösch'], ['loesch', 'lösch'],
  ['koenn', 'könn'], ['duerf', 'dürf'], ['oeffn', 'öffn'], ['spuel', 'spül'], ['verraet', 'verrät'],
  ['groess', 'größ'], ['schliess', 'schließ'], ['fliess', 'fließ'], ['ausserhalb', 'außerhalb'], ['ausser', 'außer'],
  ['gruen', 'grün'], ['haerte', 'härte'], ['waerme', 'wärme'], ['raeume', 'räume'], ['tuer', 'tür'],
  ['luecke', 'lücke'], ['spruenge', 'sprünge'], ['traegt', 'trägt'], ['faehrt', 'fährt'], ['ausloeser', 'auslöser'],
  ['wuensch', 'wünsch'], ['schluessel', 'schlüssel'], ['hoehe', 'höhe'], ['hoeher', 'höher'], ['naehr', 'nähr'],
  ['loesung', 'lösung'], ['schaetz', 'schätz'], ['anlaeufe', 'anläufe'], ['durchgaeng', 'durchgäng'],
  ['waechter', 'wächter'], ['bestaend', 'beständ'],
]

const BUCHSTABEN = 'A-Za-zÄÖÜäöüß0-9_'
const WORT = new RegExp(`(?<![${BUCHSTABEN}])([${BUCHSTABEN}]+)`, 'g')
/** Reihenfolge: lange Anfänge zuerst, damit „ueberfaellig“ vor „ueber“ greift. */
const SORTIERT = [...STAMM].sort((a, b) => b[0].length - a[0].length)

function gross(erster: string): string {
  return erster.toUpperCase()
}

/** Schreibt ein einzelnes Wort um, wenn es mit einem Wortanfang der Liste beginnt; sonst unverändert. */
function wortUmschreiben(wort: string): string {
  // „bluete“ klein ist ein Kennwert der KI-Werkzeuge (befehl=preset: veggie oder bluete); nur das Hauptwort „Bluete“ ist Sprache.
  if (wort === 'bluete') return wort
  // Wörter mit Großbuchstaben mitten drin sind Bezeichner (laeuftGerade, ForkKiSchluessel), keine Sprache.
  if (/[a-zäöüß][A-ZÄÖÜ]/.test(wort) || /[0-9_]/.test(wort)) return wort
  const klein = wort.toLowerCase()
  const nurGross = wort.length > 1 && wort === wort.toUpperCase()
  for (const [ascii, deutsch] of SORTIERT) {
    if (!klein.startsWith(ascii)) continue
    let ergebnis = deutsch + wortUmschreiben(klein.slice(ascii.length))
    if (nurGross) ergebnis = ergebnis.toUpperCase()
    else if (wort[0] !== wort[0].toLowerCase()) ergebnis = gross(ergebnis[0]) + ergebnis.slice(1)
    return ergebnis
  }
  return wort
}

/** Eine Hauptwort-Blüte mit Großbuchstaben (Bluete → Blüte). */
function bluete(wort: string): string {
  return wort === 'Bluete' ? 'Blüte' : wort
}

/** Der Text mit allen Wörtern aus der Liste in deutscher Schreibung. */
export function mitUmlauten(text: string): string {
  return geschuetzt(text, (wort, nach) => (nach === '=' ? wort : bluete(wortUmschreiben(wort))))
}

/**
 * Ruft `f` für jedes Wort auf, das nicht in `{…}` steht (Platzhalter wie `{Zaehler}` sind Namen der Logvorlage)
 * und nicht vor einem `=` (Parameter der KI-Werkzeuge wie `ergaenzen=false`).
 */
function geschuetzt(text: string, f: (wort: string, nach: string) => string): string {
  let tiefe = 0
  let aus = ''
  let letzter = 0
  for (const m of text.matchAll(WORT)) {
    const von = m.index
    for (let k = letzter; k < von; k++) { if (text[k] === '{') tiefe++; else if (text[k] === '}') tiefe = Math.max(0, tiefe - 1) }
    aus += text.slice(letzter, von)
    const nach = text[von + m[1].length] ?? ''
    aus += tiefe > 0 ? m[1] : f(m[1], nach)
    letzter = von + m[1].length
  }
  return aus + text.slice(letzter)
}

function mitUmlautenWort(wort: string): string {
  return bluete(wortUmschreiben(wort))
}

/** Die Wörter im Text, die noch ohne Umlaut dastehen (leer = in Ordnung). */
export function ohneUmlaut(text: string): string[] {
  const funde: string[] = []
  geschuetzt(text, (wort, nach) => {
    if (nach !== '=' && mitUmlautenWort(wort) !== wort) funde.push(wort)
    return wort
  })
  return funde
}

export interface Textstueck {
  readonly von: number
  readonly bis: number
  readonly text: string
}

const ERKENNT_CODE = /\b(SELECT|INSERT|UPDATE|DELETE|CREATE|ALTER|PRAGMA|FROM|WHERE)\b.*\b(FROM|INTO|SET|TABLE|WHERE|VALUES)\b|\{%|\{\{.*\}\}|states\(|=>/s

/** Ein Textstück ist Sprache, wenn ein Mensch es lesen könnte: Leerzeichen drin und kein SQL/Vorlagen-Code. */
function istSprache(text: string, einWort = false): boolean {
  if (ERKENNT_CODE.test(text) || (!einWort && !/ /.test(text))) return false
  if (einWort && (!/[A-Za-zÄÖÜäöüß]{3}/.test(text) || /^[a-z0-9_]+\.[a-z0-9_.]+$/.test(text.trim()))) return false // Entitäts-Kennungen (switch.kuehler)
  // Klassenlisten („st-geraet-wert is-faint“) und Suchwörter (alles klein, mit Bindestrich) sind keine Sprache.
  const teile = text.trim().split(/\s+/)
  if (teile.every((t) => /^[a-z0-9_:\-/[\].%]+$/.test(t)) && teile.some((t) => t.includes('-'))) return false
  return true
}

/** JSX-Attribute, deren Wert ein Mensch liest — hier genügt ein einzelnes Wort („Umwaelzpumpe“). */
const SICHTBARE_ATTRIBUTE: ReadonlySet<string> = new Set(['label', 'title', 'placeholder', 'aria-label', 'alt', 'hint', 'eyebrow', 'subtitle', 'kicker', 'caption', 'legend', 'text', 'description'])

/** Texte einer TypeScript-/TSX-Datei: Zeichenketten, Vorlagenteile und JSX-Text. */
function textstueckeTs(name: string, quelle: string): Textstueck[] {
  const datei = ts.createSourceFile(name, quelle, ts.ScriptTarget.Latest, true, name.endsWith('x') ? ts.ScriptKind.TSX : ts.ScriptKind.TS)
  const aus: Textstueck[] = []
  const nimm = (von: number, bis: number, einWort = false): void => {
    const text = quelle.slice(von, bis)
    if (istSprache(text, einWort)) aus.push({ von, bis, text })
  }
  const geh = (knoten: ts.Node): void => {
    if (ts.isImportDeclaration(knoten) || ts.isExportDeclaration(knoten)) return
    if (ts.isStringLiteral(knoten) && ts.isJsxAttribute(knoten.parent) && SICHTBARE_ATTRIBUTE.has(knoten.parent.name.getText())) nimm(knoten.getStart() + 1, knoten.getEnd() - 1, true)
    else if (ts.isStringLiteral(knoten) || ts.isNoSubstitutionTemplateLiteral(knoten)) nimm(knoten.getStart() + 1, knoten.getEnd() - 1)
    else if (ts.isTemplateHead(knoten)) nimm(knoten.getStart() + 1, knoten.getEnd() - 2)
    else if (ts.isTemplateMiddle(knoten)) nimm(knoten.getStart() + 1, knoten.getEnd() - 2)
    else if (ts.isTemplateTail(knoten)) nimm(knoten.getStart() + 1, knoten.getEnd() - 1)
    else if (ts.isJsxText(knoten)) nimm(knoten.getStart(), knoten.getEnd(), true)
    ts.forEachChild(knoten, geh)
  }
  geh(datei)
  return aus
}

/** Texte einer C#-Datei: reguläre, wörtliche (@"") und interpolierte ($"") Zeichenketten ohne die {…}-Löcher. */
function textstueckeCs(quelle: string): Textstueck[] {
  const aus: Textstueck[] = []
  const n = quelle.length
  let i = 0
  const nimm = (von: number, bis: number): void => {
    const text = quelle.slice(von, bis)
    if (istSprache(text)) aus.push({ von, bis, text })
  }
  while (i < n) {
    const c = quelle[i]
    if (c === '/' && quelle[i + 1] === '/') { while (i < n && quelle[i] !== '\n') i++; continue }
    if (c === '/' && quelle[i + 1] === '*') { const e = quelle.indexOf('*/', i + 2); i = e < 0 ? n : e + 2; continue }
    if (c === "'") { // Zeichenliteral
      i++
      while (i < n && quelle[i] !== "'") i += quelle[i] === '\\' ? 2 : 1
      i++
      continue
    }
    const vorspann = /^(\$@|@\$|\$|@)?"/.exec(quelle.slice(i, i + 4))
    if (c === '"' || ((c === '$' || c === '@') && vorspann)) {
      const marke = vorspann![1] ?? ''
      const interpoliert = marke.includes('$')
      const woertlich = marke.includes('@')
      if (!woertlich && quelle.startsWith('"""', i + marke.length)) { // rohe Zeichenkette
        const e = quelle.indexOf('"""', i + marke.length + 3)
        i = e < 0 ? n : e + 3
        continue
      }
      i += marke.length + 1
      let start = i
      while (i < n) {
        const z = quelle[i]
        if (woertlich ? z === '"' && quelle[i + 1] === '"' : false) { i += 2; continue }
        if (z === '"') break
        if (!woertlich && z === '\\') { i += 2; continue }
        if (interpoliert && z === '{') {
          if (quelle[i + 1] === '{') { i += 2; continue }
          nimm(start, i)
          let tiefe = 1
          i++
          while (i < n && tiefe > 0) { if (quelle[i] === '{') tiefe++; else if (quelle[i] === '}') tiefe--; i++ }
          start = i
          continue
        }
        i++
      }
      nimm(start, i)
      i++
      continue
    }
    i++
  }
  return aus
}

/** Alle Textstücke einer Quelldatei (.ts, .tsx, .cs), die ein Mensch lesen kann. */
export function textstuecke(dateiname: string, quelle: string): Textstueck[] {
  if (/\.cs$/.test(dateiname)) return textstueckeCs(quelle)
  if (/\.tsx?$/.test(dateiname)) return textstueckeTs(dateiname, quelle)
  return []
}

/** Die Datei mit allen Textstücken in deutscher Schreibung (für das einmalige Umschreiben). */
export function dateiMitUmlauten(dateiname: string, quelle: string): string {
  let aus = quelle
  for (const s of [...textstuecke(dateiname, quelle)].sort((a, b) => b.von - a.von)) {
    aus = aus.slice(0, s.von) + mitUmlauten(s.text) + aus.slice(s.bis)
  }
  return aus
}
