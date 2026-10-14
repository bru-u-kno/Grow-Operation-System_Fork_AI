using System.Globalization;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>Ein Grund, warum die Luftfeuchte nicht dort ist, wo der Plan sie will — für die Kachel „Was bremst gerade?“.</summary>
/// <param name="Art">Maschinenlesbar: <c>kapazitaet</c>, <c>uebertemperatur</c>, <c>aussenluft</c>, <c>tagbetrieb</c>, <c>drosselung</c>, <c>automatik</c>.</param>
/// <param name="Stufe"><c>kritisch</c>, <c>warnung</c> oder <c>info</c>.</param>
public sealed record EntfeuchterBremse(string Art, string Stufe, string Titel, string Text);

/// <summary>Was die Prüfung der Bremsen braucht — alles, was die Seite ohnehin schon liest.</summary>
public sealed record BremsEingabe(
    double? FeuchteProzent,
    double? GrenzeProzent,
    double? EinProzent,
    double? TempC,
    double? TempMaxC,
    bool? TagPhase,
    bool TagbetriebErlaubt,
    bool? AutomatikAn,
    bool? PortAn,
    bool? ZuluftVorrang,
    bool? Co2Drosselung,
    int EinschaltverzoegerungMin,
    int WartezeitAussenluftMin,
    int? UeberGrenzeSeitMin,
    int? LaeuftSeitMin);

/// <summary>
/// Fork AI (A-022): Warum ein Entfeuchter nicht schaltet oder nicht reicht — in Worten, mit Zahlen.
/// </summary>
/// <remarks>
/// <para><b>Nur, was bremst.</b> Ohne Befund bleibt die Liste leer; die Seite zeigt dann „nichts bremst“. Jede Zeile nennt den
/// Grund und die Zahlen dahinter (Wartezeit, Temperatur, Dauer), damit der Nutzer nicht raten muss.</para>
/// <para><b>Die Kapazität.</b> Läuft ein Entfeuchter seit <see cref="KapazitaetMinuten"/> Minuten und liegt die Feuchte so lange
/// über dem Plan-Wert, hilft keine Einstellung mehr: das Gerät schafft die Last nicht, oder der Fühler zeigt etwas anderes
/// als die Luft. Das sagt die Seite so.</para>
/// </remarks>
public static class EntfeuchterBremsen
{
    /// <summary>So lange muss die Feuchte über dem Plan-Wert liegen und der Entfeuchter laufen, bevor die Seite „schafft es nicht“ sagt.</summary>
    public const int KapazitaetMinuten = 60;

    /// <summary>Wie weit zurück der Verlauf gelesen wird, um die Dauer zu bestimmen.</summary>
    public static readonly TimeSpan VerlaufFenster = TimeSpan.FromHours(3);

    public static IReadOnlyList<EntfeuchterBremse> Pruefen(BremsEingabe x)
    {
        var liste = new List<EntfeuchterBremse>();
        var ueber = x is { FeuchteProzent: { } f, GrenzeProzent: { } g } && f > g;

        // Kapazität: nur mit belegter Dauer, nie aus einem einzelnen Messwert.
        if (ueber && x is { UeberGrenzeSeitMin: { } seit, LaeuftSeitMin: { } laeuft }
            && seit >= KapazitaetMinuten && laeuft >= KapazitaetMinuten)
        {
            liste.Add(new EntfeuchterBremse("kapazitaet", "kritisch", "Der Entfeuchter schafft es nicht",
                $"Er läuft seit {Dauer(laeuft)} und die Feuchte liegt seit {Dauer(seit)} über dem Plan-Wert "
                + $"({Zahl(x.FeuchteProzent!.Value)} statt {Zahl(x.GrenzeProzent!.Value)} %). Einstellungen helfen hier nicht mehr. "
                + "Prüfe, ob Zuluft und Abluft genug Luft bewegen, und vergleiche den Fühler mit einem zweiten Hygrometer."));
        }

        if (x is { TempC: { } t, TempMaxC: { } tm } && t > tm)
        {
            liste.Add(new EntfeuchterBremse("uebertemperatur", ueber ? "warnung" : "info", $"Zu warm ({Zahl(t)} °C über {Zahl(tm)} °C)",
                $"Alle Entfeuchter sind aus, bis es unter {Zahl(tm)} °C ist. Das schützt Pflanze und Geräte, entfeuchtet aber nicht."));
        }

        if (x.AutomatikAn == false)
        {
            liste.Add(new EntfeuchterBremse("automatik", "warnung", "Die Automatik ist aus",
                "Die Regelung schaltet nichts. Die Entfeuchter bleiben, wie sie gerade stehen."));
        }

        if (x is { TagPhase: true, TagbetriebErlaubt: false } && ueber)
        {
            liste.Add(new EntfeuchterBremse("tagbetrieb", "warnung", "Tagsüber ist das Entfeuchten ausgeschaltet",
                "Die Feuchte liegt über dem Plan-Wert, aber „Wann darf entfeuchtet werden?“ steht auf „Nur nachts“."));
        }

        // Außenluft: die lange Wartezeit zählt nur, solange der Entfeuchter noch nicht läuft und die Feuchte über EIN liegt.
        if (x is { ZuluftVorrang: true, PortAn: not true, FeuchteProzent: { } fe, EinProzent: { } ein } && fe > ein
            && x.WartezeitAussenluftMin > x.EinschaltverzoegerungMin)
        {
            liste.Add(new EntfeuchterBremse("aussenluft", "warnung", "Wartet auf Außenluft",
                $"Die Zuluft läuft und die Außenluft ist trockener als der Raum. Der Entfeuchter wartet deshalb {x.WartezeitAussenluftMin} statt "
                + $"{x.EinschaltverzoegerungMin} Minuten, bevor er einschaltet."));
        }

        if (x.Co2Drosselung == true && ueber)
        {
            liste.Add(new EntfeuchterBremse("drosselung", "info", "CO₂-Drosselung ist an",
                "Solange die Abluft gedrosselt ist, schaltet der Entfeuchter nicht wegen erreichter Feuchte ab. Die Drosselung wärmt das Zelt."));
        }

        return liste;
    }

    /// <summary>
    /// Wie lange die Feuchte schon ohne Unterbrechung über der Grenze liegt — aus dem Verlauf, rückwärts vom jetzt.
    /// <c>null</c>, wenn die Feuchte jetzt nicht darüber liegt oder der Verlauf nichts hergibt.
    /// </summary>
    public static int? UeberGrenzeSeitMin(IReadOnlyList<HaVerlaufsPunkt>? feuchte, double grenze, DateTime jetztUtc)
        => SeitMinuten(feuchte, p => Zahlenlesen.Maschine(p.Zustand) is { } v && v > grenze, jetztUtc);

    /// <summary>Wie lange der Entfeuchter schon ohne Unterbrechung läuft — aus dem Verlauf seines Schalters.</summary>
    public static int? LaeuftSeitMin(IReadOnlyList<HaVerlaufsPunkt>? port, DateTime jetztUtc)
        => SeitMinuten(port, p => p.Zustand is "on" or "On", jetztUtc);

    /// <summary>
    /// Die Minuten seit dem letzten Punkt, an dem die Bedingung noch NICHT galt. Gilt sie über das ganze gelesene Fenster,
    /// zählt das Fenster (mindestens so lange). Ein unbekannter oder nicht erreichbarer Zustand beendet die Reihe nicht.
    /// </summary>
    private static int? SeitMinuten(IReadOnlyList<HaVerlaufsPunkt>? verlauf, Func<HaVerlaufsPunkt, bool> gilt, DateTime jetztUtc)
    {
        if (verlauf is not { Count: > 0 }) return null;
        var punkte = verlauf.Where(p => p.Zustand is not ("unknown" or "unavailable")).OrderBy(p => p.ZeitUtc).ToList();
        if (punkte.Count == 0 || !gilt(punkte[^1])) return null;

        var beginn = punkte[^1].ZeitUtc;
        for (var i = punkte.Count - 2; i >= 0 && gilt(punkte[i]); i--) beginn = punkte[i].ZeitUtc;
        return Math.Max(0, (int)Math.Round((jetztUtc - beginn).TotalMinutes));
    }

    private static string Zahl(double v) => v.ToString(Math.Abs(v - Math.Round(v)) < 0.05 ? "0" : "0.0", CultureInfo.GetCultureInfo("de-DE"));

    private static string Dauer(int min)
        => min < 60 ? $"{min} Min." : min % 60 == 0 ? $"{min / 60} h" : $"{min / 60} h {min % 60} Min.";
}
