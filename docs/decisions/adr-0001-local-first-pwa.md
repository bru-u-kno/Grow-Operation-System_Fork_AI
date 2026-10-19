# ADR 0001: Lokale PWA statt nativer App

## Status

Teilweise abgeloest. Die Grundentscheidung (lokale Web-App statt nativer App-Store-App)
gilt weiter. Die Verteilung erfolgt inzwischen ausschliesslich als Home-Assistant-Add-on:
kein eigenstaendiger Selfhost-Betrieb, kein eigener Remote-Zugriff und keine PWA-Installation
mehr — Cultivate Control laeuft in Home Assistant und wird ueber dessen Oberflaeche (inkl. Mobil-App)
erreicht. Die folgenden Abschnitte sind der historische Original-Kontext.

## Kontext

Cultivate Control soll kostenlos, selfhosted und datenschutzfreundlich bleiben. Zielnutzer betreiben die App lokal, im Heimnetz oder ueber bewusst abgesicherten Remote-Zugriff. Mobile Nutzung ist wichtig, aber ein App-Store-Release wuerde zusaetzliche Abhaengigkeiten, Kosten und Review-Prozesse erzeugen.

## Entscheidung

Cultivate Control wird als lokale Web-App mit PWA-Unterstuetzung gebaut, nicht als native iOS- oder Android-App.

## Konsequenzen

- Nutzer koennen die App ohne App Store auf dem Home Screen installieren.
- Der Standardbetrieb bleibt lokal oder im eigenen Netzwerk.
- Remote-Zugriff kann optional ueber VPN/Tailscale oder Reverse Proxy mit HTTPS und Auth erfolgen.
- Es gibt kein vollstaendiges Offline-Versprechen: API-Daten, Home Assistant Livewerte, Speichern, Uploads und Snapshots benoetigen Verbindung zum Grow-OS-Server.
- Browser- und Plattformunterschiede bei PWA-Installation muessen dokumentiert werden.

## Nicht-Ziele

- keine native App-Store-App
- keine Cloud-Pflicht
- kein SaaS-Modell
- keine vollstaendige Offline-Synchronisation im aktuellen Stand
