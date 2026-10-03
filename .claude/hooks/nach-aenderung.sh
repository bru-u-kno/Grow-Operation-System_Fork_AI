#!/usr/bin/env bash
# Nach jeder Aenderung an Quelltext: uebersetzen lassen.
#
# WOZU. Erfundene Bezeichner fallen sonst erst drei Schritte spaeter auf.
# Belegt am 19.08.2026: `HydroStyle.Rdwc` (heisst RDWC), `--warn` statt
# `--warn-text`, `DemoData__IsEnabled` statt `GROW_OS_DEMO`. Jedes Mal habe ich
# weitergearbeitet, bevor es jemand gemerkt hat.
#
# Exit 2 gibt stderr an Claude zurueck — wie eine Rueckmeldung des Nutzers.
set -uo pipefail

source "$(dirname "${BASH_SOURCE[0]}")/werkzeuge.sh"
EINGABE="$(cat)"

datei="$(eingabe_feld file_path)"
[ $? -eq 3 ] && laut "Der Bau-Hook kann die geaenderte Datei nicht lesen: weder python3 noch python laeuft." \
  "Nach dieser Aenderung wurde NICHT gebaut."

[ -z "$datei" ] && exit 0
# Andere Projekte unter demselben Arbeitsordner gehen diesen Hook nichts an.
im_repo "$datei" || exit 0
case "$datei" in
  *_test.go|*/node_modules/*|*/bin/*|*/obj/*|*/zz-*) exit 0 ;;
esac

meldung=""

case "$datei" in
  *.cs)
    # -p:UseAppHost=false: sonst scheitert der Bau an der laufenden App, die
    # die .exe sperrt — das ist kein Fehler im Quelltext.
    dotnet_bin="$(dotnet_finden)" || laut "Der Bau-Hook findet dotnet nicht (weder im PATH noch in ~/.dotnet)." \
      "Nach der Aenderung an $(basename "$datei") wurde NICHT gebaut."
    if ! ausgabe="$(cd "$WURZEL" && "$dotnet_bin" build GrowDiary.slnx -v q --nologo -p:UseAppHost=false 2>&1)"; then
      # MSB3021/3026/3027 sind KEINE Fehler im Quelltext: die laufende App
      # sperrt ihre eigene .dll/.exe. Wer die mitmeldet, schickt bei jedem
      # zweiten Bau einen Fehlalarm.
      meldung="$(printf '%s' "$ausgabe" | grep -E "error [A-Z]+[0-9]+" | grep -vE "error MSB30(21|26|27)" | head -8)"
    fi
    ;;
  *.ts|*.tsx)
    # `tsc -b`, NICHT `--noEmit`: tsconfig.json hat "files": [] und nur
    # references. `tsc --noEmit` prueft damit NULL Dateien und ist immer gruen —
    # am 19.08.2026 mehrfach als "Typen ok" gemeldet, ohne etwas zu pruefen.
    if ! ausgabe="$(cd "$WURZEL/GrowDiary.React" && npx tsc -b 2>&1)"; then
      meldung="$(printf '%s' "$ausgabe" | grep -E "error TS" | head -8)"
    fi
    ;;
  *) exit 0 ;;
esac

if [ -n "$meldung" ]; then
  {
    echo "Der Bau ist rot nach der Aenderung an $(basename "$datei"):"
    echo "$meldung"
    echo
    echo "Behebe das, bevor du weiterarbeitest. Bezeichner aus der Datei holen, nicht aus dem Kopf."
  } >&2
  exit 2
fi
exit 0
