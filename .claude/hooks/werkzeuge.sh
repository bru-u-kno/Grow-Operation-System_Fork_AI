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

# Gehoert dieser Pfad zu diesem Repository? Die Hooks sind auch aktiv, wenn die
# Sitzung im Ordner darueber startet (`projekte/`) — dort liegen andere Projekte,
# deren Commits dieses Tor nichts angehen.
#
# Unter Windows kommen Pfade als „C:\Users\…", Git-Bash schreibt „/c/Users/…".
# Ohne Angleichung hielte der Hook dort JEDE Datei für fremd — und stiege
# wieder still aus.
pfad_einheitlich() {
  local p="${1//\\//}"
  case "$p" in
    [A-Za-z]:/*) p="/$(printf '%s' "${p:0:1}" | tr 'A-Z' 'a-z')${p:2}" ;;
  esac
  printf '%s' "$p" | tr 'A-Z' 'a-z'
}

im_repo() {
  local pfad wurzel
  [ -z "$1" ] && return 1
  pfad="$(pfad_einheitlich "$1")"
  wurzel="$(pfad_einheitlich "$WURZEL")"
  case "$pfad" in
    "$wurzel"|"$wurzel"/*) return 0 ;;
    *) return 1 ;;
  esac
}

# Laut abbrechen: Exit 2 gibt stderr an Claude zurueck (bei PreToolUse:
# der Befehl wird NICHT ausgefuehrt).
laut() {
  printf '%s\n' "$@" >&2
  exit 2
}
