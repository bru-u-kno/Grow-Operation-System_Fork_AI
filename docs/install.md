# Cultivate Control installieren

Cultivate Control ist ein **Home-Assistant-Add-on**. Die Installation dauert unter einer Minute
und braucht **keine Programmierkenntnisse**.

> Entwickler, die den Quellcode bauen wollen, finden alles unter [setup.md](setup.md).

## So sieht Cultivate Control aus

<p align="center">
  <img src="images/live-dashboard-desktop.png" alt="Live-Dashboard am PC" width="100%">
</p>
<p align="center">
  <img src="images/live-dashboard-mobile.png" alt="Live-Ansicht am Handy" width="260">
</p>

---

## Voraussetzung

Eine **Home Assistant OS**- oder **Supervised**-Installation. Bei Home Assistant
Container/Core gibt es keine Add-ons.

Cultivate Control bezieht seine Sensordaten aus Home Assistant — deshalb läuft alles direkt als
Add-on *in* Home Assistant, ohne separaten Server und ohne manuelle Verbindung.

## Schritt 1 — Repository hinzufügen

In Home Assistant: **Einstellungen → Add-ons → Add-on-Store**, oben rechts
**⋮ → Repositories**, und diese Adresse einfügen:

```
https://github.com/bru-u-kno/cultivate-control
```

## Schritt 2 — Installieren und starten

**Cultivate Control** erscheint im Store → **Installieren** → **Starten**.

## Schritt 3 — In der Seitenleiste anzeigen

Damit Cultivate Control in der **Seitenleiste** auftaucht, auf der **Info**-Seite des Add-ons den
Schalter **„In Seitenleiste anzeigen"** aktivieren. Danach erscheint das Symbol 🌱 in
der linken Leiste.

## Optional — die Titelleiste von Home Assistant ausblenden

Über jedem Add-on zeichnet Home Assistant seine eigene Titelleiste (Menü-Symbol und Name). Ein Add-on kann sie nicht abschalten.
Am Handy kostet sie Platz; **alles funktioniert aber auch mit ihr**, dieser Schritt ist freiwillig.

Wer sie loswerden will, richtet einmal ein eigenes Panel ein. Die App führt dich dabei: Öffne **Handy → App einrichten**. Steht dort
die Karte **„Ohne HA-Titelleiste“**, folge den drei Schritten:

1. In HACS die Integration **Ingress** ([`lovelylain/hass_ingress`](https://github.com/lovelylain/hass_ingress)) herunterladen und Home Assistant neu starten.
2. Den Block aus der Karte in die `configuration.yaml` einfügen. Die Kennung deiner Installation steht schon darin. Hast du
   unter `ingress:` bereits einen Eintrag für diese App, änderst du nur seine Zeile `url:`.
3. Home Assistant neu starten. Es erscheint ein Eintrag „Cultivate Control“ in der Seitenleiste, ohne Titelleiste. Den normalen
   Eintrag des Add-ons kannst du dann auf der Add-on-Seite unter „In der Seitenleiste anzeigen“ ausschalten.

Die App schreibt nichts in deine Konfiguration und installiert nichts; sie zeigt nur den fertigen Text.

## Schritt 4 — Öffnen und Sensoren wählen

Cultivate Control ist **sofort mit Home Assistant verbunden** — keine URL, kein Token. Beim
Zelt-Mapping
wählst du deine Sensoren bequem aus einem **Dropdown** deiner echten HA-Entitäten
(kein Abtippen von Entity-IDs).

> Home Assistant verwaltet die Updates und sichert die Grow-OS-Daten in seinen eigenen
> Backups automatisch mit. Updates sind ein sauberer Ein-Klick-Pull — deine Daten
> bleiben erhalten.

---

## Kamera einbinden (optional)

Cultivate Control zeigt das Live-Bild jeder Kamera-Entity, die in Home Assistant verfügbar ist
(z. B. eine USB-Webcam über [go2rtc](https://github.com/AlexxIT/go2rtc)). Beim
Zelt-Mapping die Kamera-Entity aus dem Dropdown wählen und „Kamera testen".

## Probleme?

- **Add-on erscheint nicht im Store?** ⋮ → „Nach Updates suchen", Seite neu laden;
  notfalls den **Supervisor neu starten** (Einstellungen → System).
- **Kein Bild / Fehler?** Add-on-Tab **Protokoll** ansehen; nach Änderungen
  **Neu starten**. Prüfe, ob deine Entitäten in Home Assistant sichtbar sind.
- Fragen oder Fehler bitte als
  [GitHub Issue](https://github.com/bru-u-kno/cultivate-control/issues) melden.
