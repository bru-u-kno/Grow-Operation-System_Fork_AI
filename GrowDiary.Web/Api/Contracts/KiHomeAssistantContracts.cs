using System.Text.Json;

namespace GrowDiary.Web.Api.Contracts;

// Fork AI (A-003 Etappe B, 03.10.2026): Verträge für „Home Assistant über den Fork".
// Gerufen vom Grow MCP Fork AI mit einem Schlüssel (Authorization: Bearer gok_…);
// ohne Schlüssel antwortet jeder Weg 401 ki_schluessel_fehlt. Über die Leitung
// in camelCase wie alle anderen Verträge (Program.cs, AddJsonOptions).

/// <summary>GET /api/ki-ha/bereiche — ein Bereich (Area) in Home Assistant.</summary>
public sealed record KiHaBereichDto(
    /// <summary>Die Kennung des Bereichs in Home Assistant, etwa <c>growzelt</c>.</summary>
    string Id,
    string Name);

/// <summary>GET /api/ki-ha/zustaende — der Zustand einer Entität.</summary>
public sealed record KiHaZustandDto(
    string EntityId,
    /// <summary><c>friendly_name</c>, sonst die Entity-ID.</summary>
    string Name,
    /// <summary>Der Zustand, wie Home Assistant ihn meldet (Text, auch bei Zahlen).</summary>
    string? Zustand,
    string? Einheit,
    /// <summary>Name des Bereichs, dem die Entität (oder ihr Gerät) zugeordnet ist; null = keiner.</summary>
    string? Bereich,
    DateTime? GeaendertAmUtc);

/// <summary>GET /api/ki-ha/verlauf</summary>
public sealed record KiHaVerlaufDto(
    string EntityId,
    IReadOnlyList<KiHaVerlaufsPunktDto> Punkte);

public sealed record KiHaVerlaufsPunktDto(DateTime ZeitUtc, string Zustand);

/// <summary>POST /api/ki-ha/dienst</summary>
public sealed class KiHaDienstRequest
{
    /// <summary>Die Domain des Dienstes, etwa <c>light</c>.</summary>
    public string? Domain { get; set; }

    /// <summary>Der Dienst, etwa <c>turn_on</c>.</summary>
    public string? Dienst { get; set; }

    /// <summary>Wahlweise EINE Entität; ihr Präfix muss die Domain sein (<c>light.zelt</c> zu <c>light</c>).</summary>
    public string? EntityId { get; set; }

    /// <summary>
    /// Weitere Felder des Dienstes, etwa <c>brightness_pct</c>. Ziele (<c>entity_id</c>,
    /// <c>device_id</c>, <c>area_id</c>, <c>floor_id</c>, <c>label_id</c>, <c>target</c>)
    /// gehören NICHT hierher — die Entität geht nur über <see cref="EntityId"/>.
    /// </summary>
    public Dictionary<string, JsonElement>? Daten { get; set; }
}

/// <summary>Antwort auf POST /api/ki-ha/dienst.</summary>
public sealed record KiHaDienstErgebnisDto(
    /// <summary>true nur, wenn Home Assistant den Aufruf bestätigt hat.</summary>
    bool Erfolg,
    string Meldung);
