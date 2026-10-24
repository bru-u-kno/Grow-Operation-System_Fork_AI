import { resolveUrl } from './base'
import type { ApiError } from './types'

export class ApiRequestError extends Error {
  status: number
  payload: ApiError | null

  constructor(status: number, payload: ApiError | null, fallbackMessage: string) {
    super(lesbareMeldung(payload) ?? fallbackMessage)
    this.status = status
    this.payload = payload
  }
}

/**
 * Die Meldung, die dem Nutzer wirklich weiterhilft.
 *
 * <b>Der Anlass (31.08.2026).</b> Beim Absenden eines Teilwechsels ohne Menge
 * stand auf dem Schirm „Eingaben konnten nicht validiert werden." — der
 * Grund lag daneben, ungelesen: das Backend schickt ihn in
 * <code>fieldErrors</code>, gelesen wurde nur <code>message</code>. Dieselbe
 * Stelle hat schon einmal eine englische Meldung durchgereicht.
 *
 * <b>Die Regel.</b> Ist <code>message</code> die allgemeine Sammelmeldung und
 * steht darunter genau eine Feldmeldung, gewinnt die Feldmeldung — sie sagt,
 * was zu tun ist. Bei mehreren werden sie aneinandergehängt, damit keine
 * verschwindet.
 */
function lesbareMeldung(payload: ApiError | null): string | undefined {
  if (payload == null) return undefined

  const felder = Object.values(payload.fieldErrors ?? {}).flat().filter((text) => text.trim().length > 0)
  if (felder.length === 0) return payload.message

  // Nur die Sammelmeldung wird ersetzt. Eine eigene Meldung des Endpunkts ist
  // bewusst gewählt und darf nicht von einer Feldmeldung verdrängt werden.
  const istSammelmeldung = payload.code === 'validation_failed'
  if (!istSammelmeldung) return payload.message

  return felder.join(' ')
}

async function parseResponse<T>(response: Response): Promise<T> {
  if (response.status === 204) {
    return undefined as T
  }

  const text = await response.text()
  if (!text) {
    return undefined as T
  }

  return JSON.parse(text) as T
}

export async function apiFetch<T>(path: string, init?: RequestInit): Promise<T> {
  const headers = new Headers(init?.headers)
  if (!(init?.body instanceof FormData) && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json')
  }

  const response = await fetch(resolveUrl(path), {
    ...init,
    headers,
  })

  if (!response.ok) {
    let payload: ApiError | null

    try {
      payload = await parseResponse<ApiError>(response)
    } catch {
      payload = null
    }

    throw new ApiRequestError(response.status, payload, `Anfrage fehlgeschlagen (Status ${response.status})`)
  }

  return parseResponse<T>(response)
}

/**
 * Fehler → Satz fürs UI. Ein Helfer statt (Stand des Audits) neun wörtlich
 * gleicher Kopien quer durch die Seiten — die wären bei der nächsten Änderung
 * an ApiRequestError still auseinandergedriftet.
 */
export function formatApiError(caught: unknown, fallback: string): string {
  return caught instanceof ApiRequestError
    ? caught.message
    : caught instanceof Error
      ? caught.message
      : fallback
}
