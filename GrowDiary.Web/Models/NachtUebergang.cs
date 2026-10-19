namespace GrowDiary.Web.Models;

/// <summary>
/// Der gleitende Übergang der Höchsttemperatur nach Licht-aus (Cultivate Control, 2.0.0-cc.255).
/// </summary>
/// <remarks>
/// <para><b>Anlass.</b> Beim Licht-aus sprang die Höchsttemperatur im selben Moment vom Tageswert auf den Nachtwert
/// (Beispiel: 24 → 22 °C). Das Zelt war aber noch warm und kühlte erst nach und nach ab — Haupt- und Zusatz-Entfeuchter
/// gingen sofort aus und blieben es, bis das Zelt unter dem Nachtwert lag (an drei Abenden 88, 44 und 3 Minuten).
/// Die Lampenwärme geht nicht in der Sekunde weg; die Grenze soll es auch nicht tun.</para>
/// <para><b>Was jetzt gilt.</b> Nach Licht-aus sinkt die Grenze in <see cref="Standard"/> Minuten (einstellbar,
/// 0 = sofort wie früher) gleichmäßig vom Tag- auf den Nachtwert. Danach gilt der Nachtwert die ganze Nacht.
/// Die Grenze liegt dabei nie über dem Tageswert und nie unter dem Nachtwert.</para>
/// <para><b>Wie sie gerechnet wird.</b> Der Rechenwert <c>temp_max_aktiv</c> in Home Assistant liest drei Dinge:
/// die Dauer (Helfer), den Zeitpunkt des Licht-aus (Zeitstempel-Helfer, geführt von einer kleinen Automation des
/// Forks) und die beiden Grenzen. Der Zeitstempel ersetzt <c>last_changed</c> des Licht-Sensors: der meldet bei
/// Verbindungsabbrüchen kurz „nicht verfügbar", und jeder Aussetzer setzte <c>last_changed</c> neu — die Grenze wäre
/// mitten in der Nacht wieder nach oben gesprungen.</para>
/// <para><b>Rückfall.</b> Fehlt einer der Helfer oder ist der Zeitstempel unlesbar oder älter als die Dauer, gilt sofort der
/// Nachtwert — also genau das Verhalten von vorher.</para>
/// </remarks>
public static class NachtUebergang
{
    /// <summary>Voreinstellung in Minuten.</summary>
    public const int Standard = 90;

    /// <summary>Obergrenze der Einstellung in Minuten (4 Stunden).</summary>
    public const int Hoechstens = 240;

    /// <summary>Der Zahlen-Helfer mit der Dauer in Minuten (0 = sofort).</summary>
    public const string DauerId = "input_number.fork_entfeuchter_nacht_uebergang";

    /// <summary>Der Zeitstempel-Helfer: wann das Licht zuletzt ausging.</summary>
    public const string LichtAusSeitId = "input_datetime.fork_entfeuchter_licht_aus_seit";

    /// <summary>
    /// Das Textstück des Rechenwerts, das vor dem Übergang galt: Nacht → fester Nachtwert. Steht so in älteren
    /// Anlagen (<see cref="Neu"/> tritt an seine Stelle); die Entity-IDs sind die des neuen Namensraums, im Bestand
    /// werden sie mit <see cref="Kennungen.Uebersetzen"/> übersetzt.
    /// </summary>
    public const string Alt =
        "{% elif led == 'off' %}{{ states('input_number.fork_entfeuchter_temp_max_nacht') | float(30) }}";

    /// <summary>
    /// Das neue Textstück: Die Grenze gleitet in der eingestellten Dauer ab Licht-aus vom Tag- auf den Nachtwert.
    /// Rechnet <see cref="Grenze"/> in Jinja nach.
    /// </summary>
    public const string Neu =
        "{% elif led == 'off' %}"
        + "{% set nacht = states('input_number.fork_entfeuchter_temp_max_nacht') | float(30) %}"
        + "{% set tag = states('input_number.fork_entfeuchter_temp_max_tag') | float(28.5) %}"
        + "{% set dauer = states('" + DauerId + "') | float(0) %}"
        + "{% set seit = as_timestamp(states('" + LichtAusSeitId + "'), none) %}"
        + "{% set vergangen = ((as_timestamp(now()) - seit) / 60) if seit is not none else -1 %}"
        + "{% if dauer > 0 and tag > nacht and 0 <= vergangen < dauer %}"
        + "{{ (tag - (tag - nacht) * vergangen / dauer) | round(2) }}"
        + "{% else %}{{ nacht }}{% endif %}";

    /// <summary>
    /// Die Höchsttemperatur, die <c>temp_max_aktiv</c> nach Licht-aus liefert — dieselbe Rechnung wie in
    /// <see cref="Neu"/>, damit sie ohne Home Assistant prüfbar ist.
    /// </summary>
    /// <param name="tagC">Höchsttemperatur Tag.</param>
    /// <param name="nachtC">Höchsttemperatur Nacht.</param>
    /// <param name="dauerMin">Dauer des Übergangs in Minuten; 0 oder weniger = sofort.</param>
    /// <param name="seitMin">Minuten seit Licht-aus; <c>null</c>, wenn der Zeitpunkt unbekannt ist (dann gilt sofort der Nachtwert).</param>
    public static double Grenze(double tagC, double nachtC, double dauerMin, double? seitMin)
    {
        if (dauerMin <= 0 || tagC <= nachtC || seitMin is not { } seit || seit < 0 || seit >= dauerMin) return nachtC;
        return Math.Round(tagC - (tagC - nachtC) * seit / dauerMin, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Die Minuten seit Licht-aus aus dem Zeitstempel. Spannen über Mitternacht stimmen, weil verglichen wird, was
    /// wirklich vergangen ist, und nicht Uhrzeiten.
    /// </summary>
    /// <returns><c>null</c>, wenn der Zeitstempel fehlt oder in der Zukunft liegt.</returns>
    public static double? MinutenSeit(DateTime? lichtAus, DateTime jetzt)
        => lichtAus is { } t && jetzt >= t ? (jetzt - t).TotalMinutes : null;
}
