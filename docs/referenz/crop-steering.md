# Crop Steering: Wassertemperatur, Nachtabsenkung, Kühler

> Im RDWC gibt es keine Trockenphase — gesteuert wird über die Wassertemperatur:
> je Blütewoche ein Grad kältere Nacht, und seit beta.52 schaltet Cultivate Control den
> Kühler dafür selbst.

## Wo in der App

| Was | Wo |
|---|---|
| Menü „Betrieb" → „Crop Steering" | `/cropsteering` |
| Karte „Crop Steering" am Grow (nur Stand, Knopf „Einstellen") | `/grows/<id>` → `/cropsteering?growId=<id>` |
| Karte „Kühler · Crop Steering" auf der Live-Seite (nur bei aktiver Steuerung) | Live-Seite, `data-audit="live-chiller"` |
| Tag-/Nachtwerte je Phase (`waterTempDayC`, `waterTempNightC`) | `/sollwerte` |
| Verweis darauf im Untertitel | `/regeln` |
| Schnittstelle | `GET`/`PUT /api/grows/{growId}/night-ramp` |

## Was es tut

**Die Rampe.** Ab dem Flip rechnet `NachtabsenkungService.Rechnen` je Blütewoche
einen Nachtwert: Start ist der Blüte-Nachtwert des Sollwert-Profils, danach je
Woche ein Grad tiefer bis zum Boden. Der Tagwert bleibt der des Profils. Die
Tabelle auf der Seite zeigt alle Wochen, die laufende markiert.

**Zwei Wege, den Wert in die Anlage zu bringen.**

1. *Sollwert schreiben* (`NachtabsenkungWriter`): hängt an der Lichtflanke, also
   zweimal am Tag, an ein `climate.…`-Thermostat (`set_temperature`) oder ein
   `number.…`/`input_number.…`-Feld (`set_value`). Geregelt wird in HA.
2. *Steckdose schalten* (`KuehlerWorker` + `KuehlerService`): für Geräte ohne
   Sollwert-Eingang — ein Hailea hat Thermostat, aber keinen Bus. Er wird selbst
   fest tief eingestellt und nur noch mit Strom versorgt oder nicht.
   Zwei-Punkt-Regelung mit Totband, Mindestlauf, Mindestpause und Höchstalter
   des Messwerts, geprüft jede Minute.

**Die Richtung des Fehlerfalls** ist der Kern der zweiten Lösung: bleibt die
Steckdose hängen oder stirbt das Add-on, kühlt das Gerät auf seinen *eigenen*
Thermostat und stoppt dort. Deshalb steht die tiefe Geräteeinstellung auf der
Seite als **Bedingung** und nicht als Empfehlung.

**Die Live-Karte** trägt den vollen Grund im Klartext — die Sätze stehen als
Formatzeichenketten in `KuehlerService.Entscheiden`, z. B. „18,9 °C, der Kühler
steht und liefe erst ab 20,4 °C an (Nachtwert 20,0 °C)". Sonst sieht ein
stehender Kühler wie ein Defekt aus, während nur die Mindestpause läuft.

## Der Einstieg: die Kette führt hin

Die Karte „Läuft es gerade?" zeigt je Kette (Sollwert schreiben · Kühler
schalten) jedes Glied mit Haken oder Punkt. **Jedes gerissene Glied trägt
einen Knopf, der direkt zur Reparaturstelle führt** — zum Schalter, zur
Untergrenze, zum Zielgerät, zur Steckdose, zu den Sollwert-Profilen, zum
Grow (Flip eintragen) oder zur HA-Einrichtung. Anlass war ein Nutzer, der
vor der Seite stand und nicht wusste, WIE er die Steuerung anschaltet: die
Kette sagte nur, was fehlt, die Stellschrauben lagen verstreut.

Die Zuordnung hängt an maschinenlesbaren Schlüsseln
(`KettenSchluessel.cs` ↔ `ketten-aktionen.ts`), nicht an den deutschen
Titeln; `ketten-aktionen.node.test.ts` zählt, dass jeder Schlüssel eine
Aktion hat. Liegt die Untergrenze über dem Blüte-Nachtwert, steht die
Warnung zusätzlich direkt am Untergrenze-Feld — dort, wo repariert wird.

## Die Zahlen und woher sie kommen

| Zahl | Wert | Woher |
|---|---|---|
| Schritt der Rampe | 1,0 °C je Blütewoche | `NachtabsenkungService.SchrittProWocheC`; Methode „Cold Morning Routine" nach SKX |
| Harte Untergrenze | 12 °C | `NachtabsenkungService.AbsoluteUntergrenzeC` |
| Startwert der Rampe | Blüte-Nachtwert des Profils, im Standard 18 °C | `knowledge-defaults/setpoints/rdwc-default.json`, `Flower.waterTempNightC` |
| Boden ohne eigene Angabe | Finish-Nachtwert des Profils, im Standard 16 °C | dieselbe Datei, `Finish.waterTempNightC` |
| Tagwert | Blüte-Tagwert, im Standard 20 °C | dieselbe Datei, `Flower.waterTempDayC` |
| Länge des Plans | höchstens 14 Wochen, plus eine Zeile nach Erreichen des Bodens | `NachtabsenkungService.MaxWochen` |
| Totband | ±0,4 °C, erlaubt 0,1–3,0 | `KuehlerService.StandardHystereseC`; Grenzen in `NightRampApiController.Put` |
| Mindestlaufzeit | 5 min, erlaubt 1–60 | `KuehlerService.StandardMindestlaufMinuten` |
| Mindestpause | 5 min, erlaubt 1–60 | `KuehlerService.StandardMindestpauseMinuten`; Faustregel „Herstellerrichtwert", keine Einzelquelle im Repo |
| Messwert höchstens alt | 10 min, erlaubt 1–120 | `KuehlerService.StandardHoechstalterMinuten` |
| Takt des Reglers | 1 Minute, Start 50 s versetzt | `KuehlerWorker.Takt`, `ExecuteAsync` |
| Eigene Einstellung am Kühler | Faustregel etwa 15 °C, knapp unter die Untergrenze | Bedingungstext in `CropSteeringPage.tsx` |
| Arbeitsbereich Wasser | 17–22 °C, kritisch unter 14 / über 24 | `Wasserband`; SOP-RDWC-CAN-N1 |
| Ziel | 19–20 °C; „unter 18 °C wird die Nährstoffaufnahme gehemmt" | `knowledge-defaults/guidance/water-temperature-band.json`, Quelle SOP-RDWC-CAN-N1 |

## Was es bewusst NICHT tut

- **Kein Sollwert heißt nicht „dann eben aus".** Ein Autoflower hat keinen Flip
  und damit keinen Nachtwert. Der Kühler bleibt, wie er ist — ihn abzuschalten
  wäre ein steigendes Reservoir (`KuehlerService.Entscheiden`).
- **Ohne Flipdatum wird die Blütewoche nicht geschätzt** — eine geratene Woche
  verstellt eine echte Kühlung (`NachtabsenkungService.Bluetewoche`).
- **Standardmäßig aus.** `Tent.ChillerControlEnabled` ist `false`: etwas, das
  einen Kompressor taktet, schaltet sich nicht durch ein Update selbst ein.
- **Die Ausschaltschwelle fällt nie unter die harten 12 °C**, auch bei großem
  Totband — sonst widerspräche die Klasse ihrer eigenen Sperre.
- **Ein leergeräumtes Feld wird nicht auf das Minimum gedeckelt.** Unter dem
  erlaubten Bereich gilt der Standard (`NightRampApiController.ZahlOderStandard`).
- **Kein Protokolleintrag, wenn nichts geschaltet wird.** Eine Zeile je Minute
  und Zelt läse niemand; der Grund steht in der Oberfläche.
- **Die Rampe erfindet keinen Boden.** Sie fährt gleitend dorthin, wo die
  Profile ohnehin springen — den Finish-Nachtwert.
- **Keine KI.** Ein Zwei-Punkt-Regler mit Sperren, nichts weiter.

## Im Code

| Aufgabe | Datei |
|---|---|
| Rampe rechnen (`Rechnen`, `Bluetewoche`) | `GrowDiary.Web/Services/NachtabsenkungService.cs` |
| Sollwert an HA schreiben (`SchreibenAsync`, `PlanFuer`) | `GrowDiary.Web/Services/NachtabsenkungWriter.cs` |
| Regelentscheidung (`Entscheiden`, `SollJetzt`, `IstAbsichtlichAus`) | `GrowDiary.Web/Services/KuehlerService.cs` |
| Minutentakt, schalten, Lage zusammensuchen (`LageLesen`, `LetzterBefehl`) | `GrowDiary.Web/Services/KuehlerWorker.cs` |
| Arbeitsbereich an einer Stelle (`UntergrenzeC`, `RampenBodenC`, `Begruendung`) | `GrowDiary.Web/Services/Wasserband.cs` |
| Lichtflanke löst das Schreiben aus | `GrowDiary.Web/Services/HomeAssistantSnapshotWorker.cs` |
| Schnittstelle, Deckelung, DTOs | `GrowDiary.Web/Api/Controllers/NightRampApiController.cs` |
| Zelt-Felder (`ChillerSwitchEntityId`, `ChillerHysteresisC`, …) | `GrowDiary.Web/Models/Tent.cs` |
| Daten der Live-Karte (`KuehlerLageAsync`) | `GrowDiary.Web/Controllers/TentsController.cs` |
| Seite, Grow-Karte, Live-Karte | `GrowDiary.React/src/pages/CropSteeringPage.tsx`, `features/grows/NightRampCard.tsx`, `features/live/LiveScreen.tsx` |

## Fallen

- **Der Regler hätte in einer echten Anlage nie geschaltet.** Der Zustand der
  Steckdose wurde im Wörterbuch aus `GetStatesAsync` gesucht — dessen Schlüssel
  sind Metrik-Kennungen (`chiller`, `reservoir-temp`), nie Entitäts-Kennungen.
  Es fiel nicht auf, weil die Demodaten genau diesen Schlüssel eintrugen: **der
  Testbestand verdeckte den Fehler.** Beleg: Klassenkommentar in
  `GrowDiary.Web.Tests/KuehlerLageTests.cs`.
- **Alter am `last_changed` gemessen.** Das bewegt sich nur, wenn sich der
  Zustands*text* ändert — also gerade dann nicht, wenn die Regelung ihr Ziel
  trifft. Jetzt `LastUpdated` (Kommentar in `KuehlerWorker.LageLesen`).
- **Der Anlagen-Wächter meldete die eigene Regelpause als Ausfall.** Ein
  Zeitfenster von 20 Minuten half nicht — eine kühle Nacht ist länger. Die Frage
  geht jetzt über den letzten *Befehl* (`PumpWatchNotifier`,
  `KuehlerService.IstAbsichtlichAus`).
- **Die App meldete ihre eigene Regelung als Abweichung.** Arbeitsbereich ab
  17 °C, Rampe auf 16 °C: ab Blütewoche 3 lag jede Nachtmessung „außerhalb". Die
  Zahlen standen an drei Stellen in zwei Diensten. Beleg: „Der Anlass" in
  `GrowDiary.Web.Tests/WasserbandTests.cs`.
- **Zwei Formulare für dieselbe Sache** wären es beinahe geworden: die Karte am
  Grow zeigt nur noch den Stand (`NightRampCard.tsx`).
