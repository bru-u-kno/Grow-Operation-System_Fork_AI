# Sicherheits-Leitplanken

Stand: 04.10.2026. **Jeder Bau-Agent liest diese Datei, bevor er Code schreibt**,
der Zugriffe, Rechte, Home Assistant, Geräte oder Sicherungen berührt.

Jede Regel hier stammt aus einem Befund des Prüfers. Gefunden wurden die Fehler
erst am Ende einer Runde und kosteten deshalb jedes Mal eine weitere Runde.
Wer sie beim Bauen kennt, baut sie nicht ein.

## Rechte

1. **Erlaubtlisten, nie Verbotslisten.** Was nicht ausdrücklich erlaubt ist, ist
   gesperrt. Befund A-005: Bei HA-Diensten war alles erlaubt, was nicht verboten
   war. Über `zha.issue_zigbee_cluster_command` ließ sich so ein Türschloss
   öffnen, obwohl `lock` verboten war. Das gilt für Domains, Dienste, Routen und
   Dateitypen gleichermaßen.
2. **Vergessen sperrt.** Eine neue schreibende Aktion ohne `[KiStufe]` ist über
   einen Schlüssel gesperrt (`KiStufenVollstaendigTests`). Nie einen
   Ausweich-Standard „im Zweifel erlaubt" einbauen.
3. **Wer die Grundlage einer Grenze ändert, braucht mindestens die Stufe der
   Grenze.** Befund A-003: Das Kalibrier-Ergebnis setzt `MlPerMinute`, darüber
   rechnet die Dosisgrenze. Mit „Dokumentieren" ließ sich so die 10-ml-Grenze auf
   225 ml aushebeln.
4. **Rechte nur an einer Stelle prüfen:** `KiZugriffKontext` und die Sperre.
   Kein Controller baut eine eigene Rechteprüfung, er verschärft höchstens. Die
   Domain-Tabelle `KiHaEinstufung` ist eine Verschärfung.
5. **Die Schlüsselverwaltung ist nie über einen Schlüssel erreichbar.** Auch
   nicht auf Umwegen: Sicherung zurückspielen bzw. herunterladen tragen
   `[KeinKiZugriff]` (Befund A-003).

## Eingaben

6. **Was in einen Pfad oder Dienstnamen wandert, nur aus einer Zeichenliste**
   (`[a-z0-9_]`). Sonst wird `../states` zum Ziel.
7. **Ziele nur über das geprüfte Feld.** Freie Datenfelder (`daten`) dürfen
   keine Ziele tragen (`entity_id`, `device_id`, `area_id`, `target` …). Sonst
   überschreiben sie die geprüfte Entität.
8. **Genau ein Ziel.** `light.a,lock.x` ist eine Liste und wird abgewiesen.

## Geheimnisse und Zustand

9. **Klartext-Geheimnisse nie in DB, Log, Prüfprotokoll oder Antwort.** Schlüssel
   nur als Hash. Ins Protokoll gehört, *was* geschaltet wurde (Dienst, Entität),
   nie die mitgeschickten Werte.
10. **Sicherungen enthalten Geheimnisse.** Eine Sicherung kann das HA-Token im
    Klartext enthalten. Wer Sicherungen ausliefert, liefert Geheimnisse aus.
11. **Sicherheitszustand reist nicht mit einer Sicherung zurück.** Nach dem
    Zurückspielen gelten die Schlüssel von vorher
    (`KiZustandBeimZurueckspielen`). Neue Spalten werden automatisch mitgenommen,
    nicht über eine abgetippte Liste.

## Sperren und Grenzen

12. **Eine Sperre darf nie den Berechtigten treffen.** Viele Clients teilen sich
    eine Adresse (Grow MCP, HA-MCP). Eine IP-Sperre gegen Fehlversuche ließ
    früher auch den gültigen Schlüssel nicht mehr durch (Befund A-005).
13. **Ein Stopp scheitert nie an einer Grenze** (`[KiOhneHoechstwert]` am
    Pumpen-Stopp).

## Tests

14. **Sicherheitsprüfungen mit Bissnachweis.** Lehnt die Rechteprüfung den
    Nachweis ab („Security Weaken"), wird nichts umgangen. Das kommt in den
    Bericht.
15. **Alles Zeitabhängige über einen ganzen Tag prüfen.** Ein Demowert lag
    um 07:16 bei pH 5,79 und machte die CI rot, mittags war er im Ziel.
16. **Die Liste der Umgehungsversuche eines Befunds wird zum Test**, als Theorie
    mit genau diesen Fällen. So kommt derselbe Fehler nicht wieder.
