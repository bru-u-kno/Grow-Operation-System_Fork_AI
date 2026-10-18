/**
 * Wie die Sorte eines Grows genannt wird — eine Regel für die ganze App.
 *
 * <b>Der Anlass (31.08.2026).</b> Der Tester hat definiert, was ein Grow ist:
 * „ein Durchgang in einem RDWC/DWC, der N Pflanzen mit N verschiedenen
 * Sorten/Phenos beinhalten kann." Sechs Ansichten gaben trotzdem <code>strain</code>
 * aus — <b>ein</b> Feld, <b>ein</b> Name. Bei zwei Sorten im selben Becken war
 * das eine Falschaussage: Grow-Liste, Zelt-Detail, Messformular, Addback-Kopf
 * und Addback-Übersicht nannten alle die Hauptsorte, als wäre sie die einzige.
 *
 * <b>Warum ein Helfer und nicht fünfmal dieselbe Bedingung.</b> Genau eine
 * Ansicht — die Grow-Detailseite — konnte „gemischt" schon, weil ihre
 * Pflanzen-Karte es ihr meldete. Fünf weitere hätten das nachbauen müssen, und
 * in vier Wochen hätten drei davon eine andere Schreibweise. Dieselbe Regel
 * wie „eine Wahrheit je Zahl", nur für einen Text.
 */

/** Was eine Ansicht über die Sorte eines Grows wissen muss. */
export type SortenQuelle = {
  /** Die Hauptsorte als Text — Rückfall, wenn keine Pflanze erfasst ist. */
  strain?: string | null
  /** Die Sorten der erfassten Pflanzen; leer heisst „keine erfasst". */
  pflanzenSorten?: readonly string[] | null
  /**
   * Tragen alle erfassten Pflanzen die Hauptsorte? Kommt vom Server, der die
   * Ids vergleicht — nicht die Namen.
   */
  nurHauptsorte?: boolean | null
}

/**
 * Der Name, der auf den Schirm gehört.
 *
 * - keine Pflanze erfasst → die Hauptsorte (oder <code>null</code>)
 * - eine Sorte           → ihr Name
 * - mehrere              → „gemischt (N)"
 */
export function sortenText(grow: SortenQuelle): string | null {
  const sorten = grow.pflanzenSorten ?? []
  if (sorten.length === 0) return grow.strain ?? null
  if (sorten.length === 1) return sorten[0]
  return `gemischt\u00a0(${sorten.length})`
}

/**
 * Die Sorten ausgeschrieben — für einen Titel oder eine zweite Zeile, wo Platz
 * ist. Gibt <code>null</code>, wenn es nichts zu ergänzen gibt.
 */
export function sortenAufzaehlung(grow: SortenQuelle): string | null {
  const sorten = grow.pflanzenSorten ?? []
  return sorten.length > 1 ? sorten.join(' · ') : null
}

/** Ob dieser Grow mehr als eine Sorte führt. */
export function istGemischt(grow: SortenQuelle): boolean {
  return (grow.pflanzenSorten?.length ?? 0) > 1
}

/**
 * Ob der Züchter des Laufs zu der Sorte gehört, die angezeigt wird.
 *
 * <b>Der Anlass (01.09.2026).</b> `grow.breeder` gehört zur <b>Hauptsorte</b>
 * des Laufs. Stehen in den Töpfen andere Sorten, ist er eine Falschaussage:
 * die Grow-Detailseite schrieb „Gorilla Glue (Testdaten) · Royal Queen Seeds"
 * — richtige Sorte, Züchter der anderen. Auf der Zelt-Seite dasselbe. Beide
 * Male vom Prüfer gefunden, beide Male an derselben Wurzel: eine Angabe je
 * Lauf für etwas, das je Pflanze gilt.
 *
 * <b>Der zweite Anlauf war auch falsch.</b> Zuerst wurden die NAMEN
 * verglichen — als Teilzeichenkette, weil die Bibliothek ausführlicher führen
 * darf als das Freitextfeld („White Widow" gegen „White Widow (Testdaten)").
 * Der Prüfer hat sich daraufhin „Northern Lights" und „Northern Lights Auto"
 * angelegt: der eine Name enthält den anderen, und die Seite schrieb prompt
 * „Northern Lights Auto · Sensi Seeds" — den Züchter der falschen Sorte. Eine
 * Heuristik verschiebt die Grenze, sie beseitigt sie nicht.
 *
 * <b>Die Regel.</b> Der Server vergleicht die Ids und schickt das Ergebnis als
 * <code>nurHauptsorte</code> mit. Hier wird nichts mehr geraten.
 */
export function zuechterPasst(grow: SortenQuelle): boolean {
  // Keine Pflanze einzeln erfasst: `strain` ist alles, was es gibt, und der
  // Züchter gehört dazu.
  if ((grow.pflanzenSorten?.length ?? 0) === 0) return true

  return grow.nurHauptsorte === true
}
