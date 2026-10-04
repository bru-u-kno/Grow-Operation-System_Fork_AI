import { describe, expect, it } from 'vitest'
import { buildPanelUrl, judgeHost } from './mobile-link'

describe('Adresse fürs Handy', () => {
  it('setzt Herkunft und Panel-Pfad zusammen', () => {
    expect(buildPanelUrl('http://192.168.178.68:8123', '/app/local_grow_os'))
      .toBe('http://192.168.178.68:8123/app/local_grow_os')
  })

  it('wirft den mitkopierten Ingress-Pfad weg', () => {
    // Wer die Adresse aus der Adresszeile kopiert, bringt genau den Pfad mit,
    // der hier nicht gebraucht wird — samt Token, das morgen tot ist.
    expect(buildPanelUrl('http://ha.fritz.box:8123/api/hassio_ingress/abc123/dosierung', '/app/local_grow_os'))
      .toBe('http://ha.fritz.box:8123/app/local_grow_os')
  })

  it('ergänzt ein fehlendes Schema', () => {
    expect(buildPanelUrl('192.168.178.68:8123', '/app/local_grow_os'))
      .toBe('http://192.168.178.68:8123/app/local_grow_os')
  })

  it('behält https, wenn es dasteht', () => {
    expect(buildPanelUrl('https://abc.ui.nabu.casa/', '/app/a0d7b954_grow_os'))
      .toBe('https://abc.ui.nabu.casa/app/a0d7b954_grow_os')
  })

  it('gibt bei leerer Eingabe nichts zurück', () => {
    expect(buildPanelUrl('', '/app/local_grow_os')).toBeNull()
    expect(buildPanelUrl('http://ha:8123', '')).toBeNull()
  })
})

describe('Taugt die Adresse für ein anderes Gerät', () => {
  it('lehnt localhost ab', () => {
    // Auf dem Handy gescannt zeigt localhost auf das Handy selbst.
    const verdict = judgeHost('http://localhost:8123')

    expect(verdict.usable).toBe(false)
    expect(verdict.warning).toContain('nur auf diesem Rechner')
  })

  it('lehnt auch die Loopback-IP ab', () => {
    expect(judgeHost('http://127.0.0.1:5076').usable).toBe(false)
  })

  it('warnt bei .local, ohne es zu verbieten', () => {
    // Auf iPhones geht mDNS fast immer, auf Android oft nicht.
    const verdict = judgeHost('http://homeassistant.local:8123')

    expect(verdict.usable).toBe(true)
    expect(verdict.warning).toContain('Android')
  })

  it('lässt eine IP im Netzwerk kommentarlos durch', () => {
    expect(judgeHost('http://192.168.178.68:8123')).toEqual({ usable: true, warning: null })
  })

  it('lässt eine Nabu-Casa-Adresse kommentarlos durch', () => {
    expect(judgeHost('https://abc.ui.nabu.casa')).toEqual({ usable: true, warning: null })
  })
})
