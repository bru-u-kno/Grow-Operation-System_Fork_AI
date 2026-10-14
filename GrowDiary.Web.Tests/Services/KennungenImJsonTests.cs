using System.Text.Json.Nodes;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Xunit;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (forkai.220): Die Übersetzung der neuen Kennungen in die alten (Bestand) muss auch Namen finden, die in einem JSON-Text hinter einem
/// Apostroph stehen. <c>JsonNode.ToJsonString</c> schreibt ihn als <c>'</c>; die Ziffer davor machte die Namensgrenze zu und ließ sechs neue
/// Namen in der Zusatz-Regelung stehen — Helfer, die es im Bestand nicht gibt (der Zusatz lief mit Vorgaben statt mit den Werten des Nutzers).
/// </summary>
[Collection(BestandRaumSammlung.Name)]
public sealed class KennungenImJsonTests : IDisposable
{
    public KennungenImJsonTests() => Kennungen.Festlegen(KennungsRaum.Bestand);

    public void Dispose() => Kennungen.Festlegen(KennungsRaum.Neu);

    [Fact]
    public void EinNameHinterEinemJsonApostrophWirdUebersetzt()
    {
        var text = JsonValue.Create("{{ states('input_number.fork_entfeuchter_zusatz_mindestlaufzeit') | int(15) }}")!.ToJsonString();
        Assert.Contains("\\u0027input_number.fork_entfeuchter_zusatz_mindestlaufzeit", text);

        var uebersetzt = Kennungen.Uebersetzen(text, KennungsRaum.Bestand);

        Assert.Contains("input_number.trotec_zelt_mindestlaufzeit", uebersetzt);
        Assert.DoesNotContain("fork_entfeuchter", uebersetzt);
    }

    [Theory]
    [InlineData("states('input_number.fork_entfeuchter_hysterese')")]
    [InlineData("is_state('input_boolean.fork_entfeuchter_vpd_regelung','off')")]
    [InlineData("[sensor.fork_entfeuchter_feuchte_ein_aktiv]")]
    [InlineData("\"sensor.fork_entfeuchter_temp_max_aktiv\"")]
    public void UeberallDortWoEinNameStehtWirdUebersetzt(string text)
    {
        Assert.DoesNotContain("fork_entfeuchter", Kennungen.Uebersetzen(text, KennungsRaum.Bestand));
    }

    [Fact]
    public void EinLängererNameWirdNichtAnEinemKuerzerenZerschnitten()
    {
        // „…_feuchte_ein" ist der Vorspann von „…_feuchte_ein_aktiv": beide bleiben ganz.
        var uebersetzt = Kennungen.Uebersetzen("'input_number.fork_entfeuchter_feuchte_ein_nacht' 'sensor.fork_entfeuchter_feuchte_ein_aktiv'", KennungsRaum.Bestand);
        Assert.Equal("'input_number.trotec_feuchte_ein' 'sensor.trotec_feuchte_ein_aktiv'", uebersetzt);
    }

    [Fact]
    public void EinFremderNameMitDemselbenEndeBleibtUnberuehrt()
    {
        // Die Grenze davor gilt weiter: ein Buchstabe oder eine Ziffer vor der Entity-ID gehört zu einem anderen Namen.
        const string fremd = "x.input_number.meine_fork_entfeuchter_hysterese und input_number.fork_entfeuchter_hysterese_zwei";
        Assert.Equal(fremd, Kennungen.Uebersetzen(fremd, KennungsRaum.Bestand));
        Assert.Equal("a1input_number.fork_entfeuchter_hysterese", Kennungen.Uebersetzen("a1input_number.fork_entfeuchter_hysterese", KennungsRaum.Bestand));
    }

    [Fact]
    public void KeineMitgelieferteAutomationBehaeltImBestandNeueNamen()
    {
        var wurzel = SteuerungAutomationService.VorlagenWurzel;
        var geprueft = 0;
        var uebrig = new List<string>();
        foreach (var datei in Directory.GetFiles(wurzel, "*.json", SearchOption.AllDirectories))
        {
            var modul = Path.GetFileName(Path.GetDirectoryName(datei))!;
            var vorlage = (JsonObject)JsonNode.Parse(File.ReadAllText(datei))!;
            var rollen = SteuerungGeraeteRollen.FuerModulMitMitbenutzten(modul)
                .ToDictionary(r => r.Schluessel, r => $"{r.Domains[0]}.probe_{r.Schluessel}", StringComparer.Ordinal);
            var fertig = SteuerungAutomationService.Fuellen(vorlage, rollen);
            if (fertig is null) continue;
            geprueft++;

            var text = fertig.ToJsonString();
            foreach (var k in Kennungen.Tabelle.Where(k => text.Contains(k.Neu, StringComparison.Ordinal)))
            {
                uebrig.Add($"{modul}/{Path.GetFileNameWithoutExtension(datei)}: {k.NeueId}");
            }
        }

        Assert.True(geprueft >= 7, $"nur {geprueft} Vorlagen geprüft");
        Assert.True(uebrig.Count == 0, "Im Bestand blieben neue Namen stehen: " + string.Join(", ", uebrig.Distinct()));
    }
}
