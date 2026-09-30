---
description: Neue Version ausliefern — Tag pushen, release.yml erzwingt CI, Image, GHCR, dann erst hochzaehlen
---

Liefere eine neue Version aus. **Die Reihenfolge ist die Sache**, nicht die
einzelnen Befehle: der Docker-Bau führt keine Tests aus, ein Image aus rotem
Quelltext sieht genauso aus wie eines aus grünem.

Seit forkai.153 erzwingt **`.github/workflows/release.yml`** diese Reihenfolge
selbst. Wer released, pusht einen Tag — mehr nicht. Bis forkai.152 lief das von
Hand und nacheinander: rund 20 Minuten, und jede Stufe hing daran, dass jemand
die vorige wirklich angesehen hatte.

## Schritt 0 — nachsehen, wo wir stehen

```
head -2 grow-os/config.yaml
git log --oneline -1 -- grow-os/config.yaml
git log --oneline origin/main..HEAD
```

Die neue Nummer ist die alte plus eins. **Nicht raten** — lesen.

## Schritt 1 — Changelog-Eintrag in den Commit

`grow-os/CHANGELOG.md` bekommt oben einen Abschnitt `## <X>`. `config.yaml`
wird **nicht** angefasst — das macht release.yml, und zwar zuletzt.

**Der Changelog ist auf DEUTSCH.** Bis beta.58 war er englisch — mit dem
Gedanken, er richte sich an Fremde. Der Nutzer hat das am 28.08.2026
widerrufen: „einmal müssen die Release Notes auf Deutsch sein, weil das unsere
Hauptsprache ist." Home Assistant zeigt genau diesen Text beim Update an, und
wer aktualisiert, ist kein Fremder.

Die Einträge vor beta.58 bleiben englisch; sie sind Geschichte. Gehalten wird
das von `src/release-notes-deutsch.node.test.ts` — die Prüfung liest den
NEUESTEN Eintrag und meldet englische Wendungen.

Schreibe darin, **was der Nutzer merkt**, nicht was im Code steht. Je Punkt:
was war, warum es passierte, und woran man es misst. Keine Aufzählung ohne
Substanz.

Der Eintrag darf schon vor dem Hochzählen im Repository stehen: Home Assistant
zeigt ihn erst an, wenn `config.yaml` die Nummer trägt.

## Schritt 2 — Tag pushen

**Ausnahme: Der Release ändert `.github/workflows/`.** Solche Commits darf der
GITHUB_TOKEN nicht nach `main` schieben — GitHub lehnt das ab, und das Recht
dazu kann der Token nicht bekommen (Befund des Prüfers vor forkai.153). Dann
zuerst die CI am Branch grün abwarten, `main` von Hand auf genau diesen Commit
vorspulen, **danach** taggen. release.yml prüft das als Erstes und bricht vor
dem Image-Bau ab, wenn es vergessen wurde.

**Aus einer Claude-Sitzung: Release-Branch.** GitHub lehnt Tag-Pushes aus
einer Claude-Sitzung mit HTTP 403 ab (30.09.2026) — Branches gehen:

```
git push origin HEAD:release/<X>
```

Der Workflow legt den Tag `v<X>` am Ende selbst an und löscht den Branch.

**Von Hand geht weiterhin der Tag:**

```
git tag -a v<X> -m "<X>"
git push origin v<X>
```

Dann läuft release.yml:

1. **Gleichzeitig:** die Prüfung und der Image-Bau (`docker-publish.yml`, ohne
   `latest`). Geprüft wird nur, wenn es für **genau diesen Commit** noch keinen
   grünen CI-Lauf gibt — ein Push-Lauf fährt die `ci.yml` aus demselben Commit,
   das ist dasselbe Tor. Läuft die CI noch, wartet release.yml darauf. Arbeits-
   und Release-Branch dürfen also direkt nacheinander gepusht werden; ein
   Release dauert dann nur noch Image + Veröffentlichen (~3 Minuten nach
   grüner CI). Seit forkai.155.
2. **Erst wenn beides grün ist:** Manifest **anonym** abrufen (HTTP 200,
   amd64 · arm64 · arm/v7), `config.yaml` auf `<X>` setzen, nach `main`
   **vorspulen** (nie überschreiben), dann `latest` setzen.

Ist die CI rot, wird nichts hochgezählt. Das Image unter `<X>` liegt dann in
GHCR, wird aber niemandem angeboten. Reparieren, den Tag auf den neuen Commit
setzen (`git tag -fa v<X>`, `git push -f origin v<X>`) — der nächste Lauf
überschreibt das Image unter derselben Nummer.

## Schritt 3 — hinsehen

Dem Workflow glauben reicht nicht:

- Den Release-Lauf bis `veroeffentlichen` grün ansehen.
- `git fetch origin main && git show origin/main:grow-os/config.yaml | head -2`
  — steht dort `<X>`?
- In Home Assistant den Add-on-Store neu einlesen lassen („Nach Updates
  suchen"), sonst erscheint das Update erst beim nächsten Abgleich des
  Supervisors (alle paar Stunden).

Nenne der Nutzerin oder dem Nutzer die Lauf-Nummer — dann ist die Reihenfolge
belegt und nicht behauptet.
