import { describe, expect, it } from 'vitest'
import { VORGESCHLAGENER_PANEL, waehleKarte, yamlBlock } from './titelleiste-logik'

describe('Hilfe „Ohne HA-Titelleiste“', () => {
  it('zeigt die Anleitung, wenn die Leiste da ist und es kein Panel gibt', () => {
    expect(waehleKarte({ inHomeAssistant: true, kopfleiste: true, panel: null })).toBe('einrichten')
    expect(waehleKarte({ inHomeAssistant: true, kopfleiste: true, panel: undefined })).toBe('einrichten')
  })

  it('sagt, wo das Panel ist, wenn es eins gibt, die App aber über den normalen Eintrag lief', () => {
    expect(waehleKarte({ inHomeAssistant: true, kopfleiste: true, panel: 'growos' })).toBe('oeffnen')
  })

  it('zeigt nur eine Notiz, wenn alles eingerichtet ist und die Leiste weg ist', () => {
    expect(waehleKarte({ inHomeAssistant: true, kopfleiste: false, panel: 'growos' })).toBe('fertig')
  })

  it('zeigt nichts ohne Leiste und ohne Panel, und nichts außerhalb von Home Assistant', () => {
    expect(waehleKarte({ inHomeAssistant: true, kopfleiste: false, panel: null })).toBe('keine')
    expect(waehleKarte({ inHomeAssistant: false, kopfleiste: true, panel: null })).toBe('keine')
    expect(waehleKarte({ inHomeAssistant: false, kopfleiste: false, panel: 'growos' })).toBe('keine')
  })

  it('setzt die Kennung dieser Installation in den YAML-Block ein', () => {
    const block = yamlBlock('00d580e3_cultivate_control')
    expect(block.split('\n')).toEqual([
      'ingress:',
      `  ${VORGESCHLAGENER_PANEL}:`,
      '    work_mode: hassio',
      '    url: 00d580e3_cultivate_control',
      '    ui_mode: normal',
      '    title: Cultivate Control',
      '    icon: mdi:sprout',
    ])
  })

  it('nimmt einen eigenen Panel-Namen, ohne die Kennung zu verändern', () => {
    const block = yamlBlock('abc12345_cultivate_control', 'growos')
    expect(block).toContain('  growos:\n')
    expect(block).toContain('url: abc12345_cultivate_control')
  })
})
