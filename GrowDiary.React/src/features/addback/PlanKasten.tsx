import type { Ankerphase } from '../../types'
import { zahl } from '../vorgang/ablauf-rechnung'
import { alterZeile, ecZielAnzeige, phLage, phZielText, planChips, zielAbstandEc } from './nachfuell-rechnung'
import './plan-kasten.css'

/**
 * „Dein Plan heute" — der Kasten mit Plan, Phasentag, Zielen und dem Abstand des Tanks zum Ziel.
 *
 * Eine Komponente für Addback und Wasserwechsel (Bru, 10.10.2026: eine Wahrheit je Zahl). Sie rechnet nichts selbst:
 * Tag und Woche kommen vom Phasenanker des Grows, Planname und Ziele aus dem Backend; die Texte stehen in
 * `nachfuell-rechnung.ts`. Der pH des Tanks wird nie vorausberechnet — nur Ziel und der gemessene Wert.
 */
export type PlanKastenProps = {
  programmName: string
  anker: { phase: Ankerphase; tagInPhase: number; wocheInPhase: number } | null | undefined
  /** Gesamtziel (Dünger + Wasser) und Dünger-Ziel; ohne Gesamtziel steht „Dünger-Ziel" da und es gibt keinen Abstand. */
  ecZielGesamt: number | null
  ecZielDuenger: number | null
  wasserEc: number | null
  phMin: number | null
  phMax: number | null
  /** Der Tank: EC und pH, dazu woher („jetzt", „vorher") und wie alt der Sensorwert ist (`null` bei Handwerten). */
  tank: { ec: number | null; ph: number | null; etikett: string; alterMinuten: number | null } | null
}

export function PlanKasten({ programmName, anker, ecZielGesamt, ecZielDuenger, wasserEc, phMin, phMax, tank }: PlanKastenProps) {
  const zeit = planChips(anker)
  const ziel = ecZielAnzeige(ecZielGesamt, ecZielDuenger)
  const phTank = tank?.ph ?? null
  const lage = phLage(phTank, phMin, phMax)
  const abstand = ziel?.gesamt ? zielAbstandEc(tank?.ec ?? null, ziel.wert, `${tank?.etikett === 'jetzt' ? 'Jetzt im Tank' : `Tank ${tank?.etikett}`}`) : null
  const alter = tank && (tank.ec != null || tank.ph != null) ? alterZeile(tank.alterMinuten) : null
  return (
    <div className="plan-kasten" data-audit="plan-kasten">
      <div className="k">Dein Plan heute</div>
      {zeit && <div className="kz"><span>{zeit.tag}</span><span>{zeit.woche}</span></div>}
      <div className="nm">Plan: <b>{programmName}</b></div>
      <div className="ziel">
        <div>
          <span className="k">{ziel?.etikett ?? 'EC-Ziel'}</span>
          <b>{ziel ? `${zahl(ziel.wert, 2)} mS/cm` : '–'}</b>
          {ziel?.gesamt && ecZielDuenger != null && wasserEc != null && <small>Dünger {zahl(ecZielDuenger, 2)} + Wasser {zahl(wasserEc, 2)}</small>}
        </div>
        <div>
          <span className="k">pH-Ziel</span>
          <b>{phZielText(phMin, phMax) ?? '–'}</b>
          {phTank != null && <small>Tank {tank!.etikett} {zahl(phTank, 2)}{lage ? ` · ${lage}` : ''}</small>}
        </div>
      </div>
      {abstand && <div className="abstand">{abstand}</div>}
      {alter && <div className={alter.alt ? 'alter alt' : 'alter'}>{alter.text}</div>}
    </div>
  )
}
