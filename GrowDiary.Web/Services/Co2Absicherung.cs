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
/// Wächter, eine Rückfall-Impulsdauer. Wer die Vorlage darüberlegt, nimmt das
/// weg, ohne dass es jemand merkt. Deshalb wird hier nur die schwache Stelle
/// ersetzt, alles andere bleibt, wie es ist.</para>
/// <para><b>Erkannt wird am Inhalt, nicht am Namen.</b> Eine Dosierung ist,
/// was in einer Schleife ein Ventil öffnet; ein Wächter ist, was nach einer
/// Wartezeit den Dosier-Port schließt. So greift die Prüfung auch bei einer
/// Automation, die anders heißt als hier. Gelesen werden beide Schreibweisen
/// von Home Assistant (<c>triggers</c>/<c>actions</c> und die ältere
/// <c>trigger</c>/<c>action</c>/<c>platform</c>).</para>
/// <para><b>Nur wo die Umschreibung gleichwertig ist.</b> „ist off" wird nur
/// dann zu „ist nicht on", wenn das außer bei „nicht verfügbar" dasselbe
/// bedeutet: eine einzelne An/Aus-Entität ohne Wartezeit. Bei einem Klimagerät
/// („heat"/„cool"/„off") oder einer Wartezeit hieße es etwas anderes.</para>
/// <para><b>Keine neuen Einstellwerte.</b> Alle Zeiten kommen aus der
/// Automation selbst (etwa die 90 s des Wächters), der Fühler aus der Rolle.
/// Neu ist nur die Grenze „ein Messwert über 0 ppm" — das ist keine
/// Einstellung, sondern die Bedingung dafür, dass überhaupt gemessen wird.</para>
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
    /// <param name="Ports">Die Dosier-Steckdose (Rollen <c>port_schalter</c>, <c>port_zustand</c>), soweit zugeordnet.</param>
    public sealed record Rahmen(string? Co2Sensor, IReadOnlySet<string> Dosierungen, IReadOnlySet<string>? Ports = null);

    /// <summary>Kennzeichen der eingefügten Bausteine. Daran erkennt die Prüfung ihre eigene Arbeit.</summary>
    public const string SperreAlias = "Fork AI: ohne CO₂-Messwert nicht öffnen";
    public const string AbbruchAlias = "Fork AI: CO₂-Messwert fehlt";
    public const string NeustartKennung = "fork_ai_neustart";
    public const string TaktKennung = "fork_ai_takt";
    public const string WarAnPraefix = "fork_ai_war_an_";
    public const string WiederEinAlias = "Fork AI: nur wieder einschalten, wenn sie vorher an war";

    /// <summary>Bereiche, deren Zustand nur „on"/„off" (oder nicht verfügbar) ist.</summary>
    private static readonly HashSet<string> AnAusBereiche = new(StringComparer.Ordinal)
    {
        "binary_sensor", "input_boolean", "switch", "light", "fan", "automation",
    };

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

        if (WaechterFinden(config, rahmen, umwandeln: false) is { SiehtNeustart: false })
        {
            befunde.Add(new Befund(Art.WaechterUebersiehtNeustart,
                "Wächter übersieht einen Port, der vor einem Neustart offen stand",
                "Der Wächter reagiert nur, wenn der Port auf „an“ wechselt. Stand er vor einem Neustart von Home Assistant "
                + "schon offen, wechselt danach nichts mehr, und das Ventil bleibt offen. Nach dem Absichern sieht er zusätzlich beim "
                + "Start und jede Minute nach — mit derselben Wartezeit wie bisher.",
                true, null));
        }

        if (WiederEinSchritte(Liste(config, "actions", "action", umwandeln: false), rahmen.Dosierungen).Any())
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
                SperreEinbauen(schleife, rahmen.Co2Sensor!);
            }
        }

        if (WaechterFinden(kopie, rahmen, umwandeln: true) is { SiehtNeustart: false } w)
        {
            WaechterErweitern(kopie, w);
        }

        if (Liste(kopie, "actions", "action", umwandeln: true) is { } aktionen)
        {
            WiederEinBedingen(aktionen, rahmen.Dosierungen);
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
            // input_boolean: die Vorlage schaltet ein so zugeordnetes Ventil seit
            // 01.10.2026 mit turn_on/turn_off (SteuerungAutomationService.Fuellen).
            "switch.turn_on" or "input_boolean.turn_on" or "valve.open_valve" => true,
            _ => false,
        };
    }

    private static bool IstSchliessen(JsonObject? schritt)
    {
        var aktion = Aktion(schritt);
        return aktion switch
        {
            "select.select_option" => schritt?["data"]?["option"]?.ToString() == "Off",
            "switch.turn_off" or "input_boolean.turn_off" or "valve.close_valve" => true,
            _ => false,
        };
    }

    /// <summary>
    /// Zustands-Bedingungen „ist off" im Abbruch, die direkt wirken (nicht unter
    /// einem <c>not</c>) und sich gleichwertig umdrehen lassen — mit der Liste
    /// und Stelle, an der sie stehen.
    /// </summary>
    private static IEnumerable<(JsonArray Liste, int Index, JsonObject Bedingung)> OffBedingungen(JsonNode? knoten)
    {
        if (knoten is not JsonArray liste) yield break;
        for (var i = 0; i < liste.Count; i++)
        {
            if (liste[i] is not JsonObject b) continue;
            var art = b["condition"]?.ToString();
            if (art == "state" && Umdrehbar(b))
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

    /// <summary>
    /// „ist off" ≡ „ist nicht on" bis auf „nicht verfügbar" — nur für eine
    /// einzelne An/Aus-Entität ohne Wartezeit und ohne Attribut. Mit Wartezeit
    /// („seit 2 min off") wäre „nicht seit 2 min on" kurz nach dem Einschalten
    /// wahr; eine Liste hieße danach „irgendeine" statt „alle".
    /// </summary>
    private static bool Umdrehbar(JsonObject b)
    {
        if (b["state"] is not JsonValue v || v.ToString() != "off") return false;
        if (b["for"] is not null || b["attribute"] is not null || b["match"] is not null) return false;
        if (b["entity_id"] is not JsonValue e) return false;

        var entity = e.ToString();
        if (entity.Contains(',', StringComparison.Ordinal)) return false;
        var punkt = entity.IndexOf('.', StringComparison.Ordinal);
        return punkt > 0 && AnAusBereiche.Contains(entity[..punkt]);
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
    /// <remarks>
    /// Geprüft wird nur der Fühler. Wie die Automation die Impulsdauer rechnet
    /// — mit eigenem Rückfallwert, wenn der Rechenwert ausfällt — bleibt ihre
    /// Sache; das ist eine Entscheidung dessen, der sie gebaut hat.
    /// </remarks>
    private static void SperreEinbauen(JsonObject schleife, string co2Sensor)
    {
        var ablauf = (JsonArray)schleife["sequence"]!;
        var oeffnen = ablauf.Select((s, i) => (s, i)).First(t => IstOeffnen(t.s as JsonObject)).i;

        ablauf.Insert(oeffnen, new JsonObject
        {
            ["alias"] = SperreAlias,
            ["condition"] = "template",
            ["value_template"] = $"{{{{ states('{co2Sensor}') | float(-1) > 0 }}}}",
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

    /// <param name="Stelle">Im <c>choose</c>-Zweig: die Liste und Stelle der Bedingung, die die
    /// Wartezeit-Auslöser nennt (auch verschachtelt) — oder null, wenn die ganze Automation schließt.</param>
    private sealed record Waechter(
        (JsonArray Liste, int Index)? Stelle, IReadOnlyList<JsonObject> Lang, bool SiehtNeustart);

    /// <summary>
    /// Ein Wächter: Zustands-Auslöser mit Wartezeit („länger als …"), und er
    /// schließt den Dosier-Port — entweder in einem <c>choose</c>-Zweig, der
    /// diese Auslöser nennt, oder als ganze Automation.
    /// </summary>
    private static Waechter? WaechterFinden(JsonObject config, Rahmen rahmen, bool umwandeln)
    {
        if (Liste(config, "triggers", "trigger", umwandeln) is not { } ausloeser) return null;
        var aktionen = Liste(config, "actions", "action", umwandeln);
        if (aktionen is null) return null;

        var lang = ausloeser.OfType<JsonObject>()
            .Where(t => AusloeserArt(t) == "state" && t["for"] is not null && t["to"] is JsonValue && t["entity_id"] is not null)
            .ToList();
        if (lang.Count == 0) return null;

        // Nur was den Dosier-Port schließt, ist ein CO₂-Wächter — nicht jede
        // Automation, die nach einer Wartezeit irgendetwas ausschaltet.
        var beobachtet = lang.SelectMany(t => Entitaeten(t["entity_id"])).ToHashSet(StringComparer.Ordinal);
        var ports = rahmen.Ports ?? new HashSet<string>();
        bool SchliesstPort(JsonObject s) => IstSchliessen(s) && Ziele(s).Any(z => beobachtet.Contains(z) || ports.Contains(z));

        var starts = ausloeser.OfType<JsonObject>()
            .Where(t => AusloeserArt(t) == "homeassistant" && t["event"]?.ToString() == "start")
            .Select(t => t["id"]?.ToString()).OfType<string>().ToList();
        var takte = ausloeser.OfType<JsonObject>()
            .Where(t => AusloeserArt(t) == "time_pattern")
            .Select(t => t["id"]?.ToString()).OfType<string>().ToList();
        bool Sieht(string text) => starts.Any(k => text.Contains($"\"{k}\"", StringComparison.Ordinal))
            && takte.Any(k => text.Contains($"\"{k}\"", StringComparison.Ordinal));

        var mitKennung = lang.Where(t => t["id"] is JsonValue)
            .GroupBy(t => t["id"]!.ToString(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var zweig in AlleObjekte(aktionen).Where(o => o["conditions"] is JsonArray && o["sequence"] is JsonArray))
        {
            if (!((JsonArray)zweig["sequence"]!).OfType<JsonObject>().Any(SchliesstPort)) continue;

            // Die Auslöser-Bedingung kann direkt dastehen oder in einem „oder“
            // stecken (so die Fork-Vorlage).
            var fund = Listen(zweig["conditions"])
                .SelectMany(l => l.Select((b, i) => (Liste: l, Index: i, B: b as JsonObject)))
                .FirstOrDefault(t => t.B?["condition"]?.ToString() == "trigger" && Kennungen(t.B["id"]).Any(mitKennung.ContainsKey));
            if (fund.B is null) continue;

            var genutzt = Kennungen(fund.B["id"]).Where(mitKennung.ContainsKey).Select(k => mitKennung[k]).ToList();
            return new Waechter((fund.Liste, fund.Index), genutzt, Sieht(zweig["conditions"]!.ToJsonString()));
        }

        // Ohne choose: die ganze Automation ist der Wächter — wenn sie selbst
        // den Port schließt (nicht erst in einem Zweig, der etwas anderes prüft).
        if (aktionen.OfType<JsonObject>().Any(SchliesstPort))
        {
            var bedingungen = Liste(config, "conditions", "condition", umwandeln);
            return new Waechter(null, lang, Sieht(bedingungen?.ToJsonString() ?? string.Empty));
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
        var ausloeser = Liste(config, "triggers", "trigger", umwandeln: true)!;

        // Dieselbe Schreibweise wie die vorhandenen Auslöser.
        var schluessel = ausloeser.OfType<JsonObject>().Any(t => t["trigger"] is not null) ? "trigger" : "platform";
        ausloeser.Add(new JsonObject { [schluessel] = "homeassistant", ["event"] = "start", ["id"] = NeustartKennung });
        ausloeser.Add(new JsonObject { [schluessel] = "time_pattern", ["minutes"] = "/1", ["id"] = TaktKennung });

        // Beim Start: steht der Port offen, ist er verwaist — nach einem
        // Neustart läuft keine Dosierung weiter. Im Takt: offen seit mindestens
        // derselben Wartezeit wie die bisherigen Auslöser.
        static JsonObject Zustand(JsonObject t, bool mitWartezeit)
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

        static JsonObject Oder(IEnumerable<JsonObject> teile) => new()
        {
            ["condition"] = "or",
            ["conditions"] = new JsonArray(teile.Cast<JsonNode>().ToArray()),
        };

        JsonObject Bei(string kennung, bool mitWartezeit) => new()
        {
            ["condition"] = "and",
            ["conditions"] = new JsonArray(
                new JsonObject { ["condition"] = "trigger", ["id"] = new JsonArray(kennung) },
                Oder(w.Lang.Select(t => Zustand(t, mitWartezeit)))),
        };

        if (w.Stelle is var (liste, index))
        {
            var bisher = (JsonObject)liste[index]!.DeepClone();
            liste[index] = Oder([bisher, Bei(NeustartKennung, false), Bei(TaktKennung, true)]);
            return;
        }

        // Die ganze Automation schließt: alle bisherigen Auslöser gelten wie
        // vorher, die neuen nur, wenn der Port wirklich offen steht.
        var oben = Liste(config, "conditions", "condition", umwandeln: true);
        if (oben is null)
        {
            oben = [];
            config["conditions"] = oben;
        }
        oben.Add(Oder(
        [
            new JsonObject
            {
                ["condition"] = "not",
                ["conditions"] = new JsonArray(new JsonObject
                {
                    ["condition"] = "trigger",
                    ["id"] = new JsonArray(NeustartKennung, TaktKennung),
                }),
            },
            Bei(NeustartKennung, false),
            Bei(TaktKennung, true),
        ]));
    }

    // ------------------------------------------------------------ Not-Aus

    /// <summary>
    /// Ein <c>automation.turn_on</c>, das eine Dosierung wieder einschaltet,
    /// die vorher in derselben Liste ausgeschaltet wurde — das Muster „Zyklus
    /// abbrechen und wieder scharf schalten". Ein bloßes Einschalten (etwa
    /// morgens) ist gewollt und wird nicht angefasst.
    /// </summary>
    /// <returns>Stelle des Schritts und die Dosierungen, die er so wieder einschaltet.</returns>
    private static IEnumerable<(int Index, IReadOnlyList<string> Dosierungen)> WiederEinSchritte(
        JsonArray? liste, IReadOnlySet<string> dosierungen)
    {
        if (liste is null) yield break;

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
            else if (Aktion(s) == "automation.turn_on" && ziele.Where(ausgeschaltet.Contains).ToList() is { Count: > 0 } wieder)
            {
                yield return (i, wieder);
            }
        }
    }

    /// <summary>
    /// Jede Dosierung bekommt ihre eigene Merkvariable und ihren eigenen
    /// Einschalt-Schritt — bei zwei Zelten darf das eine nicht nach dem
    /// anderen entscheiden.
    /// </summary>
    private static void WiederEinBedingen(JsonArray liste, IReadOnlySet<string> dosierungen)
    {
        // Von hinten nach vorn: das Ersetzen eines Schritts durch mehrere
        // verschiebt die Stellen dahinter.
        var stellen = WiederEinSchritte(liste, dosierungen).OrderByDescending(t => t.Index).ToList();
        if (stellen.Count == 0) return;

        var gemerkt = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (index, wieder) in stellen)
        {
            var schritt = (JsonObject)liste[index]!;
            var uebrig = Ziele(schritt).Where(z => !wieder.Contains(z)).ToList();

            var ersatz = new List<JsonNode>();
            foreach (var dosierung in wieder)
            {
                gemerkt.Add(dosierung);
                ersatz.Add(new JsonObject
                {
                    ["alias"] = WiederEinAlias,
                    ["if"] = new JsonArray(new JsonObject
                    {
                        ["condition"] = "template",
                        ["value_template"] = $"{{{{ {Merkname(dosierung)} }}}}",
                    }),
                    ["then"] = new JsonArray(MitZielen(schritt, [dosierung])),
                });
            }
            if (uebrig.Count > 0) ersatz.Add(MitZielen(schritt, uebrig));

            liste.RemoveAt(index);
            for (var k = ersatz.Count - 1; k >= 0; k--) liste.Insert(index, ersatz[k]);
        }

        // Der Zustand VOR dem Ausschalten — als erster Schritt.
        var merken = new JsonObject();
        foreach (var d in gemerkt) merken[Merkname(d)] = $"{{{{ is_state('{d}', 'on') }}}}";
        liste.Insert(0, new JsonObject { ["variables"] = merken });
    }

    private static string Merkname(string automation)
        => WarAnPraefix + automation[(automation.IndexOf('.', StringComparison.Ordinal) + 1)..];

    /// <summary>Derselbe Schritt, aber nur für diese Ziele.</summary>
    private static JsonObject MitZielen(JsonObject schritt, IReadOnlyList<string> ziele)
    {
        var kopie = (JsonObject)schritt.DeepClone();
        kopie.Remove("entity_id");
        if (kopie["data"] is JsonObject daten)
        {
            daten.Remove("entity_id");
            if (daten.Count == 0) kopie.Remove("data");
        }
        var ziel = kopie["target"] as JsonObject ?? new JsonObject();
        ziel["entity_id"] = ziele.Count == 1 ? ziele[0] : new JsonArray(ziele.Select(z => (JsonNode)z).ToArray());
        kopie["target"] = ziel;
        return kopie;
    }

    // ------------------------------------------------------------ Werkzeug

    /// <summary>
    /// Die Liste unter dem neuen Schlüssel (<c>triggers</c>) oder dem alten
    /// (<c>trigger</c>). Ein einzelnes Objekt statt einer Liste wird beim
    /// Umbauen zur Liste; beim Prüfen nur als Kopie gelesen.
    /// </summary>
    private static JsonArray? Liste(JsonObject config, string neu, string alt, bool umwandeln)
    {
        foreach (var schluessel in new[] { neu, alt })
        {
            switch (config[schluessel])
            {
                case JsonArray a:
                    return a;
                case JsonObject einzeln when umwandeln:
                    var liste = new JsonArray(einzeln.DeepClone());
                    config[schluessel] = liste;
                    return liste;
                case JsonObject einzeln:
                    return new JsonArray(einzeln.DeepClone());
            }
        }
        return null;
    }

    private static string? AusloeserArt(JsonObject t) => (t["trigger"] ?? t["platform"])?.ToString();

    private static string? Aktion(JsonObject? schritt)
        => (schritt?["action"] ?? schritt?["service"])?.ToString();

    private static IEnumerable<string> Ziele(JsonObject schritt)
        => Entitaeten(schritt["target"]?["entity_id"] ?? schritt["entity_id"] ?? schritt["data"]?["entity_id"]);

    /// <summary>
    /// Fork AI (A-016, Etappe 5): Alle Entity-IDs, die eine Automation irgendwo nennt — als eigener Wert, in einer
    /// Liste oder mitten in einer Vorlage (<c>states('sensor.x')</c>). Zu viel gefunden ist hier unschädlich:
    /// gefragt wird nur, ob eine Entität <i>vorkommt</i>.
    /// </summary>
    public static IReadOnlySet<string> AlleEntitaeten(JsonNode? knoten)
    {
        var gefunden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Lauf(JsonNode? k)
        {
            switch (k)
            {
                case JsonObject o: foreach (var p in o) Lauf(p.Value); break;
                case JsonArray a: foreach (var e in a) Lauf(e); break;
                case JsonValue v when v.TryGetValue<string>(out var text):
                    foreach (System.Text.RegularExpressions.Match m in EntityMuster.Matches(text)) gefunden.Add(m.Value);
                    break;
            }
        }
        Lauf(knoten);
        return gefunden;
    }

    private static readonly System.Text.RegularExpressions.Regex EntityMuster = new(
        @"\b[a-z][a-z0-9_]*\.[a-z0-9_]+\b", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static IEnumerable<string> Entitaeten(JsonNode? knoten) => knoten switch
    {
        JsonArray a => a.Select(k => k?.ToString()).OfType<string>(),
        JsonValue v => v.ToString().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
        _ => [],
    };

    /// <summary>Alle Listen in einem Baum, die Liste selbst eingeschlossen.</summary>
    private static IEnumerable<JsonArray> Listen(JsonNode? knoten)
    {
        switch (knoten)
        {
            case JsonArray a:
                yield return a;
                foreach (var e in a.ToList())
                foreach (var l in Listen(e)) yield return l;
                break;
            case JsonObject o:
                foreach (var p in o.ToList())
                foreach (var l in Listen(p.Value)) yield return l;
                break;
        }
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
