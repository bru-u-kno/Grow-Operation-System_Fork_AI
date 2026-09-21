import { describe, expect, it } from 'vitest'
import { geraeteName } from './plan-kette'

describe('geraeteName', () => {
  it('macht aus dem Notify-Dienst einen lesbaren Gerätenamen', () => {
    expect(geraeteName('notify.mobile_app_bruno_smartphone_1')).toBe('Bruno Smartphone 1')
    expect(geraeteName('notify.telegram')).toBe('Telegram')
  })
})
