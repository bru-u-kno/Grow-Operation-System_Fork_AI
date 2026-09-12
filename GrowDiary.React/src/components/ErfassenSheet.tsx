/* src/components/ErfassenSheet.tsx — Fork AI
   Ein Weg für alle Eingaben, statt Reiter neben Knöpfen.

   Vorher stand „Messen" als Reiter in der Navigation UND „Messung erfassen" als
   Knopf auf der Live-Seite; dasselbe bei Addback. Zwei Wege zum selben
   Formular kosten Platz in einer Leiste, in der Platz das Knappe ist — und wer
   den Reiter nimmt, landet an derselben Stelle wie über den Knopf. Deshalb
   liegen die Eintragswege jetzt hinter EINEM Plus in der Titelzeile, auf jeder
   Seite erreichbar und nicht nur auf der Startseite.

   forkai.35: Der Rahmen (Schleier, Griff, Escape, Fokus) steckt jetzt in
   `V1Sheet` — hier bleiben nur die Ziele. */

import { useNavigate } from 'react-router-dom'
import { V1Sheet } from './V1Sheet'
import { erfassenZiele } from './erfassen-ziele'

type Props = {
  open: boolean
  onClose: () => void
}

export function ErfassenSheet({ open, onClose }: Props) {
  const navigate = useNavigate()

  return (
    <V1Sheet open={open} onClose={onClose} title="Erfassen">
      {erfassenZiele.map((ziel) => (
        <button
          key={ziel.to}
          type="button"
          className="forkai-sheet-item"
          onClick={() => {
            onClose()
            navigate(ziel.to)
          }}
        >
          <span className="forkai-sheet-icon" aria-hidden="true">{ziel.icon}</span>
          <span className="forkai-sheet-text">
            <strong>{ziel.label}</strong>
            <span>{ziel.hint}</span>
          </span>
        </button>
      ))}
    </V1Sheet>
  )
}
