using System.Text.Json.Nodes;

namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI: Findet in vorhandenen CO₂-Automationen die Stellen, die im Zweifel
/// offen bleiben, und schreibt nur diese Stellen um.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (30.09.2026).</b> forkai.153 hat die Vorlagen
/// abgesichert — aber nur Automationen, die der Fork selbst anlegt, bekommen
/// sie. Wer seine CO₂-Regelung vorher von Hand gebaut hat, behält alle vier
/// Schwachstellen. In der Anlage, aus der diese Vorlagen stammen, steckten sie
/// alle vier.</para>
/// <para><b>Warum nicht einfach die Vorlage darüberschreiben.</b> Die
/// handgebaute Dosierung kann mehr als die Vorlage: Reconnect-Schutz über die
/// geplante Ein-Zeit, ein Dosierfenster vor Licht-aus, eine Push-Meldung im
/// Wächter. Wer die Vorlage darüberlegt, nimmt das weg, ohne dass es jemand
/// merkt. Deshalb wird hier nur die schwache Stelle ersetzt, alles andere
/// bleibt, wie es ist.</para>
/// <para><b>Erkannt wird am Inhalt, nicht am Namen.</b> Eine Dosierung ist,
/// was in einer Schleife ein Ventil öffnet; ein Wächter ist, was nach einer
/// Wartezeit das Ventil schließt. So greift die Prüfung auch bei einer
/// Automation, die anders heißt als hier.</para>
/// <para><b>Keine neuen Einstellwerte.</b> Alle Zeiten kommen aus der
/// Automation selbst (etwa die 90 s des Wächters). Neu ist nur die Grenze
/// „ein Messwert über 0 ppm" — das ist keine Einstellung, sondern die
/// Bedingung dafür, dass überhaupt gemessen wird.</para>
/// <para>Alle Umbauten sind wiederholbar: eine abgesicherte Automation liefert
/// keinen Befund mehr, ein zweites Absichern ändert nichts.</para>
/// </remarks>
public static class Co2Absicherung
{
    /// <summary>Die Arten von Schwachstellen, die hier erkannt werden.</summary>
    public enum Art
    {
        /// <summary>Die Dosier-Schleife endet nur bei „off" — ein „nicht verfügbar" hält sie am Laufen.</summary>
        SchleifeHaeltBeiAusfall,
        /// <summary>Die Schleife öffnet das Ventil, ohne dass ein CO₂-Messwert vorliegt.</summary>
        OeffnetOhneMesswert,
        /// <summary>Der Wächter sieht einen Port nicht, der schon vor einem Neustart offen stand.</summary>
        WaechterUebersiehtNeustart,
        /// <summary>Eine Automation schaltet die Dosierung aus und gleich wieder ein — auch nach einem Not-Aus.</summary>
        NotAusWirdZurueckgenommen,
    }

    public sealed record Befund(Art Art, string Titel, string Erklaerung, bool Behebbar, string? Hinweis);

    /// <summary>Was die Prüfung über die Anlage wissen muss.</summary>
    /// <param name="Co2Sensor">Der zugeordnete CO₂-Fühler (Rolle <c>co2_sensor</c>) oder null.</param>
    /// <param name="Dosierungen">Die Entitäten der Dosier-Automationen.</param>
    public sealed record Rahmen(string? Co2Sensor, IReadOnlySet<string> Dosierungen);

    /// <summary>Kennzeichen der eingefügten Bausteine. Daran erkennt die Prüfung ihre eigene Arbeit.</summary>
    public const string SperreAlias = "Fork AI: ohne CO₂-Messwert nicht öffnen";
    public const string AbbruchAlias = "Fork AI: CO₂-Messwert fehlt";
    public const string NeustartKennung = "fork_ai_neustart";
    public const string TaktKennung = "fork_ai_takt";
    public const string WarAnVariable = "fork_ai_dosierung_war_an";
    public const string WiederEinAlias = "Fork AI: nur wieder einschalten, wenn sie vorher an war";

    // ------------------------------------------------------------ Prüfen

    /// <summary>Alle Schwachstellen einer Automation.</summary>
    public static IReadOnlyList<Befund> Pruefen(JsonObject config, Rahmen rahmen)
    {
        var befunde = new List<Befund>();

        var schleifen = DosierSchleifen(config).ToList();
        if (schleifen.Any(s => OffBedingungen(s["until"]).Any()))
        {
            befunde.Add(new Befund(Art.SchleifeHaeltBeiAusfall,
                "Dosier-Schleife läuft bei einem Ausfall weiter",
                "Die Schleife endet nur, wenn Bedarf, Licht oder Klima „aus“ melden. Meldet eines davon „nicht verfügbar“, "
                + "ist das nicht „aus“ — dann dosiert sie weiter, bis die Höchstzahl der Impulse erreicht ist.",
                true, null));
        }

        if (schleifen.Any(s => !HatSperre(s)))
        {
            var behebbar = !string.IsNullOrWhiteSpace(rahmen.Co2Sensor);
            befunde.Add(new Befund(Art.OeffnetOhneMesswert,
                "Ventil öffnet auch ohne CO₂-Messwert",
                "Schweigt der CO₂-Fühler, rechnet der Impuls-Bedarf mit 0 ppm und liefert die längste Impulsdauer. "
                + "Die Schleife öffnet das Ventil trotzdem. Nach dem Absichern gilt: kein Messwert, kein Gas.",
                behebbar,
                behebbar ? null : "Dafür muss unter „Rollen bearbeiten“ ein CO₂-Sensor zugeordnet sein."));
        }

        if (WaechterZweig(config) is { } w && !w.SiehtNeustart)
        {
            befunde.Add(new Befund(Art.WaechterUebersiehtNeustart,
                "Wächter übersieht einen Port, der vor einem Neustart offen stand",
                "Der Wächter reagiert nur, wenn der Port auf „an“ wechselt. Stand er vor einem Neustart von Home Assistant "
                + "schon offen, wechselt danach nichts mehr, und das Ventil bleibt offen. Nach dem Absichern sieht er zusätzlich beim "
                + "Start und jede Minute nach — mit derselben Wartezeit wie bisher.",
                true, null));
        }

        if (WiederEinSchritte(config, rahmen.Dosierungen).Any())
        {
            befunde.Add(new Befund(Art.NotAusWirdZurueckgenommen,
                "Not-Aus der Dosierung wird zurückgenommen",
                "Diese Automation schaltet die Dosierung aus, um einen laufenden Zyklus abzubrechen, und gleich danach wieder "
                + "ein. War die Dosierung vorher von Hand oder über „Automatik“ abgeschaltet, ist sie danach wieder an. "
                + "Nach dem Absichern wird sie nur wieder eingeschaltet, wenn sie vorher an war.",
                true, null));
        }

        return befunde;
    }

    /// <summary>Ist das eine Dosierung — öffnet sie in einer Schleife ein Ventil?</summary>
    public static bool IstDosierung(JsonObject config) => DosierSchleifen(config).Any();

    // ------------------------------------------------------------ Absichern

    /// <summary>Eine abgesicherte Kopie. Nicht behebbare Befunde bleiben stehen.</summary>
    public static JsonObject Absichern(JsonObject config, Rahmen rahmen)
    {
        var kopie = (JsonObject)config.DeepClone();

        foreach (var schleife in DosierSchleifen(kopie).ToList())
        {
            foreach (var (liste, index, bedingung) in OffBedingungen(schleife["until"]).ToList())
            {
                liste[index] = NichtAn(bedingung);
            }

            if (!string.IsNullOrWhiteSpace(rahmen.Co2Sensor) && !HatSperre(schleife))
            {
                SperreEinbauen(schleife, rahmen.Co2Sensor!, kopie.ToJsonString());
            }
        }

        if (WaechterZweig(kopie) is { SiehtNeustart: false } w)
        {
            WaechterErweitern(kopie, w);
        }

        // Erst alle Stellen einfassen, dann die Variable davor setzen — sonst
        // verschiebt das Einfügen die übrigen Stellen.
        var wiederEin = WiederEinSchritte(kopie, rahmen.Dosierungen).ToList();
        foreach (var (liste, index) in wiederEin)
        {
            WiederEinBedingen(liste, index);
        }
        if (wiederEin.Count > 0)
        {
            WarAnMerken(wiederEin[0].Liste, rahmen.Dosierungen);
        }

        return kopie;
    }

    // ------------------------------------------------------------ Dosier-Schleife

    /// <summary>Die <c>repeat</c>-Blöcke, deren Ablauf ein Ventil öffnet.</summary>
    private static IEnumerable<JsonObject> DosierSchleifen(JsonNode? knoten)
    {
        foreach (var o in AlleObjekte(knoten))
        {
            if (o["repeat"] is JsonObject r && r["sequence"] is JsonArray ablauf && ablauf.Any(s => IstOeffnen(s as JsonObject)))
            {
                yield return r;
            }
        }
    }

    private static bool IstOeffnen(JsonObject? schritt)
    {
        var aktion = Aktion(schritt);
        return aktion switch
        {
            "select.select_option" => schritt?["data"]?["option"]?.ToString() == "On",
            "switch.turn_on" or "valve.open_valve" => true,
            _ => false,
        };
    }

    private static bool IstSchliessen(JsonObject? schritt)
    {
        var aktion = Aktion(schritt);
        return aktion switch
        {
            "select.select_option" => schritt?["data"]?["option"]?.ToString() == "Off",
            "switch.turn_off" or "valve.close_valve" => true,
            _ => false,
        };
    }

    /// <summary>
    /// Zustands-Bedingungen „ist off" im Abbruch, die direkt wirken (nicht unter
    /// einem <c>not</c>) — mit der Liste und Stelle, an der sie stehen.
    /// </summary>
    private static IEnumerable<(JsonArray Liste, int Index, JsonObject Bedingung)> OffBedingungen(JsonNode? knoten)
    {
        if (knoten is not JsonArray liste) yield break;
        for (var i = 0; i < liste.Count; i++)
        {
            if (liste[i] is not JsonObject b) continue;
            var art = b["condition"]?.ToString();
            if (art == "state" && b["state"] is JsonValue v && v.ToString() == "off")
            {
                yield return (liste, i, b);
            }
            else if (art is "or" or "and")
            {
                foreach (var t in OffBedingungen(b["conditions"])) yield return t;
            }
            // „not" ist genau die gewollte Form, darunter wird nichts umgedreht.
        }
    }

    /// <summary>„ist off" wird „ist nicht on" — damit zählt auch „nicht verfügbar".</summary>
    private static JsonObject NichtAn(JsonObject aus)
    {
        var an = (JsonObject)aus.DeepClone();
        an["state"] = "on";
        var alias = an["alias"];
        an.Remove("alias");

        var nicht = new JsonObject();
        if (alias is not null) nicht["alias"] = alias.DeepClone();
        nicht["condition"] = "not";
        nicht["conditions"] = new JsonArray(an);
        return nicht;
    }

    private static bool HatSperre(JsonObject schleife)
        => schleife["sequence"] is JsonArray ablauf
           && ablauf.OfType<JsonObject>().Any(s => s["alias"]?.ToString() == SperreAlias);

    /// <summary>
    /// Vor dem ersten Öffnen: kein Messwert, kein Gas. Und die Schleife endet,
    /// sobald der Messwert fehlt — sonst dreht sie leer bis zur Höchstzahl.
    /// </summary>
    private static void SperreEinbauen(JsonObject schleife, string co2Sensor, string ganzeAutomation)
    {
        var ablauf = (JsonArray)schleife["sequence"]!;
        var oeffnen = ablauf.Select((s, i) => (s, i)).First(t => IstOeffnen(t.s as JsonObject)).i;

        // Den Impuls-Bedarf nur prüfen, wenn die Automation ihn auch benutzt.
        var impuls = ganzeAutomation.Contains(Co2SteuerungService.Entitaeten.ImpulsBedarf, StringComparison.Ordinal)
            ? $" and states('{Co2SteuerungService.Entitaeten.ImpulsBedarf}') | int(0) > 0"
            : string.Empty;

        ablauf.Insert(oeffnen, new JsonObject
        {
            ["alias"] = SperreAlias,
            ["condition"] = "template",
            ["value_template"] = $"{{{{ states('{co2Sensor}') | float(-1) > 0{impuls} }}}}",
        });

        var abbruch = new JsonObject
        {
            ["alias"] = AbbruchAlias,
            ["condition"] = "template",
            ["value_template"] = $"{{{{ states('{co2Sensor}') | float(-1) <= 0 }}}}",
        };

        // Die Einträge von until gelten zusammen („und"). Ein einzelnes „oder"
        // bekommt den Abbruch dazu; sonst wird das Bisherige eingefasst.
        switch (schleife["until"])
        {
            case JsonArray { Count: 1 } bis when bis[0] is JsonObject o
                && o["condition"]?.ToString() == "or" && o["conditions"] is JsonArray oder:
                oder.Add(abbruch);
                break;
            case JsonArray bis:
                var bisher = new JsonArray(bis.Select(k => k?.DeepClone()).ToArray());
                bis.Clear();
                bis.Add(new JsonObject
                {
                    ["condition"] = "or",
                    ["conditions"] = new JsonArray(
                        new JsonObject { ["condition"] = "and", ["conditions"] = bisher },
                        abbruch),
                });
                break;
            default:
                // Eine Schleife ohne until (while/count) endet schon anders.
                break;
        }
    }

    // ------------------------------------------------------------ Wächter

    private sealed record Waechter(JsonObject Zweig, JsonObject AusloeserBedingung, IReadOnlyList<JsonObject> Lang, bool SiehtNeustart);

    /// <summary>
    /// Der Zweig eines Wächters: ausgelöst von Zustands-Auslösern mit Wartezeit
    /// („länger als …"), und er schließt das Ventil.
    /// </summary>
    private static Waechter? WaechterZweig(JsonObject config)
    {
        if (config["triggers"] is not JsonArray ausloeser) return null;

        var lang = ausloeser.OfType<JsonObject>()
            .Where(t => t["trigger"]?.ToString() == "state" && t["for"] is not null && t["id"] is not null && t["to"] is not null)
            .GroupBy(t => t["id"]!.ToString(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        if (lang.Count == 0) return null;

        foreach (var zweig in AlleObjekte(config["actions"]).Where(o => o["conditions"] is JsonArray && o["sequence"] is JsonArray))
        {
            if (!((JsonArray)zweig["sequence"]!).Any(s => IstSchliessen(s as JsonObject))) continue;

            var bedingung = ((JsonArray)zweig["conditions"]!).OfType<JsonObject>()
                .FirstOrDefault(b => b["condition"]?.ToString() == "trigger" && Kennungen(b["id"]).Any(lang.ContainsKey));
            if (bedingung is null) continue;

            var genutzt = Kennungen(bedingung["id"]).Where(lang.ContainsKey).Select(k => lang[k]).ToList();
            var text = zweig["conditions"]!.ToJsonString();
            var start = ausloeser.OfType<JsonObject>()
                .Where(t => t["trigger"]?.ToString() == "homeassistant" && t["event"]?.ToString() == "start")
                .Select(t => t["id"]?.ToString());
            var takt = ausloeser.OfType<JsonObject>()
                .Where(t => t["trigger"]?.ToString() == "time_pattern")
                .Select(t => t["id"]?.ToString());
            var sieht = start.Any(k => k is not null && text.Contains($"\"{k}\"", StringComparison.Ordinal))
                && takt.Any(k => k is not null && text.Contains($"\"{k}\"", StringComparison.Ordinal));

            return new Waechter(zweig, bedingung, genutzt, sieht);
        }

        return null;
    }

    private static IEnumerable<string> Kennungen(JsonNode? id) => id switch
    {
        JsonArray a => a.Select(k => k?.ToString()).OfType<string>(),
        JsonValue v => [v.ToString()],
        _ => [],
    };

    private static void WaechterErweitern(JsonObject config, Waechter w)
    {
        var ausloeser = (JsonArray)config["triggers"]!;
        ausloeser.Add(new JsonObject { ["trigger"] = "homeassistant", ["event"] = "start", ["id"] = NeustartKennung });
        ausloeser.Add(new JsonObject { ["trigger"] = "time_pattern", ["minutes"] = "/1", ["id"] = TaktKennung });

        // Beim Start: steht der Port offen, ist er verwaist — nach einem
        // Neustart läuft keine Dosierung weiter. Im Takt: offen seit mindestens
        // derselben Wartezeit wie die bisherigen Auslöser.
        JsonObject Zustand(JsonObject t, bool mitWartezeit)
        {
            var b = new JsonObject
            {
                ["condition"] = "state",
                ["entity_id"] = t["entity_id"]!.DeepClone(),
                ["state"] = t["to"]!.DeepClone(),
            };
            if (mitWartezeit) b["for"] = t["for"]!.DeepClone();
            return b;
        }

        JsonObject Oder(IEnumerable<JsonObject> teile) => new()
        {
            ["condition"] = "or",
            ["conditions"] = new JsonArray(teile.Cast<JsonNode>().ToArray()),
        };

        var bedingungen = (JsonArray)w.Zweig["conditions"]!;
        var stelle = bedingungen.IndexOf(w.AusloeserBedingung);
        var bisher = (JsonObject)w.AusloeserBedingung.DeepClone();

        bedingungen[stelle] = Oder(
        [
            bisher,
            new JsonObject
            {
                ["condition"] = "and",
                ["conditions"] = new JsonArray(
                    new JsonObject { ["condition"] = "trigger", ["id"] = new JsonArray(NeustartKennung) },
                    Oder(w.Lang.Select(t => Zustand(t, false)))),
            },
            new JsonObject
            {
                ["condition"] = "and",
                ["conditions"] = new JsonArray(
                    new JsonObject { ["condition"] = "trigger", ["id"] = new JsonArray(TaktKennung) },
                    Oder(w.Lang.Select(t => Zustand(t, true)))),
            },
        ]);
    }

    // ------------------------------------------------------------ Not-Aus

    /// <summary>
    /// Ein <c>automation.turn_on</c> einer Dosierung, dem in derselben Liste ein
    /// <c>turn_off</c> derselben Dosierung vorausgeht — das Muster „Zyklus
    /// abbrechen und wieder scharf schalten". Ein bloßes Einschalten (etwa
    /// morgens) ist gewollt und wird nicht angefasst.
    /// </summary>
    private static IEnumerable<(JsonArray Liste, int Index)> WiederEinSchritte(JsonObject config, IReadOnlySet<string> dosierungen)
    {
        if (config["actions"] is not JsonArray liste) yield break;

        var ausgeschaltet = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < liste.Count; i++)
        {
            if (liste[i] is not JsonObject s) continue;
            var ziele = Ziele(s).Where(dosierungen.Contains).ToList();
            if (ziele.Count == 0) continue;

            if (Aktion(s) == "automation.turn_off")
            {
                ausgeschaltet.UnionWith(ziele);
            }
            else if (Aktion(s) == "automation.turn_on" && ziele.Any(ausgeschaltet.Contains))
            {
                yield return (liste, i);
            }
        }
    }

    private static void WiederEinBedingen(JsonArray liste, int index)
    {
        var einschalten = liste[index]!;

        liste[index] = new JsonObject
        {
            ["alias"] = WiederEinAlias,
            ["if"] = new JsonArray(new JsonObject
            {
                ["condition"] = "template",
                ["value_template"] = $"{{{{ {WarAnVariable} }}}}",
            }),
            ["then"] = new JsonArray(einschalten.DeepClone()),
        };
    }

    /// <summary>Der Zustand VOR dem Ausschalten — als erster Schritt, einmal.</summary>
    private static void WarAnMerken(JsonArray liste, IReadOnlySet<string> dosierungen)
    {
        if (liste.OfType<JsonObject>().Any(s => s["variables"]?[WarAnVariable] is not null)) return;
        var dosierung = liste.OfType<JsonObject>()
            .Where(s => Aktion(s) == "automation.turn_off")
            .SelectMany(Ziele)
            .First(dosierungen.Contains);
        liste.Insert(0, new JsonObject
        {
            ["variables"] = new JsonObject
            {
                [WarAnVariable] = $"{{{{ is_state('{dosierung}', 'on') }}}}",
            },
        });
    }

    // ------------------------------------------------------------ Werkzeug

    private static string? Aktion(JsonObject? schritt)
        => (schritt?["action"] ?? schritt?["service"])?.ToString();

    private static IEnumerable<string> Ziele(JsonObject schritt)
    {
        var knoten = schritt["target"]?["entity_id"] ?? schritt["entity_id"] ?? schritt["data"]?["entity_id"];
        return knoten switch
        {
            JsonArray a => a.Select(k => k?.ToString()).OfType<string>(),
            JsonValue v => v.ToString().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            _ => [],
        };
    }

    private static IEnumerable<JsonObject> AlleObjekte(JsonNode? knoten)
    {
        switch (knoten)
        {
            case JsonObject o:
                yield return o;
                foreach (var p in o.ToList())
                foreach (var k in AlleObjekte(p.Value)) yield return k;
                break;
            case JsonArray a:
                foreach (var e in a.ToList())
                foreach (var k in AlleObjekte(e)) yield return k;
                break;
        }
    }
}
