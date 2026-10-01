using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI: Die beiden CO₂-Rechenwerte, die bei schweigendem Fühler die
/// falsche Richtung nahmen — erkennen und, wenn es eine bekannte ältere Fassung
/// ist, durch die aktuelle ersetzen.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass.</b> forkai.153 hat die Formeln im Katalog korrigiert
/// (<see cref="SteuerungBauteile"/>): „CO2 Bedarf" hielt bei schweigendem
/// Fühler bis zu fünf Minuten „an", „CO2 Impuls Bedarf" rechnete mit 0 ppm
/// und lieferte die Höchstdauer. Angelegte Rechenwerte behielten ihre alte
/// Formel — Fork AI legt nur fehlende an.</para>
/// <para><b>Ersetzt wird nur, was erkannt wird.</b> Steht in Home Assistant
/// genau eine ältere Fassung des Fork (mit dem zugeordneten Fühler eingesetzt),
/// ist klar, was sie tut und was die neue anders macht. Eine von Hand
/// angepasste Formel wird nur angezeigt: wer sie geändert hat, hatte einen
/// Grund, den keine Vorlage kennt.</para>
/// <para><b>Einmal die Wahrheit.</b> Die neue Formel ist die des Katalogs —
/// hier steht sie nicht noch einmal.</para>
/// </remarks>
public static class Co2Rechenwerte
{
    public enum Stand
    {
        /// <summary>Die Formel ist die aktuelle des Katalogs.</summary>
        Aktuell,
        /// <summary>Eine bekannte ältere Fassung — lässt sich ersetzen.</summary>
        Veraltet,
        /// <summary>Weder alt noch neu: von Hand angepasst. Wird nur angezeigt.</summary>
        Angepasst,
        /// <summary>
        /// Eine bekannte Fassung, aber mit einem anderen Fühler als dem
        /// zugeordneten. Wird nur angezeigt: welcher stimmt, weiß der Bediener.
        /// </summary>
        AndererFuehler,
        /// <summary>Ohne zugeordneten CO₂-Fühler lässt sich keine Fassung bilden.</summary>
        OhneFuehler,
    }

    /// <summary>Eine ältere Fassung des Fork und was sie bei schweigendem Fühler tut.</summary>
    /// <param name="Vorlage">Die Formel mit dem Platzhalter <c>[[co2_sensor]]</c>, wie sie im Katalog stand.</param>
    /// <param name="Heute">Was diese Fassung tut, wenn der Fühler schweigt.</param>
    /// <param name="Danach">Was die aktuelle Fassung dann tut.</param>
    public sealed record Fassung(string EntityId, string Seit, string Vorlage, string Heute, string Danach);

    /// <summary>
    /// Alle älteren Fassungen, die es im Fork gab (aus der Geschichte von
    /// SteuerungBauteil.cs, forkai.138 bis forkai.152).
    /// </summary>
    public static readonly IReadOnlyList<Fassung> Aeltere =
    [
        new(Co2SteuerungService.Entitaeten.Bedarf, "forkai.138",
            "{% set co2_s = states.[[co2_sensor]] %}{% set weg = co2_s is none or co2_s.state in ['unknown','unavailable'] %}{% set ziel = states('sensor.co2_ziel_effektiv') | float(0) %}{% if weg or ziel <= 0 %}{% set seit = (as_timestamp(now()) - as_timestamp(co2_s.last_changed)) if co2_s is not none else 9999 %}{% if seit > 300 %}false{% else %}{{ is_state('binary_sensor.co2_bedarf', 'on') }}{% endif %}{% else %}{% set ist = co2_s.state | float(0) %}{% set h = states('input_number.co2_hysterese') | float(100) %}{% if ist <= 0 %}{{ is_state('binary_sensor.co2_bedarf', 'on') }}{% elif ist < ziel - h %}true{% elif ist >= ziel %}false{% else %}{{ is_state('binary_sensor.co2_bedarf', 'on') }}{% endif %}{% endif %}",
            "bleibt bis zu 5 Minuten „an“, bei 0 ppm unbegrenzt",
            "sofort „aus“"),
        new(Co2SteuerungService.Entitaeten.ImpulsBedarf, "forkai.138",
            "{% set ziel = states('sensor.co2_ziel_effektiv') | float(0) %}{% set ist = states('[[co2_sensor]]') | float(0) %}{% set gps = states('input_number.co2_gramm_pro_sekunde') | float(0.26) %}{% set vol = states('input_number.co2_zeltvolumen') | float(5.76) %}{% set mn = states('input_number.co2_impulsdauer') | float(5) %}{% set mx = states('input_number.co2_impulsdauer_max') | float(20) %}{% set gramm = ([ziel - ist, 0] | max) * vol / 557 %}{{ ([ [gramm / gps, mn] | max, mx ] | min) | round(0) | int }}",
            "rechnet mit 0 ppm und liefert die Höchstdauer",
            "0 s"),
    ];

    /// <summary>Die Rechenwerte, um die es hier geht.</summary>
    public static IReadOnlyList<string> Betroffene { get; } =
        Aeltere.Select(f => f.EntityId).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>Die aktuelle Formel des Katalogs, mit den zugeordneten Geräten eingesetzt.</summary>
    public static string? Aktuelle(string entityId, IReadOnlyDictionary<string, string> zuordnung)
    {
        var bauteil = SteuerungBauteile.Alle.SingleOrDefault(b => b.EntityId == entityId);
        return bauteil?.Vorlage is null ? null : SteuerungBauteile.VorlageFuellen(bauteil.Vorlage, zuordnung);
    }

    /// <summary>Was in Home Assistant steht — und ob es sich ersetzen lässt.</summary>
    public static (Stand Stand, Fassung? Alt) Beurteilen(
        string entityId, string formel, IReadOnlyDictionary<string, string> zuordnung)
    {
        if (Aktuelle(entityId, zuordnung) is not { } aktuell) return (Stand.OhneFuehler, null);
        if (Gleich(formel, aktuell)) return (Stand.Aktuell, null);

        foreach (var f in Aeltere.Where(f => f.EntityId == entityId))
        {
            if (SteuerungBauteile.VorlageFuellen(f.Vorlage, zuordnung) is { } alt && Gleich(formel, alt))
            {
                return (Stand.Veraltet, f);
            }
        }
        return (FuehlerInFormel(entityId, formel) is not null ? Stand.AndererFuehler : Stand.Angepasst, null);
    }

    /// <summary>
    /// Der Fühler, wenn die Formel eine bekannte Fassung (alt oder aktuell) mit
    /// irgendeinem Fühler ist — sonst null.
    /// </summary>
    public static string? FuehlerInFormel(string entityId, string formel)
    {
        var vorlagen = Aeltere.Where(f => f.EntityId == entityId).Select(f => f.Vorlage)
            .Append(SteuerungBauteile.Alle.SingleOrDefault(b => b.EntityId == entityId)?.Vorlage)
            .OfType<string>();

        foreach (var vorlage in vorlagen)
        {
            var teile = Normal(vorlage).Split("[[co2_sensor]]");
            if (teile.Length < 2) continue;
            var muster = "^" + Regex.Escape(teile[0]) + @"(?<f>[a-z_]+\.[a-z0-9_]+)"
                + string.Concat(teile.Skip(1).Select((t, i) => (i == 0 ? "" : @"\k<f>") + Regex.Escape(t))) + "$";
            if (Regex.Match(Normal(formel), muster) is { Success: true } treffer) return treffer.Groups["f"].Value;
        }
        return null;
    }

    /// <summary>
    /// Gleich bis auf Leerraum: der Editor in Home Assistant bricht lange
    /// Formeln gelegentlich um. Leerraum zwischen zwei Jinja-Blöcken landet nur
    /// am Rand des Ergebnisses, und den schneidet Home Assistant ab.
    /// </summary>
    private static bool Gleich(string a, string b) => Normal(a) == Normal(b);

    private static string Normal(string s)
    {
        var ohneZwischenraum = Regex.Replace(s, @"(%\}|\}\})\s+(?=\{%|\{\{)", "$1");
        return Regex.Replace(ohneZwischenraum, @"\s+", " ").Trim();
    }

    // ------------------------------------------------------------ Dialog

    /// <summary>
    /// Die Formel, die der Einstellungsdialog als aktuellen Wert vorbelegt
    /// (Feld <c>state</c>, <c>description.suggested_value</c>).
    /// </summary>
    public static string? FormelAusDialog(JsonNode? dataSchema)
        => Felder(dataSchema).FirstOrDefault(f => f["name"]?.ToString() == "state")
            ?["description"]?["suggested_value"]?.ToString();

    /// <summary>
    /// Die Antwort auf den Dialog: jeder vorbelegte Wert unverändert zurück,
    /// nur die Formel neu.
    /// </summary>
    /// <remarks>
    /// Home Assistant löscht beim Absenden jedes optionale Feld, das fehlt
    /// (<c>_update_and_remove_omitted_optional_keys</c> in
    /// <c>schema_config_entry_flow.py</c>). Wer nur die Formel schickt, nimmt
    /// Einheit, Zustandsklasse und Verfügbarkeit still weg.
    /// </remarks>
    public static JsonObject Antwort(JsonNode? dataSchema, string neueFormel)
    {
        var antwort = Vorbelegt(dataSchema);
        antwort["state"] = neueFormel;
        return antwort;
    }

    private static JsonObject Vorbelegt(JsonNode? dataSchema)
    {
        var werte = new JsonObject();
        foreach (var feld in Felder(dataSchema))
        {
            if (feld["name"]?.ToString() is not { } name) continue;

            if (feld["type"]?.ToString() == "expandable")
            {
                var innen = Vorbelegt(feld["schema"]);
                if (innen.Count > 0) werte[name] = innen;
                continue;
            }

            if (feld["description"] is JsonObject beschreibung
                && beschreibung.TryGetPropertyValue("suggested_value", out var wert)
                && wert is not null)
            {
                werte[name] = wert.DeepClone();
            }
        }
        return werte;
    }

    private static IEnumerable<JsonObject> Felder(JsonNode? dataSchema)
        => dataSchema is JsonArray liste ? liste.OfType<JsonObject>() : [];
}
