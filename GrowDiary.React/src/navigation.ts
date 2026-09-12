/* src/navigation.ts
   Sortiert nach einer einzigen Frage: wie oft fasst man das an?

   Jetzt        täglich          Live, Messen, Addback, Aufgaben
   Pflanzen     mehrmals/Woche   Grows, Diagnose, Journal, Sorten, Archiv
   Betrieb      alle paar Wochen Dosierung, Sensoren, Regeln, Sollwerte
   Einrichtung  einmal           Zelte, Hydro, Wasser, Home Assistant, Handy
   Wissen       zum Nachschlagen SOPs, Einkaufsliste, Mappe

   Vorher lagen acht Ziele unter „Anlage" — fünf davon öffnet man nach der
   ersten Woche nie wieder, drei (Dosierung, Sensoren, Regeln) fasst man
   laufend an und musste sie dazwischen heraussuchen.

   Zusammengelegt (bleibt so):
     Automatik + Grenzwerte + Benachrichtigungen  -> /regeln (Tabs)
     Sorten + Pheno-Hunt                          -> /sorten (Tabs)
     Ernte + Archiv                               -> /archiv (Tabs)
     Sensoren + Wartung + Kalibrierung            -> /sensoren (eine Tabelle)
   Alte Routen bleiben als Redirect erhalten (siehe legacyRedirects). */

export type NavLeaf = {
  to: string
  label: string
  end: boolean
  /** 'warn' faerbt das Badge (z. B. fällige Aufgabe) */
  badge?: 'count' | 'warn'
  keywords?: string
  /**
   * Fork AI: Zeichen für die Leiste am oberen Rand (siehe barCandidates).
   *
   * Bewusst ein Schriftzeichen und keine Icon-Bibliothek: das Add-on laedt
   * seine Oberflaeche ueber den Ingress von Home Assistant aus, und jede
   * zusaetzliche Schriftdatei ist dort eine weitere Anfrage, die beim ersten
   * Aufruf am Telefon sichtbar dauert. Die Kurzform darunter traegt ohnehin
   * die Bedeutung — das Zeichen ist nur der Ankerpunkt fuers Auge.
   */
  icon?: string
  /** Fork AI: kurze Beschriftung für die Leiste, wenn das Menüwort zu lang ist. */
  short?: string
}

export type NavGroup = {
  id: 'now' | 'grow' | 'ops' | 'plant' | 'library' | 'versuch'
  label: string
  items: NavLeaf[]
}

export const navGroups: NavGroup[] = [
  {
    id: 'now',
    label: 'Jetzt',
    items: [
      { to: '/', label: 'Live', end: true, icon: '◉', short: 'Live', keywords: 'dashboard übersicht start sensoren cockpit' },
      { to: '/messung', label: 'Messen', end: true, icon: '◎', short: 'Messen', keywords: 'werte eintragen ph ec do orp foto snapshot' },
      { to: '/addback', label: 'Addback', end: true, badge: 'warn', icon: '⤓', short: 'Addback', keywords: 'nachfüllen dünger dosieren reservoir aufdüngen' },
      // Stand 31.08.2026 hierher geholt. Das Formular lag im dritten Abschnitt
      // von /addback, und „Wasserwechsel" stand im ganzen Menü nur EINMAL — als
      // Schlagwort bei den Aufgaben. Wer das Wort tippte, landete also auf der
      // Aufgabenseite statt beim Eintrag. Genau das hat der Nutzer gemeldet.
      { to: '/wasserwechsel', label: 'Wasserwechsel', end: true, icon: '⟳', short: 'Wechsel', keywords: 'wechsel reservoir tank frisch austauschen leeren neu ansetzen rdwc nachtragen' },
      { to: '/aufgaben', label: 'Aufgaben', end: true, badge: 'count', icon: '☐', short: 'Aufgaben', keywords: 'todo risiken checkliste wartung routinen fällig' },
    ],
  },
  {
    // Hiess „Grow" — also genauso wie ihr erster Eintrag. In der Seitenleiste
    // stand „GROW / Grows" untereinander: eine Überschrift, die nichts sagt,
    // was der Eintrag darunter nicht schon sagt.
    id: 'grow',
    label: 'Pflanzen',
    items: [
      { to: '/grows', label: 'Grows', end: false, icon: '✿', short: 'Grows', keywords: 'lauf run pflanzen anbau' },
      { to: '/diagnose', label: 'Diagnose', end: true, icon: '⊕', short: 'Diagnose', keywords: 'problem mangel abweichung risiko krankheit sop' },
      // Das Messprotokoll. Stand bis beta.50 in KEINER Gruppe — und weil die
      // Suche ihre Eintraege aus diesen Gruppen baut, war die Seite damit auch
      // nicht suchbar: „Messungen“, „Sensorwerte“, „gemessen“, „automatisch“
      // lieferten alle nichts. Erreichbar war sie nur ueber einen Reiter tief
      // in einem Grow. Dasselbe Muster wie bei der Einkaufsliste in beta.42.
      //
      // Sie heisst „Messungen“ und nicht „Messprotokoll“, weil App.tsx die
      // Seite so betitelt und der Wegweiser-Test die Ueberschrift mit dem
      // Menuewort vergleicht.
      //
      // Steht hinter der Diagnose, nicht hinter dem Journal: beide beantworten
      // dieselbe Frage — laeuft der Grow im gruenen Bereich?
      { to: '/messungen', label: 'Messungen', end: true, icon: '∿', short: 'Verlauf', keywords: 'protokoll verlauf historie messwerte tabelle vergleich sensorwerte handmessung automatik ph ec' },
      { to: '/journal', label: 'Journal & Fotos', end: true, icon: '✎', short: 'Journal', keywords: 'tagebuch notizen bilder verlauf' },
      { to: '/sorten', label: 'Sorten & Pheno', end: true, icon: '❀', short: 'Sorten', keywords: 'strain genetik züchter keeper selektion' },
      // Steht VOR dem Archiv, weil es zeitlich davor liegt: nach der Ernte
      // laeuft das Aushaerten noch 30-60 Tage. Ins Archiv gehoert ein Lauf
      // erst, wenn auch das durch ist.
      { to: '/aushaerten', label: 'Aushärten', end: true, icon: '◔', short: 'Aushärten', keywords: 'curing cure glas gläser jar burping lüften feuchte hygrometer boveda nach der ernte trocknen fertig' },
      { to: '/archiv', label: 'Ernte & Archiv', end: true, icon: '▫', short: 'Archiv', keywords: 'harvest ertrag abgeschlossen vergleich' },
    ],
  },
  {
    // Was man anfasst, WÄHREND ein Grow läuft — nicht täglich, aber immer
    // wieder. Lag vorher zwischen Zelten und Home Assistant begraben.
    id: 'ops',
    label: 'Betrieb',
    items: [
      { to: '/dosierung', label: 'Dosierung', end: false, icon: '⚗', short: 'Dosierung', keywords: 'pumpe peristaltik ph minus plus säure nährstoff dosieren kalibrieren' },
      { to: '/geraete', label: 'Geräte & Entitäten', end: true, icon: '⧉', short: 'Geräte', keywords: 'entität entity home assistant zuordnung controller port sensor steckdose gerät hardware ha mapping' },
      { to: '/sensoren', label: 'Sensoren & Wartung', end: true, icon: '⚙', short: 'Sensoren', keywords: 'hardware geräte kalibrierung inventar wechseln lebensdauer' },
      { to: '/regeln', label: 'Regeln & Automatik', end: true, icon: '≡', short: 'Regeln', keywords: 'grenzwerte schwellen alarm push zeitplan automation' },
      { to: '/sollwerte', label: 'Sollwert-Profile', end: true, icon: '◈', short: 'Sollwerte', keywords: 'zielwerte setpoints profil rdwc dwc phasen erfahrung eigene werte' },
      { to: '/cropsteering', label: 'Crop Steering', end: true, icon: '❄', short: 'Steering', keywords: 'wassertemperatur kühler chiller steckdose nachtabsenkung rampe tag nacht wurzeltemperatur steuern' },
      // Fork AI (forkai.6). Steht unter Betrieb, weil man es anfasst, WÄHREND
      // ein Grow läuft: jede neue CO₂-Flasche, jeder Kanister Dünger wird hier
      // erfasst. Das Archiv rechnet den Strom aus Lampen-Watt; hier kommt er
      // vom Zähler.
      { to: '/steuerung', label: 'Steuerung', end: false, icon: '⊚', short: 'Steuerung', keywords: 'co2 begasung regelung leitstand entfeuchter chiller abluft licht sollwerte automatik ventil dosierung klima' },
      { to: '/kosten', label: 'Kosten', end: true, icon: '€', short: 'Kosten', keywords: 'strom kwh euro preis zähler verbrauch verbrauchsartikel co2 flasche nachfüllung nachfüllen dünger kanister laufzeit prognose je tag je pflanze durchgang' },
    ],
  },
  {
    // Eine eigene Gruppe statt eines Eintrags unter Betrieb: was hier steht,
    // ist ein Versuch und soll auch so aussehen. Die Wegweiser-Zaehlung
    // verlangt ausserdem, dass die Kopfzeile der Seite ihre Gruppe nennt —
    // Versuch auf der Seite und Betrieb im Menue waere genau der Widerspruch,
    // den sie seit beta.42 sucht.
    id: 'versuch',
    label: 'Versuch',
    items: [
      { to: '/ac-test', label: 'Zelt (AC-Test)', end: true, keywords: 'ac infinity controller uis licht lüfter abluft stufe leistung 0 10 dimmen versuch test gerät steuern zentrale' },
    ],
  },
  {
    // Einmal einrichten, dann in Ruhe lassen. Deshalb steht die Gruppe unten.
    id: 'plant',
    label: 'Einrichtung',
    items: [
      { to: '/zelte', label: 'Zelte & Räume', end: false, icon: '▣', short: 'Zelte', keywords: 'tent kamera lichtzyklus klima abluft' },
      { to: '/hydro', label: 'Hydro-Systeme', end: false, keywords: 'rdwc dwc reservoir tank pumpe sites layout' },
      // Was aus dem Hahn kommt, gehört zur Anlage wie das Reservoir selbst.
      // „Leitungswasser" war zu eng: hier steht auch das Wasser NACH der
      // eigenen Aufbereitung (Osmose/VE). Wer unter Anlage nach „Wasser"
      // sucht, soll es finden — genau das war die Rueckmeldung.
      { to: '/wasser', label: 'Wasser', end: true, keywords: 'wasser wasserprofil trinkwasser leitungswasser osmose ro umkehrosmose ve entsalzt stadtwerk bericht härte calcium magnesium leitfähigkeit ec kalk weich hart' },
      { to: '/home-assistant', label: 'Home Assistant', end: true, keywords: 'ha entitäten verbindung integration mapping kamera' },
      { to: '/handy', label: 'Aufs Handy holen', end: true, keywords: 'mobil smartphone qr code startbildschirm lesezeichen app icon telefon' },
    ],
  },
  {
    id: 'library',
    label: 'Wissen',
    items: [
      // Ein Eintrag wie im Entwurf: SOPs, Bibliothek und Symptome sind EINE
      // durchsuchbare Sammlung. Laufende SOPs wohnen bei den Aufgaben und im
      // Grow-Detail; die Ersten Schritte öffnet man aus den Einstellungen.
      { to: '/wissen', label: 'SOPs & Bibliothek', end: true, icon: '▤', short: 'Wissen', keywords: 'sop anleitung ablauf prozedur checkliste nachschlagen growplan quellen doku symptome bibliothek wissen' },
      // Hing zugeklappt am Fuss der Wissensseite: kein Menuepunkt, kein
      // Suchtreffer. Wer „Einkaufsliste" tippte, bekam „Nichts gefunden" —
      // ausgerechnet in der Lage, fuer die die Liste gemacht ist.
      { to: '/einkaufsliste', label: 'Einkaufsliste', end: true, icon: '☑', short: 'Einkauf', keywords: 'einkauf einkaufen kaufen material besorgen laden bestellen vorrat zubehör liste posten was brauche ich' },
      // Nicht „KI-Berater": in Grow OS steckt keine KI. Die Seite packt das
      // Fachwissen der Anlage zum Mitnehmen zusammen — der Name muss das
      // sagen, sonst sucht man eine Funktion, die es nicht gibt.
      { to: '/berater', label: 'Mappe für eigene KI', end: true, keywords: 'ki agent assistent chatgpt claude ollama mappe export lagebericht prompt prueffragen berater' },
    ],
  },
]

/**
 * Mobile Bottom-Nav = die vier Ziele der Gruppe "Jetzt".
 *
 * Fork AI: nur noch als Rueckfalloption in Gebrauch. Die Leiste zeigt jetzt
 * `useNavBar()`, weil hier fuenf Ziele standen, die Leiste aber vier Spalten
 * hatte — das fuenfte („Aufgaben“) brach in eine zweite Zeile um, und die
 * Kopfflaeche war weiterhin auf 108 px gerechnet. Genau darunter verschwand
 * die obere Haelfte der Bewertungsscheibe. Siehe AppShell.tsx.
 */
export const mobilePrimaryNav = navGroups[0].items

/**
 * Fork AI: alles, was in die Leiste am oberen Rand darf.
 *
 * Nicht jedes Ziel: was man einmal einrichtet (Hydro, Wasser, Home Assistant)
 * gehoert hinter „Mehr“ und nicht auf einen der fuenf knappen Plaetze. Die
 * Auswahl bleibt trotzdem grosszuegig — wer taeglich dosiert, soll die
 * Dosierung vorne haben duerfen.
 */
export const barCandidates: NavLeaf[] = navGroups
  .flatMap((group) => group.items)
  .filter((item) => item.icon != null)

/**
 * Werkseinstellung der Leiste.
 *
 * Fuenf Ziele, nach derselben Frage sortiert wie das ganze Menue: wie oft
 * fasst man das an? Messen und Addback sind absichtlich NICHT dabei — beide
 * sind Eintraege und keine Ansichten, sie haengen am Erfassen-Knopf in der
 * Titelzeile. Sonst stuende dasselbe zweimal auf dem Schirm, wie vorher bei
 * „Messen“ als Reiter UND „Messung erfassen“ als Knopf.
 */
export const defaultBarRoutes: string[] = ['/', '/messungen', '/wissen', '/kosten', '/aufgaben', '/steuerung']

/**
 * Alte Pfade, die weiterhin funktionieren müssen.
 *
 * Mit Tab-Angabe, sonst landet ein Lesezeichen auf /alarme zwar auf der richtigen
 * Seite, aber auf deren erstem Tab — also bei der Automatik statt bei den
 * Grenzwerten. Das ist schlimmer als ein toter Link, weil es unbemerkt bleibt.
 */
export const legacyRedirects: Record<string, string> = {
  '/automatik': '/regeln?tab=automatik',
  '/alarme': '/regeln?tab=grenzwerte',
  '/benachrichtigungen': '/regeln?tab=push',
  // Die KI wurde entfernt; das Lesezeichen darf trotzdem nicht ins Leere laufen.
  '/assistent': '/regeln',
  '/phenohunt': '/sorten',
  '/hardware': '/sensoren',
  '/analyse': '/archiv',
  '/action': '/aufgaben',
}

export const searchablePages = navGroups.flatMap((group) =>
  group.items.map((item) => ({
    label: item.label,
    route: item.to,
    keywords: `${group.label} ${item.keywords ?? ''}`,
  })))

export function isNavLeafActive(item: NavLeaf, pathname: string): boolean {
  return item.end ? pathname === item.to : pathname === item.to || pathname.startsWith(`${item.to}/`)
}
