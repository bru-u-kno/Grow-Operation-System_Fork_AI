import type { V1Option } from '../../components/V1Select'
import type { HomeAssistantEntity } from '../../types'
import { haWert } from '../../utils'
import { rangiere } from './entitaets-passung'
import type { Kriterium, Vorbereitet } from './entitaets-passung'

export const GRUPPE_PASST = 'Passt zu dieser Rolle'
export const GRUPPE_ANDERE = 'Alle anderen'
export const HINWEIS_NICHTS_PASSENDES =
  'Keine passende Entität gefunden — unter „Alle anderen“ suchen oder im Suchfeld tippen.'

/**
 * Fork AI (forkai.236): Die Auswahl-Einträge aus Home Assistant, in zwei Gruppen vorsortiert.
 * EINE Stelle für alle Auswahlen, die Entitäten anbieten (Rollen der Steuerungen, Messgrößen).
 * Sortiert nur — gewählt wird immer vom Nutzer.
 */
export function entitaetsOptionen(
  vorbereitet: readonly Vorbereitet[],
  kriterium: Kriterium,
  opts: { passtGruppe?: string; hinweis?: (entity: HomeAssistantEntity) => string } = {},
): { optionen: V1Option[]; leerHinweis: string | null } {
  const passtGruppe = opts.passtGruppe ?? GRUPPE_PASST
  const { passt, andere } = rangiere(vorbereitet, kriterium)
  const eintrag = (entity: HomeAssistantEntity, gruppe: string): V1Option => ({
    wert: entity.entityId,
    text: entity.friendlyName ?? entity.entityId,
    hinweis: opts.hinweis ? opts.hinweis(entity) : `${entity.entityId}${entity.state ? ` · ${haWert(entity.state, entity.unitOfMeasurement)}` : ''}`,
    gruppe,
  })
  return {
    optionen: [
      ...passt.map((e) => eintrag(e, passtGruppe)),
      ...andere.map((e) => eintrag(e, GRUPPE_ANDERE)),
    ],
    leerHinweis: passt.length === 0 && andere.length > 0 ? HINWEIS_NICHTS_PASSENDES : null,
  }
}
