using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (01.10.2026): Was aus den mitgelieferten Automations-Vorlagen wird —
/// geprüft an der gefüllten Automation, so wie sie nach Home Assistant geht.
/// </summary>
/// <remarks>
/// <para>Drei Fehler aus einer Durchsicht, alle von derselben Form: die gefüllte
/// Vorlage war gültig, Home Assistant hätte sie angenommen — und sie hätte nie
/// etwas getan.</para>
/// <list type="bullet">
/// <item>Die Dosierung prüfte vor dem Öffnen, ob das Ventil offen ist
/// (<c>port_zustand</c> „on"). Vor dem Öffnen ist es zu.</item>
/// <item>Ohne Abluft-Regler blieb die Prüfung auf
/// <c>input_boolean.co2_abluft_drosseln</c> stehen — ein Helfer, der ohne
/// Abluft-Regler gar nicht angelegt wird.</item>
/// <item>An ein Ventil an einem <c>switch</c> ging <c>select.select_option</c>.</item>
/// </list>
/// </remarks>
public sealed class AutomationsVorlagenStimmigTests
{
    private static string Wurzel => SteuerungAutomationService.VorlagenWurzel;

    private static JsonObject Laden(string modul, string vorlage)
        => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(Wurzel, modul, vorlage + ".json")))!;

    /// <summary>Jede Rolle des Moduls mit einer gültigen, unterscheidbaren Entität ihrer ersten Domäne.</summary>
    private static Dictionary<string, string> AlleRollen(string modul)
        => SteuerungGeraeteRollen.FuerModul(modul)
            .ToDictionary(r => r.Schluessel, r => $"{r.Domains[0]}.probe_{r.Schluessel}", StringComparer.Ordinal);

    private static Dictionary<string, string> OhneOptionale(string modul)
    {
        var pflicht = SteuerungGeraeteRollen.FuerModul(modul).Where(r => r.Pflicht).Select(r => r.Schluessel).ToHashSet();
        return AlleRollen(modul).Where(p => pflicht.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
    }

    /// <summary>Alle Objekte im Baum.</summary>
    private static IEnumerable<JsonObject> Objekte(JsonNode? knoten)
    {
        switch (knoten)
        {
            case JsonObject o:
                yield return o;
                foreach (var (_, kind) in o)
                {
                    foreach (var k in Objekte(kind)) yield return k;
                }
                break;
            case JsonArray a:
                foreach (var kind in a)
                {
                    foreach (var k in Objekte(kind)) yield return k;
                }
                break;
        }
    }

    /// <summary>
    /// Der Text, den Home Assistant auswertet — ohne Beschreibung und Aliase.
    /// Die nennen Entitäten, ohne sie zu benutzen (eine Erwähnung ist keine Verwendung).
    /// </summary>
    private static string Wirksam(JsonObject automation)
    {
        var kopie = (JsonObject)automation.DeepClone();
        foreach (var o in Objekte(kopie).ToList())
        {
            o.Remove("description");
            o.Remove("alias");
        }
        return kopie.ToJsonString();
    }

    // ------------------------------------------------ Befund 1: port_zustand

    [Fact]
    public void DieDosierungVerlangtVorDemOeffnenKeinOffenesVentil()
    {
        var rollen = AlleRollen("co2");
        var fertig = SteuerungAutomationService.Fuellen(Laden("co2", "dosierung"), rollen);
        Assert.NotNull(fertig);

        // Die Startbedingungen werden geprüft, bevor das Ventil zum ersten Mal
        // aufgeht. Eine Bedingung auf den Ventil-Zustand kann dort nie wahr sein.
        var bedingungen = fertig!["conditions"]!.AsArray();
        Assert.True(bedingungen.Count >= 4, "Die Vorlage hat keine Startbedingungen mehr — der Test sieht sie nicht.");
        Assert.DoesNotContain(rollen["port_zustand"], bedingungen.ToJsonString());
    }

    [Fact]
    public void DieDosierungPrueftDenPortOnlineNurMitZugeordneterRolle()
    {
        var mit = SteuerungAutomationService.Fuellen(Laden("co2", "dosierung"), AlleRollen("co2"))!;
        Assert.Contains(mit["conditions"]!.AsArray(), b
            => b?["condition"]?.ToString() == "state"
            && b["entity_id"]?.ToString() == AlleRollen("co2")["port_status"]
            && b["state"]?.ToString() == "on");

        var ohneStatus = AlleRollen("co2");
        ohneStatus.Remove("port_status");
        var ohne = SteuerungAutomationService.Fuellen(Laden("co2", "dosierung"), ohneStatus);
        Assert.NotNull(ohne); // die Rolle ist optional: ohne sie entfällt nur die Prüfung
        Assert.DoesNotContain("probe_port_status", ohne!.ToJsonString());
    }

    // ------------------------------------- Befund 3: Helfer ohne ihre Rolle

    /// <summary>
    /// Zählung über alle Vorlagen und zwei Rollen-Lagen: Keine gefüllte
    /// Automation verlangt einen Helfer oder Rechenwert des Katalogs, der bei
    /// diesen Rollen gar nicht angelegt wird (<see cref="SteuerungBauteile.Anwendbar"/>).
    /// </summary>
    [Fact]
    public void KeineVorlageVerlangtEinenHelferDerBeiIhrenRollenNichtAngelegtWird()
    {
        var dateien = Directory.GetFiles(Wurzel, "*.json", SearchOption.AllDirectories);
        Assert.True(dateien.Length >= 7, $"Nur {dateien.Length} Vorlagen gefunden — der Test sieht seine Grundmenge nicht.");

        var geprueft = 0;
        var verweise = 0;
        var fehler = new List<string>();
        foreach (var datei in dateien)
        {
            var modul = Path.GetFileName(Path.GetDirectoryName(datei))!;
            var vorlage = Path.GetFileNameWithoutExtension(datei);
            var katalog = SteuerungBauteile.FuerModul(modul).Where(b => b.Art != BauteilArt.Automation).ToList();

            foreach (var (lage, rollen) in new[] { ("alle Rollen", AlleRollen(modul)), ("nur Pflicht-Rollen", OhneOptionale(modul)) })
            {
                var json = Laden(modul, vorlage);
                if (SteuerungAutomationService.UeberfluessigWegen(json, rollen) is not null) continue;
                if (SteuerungAutomationService.Fuellen(json, rollen) is not { } fertig) continue;

                geprueft++;
                var text = Wirksam(fertig);
                var anwendbar = SteuerungBauteile.Anwendbar(modul, rollen.Keys.ToList()).Select(b => b.EntityId).ToHashSet();
                foreach (var b in katalog.Where(b => Regex.IsMatch(text, $@"\b{Regex.Escape(b.EntityId)}\b")))
                {
                    verweise++;
                    if (!anwendbar.Contains(b.EntityId))
                    {
                        fehler.Add($"{modul}/{vorlage} ({lage}): verlangt {b.EntityId}, der so nicht angelegt wird");
                    }
                }
            }
        }

        // Selbsttest: die Zählung hat wirklich gefüllte Vorlagen und Verweise gesehen.
        Assert.True(geprueft >= 8, $"Nur {geprueft} gefüllte Vorlagen geprüft.");
        Assert.True(verweise >= 20, $"Nur {verweise} Verweise auf Katalog-Helfer gefunden — die Suche greift nicht.");
        Assert.Empty(fehler);
    }

    // --------------------------------- Befund 4: Schaltbefehl je Domäne

    /// <summary>
    /// Zählung über alle Vorlagen, die eine Schalt-Rolle benutzen, und jede
    /// Domäne, die diese Rolle zulässt: kein <c>select</c>-Befehl an ein Gerät,
    /// das kein <c>select</c> ist, und jede Zustandsprüfung nennt einen Zustand,
    /// den das Gerät melden kann.
    /// </summary>
    [Fact]
    public void JederSchaltbefehlPasstZurDomaeneDesZugeordnetenGeraets()
    {
        var befehle = 0;
        var zustaende = 0;
        var fehler = new List<string>();
        foreach (var datei in Directory.GetFiles(Wurzel, "*.json", SearchOption.AllDirectories))
        {
            var modul = Path.GetFileName(Path.GetDirectoryName(datei))!;
            var vorlage = Path.GetFileNameWithoutExtension(datei);
            var text = File.ReadAllText(datei);

            foreach (var rolle in SteuerungGeraeteRollen.FuerModul(modul).Where(r => r.Gruppe == SteuerungGeraeteRollen.GruppeSchalten))
            {
                if (!text.Contains($"[[{rolle.Schluessel}]]", StringComparison.Ordinal)) continue;

                foreach (var domaene in rolle.Domains)
                {
                    var rollen = AlleRollen(modul);
                    var geraet = $"{domaene}.ventil_{rolle.Schluessel}";
                    rollen[rolle.Schluessel] = geraet;
                    var json = Laden(modul, vorlage);
                    if (SteuerungAutomationService.UeberfluessigWegen(json, rollen) is not null) continue;
                    if (SteuerungAutomationService.Fuellen(json, rollen) is not { } fertig) continue;

                    foreach (var o in Objekte(fertig))
                    {
                        var ziel = o["target"]?["entity_id"]?.ToString();
                        if (ziel == geraet && o["action"]?.ToString() is { } aktion)
                        {
                            befehle++;
                            // Ein select-Befehl an etwas, das kein select ist, geht ins
                            // Leere. (homeassistant.turn_on/off ist domänenfrei, und die
                            // Kühler-Vorlagen verzweigen zur Laufzeit nach der Domäne —
                            // beides ist hier nicht der Gegenstand.)
                            if (aktion.StartsWith("select.", StringComparison.Ordinal) && domaene != "select")
                            {
                                fehler.Add($"{modul}/{vorlage}: {aktion} an {geraet}");
                            }
                        }

                        if (o["entity_id"]?.ToString() == geraet && o["condition"]?.ToString() == "state")
                        {
                            zustaende++;
                            var soll = domaene == "select" ? new[] { "On", "Off" } : ["on", "off"];
                            if (o["state"]?.ToString() is { } z && !soll.Contains(z))
                            {
                                fehler.Add($"{modul}/{vorlage}: Zustand „{z}“ an {geraet}");
                            }
                        }
                    }
                }
            }
        }

        // Selbsttest: Befehle und Zustandsprüfungen an das Gerät wurden wirklich gefunden.
        Assert.True(befehle >= 15, $"Nur {befehle} Schaltbefehle gefunden — der Test sieht seine Grundmenge nicht.");
        Assert.True(zustaende >= 3, $"Nur {zustaende} Zustandsprüfungen gefunden.");
        Assert.True(fehler.Count == 0, string.Join("\n", fehler));
    }

    [Fact]
    public void EinSelectBehaeltSeinenBefehl()
    {
        var rollen = AlleRollen("co2");
        rollen["port_schalter"] = "select.rdwc_venti_aktiver_modus_2";
        var fertig = SteuerungAutomationService.Fuellen(Laden("co2", "dosierung"), rollen)!;

        var befehle = Objekte(fertig).Where(o => o["target"]?["entity_id"]?.ToString() == rollen["port_schalter"]).ToList();
        Assert.True(befehle.Count >= 4);
        Assert.All(befehle, b => Assert.Equal("select.select_option", b["action"]!.ToString()));
        Assert.Contains(befehle, b => b["data"]?["option"]?.ToString() == "On");
    }

    [Fact]
    public void DieAbsicherungErkenntEineDosierungAnEinemInputBoolean()
    {
        // Sonst fände sie eine so angelegte Dosierung nicht und sicherte sie nie ab.
        var rollen = AlleRollen("co2");
        rollen["port_schalter"] = "input_boolean.co2_ventil";
        var fertig = SteuerungAutomationService.Fuellen(Laden("co2", "dosierung"), rollen)!;

        Assert.True(Co2Absicherung.IstDosierung(fertig));
    }

    // -------------------------------------------- Fassungen der Vorlagen

    [Fact]
    public void JedeVorlageNenntIhrenEigenenPfadUndEineFassung()
    {
        var dateien = Directory.GetFiles(Wurzel, "*.json", SearchOption.AllDirectories);
        Assert.True(dateien.Length >= 7);
        foreach (var datei in dateien)
        {
            var modul = Path.GetFileName(Path.GetDirectoryName(datei))!;
            var vorlage = Path.GetFileNameWithoutExtension(datei);
            var beschreibung = Laden(modul, vorlage)["description"]!.GetValue<string>();

            Assert.Contains($"Herkunft: fork-ai/{modul}/{vorlage}/", beschreibung, StringComparison.Ordinal);
            Assert.True(SteuerungAutomationService.VorlagenFassung(modul, vorlage) >= 1, datei);
        }
    }

    [Fact]
    public void DieReparierteDosierungIstEineNeueFassung()
    {
        // Bestehende Installationen bekommen eine Vorlage nur angeboten, wenn
        // ihre Fassung höher ist als die in Home Assistant (SteuerungBestandService).
        Assert.Equal(4, SteuerungAutomationService.VorlagenFassung("co2", "dosierung"));
        Assert.Equal(3, SteuerungAutomationService.VorlagenFassung("co2", "waechter"));
        Assert.Equal(2, SteuerungAutomationService.VorlagenFassung("zuluft", "regelung"));
    }

    // ------------------------------------- Katalog und Vorlagen gehören zusammen

    [Fact]
    public void JedeVorlageHatGenauEinenKatalogEintragUndUmgekehrt()
    {
        var dateien = Directory.GetFiles(Wurzel, "*.json", SearchOption.AllDirectories)
            .Select(d => (Modul: Path.GetFileName(Path.GetDirectoryName(d))!, Vorlage: Path.GetFileNameWithoutExtension(d)))
            .ToList();
        Assert.True(dateien.Count >= 7);

        var eintraege = SteuerungBauteile.Alle.Where(b => b.VorlagenDatei is not null).ToList();
        foreach (var (modul, vorlage) in dateien)
        {
            Assert.Single(eintraege, b => b.Modul == modul && b.VorlagenDatei == vorlage);
        }
        foreach (var b in eintraege)
        {
            Assert.Equal(BauteilArt.Automation, b.Art);
            Assert.Contains((b.Modul, b.VorlagenDatei!), dateien);
        }
    }
}
