import { useEffect, useMemo, useState, type FormEvent } from 'react'
import { apiFetch, ApiRequestError } from '../../api'
import FileInput from '../../components/FileInput'
import { V1Alert, V1Button, V1Field, V1Section, V1Switch, V1Tabs } from '../../components/v1'
import type {
  AddbackLogKind,
  AddbackResultDto,
  AddbackVorgangDto,
  AddbackVorgangRequest,
  ChangeoutKind,
  GrowDetail,
  MischplanVorschlag,
  VorgangBuchungRequest,
  VorgangMessungRequest,
  WasserwechselSensorDto,
  WasserwechselStandDto,
  WasserwechselVorgangDto,
  WasserwechselVorgangRequest,
  WaterSource,
} from '../../types'
import { classNames, toLocalInputValue } from '../../utils'
import { feldText, istUnlesbar, unlesbarMeldung, unlesbareFelder, zahlOderNull } from '../../zahlenfeld'
import {
  aenderung,
  alleAufVorschlag,
  anteilPlanDosis,
  ecMitDeinenMengen,
  ecTankDanach,
  istGeaendert,
  meineWiederEinsetzen,
  meinsVerfuegbar,
  mengeDerZeile,
  tagebuchZeile,
  teileText,
  wasserZeilen,
  wertDerZeile,
  zahl,
  NACHFUELL_ART,
  type AblaufZeile,
  type Vorbelegung,
  type Werte,
} from './ablauf-rechnung'
import './vorgang-ablauf.css'

type Schritt = 1 | 2 | 3 | 4
type Messfelder = { ec: string; ph: string; wt: string; do: string; orp: string }
type Artikel = { id: number; name: string; einheit: string; aktiv: boolean }

/** Welcher Vorgang: der Wasserwechsel (Etappe 1) oder das Nachfüllen (Etappe 3). */
export type AblaufArt = 'wasserwechsel' | 'addback'

const LEER: Messfelder = { ec: '', ph: '', wt: '', do: '', orp: '' }
const WASSER_NAME: Record<WaterSource, string> = { Tap: 'Leitungswasser', RO: 'Osmose', Mixed: 'Mischung' }

/**
 * Was die beiden Abläufe unterscheidet — Wörter, Ziel der Anfrage, Prüfkennungen.
 *
 * Alles andere ist derselbe Ablauf: vorher, Wasser und Zugaben, nachher,
 * speichern. Die Prüfkennungen (`data-audit`) des Wechsels bleiben, wie sie
 * waren — die E2E-Mappe liest sie.
 */
const TEXTE = {
  wasserwechsel: {
    audit: 'wasserwechsel',
    schritt2: '2 · Ansetzen',
    wannHinweis: 'Beginn des Wechsels — für einen Nachtrag zurückstellen.',
    titel2: 'Neu ansetzen',
    literLabel: 'Neues Wasser',
    literAria: 'Neues Wasser in Litern',
    literFehlt: 'Wie viele Liter hast du neu angesetzt? Trag sie in Schritt 2 ein.',
    titel3: 'Nachher — fertig angesetzt',
    loesung: 'Mit deinen Mengen',
    fotoTitel: 'Nach dem Wasserwechsel',
    fotoFehlt: 'Der Wechsel ist gespeichert, das Foto nicht',
    speichern: 'Wasserwechsel speichern',
  },
  addback: {
    audit: 'addback',
    schritt2: '2 · Nachfüllen',
    wannHinweis: 'Wann du nachgefüllt hast — für einen Nachtrag zurückstellen.',
    titel2: 'Nachfüllen',
    literLabel: 'Nachgefüllt',
    literAria: 'Nachgefüllt in Litern',
    literFehlt: 'Wie viele Liter hast du nachgefüllt? Trag sie in Schritt 2 ein.',
    titel3: 'Nachher — nach dem Durchmischen',
    loesung: 'Deine Lösung',
    fotoTitel: 'Nach dem Nachfüllen',
    fotoFehlt: 'Das Nachfüllen ist gespeichert, das Foto nicht',
    speichern: 'Nachfüllen speichern',
  },
} as const

function Zurueck({ text, onClick }: { text: string; onClick: () => void }) {
  return <button type="button" className="wa-zurueck" onClick={onClick} title="Zurück auf den Vorschlag">↺ Vorschlag {text}</button>
}

function Meins({ text, onClick }: { text: string; onClick: () => void }) {
  return <button type="button" className="wa-zurueck is-meins" onClick={onClick} title="Deinen Wert wieder einsetzen">↶ deins {text}</button>
}

function uhrzeit(utc: string): string {
  return new Intl.DateTimeFormat('de-DE', { hour: '2-digit', minute: '2-digit' }).format(new Date(utc))
}

function fehlerText(caught: unknown, ersatz: string): string {
  return caught instanceof ApiRequestError ? caught.message : ersatz
}

/** Vorbelegte Messwerte als Feldtext (mit Komma). */
function alsFelder(werte: { ec: number | null; ph: number | null; wt: number | null } | undefined): Messfelder {
  if (!werte) return LEER
  return { ec: feldText(werte.ec), ph: feldText(werte.ph), wt: feldText(werte.wt), do: '', orp: '' }
}

/**
 * Ein Vorgang als Ablauf in vier Schritten (A-006, freigegeben von Bru am 05.10.2026).
 *
 * Vorher · Wasser und Zugaben · Nachher · Speichern. Ein Speichern legt alles
 * an — beim **Wasserwechsel** Wechsel, Messung vorher und nachher, Verbrauch
 * und Tagebuchzeile (`POST /api/grows/{id}/wasserwechsel`), beim
 * **Nachfüllen** dasselbe mit einem Addback-Eintrag statt des Wechsels
 * (`POST /api/grows/{id}/addback/vorgaenge`, Etappe 3).
 *
 * **Die Felder stehen auf dem Vorschlag** (kein „übernehmen"). Wer ändert,
 * sieht das Feld gelb und „↺ Vorschlag …"; der eigene Wert bleibt gemerkt und
 * kommt mit „↶ deins …" zurück. Gemerkt wird nur **innerhalb dieses Vorgangs**:
 * jeder Wechsel und jedes Nachfüllen rechnet neu, ein „wie letztes Mal" gibt
 * es nicht (Bru).
 *
 * Die Vorschläge rechnet das Backend (`GET …/mixing-plan/vorschlag` auf die
 * Literzahl, beim Nachfüllen zusätzlich der Addback-Rechner
 * `POST …/addback/calculate`) — hier wird nichts davon nachgerechnet.
 *
 * @param vorbelegung Werte aus einem Link (`/addback?zeitpunkt=…&ecVorher=…`),
 *   z. B. „Nachfüllen eintragen" an einer Auffälligkeit im Tagebuch.
 */
export function VorgangAblauf({ art, growId, stand = null, startSchritt = 1, vorbelegung = null, onGespeichert }: {
  art: AblaufArt
  growId: number
  /** Auf welchem Schritt der Ablauf beginnt (`?schritt=`). */
  startSchritt?: Schritt
  /** Nur beim Wechsel: der Stand der Wechsel-Erinnerung. */
  stand?: WasserwechselStandDto | null
  vorbelegung?: Vorbelegung | null
  /** Nach dem Speichern: was am Vorgang hängt („2 Messwerten, 3 Buchungen …") und ein Hinweis, falls das Foto scheiterte. */
  onGespeichert: (teile: string, hinweis: string | null, vorgangId: number) => void
}) {
  const t = TEXTE[art]
  const istWechsel = art === 'wasserwechsel'
  const [schritt, setSchritt] = useState<Schritt>(startSchritt)
  const [zeitpunkt, setZeitpunkt] = useState(() => vorbelegung?.zeitpunkt ?? toLocalInputValue())

  // Vorbelegte Werte „vorher" vom Sensor gelten nur für den vorbelegten
  // Zeitpunkt. Wer „Wann" verstellt, bekommt wieder die Sensorwerte von dort.
  const vorbelegtVorher = vorbelegung?.quelle === 'Sensor' && Object.values(vorbelegung.vorher).some((w) => w != null)
    && (vorbelegung.zeitpunkt == null || vorbelegung.zeitpunkt === zeitpunkt)
    ? vorbelegung.vorher : null
  const vorbelegtNachher = useMemo(() => alsFelder(vorbelegung?.nachher), [vorbelegung])

  // Schritt 1 — vorher
  /* Für WELCHEN Zeitpunkt die Sensorwerte gelten. Gefunden im E2E-Rundweg: wer
     „Wann" zurückstellt und sofort speichert, schickte noch die Werte von
     „jetzt" — die lagen nach dem Wechsel, und das Backend lehnte ab. Nur
     Werte zum eingestellten Zeitpunkt zählen. */
  const [sensorStand, setSensorStand] = useState<{ fuer: string; daten: WasserwechselSensorDto } | null>(null)
  const [sensorFehler, setSensorFehler] = useState<string | null>(null)
  const [vorherHand, setVorherHand] = useState<Messfelder>(() => vorbelegung?.quelle === 'Hand' ? alsFelder(vorbelegung.vorher) : LEER)

  // Schritt 2 — Wasser und Zugaben
  const [wechselArt, setWechselArt] = useState<ChangeoutKind>('Full')
  const [nachfuellArt, setNachfuellArt] = useState<AddbackLogKind>('Addback')
  const [liter, setLiter] = useState(vorbelegung?.liter ?? '')
  const [wasser, setWasser] = useState<WaterSource>(vorbelegung?.wasser ?? 'Tap')
  const [osmoseProzent, setOsmoseProzent] = useState('50')
  const [ecEigenJe, setEcEigenJe] = useState<Partial<Record<WaterSource, string>>>({})
  const [ecGemerktJe, setEcGemerktJe] = useState<Partial<Record<WaterSource, string>>>({})
  const [vorschlagRoh, setVorschlag] = useState<MischplanVorschlag | null>(null)
  const [vorschlagFehler, setVorschlagFehler] = useState<string | null>(null)
  const [eigen, setEigen] = useState<Werte>({})
  const [gemerkt, setGemerkt] = useState<Werte>({})
  const [extra, setExtra] = useState<Artikel[]>([])
  const [artikelWahl, setArtikelWahl] = useState<Record<string, number>>({})
  const [buchenAus, setBuchenAus] = useState<Set<string>>(() => new Set())
  const [wasserBuchen, setWasserBuchen] = useState(true)
  const [artikel, setArtikel] = useState<Artikel[]>([])
  const [produktWahl, setProduktWahl] = useState('')
  const [rechner, setRechner] = useState<{ fuer: string; daten: AddbackResultDto } | null>(null)

  // Schritt 3 — nachher
  const [nachher, setNachher] = useState<Messfelder>(vorbelegtNachher)
  const [sensorJetzt, setSensorJetzt] = useState<WasserwechselSensorDto | null>(null)
  const [notiz, setNotiz] = useState(vorbelegung?.notiz ?? '')
  const [fotos, setFotos] = useState<File[]>([])

  // Schritt 4 — speichern
  const [insTagebuch, setInsTagebuch] = useState(true)
  const [erinnerung, setErinnerung] = useState(true)
  const [speichert, setSpeichert] = useState(false)
  const [fehler, setFehler] = useState<string | null>(null)

  // Grundlage: Anlagevolumen (beim Wechsel vorbelegt als Liter), Wasserquelle des Grows, aktive Artikel.
  useEffect(() => {
    const controller = new AbortController()
    void (async () => {
      const [plan, grow, liste] = await Promise.all([
        apiFetch<{ volumenLiter: number | null }>(`/api/grows/${growId}/mixing-plan`, { signal: controller.signal }).catch(() => null),
        apiFetch<GrowDetail>(`/api/grows/${growId}`, { signal: controller.signal }).catch(() => null),
        apiFetch<Artikel[]>('/api/kosten/artikel', { signal: controller.signal }).catch(() => [] as Artikel[]),
      ])
      if (controller.signal.aborted) return
      // Nachgefüllt wird ein Teil, nicht die Anlage — dort gibt es keine Vorbelegung aus dem Volumen.
      if (istWechsel && plan?.volumenLiter != null) setLiter((alt) => alt === '' ? zahl(plan.volumenLiter!, 0) : alt)
      if (grow?.waterSource && !vorbelegung?.wasser) setWasser(grow.waterSource)
      setArtikel(liste.filter((a) => a.aktiv))
    })()
    return () => controller.abort()
  }, [growId, istWechsel, vorbelegung])

  // Sensorwerte kurz vor dem Zeitpunkt — derselbe Endpunkt für Wechsel und Nachfüllen.
  useEffect(() => {
    const controller = new AbortController()
    const zeit = new Date(zeitpunkt)
    if (Number.isNaN(zeit.getTime())) return
    const warte = window.setTimeout(() => {
      apiFetch<WasserwechselSensorDto>(`/api/grows/${growId}/wasserwechsel/sensor?zeitpunkt=${encodeURIComponent(zeit.toISOString())}`, { signal: controller.signal })
        .then((daten) => { setSensorStand({ fuer: zeitpunkt, daten }); setSensorFehler(null) })
        .catch((caught) => { if (!controller.signal.aborted) setSensorFehler(fehlerText(caught, 'Sensorwerte konnten nicht geladen werden.')) })
    }, 250)
    return () => { window.clearTimeout(warte); controller.abort() }
  }, [growId, zeitpunkt])

  // Zum Vergleich in Schritt 3: was der Sensor jetzt zeigt.
  useEffect(() => {
    if (schritt !== 3) return
    const controller = new AbortController()
    apiFetch<WasserwechselSensorDto>(`/api/grows/${growId}/wasserwechsel/sensor`, { signal: controller.signal })
      .then(setSensorJetzt)
      .catch(() => { if (!controller.signal.aborted) setSensorJetzt(null) })
    return () => controller.abort()
  }, [growId, schritt])

  const sensorGeladen = sensorStand?.fuer === zeitpunkt ? sensorStand.daten : null
  // Vorbelegt vom Sensor (aus dem Tagebuch): diese Werte gelten, die Suche ergänzt nur, was fehlt.
  const sensor: WasserwechselSensorDto | null = vorbelegtVorher
    ? {
      zeitpunktUtc: new Date(zeitpunkt).toISOString(),
      fensterMinuten: sensorGeladen?.fensterMinuten ?? 0,
      ec: vorbelegtVorher.ec != null ? { wert: vorbelegtVorher.ec, zeitUtc: '' } : sensorGeladen?.ec ?? null,
      ph: vorbelegtVorher.ph != null ? { wert: vorbelegtVorher.ph, zeitUtc: '' } : sensorGeladen?.ph ?? null,
      wasserTemp: vorbelegtVorher.wt != null ? { wert: vorbelegtVorher.wt, zeitUtc: '' } : sensorGeladen?.wasserTemp ?? null,
      hinweis: null,
    }
    : sensorGeladen
  const sensorLaedt = !vorbelegtVorher && sensorGeladen == null && sensorFehler == null && !Number.isNaN(new Date(zeitpunkt).getTime())
  const literZahl = zahlOderNull(liter)
  const ecEigen = ecEigenJe[wasser] ?? null
  const ecGemerkt = ecGemerktJe[wasser] ?? null
  const ecEigenZahl = ecEigen != null ? zahlOderNull(ecEigen) : null
  const osmoseZahl = zahlOderNull(osmoseProzent)
  const nurWasser = !istWechsel && nachfuellArt === 'TopOff'

  // Der Vorschlag — neu bei Liter, Wasserart, Anteil und eigenem Wasser-EC.
  useEffect(() => {
    if (literZahl == null || literZahl <= 0) return
    if (wasser === 'Mixed' && (osmoseZahl == null || osmoseZahl < 0 || osmoseZahl > 100)) return
    const controller = new AbortController()
    const suche = new URLSearchParams({ liter: String(literZahl), wasser })
    if (wasser === 'Mixed' && osmoseZahl != null) suche.set('osmoseProzent', String(osmoseZahl))
    if (ecEigenZahl != null) suche.set('wasserEc', String(ecEigenZahl))
    const warte = window.setTimeout(() => {
      apiFetch<MischplanVorschlag>(`/api/grows/${growId}/mixing-plan/vorschlag?${suche.toString()}`, { signal: controller.signal })
        .then((daten) => { setVorschlag(daten); setVorschlagFehler(null) })
        .catch((caught) => { if (!controller.signal.aborted) setVorschlagFehler(fehlerText(caught, 'Der Vorschlag konnte nicht gerechnet werden.')) })
    }, 250)
    return () => { window.clearTimeout(warte); controller.abort() }
  }, [growId, literZahl, wasser, osmoseZahl, ecEigenZahl])

  // Ohne Literzahl gibt es keinen Vorschlag — auch keinen alten von vorhin.
  const vorschlag = literZahl != null && literZahl > 0 ? vorschlagRoh : null

  const zeilen: AblaufZeile[] = useMemo(() => [
    // „Nur Wasser": keine Plan-Zeilen. Über „+ Produkt" lässt sich trotzdem etwas buchen.
    ...(nurWasser ? [] : vorschlag?.zeilen ?? []).map((z): AblaufZeile => {
      const schluessel = `plan:${z.komponente}`
      const gewaehlt = artikelWahl[schluessel]
      const gewaehlterArtikel = gewaehlt != null ? artikel.find((a) => a.id === gewaehlt) : null
      return {
        schluessel,
        name: z.komponente,
        art: 'plan',
        rolle: z.rolle,
        vorschlagMl: z.vorschlagMl,
        hinweis: z.hinweis,
        artikelId: gewaehlterArtikel?.id ?? z.artikelId,
        einheit: gewaehlterArtikel?.einheit ?? z.artikelEinheit ?? 'ml',
      }
    }),
    ...extra.map((a): AblaufZeile => ({
      schluessel: `extra:${a.id}`, name: a.name, art: 'extra', rolle: null, vorschlagMl: null, hinweis: null, artikelId: a.id, einheit: a.einheit,
    })),
  ], [vorschlag, extra, artikel, artikelWahl, nurWasser])

  const setze = (schluessel: string, wert: string | null) => {
    setEigen((alt) => {
      const neu = { ...alt }
      if (wert == null) delete neu[schluessel]
      else neu[schluessel] = wert
      return neu
    })
    if (wert != null) setGemerkt((alt) => ({ ...alt, [schluessel]: wert }))
  }
  const setzeEc = (wert: string | null) => {
    setEcEigenJe((alt) => ({ ...alt, [wasser]: wert ?? undefined }))
    if (wert != null) setEcGemerktJe((alt) => ({ ...alt, [wasser]: wert }))
  }

  const ecVorschlag = vorschlag?.wasserEcVorschlag ?? null
  const ecMeinsVerfuegbar = ecEigen == null && ecGemerkt != null && zahlOderNull(ecGemerkt) !== ecVorschlag
  const geaendert = zeilen.filter((z) => istGeaendert(z, eigen)).length + (ecEigen != null ? 1 : 0)
  const zurueckholbar = zeilen.filter((z) => meinsVerfuegbar(z, eigen, gemerkt)).length + (ecMeinsVerfuegbar ? 1 : 0)
  const anteil = anteilPlanDosis(zeilen, eigen)
  const ecErwartet = ecMitDeinenMengen(vorschlag?.wasserEc ?? null, vorschlag?.ecZielDuenger ?? null, anteil)
  const wasserListe = literZahl != null && literZahl > 0 ? wasserZeilen(literZahl, wasser, vorschlag?.osmoseAnteil ?? (wasser === 'RO' ? 1 : wasser === 'Mixed' ? (osmoseZahl ?? 0) / 100 : 0)) : []
  const osmoseArtikelFehlt = !artikel.some((a) => a.name.trim().toLowerCase() === 'osmosewasser')
  const leitungArtikelFehlt = !artikel.some((a) => a.name.trim().toLowerCase() === 'leitungswasser')

  const wirdGebucht = (z: AblaufZeile) => z.artikelId != null && mengeDerZeile(z, eigen) > 0 && !buchenAus.has(z.schluessel)
  const gebucht = zeilen.filter(wirdGebucht)
  const wasserGebucht = wasserBuchen ? wasserListe.filter((w) => w.menge > 0) : []

  // ---- Werte vorher/nachher, wie sie gespeichert werden
  const hand = (text: string) => zahlOderNull(text)
  const vorherWert = (handText: string, sensorWert: number | null | undefined) => hand(handText) ?? sensorWert ?? null
  const vorher = {
    ec: vorherWert(vorherHand.ec, sensor?.ec?.wert),
    ph: vorherWert(vorherHand.ph, sensor?.ph?.wert),
    wt: vorherWert(vorherHand.wt, sensor?.wasserTemp?.wert),
    do: hand(vorherHand.do),
    orp: hand(vorherHand.orp),
  }
  const sensorGenutzt = (hand(vorherHand.ec) == null && sensor?.ec != null)
    || (hand(vorherHand.ph) == null && sensor?.ph != null)
    || (hand(vorherHand.wt) == null && sensor?.wasserTemp != null)
  const handGenutzt = Object.values(vorherHand).some((text) => hand(text) != null)
  const vorherHerkunft: 'Sensor' | 'Hand' | 'gemischt' = sensorGenutzt ? (handGenutzt ? 'gemischt' : 'Sensor') : 'Hand'
  // Vorbelegte Werte tragen keine eigene Sensorzeit — die Messung steht dann eine Minute vor dem Vorgang.
  const sensorZeit = [sensor?.ec, sensor?.ph, sensor?.wasserTemp].filter((w) => w != null && w.zeitUtc !== '').map((w) => w!.zeitUtc).sort().at(-1) ?? null
  const nach = { ec: hand(nachher.ec), ph: hand(nachher.ph), wt: hand(nachher.wt), do: hand(nachher.do), orp: hand(nachher.orp) }
  const vorherHatWerte = Object.values(vorher).some((w) => w != null)
  const nachherHatWerte = Object.values(nach).some((w) => w != null)
  // „nachher" vom Sensor, wenn der Link die Werte so mitgab und niemand sie geändert hat.
  const nachherVomSensor = vorbelegung?.quelle === 'Sensor' && Object.values(vorbelegung.nachher).some((w) => w != null)
    && (['ec', 'ph', 'wt'] as const).every((f) => nachher[f] === vorbelegtNachher[f])
  const nachherHerkunft: 'Sensor' | 'Hand' | 'gemischt' = nachherVomSensor ? (nach.do != null || nach.orp != null ? 'gemischt' : 'Sensor') : 'Hand'

  // ---- Nachfüllen: was im Tank war, und was danach drin ist
  const anlageLiter = vorschlag?.anlageLiter ?? null
  const restLiter = anlageLiter != null && literZahl != null && literZahl <= anlageLiter ? anlageLiter - literZahl : null
  const ecLoesung = nurWasser ? vorschlag?.wasserEc ?? null : ecErwartet
  const ecDanach = istWechsel ? null : ecTankDanach(restLiter, vorher.ec, literZahl, ecLoesung)
  const rechnerSchluessel = !istWechsel && !nurWasser && restLiter != null && restLiter > 0 && vorher.ec != null && vorschlag?.ecZielGesamt != null && ecLoesung != null
    ? JSON.stringify([restLiter, vorher.ec, vorschlag.ecZielGesamt, Number(ecLoesung.toFixed(2))])
    : null

  // Der Addback-Rechner: wie viel DIESER Lösung bräuchte der Tank bis zum Ziel?
  useEffect(() => {
    if (rechnerSchluessel == null) return
    const [reservoirLiters, ecIst, ecZiel, ecStock] = JSON.parse(rechnerSchluessel) as number[]
    const controller = new AbortController()
    const warte = window.setTimeout(() => {
      apiFetch<AddbackResultDto>(`/api/grows/${growId}/addback/calculate`, {
        method: 'POST', signal: controller.signal, body: JSON.stringify({ reservoirLiters, ecIst, ecZiel, ecStock }),
      })
        .then((daten) => setRechner({ fuer: rechnerSchluessel, daten }))
        .catch(() => { if (!controller.signal.aborted) setRechner(null) })
    }, 300)
    return () => { window.clearTimeout(warte); controller.abort() }
  }, [growId, rechnerSchluessel])
  const rechnerErgebnis = rechner != null && rechner.fuer === rechnerSchluessel ? rechner.daten : null

  const zugaben = [
    ...wasserGebucht.map((w) => ({ name: w.name, menge: w.menge, einheit: 'L' })),
    ...gebucht.map((z) => ({ name: z.name, menge: mengeDerZeile(z, eigen), einheit: z.einheit })),
  ]
  const tagebuch = tagebuchZeile({
    titel: istWechsel ? 'Wasserwechsel' : NACHFUELL_ART[nachfuellArt],
    liter: literZahl ?? 0,
    wasserName: wasser === 'Tap' ? 'Leitungswasser' : wasser === 'RO' ? 'Osmosewasser' : 'Mischung',
    vorher,
    nachher: nach,
    zugaben,
    notiz,
  })

  const naechsterWechsel = useMemo(() => {
    if (!stand) return null
    const basis = new Date(zeitpunkt)
    if (Number.isNaN(basis.getTime())) return null
    basis.setDate(basis.getDate() + stand.intervallTage)
    return new Intl.DateTimeFormat('de-DE', { day: '2-digit', month: '2-digit' }).format(basis)
  }, [stand, zeitpunkt])

  const weiter = (ziel: Schritt) => (
    <div className="wa-weiter">
      <V1Button variant="primary" onClick={() => setSchritt(ziel)} audit={`${t.audit}-weiter-${ziel}`}>Weiter</V1Button>
    </div>
  )

  async function speichern(event: FormEvent) {
    event.preventDefault()
    // Enter in einem Feld schickt das Formular ab — vor Schritt 4 heißt das „weiter".
    if (schritt < 4) { setSchritt((schritt + 1) as Schritt); return }
    if (sensorLaedt) return

    const unlesbar = unlesbarMeldung(unlesbareFelder([
      [vorherHand.ec, 'EC vorher'], [vorherHand.ph, 'pH vorher'], [vorherHand.wt, 'Wasser vorher'],
      [vorherHand.do, 'DO vorher'], [vorherHand.orp, 'ORP vorher'],
      [liter, `${t.literLabel} (Liter)`], [osmoseProzent, 'Anteil Osmose'], [ecEigen ?? '', 'EC des Wassers'],
      [nachher.ec, 'EC nachher'], [nachher.ph, 'pH nachher'], [nachher.wt, 'Wasser nachher'],
      [nachher.do, 'DO nachher'], [nachher.orp, 'ORP nachher'],
      ...zeilen.map((z): [string, string] => [wertDerZeile(z, eigen), `Menge ${z.name}`]),
    ]))
    if (unlesbar) { setFehler(unlesbar); return }
    if (literZahl == null || literZahl <= 0) { setFehler(t.literFehlt); setSchritt(2); return }
    // Ein Foto hängt an der Messung „nachher" — ohne sie ginge es still verloren (Befund des Prüfers).
    if (fotos.length > 0 && !nachherHatWerte) {
      setFehler('Das Foto braucht die Messung „nachher" — trag dort mindestens einen Wert ein oder nimm das Foto heraus.')
      setSchritt(3)
      return
    }

    const messung = (werte: typeof vorher, extraFelder: Partial<VorgangMessungRequest>): VorgangMessungRequest | null =>
      Object.values(werte).some((w) => w != null)
        ? { ...extraFelder, reservoirEc: werte.ec, reservoirPh: werte.ph, reservoirWaterTempC: werte.wt, dissolvedOxygenMgL: werte.do, orpMv: werte.orp }
        : null

    const buchungen: VorgangBuchungRequest[] = [
      ...wasserGebucht.map((w) => ({ wasser: w.wasser, menge: w.menge })),
      ...gebucht.map((z) => ({ artikelId: z.artikelId, menge: mengeDerZeile(z, eigen) })),
    ]

    const gemeinsam = {
      zeitpunktLokal: zeitpunkt || null,
      liter: literZahl,
      wasser,
      osmoseProzent: wasser === 'Mixed' ? osmoseZahl : null,
      wasserEcMsCm: ecEigenZahl ?? vorschlag?.wasserEcVorschlag ?? null,
      vorher: messung(vorher, {
        herkunft: vorherHerkunft,
        sensorZeitUtc: sensorGenutzt ? sensorZeit : null,
        // Nur Sensorwerte mit eigener Zeit: die Messung steht zur Zeit des Sensorwerts. Sonst eine Minute vor dem Vorgang.
        zeitpunktLokal: vorherHerkunft === 'Sensor' && sensorZeit ? toLocalInputValue(new Date(sensorZeit)) : null,
      }),
      nachher: messung(nach, { herkunft: nachherHerkunft }),
      buchungen,
      notiz: notiz.trim() || null,
      tagebuch: insTagebuch ? tagebuch : null,
    }

    setSpeichert(true)
    setFehler(null)
    try {
      let vorgang: WasserwechselVorgangDto | AddbackVorgangDto
      if (istWechsel) {
        const body: WasserwechselVorgangRequest = { ...gemeinsam, art: wechselArt, erinnerungNeuStarten: erinnerung }
        vorgang = await apiFetch<WasserwechselVorgangDto>(`/api/grows/${growId}/wasserwechsel`, { method: 'POST', body: JSON.stringify(body) })
      } else {
        const body: AddbackVorgangRequest = { ...gemeinsam, art: nachfuellArt, ecZiel: nurWasser ? null : vorschlag?.ecZielGesamt ?? null }
        vorgang = await apiFetch<AddbackVorgangDto>(`/api/grows/${growId}/addback/vorgaenge`, { method: 'POST', body: JSON.stringify(body) })
      }
      let hinweis: string | null = null
      if (fotos.length > 0 && vorgang.nachher) {
        try {
          const form = new FormData()
          form.append('photoCaption', t.fotoTitel)
          form.append('photoTag', 'Overview')
          form.append('useAsReferenceShot', 'false')
          form.append('source', 'Manual')
          for (const datei of fotos) form.append('photos', datei)
          await apiFetch(`/api/measurements/${vorgang.nachher.id}/photos`, { method: 'POST', body: form })
        } catch (caught) {
          hinweis = `${t.fotoFehlt}: ${fehlerText(caught, 'Hochladen fehlgeschlagen.')}`
        }
      }
      onGespeichert(teileText(vorgang), hinweis, vorgang.id)
    } catch (caught) {
      setFehler(fehlerText(caught, 'Speichern fehlgeschlagen.'))
    } finally {
      setSpeichert(false)
    }
  }

  const messFeld = (werte: Messfelder, setzen: (neu: Messfelder) => void, feld: keyof Messfelder, label: string, hint: string, platzhalter: string) => (
    <V1Field label={label} hint={hint}>
      <input value={werte[feld]} onChange={(e) => setzen({ ...werte, [feld]: e.target.value })} inputMode="decimal" placeholder={platzhalter}
        className={istUnlesbar(werte[feld]) ? 'is-unlesbar' : undefined} />
    </V1Field>
  )

  const vergleich = (name: string, einheit: string, vor: number | null, nachWert: number | null, stellen: number) => (
    <div className="wa-tr" role="row" key={name}>
      <span role="cell" className="wa-vt-name">{name}{einheit && <small>{einheit}</small>}</span>
      <span role="cell">{vor == null ? '—' : zahl(vor, stellen)}</span>
      <span role="cell"><b>{nachWert == null ? '—' : zahl(nachWert, stellen)}</b></span>
      <span role="cell" className="wa-diff">{aenderung(vor, nachWert, stellen) ?? (vor == null && nachWert == null ? 'nicht gemessen' : '')}</span>
    </div>
  )

  const sensorHerkunftText = vorbelegtVorher
    ? 'Vom Sensor, aus dem Link'
    : `Vom Sensor, ${sensorZeit ? `${uhrzeit(sensorZeit)} Uhr` : 'kurz davor'}`

  return (
    <form onSubmit={(e) => void speichern(e)} className="wa-ablauf" data-audit={`${t.audit}-ablauf`} noValidate>
      <V1Tabs label="Schritt" active={schritt} onChange={setSchritt}
        items={[
          { value: 1, label: '1 · Vorher', audit: `${t.audit}-schritt-1` },
          { value: 2, label: t.schritt2, audit: `${t.audit}-schritt-2` },
          { value: 3, label: '3 · Nachher', audit: `${t.audit}-schritt-3` },
          { value: 4, label: '4 · Speichern', audit: `${t.audit}-schritt-4` },
        ]} />

      {fehler && <V1Alert message={fehler} tone="warn" />}

      {schritt === 1 && (
        <V1Section title="Vorher — was ist im Tank?">
          {vorbelegung && (
            <V1Alert tone="neutral" message="Vorbelegt aus dem Link — etwa vom Tagebuch („Nachfüllen eintragen“). Prüf Zeitpunkt und Werte und ergänze, was fehlt." />
          )}
          <div className="v1-form-grid wa-felder">
            <V1Field label="Wann" hint={t.wannHinweis}>
              <input type="datetime-local" value={zeitpunkt} max={toLocalInputValue()} onChange={(e) => setZeitpunkt(e.target.value)} />
            </V1Field>
          </div>
          <div className="wa-sensorbox" data-audit={`${t.audit}-sensor`}>
            {sensor && (sensor.ec || sensor.ph || sensor.wasserTemp) ? (
              <>
                <span className="wa-herkunft">{sensorHerkunftText}</span>
                <div className="wa-gross">
                  <div><small>EC</small><b>{sensor.ec ? zahl(sensor.ec.wert, 2) : '—'}</b></div>
                  <div><small>pH</small><b>{sensor.ph ? zahl(sensor.ph.wert, 2) : '—'}</b></div>
                  <div><small>Wasser</small><b>{sensor.wasserTemp ? `${zahl(sensor.wasserTemp.wert, 1)} °C` : '—'}</b></div>
                </div>
                <p className="wa-hinweis">Steht unten nichts drin, gelten diese Werte. DO und ORP haben keinen Sensor — die misst du von Hand, wenn du magst.</p>
              </>
            ) : (
              <p className="wa-hinweis">{sensorFehler ?? sensor?.hinweis ?? 'Sensorwerte werden gesucht …'}</p>
            )}
          </div>
          <div className="v1-form-grid wa-felder">
            {messFeld(vorherHand, setVorherHand, 'do', 'DO', 'mg/L · von Hand', 'z. B. 7,8')}
            {messFeld(vorherHand, setVorherHand, 'orp', 'ORP', 'mV · von Hand', '—')}
            {messFeld(vorherHand, setVorherHand, 'ec', 'EC selbst gemessen', 'optional, ersetzt den Sensor', sensor?.ec ? zahl(sensor.ec.wert, 2) : '—')}
            {messFeld(vorherHand, setVorherHand, 'ph', 'pH selbst gemessen', 'optional, ersetzt den Sensor', sensor?.ph ? zahl(sensor.ph.wert, 2) : '—')}
            {messFeld(vorherHand, setVorherHand, 'wt', 'Wasser °C selbst gemessen', 'optional, ersetzt den Sensor', sensor?.wasserTemp ? zahl(sensor.wasserTemp.wert, 1) : '—')}
          </div>
          {weiter(2)}
        </V1Section>
      )}

      {schritt === 2 && (
        <V1Section title={t.titel2}>
          <div className="v1-form-grid wa-felder">
            <V1Field label={t.literLabel} hint={vorschlag?.anlageLiter != null ? `Liter · Anlage fasst ${zahl(vorschlag.anlageLiter, 0)} L` : 'Liter'}>
              <input value={liter} onChange={(e) => setLiter(e.target.value)} inputMode="decimal" aria-label={t.literAria}
                className={istUnlesbar(liter) ? 'is-unlesbar' : undefined} />
            </V1Field>
            {istWechsel ? (
              <V1Field label="Art">
                <select value={wechselArt} onChange={(e) => setWechselArt(e.target.value as ChangeoutKind)}>
                  <option value="Full">Komplettwechsel</option>
                  <option value="Partial">Teilwechsel</option>
                </select>
              </V1Field>
            ) : (
              <V1Field label="Art">
                <select value={nachfuellArt} onChange={(e) => setNachfuellArt(e.target.value as AddbackLogKind)} aria-label="Art des Nachfüllens">
                  <option value="Addback">Mit Dünger nach Plan</option>
                  <option value="TopOff">Nur Wasser</option>
                </select>
              </V1Field>
            )}
            <V1Field label="Wasser">
              <select value={wasser} onChange={(e) => setWasser(e.target.value as WaterSource)}>
                <option value="Tap">Leitungswasser</option>
                <option value="RO">Osmose</option>
                <option value="Mixed">Mischung</option>
              </select>
            </V1Field>
            {wasser === 'Mixed' && (
              <V1Field label="Anteil Osmose" hint="Prozent">
                <input value={osmoseProzent} onChange={(e) => setOsmoseProzent(e.target.value)} inputMode="numeric" />
              </V1Field>
            )}
            <div className="v1-field">
              <span>EC des Wassers</span>
              <input value={ecEigen ?? (ecVorschlag != null ? zahl(ecVorschlag, 2) : '')} onChange={(e) => setzeEc(e.target.value)} inputMode="decimal"
                aria-label="EC des Wassers" placeholder="mS/cm" className={ecEigen != null ? 'is-geaendert' : undefined} />
              <small className="wa-feldhinweis">
                {ecEigen != null
                  ? <>von dir gemessen · <Zurueck text={ecVorschlag != null ? `${zahl(ecVorschlag, 2)} (${vorschlag?.wasserEcQuelle ?? ''})` : '—'} onClick={() => setzeEc(null)} /></>
                  : <>Vorschlag: {vorschlag?.wasserEcQuelle ?? '—'}{ecMeinsVerfuegbar && <> · <Meins text={ecGemerkt ?? ''} onClick={() => setzeEc(ecGemerkt)} /></>}</>}
              </small>
            </div>
          </div>

          {vorschlagFehler && <V1Alert message={vorschlagFehler} tone="warn" />}
          {vorschlag?.luecke && !nurWasser && <V1Alert message={vorschlag.luecke} tone="neutral" />}
          {nurWasser && <V1Alert tone="neutral" message="Nur Wasser — ohne Zugaben nach Plan. Über „+ Produkt“ buchst du trotzdem, was du hineingegeben hast." />}

          {vorschlag && !vorschlag.luecke && !nurWasser && (
            <div className="wa-ziel" data-audit={`${t.audit}-ziel`}>
              <div><small>Plan</small><b>{vorschlag.programmName}{vorschlag.spalteLabel ? ` · ${vorschlag.spalteLabel}` : ''}</b></div>
              <div>
                <small>EC-Ziel gesamt</small>
                <b>{vorschlag.ecZielGesamt != null ? zahl(vorschlag.ecZielGesamt, 2) : '—'}</b>
                <em>{vorschlag.ecZielDuenger != null && vorschlag.wasserEc != null
                  ? `= ${zahl(vorschlag.ecZielDuenger, 2)} Dünger + ${zahl(vorschlag.wasserEc, 2)} Wasser`
                  : 'ohne Wasser-EC kein Gesamtziel'}</em>
              </div>
              <div>
                <small>{t.loesung}</small>
                <b className={classNames(ecErwartet != null && vorschlag.ecZielGesamt != null && ecErwartet < vorschlag.ecZielGesamt - 0.15 && 'is-warn')}>
                  {ecErwartet != null ? `≈ ${zahl(ecErwartet, 2)}` : '—'}
                </b>
                <em>{anteil != null ? `${zahl(anteil * 100, 0)} % der Plan-Dosis · Faustregel: Dünger-EC wächst mit der Dosis` : 'Plan nennt keine Grunddünger'}</em>
              </div>
            </div>
          )}
          {!istWechsel && (
            <div className="wa-ziel" data-audit="addback-tank">
              <div>
                <small>Tank danach</small>
                <b>{ecDanach != null ? `≈ ${zahl(ecDanach, 2)}` : '—'}</b>
                <em>{ecDanach != null && restLiter != null && vorher.ec != null && literZahl != null && ecLoesung != null
                  ? `Mischrechnung: ${zahl(restLiter, 0)} L mit EC ${zahl(vorher.ec, 2)} + ${zahl(literZahl, 0)} L mit EC ${zahl(ecLoesung, 2)}`
                  : anlageLiter == null ? 'Ohne Anlagevolumen keine Mischrechnung'
                    : vorher.ec == null ? 'Ohne EC vorher keine Mischrechnung'
                      : literZahl != null && literZahl > anlageLiter ? `Mehr als die Anlage fasst (${zahl(anlageLiter, 0)} L)`
                        : 'Trag die nachgefüllten Liter ein'}</em>
              </div>
              {rechnerErgebnis && vorschlag?.ecZielGesamt != null && vorher.ec != null && ecLoesung != null && restLiter != null && (
                <div data-audit="addback-rechner">
                  <small>Addback-Rechner</small>
                  {rechnerErgebnis.errorMessage
                    ? <em>Deine Lösung (EC ≈ {zahl(ecLoesung, 2)}) ist nicht stärker als das Ziel {zahl(vorschlag.ecZielGesamt, 2)} — Nachfüllen bringt den Tank nicht dorthin.</em>
                    : !rechnerErgebnis.needsAddback
                      ? <em>EC vorher ({zahl(vorher.ec, 2)}) liegt schon auf dem Ziel {zahl(vorschlag.ecZielGesamt, 2)} oder darüber — reines Wasser senkt ihn.</em>
                      : (
                        <>
                          <b>≈ {zahl(rechnerErgebnis.litersToAdd ?? 0, 1)} L</b>
                          <em>deiner Lösung brächten {zahl(restLiter, 0)} L von EC {zahl(vorher.ec, 2)} auf das Ziel {zahl(vorschlag.ecZielGesamt, 2)}.</em>
                        </>
                      )}
                </div>
              )}
            </div>
          )}
          {vorschlag?.calMagHinweis && !nurWasser && <V1Alert tone="neutral" message={vorschlag.calMagHinweis} />}

          <div className="wa-tabelle-kopf">
            <span>{geaendert > 0 ? `${geaendert} ${geaendert === 1 ? 'Wert' : 'Werte'} von dir geändert` : 'Alles wie vorgeschlagen'}</span>
            <span className="wa-kopf-knoepfe">
              {geaendert > 0 && <V1Button className="wa-klein" onClick={() => { setEigen((alt) => alleAufVorschlag(zeilen, alt)); setEcEigenJe((alt) => ({ ...alt, [wasser]: undefined })) }}>↺ Alles auf Vorschlag</V1Button>}
              {zurueckholbar > 0 && <V1Button className="wa-klein" onClick={() => {
                setEigen((alt) => meineWiederEinsetzen(zeilen, alt, gemerkt))
                if (ecMeinsVerfuegbar) setEcEigenJe((alt) => ({ ...alt, [wasser]: ecGemerkt ?? undefined }))
              }}>↶ Meine Werte wieder einsetzen ({zurueckholbar})</V1Button>}
            </span>
          </div>

          <div className="wa-tabelle" role="table" aria-label="Mischplan" data-audit={`${t.audit}-tabelle`}>
            <div className="wa-tr wa-th" role="row">
              <span role="columnheader">Produkt</span>
              <span role="columnheader">{literZahl != null ? `Vorschlag für ${zahl(literZahl, 0)} L` : 'Vorschlag'}</span>
              <span role="columnheader">Eingesetzt</span>
              <span role="columnheader">Buchen</span>
            </div>
            {zeilen.map((z) => {
              const menge = mengeDerZeile(z, eigen)
              const geaendertHier = istGeaendert(z, eigen)
              return (
                <div key={z.schluessel} className={classNames('wa-tr', z.art === 'extra' && 'is-zusatz', menge === 0 && 'is-null')} role="row">
                  <span role="cell">
                    <b>{z.name}</b>
                    {z.art === 'extra' && <small>nicht im Plan</small>}
                    {z.hinweis && <small>{z.hinweis}</small>}
                    {z.art === 'plan' && z.artikelId == null && (
                      <select className="wa-artikel" value="" aria-label={`Artikel für ${z.name}`}
                        onChange={(e) => e.target.value && setArtikelWahl((alt) => ({ ...alt, [z.schluessel]: Number(e.target.value) }))}>
                        <option value="">kein Artikel — wählen …</option>
                        {artikel.map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}
                      </select>
                    )}
                  </span>
                  <span role="cell" className="wa-vorschlag">{z.vorschlagMl != null ? `${zahl(z.vorschlagMl, 0)} ml` : '—'}</span>
                  <span role="cell" className="wa-eingabe-zelle">
                    <span className="wa-eingabe">
                      <input value={wertDerZeile(z, eigen)} onChange={(e) => setze(z.schluessel, e.target.value)} inputMode="decimal"
                        aria-label={`${z.name} eingesetzt`} className={classNames(geaendertHier && 'is-geaendert', istUnlesbar(wertDerZeile(z, eigen)) && 'is-unlesbar')} /> {z.einheit}
                    </span>
                    {geaendertHier && z.vorschlagMl != null && <Zurueck text={`${zahl(z.vorschlagMl, 0)} ml`} onClick={() => setze(z.schluessel, null)} />}
                    {meinsVerfuegbar(z, eigen, gemerkt) && <Meins text={`${gemerkt[z.schluessel]} ${z.einheit}`} onClick={() => setze(z.schluessel, gemerkt[z.schluessel])} />}
                    {z.art === 'extra' && (
                      <button type="button" className="wa-zurueck" onClick={() => { setExtra((alt) => alt.filter((a) => `extra:${a.id}` !== z.schluessel)); setze(z.schluessel, null) }}>✕ entfernen</button>
                    )}
                  </span>
                  <span role="cell">
                    <input type="checkbox" checked={wirdGebucht(z)} disabled={z.artikelId == null || menge === 0}
                      onChange={(e) => setBuchenAus((alt) => { const neu = new Set(alt); if (e.target.checked) neu.delete(z.schluessel); else neu.add(z.schluessel); return neu })}
                      aria-label={`${z.name} buchen`} />
                  </span>
                </div>
              )
            })}
            {wasserListe.map((w) => {
              const fehlt = w.wasser === 'RO' ? osmoseArtikelFehlt : leitungArtikelFehlt
              return (
                <div key={w.name} className="wa-tr is-zusatz" role="row">
                  <span role="cell"><b>{w.name}</b><small>{fehlt ? 'Artikel fehlt noch — wird beim Speichern angelegt' : 'Kosten je Liter im Artikel'}</small></span>
                  <span role="cell" className="wa-vorschlag">—</span>
                  <span role="cell" className="wa-eingabe"><input value={zahl(w.menge, Number.isInteger(w.menge) ? 0 : 1)} readOnly aria-label={`${w.name} eingesetzt`} /> L</span>
                  <span role="cell"><input type="checkbox" checked={wasserBuchen} onChange={(e) => setWasserBuchen(e.target.checked)} aria-label={`${w.name} buchen`} /></span>
                </div>
              )
            })}
          </div>

          <div className="wa-knoepfe">
            <select value={produktWahl} onChange={(e) => setProduktWahl(e.target.value)} aria-label="Produkt hinzufügen" className="wa-produkt-wahl">
              <option value="">Produkt wählen …</option>
              {artikel.filter((a) => !zeilen.some((z) => z.artikelId === a.id) && !['leitungswasser', 'osmosewasser'].includes(a.name.trim().toLowerCase()))
                .map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}
            </select>
            <V1Button variant="ghost" disabled={produktWahl === ''} audit={`${t.audit}-produkt`} onClick={() => {
              const gewaehlt = artikel.find((a) => String(a.id) === produktWahl)
              if (gewaehlt) setExtra((alt) => [...alt, gewaehlt])
              setProduktWahl('')
            }}>+ Produkt</V1Button>
          </div>
          {weiter(3)}
        </V1Section>
      )}

      {schritt === 3 && (
        <V1Section title={t.titel3}>
          {nachherVomSensor && <p className="wa-hinweis">EC, pH und Wasser sind Sensorwerte aus dem Link — überschreib sie, wenn du selbst gemessen hast.</p>}
          <div className="v1-form-grid wa-felder">
            {messFeld(nachher, setNachher, 'ec', 'EC', 'mS/cm', 'z. B. 1,15')}
            {messFeld(nachher, setNachher, 'ph', 'pH', '', 'z. B. 6,1')}
            {messFeld(nachher, setNachher, 'wt', 'Wassertemperatur', '°C', 'z. B. 18,5')}
            {messFeld(nachher, setNachher, 'do', 'DO', 'mg/L · von Hand', 'z. B. 8,4')}
            {messFeld(nachher, setNachher, 'orp', 'ORP', 'mV · von Hand', 'z. B. 450')}
          </div>
          {sensorJetzt && (sensorJetzt.ec || sensorJetzt.ph) && (
            <p className="wa-abgleich">
              Zum Vergleich — Sensor {uhrzeit((sensorJetzt.ec ?? sensorJetzt.ph)!.zeitUtc)} Uhr:
              {sensorJetzt.ec && ` EC ${zahl(sensorJetzt.ec.wert, 2)}`}{sensorJetzt.ec && sensorJetzt.ph && ' ·'}{sensorJetzt.ph && ` pH ${zahl(sensorJetzt.ph.wert, 2)}`}
            </p>
          )}

          <div className="wa-vergleich" role="table" aria-label="Vorher-Nachher" data-audit={`${t.audit}-vergleich`}>
            <div className="wa-tr wa-th" role="row">
              <span role="columnheader" />
              <span role="columnheader">Vorher</span>
              <span role="columnheader">Nachher</span>
              <span role="columnheader">Änderung</span>
            </div>
            {vergleich('EC', 'mS/cm', vorher.ec, nach.ec, 2)}
            {vergleich('pH', '', vorher.ph, nach.ph, 2)}
            {vergleich('Wasser', '°C', vorher.wt, nach.wt, 1)}
            {vergleich('DO', 'mg/L', vorher.do, nach.do, 1)}
            {vergleich('ORP', 'mV', vorher.orp, nach.orp, 0)}
            {vorschlag?.ecZielGesamt != null && !nurWasser && (
              <div className="wa-tr" role="row">
                <span role="cell" className="wa-vt-name">EC-Ziel</span>
                <span role="cell" />
                <span role="cell">{zahl(vorschlag.ecZielGesamt, 2)}</span>
                <span role="cell" className="wa-diff">
                  {nach.ec != null ? (nach.ec < vorschlag.ecZielGesamt
                    ? `${zahl(vorschlag.ecZielGesamt - nach.ec, 2)} darunter`
                    : nach.ec > vorschlag.ecZielGesamt ? `${zahl(nach.ec - vorschlag.ecZielGesamt, 2)} darüber` : 'genau') : ''}
                </span>
              </div>
            )}
          </div>
          <V1Field label="Notiz" wide hint="Landet mit im Tagebuch">
            <textarea rows={3} value={notiz} onChange={(e) => setNotiz(e.target.value)} placeholder={istWechsel ? 'Warum so angesetzt, was aufgefallen ist …' : 'Warum nachgefüllt, was aufgefallen ist …'} />
          </V1Field>
          <V1Field label="Foto" wide hint={nachherHatWerte ? 'Hängt an der Messung „nachher".' : 'Ein Foto hängt an der Messung „nachher" — trag dafür mindestens einen Wert ein.'}>
            <FileInput accept="image/*" disabled={!nachherHatWerte && fotos.length === 0} fileNames={fotos.map((f) => f.name)} label="+ Foto" onFiles={setFotos} />
          </V1Field>
          {/* Außerhalb des Felds: ein Knopf in einem <label> öffnet sonst den Dateidialog mit. */}
          {fotos.length > 0 && <button type="button" className="wa-zurueck" onClick={() => setFotos([])}>✕ Foto herausnehmen</button>}
          {weiter(4)}
        </V1Section>
      )}

      {schritt === 4 && (
        <V1Section title="Das wird gespeichert — als ein Vorgang">
          <ul className="wa-liste" data-audit={`${t.audit}-zusammenfassung`}>
            {istWechsel
              ? <li>✓ <b>Wasserwechsel</b> {wechselArt === 'Full' ? 'komplett' : 'teilweise'}, {literZahl != null ? `${zahl(literZahl, 0)} L` : '— L'} {WASSER_NAME[wasser]}{vorschlag?.wasserEc != null ? ` (EC ${zahl(vorschlag.wasserEc, 2)})` : ''}</li>
              : <li>✓ <b>{NACHFUELL_ART[nachfuellArt]}</b> {literZahl != null ? `${zahl(literZahl, Number.isInteger(literZahl) ? 0 : 1)} L` : '— L'} {WASSER_NAME[wasser]}{nurWasser ? ', nur Wasser' : ', mit Dünger'}{vorschlag?.wasserEc != null ? ` (EC Wasser ${zahl(vorschlag.wasserEc, 2)})` : ''}</li>}
            <li>
              {vorherHatWerte || nachherHatWerte ? '✓' : '–'} <b>{[vorherHatWerte, nachherHatWerte].filter(Boolean).length} {[vorherHatWerte, nachherHatWerte].filter(Boolean).length === 1 ? 'Messwert' : 'Messwerte'}</b>
              {vorherHatWerte && `: vorher (${vorherHerkunft === 'Sensor' ? 'Sensor' : vorherHerkunft === 'gemischt' ? 'Sensor und von Hand' : 'von Hand'}${sensorGenutzt && sensorZeit ? `, ${uhrzeit(sensorZeit)}` : ''}${vorher.do != null ? `, DO ${zahl(vorher.do, 1)}` : ''})`}
              {vorherHatWerte && nachherHatWerte && ' ·'}
              {nachherHatWerte && ` nachher (${nachherHerkunft === 'Sensor' ? 'Sensor' : nachherHerkunft === 'gemischt' ? 'Sensor und von Hand' : 'von Hand'}${nach.do != null ? `, DO ${zahl(nach.do, 1)}` : ''})`}
              {!vorherHatWerte && !nachherHatWerte && ' — keine Werte eingetragen'}
            </li>
            <li>
              {zugaben.length > 0 ? '✓' : '–'} <b>{zugaben.length} {zugaben.length === 1 ? 'Verbrauchsbuchung' : 'Verbrauchsbuchungen'}</b>
              {zugaben.length > 0 && ` → Kosten: ${zugaben.map((z) => `${z.name} ${zahl(z.menge, Number.isInteger(z.menge) ? 0 : 1)}\u00a0${z.einheit}`).join(' · ')}`}
            </li>
            {wasserGebucht.some((w) => (w.wasser === 'RO' && osmoseArtikelFehlt) || (w.wasser === 'Tap' && leitungArtikelFehlt)) && (
              <li>✓ <b>Neuer Artikel</b> „{wasserGebucht.find((w) => (w.wasser === 'RO' && osmoseArtikelFehlt) || (w.wasser === 'Tap' && leitungArtikelFehlt))!.name}" unter Kosten (Preis je Liter dort nachtragen)</li>
            )}
          </ul>
          <V1Switch label="Ins Tagebuch" checked={insTagebuch} onChange={setInsTagebuch} hint="Eine Zeile mit Vorher → Nachher, Zugaben und Notiz." />
          {insTagebuch && (
            <div className="wa-vorschau-zeile" data-audit={`${t.audit}-tagebuch-vorschau`}>
              <span className="wa-tag">{istWechsel ? 'Wasserwechsel' : 'Addback'}</span> <b>{tagebuch.titel}</b>
              {tagebuch.text.split('\n').map((zeile) => <p key={zeile}>{zeile}</p>)}
            </div>
          )}
          {istWechsel && (
            <V1Switch label="Wasserwechsel-Erinnerung neu starten" checked={erinnerung} onChange={setErinnerung}
              hint={erinnerung
                ? (stand && naechsterWechsel ? `Nächster Wechsel fällig in ${stand.intervallTage} Tagen (${naechsterWechsel}).` : 'Die Erinnerung zählt ab diesem Wechsel.')
                : 'Der Wechsel wird eingetragen, zählt aber nicht für die Erinnerung — etwa ein kleiner Teilwechsel. Die Dosierung rechnet trotzdem ab hier mit frischem Wasser.'} />
          )}
          {!istWechsel && <p className="wa-hinweis">Nachfüllen ist kein Wasserwechsel — die Wechsel-Erinnerung zählt weiter.</p>}
          <p className="wa-hinweis">Später löschen: immer den ganzen Vorgang — Messwerte, Buchungen und Tagebuchzeile gehen mit.</p>
          <div className="wa-weiter">
            <V1Button type="submit" variant="primary" disabled={speichert || sensorLaedt} audit={`${t.audit}-speichern`}>
              {speichert ? 'Speichert …' : sensorLaedt ? 'Sensorwerte werden geladen …' : t.speichern}
            </V1Button>
          </div>
        </V1Section>
      )}
    </form>
  )
}
