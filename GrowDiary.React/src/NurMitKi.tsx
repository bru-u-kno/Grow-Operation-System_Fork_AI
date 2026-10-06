/* src/NurMitKi.tsx — Fork AI (A-011)
   Eine Seite, die es nur bei eingeschalteten KI-Funktionen gibt. Bei „aus"
   geht es zur Startseite — auch über ein altes Lesezeichen (/ki, /berater). */

import type { ReactNode } from 'react'
import { Navigate } from 'react-router-dom'
import { useKiAktiv } from './ki-aktiv'

export function NurMitKi({ children }: { children: ReactNode }) {
  const { aktiv, geladen } = useKiAktiv()
  if (!geladen) return null
  if (!aktiv) return <Navigate to="/" replace />
  return <>{children}</>
}
