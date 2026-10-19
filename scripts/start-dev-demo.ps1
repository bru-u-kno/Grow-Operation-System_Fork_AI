<#
.SYNOPSIS
    Startet Cultivate Control lokal mit Testdaten — für den Entwicklungsrechner.

.DESCRIPTION
    Auf dem Entwicklungsrechner gibt es kein Zelt, keine Sonden und kein Home
    Assistant. Ohne Werte lässt sich dort nichts prüfen: keine Ampelfarben,
    keine Kurven, keine Alarme, keine Dosier-Vorschläge.

    Dieser Modus liefert erfundene, aber plausible Messwerte — bewegt, damit
    Trends sichtbar sind. pH und EC driften über den Tag nach oben, der
    Füllstand sinkt: so gibt es etwas zu korrigieren, und die Dosierung lässt
    sich gegen eine echte Abweichung prüfen.

    Beim ersten Start werden 24 Stunden Verlauf nachgetragen, damit die Kurven
    sofort etwas zeigen.

    Die Oberfläche trägt durchgehend einen Streifen „Testdaten". Der Modus
    hängt allein an dieser Umgebungsvariablen und lässt sich in der Oberfläche
    nicht einschalten — erfundene Messwerte im Betrieb wären nicht bloß falsch,
    an ihnen hängen Alarme und die Dosierung.

.EXAMPLE
    .\scripts\start-dev-demo.ps1
    Startet auf http://localhost:5076 mit Testdaten.

.EXAMPLE
    .\scripts\start-dev-demo.ps1 -Port 5099
    Startet auf einem anderen Port, etwa neben einer zweiten Instanz.
#>
[CmdletBinding()]
param(
    [int]$Port = 5076
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

Write-Host ""
Write-Host "  Cultivate Control - TESTDATEN" -ForegroundColor Cyan
Write-Host "  Alle Messwerte sind erfunden. Kein Home Assistant, es wird nichts geschaltet." -ForegroundColor DarkGray
Write-Host "  http://localhost:$Port" -ForegroundColor Cyan
Write-Host ""

$env:GROW_OS_DEMO = '1'
$env:Hosting__DefaultUrls = "http://localhost:$Port"

try {
    Push-Location $repoRoot
    dotnet run --project GrowDiary.Web -c Release --no-launch-profile
}
finally {
    Pop-Location
    Remove-Item Env:\GROW_OS_DEMO -ErrorAction SilentlyContinue
    Remove-Item Env:\Hosting__DefaultUrls -ErrorAction SilentlyContinue
}
