# Geräte & Entitäten — alles, was der Fork an Home Assistant benutzt

> Fork AI (forkai.22), Etappe 1 und 2. Die Seite zeigt; gepflegt wird noch an
> den bisherigen Stellen. Die Reiter je Gerät folgen in der nächsten Etappe.

## Wo in der App

`/geraete` — Menü **Betrieb → Geräte & Entitäten**, direkt vor
„Sensoren & Wartung". Eine Zeile je Gerät; Controller tragen ihre Ports
eingerückt darunter. Ein Tipp klappt die Entitäten des Geräts auf, jede mit
einer Marke dahinter, wofür sie benutzt wird.

## Was es tut

Sie beantwortet eine Frage, die vorher fünf Seiten brauchte: **welche Entität
benutzt der Fork wofür, und zu welchem Gerät gehört sie?** Eingesammelt wird
aus sechs Quellen — Messgrößen des Zelts, Zelt-Technik, Inventar,
Dosierpumpen, Steuerungs-Rollen und Stromzähler.

Die Einheit ist das **Gerät**, nicht die Entität. Der Bluelab Guardian ist eine
Zeile mit pH, EC und Wassertemperatur darin — nicht drei. Ein Controller ist
eine Zeile, und was in seinen Ports steckt, hängt als eigenes Gerät darunter.

## Die Zahlen und woher sie kommen

- **Geräte** und **Entitäten**: Zählung aus `GET /api/geraete`.
- **vermutet**: Geräte, deren Zuschnitt weder der Nutzer noch Home Assistant
  bestätigt hat — der Name der Entität war die einzige Spur. Steht die Zahl
  über null, lohnt ein Blick; in einer eingerichteten Anlage ist sie klein.
- Die Herkunft kommt aus `config/entity_registry/list` und
  `config/device_registry/list` über den WebSocket. Reihenfolge der Wahrheit:
  Zuordnung des Nutzers, dann `device_id`/`via_device_id` von Home Assistant,
  dann die Namensvermutung.

## Was es bewusst NICHT tut

- **Nicht ändern.** Diese Etappe schreibt nichts. Wer eine Entität austauschen
  will, tut es weiter dort, wo sie heute steht; die Marke sagt, wo das ist.
- **Nicht raten, was am Port hängt.** Home Assistant kennt den Port, aber nicht
  das Gerät darin. „Port 5" steht als Steckstelle da; dass dort das CO₂-Ventil
  hängt, weiß nur der Nutzer.
- **Keine Livewerte.** Die Liste ist eine Landkarte, kein Dashboard.
- **Keine Wartung.** Kalibrierfristen und Verschleiß bleiben vorerst unter
  „Sensoren & Wartung"; sie ziehen in der letzten Etappe als Reiter um.

## Im Code

- `GrowDiary.Web/Models/Geraet.cs` — Gerät, Entität, Verwendung, MAC-Notnagel
- `GrowDiary.Web/Services/GeraeteUebersichtService.cs` — Quellen einsammeln,
  zu Geräten zusammenfassen
- `GrowDiary.Web/Services/HomeAssistantRegistryService.cs` — Register über
  `/api/websocket`
- `GrowDiary.Web/Infrastructure/GeraeteRepository.cs` — `ForkGeraete`,
  `ForkGeraetEntitaeten` (Korrekturen des Nutzers, ab Etappe 3)
- `GrowDiary.Web/Api/Controllers/GeraeteApiController.cs` — `GET /api/geraete`
- `GrowDiary.React/src/pages/GeraetePage.tsx`, `pages/geraete.css`
