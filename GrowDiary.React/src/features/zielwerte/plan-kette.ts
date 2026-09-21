/** Fork AI (forkai.133): Hilfen der Kette Plan · Grenzwerte · Handy (ohne React). */

/** „notify.mobile_app_bruno_smartphone_1" → „Bruno Smartphone 1". */
export function geraeteName(dienst: string): string {
  const roh = dienst.replace(/^notify\./, '').replace(/^mobile_app_/, '')
  return roh.split('_').filter(Boolean).map((w) => w.charAt(0).toUpperCase() + w.slice(1)).join(' ') || dienst
}
