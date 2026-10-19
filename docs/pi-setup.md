# Cultivate Control von Grund auf einrichten — Raspberry Pi + Home Assistant

Du hast einen Raspberry Pi und eine leere SD-Karte? Diese Anleitung führt dich Schritt
für Schritt bis Cultivate Control in deinem Browser läuft — **ohne Vorwissen, ohne Terminal**.

**Der Weg:** 🍓 Raspberry Pi → 🏠 Home Assistant → 🌱 Cultivate Control
**Gesamtdauer:** ca. 45 Minuten — davon rund 20 Minuten automatisches Warten, in denen du
nichts tun musst.

> Wenn Home Assistant bei dir **schon läuft**, überspring die Schritte 1–3 und geh direkt
> zu [Schritt 4](#schritt-4--grow-os-installieren). Die reine Add-on-Installation steht
> auch kompakt in der [Installationsanleitung](install.md).

---

## Was du brauchst

- **Raspberry Pi 4 (4 GB+) oder Pi 5** — empfohlen. Kleinere/ältere Modelle laufen sehr
  zäh mit Home Assistant.
- **Deine SD-Karte — mind. 32 GB, Markenqualität** (SanDisk oder Samsung). SD-Karten
  verschleißen mit der Zeit; eine SSD per USB ist langlebiger, aber zum Starten reicht die
  Karte völlig.
- **Das originale Pi-Netzteil** (Pi 5: 27 W USB-C · Pi 4: 15 W USB-C). Billige
  Handy-Ladegeräte verursachen Abstürze — beim Dauerbetrieb wirklich wichtig.
- **Ein LAN-Kabel vom Pi zum Router** — dringend empfohlen, macht die Einrichtung am
  einfachsten. WLAN geht auch, ist aber fummeliger.
- **Einen Computer + SD-Karten-Leser** (Windows oder Mac), um die Karte einmalig zu
  beschreiben.

---

## Schritt 1 — Home Assistant auf die SD-Karte spielen

*⏱ ca. 10 Minuten*

1. Am Computer den **„Raspberry Pi Imager"** installieren — das offizielle Programm zum
   Beschreiben der Karte:
   ```
   https://www.raspberrypi.com/software/
   ```
2. **SD-Karte in den Kartenleser** am Computer stecken und den Imager öffnen.
3. **„Choose Device" → dein Pi-Modell** auswählen (z. B. Raspberry Pi 5).
4. **„Choose OS" → nach unten scrollen** zu *„Other specific-purpose OS" → „Home
   assistants and home automation" → „Home Assistant"* und die oberste (empfohlene)
   Version wählen.
5. **„Choose Storage" → deine SD-Karte** auswählen.
6. **„Write" / „Schreiben"** klicken und bestätigen. Fertig, wenn *„Write Successful"*
   erscheint.

> ⚠️ **Wichtig:** Beim Schreiben wird **alles auf der SD-Karte gelöscht**. Achte darauf,
> dass wirklich die SD-Karte ausgewählt ist — nicht versehentlich eine andere Festplatte.

> 💡 **Tipp:** Ein Passwort oder WLAN musst du hier **nicht** eintragen — das richtest du
> später bequem im Browser ein.

---

## Schritt 2 — Pi anschließen und starten

*⏱ 2 Minuten Aufbau + ca. 20 Minuten warten*

1. **SD-Karte in den Pi stecken** (der Slot ist an der Unterseite).
2. **LAN-Kabel einstecken:** Pi mit dem Router (oder einem Switch) verbinden.
3. **Zuletzt das Netzteil einstecken.** Der Pi startet automatisch — es gibt keinen
   An/Aus-Schalter.
4. **Jetzt warten.** Beim allerersten Start lädt und installiert Home Assistant sich
   selbst. Das dauert etwa 20 Minuten.

> ⚠️ **Wichtig:** In diesen ~20 Minuten den **Strom nicht abziehen**, auch wenn scheinbar
> nichts passiert. Der Pi arbeitet im Hintergrund.

---

## Schritt 3 — Home Assistant einrichten

*⏱ ca. 5 Minuten*

1. **Am Computer oder Handy** (im **gleichen Netzwerk**) den Browser öffnen und diese
   Adresse eingeben:
   ```
   http://homeassistant.local:8123
   ```
2. **Auf den Willkommens-Bildschirm warten** und *„Neue Installation erstellen"* wählen.
3. **Konto anlegen:** Name, Benutzername und Passwort. **Gut notieren** — das ist dein
   Zugang.
4. **Standort und Zeitzone** festlegen (sinnvoll für Lichtzeiten) und weiter bis zum
   Dashboard.

> 💡 **Falls die Seite nicht lädt:** Noch 5 Minuten warten (der Pi installiert evtl. noch),
> dann neu laden. Wenn `homeassistant.local` partout nicht geht, die IP-Adresse des Pi im
> Router nachschauen und stattdessen `http://<IP-des-Pi>:8123` eingeben.

---

## Schritt 4 — Cultivate Control installieren

*⏱ ca. 3 Minuten*

1. In Home Assistant: **Einstellungen → Add-ons → Add-on-Store** (unten rechts).
2. Oben rechts das **⋮-Menü → „Repositories"**.
3. Diese Adresse einfügen und auf *„Hinzufügen"*, danach das Fenster schließen:
   ```
   https://github.com/bru-u-kno/cultivate-control
   ```
4. **Im Store nach unten scrollen** → **Cultivate Control** anklicken → *„Installieren"*.
5. Danach **„Starten"** klicken.
6. Oben auf den Reiter **„Info"** und den Schalter **„In Seitenleiste anzeigen"**
   aktivieren.
7. Links in der Seitenleiste erscheint jetzt **🌱 Cultivate Control** — anklicken, fertig. Es ist
   bereits automatisch mit Home Assistant verbunden.

---

## Schritt 5 — Sensoren verbinden & am Handy nutzen

1. **Sensoren zuerst in Home Assistant hinzufügen.** Damit Cultivate Control Werte anzeigt, müssen
   deine Sensoren in Home Assistant auftauchen — unter **Einstellungen → Geräte & Dienste**
   (je nach Hardware, z. B. WLAN-, Zigbee- oder ESPHome-Sensoren).
2. **In Cultivate Control die Sensoren zuordnen.** Sobald ein Sensor in Home Assistant sichtbar ist,
   wählst du ihn in Cultivate Control beim Zelt-Mapping einfach aus dem **Dropdown** (pH, EC, DO, ORP,
   Wassertemperatur …).
3. **Fürs Handy:** im App-Store die App **„Home Assistant"** installieren, mit deinem Konto
   anmelden — Cultivate Control ist dann auch mobil in der Seitenleiste.

> 💡 **Gut zu wissen:** Deine Daten liegen komplett lokal auf dem Pi und werden von den
> Home-Assistant-Backups automatisch mitgesichert. Updates von Cultivate Control holst du später mit
> einem Klick über den Add-on-Store.

---

## Wenn etwas klemmt

- **Die Seite `homeassistant.local:8123` lädt nicht** — meist ist der erste Start noch
  nicht fertig; 5–10 Minuten warten und neu laden. Manche Netzwerke mögen „.local"-Adressen
  nicht: dann die IP-Adresse des Pi im Router nachsehen und `http://<IP>:8123` verwenden.
- **Cultivate Control taucht nicht im Add-on-Store auf** — oben rechts ⋮ → „Nach Updates suchen",
  Seite neu laden. Hilft das nicht: **Einstellungen → System → ⋮ → „Supervisor neu
  starten"** und kurz warten.
- **Der Pi startet nicht, hängt oder wird instabil** — fast immer das Netzteil. Verwende
  das **originale Raspberry-Pi-Netzteil** mit genug Leistung; Handy-Ladegeräte reichen oft
  nicht.
- **In Cultivate Control fehlen Sensorwerte oder das Kamerabild** — prüfe zuerst, ob der Sensor bzw.
  die Kamera in Home Assistant selbst sichtbar ist (Geräte & Dienste). Cultivate Control zeigt nur,
  was in Home Assistant vorhanden und im Zelt-Mapping zugeordnet ist.

---

Fragen oder ein Fehler? Melde dich als
[GitHub-Issue](https://github.com/bru-u-kno/cultivate-control/issues).
