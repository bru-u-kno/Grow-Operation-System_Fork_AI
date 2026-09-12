using System.Text.Json.Serialization;

namespace GrowDiary.Web.Services.Knowledge.Schema;

public sealed class NutrientProgramDefinition : KnowledgeFileMetadata
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("manufacturer")]
    public string Manufacturer { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("bestFor")]
    public string BestFor { get; set; } = string.Empty;

    [JsonPropertyName("waterGuidance")]
    public string WaterGuidance { get; set; } = string.Empty;

    [JsonPropertyName("phGuidance")]
    public string PhGuidance { get; set; } = string.Empty;

    [JsonPropertyName("ecGuidance")]
    public string EcGuidance { get; set; } = string.Empty;

    [JsonPropertyName("scheduleStyle")]
    public string ScheduleStyle { get; set; } = string.Empty;

    [JsonPropertyName("officialHighlights")]
    public string OfficialHighlights { get; set; } = string.Empty;

    [JsonPropertyName("practiceNotes")]
    public string PracticeNotes { get; set; } = string.Empty;

    [JsonPropertyName("stages")]
    public List<NutrientStage> Stages { get; set; } = [];

    [JsonPropertyName("tips")]
    public List<string> Tips { get; set; } = [];

    [JsonPropertyName("searchTerms")]
    public List<string> SearchTerms { get; set; } = [];

    /// <summary>
    /// Das Wochen-Chart mit Zahlen — die Grundlage des Mischrechners.
    /// </summary>
    /// <remarks>
    /// Optional: die Programme tragen seit jeher Text-Leitplanken; erst mit dem
    /// Chart kann die App beim Ansetzen konkrete Milliliter nennen. Ohne Chart
    /// bleibt der Mischrechner still, statt zu raten.
    /// </remarks>
    [JsonPropertyName("feedChart")]
    public FeedChartDefinition? FeedChart { get; set; }
}

/// <summary>Ein Wochen-Chart: Spalten mit Dosen je Komponente und Zielwerten.</summary>
public sealed class FeedChartDefinition
{
    /// <summary>Einheit der Dosen — derzeit immer <c>mlPerLiter</c>.</summary>
    [JsonPropertyName("unit")]
    public string Unit { get; set; } = "mlPerLiter";

    /// <summary>Herkunft und Umrechnung, damit die Zahl belegbar bleibt.</summary>
    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("columns")]
    public List<FeedChartColumn> Columns { get; set; } = [];
}

/// <summary>Eine Spalte des Charts — eine Woche oder ein Sonderschritt (Vorweichen, Flush).</summary>
public sealed class FeedChartColumn
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    /// <summary>Clone, Veg, Flower oder Finish — für die Zuordnung zur Phase des Grows.</summary>
    [JsonPropertyName("stage")]
    public string Stage { get; set; } = string.Empty;

    /// <summary>Wochennummer innerhalb der Phase; null bei Sonderschritten.</summary>
    [JsonPropertyName("week")]
    public int? Week { get; set; }

    [JsonPropertyName("items")]
    public List<FeedChartItem> Items { get; set; } = [];

    [JsonPropertyName("ecTarget")]
    public double? EcTarget { get; set; }

    [JsonPropertyName("phMin")]
    public double? PhMin { get; set; }

    [JsonPropertyName("phMax")]
    public double? PhMax { get; set; }

    // ---- Fork AI: Klima je Woche ----
    // Der Klimateil eines Plans (Wasser, VPD, RH, Luft, CO2, PPFD) stand bisher
    // nur als Fliesstext in den Phasennotizen und wirkte nirgends. Er gehoert an
    // dieselbe Wochenspalte wie EC und pH: dann erbt er ohne Zutun deren Anker
    // (Bluetewochen ab Flip, Vegi-Wochen ab Start, letzte Spalte wird gehalten)
    // und jede Stelle, die das Zielband liest, sieht ihn automatisch mit.
    // Alles optional — ein Programm ohne diese Angaben verhaelt sich wie bisher.

    [JsonPropertyName("waterTempDayC")]
    public double? WaterTempDayC { get; set; }

    [JsonPropertyName("waterTempNightC")]
    public double? WaterTempNightC { get; set; }

    [JsonPropertyName("vpdMin")]
    public double? VpdMin { get; set; }

    [JsonPropertyName("vpdMax")]
    public double? VpdMax { get; set; }

    [JsonPropertyName("co2Min")]
    public double? Co2Min { get; set; }

    [JsonPropertyName("co2Max")]
    public double? Co2Max { get; set; }

    [JsonPropertyName("ppfdMin")]
    public double? PpfdMin { get; set; }

    [JsonPropertyName("ppfdMax")]
    public double? PpfdMax { get; set; }

    /// <summary>Obergrenze der Luftfeuchte in Prozent.</summary>
    /// <remarks>
    /// Steht bewusst NICHT im Zielband: <c>HydroTargetValues</c> kennt weder RH
    /// noch Lufttemperatur, und diese Felder aufzubohren hiesse, jeden Aufrufer
    /// im Original anzufassen. Beide Werte bleiben deshalb an der Spalte und
    /// werden von der Wochenplan-Seite und der HA-Uebergabe gelesen.
    /// </remarks>
    [JsonPropertyName("rhMax")]
    public double? RhMax { get; set; }

    /// <summary>Ziel-Lufttemperatur in °C.</summary>
    [JsonPropertyName("airTempC")]
    public double? AirTempC { get; set; }
}

/// <summary>Eine Komponente in einer Spalte — als Spanne, wo das Chart eine nennt.</summary>
public sealed class FeedChartItem
{
    [JsonPropertyName("component")]
    public string Component { get; set; } = string.Empty;

    [JsonPropertyName("minMlPerLiter")]
    public double MinMlPerLiter { get; set; }

    [JsonPropertyName("maxMlPerLiter")]
    public double MaxMlPerLiter { get; set; }
}
