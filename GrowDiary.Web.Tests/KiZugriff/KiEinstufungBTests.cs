using System.Reflection;
using GrowDiary.Web.Api.Controllers;
using GrowDiary.Web.Controllers;
using GrowDiary.Web.Infrastructure.KiZugriff;
using Microsoft.AspNetCore.Mvc.Routing;

namespace GrowDiary.Web.Tests.KiZugriff;

/// <summary>
/// Fork AI (A-003, 03.10.2026): Die Einstufung der Geräte-, System- und
/// Verwaltungs-Controller — so, wie sie nach Lesen jeder Aktion entschieden wurde.
/// </summary>
/// <remarks>
/// <para>Die Vollständigkeit über ALLE Controller hält
/// <c>KiStufenVollstaendigTests</c>. Diese Datei hält die Entscheidungen fest:
/// wer eine Aktion umstuft, eine neue anlegt oder eine Sicherung vergisst, sieht
/// hier, dass er eine begründete Entscheidung ändert — und muss den Eintrag mit
/// ändern.</para>
/// <para>Ermittelt wird die <b>wirksame</b> Einstufung wie in der Middleware:
/// <see cref="KeinKiZugriffAttribute"/> an Aktion oder Klasse sperrt; sonst
/// gewinnt <see cref="KiStufeAttribute"/> an der Aktion vor dem an der Klasse.</para>
/// </remarks>
public sealed class KiEinstufungBTests
{
    private static readonly Type[] Controller =
    [
        typeof(AcTestApiController),
        typeof(DosingApiController),
        typeof(SteuerungApiController),
        typeof(GeraeteApiController),
        typeof(LevelCalibrationApiController),
        typeof(PumpWatchApiController),
        typeof(NightRampApiController),
        typeof(LightTransitionsApiController),
        typeof(HomeAssistantApiController),
        typeof(SystemApiController),
        typeof(GrowExportsApiController),
        typeof(SettingsApiController),
        typeof(ApiErrorController),
        typeof(AgentExportApiController),
        typeof(SensorHistoryApiController),
        typeof(SettingsController),
        typeof(GrowsController),
        typeof(TentsController),
        typeof(KnowledgeController),
        typeof(CameraProxyController),
    ];

    private const string Sicherung = " + Sicherung";
    private const string Gesperrt = "kein KI-Zugriff";

    /// <summary>Jede schreibende Aktion dieser Controller und ihre Einstufung.</summary>
    private static readonly Dictionary<string, string> Erwartet = new(StringComparer.Ordinal)
    {
        ["AcTestApiController.Speichern"] = "Verwaltung",
        ["AcTestApiController.Stufe"] = "GeraeteSchalten",
        ["AcTestApiController.Zeitplan"] = "GeraeteSchalten",

        ["DosingApiController.Create"] = "Verwaltung",
        ["DosingApiController.Update"] = "Verwaltung",
        ["DosingApiController.Delete"] = "Verwaltung" + Sicherung,
        ["DosingApiController.CalibrationRun"] = "GeraeteSchalten",
        ["DosingApiController.SaveCalibration"] = "Verwaltung",
        ["DosingApiController.Dose"] = "GeraeteSchalten",
        ["DosingApiController.Stop"] = "GeraeteSchalten",

        ["SteuerungApiController.Co2Speichern"] = "GeraeteSchalten, Verwaltung",
        ["SteuerungApiController.ZuluftSpeichern"] = "GeraeteSchalten, Verwaltung",
        ["SteuerungApiController.EntfeuchterSpeichern"] = "GeraeteSchalten, Verwaltung",
        // Fork AI (A-009): Speichern schreibt Helfer, schaltet die Automation und kann den
        // Zusatz ausschalten (Hilfe „aus") — wie der Entfeuchter. Die Namen sind nur Anzeige.
        ["SteuerungApiController.EntfeuchterZusatzSpeichern"] = "GeraeteSchalten, Verwaltung",
        ["SteuerungApiController.EntfeuchterNamenSpeichern"] = "Verwaltung",
        // Fork AI (A-015): sagt nur, wie viele Entfeuchter es gibt — die Regelung bleibt unberührt.
        ["SteuerungApiController.EntfeuchtungEinrichtungSpeichern"] = "Verwaltung",
        ["SteuerungApiController.ChillerSpeichern"] = "GeraeteSchalten, Verwaltung",
        ["SteuerungApiController.LichtSpeichern"] = "GeraeteSchalten, Verwaltung",
        ["SteuerungApiController.LichtBefehl"] = "GeraeteSchalten",
        ["SteuerungApiController.Probeschaltung"] = "GeraeteSchalten",
        ["SteuerungApiController.AutomationenAnlegen"] = "Verwaltung",
        ["SteuerungApiController.Absichern"] = "Verwaltung",
        ["SteuerungApiController.RechenwerteAnlegen"] = "Verwaltung",
        ["SteuerungApiController.HelferAnlegen"] = "Verwaltung",
        ["SteuerungApiController.GeraeteSpeichern"] = "Verwaltung",
        ["SteuerungApiController.EigenesGeraet"] = "Verwaltung",
        ["SteuerungApiController.EigenesGeraetLoeschen"] = "Verwaltung" + Sicherung,

        ["GeraeteApiController.EntitaetZuordnen"] = "Verwaltung",
        ["GeraeteApiController.Speichern"] = "Verwaltung",
        ["GeraeteApiController.RubrikAnlegen"] = "Verwaltung",
        ["GeraeteApiController.Verwerfen"] = "Verwaltung" + Sicherung,

        ["LevelCalibrationApiController.Start"] = "Dokumentieren",
        ["LevelCalibrationApiController.Finish"] = "Dokumentieren",
        ["LevelCalibrationApiController.Cancel"] = "Dokumentieren",

        ["PumpWatchApiController.SetGrace"] = "Verwaltung",
        ["NightRampApiController.Put"] = "Verwaltung",

        ["SystemApiController.RestorePlan"] = "Verwaltung",
        ["SystemApiController.CreateBackup"] = "Verwaltung",
        ["SystemApiController.RestoreBackup"] = Gesperrt,
        ["SystemApiController.UpgradePreflight"] = "Verwaltung",

        ["GrowExportsApiController.ImportGrow"] = "Verwaltung" + Sicherung,
        ["GrowExportsApiController.CreateImportPlan"] = "Verwaltung",
        ["GrowExportsApiController.ValidateExport"] = "Verwaltung",

        ["SettingsApiController.SaveHomeAssistant"] = Gesperrt,
        ["SettingsApiController.CreateTent"] = "Verwaltung",
        ["SettingsApiController.SaveTent"] = "Verwaltung",
        ["SettingsApiController.ArchiveTent"] = "Verwaltung",
        ["SettingsApiController.DeleteTent"] = "Verwaltung" + Sicherung,

        ["ApiErrorController.Error"] = Gesperrt,

        ["SettingsController.SaveHomeAssistant"] = Gesperrt,
        ["SettingsController.SaveTent"] = Gesperrt,
        ["GrowsController.ConfirmGermination"] = Gesperrt,
        ["GrowsController.ConfirmRooting"] = Gesperrt,
        ["GrowsController.FlipToFlower"] = Gesperrt,
    };

    [Fact]
    public void JedeSchreibendeAktion_IstSoEingestuftWieEntschieden()
    {
        var ist = SchreibendeAktionen().ToDictionary(a => a.Name, a => Einstufung(a.Methode), StringComparer.Ordinal);

        // Selbsttest: sieht die Zählung ihre Grundmenge?
        Assert.True(ist.Count >= 50, $"Nur {ist.Count} schreibende Aktionen gefunden — die Reflexion sieht die Controller nicht.");

        var abweichungen = Erwartet.Keys.Union(ist.Keys)
            .Where(name => Erwartet.GetValueOrDefault(name) != ist.GetValueOrDefault(name))
            .Select(name => $"{name}: erwartet „{Erwartet.GetValueOrDefault(name) ?? "(nicht in der Liste)"}\", "
                            + $"ist „{ist.GetValueOrDefault(name) ?? "(keine schreibende Aktion)"}\"")
            .ToList();

        Assert.True(abweichungen.Count == 0, "Einstufung weicht ab:\n" + string.Join("\n", abweichungen));
    }

    /// <summary>Jedes DELETE legt über einen Schlüssel vorher eine Sicherung an — sofern es überhaupt erreichbar ist.</summary>
    [Fact]
    public void JedesDelete_HatSicherungVorher()
    {
        var deletes = SchreibendeAktionen()
            .Where(a => a.Verben.Contains("DELETE") && !Gesperrt_(a.Methode))
            .ToList();

        Assert.True(deletes.Count >= 4, $"Nur {deletes.Count} DELETE-Aktionen gefunden — die Prüfung sieht ihre Grundmenge nicht.");
        var ohne = deletes.Where(a => a.Methode.GetCustomAttribute<KiSicherungVorherAttribute>(inherit: true) is null)
            .Select(a => a.Name).ToList();
        Assert.True(ohne.Count == 0, "DELETE ohne [KiSicherungVorher]: " + string.Join(", ", ohne));
    }

    /// <summary>Jede Sperre trägt einen ausgeschriebenen Grund.</summary>
    [Fact]
    public void JedeSperre_HatEinenGrund()
    {
        var sperren = SchreibendeAktionen()
            .Select(a => (a.Name, Attribut: a.Methode.GetCustomAttribute<KeinKiZugriffAttribute>(inherit: true)
                                            ?? a.Methode.DeclaringType!.GetCustomAttribute<KeinKiZugriffAttribute>(inherit: true)))
            .Where(s => s.Attribut is not null)
            .ToList();

        Assert.True(sperren.Count >= 3, $"Nur {sperren.Count} Sperren gefunden — die Prüfung sieht ihre Grundmenge nicht.");
        Assert.All(sperren, s => Assert.True(s.Attribut!.Grund.Trim().Length >= 20, $"{s.Name}: Grund „{s.Attribut.Grund}\" ist zu dünn."));
    }

    // ----------------------------------------------------------- Innenleben

    private sealed record Aktion(string Name, MethodInfo Methode, HashSet<string> Verben);

    private static IEnumerable<Aktion> SchreibendeAktionen()
        => Controller.SelectMany(typ => typ
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Select(methode => new Aktion(
                $"{typ.Name}.{methode.Name}",
                methode,
                methode.GetCustomAttributes<HttpMethodAttribute>(inherit: true)
                    .SelectMany(attribut => attribut.HttpMethods)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)))
            .Where(aktion => aktion.Verben.Any(verb => verb is not ("GET" or "HEAD"))));

    private static bool Gesperrt_(MethodInfo methode)
        => methode.GetCustomAttribute<KeinKiZugriffAttribute>(inherit: true) is not null
           || methode.DeclaringType!.GetCustomAttribute<KeinKiZugriffAttribute>(inherit: true) is not null;

    private static string Einstufung(MethodInfo methode)
    {
        if (Gesperrt_(methode)) return Gesperrt;

        var stufe = (methode.GetCustomAttribute<KiStufeAttribute>(inherit: true)
                     ?? methode.DeclaringType!.GetCustomAttribute<KiStufeAttribute>(inherit: true))?.Stufe ?? KiStufe.Keine;
        var sicherung = methode.GetCustomAttribute<KiSicherungVorherAttribute>(inherit: true) is not null;
        return stufe + (sicherung ? Sicherung : string.Empty);
    }
}
