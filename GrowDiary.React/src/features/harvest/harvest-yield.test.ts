import { describe, expect, it } from 'vitest'
import { summariseYield } from './harvest-yield'

describe('summariseYield', () => {
  it('rechnet die Trockenausbeute aus', () => {
    expect(summariseYield('1000', '220')?.text).toBe('Trockenausbeute 22 % (220 g von 1.000 g)')
  })

  it('versteht das Komma als Dezimaltrenner', () => {
    // Wer auf einer deutschen Tastatur 21,5 tippt, meint 21.5 — nicht 215.
    expect(summariseYield('100,0', '21,5')?.text, 'englischer Dezimalpunkt im Satz').toBe('Trockenausbeute 21,5 % (21,5 g von 100 g)')
  })

  it('sagt es, wenn die Gewichte vertauscht sind', () => {
    expect(summariseYield('200', '900')?.text).toBe('Trockengewicht über Frischgewicht — vermutlich vertauscht.')
  })

  it('schweigt, solange nur eines der beiden Felder gefüllt ist', () => {
    expect(summariseYield('1000', '')).toBeNull()
    expect(summariseYield('', '220')).toBeNull()
  })

  it('schweigt bei Null und bei Unsinn', () => {
    expect(summariseYield('0', '220')).toBeNull()
    expect(summariseYield('abc', '220')).toBeNull()
  })
})
