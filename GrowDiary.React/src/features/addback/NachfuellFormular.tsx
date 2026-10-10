import { useEffect, useMemo, useState } from 'react'
import { apiFetch, ApiRequestError } from '../../api'
import { V1Alert } from '../../components/v1'
import { Klappkachel } from '../steuerung/Klappkachel'
import type {
  AddbackEinstellungenDto,
  AddbackVorgangDto,
  AddbackVorgangRequest,
  GrowDetail,
  MischplanVorschlag,
  VorgangBuchungRequest,
  VorgangMessungRequest,
  WasserwechselSensorDto,
  WaterSource,
} from '../../types'
import { classNames, toLocalInputValue } from '../../utils'
import { feldText, unlesbarMeldung, unlesbareFelder, zahlOderNull } from '../../zahlenfeld'
import {
  anteilPlanDosis,
  ecMitDeinenMengen,
  ecTankDanach,
  istGeaendert,
  mengeDerZeile,
  teileText,
  wasserZeilen,
  wertDerZeile,
  zahl,
  type AblaufZeile,
  type Vorbelegung,
  type Werte,
} from '../vorgang/ablauf-rechnung'
import {
  alterMinuten,
  alterText,
  ART_VON_MODUS,
  ecWirkung,
  artikelFuerName,
  eigenesWasserZeile,
  fuellstand,
  MODI,
  nachfuellTagebuch,
  nachmessungZeit,
  phLage,
  phZielText,
  planChips,
  uhrzeitText,
  wirksameWerte,
  zielAbstandEc,
  type Modus,
  type WasserWahl,
} from './nachfuell-rechnung'
import { ArtikelZeile, type Artikel, type Frage } from '../vorgang/ArtikelZeile'
import './nachfuell-formular.css'

type Wasserprofil = { ph: number | null; treatedPh: number | null }
type Extra = { id: number; name: string }

const WASSER_NAMEN: Record<WaterSource, string> = { Tap: 'Leitungswasser', RO: 'Osmosewasser', Mixed: 'Mischung' }

function fehlerText(caught: unknown, ersatz: string): string {
  return caught instanceof ApiRequestError ? caught.message : ersatz
}

/**
 * Das Nachfüllen auf einer Seite (A-006, Etappe 3, freigegeben von Bru am 10.10.2026).
 *
 * Erst die Frage „Was hast du gemacht?" — nur Wasser, Wasser mit Dünger und Zusätzen, oder nur
 * Zusätze — und danach nur die Felder, die dazu gehören. Für „18 L Leitungswasser" sind das die
 * Wasserquelle und die Liter; alles andere kommt aus dem Grow, dem Mischplan und den Sensoren.
 *
 * <b>Gerechnet wird mit den Sensorwerten zum Zeitpunkt</b> (`GET …/wasserwechsel/sensor`), nicht mit
 * der letzten Messung. Ohne Pegelsensor gilt: danach ist der Tank wieder voll (Anlagevolumen), vorher
 * fehlten die nachgefüllten Liter — derselbe Ansatz wie im Wasserwechsel-Ablauf.
 *
 * <b>Artikel nur nach Rückfrage.</b> Gebucht wird nur, was einem bestehenden Verbrauchsartikel gehört.
 * Fehlt er, bietet die Zeile „Als Artikel anlegen …" an; erst nach „Ja, anlegen" entsteht er (ohne
 * Preis) und die Zugabe wird mitgebucht. Der Fork legt nie still etwas an — auch kein Wasser.
 *
 * <b>Nachmessung.</b> Auf Wunsch trägt der Fork nach X Minuten EC und pH aus den Sensoren selbst ein
 * (Hintergrundtakt im Backend). Was der Nutzer selbst als „nachher" einträgt, hat Vorrang.
 *
 * Die Vorschläge rechnet das Backend (`GET …/mixing-plan/vorschlag`); hier wird nichts davon
 * nachgerechnet, nur die Mischung im Tank (`ecTankDanach`) — mit Etikett „Mischrechnung".
 */
/** Was `GET …/mixing-plan` über den Plan dieser Woche sagt — für den Kasten „Dein Plan heute". */
type PlanKopf = { volumenLiter: number | null; programmName: string | null; ecZiel: number | null; phMin: number | null; phMax: number | null }

export function NachfuellFormular({ growId, startModus = 'wasser', vorbelegung = null, onGespeichert }: {
  growId: number
  /** Welcher Fall beim Öffnen gewählt ist (`?modus=`) — für Links und für die Oberflächen-Prüfungen. */
  startModus?: Modus
  /** Werte aus einem Link (`/addback?zeitpunkt=…&ecVorher=…`), z. B. „Nachfüllen eintragen" im Tagebuch. */
  vorbelegung?: Vorbelegung | null
  /** Nach dem Speichern: was am Vorgang hängt, ein Hinweis bei Teilerfolg, die Id und die Zeit der Nachmessung (falls geplant). */
  onGespeichert: (teile: string, hinweis: string | null, vorgangId: number, nachmessung: string | null) => void
}) {
  // ---- Eingaben
  const [modus, setModus] = useState<Modus>(startModus)
  const [zeitpunkt, setZeitpunkt] = useState(() => vorbelegung?.zeitpunkt ?? toLocalInputValue())
  const [wasserWahl, setWasserWahl] = useState<WasserWahl>(vorbelegung?.wasser ?? 'Tap')
  const [osmoseProzent, setOsmoseProzent] = useState('80')
  const [eigenW, setEigenW] = useState({ ec: '', ph: '', haerte: '', temp: '' })
  const [liter, setLiter] = useState(vorbelegung?.liter ?? '')
  const [danach, setDanach] = useState('')
  const [eigen, setEigen] = useState<Werte>({})
  const [abgewaehlt, setAbgewaehlt] = useState<Set<string>>(() => new Set())
  const [buchenAus, setBuchenAus] = useState<Set<string>>(() => new Set())
  const [extra, setExtra] = useState<Extra[]>([])
  const [naechsteExtraId, setNaechsteExtraId] = useState(1)
  const [artikelWahl, setArtikelWahl] = useState<Record<string, number>>({})
  const [frage, setFrage] = useState<Frage | null>(null)
  const [frageFehler, setFrageFehler] = useState<string | null>(null)
  const [vorherHand, setVorherHand] = useState({ ec: vorbelegung?.quelle === 'Hand' ? feldText(vorbelegung.vorher.ec) : '', ph: vorbelegung?.quelle === 'Hand' ? feldText(vorbelegung.vorher.ph) : '' })
  const [nachherHand, setNachherHand] = useState({ ec: feldText(vorbelegung?.nachher.ec), ph: feldText(vorbelegung?.nachher.ph) })
  const [nachmessungAuto, setNachmessungAuto] = useState(true)
  const [nachmessungMinuten, setNachmessungMinuten] = useState('15')
  const [merken, setMerken] = useState(false)
  const [notiz, setNotiz] = useState(vorbelegung?.notiz ?? '')
  const [verbrauch, setVerbrauch] = useState('')

  // ---- Was die Anlage weiß
  const [anlageLiter, setAnlageLiter] = useState<number | null>(null)
  const [artikel, setArtikel] = useState<Artikel[]>([])
  const [profil, setProfil] = useState<Wasserprofil | null>(null)
  const [sensorJetzt, setSensorJetzt] = useState<WasserwechselSensorDto | null | undefined>(undefined)
  const [sensorStand, setSensorStand] = useState<{ fuer: string; daten: WasserwechselSensorDto | null } | null>(null)
  const [plan, setPlan] = useState<PlanKopf | null>(null)
  const [anker, setAnker] = useState<GrowDetail['phasenanker'] | null>(null)
  const [vorschlagRoh, setVorschlag] = useState<MischplanVorschlag | null>(null)
  const [vorschlagFehler, setVorschlagFehler] = useState<string | null>(null)
  const [speichert, setSpeichert] = useState(false)
  const [fehler, setFehler] = useState<string | null>(null)
  // Die Uhrzeit für die Anzeige (Alter der Sensorwerte, Nachmessung in der Vergangenheit); läuft alle 30 s mit.
  const [jetzt, setJetzt] = useState(() => Date.now())

  useEffect(() => {
    const controller = new AbortController()
    void (async () => {
      const [plan, g, liste, wasserprofil, einstellung] = await Promise.all([
        apiFetch<PlanKopf>(`/api/grows/${growId}/mixing-plan`, { signal: controller.signal }).catch(() => null),
        apiFetch<GrowDetail>(`/api/grows/${growId}`, { signal: controller.signal }).catch(() => null),
        apiFetch<Artikel[]>('/api/kosten/artikel', { signal: controller.signal }).catch(() => [] as Artikel[]),
        apiFetch<Wasserprofil>('/api/water-profile', { signal: controller.signal }).catch(() => null),
        apiFetch<AddbackEinstellungenDto>('/api/addback/einstellungen', { signal: controller.signal }).catch(() => null),
      ])
      if (controller.signal.aborted) return
      setAnlageLiter(plan?.volumenLiter ?? null)
      setPlan(plan)
      setAnker(g?.phasenanker ?? null)
      setArtikel(liste.filter((a) => a.aktiv))
      setProfil(wasserprofil)
      if (g?.waterSource && !vorbelegung?.wasser) setWasserWahl(g.waterSource)
      if (einstellung) {
        setNachmessungAuto(einstellung.nachmessungAutomatisch)
        setNachmessungMinuten(String(einstellung.nachmessungMinuten))
      }
    })()
    return () => controller.abort()
  }, [growId, vorbelegung])

  // Live: was die Sensoren jetzt zeigen — alle 30 Sekunden neu.
  useEffect(() => {
    const controller = new AbortController()
    const holen = () => {
      apiFetch<WasserwechselSensorDto>(`/api/grows/${growId}/wasserwechsel/sensor`, { signal: controller.signal })
        .then(setSensorJetzt)
        .catch(() => { if (!controller.signal.aborted) setSensorJetzt(null) })
    }
    holen()
    const takt = window.setInterval(() => { holen(); setJetzt(Date.now()) }, 30_000)
    return () => { window.clearInterval(takt); controller.abort() }
  }, [growId])

  // Die Werte „vorher": was die Sensoren kurz vor dem Zeitpunkt zeigten.
  useEffect(() => {
    const controller = new AbortController()
    const zeit = new Date(zeitpunkt)
    if (Number.isNaN(zeit.getTime())) return
    const warte = window.setTimeout(() => {
      apiFetch<WasserwechselSensorDto>(`/api/grows/${growId}/wasserwechsel/sensor?zeitpunkt=${encodeURIComponent(zeit.toISOString())}`, { signal: controller.signal })
        .then((daten) => setSensorStand({ fuer: zeitpunkt, daten }))
        .catch(() => { if (!controller.signal.aborted) setSensorStand({ fuer: zeitpunkt, daten: null }) })
    }, 250)
    return () => { window.clearTimeout(warte); controller.abort() }
  }, [growId, zeitpunkt])

  const literZahl = zahlOderNull(liter)
  const mitWasser = modus !== 'zusatz'
  const osmoseZahl = zahlOderNull(osmoseProzent)
  const wasserApi: WaterSource = wasserWahl === 'eigen' ? 'Tap' : wasserWahl
  const ecEigenZahl = wasserWahl === 'eigen' ? zahlOderNull(eigenW.ec) : null

  // Der Vorschlag des Plans — neu bei Liter, Wasserart, Anteil und eigenem Wasser-EC.
  useEffect(() => {
    if (!mitWasser || literZahl == null || literZahl <= 0) return
    if (wasserApi === 'Mixed' && (osmoseZahl == null || osmoseZahl < 0 || osmoseZahl > 100)) return
    const controller = new AbortController()
    const suche = new URLSearchParams({ liter: String(literZahl), wasser: wasserApi })
    if (wasserApi === 'Mixed' && osmoseZahl != null) suche.set('osmoseProzent', String(osmoseZahl))
    if (ecEigenZahl != null) suche.set('wasserEc', String(ecEigenZahl))
    const warte = window.setTimeout(() => {
      apiFetch<MischplanVorschlag>(`/api/grows/${growId}/mixing-plan/vorschlag?${suche.toString()}`, { signal: controller.signal })
        .then((daten) => { setVorschlag(daten); setVorschlagFehler(null) })
        .catch((caught) => { if (!controller.signal.aborted) setVorschlagFehler(fehlerText(caught, 'Der Vorschlag konnte nicht gerechnet werden.')) })
    }, 250)
    return () => { window.clearTimeout(warte); controller.abort() }
  }, [growId, mitWasser, literZahl, wasserApi, osmoseZahl, ecEigenZahl])

  const vorschlag = mitWasser && literZahl != null && literZahl > 0 ? vorschlagRoh : null

  // ---- Werte „vorher"
  const sensorAmZeitpunkt = sensorStand?.fuer === zeitpunkt ? sensorStand.daten : null
  const vorbelegtVorher = vorbelegung?.quelle === 'Sensor' && Object.values(vorbelegung.vorher).some((w) => w != null)
    && (vorbelegung.zeitpunkt == null || vorbelegung.zeitpunkt === zeitpunkt)
    ? vorbelegung.vorher : null
  const sensorEc = vorbelegtVorher?.ec ?? sensorAmZeitpunkt?.ec?.wert ?? null
  const sensorPh = vorbelegtVorher?.ph ?? sensorAmZeitpunkt?.ph?.wert ?? null
  const sensorWt = vorbelegtVorher?.wt ?? sensorAmZeitpunkt?.wasserTemp?.wert ?? null
  const vorherEc = zahlOderNull(vorherHand.ec) ?? sensorEc
  const vorherPh = zahlOderNull(vorherHand.ph) ?? sensorPh
  const sensorLaedt = vorbelegtVorher == null && sensorStand?.fuer !== zeitpunkt && !Number.isNaN(new Date(zeitpunkt).getTime())
  const vorherFehlt = !sensorLaedt && sensorEc == null && sensorPh == null

  // ---- Wie voll der Tank ist
  const fl = fuellstand(anlageLiter ?? vorschlag?.anlageLiter ?? null, danach, mitWasser ? literZahl : null)

  // ---- Zeilen: Plan (nur mit Dünger) und eigene Zusätze
  const artikelIdVon = (z: { schluessel: string; name: string; artikelId: number | null }): number | null =>
    // Die Wahl „vorhandenen Artikel“ gilt nur für Plan-Zeilen (ihr Name steht fest). Bei eigenen Zusätzen übernimmt die Zeile
    // den Namen des gewählten Artikels — sonst bliebe die Wahl nach dem Umbenennen hängen und buchte auf einen fremden Artikel.
    (z.schluessel.startsWith('plan:') ? artikelWahl[z.schluessel] : undefined) ?? z.artikelId ?? artikelFuerName(artikel, z.name)?.id ?? null
  const einheitVon = (id: number | null, ersatz: string): string => artikel.find((a) => a.id === id)?.einheit ?? ersatz

  const zeilen: AblaufZeile[] = useMemo(() => {
    const plan = modus === 'mix'
      ? (vorschlag?.zeilen ?? []).map((z): AblaufZeile => ({
        schluessel: `plan:${z.komponente}`, name: z.komponente, art: 'plan', rolle: z.rolle, vorschlagMl: z.vorschlagMl,
        hinweis: z.hinweis, artikelId: z.artikelId, einheit: z.artikelEinheit ?? 'ml',
      }))
      : []
    const eigene = modus === 'wasser'
      ? []
      : extra.map((e): AblaufZeile => ({
        schluessel: `extra:${e.id}`, name: e.name, art: 'extra', rolle: null, vorschlagMl: null, hinweis: null, artikelId: null, einheit: 'ml',
      }))
    return [...plan, ...eigene]
  }, [modus, vorschlag, extra])

  const zeilenMenge = (z: AblaufZeile) => (abgewaehlt.has(z.schluessel) ? 0 : mengeDerZeile(z, eigen))
  const zeilenArtikel = (z: AblaufZeile) => artikelIdVon(z)
  const wirdGebucht = (z: AblaufZeile) => zeilenArtikel(z) != null && zeilenMenge(z) > 0 && !buchenAus.has(z.schluessel)
  const zugegeben = zeilen.filter((z) => zeilenMenge(z) > 0)
  const gebucht = zeilen.filter(wirdGebucht)
  const ohneArtikel = zugegeben.filter((z) => zeilenArtikel(z) == null)

  const anteilOsmose = vorschlag?.osmoseAnteil ?? (wasserApi === 'RO' ? 1 : wasserApi === 'Mixed' ? (osmoseZahl ?? 0) / 100 : 0)
  const wasserTeile = mitWasser && wasserWahl !== 'eigen' && literZahl != null && literZahl > 0 ? wasserZeilen(literZahl, wasserApi, anteilOsmose) : []
  const wasserBuchungen = wasserTeile.filter((w) => {
    const a = artikelFuerName(artikel, w.name)
    return a != null && !buchenAus.has(`wasser:${w.name}`) && w.menge > 0
  })
  const wasserOhneArtikel = wasserTeile.filter((w) => artikelFuerName(artikel, w.name) == null)

  // ---- Die Erwartung: Mischrechnung im Tank
  const wasserEc = ecEigenZahl ?? vorschlag?.wasserEc ?? null
  const ecLoesung = modus === 'mix'
    ? (ecMitDeinenMengen(wasserEc, vorschlag?.ecZielDuenger ?? null, anteilPlanDosis(zeilen, wirksameWerte(eigen, abgewaehlt))) ?? wasserEc)
    : wasserEc
  const ecErwartet = modus === 'zusatz' ? null : ecTankDanach(fl.vorher, vorherEc, literZahl, ecLoesung)
  const ecZielTank = vorschlag?.ecZielGesamt ?? plan?.ecZiel ?? null
  const phMin = vorschlag?.phMin ?? plan?.phMin ?? null
  const phMax = vorschlag?.phMax ?? plan?.phMax ?? null
  const planZeit = planChips(anker)
  const wirkung = modus === 'zusatz' ? null : ecWirkung(vorherEc, ecErwartet)
  const ecAnteilDuenger = modus === 'mix' && ecLoesung != null && wasserEc != null ? ecLoesung - wasserEc : null
  const wasserPh = wasserWahl === 'eigen' ? zahlOderNull(eigenW.ph) : wasserWahl === 'Tap' ? (profil?.treatedPh ?? profil?.ph ?? null) : null

  // ---- Nachmessung
  const nachherHatWerte = zahlOderNull(nachherHand.ec) != null || zahlOderNull(nachherHand.ph) != null
  const sensorenDa = sensorJetzt === undefined ? true : sensorJetzt != null && (sensorJetzt.ec != null || sensorJetzt.ph != null)
  const minutenZahl = zahlOderNull(nachmessungMinuten)
  const nachmessungAm = nachmessungZeit(zeitpunkt, minutenZahl != null && Number.isInteger(minutenZahl) ? minutenZahl : null)
  const nachmessungGeplant = nachmessungAuto && sensorenDa && !nachherHatWerte && nachmessungAm != null
  const vergangen = nachmessungAm != null && nachmessungAm.getTime() < jetzt

  // ---- Was gespeichert wird
  const wasserName = wasserWahl === 'eigen' ? 'eigenes Wasser' : wasserWahl === 'Mixed' ? `Mischung (${zahl(osmoseZahl ?? 0, 0)} % Osmose)` : WASSER_NAMEN[wasserWahl]
  const eigeneZeile = wasserWahl === 'eigen' ? eigenesWasserZeile(eigenW) : null
  const zugabenText = zeilen.filter((z) => zeilenMenge(z) > 0)
    .map((z) => ({ name: z.name, menge: zeilenMenge(z), einheit: einheitVon(zeilenArtikel(z), z.einheit) }))
  const nachherWerte = { ec: zahlOderNull(nachherHand.ec), ph: zahlOderNull(nachherHand.ph), wt: null, do: null, orp: null }
  const tagebuch = nachfuellTagebuch(modus, {
    titel: '',
    liter: mitWasser ? literZahl ?? 0 : 0,
    wasserName,
    vorher: { ec: vorherEc, ph: vorherPh, wt: sensorWt, do: null, orp: null },
    nachher: nachherWerte,
    zugaben: zugabenText,
    notiz: [eigeneZeile, notiz.trim()].filter(Boolean).join('\n'),
  })

  // ---- Artikel anlegen — nur nach Rückfrage
  async function artikelAnlegen() {
    if (!frage) return
    setFrageFehler(null)
    try {
      const neu = await apiFetch<Artikel>('/api/kosten/artikel', {
        method: 'POST',
        body: JSON.stringify({ name: frage.name.trim(), einheit: frage.einheit, aktiv: true, aufGrowBuchen: true, notiz: 'Angelegt beim Nachfüllen. Preis hier nachtragen, dann rechnet die Kostenseite mit.' }),
      })
      setArtikel((alt) => [...alt.filter((a) => a.id !== neu.id), neu])
      setFrage(null)
    } catch (caught) {
      setFrageFehler(fehlerText(caught, 'Der Artikel konnte nicht angelegt werden.'))
    }
  }

  const setze = (schluessel: string, wert: string | null) => setEigen((alt) => {
    const neu = { ...alt }
    if (wert == null) delete neu[schluessel]
    else neu[schluessel] = wert
    return neu
  })
  const umschalten = (menge: Set<string>, setMenge: (m: Set<string>) => void, schluessel: string, an: boolean) => {
    const neu = new Set(menge)
    if (an) neu.add(schluessel)
    else neu.delete(schluessel)
    setMenge(neu)
  }

  async function speichern() {
    if (speichert) return
    const unlesbar = unlesbarMeldung(unlesbareFelder([
      [liter, 'Wasser (Liter)'], [danach, 'Füllstand danach'], [osmoseProzent, 'Anteil Osmose'], [verbrauch, 'Verbrauch'],
      [vorherHand.ec, 'EC jetzt'], [vorherHand.ph, 'pH jetzt'], [nachherHand.ec, 'EC nachher'], [nachherHand.ph, 'pH nachher'],
      [eigenW.ec, 'EC des Wassers'], [eigenW.ph, 'pH des Wassers'], [eigenW.haerte, 'Härte'], [eigenW.temp, 'Temperatur'],
      [nachmessungMinuten, 'Minuten bis zur Nachmessung'],
      ...zeilen.map((z): [string, string] => [wertDerZeile(z, eigen), `Menge ${z.name}`]),
    ]))
    if (unlesbar) { setFehler(unlesbar); return }
    if (mitWasser && (literZahl == null || literZahl <= 0)) { setFehler('Wie viele Liter Wasser hast du eingefüllt?'); return }
    if (wasserWahl === 'eigen' && ecEigenZahl == null) { setFehler('Trag den EC deines Wassers ein — oder wähl eine Quelle aus der Liste.'); return }
    if (modus === 'zusatz' && zugegeben.length === 0) { setFehler('Was hast du zugegeben? Wähl einen Zusatz und trag die Menge ein.'); return }
    if (extra.some((e) => e.name.trim() === '')) { setFehler('Ein Zusatz hat keinen Namen — trag ihn ein oder nimm die Zeile heraus.'); return }
    if (nachmessungAuto && (minutenZahl == null || !Number.isInteger(minutenZahl) || minutenZahl < 1 || minutenZahl > 240)) {
      setFehler('Die Nachmessung kann nach 1 bis 240 ganzen Minuten erfolgen.')
      return
    }

    const messung = (ec: number | null, ph: number | null, wt: number | null, extraFelder: Partial<VorgangMessungRequest>): VorgangMessungRequest | null =>
      ec == null && ph == null && wt == null ? null : { ...extraFelder, reservoirEc: ec, reservoirPh: ph, reservoirWaterTempC: wt }

    const vonHand = zahlOderNull(vorherHand.ec) != null || zahlOderNull(vorherHand.ph) != null
    const vomSensor = (zahlOderNull(vorherHand.ec) == null && sensorEc != null) || (zahlOderNull(vorherHand.ph) == null && sensorPh != null)
    const vorherHerkunft: 'Sensor' | 'Hand' | 'gemischt' = vomSensor ? (vonHand ? 'gemischt' : 'Sensor') : 'Hand'
    const sensorZeit = [sensorAmZeitpunkt?.ec, sensorAmZeitpunkt?.ph, sensorAmZeitpunkt?.wasserTemp]
      .filter((w) => w != null && w.zeitUtc !== '').map((w) => w!.zeitUtc).sort().at(-1) ?? null

    const buchungen: VorgangBuchungRequest[] = [
      ...wasserBuchungen.map((w) => ({ artikelId: artikelFuerName(artikel, w.name)!.id, menge: w.menge })),
      ...gebucht.map((z) => ({ artikelId: zeilenArtikel(z)!, menge: zeilenMenge(z) })),
    ]

    const body: AddbackVorgangRequest = {
      zeitpunktLokal: zeitpunkt || null,
      art: ART_VON_MODUS[modus],
      liter: mitWasser ? literZahl : null,
      wasser: wasserApi,
      osmoseProzent: wasserApi === 'Mixed' ? osmoseZahl : null,
      wasserEcMsCm: mitWasser ? ecEigenZahl ?? vorschlag?.wasserEcVorschlag ?? null : null,
      ecZiel: modus === 'mix' ? vorschlag?.ecZielGesamt ?? null : null,
      vorher: messung(vorherEc, vorherPh, sensorWt, { herkunft: vorherHerkunft, sensorZeitUtc: vomSensor ? sensorZeit : null, zeitpunktLokal: vorherHerkunft === 'Sensor' && sensorZeit ? toLocalInputValue(new Date(sensorZeit)) : null }),
      nachher: messung(nachherWerte.ec, nachherWerte.ph, null, { herkunft: vorbelegung?.quelle === 'Sensor' && vorbelegung.nachher.ec === nachherWerte.ec && vorbelegung.nachher.ph === nachherWerte.ph ? 'Sensor' : 'Hand' }),
      buchungen,
      notiz: [eigeneZeile, notiz.trim()].filter(Boolean).join('\n') || null,
      tagebuch,
      verbrauchLiter: zahlOderNull(verbrauch),
      fuellstandDanachLiter: mitWasser && fl.danach != null && fl.danach !== (anlageLiter ?? vorschlag?.anlageLiter ?? null) ? fl.danach : null,
      nachmessungMinuten: nachmessungGeplant ? minutenZahl : null,
    }

    setSpeichert(true)
    setFehler(null)
    try {
      const vorgang = await apiFetch<AddbackVorgangDto>(`/api/grows/${growId}/addback/vorgaenge`, { method: 'POST', body: JSON.stringify(body) })
      let hinweis: string | null = null
      if (merken) {
        try {
          await apiFetch<AddbackEinstellungenDto>('/api/addback/einstellungen', {
            method: 'PUT',
            body: JSON.stringify({ nachmessungAutomatisch: nachmessungAuto, nachmessungMinuten: minutenZahl ?? 15 }),
          })
        } catch (caught) {
          hinweis = `Das Nachfüllen ist gespeichert, deine Vorgabe für die Nachmessung nicht: ${fehlerText(caught, 'Der Server hat sie nicht angenommen.')}`
        }
      }
      onGespeichert(teileText(vorgang), hinweis, vorgang.id, nachmessungGeplant && nachmessungAm ? uhrzeitText(nachmessungAm) : null)
    } catch (caught) {
      setFehler(fehlerText(caught, 'Das Nachfüllen konnte nicht gespeichert werden.'))
    } finally {
      setSpeichert(false)
    }
  }

  // ---- Anzeige
  const alterJetzt = sensorJetzt?.ec ? alterMinuten(sensorJetzt.ec.zeitUtc, new Date(jetzt)) : sensorJetzt?.ph ? alterMinuten(sensorJetzt.ph.zeitUtc, new Date(jetzt)) : null
  const keinLive = sensorJetzt === null || (sensorJetzt != null && sensorJetzt.ec == null && sensorJetzt.ph == null)
  const chips = artikel.filter((a) => !zeilen.some((z) => zeilenArtikel(z) === a.id) && !['leitungswasser', 'osmosewasser'].includes(a.name.trim().toLowerCase())).slice(0, 8)
  const letzteSchritt = modus === 'wasser' ? 3 : modus === 'mix' ? 4 : 3

  const modusKnoepfe = (
    <div className="nf-modi" role="group" aria-label="Was hast du gemacht?">
      {MODI.map((m) => (
        <button key={m.modus} type="button" className="nf-modus" aria-pressed={modus === m.modus} data-audit={`nachfuellen-modus-${m.modus}`}
          onClick={() => { setModus(m.modus); setFrage(null); setFehler(null) }}>
          <strong>{m.titel}</strong>
          <span>{m.hinweis}</span>
        </button>
      ))}
    </div>
  )

  return (
    <div className="nf" data-audit="nachfuellen-formular">
      {vorbelegung != null && (
        <p className="st-hinweis nf-vorbelegt" data-audit="nachfuellen-vorbelegt">
          Vorbelegt aus dem Link (zum Beispiel vom Grow-Tagebuch): Zeitpunkt, Liter und die gemessenen Werte stehen schon da — prüf sie und trag das Nachfüllen ein.
        </p>
      )}
      {/* ---- Live */}
      <div className={classNames('nf-live', keinLive && 'aus')} data-audit="nachfuellen-live">
        <div className="kopf">
          <i className="dot" aria-hidden="true" />
          <span className="titel">{keinLive ? 'Kein Live-Wert' : 'Live jetzt'}</span>
          <span className="al">
            {sensorJetzt === undefined ? 'lädt …' : keinLive ? 'EC- oder pH-Sensor liefert nichts' : `aus deinen Sensoren, ${alterJetzt != null ? alterText(alterJetzt) : ''}`}
          </span>
        </div>
        {!keinLive && sensorJetzt && (
          <div className="nf-kachel4">
            <div className="nf-k"><span className="k">EC</span><span className="z">{sensorJetzt.ec ? zahl(sensorJetzt.ec.wert, 2) : '–'}<small>mS/cm</small></span></div>
            <div className="nf-k"><span className="k">pH</span><span className="z">{sensorJetzt.ph ? zahl(sensorJetzt.ph.wert, 2) : '–'}</span></div>
            <div className="nf-k"><span className="k">Wasser-Temp.</span><span className="z">{sensorJetzt.wasserTemp ? zahl(sensorJetzt.wasserTemp.wert, 1) : '–'}<small>°C</small></span></div>
            <div className="nf-k"><span className="k">Volumen</span><span className="z">{anlageLiter != null ? zahl(anlageLiter, 0) : '–'}<small>L</small></span><span className="n">Anlage (Hydro-System)</span></div>
          </div>
        )}
        {keinLive && sensorJetzt !== undefined && (
          <p className="st-hinweis" style={{ margin: 0 }}>Es gibt gerade keine Sensorwerte. Mess den Tank von Hand und trag EC und pH unten ein.</p>
        )}
      </div>

      {/* ---- Dein Plan heute */}
      {plan?.programmName && (
        <div className="nf-plan" data-audit="nachfuellen-plan">
          <div className="k">Dein Plan heute</div>
          {planZeit && <div className="kz"><span>{planZeit.tag}</span><span>{planZeit.woche}</span></div>}
          <div className="nm">Plan: <b>{plan.programmName}</b></div>
          <div className="ziel">
            <div>
              <span className="k">EC-Ziel</span>
              <b>{ecZielTank != null ? `${zahl(ecZielTank, 2)} mS/cm` : '–'}</b>
              {vorschlag?.ecZielDuenger != null && vorschlag.wasserEc != null && <small>Dünger {zahl(vorschlag.ecZielDuenger, 2)} + Wasser {zahl(vorschlag.wasserEc, 2)}</small>}
            </div>
            <div>
              <span className="k">pH-Ziel</span>
              <b>{phZielText(phMin, phMax) ?? '–'}</b>
              {phLage(sensorJetzt?.ph?.wert ?? null, phMin, phMax) && <small>Tank jetzt {zahl(sensorJetzt!.ph!.wert, 2)} · {phLage(sensorJetzt?.ph?.wert ?? null, phMin, phMax)}</small>}
              {phLage(sensorJetzt?.ph?.wert ?? null, phMin, phMax) == null && sensorJetzt?.ph != null && <small>Tank jetzt {zahl(sensorJetzt.ph.wert, 2)}</small>}
            </div>
          </div>
          {zielAbstandEc(sensorJetzt?.ec?.wert ?? null, ecZielTank) && <div className="abstand">{zielAbstandEc(sensorJetzt?.ec?.wert ?? null, ecZielTank)}</div>}
        </div>
      )}

      {/* ---- 1 · Was hast du gemacht? */}
      <div className="v1-eyebrow nf-ek nf-abschnitt"><span className="nf-nr">1</span>Was hast du gemacht?</div>
      {modusKnoepfe}

      {/* ---- 2 · Wasser */}
      {mitWasser && (
        <>
          <div className="v1-eyebrow nf-ek nf-abschnitt"><span className="nf-nr">2</span>Wasser</div>
          <article className="v1-card tone-neutral">
            <div className="nf-feld">
              <label className="l" htmlFor="nf-wasser">Womit hast du aufgefüllt?</label>
              <select id="nf-wasser" className="nf-select" value={wasserWahl} onChange={(e) => setWasserWahl(e.target.value as WasserWahl)}>
                <option value="Tap">Leitungswasser</option>
                <option value="RO">Osmose / VE-Wasser</option>
                <option value="Mixed">Mischung (Osmose + Leitung)</option>
                <option value="eigen">Eigene Werte eintragen …</option>
              </select>
            </div>

            {wasserWahl === 'Mixed' && (
              <div className="nf-feld">
                <label className="l" htmlFor="nf-osmose">Anteil Osmosewasser</label>
                <div className="nf-mit">
                  <input id="nf-osmose" inputMode="numeric" value={osmoseProzent} onChange={(e) => setOsmoseProzent(e.target.value)} style={{ maxWidth: 120 }} />
                  <span className="e">%</span>
                </div>
                <input type="range" min={0} max={100} step={5} aria-label="Anteil Osmosewasser in Prozent" value={osmoseZahl ?? 0} onChange={(e) => setOsmoseProzent(e.target.value)} />
                <small>Der Rest ist Leitungswasser. EC und pH der Mischung kommen aus deinem Wasserprofil.</small>
              </div>
            )}

            {wasserWahl === 'eigen' && (
              <>
                <div className="nf-vier">
                  <FeldMit label="EC" aria="EC des Wassers" einheit="mS/cm" wert={eigenW.ec} onChange={(v) => setEigenW({ ...eigenW, ec: v })} />
                  <FeldMit label="pH" aria="pH des Wassers" einheit="pH" wert={eigenW.ph} onChange={(v) => setEigenW({ ...eigenW, ph: v })} />
                  <FeldMit label="Härte" aria="Härte des Wassers" einheit="°dH" hinweis="optional" wert={eigenW.haerte} onChange={(v) => setEigenW({ ...eigenW, haerte: v })} />
                  <FeldMit label="Temperatur" aria="Temperatur des Wassers" einheit="°C" hinweis="optional" wert={eigenW.temp} onChange={(v) => setEigenW({ ...eigenW, temp: v })} />
                </div>
                <small className="nf-klein">Gilt nur für diesen Eintrag. EC und pH gehen in die Rechnung; alle Werte stehen mit im Eintrag.</small>
              </>
            )}

            <div className="nf-zwei" style={{ marginTop: 'var(--s-3)' }}>
              <div className="nf-feld nf-gross">
                <label className="l" htmlFor="nf-liter">Wie viel Wasser?</label>
                <div className="nf-mit">
                  <input id="nf-liter" inputMode="decimal" value={liter} onChange={(e) => setLiter(e.target.value)} placeholder="z. B. 18" data-audit="nachfuellen-liter" />
                  <span className="e">L</span>
                </div>
                <small>Das ist die einzige Pflichtangabe.</small>
              </div>
              <div className="nf-feld">
                <label className="l" htmlFor="nf-danach">Füllstand danach</label>
                <div className="nf-mit">
                  <input id="nf-danach" inputMode="decimal" value={danach} onChange={(e) => setDanach(e.target.value)} placeholder={anlageLiter != null ? zahl(anlageLiter, 0) : ''} />
                  <span className="e">L</span>
                </div>
                <small>Voll = Anlagevolumen aus deinem Hydro-System{anlageLiter != null ? ` (${zahl(anlageLiter, 0)} L)` : ''}. Nur ändern, wenn das Reservoir danach nicht ganz voll ist.</small>
              </div>
            </div>

            {wasserWahl !== 'eigen' && (
              <div className="nf-wasser" data-audit="nachfuellen-wasserwerte">
                {wasserWahl === 'Mixed' && literZahl != null && literZahl > 0 && (
                  <span><b>{zahl(literZahl * (anteilOsmose), 1)} L</b> Osmose + <b>{zahl(literZahl * (1 - anteilOsmose), 1)} L</b> Leitung</span>
                )}
                <span>EC <b>{wasserEc != null ? zahl(wasserEc, 2) : '–'}</b> mS/cm</span>
                {wasserWahl !== 'RO' && wasserPh != null && <span>pH <b>{zahl(wasserPh, 1)}</b></span>}
                <span className="nf-leise">{wasserEc != null ? 'aus deinem Wasserprofil — nichts abzutippen' : literZahl != null && literZahl > 0 ? 'Zu diesem Wasser fehlt der EC im Wasserprofil — trag ihn dort ein, dann rechnet der Fork mit.' : 'EC und pH kommen aus deinem Wasserprofil, sobald du die Liter einträgst.'}</span>
              </div>
            )}
            {vorschlagFehler && <p className="nf-fehler" role="alert">{vorschlagFehler}</p>}

            {wasserTeile.map((w) => (
              <ArtikelZeile key={w.name} name={w.name} menge={`${zahl(w.menge, 1)} L`} einheit="L"
                artikel={artikelFuerName(artikel, w.name)} aus={buchenAus.has(`wasser:${w.name}`)}
                frage={frage?.schluessel === `wasser:${w.name}` ? frage : null} frageFehler={frageFehler} einheitFest="L"
                onBuchen={(an) => umschalten(buchenAus, setBuchenAus, `wasser:${w.name}`, !an)}
                onFrage={() => { setFrageFehler(null); setFrage({ schluessel: `wasser:${w.name}`, name: w.name, einheit: 'L' }) }}
                onEinheit={(einheit) => frage && setFrage({ ...frage, einheit })} onAnlegen={() => void artikelAnlegen()} onNein={() => setFrage(null)} />
            ))}
            {wasserWahl === 'eigen' && <p className="nf-klein">Eigenes Wasser hat keinen Artikel — es wird nicht gebucht.</p>}
          </article>
        </>
      )}

      {/* ---- Dünger & Zusätze */}
      {modus !== 'wasser' && (
        <>
          <div className="v1-eyebrow nf-ek nf-abschnitt"><span className="nf-nr">{mitWasser ? 3 : 2}</span>{modus === 'mix' ? 'Dünger & Zusätze' : 'Zusätze'}</div>
          <article className="v1-card tone-neutral" data-audit="nachfuellen-zusaetze">
            <p className="st-hinweis">
              {modus === 'mix'
                ? 'Vorgabe ist der aktuelle Mischplan, umgerechnet auf deine Liter. Du kannst jede Menge überschreiben und jeden Haken wegnehmen — abgewählte Zeilen werden nicht eingetragen.'
                : 'Trag ein, was du ins Reservoir gegeben hast. Das Volumen bleibt gleich.'}
            </p>
            {modus === 'mix' && vorschlag?.luecke && <V1Alert tone="neutral" message={vorschlag.luecke} />}
            {modus === 'mix' && (literZahl == null || literZahl <= 0) && <p className="st-hinweis">Trag oben die Liter ein, dann rechnet der Fork den Mischplan darauf.</p>}
            {zeilen.length === 0 && modus === 'zusatz' && <p className="st-hinweis">Noch nichts hinzugefügt — wähle unten einen Zusatz.</p>}
            <div>
              {zeilen.map((z) => {
                const id = zeilenArtikel(z)
                const a = artikel.find((x) => x.id === id) ?? null
                const einheit = a?.einheit ?? z.einheit
                const an = !abgewaehlt.has(z.schluessel)
                const geaendert = istGeaendert(z, eigen)
                return (
                  <div key={z.schluessel} className={classNames('nf-zr', !an && 'aus')} data-audit="nachfuellen-zeile">
                    <label>
                      <input type="checkbox" checked={an} aria-label={`${z.name} zugegeben`} onChange={(e) => umschalten(abgewaehlt, setAbgewaehlt, z.schluessel, !e.target.checked)} />
                      {z.art === 'extra'
                        ? (
                          <span className="nm">
                            <input list="nf-artikelnamen" value={z.name} placeholder="Name des Zusatzes" aria-label="Name des Zusatzes"
                              onChange={(e) => setExtra((alt) => alt.map((x) => (`extra:${x.id}` === z.schluessel ? { ...x, name: e.target.value } : x)))} />
                          </span>
                        )
                        : <span className="nm">{z.name}</span>}
                    </label>
                    <span className="nf-mit">
                      <input inputMode="decimal" value={wertDerZeile(z, eigen)} onChange={(e) => setze(z.schluessel, e.target.value)} aria-label={`${z.name || 'Zusatz'} Menge`} placeholder={z.art === 'plan' && z.vorschlagMl == null ? 'n. Messg.' : '0'} />
                      <span className="e">{einheit}</span>
                    </span>
                    <span className="rt">
                      {z.art === 'plan'
                        ? <>Mischplan: {z.vorschlagMl != null && literZahl ? `${zahl(z.vorschlagMl / literZahl, 1)} ${einheit}/L` : 'nach Messung'}{geaendert ? ' · von dir geändert ' : ' '}
                          {geaendert && <button type="button" className="nf-zurueck" onClick={() => setze(z.schluessel, null)}>↺ Vorschlag {zahl(z.vorschlagMl!, 0)}</button>}</>
                        : 'eigener Zusatz'}
                      {z.art === 'extra' && <button type="button" className="nf-zeile-weg" onClick={() => setExtra((alt) => alt.filter((x) => `extra:${x.id}` !== z.schluessel))}>✕ entfernen</button>}
                    </span>
                    {an && z.name.trim() !== '' && (
                      <ArtikelZeile name={z.name} menge={null} einheit={einheit} artikel={a} aus={buchenAus.has(z.schluessel)}
                        frage={frage?.schluessel === z.schluessel ? frage : null} frageFehler={frageFehler}
                        auswahl={a == null ? artikel.filter((x) => !['leitungswasser', 'osmosewasser'].includes(x.name.trim().toLowerCase())) : undefined}
                        onWahl={(artikelId) => {
                          if (z.art === 'plan') { setArtikelWahl((alt) => ({ ...alt, [z.schluessel]: artikelId })); return }
                          const gewaehlt = artikel.find((x) => x.id === artikelId)
                          if (gewaehlt) setExtra((alt) => alt.map((x) => (`extra:${x.id}` === z.schluessel ? { ...x, name: gewaehlt.name } : x)))
                        }}
                        onBuchen={(ja) => umschalten(buchenAus, setBuchenAus, z.schluessel, !ja)}
                        onFrage={() => { setFrageFehler(null); setFrage({ schluessel: z.schluessel, name: z.name, einheit: 'ml' }) }}
                        onEinheit={(e) => frage && setFrage({ ...frage, einheit: e })} onAnlegen={() => void artikelAnlegen()} onNein={() => setFrage(null)} />
                    )}
                  </div>
                )
              })}
            </div>
            <div className="nf-chips">
              <span className="l">Zusatz hinzufügen:</span>
              {chips.map((a) => (
                <button key={a.id} type="button" onClick={() => { const id = naechsteExtraId; setNaechsteExtraId(id + 1); setExtra((alt) => [...alt, { id, name: a.name }]) }}>+ {a.name}</button>
              ))}
              <button type="button" data-audit="nachfuellen-zusatz-anderer" onClick={() => { const id = naechsteExtraId; setNaechsteExtraId(id + 1); setExtra((alt) => [...alt, { id, name: '' }]) }}>+ Anderer …</button>
            </div>
            <datalist id="nf-artikelnamen">{artikel.map((a) => <option key={a.id} value={a.name} />)}</datalist>
          </article>
        </>
      )}

      {/* ---- Wenn die Sensoren fehlen */}
      {vorherFehlt && (
        <article className="v1-card tone-neutral" style={{ marginTop: 'var(--s-4)' }}>
          <p className="st-hinweis" style={{ marginTop: 0 }}>Für diesen Zeitpunkt hat kein Sensor EC oder pH geliefert. Trag die Werte von vorher von Hand ein:</p>
          <div className="nf-zwei" style={{ maxWidth: 520 }}>
            <FeldMit label="EC vorher" einheit="mS/cm" wert={vorherHand.ec} onChange={(v) => setVorherHand({ ...vorherHand, ec: v })} />
            <FeldMit label="pH vorher" einheit="pH" wert={vorherHand.ph} onChange={(v) => setVorherHand({ ...vorherHand, ph: v })} />
          </div>
        </article>
      )}

      {/* ---- Erwartung */}
      {modus === 'mix' && ecLoesung != null && literZahl != null && literZahl > 0 && (
        <>
          <div className="v1-eyebrow nf-abschnitt">Die neue Lösung (so mischst du an)</div>
          <div className="nf-vor nf-neu" data-audit="nachfuellen-neue-loesung">
            <div>
              <div className="k">EC</div>
              <div className="z">≈ {zahl(ecLoesung, 2)}</div>
              <div className="n">
                {wasserEc != null && ecAnteilDuenger != null ? `${zahl(wasserEc, 2)} Wasser + ${zahl(ecAnteilDuenger, 2)} Dünger` : 'Wasser + Dünger'}
                {ecZielTank != null && Math.abs(ecLoesung - ecZielTank) < 0.005 && <><br />= Ziel laut Plan ✓</>}
              </div>
            </div>
            <div>
              <div className="k">pH</div>
              <div className="z">{phZielText(phMin, phMax) ?? '–'}</div>
              <div className="n">{phMin != null && phMax != null ? 'Ziel laut Plan · ' : ''}nach dem Anmischen messen und einstellen</div>
            </div>
            <div className="nf-neu-hin">Gilt für die Menge, die du ansetzt – im Extratank oder direkt im Tank. Trag oben nur die Liter ein, die du in den Tank gießt.</div>
          </div>
        </>
      )}
      <div className="v1-eyebrow nf-abschnitt">{mitWasser ? `Im Tank danach (${literZahl != null && literZahl > 0 ? `${zahl(literZahl, Number.isInteger(literZahl) ? 0 : 1)} L dazu` : 'Erwartung, keine Messung'})` : 'So sieht das Reservoir danach aus (Erwartung, keine Messung)'}</div>
      <div className="nf-vor" data-audit="nachfuellen-erwartung">
        <div>
          <div className="k">Volumen</div>
          <div className="z">{fl.danach != null ? <>{mitWasser && literZahl != null && literZahl > 0 && fl.vorher != null && <s>{zahl(fl.vorher, 1)} L</s>}{zahl(fl.danach, 1)} L</> : '–'}</div>
          <div className="n">{!mitWasser ? 'unverändert'
            : fl.danach == null ? 'Das Anlagevolumen fehlt — trag den Füllstand danach ein.'
              : literZahl != null && literZahl > 0 ? `${zahl(fl.danach, 1)} L danach − ${zahl(literZahl, 1)} L eingefüllt`
                : 'Trag die Liter ein, dann rechnet der Fork.'}</div>
        </div>
        <div>
          <div className="k">EC</div>
          <div className="z">{vorherEc != null && <s>{zahl(vorherEc, 2)}</s>}{modus === 'zusatz' ? (vorherEc != null ? '' : '–') : ecErwartet != null ? `≈ ${zahl(ecErwartet, 2)}` : '–'}</div>
          <div className="n">
            {modus === 'zusatz' ? 'steigt je nach Zusatz — nachmessen'
              : ecErwartet == null ? 'Mischrechnung braucht EC vorher, Liter und das Wasserprofil'
                : modus === 'mix' ? `Mischrechnung mit deinen Mengen${vorschlag?.ecZielGesamt != null ? `; Ziel ${zahl(vorschlag.ecZielGesamt, 2)}` : ''}`
                  : 'Mischrechnung — ohne Dünger sinkt der EC'}
          </div>
          {wirkung && <span className={classNames('nf-wirkung', wirkung.richtung)}>{wirkung.text}</span>}
        </div>
        <div>
          <div className="k">pH</div>
          <div className="z">{vorherPh != null ? zahl(vorherPh, 2) : '–'}{vorherPh != null && modus !== 'zusatz' && <small className="nf-jetzt">jetzt</small>}</div>
          <div className="n">{modus === 'zusatz' ? 'pH-Zusätze ändern ihn — nachmessen' : `wird nicht vorausberechnet – nach dem Durchmischen nachmessen${wasserPh != null && wasserWahl !== 'RO' ? ` (${wasserName} hat pH ${zahl(wasserPh, 1)})` : ''}`}</div>
        </div>
      </div>

      <div className="nf-ein" data-audit="nachfuellen-eintrag">
        <div className="kopf">✓ Das wird eingetragen</div>
        {mitWasser && (
          <div className="z"><span className="k">Wasser</span><span className="w"><b>{literZahl != null && literZahl > 0 ? `${zahl(literZahl, Number.isInteger(literZahl) ? 0 : 1)} L` : 'Noch keine Liter'}</b> {wasserName}<small>EC {wasserEc != null ? zahl(wasserEc, 2) : '–'} mS/cm{wasserPh != null ? ` · pH ${zahl(wasserPh, 1)}` : ''}</small></span></div>
        )}
        {zugegeben.length > 0 && (
          <div className="z"><span className="k">Zusätze</span><span className="w liste">{zugegeben.map((z) => <span key={z.schluessel}><em style={{ fontStyle: 'normal' }}>{z.name}</em><i>{zahl(zeilenMenge(z), Number.isInteger(zeilenMenge(z)) ? 0 : 1)} {einheitVon(zeilenArtikel(z), z.einheit)}</i></span>)}</span></div>
        )}
        <div className="z">
          <span className="k">Verbrauch</span>
          <span className="w">
            <span className="pills">
              {wasserBuchungen.map((w) => <span key={w.name} className="pill">✓ {w.name} {zahl(w.menge, 1)} L</span>)}
              {gebucht.map((z) => <span key={z.schluessel} className="pill">✓ {z.name}</span>)}
              {[...wasserOhneArtikel.map((w) => w.name), ...ohneArtikel.map((z) => z.name)].map((n) => <span key={n} className="pill nein">⚠ {n} · kein Artikel</span>)}
              {wasserBuchungen.length + gebucht.length + wasserOhneArtikel.length + ohneArtikel.length === 0 && <span className="pill aus">nichts zu buchen</span>}
            </span>
            {wasserOhneArtikel.length + ohneArtikel.length > 0 && <small>Ohne Artikel wird nur im Tagebuch notiert. „Als Artikel anlegen …“ oben bucht es mit.</small>}
          </span>
        </div>
        <div className="z"><span className="k">Reservoir</span><span className="w"><b>{fl.danach != null ? `${zahl(fl.danach, 1)} L` : '–'}</b> danach{!mitWasser ? ' (unverändert)' : ''}</span></div>
        <div className="z"><span className="k">Tagebuch</span><span className="w">{mitWasser && (literZahl == null || literZahl <= 0) ? 'eine Zeile, sobald die Liter eingetragen sind' : `1 Zeile: ${tagebuch.titel}`}</span></div>
        <div className="z"><span className="k">Nachmessung</span><span className="w">
          {nachherHatWerte ? <b>selbst eingetragen</b>
            : nachmessungGeplant && nachmessungAm ? <><b>automatisch um {uhrzeitText(nachmessungAm)}</b><small>EC und pH aus den Sensoren</small></>
              : 'keine'}
        </span></div>
      </div>

      {/* ---- Nachmessung */}
      <div className="v1-eyebrow nf-ek nf-abschnitt"><span className="nf-nr">{letzteSchritt}</span>Nachmessung</div>
      <article className="v1-card tone-neutral" data-audit="nachfuellen-nachmessung">
        <div className="nf-nm">
          <label className="v1-switch nf-schalter">
            <input type="checkbox" checked={nachmessungAuto && sensorenDa} disabled={!sensorenDa} onChange={(e) => setNachmessungAuto(e.target.checked)} />
            <span>
              <strong>Nachmessung automatisch eintragen</strong>
              <small>
                {sensorenDa
                  ? 'Nach der eingestellten Zeit liest der Fork EC und pH aus deinen Sensoren und hängt sie im Hintergrund an diesen Eintrag. Du musst nichts offen halten.'
                  : 'Dafür braucht der Fork EC- oder pH-Sensoren am Zelt. Trag die Werte unten von Hand ein.'}
              </small>
            </span>
          </label>
          {sensorenDa && (
            <label className="v1-switch nf-schalter nf-merken">
              <input type="checkbox" checked={merken} onChange={(e) => setMerken(e.target.checked)} />
              <span><small>als meinen Standard merken — an/aus und Minuten</small></span>
            </label>
          )}
          {nachmessungAuto && sensorenDa && (
            <div className="nf-nmz">
              <span>Nach</span>
              <input inputMode="numeric" aria-label="Minuten bis zur Nachmessung" value={nachmessungMinuten} onChange={(e) => setNachmessungMinuten(e.target.value)} />
              <span>Minuten</span>
              <span className="nf-chips" style={{ margin: 0 }}>
                {['15', '30', '60'].map((v) => <button key={v} type="button" aria-pressed={nachmessungMinuten === v} onClick={() => setNachmessungMinuten(v)}>{v} min</button>)}
              </span>
              <span className="info">
                {nachherHatWerte
                  ? 'Du trägst die Werte selbst ein — die Automatik entfällt.'
                  : nachmessungAm
                    ? <>Wird um <b>{uhrzeitText(nachmessungAm)}</b> eingetragen ({minutenZahl} min nach dem Zeitpunkt).{vergangen ? ' Das liegt schon in der Vergangenheit — der Fork holt den Wert dann aus dem Sensorverlauf.' : ''} Fällt der Fork zwischendurch aus, holt er es beim Start nach.</>
                    : 'Trag ganze Minuten von 1 bis 240 ein.'}
              </span>
            </div>
          )}
          <div className="nf-trenn">Oder selbst eintragen{nachmessungAuto && sensorenDa ? ' (hat Vorrang vor der Automatik)' : ''}</div>
          <div className="nf-zwei">
            <FeldMit label="EC nachher" einheit="mS/cm" hinweis="nach dem Durchmischen" wert={nachherHand.ec} onChange={(v) => setNachherHand({ ...nachherHand, ec: v })} />
            <FeldMit label="pH nachher" einheit="pH" wert={nachherHand.ph} onChange={(v) => setNachherHand({ ...nachherHand, ph: v })} />
          </div>
        </div>
      </article>

      {/* ---- Weitere Angaben */}
      <div className="nf-klapp">
        <Klappkachel titel="Weitere Angaben (optional)" zusammenfassung="Zeitpunkt, Notiz, Verbrauch" offen={vorbelegung != null}>
          <article className="v1-card tone-neutral">
            <div className="nf-feld">
              <label className="l" htmlFor="nf-zeit">Zeitpunkt</label>
              <div className="nf-dt">
                <input id="nf-zeit" type="datetime-local" value={zeitpunkt} max={toLocalInputValue()} onChange={(e) => setZeitpunkt(e.target.value)} />
                <span className="nf-chips" style={{ margin: 0 }}>
                  {[['Jetzt', 0], ['vor 30 min', 30], ['vor 1 Std.', 60], ['gestern', 1440]].map(([text, min]) => (
                    <button key={text} type="button" onClick={() => setZeitpunkt(toLocalInputValue(new Date(Date.now() - Number(min) * 60_000)))}>{text}</button>
                  ))}
                </span>
              </div>
              <small>Standard: jetzt. Die „vorher“-Werte (EC, pH) holt der Fork für diesen Zeitpunkt aus dem Sensorverlauf — so kannst du auch später eintragen.</small>
            </div>
            <div className="nf-feld">
              <label className="l" htmlFor="nf-notiz">Notiz</label>
              <input id="nf-notiz" value={notiz} onChange={(e) => setNotiz(e.target.value)} placeholder="z. B. Osmose-Tank war leer" />
            </div>
            <div className="nf-feld nf-schmal">
              <label className="l" htmlFor="nf-verbrauch">Verbrauch seit dem letzten Mal</label>
              <div className="nf-mit">
                <input id="nf-verbrauch" inputMode="decimal" value={verbrauch} onChange={(e) => setVerbrauch(e.target.value)} placeholder="z. B. 14,5" />
                <span className="e">L</span>
              </div>
              <small>nur zum Festhalten, wenn du ihn kennst</small>
            </div>
            <p className="st-hinweis">Den Verbrauch brauchst du nicht: Er steckt in den Messwerten, und das Volumen kommt aus dem Anlagevolumen. Wer ihn messen kann, trägt ihn hier in Litern ein; er dient der Statistik und als Gegenprobe zur eingefüllten Menge.</p>
          </article>
        </Klappkachel>
      </div>

      {fehler && <V1Alert title="Hinweis" message={fehler} tone="warn" />}
      <div className="nf-fuss">
        <span className="hin">{modus === 'wasser' ? 'Quelle wählen, Liter eintragen, fertig — mehr braucht ein Wasser-Addback nicht.' : 'Alles Übrige kommt aus dem Grow, dem Mischplan und den Live-Werten.'}</span>
        <button type="button" className="nf-btn" disabled={speichert || sensorLaedt} onClick={() => void speichern()} data-audit="nachfuellen-speichern">
          <span aria-hidden="true">✓</span> {speichert ? 'Trägt ein …' : 'Eintragen'}
        </button>
      </div>
    </div>
  )
}

/** Ein Zahlenfeld mit Einheit und Beschriftung. */
function FeldMit({ label, einheit, wert, onChange, hinweis, aria }: { label: string; einheit: string; wert: string; onChange: (v: string) => void; hinweis?: string; /** Eindeutiger Name für Bildschirmleser, wo die Beschriftung allein mehrdeutig wäre. */ aria?: string }) {
  return (
    <label className="nf-feld">
      <span className="l">{label}</span>
      <span className="nf-mit">
        <input inputMode="decimal" aria-label={aria} value={wert} onChange={(e) => onChange(e.target.value)} />
        <span className="e">{einheit}</span>
      </span>
      {hinweis && <small>{hinweis}</small>}
    </label>
  )
}
