namespace GrowDiary.Web.Models;

public sealed class HomeAssistantState
{
    public string EntityId { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string? FriendlyName { get; set; }
    public string? UnitOfMeasurement { get; set; }

    /// <summary>
    /// Fork AI (Chiller-Ansteuerung): das Attribut <c>temperature</c> — bei einem
    /// climate-Gerät der eingestellte Sollwert.
    /// </summary>
    public double? AttributTemperatur { get; set; }
    /// <summary>Wann sich der Zustands<b>text</b> zuletzt geändert hat.</summary>
    public DateTime? LastChanged { get; set; }

    /// <summary>
    /// Wann Home Assistant die Entität zuletzt <b>aktualisiert</b> hat — auch
    /// dann, wenn derselbe Wert nochmal kam.
    /// </summary>
    /// <remarks>
    /// Für „wie frisch ist der Messwert" ist das die richtige Zahl.
    /// <see cref="LastChanged"/> steht bei einer Temperatur, die stabil auf
    /// ihrem Sollwert liegt, beliebig lange still.
    /// </remarks>
    public DateTime? LastUpdated { get; set; }
    public double? NumericValue { get; set; }
}
