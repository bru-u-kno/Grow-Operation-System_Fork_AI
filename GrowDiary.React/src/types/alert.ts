export type AlertRuleDto = {
  metricKey: string
  minValue: number | null
  maxValue: number | null
  notifyService: string
  enabled: boolean
  cooldownMinutes: number
  /** 'Fest' = eingetragene Zahlen, 'Plan' = Grenzen aus dem Wochenplan. */
  quelle: 'Fest' | 'Plan'
  /** Nur bei 'Plan': wie weit der Wert über das Zielband hinausdarf. */
  toleranz: number | null
}

export type TentAlertRulesDto = {
  tentId: number
  rules: AlertRuleDto[]
}
