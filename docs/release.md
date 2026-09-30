# Release-Ablauf

**Die Reihenfolge ist der ganze Punkt dieses Dokuments.**

Home Assistant liest `grow-os/config.yaml` direkt aus dem Repository. In dem Moment, in dem
ein Versions-Bump auf `main` landet, bietet jede Installation das Update an — unabhängig
davon, ob das Image überhaupt existiert. Wer in diesem Fenster klickt, bekommt:

```
Could not pull image to update app …: [404] failed to resolve reference
"ghcr.io/nerdstreak/grow-operation-system:1.8.2": not found
```

Am 2026-07-26 ist genau das passiert: `config.yaml` um 00:59:23 gepusht, das Image erst um
01:04:50 fertig. Fünfeinhalb Minuten Lücke, drei fehlgeschlagene Versuche beim Nutzer.

Ironie am Rand: Das Fenster war vorher kleiner. Es wurde größer, als nach dem 1.6.1-Vorfall
die Regel „erst CI abwarten, dann Image bauen" eingeführt wurde — die Korrektur für ein
Problem hat ein anderes vergrößert. Deshalb steht hier jetzt der ganze Ablauf.

## Ablauf

Seit forkai.153 erzwingt `.github/workflows/release.yml` diese Reihenfolge — nicht mehr
die Sorgfalt dessen, der released. Die Schritte von Hand (CI abwarten, Image anstoßen,
Manifest abfragen, hochzählen) liefen bis forkai.152 nacheinander und dauerten rund
20 Minuten.

**1. Code und Changelog-Eintrag committen — ohne Versionsnummer.**
`grow-os/CHANGELOG.md` bekommt den Abschnitt `## X`; `config.yaml` bleibt, wie es ist.
Home Assistant zeigt den Changelog erst, wenn `config.yaml` die Nummer trägt.

**2. Tag pushen.**

```bash
git tag -a v2.0.0-forkai.153 -m "2.0.0-forkai.153"
git push origin v2.0.0-forkai.153
```

**3. release.yml erledigt den Rest — in dieser Reihenfolge:**

- CI (`ci.yml`, alle Prüfungen) und Image-Bau laufen **gleichzeitig**. Das Image liegt
  danach unter seiner Nummer in GHCR, wird aber noch niemandem angeboten.
- **Erst wenn beides grün ist:** Manifest anonym abrufen (HTTP 200, amd64 · arm64 · arm/v7),
  `config.yaml` hochzählen, nach `main` vorspulen, `latest` setzen.

Damit ist das Fenster vom 2026-07-26 zu: `config.yaml` erreicht `main` erst, wenn das Image
nachweislich abrufbar ist. Ist die CI rot, wird nichts hochgezählt.

**4. Hinsehen.** Den Lauf ansehen, `config.yaml` auf `main` lesen, in Home Assistant den
Add-on-Store neu einlesen lassen. Einzelheiten: `.claude/commands/release.md`.

## Was wann eine Versionsnummer bekommt

- **Patch** (1.8.2 → 1.8.3): Fehlerbehebungen, Textänderungen, Wissenseinträge
- **Minor** (1.8.x → 1.9.0): neue Funktionen
- Keine neun Releases an einem Tag. Änderungen sammeln.

## Offener Punkt

Der Supervisor warnt bei jeder Prüfung:

```
App config 'arch' uses deprecated values ['armv7'].
Please report this to the maintainer of Grow OS
```

`armv7` in `grow-os/config.yaml` ist abgekündigt. Das Entfernen würde 32-Bit-Installationen
ausschließen — eine Produktentscheidung, keine reine Aufräumarbeit, deshalb noch offen.
