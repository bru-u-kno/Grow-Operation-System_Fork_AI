using System.Text.RegularExpressions;

namespace GrowDiary.Web.Models;

/// <summary>
/// Fork AI (forkai.217): In welchem Namensraum die Objekte des Forks in Home Assistant heißen.
/// </summary>
/// <remarks>
/// <para><b>Wozu.</b> Die Helfer, Rechen-Sensoren und Automationen der Entfeuchter-Steuerungen hießen bisher
/// <c>trotec_*</c> bzw. <c>rdwc_…_port_N</c> — so, wie sie in der Anlage des Entwicklers von Hand entstanden sind.
/// Wer den Fork neu installiert, hat seine eigenen Geräte und Namen; ein Helfer mit fremder Marke im Namen
/// ist irreführend, und ein gleichnamiger Helfer des Nutzers würde vom Fork als sein eigener behandelt.</para>
/// <para><b>Zwei Räume.</b> <see cref="KennungsRaum.Neu"/> (Voreinstellung) trägt das Präfix <c>fork_entfeuchter_</c>
/// bzw. die vom Alias der Vorlage abgeleitete Automation. <see cref="KennungsRaum.Bestand"/> gilt für Installationen,
/// die schon mit den alten Namen liefen: Dort bleiben die alten Kennungen bestehen — nichts wird umbenannt, doppelt
/// angelegt oder überschrieben. Der Raum wird einmal beim Start festgelegt (<see cref="KennungsRaumWahl"/>) und danach
/// nicht mehr geändert.</para>
/// <para>Im Quelltext stehen überall die neuen Kennungen; <see cref="Id"/> und <see cref="Uebersetzen"/> machen im
/// Bestand die alten daraus.</para>
/// </remarks>
public enum KennungsRaum
{
    /// <summary>Frische Installation: neutrale Kennungen mit Fork-Präfix.</summary>
    Neu,
    /// <summary>Installation, die schon mit den alten (<c>trotec_*</c>-)Kennungen lief: sie bleiben unverändert.</summary>
    Bestand,
}

/// <summary>Eine Kennung im neuen und im alten Namensraum.</summary>
/// <param name="Domaene">Die Domäne der Entität (<c>input_number</c>, <c>sensor</c>, …).</param>
/// <param name="Neu">Objektkennung im neuen Namensraum (ohne Domäne).</param>
/// <param name="Alt">Objektkennung im Bestand (ohne Domäne).</param>
/// <param name="NeuName">Name in Home Assistant im neuen Namensraum.</param>
/// <param name="AltName">Name in Home Assistant im Bestand.</param>
public sealed record Altkennung(string Domaene, string Neu, string Alt, string NeuName, string AltName)
{
    /// <summary>Die volle Entity-ID im neuen Namensraum.</summary>
    public string NeueId => $"{Domaene}.{Neu}";

    /// <summary>Die volle Entity-ID im Bestand.</summary>
    public string AlteId => $"{Domaene}.{Alt}";
}

/// <summary>Die Zuordnung neuer zu alten Kennungen und der aktuell gültige Raum.</summary>
public static class Kennungen
{
    /// <summary>Alle umbenannten Objekte. Die Reihenfolge ist egal.</summary>
    public static IReadOnlyList<Altkennung> Tabelle { get; } = new Altkennung[]
    {
        new("input_boolean", "fork_entfeuchter_vpd_regelung", "trotec_vpd_regelung", "Fork Entfeuchter VPD Regelung", "Trotec VPD Regelung"),
        new("input_number", "fork_entfeuchter_hysterese", "trotec_hysterese", "Fork Entfeuchter Hysterese", "Trotec Hysterese"),
        new("input_number", "fork_entfeuchter_mindestlaufzeit", "trotec_mindestlaufzeit", "Fork Entfeuchter Mindestlaufzeit", "Trotec Mindestlaufzeit"),
        new("input_number", "fork_entfeuchter_einschaltverzoegerung", "trotec_einschaltverzoegerung", "Fork Entfeuchter Einschaltverzoegerung", "Trotec Einschaltverzoegerung"),
        new("input_number", "fork_entfeuchter_wartezeit_aussenluft", "trotec_wartezeit_aussenluft", "Fork Entfeuchter Wartezeit Aussenluft", "Trotec Wartezeit Aussenluft"),
        new("input_boolean", "fork_entfeuchter_tagbetrieb_erlauben", "trotec_tagbetrieb_erlauben", "Fork Entfeuchter Tagbetrieb erlauben", "Trotec Tagbetrieb erlauben"),
        new("input_number", "fork_entfeuchter_temp_max_tag", "trotec_temp_max_tag", "Fork Entfeuchter Temp max Tag", "Trotec Temp max Tag"),
        new("input_number", "fork_entfeuchter_temp_max_nacht", "trotec_temp_max", "Fork Entfeuchter Temp max Nacht", "Trotec Temp max"),
        new("input_number", "fork_entfeuchter_feuchte_ein_tag", "trotec_feuchte_ein_tag", "Fork Entfeuchter Feuchte EIN Tag", "Trotec Feuchte EIN Tag"),
        new("input_number", "fork_entfeuchter_feuchte_aus_tag", "trotec_feuchte_aus_tag", "Fork Entfeuchter Feuchte AUS Tag", "Trotec Feuchte AUS Tag"),
        new("input_number", "fork_entfeuchter_feuchte_ein_nacht", "trotec_feuchte_ein", "Fork Entfeuchter Feuchte EIN Nacht", "Trotec Feuchte EIN"),
        new("input_number", "fork_entfeuchter_feuchte_aus_nacht", "trotec_feuchte_aus", "Fork Entfeuchter Feuchte AUS Nacht", "Trotec Feuchte AUS"),
        new("sensor", "fork_entfeuchter_feuchte_ein_aktiv", "trotec_feuchte_ein_aktiv", "Fork Entfeuchter Feuchte EIN aktiv", "Trotec Feuchte EIN aktiv"),
        new("sensor", "fork_entfeuchter_feuchte_aus_aktiv", "trotec_feuchte_aus_aktiv", "Fork Entfeuchter Feuchte AUS aktiv", "Trotec Feuchte AUS aktiv"),
        new("sensor", "fork_entfeuchter_temp_max_aktiv", "trotec_temp_max_aktiv", "Fork Entfeuchter Temp Max aktiv", "Trotec Temp Max aktiv"),
        new("binary_sensor", "fork_entfeuchter_feuchte_ueber_ein", "trotec_feuchte_uber_ein", "Fork Entfeuchter Feuchte ueber EIN", "Trotec Feuchte uber EIN"),
        new("automation", "entfeuchter_regelung_tag_nacht", "rdwc_trotec_nachtregelung_port_7_dehumi", "Entfeuchter Regelung Tag/Nacht", "RDWC Trotec Regelung"),
        new("input_number", "fork_entfeuchter_zusatz_vpd_hysterese", "trotec_zelt_vpd_hysterese", "Fork Entfeuchter Zusatz VPD Hysterese", "Trotec Zelt VPD Hysterese"),
        new("input_number", "fork_entfeuchter_zusatz_mindestlaufzeit", "trotec_zelt_mindestlaufzeit", "Fork Entfeuchter Zusatz Mindestlaufzeit", "Trotec Zelt Mindestlaufzeit"),
        new("input_number", "fork_entfeuchter_zusatz_mindestpause", "trotec_zelt_mindestpause", "Fork Entfeuchter Zusatz Mindestpause", "Trotec Zelt Mindestpause"),
        new("input_number", "fork_entfeuchter_zusatz_folge_abstand", "trotec_zelt_folge_abstand", "Fork Entfeuchter Zusatz Folge Abstand", "Trotec Zelt Folge Abstand"),
        new("input_number", "fork_entfeuchter_zusatz_wieder_ein_abstand", "trotec_zelt_wieder_ein_abstand", "Fork Entfeuchter Zusatz Wieder Ein Abstand", "Trotec Zelt Wieder Ein Abstand"),
        new("input_number", "fork_entfeuchter_zusatz_zuschalt_verzogerung", "trotec_zelt_zuschalt_verzogerung", "Fork Entfeuchter Zusatz Zuschalt Verzogerung", "Trotec Zelt Zuschalt Verzogerung"),
        new("input_boolean", "fork_entfeuchter_zusatz_tagbetrieb_erlauben", "trotec_zelt_tagbetrieb_erlauben", "Fork Entfeuchter Zusatz Tagbetrieb erlauben", "Trotec Zelt Tagbetrieb erlauben"),
        new("input_boolean", "fork_entfeuchter_zusatz_nacht_durchlaufen", "trotec_zelt_nacht_durchlaufen", "Fork Entfeuchter Zusatz Nacht durchlaufen", "Trotec Zelt Nacht durchlaufen"),
        new("input_boolean", "fork_entfeuchter_zusatz_melden", "trotec_zelt_melden", "Fork Entfeuchter Zusatz Melden", "Trotec Zelt Melden"),
        new("input_number", "fork_entfeuchter_zusatz_melde_grenze_w", "trotec_zelt_melde_grenze_w", "Fork Entfeuchter Zusatz Melde Grenze W", "Trotec Zelt Melde Grenze W"),
        new("input_number", "fork_entfeuchter_zusatz_melde_dauer_min", "trotec_zelt_melde_dauer_min", "Fork Entfeuchter Zusatz Melde Dauer Min", "Trotec Zelt Melde Dauer Min"),
        new("input_number", "fork_entfeuchter_zusatz_melde_wiederholung_h", "trotec_zelt_melde_wiederholung_h", "Fork Entfeuchter Zusatz Melde Wiederholung H", "Trotec Zelt Melde Wiederholung H"),
        new("sensor", "fork_entfeuchter_zusatz_vpd_ein_schwelle", "trotec_zelt_vpd_ein_schwelle", "Fork Entfeuchter Zusatz VPD EIN Schwelle", "Trotec Zelt VPD EIN Schwelle"),
        new("sensor", "fork_entfeuchter_zusatz_vpd_aus_schwelle", "trotec_zelt_vpd_aus_schwelle", "Fork Entfeuchter Zusatz VPD AUS Schwelle", "Trotec Zelt VPD AUS Schwelle"),
        new("sensor", "fork_entfeuchter_zusatz_folge_aus_temperatur", "trotec_zelt_folge_aus_temperatur", "Fork Entfeuchter Zusatz Folge AUS Temperatur", "Trotec Zelt Folge AUS Temperatur"),
        new("sensor", "fork_entfeuchter_zusatz_wieder_ein_temperatur", "trotec_zelt_wieder_ein_temperatur", "Fork Entfeuchter Zusatz Wieder EIN Temperatur", "Trotec Zelt Wieder EIN Temperatur"),
        new("automation", "zusatz_entfeuchter_regelung", "rdwc_trotec_zelt_shelly_plan_regelung", "Zusatz-Entfeuchter Regelung", "Zusatz-Entfeuchter Regelung")
    };

    private static readonly IReadOnlyDictionary<string, Altkennung> NachNeuerId =
        Tabelle.ToDictionary(a => a.NeueId, StringComparer.Ordinal);

    // Längste zuerst: „…_feuchte_ein“ ist Vorspann von „…_feuchte_ein_aktiv“; die Grenzen im Muster
    // (kein Buchstabe, Ziffer oder Unterstrich davor/danach) trennen sie, die Sortierung hält es robust.
    private static readonly Regex NeueIds = new(
        "(?<![a-z0-9_])(" + string.Join("|", Tabelle.OrderByDescending(a => a.NeueId.Length)
            .Select(a => Regex.Escape(a.NeueId))) + ")(?![a-z0-9_])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static volatile KennungsRaum _raum = KennungsRaum.Neu;

    /// <summary>Der Raum dieser Installation. Voreinstellung <see cref="KennungsRaum.Neu"/>.</summary>
    public static KennungsRaum Raum => _raum;

    /// <summary>Legt den Raum fest — einmal beim Start, bevor ein Dienst Kennungen benutzt.</summary>
    public static void Festlegen(KennungsRaum raum) => _raum = raum;

    /// <summary>Die Entity-ID im aktuellen Raum — Kennungen, die der Katalog nicht umbenennt, kommen unverändert zurück.</summary>
    public static string Id(string neueId) => Id(neueId, _raum);

    /// <summary>Die Entity-ID im angegebenen Raum.</summary>
    public static string Id(string neueId, KennungsRaum raum)
        => raum == KennungsRaum.Bestand && NachNeuerId.TryGetValue(neueId, out var a) ? a.AlteId : neueId;

    /// <summary>
    /// Ersetzt in einem Text (Vorlage, Rechenvorschrift, Automation) jede neue Entity-ID durch die des Raums.
    /// Im neuen Raum bleibt der Text unverändert.
    /// </summary>
    public static string Uebersetzen(string text, KennungsRaum raum)
        => raum == KennungsRaum.Neu || string.IsNullOrEmpty(text)
            ? text
            : NeueIds.Replace(text, m => NachNeuerId[m.Value].AlteId);

    /// <summary>Der Name in Home Assistant im angegebenen Raum (zu einer Entity-ID im neuen Namensraum).</summary>
    public static string Name(string neueId, string neuerName, KennungsRaum raum)
        => raum == KennungsRaum.Bestand && NachNeuerId.TryGetValue(neueId, out var a) ? a.AltName : neuerName;
}
