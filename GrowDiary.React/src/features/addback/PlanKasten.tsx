import type { Ankerphase } from '../../types'
import { zahl } from '../vorgang/ablauf-rechnung'
import { alterZeile, ecLage, ecZielAnzeige, phAbstand, phZielText, planChips, zielAbstandEc } from './nachfuell-rechnung'
import './plan-kasten.css'

/**
 * „Dein Plan heute" — der Kasten mit Plan, Phasentag, Zielen und dem Abstand des Tanks zum Ziel.
 *
 * Eine Komponente für Addback und Wasserwechsel (10.10.2026: eine Wahrheit je Zahl; 11.10.2026: mehrfach genutzte
 * Bausteine an einer zentralen Stelle). Sie rechnet nichts selbst: Tag und Woche kommen vom Phasenanker des Grows,
 * Planname und Ziele aus dem Backend; die Texte stehen in `nachfuell-rechnung.ts`. Der pH des Tanks wird nie
 * vorausberechnet — nur Ziel und der gemessene Wert.
 *
 * Eine Zeile je Ziel: links Name und Hinweis, rechts die große Zahl und darunter der Abstand des Tanks.
 */
export type PlanKastenProps = {
  /** Prüfkennung für die E2E-Mappe — jede Seite behält ihre eigene. */
  audit?: string
  programmName: string
  anker: { phase: Ankerphase; tagInPhase: number; wocheInPhase: number } | null | undefined
  /** Gesamtziel (Dünger + Wasser) und Dünger-Ziel; ohne Gesamtziel steht „Dünger-Ziel" da und es gibt keinen Abstand. */
  ecZielGesamt: number | null
  ecZielDuenger: number | null
  wasserEc: number | null
  phMin: number | null
  phMax: number | null
  /** Der Tank: EC und pH, dazu woher („jetzt", „vorher", „nachher") und wie alt der Sensorwert ist (`null` bei Handwerten). */
  tank: { ec: number | null; ph: number | null; etikett: string; alterMinuten: number | null } | null
  /** Optional eine dritte Zeile „EC mit deinen Mengen" — die Rechnung des Mischplans, keine Messung. */
  erwartung?: { ec: number | null } | null
}

export function PlanKasten({ audit = 'plan-kasten', programmName, anker, ecZielGesamt, ecZielDuenger, wasserEc, phMin, phMax, tank, erwartung = null }: PlanKastenProps) {
  const zeit = planChips(anker)
  const ziel = ecZielAnzeige(ecZielGesamt, ecZielDuenger)
  const vorspann = `Tank ${tank?.etikett ?? ''}`
  const ecAbstand = ziel?.gesamt ? zielAbstandEc(tank?.ec ?? null, ziel.wert, vorspann) : null
  const phText = phZielText(phMin, phMax)
  const phTankText = phAbstand(tank?.ph ?? null, phMin, phMax, vorspann)
    ?? (tank?.ph != null && phText != null ? `${vorspann} ${zahl(tank.ph, 2)}` : null)
  const erwartungLage = ziel?.gesamt ? ecLage(erwartung?.ec ?? null, ziel.wert) : null
  const alter = tank && (tank.ec != null || tank.ph != null) ? alterZeile(tank.alterMinuten) : null
  return (
    <div className="plan-kasten" data-audit={audit}>
      <div className="k">Dein Plan heute</div>
      {zeit && <div className="kz"><span>{zeit.tag}</span><span>{zeit.woche}</span></div>}
      <div className="nm">Plan: <b>{programmName}</b></div>
      <div className="zeile">
        <span className="n">{ziel?.etikett ?? 'EC-Ziel'}</span>
        <span className="w">{ziel ? <>{zahl(ziel.wert, 2)}<small>mS/cm</small></> : '–'}</span>
        <span className="h">{ziel?.gesamt && ecZielDuenger != null && wasserEc != null ? `Dünger ${zahl(ecZielDuenger, 2)} + Wasser ${zahl(wasserEc, 2)}` : ''}</span>
        {ecAbstand && <span className="a">{ecAbstand}</span>}
      </div>
      <div className="zeile">
        <span className="n">pH-Ziel</span>
        <span className="w">{phText ?? '–'}</span>
        <span className="h">nach dem Anmischen messen</span>
        {phTankText && <span className="a">{phTankText}</span>}
      </div>
      {erwartung && (
        <div className="zeile">
          <span className="n">EC mit deinen Mengen</span>
          <span className="w">{erwartung.ec != null ? <>≈ {zahl(erwartung.ec, 2)}<small>mS/cm</small></> : '–'}</span>
          <span className="h">Rechnung, keine Messung</span>
          {erwartungLage && <span className={erwartungLage === 'im Ziel' ? 'a gut' : 'a'}>{erwartungLage === 'im Ziel' ? '= Ziel laut Plan ✓' : erwartungLage}</span>}
        </div>
      )}
      {alter && <div className={alter.alt ? 'alter alt' : 'alter'}>{alter.text}</div>}
    </div>
  )
}
