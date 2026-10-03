#!/usr/bin/env bash
# Vor `git commit`: das volle lokale Tor. Rot => der Commit findet nicht statt.
#
# WOZU. Am 19.08.2026 sind zwei Commits hintereinander bei rotem CI gelandet,
# weil ich lokal gruen gemeldet und CI nie angesehen habe. Das Tor hier ist
# dasselbe, das ci.yml faehrt — was hier durchkommt, kommt dort durch.
#
# Reine Text-Aenderungen (Changelog, Notizen) laufen ohne Tor durch: dort kann
# nichts uebersetzen.
set -uo pipefail

source "$(dirname "${BASH_SOURCE[0]}")/werkzeuge.sh"
EINGABE="$(cat)"

# Schneller Ausstieg fuer jeden Bash-Aufruf ohne das Wort „commit" — der Hook
# laeuft vor JEDEM Befehl, und Python zu starten kostet.
case "$EINGABE" in *commit*) ;; *) exit 0 ;; esac

befehl="$(eingabe_feld command)"
[ $? -eq 3 ] && laut "Der Commit-Hook kann den Befehl nicht lesen: weder python3 noch python laeuft." \
  "Das Tor (Backend, Typen, Lint, Vitest) laeuft damit NICHT — deshalb wird nichts durchgelassen." \
  "Python installieren oder in den PATH bringen; bis dahin das Tor von Hand fahren."

# Nur beim echten Commit, nicht bei `git log --grep commit` o.ae.
printf '%s' "$befehl" | grep -qE '(^|[;&|]|\s)git\s+(-[^ ]+\s+)*commit(\s|$)' || exit 0

# Nur Commits in DIESEM Projekt — und geprueft wird der Stand, in dem der
# Commit geschieht (Haupt-Verzeichnis oder Worktree, F2). Woher: das letzte
# `cd` oder `git -C` im Befehl, sonst das Arbeitsverzeichnis der Sitzung.
arbeitsort="$(eingabe_feld oben.cwd)"
ZIEL=""
kandidaten="$(printf '%s' "$befehl" | grep -oE '(cd|git -C)[[:space:]]+"?[^"&;|[:space:]]+' | sed -E 's/^(cd|git -C)[[:space:]]+"?//' | tac)"
for kandidat in $kandidaten "$arbeitsort"; do
  ZIEL="$(ziel_wurzel "$kandidat")" && break
  ZIEL=""
done
[ -z "$ZIEL" ] && exit 0

# WELCHE DATEIEN. Der Index allein reicht NICHT: dieser Hook laeuft, BEVOR der
# Befehl ausgefuehrt wird. Bei `git add -A && git commit` in EINEM Aufruf ist zu
# diesem Zeitpunkt noch nichts vorgemerkt — der Hook sah eine leere Liste und
# liess den Commit ungeprueft durch. Am 01.09.2026 sind so drei Commits bei
# rotem CI gelandet; genau das, was dieser Hook verhindern soll.
#
# Merkt der Befehl selbst etwas vor (git add, commit -a, commit <pfade>), zaehlt
# deshalb der ganze Arbeitsbaum.
vorgemerkt="$(cd "$ZIEL" && git diff --cached --name-only)"

if printf '%s' "$befehl" | grep -qE '(^|[;&|]|\s)git\s+add(\s|$)|commit\s+(-[a-zA-Z]*a|--all)'; then
  vorgemerkt="$vorgemerkt
$(cd "$ZIEL" && git status --porcelain | sed 's/^...//' | sed 's/.* -> //')"
fi

vorgemerkt="$(printf '%s
' "$vorgemerkt" | grep -v '^$' | sort -u)"
[ -z "$vorgemerkt" ] && exit 0

# Nur Text? Dann gibt es nichts zu uebersetzen.
if ! printf '%s\n' "$vorgemerkt" | grep -qE '\.(cs|ts|tsx|css|json|csproj|slnx|runsettings)$'; then
  exit 0
fi

fehler=""

if printf '%s\n' "$vorgemerkt" | grep -qE '\.(cs|csproj|slnx|runsettings)$'; then
  dotnet_bin="$(dotnet_finden)" || laut "Der Commit-Hook findet dotnet nicht (weder im PATH noch in ~/.dotnet)." \
    "Das Backend-Tor laeuft damit NICHT — der Commit wird nicht ausgefuehrt."
  if ! a="$(cd "$ZIEL" && "$dotnet_bin" test GrowDiary.slnx --nologo -v q 2>&1)"; then
    fehler="$fehler
BACKEND ROT:
$(printf '%s' "$a" | grep -E 'FAIL|Fehler:|error ' | head -10)"
  fi
fi

if printf '%s\n' "$vorgemerkt" | grep -qE '\.(ts|tsx|css|json)$'; then
  if ! a="$(cd "$ZIEL/GrowDiary.React" && npx tsc -b --force 2>&1)"; then
    fehler="$fehler
TYPEN ROT:
$(printf '%s' "$a" | grep -E 'error TS' | head -10)"
  fi
  if ! a="$(cd "$ZIEL/GrowDiary.React" && npm run lint 2>&1)"; then
    fehler="$fehler
LINT ROT:
$(printf '%s' "$a" | grep -E 'error' | head -10)"
  fi
  if ! a="$(cd "$ZIEL/GrowDiary.React" && npx vitest run 2>&1)"; then
    fehler="$fehler
VITEST ROT:
$(printf '%s' "$a" | grep -E 'FAIL|×' | head -10)"
  fi
fi

if [ -n "$fehler" ]; then
  {
    echo "Der Commit wurde NICHT ausgefuehrt — das Tor ist rot:"
    echo "$fehler"
    echo
    echo "Dasselbe Tor faehrt ci.yml. Wer hier vorbeikommt, macht CI rot."
  } >&2
  exit 2
fi
exit 0
