import { useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { apiFetch } from '../api'
import { matchesSearchTerm } from '../search-fold'

export type SearchHit = { kind: string; title: string; subtitle: string | null; route: string }

/** A destination inside the app, searchable by name and by the words people actually use. */
export type SearchablePage = { label: string; route: string; keywords?: string }

/**
 * One box for everything. Pages are matched locally so it answers instantly; grows, tents,
 * strains, SOPs and knowledge come from the server.
 */
export function AppSearch({ pages, onNavigate }: { pages: SearchablePage[]; onNavigate?: () => void }) {
  const navigate = useNavigate()
  const [query, setQuery] = useState('')
  const [open, setOpen] = useState(false)
  const [remote, setRemote] = useState<SearchHit[]>([])
  const [active, setActive] = useState(0)
  const inputRef = useRef<HTMLInputElement>(null)
  const boxRef = useRef<HTMLDivElement>(null)

  const term = query.trim().toLowerCase()

  const pageHits = useMemo<SearchHit[]>(() => {
    if (term.length < 1) return []
    return pages
      .filter((page) => matchesSearchTerm(`${page.label} ${page.keywords ?? ''}`, term))
      .slice(0, 6)
      .map((page) => ({ kind: 'Seite', title: page.label, subtitle: null, route: page.route }))
  }, [pages, term])

  const hits = useMemo(() => [...pageHits, ...remote], [pageHits, remote])

  useEffect(() => {
    const controller = new AbortController()
    // Debounced so typing doesn't fire a request per keystroke; the clear also happens
    // inside the timer so no state is set straight from the effect body.
    const timer = window.setTimeout(async () => {
      if (term.length < 2) {
        setRemote([])
        return
      }
      try {
        const found = await apiFetch<SearchHit[]>(`/api/search?q=${encodeURIComponent(term)}`, { signal: controller.signal })
        if (!controller.signal.aborted) setRemote(found)
      } catch {
        // Pages still match locally, so the box stays useful without the backend.
      }
    }, 180)
    return () => {
      controller.abort()
      window.clearTimeout(timer)
    }
  }, [term])

  // Ctrl/Cmd+K from anywhere, Escape to leave.
  useEffect(() => {
    function onKey(event: KeyboardEvent) {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault()
        setOpen(true)
        inputRef.current?.focus()
        inputRef.current?.select()
      }
      if (event.key === 'Escape') setOpen(false)
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  useEffect(() => {
    function onClick(event: MouseEvent) {
      if (boxRef.current && !boxRef.current.contains(event.target as Node)) setOpen(false)
    }
    document.addEventListener('mousedown', onClick)
    return () => document.removeEventListener('mousedown', onClick)
  }, [])

  function go(hit: SearchHit) {
    navigate(hit.route)
    setQuery('')
    setOpen(false)
    // Am Telefon liegt die Suche IM „Mehr"-Menue. Ohne diesen Rueckruf blieb es
    // nach dem Treffer offen und verdeckte genau die Seite, auf die man gerade
    // gesprungen ist.
    onNavigate?.()
  }

  // Guards against a stale index when the result list shrinks under the cursor.
  const activeIndex = Math.min(active, Math.max(hits.length - 1, 0))

  // Pfeiltasten schoben die Liste nicht mit: ab Treffer 8 stand die Auswahl
  // ausserhalb des sichtbaren Bereichs, man tippte ins Blinde.
  useEffect(() => {
    const kasten = boxRef.current?.querySelector('.app-search-results')
    kasten?.querySelectorAll('.app-search-hit')[activeIndex]?.scrollIntoView({ block: 'nearest' })
  })

  function onKeyDown(event: React.KeyboardEvent<HTMLInputElement>) {
    if (event.key === 'ArrowDown') {
      event.preventDefault()
      setActive(Math.min(activeIndex + 1, hits.length - 1))
    } else if (event.key === 'ArrowUp') {
      event.preventDefault()
      setActive(Math.max(activeIndex - 1, 0))
    } else if (event.key === 'Enter' && hits[activeIndex]) {
      event.preventDefault()
      go(hits[activeIndex])
    }
  }

  const showResults = open && term.length >= 1

  return (
    <div className="app-search" ref={boxRef} data-audit="app-search">
      <input
        ref={inputRef}
        type="search"
        className="app-search-input"
        value={query}
        placeholder={zeigtTastenkuerzel() ? 'Suchen … (Strg+K)' : 'Suchen …'}
        aria-label="In Cultivate Control suchen"
        onChange={(event) => { setQuery(event.target.value); setActive(0); setOpen(true) }}
        onFocus={() => setOpen(true)}
        onKeyDown={onKeyDown}
      />

      {showResults && (
        <div className="app-search-results" role="listbox">
          {hits.length === 0 ? (
            <div className="app-search-empty">Nichts gefunden.</div>
          ) : (
            hits.map((hit, index) => (
              <button
                key={`${hit.kind}-${hit.route}-${hit.title}`}
                type="button"
                role="option"
                aria-selected={index === activeIndex}
                className={`app-search-hit${index === activeIndex ? ' active' : ''}`}
                onMouseEnter={() => setActive(index)}
                onClick={() => go(hit)}
              >
                <span className="k">{hit.kind}</span>
                <span className="t">{hit.title}</span>
                {hit.subtitle && <span className="s">{hit.subtitle}</span>}
              </button>
            ))
          )}
        </div>
      )}
    </div>
  )
}

/**
 * Hat dieses Geraet ueberhaupt eine Strg-Taste?
 *
 * Im Platzhalter stand „Suchen …  (Strg+K)" — auch auf dem Telefon, wo es
 * weder Strg noch K gibt und der Hinweis nur Platz frisst. `pointer: coarse`
 * beschreibt genau das: ein Zeiger, der ein Finger ist.
 */
function zeigtTastenkuerzel(): boolean {
  if (typeof window === 'undefined' || !window.matchMedia) return true
  return !window.matchMedia('(pointer: coarse)').matches
}
