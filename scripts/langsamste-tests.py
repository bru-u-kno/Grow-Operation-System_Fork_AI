#!/usr/bin/env python3
"""Die langsamsten Backend-Tests aus den TRX-Dateien von `dotnet test`.

Anlass (30.09.2026): Derselbe Commit brauchte im Backend einmal 2:52 und einmal
8:22 Minuten — alle 2181 Tests grün, aber welcher Test bremste, stand nirgends.
Die CI schreibt deshalb TRX-Dateien; dieses Skript nennt die langsamsten Tests
im Protokoll und in der Zusammenfassung des Laufs.

Aufruf: langsamste-tests.py <verzeichnis> [anzahl]

Bricht ab (Exit 1), wenn es KEINE Ergebnisse findet: eine Auswertung, die bei
leerer Grundmenge still nichts meldet, hat nichts gemessen (CLAUDE.md).
"""

import glob
import os
import sys
import xml.etree.ElementTree as ET

NS = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"


def sekunden(dauer: str) -> float:
    """TRX-Dauer „hh:mm:ss.fffffff“ in Sekunden."""
    stunden, minuten, rest = dauer.split(":")
    return int(stunden) * 3600 + int(minuten) * 60 + float(rest)


def main() -> int:
    verzeichnis = sys.argv[1]
    anzahl = int(sys.argv[2]) if len(sys.argv) > 2 else 15
    dateien = sorted(glob.glob(os.path.join(verzeichnis, "**", "*.trx"), recursive=True))
    ergebnisse = []
    for datei in dateien:
        for r in ET.parse(datei).getroot().iter(f"{NS}UnitTestResult"):
            if r.get("duration"):
                ergebnisse.append((sekunden(r.get("duration")), r.get("testName"), os.path.basename(datei)))

    if not ergebnisse:
        print(f"::error::Keine Testergebnisse in {verzeichnis} ({len(dateien)} TRX-Dateien) — die Messung misst nichts.")
        return 1

    ergebnisse.sort(reverse=True)
    summe = sum(s for s, _, _ in ergebnisse)
    zeilen = [f"{s:8.1f} s  {name}" for s, name, _ in ergebnisse[:anzahl]]
    kopf = f"{len(ergebnisse)} Tests aus {len(dateien)} TRX-Dateien, zusammen {summe:.0f} s. Die {min(anzahl, len(ergebnisse))} langsamsten:"
    print(kopf)
    print("\n".join(zeilen))

    zusammenfassung = os.environ.get("GITHUB_STEP_SUMMARY")
    if zusammenfassung:
        with open(zusammenfassung, "a", encoding="utf-8") as f:
            f.write("### Langsamste Backend-Tests\n\n" + kopf + "\n\n```\n" + "\n".join(zeilen) + "\n```\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
