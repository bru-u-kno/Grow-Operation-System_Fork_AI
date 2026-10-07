# Probelauf — eine Steuerung zeitlich begrenzt abschalten

> Fork AI (A-010, 07.10.2026). Der Fork schaltet auf Wunsch eine Regelung samt Gerät für 10–30 Minuten
> ab, zeichnet auf, wie sich Luftfeuchte, Temperatur und VPD entwickeln, stellt zurück und wertet aus.

## Wo in der App

`/steuerung/probelauf` — Menü **Betrieb → Probelauf**. Eine Seite für alles:

- **Formular** (ohne offenen Lauf): Gerät wählen (die Steuerungen mit ihrem zentral gepflegten Titel),
  Dauer (10, 20 oder 30 Minuten), unter „Erweitert" die Grenzen, dann **Probelauf starten**.
- **Laufender Lauf**: Restzeit, die letzten Messwerte gegen die Grenzen und **Jetzt abbrechen und zurückstellen**.
- **Ergebnis**: Kurve (Luftfeuchte, Temperatur oder VPD; Vorlauf, Gerät aus, Nachlauf), Kennzahlen und Hinweise.
- **Frühere Läufe**: Liste, jeder Lauf lässt sich wieder ansehen.

## Was es tut

Ein Probelauf pausiert die **Regelung** der gewählten Steuerung (die Automation in Home Assistant, bei
Kühler zusätzlich die Fork-eigene Regelung) und schaltet das **Gerät** aus. Alle fünf Sekunden prüft der Fork
die Grenzen; wird eine überschritten — oder meldet ein Fühler länger als eine Minute nichts —, bricht der Lauf ab
und stellt zurück. Zum geplanten Ende (spätestens fünf Minuten danach) wird in jedem Fall zurückgestellt.
Danach läuft ein Nachlauf von zehn Minuten, dann wird ausgewertet.

Möglich an: Entfeuchter, Zusatz-Entfeuchter, Zuluft, CO₂ und Chiller. Es läuft immer nur **ein** Lauf zugleich.

## Die Zahlen und woher sie kommen

- **Messwerte**: die Zeltfühler aus „Geräte & Entitäten" (Rollen Feuchte, Temperatur, VPD, Lichtzustand), im
  Abstand von 30 Sekunden aufgezeichnet.
- **Vorher**: die letzten 10 Minuten aus dem Verlauf von Home Assistant, auf Minuten gelegt.
- **Grenzen**: vorbelegt als Pflanzenziel **plus Spielraum** (Luftfeuchte max. +4 Punkte, Temperatur max. +1,5 K, VPD-Band ±0,5 kPa; Quelle: Entfeuchter-Seite). Die Ziele regeln, die Grenzen sichern — ein Lauf soll das Ziel überschreiten dürfen.
- **Kennzahlen**: Start, Spitze, Ende, Änderung je Minute (Ende minus Start durch die Minuten des Eingriffs) und
  Erholung (Minuten nach dem Ende, bis 90 % der Abweichung vom Startwert wieder abgebaut sind).
- **Hinweise**: u. a. ein Lichtwechsel während des Laufs — dann sind die Werte nur eingeschränkt vergleichbar.

## Was es bewusst NICHT tut

- **Keine Wächter abschalten.** CO₂-Wächter, Kühler-Wächter und die Meldung „Zusatz zieht nichts" laufen im Probelauf weiter.
- **Nichts ohne Rückstellung.** Der Zustand vor dem Lauf (mit den damals zugeordneten Entity-IDs) steht in der Datenbank;
  nach einem Neustart des Add-ons wird zuerst zurückgestellt. Ist das Zurückstellen nicht bestätigt, bleibt der Lauf offen,
  der Fork versucht es weiter und meldet sich aufs Handy.
- **Kein Licht.** Das Licht ändert die Lichtphase und ist (noch) nicht Teil des Probelaufs.
- **Keine Einstellung ändern.** Der Probelauf misst; was daraus folgt, übernimmt der Nutzer selbst.
- **Kein zweiter Lauf zugleich**, auch nicht an einer anderen Steuerung.

## Im Code

`Services/ProbelaufService.cs` (Ablauf), `Services/ProbelaufBewertung.cs` (Grenzprüfung, Kennzahlen),
`Services/Probelauf/ProbelaufEingriff.cs` (Eingreifen und Zurückstellen), `Services/ProbelaufWorker.cs` (Takt),
`Infrastructure/ProbelaufRepository.cs` (Tabelle `ForkProbelauf`), `Api/Controllers/ProbelaufApiController.cs`
(`api/steuerung/probelauf`), Oberfläche `GrowDiary.React/src/features/probelauf/`. Welche Automation pausiert wird, steht
ausdrücklich am Bauteil (`ProbelaufRolle` in `Models/SteuerungBauteil.cs`).
