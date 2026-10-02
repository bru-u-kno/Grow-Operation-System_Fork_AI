using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>Ein Mangel an einem eingetragenen Sollwert.</summary>
public sealed record ProfilMangel(string Feld, string Meldung);

/// <summary>
/// Was in einem eigenen Sollwert-Profil stehen darf.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (01.09.2026).</b> Der Endpunkt für eigene Profile
/// prüfte Name und Basisprofil — die <b>Werte</b> gar nicht. Angenommen wurde
/// alles, was eine endliche Zahl ist.</para>
///
/// <para><b>Zwei Wege, die daran hängen:</b></para>
/// <list type="bullet">
///   <item><b>Vertauschte Grenzen.</b> <c>phMin 6,5</c> mit <c>phMax 5,5</c>
///   landet über <see cref="TargetValueService"/> im Urteil
///   <c>wert &lt; min ? Below : wert &gt; max ? Above : InTarget</c>. Danach ist
///   <b>jede</b> Messung „daneben", egal welcher Wert — und der Nutzer sucht
///   den Fehler an der Sonde.</item>
///   <item><b>Werte, die es nicht geben kann.</b> <c>waterTempNightC</c> geht
///   über <see cref="NachtabsenkungService"/> und den Schreiber an das
///   Zielgerät in Home Assistant, also an den Kühler im Zelt.</item>
/// </list>
///
/// <para><b>Die Grenzen stehen nicht hier.</b> Sie kommen aus
/// <see cref="MeasurementSanityService.PhysikalischeGrenzen"/> — derselben
/// Tabelle, die beim Speichern einer Messung sperrt. Zwei Tabellen für dieselbe
/// Frage wären zwei Wahrheiten, und genau daran ist dieses Projekt schon
/// dreimal hängengeblieben.</para>
/// </remarks>
public static class SetpointProfilGrenzen
{
    /// <summary>
    /// Die Messgrösse hinter einem Profilfeld — oder <c>null</c>, wenn es keine gibt.
    /// </summary>
    /// <remarks>
    /// Abgeleitet aus dem Namen, nicht aus einer zweiten Liste: <c>phMin</c>
    /// und <c>phMax</c> gehören zu <c>ph</c>, <c>waterTempDayC</c> und
    /// <c>waterTempNightC</c> zu <c>water-temp</c>. Kommt ein Feld dazu, fällt
    /// eine fehlende Zuordnung in der Zählung auf.
    /// </remarks>
    public static string? MessgroesseFuer(string feld)
    {
        var kern = feld;
        foreach (var endung in new[] { "Min", "Max", "DayC", "NightC" })
        {
            if (kern.EndsWith(endung, StringComparison.Ordinal))
            {
                kern = kern[..^endung.Length];
                break;
            }
        }

        var groesse = kern switch
        {
            "ph" => "ph",
            "ec" => "ec",
            "orp" => "orp",
            "waterTemp" => "water-temp",
            "vpd" => "vpd",
            "ppfd" => "ppfd",
            "co2" => "co2",
            _ => null,
        };

        return groesse is not null && MeasurementSanityService.PhysikalischeGrenzen.ContainsKey(groesse)
            ? groesse
            : null;
    }

    /// <summary>Alle Mängel einer Profiltabelle — leer heisst in Ordnung.</summary>
    /// <param name="stufen">Die Abweichungen je Phase, wie sie gespeichert werden sollen.</param>
    /// <param name="basis">
    /// Die Werte des Basisprofils für eine Phase (Phasenname → Feld → Wert), oder
    /// <c>null</c> ohne Basis. Gegen sie wird ein Paar geprüft, von dem nur eine
    /// Hälfte in den Abweichungen steht.
    /// </param>
    /// <remarks>
    /// <para><b>Zusammengesetzt prüfen (Fork AI, 02.10.2026).</b> Ein Paar wurde
    /// nur geprüft, wenn BEIDE Werte in den Abweichungen standen. <c>phMin 7</c>
    /// allein ging durch — und lag dann über dem <c>phMax 6,2</c> der Basis:
    /// jede pH-Messung „daneben". Geprüft wird jetzt das, was am Ende gilt:
    /// eigene Hälfte plus Basis-Hälfte, genau wie
    /// <see cref="SetpointProfileResolver.Apply"/> sie zusammensetzt.</para>
    /// </remarks>
    public static IReadOnlyList<ProfilMangel> Pruefe(
        IReadOnlyDictionary<string, Dictionary<string, double>>? stufen,
        Func<string, IReadOnlyDictionary<string, double>?>? basis = null)
    {
        var maengel = new List<ProfilMangel>();
        if (stufen is null) return maengel;

        foreach (var (phase, felder) in stufen)
        {
            foreach (var (feld, wert) in felder)
            {
                if (MessgroesseFuer(feld) is not { } groesse) continue;

                var (min, max) = MeasurementSanityService.PhysikalischeGrenzen[groesse];
                if (wert < min || wert > max)
                {
                    maengel.Add(new ProfilMangel(
                        $"{phase}.{feld}",
                        $"{wert:0.##} liegt ausserhalb dessen, was physikalisch vorkommen kann "
                        + $"({min:0}–{max:0}). Bitte Einheit pruefen."));
                }
            }

            // Und die Paare: was „Min" heisst, darf nicht ueber seinem „Max" liegen —
            // gemessen an dem, was nach dem Zusammensetzen mit der Basis gilt.
            var vonBasis = basis?.Invoke(phase);
            var minFelder = felder.Keys
                .Concat(SetpointProfile.Fields)
                .Where(k => k.EndsWith("Min", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal);
            foreach (var feld in minFelder)
            {
                var gegenstueck = feld[..^3] + "Max";
                var eigenUnten = felder.TryGetValue(feld, out var unten);
                var eigenOben = felder.TryGetValue(gegenstueck, out var oben);

                // Nichts davon angefasst: das Paar gehoert der Basis, nicht dem Nutzer.
                if (!eigenUnten && !eigenOben) continue;
                if (!eigenUnten && vonBasis?.TryGetValue(feld, out unten) != true) continue;
                if (!eigenOben && vonBasis?.TryGetValue(gegenstueck, out oben) != true) continue;
                if (unten <= oben) continue;

                var herkunft = (eigenUnten, eigenOben) switch
                {
                    (true, false) => " — die Obergrenze kommt aus dem Basisprofil",
                    (false, true) => " — die Untergrenze kommt aus dem Basisprofil",
                    _ => string.Empty,
                };

                maengel.Add(new ProfilMangel(
                    $"{phase}.{(eigenUnten ? feld : gegenstueck)}",
                    $"Die Untergrenze ({unten:0.##}) liegt ueber der Obergrenze "
                    + $"({oben:0.##}){herkunft}. So gespeichert waere danach jede Messung „daneben\", "
                    + "egal welcher Wert."));
            }
        }

        return maengel;
    }
}
