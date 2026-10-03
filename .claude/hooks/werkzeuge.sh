#!/usr/bin/env bash
# Gemeinsames fuer beide Hooks: Werkzeuge finden, Eingabe lesen, Zustaendigkeit.
#
# WOZU (03.10.2026, offene Punkte F1). Auf der VM `ClaudeCode` lief der
# Commit-Hook wochenlang ins Leere: er rief `python` und `dotnet`, dort gibt es
# nur `python3`, und `dotnet` liegt in ~/.dotnet. Ohne Python blieb der gelesene
# Befehl leer, die Pruefung „ist das ein git commit?" sagte nein — und der Hook
# endete mit 0. Jeder Commit kam durch, ohne dass ein einziger Test lief, und
# niemand sah es. Ein Tor, das bei fehlendem Werkzeug „gruen" meldet, ist
# schlimmer als keins: es behauptet eine Pruefung.
#
# Deshalb: Werkzeuge werden gesucht, nicht vorausgesetzt — und wenn eins fehlt,
# sagt der Hook das LAUT (Exit 2), statt still durchzuwinken.

WURZEL="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

# Python, das wirklich laeuft. `python3` zuerst: unter Debian gibt es nur das,
# unter Windows ist `python3` oft der Store-Platzhalter — der faellt beim
# Probelauf durch, dann kommt `python`.
python_finden() {
  local kandidat
  for kandidat in python3 python; do
    if command -v "$kandidat" >/dev/null 2>&1 && "$kandidat" -c "import json" >/dev/null 2>&1; then
      printf '%s' "$kandidat"
      return 0
    fi
  done
  return 1
}

# dotnet aus dem PATH oder aus der Benutzer-Installation von dotnet-install.sh.
dotnet_finden() {
  if command -v dotnet >/dev/null 2>&1; then
    printf '%s' "dotnet"
  elif [ -x "$HOME/.dotnet/dotnet" ]; then
    printf '%s' "$HOME/.dotnet/dotnet"
  else
    return 1
  fi
}

# Ein Feld aus der JSON-Eingabe des Hooks: `tool_input.<name>` oder, mit
# Praefix `oben.`, ein Feld der obersten Ebene (z. B. `oben.cwd`).
# Gibt 3 zurueck, wenn kein Python da ist — der Aufrufer muss das melden.
eingabe_feld() {
  local feld="$1" py
  py="$(python_finden)" || return 3
  printf '%s' "$EINGABE" | "$py" -c "import json,sys
feld=sys.argv[1]
try:
    d=json.load(sys.stdin)
    w=d.get(feld[5:]) if feld.startswith('oben.') else d.get('tool_input',{}).get(feld)
    print(w or '')
except Exception:
    print('')" "$feld"
}

# In WELCHEM Stand des Repos arbeitet dieser Pfad? — das Haupt-Verzeichnis
# oder ein Worktree (offene Punkte 03.10.2026, F2).
#
# Bis dahin bauten und prueften die Hooks immer `WURZEL`, also das
# Haupt-Verzeichnis, aus dem die Skripte stammen. Ein Agent in einem Worktree
# (`.claude/worktrees/…`, oder ein `git worktree` daneben) aenderte seine Datei
# — und der Hook baute einen ANDEREN Stand: gruen, ohne die Aenderung je
# gesehen zu haben. Jetzt gilt der Stand, zu dem der Pfad gehoert, sofern es
# dieses Projekt ist (es traegt `GrowDiary.slnx`). Gibt die Ausgabe leer
# zurueck, geht der Pfad dieses Projekt nichts an.
ziel_wurzel() {
  local pfad="$1" ordner oben
  [ -z "$pfad" ] && return 1
  ordner="$pfad"
  [ -d "$ordner" ] || ordner="$(dirname "$ordner")"
  # Neue Dateien: der Ordner kann noch fehlen — dann den naechsten vorhandenen.
  while [ ! -d "$ordner" ] && [ "$ordner" != "/" ] && [ "$ordner" != "." ]; do ordner="$(dirname "$ordner")"; done
  oben="$(git -C "$ordner" rev-parse --show-toplevel 2>/dev/null)" || return 1
  [ -f "$oben/GrowDiary.slnx" ] || return 1
  printf '%s' "$oben"
}

# Laut abbrechen: Exit 2 gibt stderr an Claude zurueck (bei PreToolUse:
# der Befehl wird NICHT ausgefuehrt).
laut() {
  printf '%s\n' "$@" >&2
  exit 2
}
