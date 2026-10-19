# Drittlizenzen und Hinweise

Cultivate Control steht unter der MIT-Lizenz (siehe [LICENSE](LICENSE)): Copyright (c) 2026 Nerdstreak für das
Ursprungsprojekt, Änderungen und Erweiterungen seit dem Fork von bru-u-kno. Diese Datei nennt, was
darüber hinaus in der ausgelieferten App steckt und unter welchen Bedingungen. Die vollständigen Lizenztexte
liegen im Ordner [LICENSES](LICENSES) und in jedem Add-on-Image unter `/licenses`.

Stand: 19.10.2026, Version 2.0.0-cc.259. Geprüft wurden die Produktionsabhängigkeiten (Frontend: `package-lock.json`,
Backend: `project.assets.json`); Werkzeuge, die nur beim Entwickeln laufen (Test- und Build-Werkzeuge), gelangen nicht in die App.

## Frontend (läuft im Browser)

| Paket | Version | Lizenz | Urheber |
|---|---|---|---|
| react, react-dom, scheduler | 19.2.5 / 19.2.5 / 0.27.0 | MIT | Meta Platforms, Inc. and affiliates |
| react-router, react-router-dom | 7.14.2 | MIT | React Training LLC 2015-2019 |
| cookie | 1.1.1 | MIT | 2012-2014 Roman Shtylman |
| set-cookie-parser | 2.7.2 | MIT | 2015 Nathan Friedly |
| qrcode-generator | 2.0.4 | MIT | Kazuhiko Arase |

Der MIT-Lizenztext steht in [LICENSES/MIT.txt](LICENSES/MIT.txt); er gilt für jede Zeile oben mit dem dort genannten Urheber.

### Schriften (SIL Open Font License 1.1)

Die Schriften werden über die Pakete `@fontsource/*` mit der App ausgeliefert. Die Lizenz erlaubt Nutzung, Einbettung und
Weitergabe, auch kommerziell, solange die Schriften nicht für sich allein verkauft werden und dieser Hinweis erhalten bleibt.
Volltext: [LICENSES/OFL-1.1.txt](LICENSES/OFL-1.1.txt).

| Schrift | Paket / Version | Copyright |
|---|---|---|
| Archivo | @fontsource/archivo 5.3.0 | Copyright 2020 The Archivo Project Authors (https://github.com/Omnibus-Type/Archivo) |
| Inter | @fontsource/inter 5.2.8 | Copyright 2016 The Inter Project Authors (https://github.com/rsms/inter) |
| JetBrains Mono | @fontsource/jetbrains-mono 5.2.8 | Copyright 2020 The JetBrains Mono Project Authors (https://github.com/JetBrains/JetBrainsMono) |

## Backend (.NET)

| Paket | Lizenz | Urheber |
|---|---|---|
| Microsoft.Data.Sqlite, Microsoft.Extensions.* (Http, Logging, Options, DependencyInjection, Configuration, Caching, Diagnostics, Hosting, FileProviders, Primitives), System.Text.Json, System.Text.Encodings.Web, System.IO.Pipelines, System.Net.ServerSentEvents, System.Diagnostics.DiagnosticSource, System.Memory | MIT | © Microsoft Corporation |
| ModelContextProtocol, ModelContextProtocol.Core, ModelContextProtocol.AspNetCore (Grow-MCP-Server), Microsoft.Extensions.AI.Abstractions | Apache-2.0 bzw. MIT | © Model Context Protocol a Series of LF Projects, LLC bzw. © Microsoft Corporation |
| SQLitePCLRaw (core, provider, lib und bundle `e_sqlite3`) | Apache-2.0 | Copyright 2014-2023 SourceGear, LLC |

Apache-2.0 (Volltext: [LICENSES/Apache-2.0.txt](LICENSES/Apache-2.0.txt)) verlangt, den Lizenztext beizulegen und Änderungen an
den Paketen selbst zu kennzeichnen. Die Pakete werden unverändert benutzt.

**SQLite** (die Datenbank-Bibliothek in `e_sqlite3`) ist gemeinfrei (Public Domain); https://www.sqlite.org/copyright.html.

**Laufzeit und Basis-Image.** Das Add-on-Image baut auf `mcr.microsoft.com/dotnet/aspnet:8.0` auf (.NET: MIT, Microsoft; das
Betriebssystem-Image darunter bringt Pakete unter eigenen, überwiegend freien Lizenzen mit, deren Hinweise im Image liegen).

## Inhalte aus dem Ursprungsprojekt

Logo, Symbole und Beispielbilder, die bis 19.10.2026 in der App und in der Anleitung standen, stammen von Nerdstreak und
standen im Ursprungsprojekt unter derselben MIT-Lizenz. Das Logo und die Symbole der App wurden danach durch eigene ersetzt.

## Hersteller- und Markennamen

Die Wissensbasis nennt Düngerhersteller und Produkte (zum Beispiel Athena, Canna, SKX, Hydroponic Research) und beschreibt ihre
Fütterungsschemata mit eigenen Worten zur Orientierung. Die Namen sind Marken ihrer jeweiligen Inhaber und werden hier nur
beschreibend genannt. Cultivate Control steht in keiner Verbindung zu diesen Unternehmen und wird von ihnen weder gefördert noch
geprüft. Verbindlich sind immer die Angaben des Herstellers auf der Packung und in seinen Unterlagen; Dosierungen und Fristen
müssen am eigenen System geprüft werden. Home Assistant ist eine Marke der Open Home Foundation; Cultivate Control ist ein
unabhängiges Add-on und kein Produkt dieser Stiftung.

## Fehler oder Auslassungen melden

Fehlt hier etwas oder stimmt ein Eintrag nicht, bitte als Issue im Repository melden:
https://github.com/bru-u-kno/cultivate-control/issues
