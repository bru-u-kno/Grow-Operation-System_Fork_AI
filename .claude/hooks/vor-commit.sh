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

# ---------------------------------------------------------------------------
# AB HIER DAS TOR SELBST (beschleunigt am 19.10.2026, Messung vom selben Tag).
#
# Gemessen (8 Kerne): Backend seriell 265 s bei nur 55 s CPU — die Tests warten,
# sie rechnen nicht; tsc 22 s + Lint 44 s + Vitest 14 s hintereinander. Deshalb:
#   A  Ein gruenes Ergebnis wird je INHALT gemerkt (Baum-Hash des Arbeitsbaums
#      samt neuen Dateien) — `--amend` oder ein zweiter Versuch mit gleichem
#      Stand fahren nichts neu. Rot wird nie gemerkt. Nur lokal, je Worktree,
#      hoechstens 6 Stunden gueltig, und eine Aenderung an diesem Tor selbst
#      macht alle Eintraege ungueltig.
#   B  Backend in den drei Teilen von ci.yml GLEICHZEITIG (gleiche Filter, gleiche
#      Mindestzahlen als Mengenwaechter). Faellt ein Teil durch, laeuft genau
#      dieser Teil noch einmal einzeln; nur wenn er auch dann rot ist, ist das
#      Tor rot (ein Flackern durch die Parallelitaet wird laut gemeldet).
#   C  tsc, Lint und Vitest gleichzeitig; jede Pruefung wird einzeln ausgewertet.
#   D  Jede Dauer steht in `<git-dir>/tor-zeiten.csv` (lokal, nicht im Repo).
# Die Pruefungen selbst sind dieselben geblieben — nichts davon wird uebersprungen.
# ---------------------------------------------------------------------------
py="$(python_finden)"
GITDIR="$(cd "$ZIEL" && git rev-parse --absolute-git-dir 2>/dev/null)"
CACHE="$GITDIR/tor-cache"
ZEITEN="$GITDIR/tor-zeiten.csv"
mkdir -p "$CACHE" 2>/dev/null
jetzt() { date +%s; }
zeit_log() { # schritt sekunden ergebnis
  [ -f "$ZEITEN" ] || echo "zeit,schritt,sekunden,ergebnis" >> "$ZEITEN"
  echo "$(date +%F_%T),$1,$2,$3" >> "$ZEITEN"
}

baumhash() {
  local tmp h
  tmp="$(mktemp)" || return 1
  if [ -f "$GITDIR/index" ]; then cp "$GITDIR/index" "$tmp"; else rm -f "$tmp"; fi
  h="$(cd "$ZIEL" && GIT_INDEX_FILE="$tmp" git add -A >/dev/null 2>&1 && GIT_INDEX_FILE="$tmp" git write-tree 2>/dev/null)"
  rm -f "$tmp"
  [ -n "$h" ] && printf '%s' "$h"
}
torhash="$(cat "${BASH_SOURCE[0]}" "$(dirname "${BASH_SOURCE[0]}")/werkzeuge.sh" | sha1sum | cut -c1-12)"
baum="$(baumhash)"
schluessel() { printf '%s-%s-%s' "$baum" "$1" "$torhash"; }
cache_treffer() { [ -n "$baum" ] && [ -n "$(find "$CACHE" -name "$(schluessel "$1")" -mmin -360 2>/dev/null)" ]; }
cache_merken() { [ -n "$baum" ] && : > "$CACHE/$(schluessel "$1")"; }
find "$CACHE" -type f -mmin +2880 -delete 2>/dev/null

hinweise=""
fehler=""

# Zaehlt Ergebnisse aus TRX-Dateien: "gesamt fehlgeschlagen" — Namen der roten dazu.
trx_zaehlen() { # verzeichnis
  "$py" - "$1" <<'PYEOF'
import glob, re, sys
gesamt = rot = 0
namen = []
for f in glob.glob(sys.argv[1] + "/**/*.trx", recursive=True):
    t = open(f, encoding="utf-8", errors="replace").read()
    m = re.search(r"<Counters\b[^>]*>", t)
    if not m:
        continue
    c = m.group(0)
    gesamt += int(re.search(r'\btotal="(\d+)"', c).group(1))
    rot += int(re.search(r'\bfailed="(\d+)"', c).group(1))
    namen += re.findall(r'<UnitTestResult\b[^>]*testName="([^"]+)"[^>]*outcome="Failed"', t)
    namen += re.findall(r'<UnitTestResult\b[^>]*outcome="Failed"[^>]*testName="([^"]+)"', t)
print(gesamt, rot)
print(";".join(sorted(set(namen))[:8]))
PYEOF
}

backend_teil() { # name filter ergebnisordner  -> schreibt rc in <ordner>/<name>.rc
  local name="$1" filter="$2" d="$3"
  mkdir -p "$d/tmp-$name"
  ( cd "$ZIEL" && TMPDIR="$d/tmp-$name" "$dotnet_bin" test GrowDiary.slnx --no-build --nologo -v q \
      --filter "$filter" --logger trx --results-directory "$d/$name" > "$d/$name.log" 2>&1; echo $? > "$d/$name.rc" )
}

# Gruen, wenn Exit 0, nichts rot und mindestens so viele Tests wie erwartet
# (Mengenwaechter: ein vertippter Filter faellt auf). Setzt rc/gesamt/rot/namen.
teil_gruen() { # name minimum ordner
  local zaehl
  rc="$(cat "$3/$1.rc" 2>/dev/null || echo 99)"
  zaehl="$(trx_zaehlen "$3/$1")"
  read -r gesamt rot <<< "$(printf '%s\n' "$zaehl" | sed -n 1p)"
  namen="$(printf '%s\n' "$zaehl" | sed -n 2p)"
  gesamt="${gesamt:-0}"; rot="${rot:-0}"
  [ "$rc" = 0 ] && [ "$rot" = 0 ] && [ "$gesamt" -ge "$2" ]
}

backend_pruefen() {
  local d t0 a name filter minimum z rc gesamt rot namen
  d="$(mktemp -d)"; t0="$(jetzt)"
  if ! a="$(cd "$ZIEL" && "$dotnet_bin" build GrowDiary.slnx --nologo -v q 2>&1)"; then
    fehler="$fehler
BACKEND ROT (Bau):
$(printf '%s' "$a" | grep -E 'error ' | sort -u | head -10)"
    zeit_log backend-bau $(( $(jetzt) - t0 )) rot; rm -rf "$d"; return
  fi
  zeit_log backend-bau $(( $(jetzt) - t0 )) gruen
  local teile=(
    "services|FullyQualifiedName~GrowDiary.Web.Tests.Services.|900"
    "api|FullyQualifiedName~GrowDiary.Web.Tests.Api.|350"
    "rest|FullyQualifiedName!~GrowDiary.Web.Tests.Services.&FullyQualifiedName!~GrowDiary.Web.Tests.Api.|450"
  )
  t0="$(jetzt)"
  for z in "${teile[@]}"; do
    IFS='|' read -r name filter minimum <<< "$z"
    backend_teil "$name" "$filter" "$d" &
  done
  wait
  zeit_log backend-parallel $(( $(jetzt) - t0 )) fertig
  for z in "${teile[@]}"; do
    IFS='|' read -r name filter minimum <<< "$z"
    if ! teil_gruen "$name" "$minimum" "$d"; then
      # Einmal einzeln wiederholen: war es die Parallelitaet, ist es jetzt gruen.
      erst="rc=$rc, Tests $gesamt, rot $rot ${namen:-}"
      rm -rf "$d/$name" "$d/$name.rc"
      t0="$(jetzt)"; backend_teil "$name" "$filter" "$d"
      zeit_log "backend-$name-wiederholt" $(( $(jetzt) - t0 )) fertig
      if teil_gruen "$name" "$minimum" "$d"; then
        hinweise="$hinweise
ACHTUNG: Backend-Teil \"$name\" war im Parallellauf rot ($erst), einzeln gruen. Ein Test haengt von Reihenfolge/Gleichzeitigkeit ab — nachgehen."
      else
        fehler="$fehler
BACKEND ROT (Teil $name; rc=$rc, Tests $gesamt, rot $rot, Mindestzahl $minimum):
$(printf '%s' "${namen:-}" | tr ';' '\n')
$(grep -E 'FAIL|Fehler:|error ' "$d/$name.log" | head -10)"
      fi
    fi
  done
  rm -rf "$d"
}

frontend_pruefen() {
  local d n t0 a
  d="$(mktemp -d)"; t0="$(jetzt)"
  for n in tsc lint vitest; do
    (
      s="$(jetzt)"; cd "$ZIEL/GrowDiary.React" || exit 99
      case "$n" in
        tsc) npx tsc -b --force ;;
        lint) npm run lint ;;
        vitest) npx vitest run ;;
      esac > "$d/$n.log" 2>&1
      echo $? > "$d/$n.rc"; echo $(( $(jetzt) - s )) > "$d/$n.sek"
    ) &
  done
  wait
  for n in tsc lint vitest; do
    zeit_log "frontend-$n" "$(cat "$d/$n.sek" 2>/dev/null || echo 0)" "$([ "$(cat "$d/$n.rc" 2>/dev/null)" = 0 ] && echo gruen || echo rot)"
    if [ "$(cat "$d/$n.rc" 2>/dev/null || echo 99)" != 0 ]; then
      case "$n" in
        tsc) titel="TYPEN ROT"; muster='error TS' ;;
        lint) titel="LINT ROT"; muster='error' ;;
        vitest) titel="VITEST ROT"; muster='FAIL|×' ;;
      esac
      a="$(grep -E "$muster" "$d/$n.log" | head -10)"
      [ -z "$a" ] && a="$(tail -n 8 "$d/$n.log")"
      fehler="$fehler
$titel:
$a"
    fi
  done
  zeit_log frontend-gesamt $(( $(jetzt) - t0 )) fertig
  rm -rf "$d"
}

# Backend (.cs) und Frontend (.ts/.tsx/.css/.json) laufen gleichzeitig, sofern noetig.
braucht_backend=0; braucht_frontend=0
printf '%s\n' "$vorgemerkt" | grep -qE '\.(cs|csproj|slnx|runsettings)$' && braucht_backend=1
printf '%s\n' "$vorgemerkt" | grep -qE '\.(ts|tsx|css|json)$' && braucht_frontend=1

if [ "$braucht_backend" = 1 ]; then
  dotnet_bin="$(dotnet_finden)" || laut "Der Commit-Hook findet dotnet nicht (weder im PATH noch in ~/.dotnet)." \
    "Das Backend-Tor laeuft damit NICHT — der Commit wird nicht ausgefuehrt."
fi
[ -z "$py" ] && laut "Der Commit-Hook findet kein Python — das Tor laeuft damit NICHT."

gesamt_start="$(jetzt)"
ergebnis_ordner="$(mktemp -d)"
treffer=""
if [ "$braucht_backend" = 1 ]; then
  if cache_treffer backend; then treffer="$treffer Backend"; braucht_backend=0; zeit_log backend-gesamt 0 cache; fi
fi
if [ "$braucht_frontend" = 1 ]; then
  if cache_treffer frontend; then treffer="$treffer Frontend"; braucht_frontend=0; zeit_log frontend-gesamt 0 cache; fi
fi

# Backend und Frontend gleichzeitig (je Teil laufen intern mehrere Prozesse).
# Jeder Zweig laeuft in einer Unter-Shell und gibt Fehler/Hinweise ueber Dateien
# zurueck; gemerkt wird nur, was in seinem Zweig gruen war.
zweig() { # name funktion
  (
    fehler=""; hinweise=""
    "$2"
    printf '%s' "$fehler" > "$ergebnis_ordner/$1.fehler"
    printf '%s' "$hinweise" > "$ergebnis_ordner/$1.hinweise"
    [ -z "$fehler" ] && cache_merken "$1"
  ) &
}
[ "$braucht_backend" = 1 ] && zweig backend backend_pruefen
[ "$braucht_frontend" = 1 ] && zweig frontend frontend_pruefen
wait
for n in backend frontend; do
  [ -f "$ergebnis_ordner/$n.fehler" ] && fehler="$fehler$(cat "$ergebnis_ordner/$n.fehler")"
  [ -f "$ergebnis_ordner/$n.hinweise" ] && hinweise="$hinweise$(cat "$ergebnis_ordner/$n.hinweise")"
done
# Ein Zweig, der nicht einmal sein Ergebnis schreiben konnte, ist rot, nicht gruen.
for n in backend frontend; do
  if { [ "$n" = backend ] && [ "$braucht_backend" = 1 ]; } || { [ "$n" = frontend ] && [ "$braucht_frontend" = 1 ]; }; then
    [ -f "$ergebnis_ordner/$n.fehler" ] || fehler="$fehler
$n: Zweig ohne Ergebnis abgebrochen — als ROT gewertet."
  fi
done
rm -rf "$ergebnis_ordner"
zeit_log tor-gesamt $(( $(jetzt) - gesamt_start )) "$([ -n "$fehler" ] && echo rot || echo gruen)"

if [ -n "$fehler" ]; then
  {
    echo "Der Commit wurde NICHT ausgefuehrt — das Tor ist rot:"
    echo "$fehler"
    [ -n "$hinweise" ] && echo "$hinweise"
    echo
    echo "Dasselbe Tor faehrt ci.yml. Wer hier vorbeikommt, macht CI rot."
  } >&2
  exit 2
fi

# Gruen: der Hook sagt es ausdruecklich, damit „uebersprungen" und „bestanden"
# nie verwechselt werden (Treffer im Zwischenspeicher sind eigens genannt).
meldung="Commit-Tor gruen in $(( $(jetzt) - gesamt_start )) s."
[ -n "$treffer" ] && meldung="$meldung Aus dem Zwischenspeicher (gleicher Inhalt, vor <6 h gruen):$treffer."
[ -n "$hinweise" ] && meldung="$meldung$hinweise"
"$py" -c "import json,sys; print(json.dumps({'hookSpecificOutput':{'hookEventName':'PreToolUse','additionalContext':sys.argv[1]}}))" "$meldung"
exit 0
