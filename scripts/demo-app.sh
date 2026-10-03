#!/usr/bin/env bash
# Demo-App starten und beenden — je Port eine eigene Instanz (Linux, VM, Agenten).
#
#   scripts/demo-app.sh start [PORT]   baut, startet mit frischem Demobestand, wartet
#   scripts/demo-app.sh stop  [PORT]   beendet GENAU die App an diesem Port
#   scripts/demo-app.sh status [PORT]  wer hört dort, und ist es der gebaute Stand?
#
# Das Gegenstück zu scripts/start-dev-demo.ps1 (Windows). Warum es das gibt
# (offene Punkte 03.10.2026, F3):
#
# - Der PORT kommt über `Hosting__DefaultUrls`. `ASPNETCORE_URLS` wirkt NICHT —
#   Program.cs setzt `Hosting:DefaultUrls` (appsettings.json: 5076) per
#   UseUrls und überschreibt damit jede andere Angabe. Wer es mit
#   ASPNETCORE_URLS versucht, landet still auf 5076, neben der App eines anderen.
# - BEENDET wird nur die PID, die an DIESEM Port hört. Am 02.10.2026 hat ein
#   Beenden über alle `GrowDiary.Web.dll`-Prozesse die Apps der Agenten
#   mitgerissen.
# - Ein belegter Port bricht laut ab. Am 25.08.2026 wurde eine halbe Stunde
#   gegen eine alte Instanz gemessen, die den Port noch hielt (CLAUDE.md,
#   „Erst belegen, dass der laufende Stand der gebaute ist").
# - Jeder Start bekommt einen NEUEN Datenordner — der Demobestand entsteht nur
#   in einer leeren Datenbank. Nichts wird gelöscht.
set -uo pipefail

WURZEL="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
befehl="${1:-status}"
port="${2:-5076}"
url="http://127.0.0.1:$port"

dotnet_bin() {
  if command -v dotnet >/dev/null 2>&1; then echo dotnet
  elif [ -x "$HOME/.dotnet/dotnet" ]; then echo "$HOME/.dotnet/dotnet"
  else echo "dotnet nicht gefunden (weder im PATH noch in ~/.dotnet)." >&2; exit 2; fi
}

pid_am_port() {
  ss -ltnp 2>/dev/null | grep -E "[:.]$port[[:space:]]" | grep -o 'pid=[0-9]*' | head -1 | cut -d= -f2
}

# TimeDateStamp aus dem PE-Kopf — dieselbe Zahl, die /api/system/backend-health
# als bauKennung meldet.
kennung_der_dll() {
  python3 - "$WURZEL/GrowDiary.Web/bin/Debug/net8.0/GrowDiary.Web.dll" <<'EOF'
import struct, sys
b = open(sys.argv[1], 'rb').read()
pe = struct.unpack_from('<I', b, 0x3c)[0]
print(format(struct.unpack_from('<I', b, pe + 8)[0], 'x'))
EOF
}

kennung_der_app() {
  curl -s -m 3 "$url/api/system/backend-health" | python3 -c "import json,sys; print(json.load(sys.stdin).get('bauKennung',''))" 2>/dev/null
}

case "$befehl" in
  start)
    if pid="$(pid_am_port)" && [ -n "$pid" ]; then
      echo "Port $port ist belegt (PID $pid). Erst 'scripts/demo-app.sh stop $port' — sonst misst du gegen eine alte Instanz." >&2
      exit 2
    fi
    dn="$(dotnet_bin)"
    "$dn" build "$WURZEL/GrowDiary.Web" -v q --nologo -p:UseAppHost=false >/dev/null || { echo "Bau fehlgeschlagen." >&2; exit 2; }
    daten="/tmp/grow-os-demo-$port-$(date +%Y%m%d-%H%M%S)"
    mkdir -p "$daten"
    # Ganz abgekoppelt: `exec` statt eines wartenden Unterprozesses, und alle
    # drei Kanäle umgeleitet. Sonst hält der Hintergrund die Ausgabe-Pipe des
    # Aufrufers offen, und `scripts/demo-app.sh start | tail` kehrt nie zurück.
    (cd "$WURZEL/GrowDiary.Web" && exec env GROWDIARY_DATA_PATH="$daten" GROW_OS_DEMO=1 \
      ASPNETCORE_ENVIRONMENT=Development Hosting__DefaultUrls="$url" \
      nohup "$dn" bin/Debug/net8.0/GrowDiary.Web.dll >"$daten/app.log" 2>&1) </dev/null >/dev/null 2>&1 &
    for _ in $(seq 1 60); do
      [ -n "$(kennung_der_app)" ] && break
      sleep 2
    done
    app="$(kennung_der_app)"; dll="$(kennung_der_dll)"
    if [ -z "$app" ]; then echo "Die App antwortet nicht. Log: $daten/app.log" >&2; exit 2; fi
    if [ "$app" != "$dll" ]; then echo "Bau-Kennung der App ($app) ≠ gebaute DLL ($dll) — das ist nicht der gebaute Stand." >&2; exit 2; fi
    echo "Demo-App läuft: $url  (PID $(pid_am_port), Bau-Kennung $app)"
    echo "Daten: $daten   Prüfen: GROW_OS_URL=$url E2E_STRENG=1 npx playwright test"
    ;;
  stop)
    pid="$(pid_am_port)"
    if [ -z "$pid" ]; then echo "An Port $port hört nichts."; exit 0; fi
    kill "$pid" && echo "Beendet: PID $pid an Port $port."
    ;;
  status)
    pid="$(pid_am_port)"
    if [ -z "$pid" ]; then echo "An Port $port hört nichts."; exit 0; fi
    app="$(kennung_der_app)"; dll="$(kennung_der_dll 2>/dev/null || true)"
    echo "Port $port: PID $pid, Bau-Kennung ${app:-?} (gebaut: ${dll:-?})$( [ -n "$app" ] && [ "$app" = "$dll" ] && echo ' — gebauter Stand' || echo ' — NICHT der gebaute Stand')"
    ;;
  *)
    echo "Aufruf: scripts/demo-app.sh start|stop|status [PORT]" >&2
    exit 2
    ;;
esac
