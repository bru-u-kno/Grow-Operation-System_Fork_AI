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

    /// <summary>
    /// Fork AI (02.10.2026): die Attribute <c>min</c> und <c>max</c> — bei einem
    /// <c>input_number</c> die Spanne, außerhalb der <c>set_value</c> ablehnt.
    /// </summary>
    /// <remarks>
    /// Für Helfer, die nicht aus dem Katalog (<see cref="SteuerungBauteile"/>)
    /// stammen, ist das die einzige Quelle ihrer Spanne — etwa
    /// <c>input_number.vpd_ziel_unten</c>, das der Nutzer selbst angelegt hat.
    /// </remarks>
    public double? AttributMin { get; set; }

    /// <inheritdoc cref="AttributMin"/>
    public double? AttributMax { get; set; }
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
