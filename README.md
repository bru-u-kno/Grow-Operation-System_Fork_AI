# Grow OS — the RDWC/DWC grow add-on for Home Assistant

> **Fork AI:** Dies ist ein Ableger von Grow OS (Nerdstreak). Was hier gegenüber dem Original neu ist, steht in [FORK.md](FORK.md).

**English** · [Deutsch](README.de.md)

**Turn your Home Assistant sensors into a real grow-management cockpit.** Grow OS is a
free, local-first Home Assistant add-on for hydroponic (RDWC/DWC) growers: a live
instrument dashboard, grow documentation, SOPs, hardware & maintenance tracking,
sensor-driven diagnosis, and risk alerts — all running inside Home Assistant, all on
your own hardware. No cloud, no account, no SaaS.

<p align="center">
  <img src="docs/images/live-dashboard-desktop.png" alt="Grow OS live dashboard" width="100%">
</p>

## Why Grow OS

- **Home Assistant native.** It reads your existing HA entities, so *any* sensor HA
  supports works — pH, EC, water temp, DO, ORP, CO₂, PPFD, tent climate, cameras. No
  proprietary hardware.
- **Built for recirculating hydro (RDWC/DWC).** Reservoir, addback, water changes,
  targets per phase, and a diagnosis engine that maps deviations to symptoms and
  recommended treatments/SOPs.
- **One-click install, zero config.** As an add-on it connects to Home Assistant
  automatically — no URL, no token. Pick your sensors from a dropdown of your real
  entities.
- **Local-first.** Your data stays on your device and is included in Home Assistant's
  backups. Nothing leaves your network.

## Features

- 📊 **Live instrument dashboard** — climate, VPD, reservoir, light status and a
  system score at a glance, with a near-live tent camera. Arrange the tiles the way
  you read them; each one carries its 24 h curve and says where its target came from.
- 💧 **Addback & water-change assistant** — measure, target, dose, re-check; nothing
  blind.
- ⚗️ **Dosing pumps** — drive peristaltic pumps for pH and nutrients through Home
  Assistant. Calibrate by volume (run until 100 ml is in the cup), then dose in
  millilitres. Every dose is capped, spaced by a mixing pause and logged; nothing
  runs on its own.
- 🎚️ **Setpoints that are yours** — shipped per-phase targets for RDWC and DWC, a
  threshold you type beats them everywhere, and you can write your own profile from
  your own experience. Only what you change becomes yours; the rest keeps receiving
  updates.
- 🔔 **Threshold alerts** — push straight to your phone through Home Assistant when a
  value leaves the band you set.
- 🧪 **Auto-measurements** — capture sensor readings (and camera snapshots) on a
  trigger, e.g. *30 min after lights-on*.
- 🩺 **Diagnosis & risk tracking** — deviations become symptoms with likely causes and
  linked treatments/SOPs; power/pump/DO emergencies get guided recovery SOPs.
- 🐕 **Watchdog** — tells you when Grow OS itself has gone quiet, per tent, so a tent
  that stopped reporting can't hide behind one that still does.
- 📚 **Knowledge base** — searchable guides, SOPs, treatments, symptoms, pathogens,
  target values and nutrient programs.
- 🔧 **Hardware & maintenance** — lifespan, inspection and per-sensor calibration
  reminders.
- 📱 **Mobile-friendly** — works right inside the Home Assistant app.

<p align="center">
  <img src="docs/images/live-dashboard-mobile.png" alt="Grow OS on mobile" width="260">
</p>

## Screenshots

<table>
  <tr>
    <td width="50%"><img src="docs/images/diagnosis.png" alt="Deviation diagnosis with suggested treatments and SOPs"><br><sub><b>Diagnosis</b> — deviations mapped to symptoms with suggested treatments/SOPs</sub></td>
    <td width="50%"><img src="docs/images/automation.png" alt="Sensor auto-measurements and camera snapshots on triggers"><br><sub><b>Automation</b> — auto-measurements & camera snapshots on triggers (e.g. 30 min after lights-on)</sub></td>
  </tr>
  <tr>
    <td width="50%"><img src="docs/images/knowledge.png" alt="Searchable knowledge base"><br><sub><b>Knowledge base</b> — searchable SOPs, treatments, symptoms and target values</sub></td>
    <td width="50%"><img src="docs/images/getting-started.png" alt="Guided onboarding"><br><sub><b>Getting started</b> — guided onboarding that surfaces every feature</sub></td>
  </tr>
</table>

## Install (Home Assistant add-on)

**Requires Home Assistant OS or Supervised** (add-ons aren't available on Home
Assistant Container/Core).

> 🍓 **Starting from scratch?** If you don't have Home Assistant yet, the
> [step-by-step Raspberry Pi guide](docs/pi-setup.md) (German) takes you from a blank
> SD card to Grow OS running — no prior experience needed.

1. In Home Assistant: **Settings → Add-ons → Add-on Store**.
2. Top-right **⋮ → Repositories**, and add:

   ```
   https://github.com/Nerdstreak/Grow-Operation-System
   ```

3. **Grow OS** appears in the store → **Install** → **Start**.
4. On the add-on's **Info** page, enable **"Show in sidebar"** so Grow OS appears in
   the Home Assistant sidebar (🌱).
5. Open it from the sidebar. It's already connected to Home Assistant — just pick your
   sensors from the dropdown.

Updates are a clean, one-click pull from Home Assistant; your data is preserved.

## Documentation

The docs live under [docs/](docs/) (currently in German):

- [Set up a Raspberry Pi from scratch](docs/pi-setup.md)
- [Installation](docs/install.md)
- [Architecture](docs/architecture.md)
- [Grow domain notes](docs/grow-domain-notes.md)
- [Development](docs/development.md)

## License

Released under the [MIT License](LICENSE) — free to use, modify and redistribute.
