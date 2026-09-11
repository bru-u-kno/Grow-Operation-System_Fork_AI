using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// Was ein Formular schickt, kommt auch wieder heraus.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (25.08.2026).</b> Das Flipdatum wurde still verworfen:
/// eingetragen, HTTP 200, unveränderter Wert zurück. Der Nutzer meldete es —
/// zum wiederholten Mal in derselben Klasse (toter Speichern-Knopf, 21 stille
/// Zahlenfelder, genullter Topf). Gezählt: <b>471 Felder</b> in 36 schreibenden
/// Verträgen, und <b>zwei</b> Testdateien prüften je, ob ein Feld die Runde
/// übersteht.</para>
///
/// <para><b>Wie diese Zählung arbeitet.</b> Für jeden PUT-Endpunkt mit
/// <c>{id:int}</c> holt sie den aktuellen Stand, ändert <b>ein einziges</b>
/// Feld, schickt ab, liest neu — und vergleicht. Ein Feld nach dem anderen,
/// weil nur so klar ist, welches verlorengeht. Genau dieser Ablauf hätte das
/// Flipdatum an dem Tag gefunden, an dem es geschrieben wurde.</para>
///
/// <para><b>Was sie bewusst nicht anfasst</b> — jeweils mit Grund, nicht
/// stillschweigend: Verweise auf andere Sachen (<c>…Id</c>) würden ins Leere
/// zeigen; Aufzählungstypen haben eigene Prüfungen
/// (<c>deutsche-woerter.node.test.ts</c>, <c>rohe-enums.spec.ts</c>); Listen
/// und verschachtelte Objekte brauchen einen eigenen Bauplan. Alles andere —
/// Text, Zahl, Ja/Nein, Datum — wird gefahren.</para>
/// </remarks>
[Collection(IntegrationsSammlung.Name)]
public sealed class RundwegVollstaendigTests
{
    /// <summary>Ein Datum weit in der Zukunft — siehe <see cref="NeuerWert"/>.</summary>
    private const string Probedatum = "2027-03-01";

    private readonly IntegrationsApp _app;

    public RundwegVollstaendigTests(IntegrationsApp app) => _app = app;

    /// <summary>Ein Endpunkt, den die Zählung fahren kann.</summary>
    /// <param name="Sammlung">Weg zur Liste, um eine echte Id zu finden.</param>
    /// <param name="Einzeln">Weg zum einzelnen Stück, mit <c>{id}</c> als Platzhalter.</param>
    /// <param name="Sammlung">
    /// Weg zur Liste, um eine echte Id zu finden — oder <c>null</c> bei einem
    /// Einzelstueck (Einstellungen gibt es genau einmal, ohne Id).
    /// </param>
    private sealed record Weg(string Name, string? Sammlung, string Einzeln, Type Vertrag);

    /// <summary>
    /// Die Grundmenge. Sie steht hier, weil Sammel- und Einzelweg sich nicht
    /// aus dem PUT allein ableiten lassen — <c>KeinWegFehlt</c> prüft dafür
    /// gegen die Reflexion, dass keiner vergessen wurde.
    /// </summary>
    private static readonly Weg[] Wege =
    [
        new("Grow", "/api/grows", "/api/grows/{id}", typeof(GrowUpsertRequest)),
        new("Pflanze", "/api/plants", "/api/plants/{id}", typeof(UpdatePlantInstanceRequest)),
        new("Sorte", "/api/strains", "/api/strains/{id}", typeof(UpdateStrainRequest)),
        new("Hydro-System", "/api/hydro-setups", "/api/hydro-setups/{id}", typeof(UpdateHydroSetupRequest)),
        new("Geraet", "/api/hardware-items", "/api/hardware-items/{id}", typeof(UpdateHardwareItemRequest)),
        new("Lichtplan", "/api/light-schedules?tentId=1", "/api/light-schedules/{id}", typeof(UpdateLightScheduleRequest)),
        new("Dosierpumpe", "/api/dosing/pumps", "/api/dosing/pumps/{id}", typeof(DosingPumpUpsertRequest)),
        new("Risiko", "/api/risk-events", "/api/risk-events/{id}", typeof(UpdateRiskEventRequest)),
        // Das meistbenutzte Formular der App — und bis zum 25.08.2026 von
        // keiner Rundweg-Pruefung beruehrt.
        new("Messung", "/api/grows/1/measurements", "/api/measurements/{id}", typeof(MeasurementUpsertRequest)),

        // Einzelstuecke: es gibt sie genau einmal, ohne Id.
        new("Kosten", null, "/api/costs/settings", typeof(CostsApiController.KostenEinstellungenRequest)),
        // Fork AI (forkai.6): welche HA-Entitaeten den Strom liefern.
        new("Strom-Quelle", null, "/api/kosten/strom-quelle", typeof(GrowDiary.Web.Models.StromQuelle)),
        new("Wasserprofil", null, "/api/water-profile", typeof(GrowDiary.Web.Models.WaterProfile)),
        new("Benachrichtigungen", null, "/api/notifications/settings", typeof(NotificationSettingsDto)),
        // Fork AI (forkai.13): wohin das Haus-Zeichen in der Titelzeile springt.
        // Die Reihenfolge selbst (Items) ist eine Liste und wird von dieser
        // Zaehlung ohnehin nicht gefahren — dafuer gibt es
        // NavBarApiControllerTests.
        new("Navigationsleiste", null, "/api/navbar", typeof(SaveNavBarRequest)),
    ];

    /// <summary>
    /// Felder, die bewusst nicht zurueckkommen — Schluessel „Weg.Feld".
    /// </summary>
    private static readonly Dictionary<string, string> Ausnahmen = new(StringComparer.Ordinal)
    {
        // --- Felder, die nur in einem bestimmten Fall gelten -----------------
        ["Grow.DaysAlreadyInPhase"] =
            "Gilt nur ausserhalb der Keimung und nicht fuer Autoflower "
            + "(GrowFormViewModel.NeedsDaysInPhase). Der Demobestand steigt in der "
            + "Keimung ein — dort ist der Grow an Tag 1, die Angabe waere sinnlos. "
            + "DASS das Formular sie trotzdem anbot, war ein echter Fehler: gefunden "
            + "von dieser Zaehlung, behoben in GrowSetupPage, festgehalten von "
            + "e2e/formularfelder-kommen-an.spec.ts.",
        ["Grow.AutoflowerDaysSinceGermination"] =
            "Eine Autoflower geht nach Tagen in die Bluete; der Demobestand faehrt "
            + "eine feminisierte Sorte. Das Formular bietet das Feld seit dem "
            + "25.08.2026 genau dann an, wenn es auch ankommt.",
        ["Grow.CloneSource"] =
            "Gilt nur, wenn der Grow aus einem Steckling stammt "
            + "(StartMaterial == Clone). Der Demobestand faehrt aus Samen; das "
            + "Formular bietet das Feld gar nicht an, es kommt aus dem Klon-Weg "
            + "einer Mutterpflanze.",
        ["Grow.CloneIsRooted"] =
            "Gilt nur fuer einen Grow aus Stecklingen (StartMaterial == Clone) und "
            + "entscheidet, ob die Saemlingsphase uebersprungen wird. Der "
            + "Demobestand faehrt aus Samen; das Formular bietet den Schalter nicht "
            + "an, er kommt aus dem Klon-Weg einer Mutterpflanze.",

        // --- Werte, die die App selbst ausrechnet ----------------------------
        ["Grow.ContainerSize"] =
            "Rechnet der Server aus dem Hydro-System aus (FormatPotSize, "
            + "GrowsApiController): etwa 4 x 27 L. Eine zweite, von Hand eingetragene "
            + "Wahrheit waere genau der Widerspruch, den EINE WAHRHEIT JE ZAHL "
            + "verhindern soll.",
        ["Grow.ReservoirSize"] =
            "Rechnet der Server ebenfalls aus dem Hydro-System aus "
            + "(FormatReservoirSize): Tankvolumen plus Toepfe. Von Hand eingetragen "
            + "waere es eine zweite Wahrheit ueber dieselbe Anlage.",
        ["Grow.PlantCount"] =
            "Sind Pflanzen EINZELN erfasst, sind sie die Wahrheit ueber die Anzahl "
            + "(seit 25.08.2026). Das Formular zeigt die Zahl seither nur noch an, "
            + "statt sie anzubieten.",

        // --- Schreiben ohne Speichern ---------------------------------------
        ["Dosierpumpe.TubeChangedNow"] =
            "Ein Befehl, kein Wert: der Schalter fuer den Schlauchwechsel setzt das "
            + "Wechseldatum und wird selbst nicht gespeichert. Ein Rundweg waere "
            + "hier sinnlos.",
        ["Dosierpumpe.Purpose"] =
            "Eine Aufzaehlung in Textkleidern (PhDown, PhUp, Nutrient, CalMag, "
            + "Custom). Ein unbekannter Text faellt bewusst auf Custom zurueck, "
            + "statt die Pumpe unbrauchbar zu machen.",

        // --- Fachregeln, die den Wert zu Recht ablehnen ----------------------
        ["Pflanze.SiteIndex"] =
            "Ein Topf traegt eine Pflanze (seit 25.08.2026). Die Zaehlung setzt "
            + "eins mehr (plus 1) — und trifft damit den Nachbartopf. Die Regel selbst "
            + "prueft ToepfeReichenNichtTests.",
        ["Messung.TakenAtLocal"] =
            "Heisst in der Antwort takenAt, nicht takenAtLocal: die Anfrage traegt "
            + "ORTSZEIT, die Antwort den Zeitpunkt. Die Zaehlung vergleicht nach "
            + "Namen und findet ihn deshalb nicht. Dass der Zeitpunkt ankommt, "
            + "prueft e2e/formular-rundweg.spec.ts am Messformular.",
        ["Messung.WaterFlow"] =
            "Eine Aufzaehlung in Textkleidern (WaterFlowLevel). Ein unbekannter "
            + "Text faellt bewusst auf null zurueck, statt die Messung "
            + "unbrauchbar zu machen — dieselbe Bauart wie der Pumpen-Zweck.",
        ["Wasserprofil.UpdatedAtUtc"] =
            "Setzt der Server bei jedem Speichern auf den Zeitpunkt des "
            + "Speicherns. Ein vom Aufrufer gesetzter Wert waere eine Luege "
            + "darueber, wann das Profil zuletzt geaendert wurde.",
        ["Risiko.StartedAtUtc"] =
            "Verschiebt man den Beginn in die Zukunft, liegen Bestaetigung, letzte "
            + "Sichtung und Erledigung davor — und die App lehnt das zu Recht ab. "
            + "Der Rundweg kann nur EIN Feld aendern und traefe hier immer diese "
            + "Ordnung.",
    };

    /// <summary>
    /// Eine Ausnahme, die es nicht mehr braucht, ist eine Luege im Code.
    /// </summary>
    /// <remarks>
    /// Die CRUD-Zaehlung hat mit derselben Pruefung sofort einen falschen
    /// Eintrag von mir gefunden. Hier reicht die Form: der Schluessel muss zu
    /// einem gefahrenen Weg gehoeren, und ein Grund muss einer sein.
    /// </remarks>
    [Fact]
    public void JedeAusnahmeGehoertZuEinemWegUndHatEinenGrund()
    {
        foreach (var (schluessel, grund) in Ausnahmen)
        {
            var teile = schluessel.Split('.');
            Assert.True(teile.Length == 2, $"Schluessel '{schluessel}' hat nicht die Form Weg.Feld.");

            var weg = Wege.SingleOrDefault(w => w.Name == teile[0]);
            Assert.True(weg is not null, $"Ausnahme fuer '{schluessel}': den Weg '{teile[0]}' gibt es nicht.");
            Assert.Contains(FahrbareFelder(weg!.Vertrag), f => f.Name == teile[1]);

            Assert.True(grund.Length >= 80,
                $"Der Grund fuer '{schluessel}' ist zu kurz, um einer zu sein: \"{grund}\"");
        }

        // Und die Gegenprobe, die der Pruefer vermisst hat: ein Eintrag fuer
        // einen Vertrag, den es nicht (mehr) gibt, ist ein Deckel ohne Topf.
        // Vorher liess sich hier ein erfundener Name eintragen, ohne dass ein
        // Fall rot wurde.
        var alleVertraege = typeof(PlantsApiController).Assembly
            .GetTypes()
            .Where(x => x.IsSubclassOf(typeof(ControllerBase)) && !x.IsAbstract)
            .SelectMany(x => x.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttributes<HttpPutAttribute>().Any())
            .SelectMany(m => m.GetParameters())
            .Where(x => x.GetCustomAttributes<FromBodyAttribute>().Any())
            .Select(x => x.ParameterType.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (name, grund) in NichtGefahren)
        {
            Assert.True(alleVertraege.Contains(name),
                $"Ausnahme fuer '{name}' — diesen PUT-Vertrag gibt es nicht (mehr). Weg damit.");
            Assert.True(grund.Length >= 80,
                $"Der Grund fuer '{name}' ist zu kurz, um einer zu sein: \"{grund}\"");
        }
    }

    // ------------------------------------------------------------ Grundmenge

    [Fact]
    public void DieZaehlungSiehtIhreGrundmenge()
    {
        Assert.True(Wege.Length >= 8, $"Nur {Wege.Length} Wege — das prueft zu wenig.");

        var felder = Wege.Sum(w => FahrbareFelder(w.Vertrag).Count);
        Assert.True(felder >= 40,
            $"Nur {felder} fahrbare Felder ueber alle Wege — die Zaehlung laeuft fast ins Leere.");
    }

    /// <summary>
    /// Kein PUT-Endpunkt fehlt in der Liste, ohne dass es jemand ausspricht.
    /// </summary>
    /// <remarks>
    /// Die Wege oben sind von Hand — Sammel- und Einzelweg stehen nirgends
    /// maschinenlesbar. Diese Pruefung haelt die Handarbeit ehrlich: sie zaehlt
    /// die PUT-Aktionen mit <c>{id:int}</c> ueber die Reflexion und verlangt,
    /// dass jeder Vertrag entweder gefahren oder ausdruecklich ausgenommen ist.
    /// </remarks>
    [Fact]
    public void KeinWegFehlt()
    {
        var vertraegeMitPut = typeof(PlantsApiController).Assembly
            .GetTypes()
            .Where(t => t.IsSubclassOf(typeof(ControllerBase)) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            // ALLE PUTs mit Koerper, nicht nur die mit {id:int}. Die erste
            // Fassung filterte auf diese eine Schreibweise — und uebersah damit
            // 15 Endpunkte, darunter das MESSFORMULAR
            // (measurements/{measurementId:int}). Der Pruefer hat es belegt: er
            // hat in RequestMapping ReservoirPh auf null gesetzt, also den pH
            // der Naehrloesung bei JEDER Messung still verworfen — und alle
            // 1442 Tests blieben gruen. Genau die Klasse, gegen die diese
            // Zaehlung gebaut ist.
            .Where(m => m.GetCustomAttributes<HttpPutAttribute>().Any())
            .SelectMany(m => m.GetParameters())
            .Where(p => p.GetCustomAttributes<FromBodyAttribute>().Any())
            .Select(p => p.ParameterType)
            .Distinct()
            .ToList();

        // Auf die VOLLE Menge geeicht. Der alte Wert (15) passte zur verkuerzten
        // Menge und haette den blinden Fleck nie gemeldet.
        Assert.True(vertraegeMitPut.Count >= 30,
            $"Nur {vertraegeMitPut.Count} PUT-Vertraege gefunden — die Erkennung greift nicht mehr.");

        var gefahren = Wege.Select(w => w.Vertrag).ToHashSet();
        var offen = vertraegeMitPut.Where(t => !gefahren.Contains(t) && !NichtGefahren.ContainsKey(t.Name))
            .Select(t => t.Name).ToList();

        Assert.True(offen.Count == 0,
            $"{offen.Count} PUT-Vertraege werden nicht auf Rundweg geprueft:\n  "
            + string.Join("\n  ", offen)
            + "\n\nEntweder in Wege aufnehmen, oder in NichtGefahren MIT Grund eintragen.");
    }

    /// <summary>Verträge ohne Rundweg — mit ausgeschriebenem Grund.</summary>
    private static readonly Dictionary<string, string> NichtGefahren = new(StringComparer.Ordinal)
    {
        ["UpdateAutoMeasurementConfigRequest"] =
            "Der Demobestand legt keine Auto-Messung an; ohne Bestand faehrt der "
            + "Rundweg ins Leere und waere gruen, ohne etwas zu pruefen.",
        // Fork AI (forkai.20)
        ["Co2Einstellungen"] =
            "Der Rundweg faehrt jedes Feld mit einer festen Probe (1 bzw. true). Die "
            + "CO2-Sollwerte haben durchgehend geprüfte Wertebereiche — eine Hysterese "
            + "von 1 ppm oder ein Impuls von 1 s sind keine gueltigen Eingaben, das PUT "
            + "lehnt sie mit 400 ab. Ein Rundweg, der nur Ablehnungen einsammelt, prueft "
            + "nichts. Die Felder fahren Co2SteuerungTests (Rechnung und Grenzen) und die "
            + "Oberflaechen-Erfassung auf /steuerung/co2.",
        // Fork AI (forkai.6)
        ["ArtikelRequest"] =
            "Der Demobestand legt keinen Verbrauchsartikel an; ohne Bestand faehrt der "
            + "Rundweg ins Leere und waere gruen, ohne etwas zu pruefen. Die Felder "
            + "fahren KostenSeiteTests und die Oberflaechen-Erfassung.",
        ["AnschaffungRequest"] =
            "Der Demobestand legt keine Anschaffung an; ohne Bestand faehrt der Rundweg ins "
            + "Leere und waere gruen, ohne etwas zu pruefen. Die Felder fahren KostenSeiteTests "
            + "und die Oberflaechen-Erfassung (Anlegen, Bearbeiten, zweimal speichern).",
        ["NachfuellungUpdateRequest"] =
            "Der Demobestand legt keine Nachfuellung an; ohne Bestand faehrt der Rundweg "
            + "ins Leere und waere gruen, ohne etwas zu pruefen. Der Vertrag ist fuer den "
            + "Grow-MCP und die API gedacht; die Oberflaeche loescht und erfasst neu.",
        ["ReplaceAutoMeasurementFieldMappingsRequest"] =
            "Traegt nur eine Liste von Zuordnungen — kein Feld, das einzeln "
            + "zurueckkommen muesste. Fuer Listen braucht es einen eigenen Bauplan.",
        ["CuringJarUpsertRequest"] =
            "Aushaerte-Glaeser legt der Demobestand erst nach einer Ernte an; der "
            + "Rundweg haette dort nichts zu fassen.",
        ["UpdateCalibrationEventRequest"] =
            "Der Vorgang haengt an einem Zustandsablauf (geplant -> erledigt) — ein "
            + "einzelnes Feld zu aendern ist dort nicht immer erlaubt. Gehoert in "
            + "einen eigenen Fall statt in die Reihenpruefung.",
        ["UpdateMaintenanceEventRequest"] =
            "Wie der Kalibriervorgang haengt auch die Wartung an einem "
            + "Zustandsablauf (geplant -> erledigt); ein einzelnes Feld zu aendern "
            + "ist dort nicht in jedem Zustand erlaubt. Gehoert in einen eigenen "
            + "Fall statt in die Reihenpruefung.",
        ["HarvestUpsertRequest"] =
            "Die Ernte gibt es erst, wenn ein Grow geerntet ist; im Demobestand "
            + "laeuft er noch.",
        ["SetpointProfileUpsertRequest"] =
            "Traegt sechs Phasen als verschachtelte Liste — kein flaches Feld, das "
            + "die Reihenpruefung anfassen koennte.",
        ["UpdateTentRequest"] =
            "Das Zelt traegt die Sensor-Zuordnungen als Liste und rettet einzelne "
            + "Felder ausdruecklich vor dem Ueberschreiben (SettingsApiController). "
            + "Dafuer gibt es eigene Faelle; die Reihenpruefung wuerde sie stoeren.",
        ["SaveTentAlertRulesRequest"] =
            "Traegt eine LISTE von Grenzwert-Regeln je Zelt, kein flaches Feld. "
            + "Dazu die Besonderheit, dass nur AKTIVE Zeilen gespeichert werden — "
            + "ein Haekchen weg heisst geloescht. Das gehoert in einen eigenen "
            + "Fall, nicht in die Reihenpruefung.",
        ["DashboardLayoutDto"] =
            "Die Anordnung der Live-Seite: Bereiche mit Kacheln, also verschachtelte "
            + "Listen. Ein einzelnes Feld gibt es dort nicht; fuer Listen braucht "
            + "die Zaehlung einen eigenen Bauplan.",
        ["UpdateSopStepInstanceRequest"] =
            "Ein Ablaufschritt haengt an einem Zustandsablauf (offen -> erledigt -> "
            + "uebersprungen); ein einzelnes Feld zu aendern ist dort nicht in jedem "
            + "Zustand erlaubt. Geprueft wird das von SopInstancesApiControllerTests.",
        ["PhenoEvaluationDto"] =
            "Die Pheno-Bewertung besteht aus Bloecken mit je mehreren Noten — "
            + "verschachtelt, und fehlende Bloecke werden bewusst herausgerechnet. "
            + "Eine Reihenpruefung ueber flache Felder trifft das nicht.",
        ["PhenoWeightsDto"] =
            "Die Gewichtung der Bewertungsbloecke; die Werte haengen voneinander ab "
            + "(sie ergeben zusammen die Note). Ein einzelnes Feld zu verschieben "
            + "aendert die Bedeutung der anderen mit.",
        ["SaveHomeAssistantSettingsRequest"] =
            "Traegt das Long-Lived-Token. Ein Geheimnis wird bewusst NICHT "
            + "zurueckgegeben — der Rundweg wuerde hier zu Recht scheitern, und "
            + "eine API, die Token zurueckliefert, waere der schlimmere Fehler.",
        ["NightRampRequest"] =
            "Die Nachtabsenkung schreibt geteilt: Schalter und Untergrenze an den "
            + "GROW, alle Kuehlerwerte ans ZELT. Eine Reihenpruefung ueber einen Weg "
            + "saehe nur die Haelfte. Geprueft wird sie von SteuerungsstandTests.",
        ["UseTargetsRequest"] =
            "Ein einzelner Schalter (Feedchart-Ziele ja/nein) am Mischplan eines "
            + "Grows. Sein Bewahren-Verhalten prueft GrowUpdatePreservationTests "
            + "ausdruecklich — dort gehoert es hin, nicht in die Reihe.",
        ["LevelRequest"] =
            "Die Begleitungsstufe (voll/wichtig/experte) ist eine Aufzaehlung mit "
            + "genau drei Werten; die Zaehlung fasst Aufzaehlungen grundsaetzlich "
            + "nicht an, weil ein erfundener Text dort nichts belegt.",
        ["GraceRequest"] =
            "Die Pumpen-Schonfrist in Minuten, ein einzelner Wert ohne Sammlung und "
            + "ohne Id. Ihr Verhalten haengt am Waechter, nicht am Speichern — "
            + "geprueft von AnlagenWatchServiceTests.",
        ["List`1"] =
            "Der AC-Test nimmt eine LISTE von Geraeten entgegen, keinen benannten "
            + "Vertrag. Die Reihenpruefung ueber Eigenschaften trifft dort nichts; "
            + "geprueft wird der Weg von AcSchreiberWegTests.",
        ["UpdateSetupRequest"] =
            "Der Demobestand legt kein Setup an (0 Zeilen in der Tabelle), also "
            + "haette der Rundweg dort nichts zu fassen und waere gruen, ohne etwas "
            + "zu pruefen. Sobald der Bestand ein Mutter- oder Quarantaene-Setup "
            + "mitbringt, gehoert dieser Weg in die Liste",
    };

    // ------------------------------------------------------------- Der Lauf

    [Fact]
    public async Task JedesFeldKommtZurueck()
    {
        var client = _app.IngressClient();
        var befunde = new List<string>();
        var gefahren = 0;

        foreach (var weg in Wege)
        {
            string einzeln;
            if (weg.Sammlung is null)
            {
                einzeln = weg.Einzeln;
            }
            else
            {
                var id = await ErsteId(client, weg.Sammlung);
                if (id is null)
                {
                    befunde.Add($"{weg.Name}: kein Bestand unter {weg.Sammlung} — der Rundweg prueft dort NICHTS.");
                    continue;
                }

                einzeln = weg.Einzeln.Replace("{id}", id.Value.ToString(), StringComparison.Ordinal);
            }
            var vorher = await Holen(client, einzeln);
            if (vorher is null)
            {
                befunde.Add($"{weg.Name}: {einzeln} gab keinen Stand zurueck.");
                continue;
            }

            foreach (var feld in FahrbareFelder(weg.Vertrag))
            {
                var schluessel = $"{weg.Name}.{feld.Name}";
                if (Ausnahmen.ContainsKey(schluessel)) continue;

                var name = JsonName(feld.Name);
                var neuerWert = NeuerWert(feld, vorher[name]);
                if (neuerWert is null) continue;

                var koerper = Grundkoerper(weg.Vertrag, vorher);
                koerper[name] = neuerWert;

                var antwort = await client.PutAsJsonAsync(einzeln, koerper);
                if (!antwort.IsSuccessStatusCode)
                {
                    var text = await antwort.Content.ReadAsStringAsync();
                    befunde.Add($"{schluessel}: PUT abgelehnt ({(int)antwort.StatusCode}) — {Kurz(text)}");
                    continue;
                }

                gefahren++;
                var nachher = await Holen(client, einzeln);
                var steht = nachher?[name];
                if (!Gleich(steht, neuerWert))
                {
                    befunde.Add(
                        $"{schluessel}: geschickt {Kurz(neuerWert.ToJsonString())}, "
                        + $"zurueck kam {Kurz(steht?.ToJsonString() ?? "nichts")}");
                }

                // Zurueckdrehen, damit das naechste Feld auf dem Ausgangsstand faehrt.
                await client.PutAsJsonAsync(einzeln, Grundkoerper(weg.Vertrag, vorher));
            }
        }

        Assert.True(gefahren >= 30,
            $"Nur {gefahren} Felder wirklich gefahren — die Zaehlung prueft zu wenig, "
            + "um etwas zu belegen.");

        Assert.True(befunde.Count == 0,
            $"{befunde.Count} Felder ueberleben den Rundweg nicht:\n  "
            + string.Join("\n  ", befunde)
            + "\n\nEntweder beheben, oder in Ausnahmen MIT Grund eintragen.");
    }

    // ------------------------------------------------------------- Werkzeug

    private static List<PropertyInfo> FahrbareFelder(Type vertrag)
        => vertrag.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .Where(p => IstFahrbar(p.PropertyType))
            // Verweise auf andere Sachen zeigten sonst ins Leere.
            .Where(p => !p.Name.EndsWith("Id", StringComparison.Ordinal))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToList();

    private static bool IstFahrbar(Type t)
    {
        var kern = Nullable.GetUnderlyingType(t) ?? t;
        if (kern.IsEnum) return false;                 // eigene Pruefungen, siehe Klassendoku
        return kern == typeof(string)
            || kern == typeof(bool)
            || kern == typeof(int)
            || kern == typeof(double)
            || kern == typeof(decimal)
            || kern == typeof(DateTime);
    }

    private static string JsonName(string name)
        => char.ToLowerInvariant(name[0]) + name[1..];

    private static async Task<int?> ErsteId(HttpClient client, string sammlung)
    {
        var antwort = await client.GetAsync(sammlung);
        if (!antwort.IsSuccessStatusCode) return null;
        var liste = JsonNode.Parse(await antwort.Content.ReadAsStringAsync()) as JsonArray;
        var erstes = liste?.FirstOrDefault();
        return erstes?["id"]?.GetValue<int>();
    }

    private static async Task<JsonObject?> Holen(HttpClient client, string weg)
    {
        var antwort = await client.GetAsync(weg);
        if (!antwort.IsSuccessStatusCode) return null;
        return JsonNode.Parse(await antwort.Content.ReadAsStringAsync()) as JsonObject;
    }

    /// <summary>Der aktuelle Stand als Anfrage — alles, was der Vertrag kennt.</summary>
    private static JsonObject Grundkoerper(Type vertrag, JsonObject stand)
    {
        var koerper = new JsonObject();
        foreach (var p in vertrag.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var name = JsonName(p.Name);
            if (stand[name] is { } wert)
            {
                koerper[name] = wert.DeepClone();
            }
        }
        return koerper;
    }

    private static JsonNode? NeuerWert(PropertyInfo feld, JsonNode? aktuell)
    {
        var kern = Nullable.GetUnderlyingType(feld.PropertyType) ?? feld.PropertyType;

        if (kern == typeof(bool))
        {
            var jetzt = aktuell?.GetValue<bool>() ?? false;
            return JsonValue.Create(!jetzt);
        }

        if (kern == typeof(DateTime))
        {
            // Weit in der Zukunft: viele Vorgaenge pruefen „nicht vor dem
            // Beginn". Ein Datum in der Vergangenheit wuerde zu Recht
            // abgelehnt — das waere ein Befund ueber den Test, nicht ueber
            // die App.
            return JsonValue.Create(Probedatum);
        }

        if (kern == typeof(int))
        {
            var jetzt = aktuell is null ? 0 : aktuell.GetValue<int>();
            return JsonValue.Create(jetzt + 1);
        }

        if (kern == typeof(double) || kern == typeof(decimal))
        {
            var jetzt = aktuell is null ? 0d : aktuell.GetValue<double>();
            return JsonValue.Create(jetzt + 1);
        }

        if (kern == typeof(string))
        {
            // Ortszeit-Felder tragen Datum UND Uhrzeit (TakenAtLocal).
            if (feld.Name.EndsWith("Local", StringComparison.Ordinal))
            {
                return JsonValue.Create(Probedatum + "T10:00");
            }

            // Uhrzeiten haben ein Format, das die App prueft (HH:mm).
            if (feld.Name.EndsWith("Time", StringComparison.Ordinal))
            {
                return JsonValue.Create("07:30");
            }

            // Datumsfelder werden als Text gefuehrt (StartDate, FlipDate …).
            if (feld.Name.EndsWith("Date", StringComparison.Ordinal)
                || feld.Name.EndsWith("At", StringComparison.Ordinal))
            {
                return JsonValue.Create(Probedatum);
            }
            return JsonValue.Create("Rundweg-Probe");
        }

        return null;
    }

    private static bool Gleich(JsonNode? steht, JsonNode geschickt)
    {
        if (steht is null) return false;
        var a = steht.ToJsonString().Trim('"');
        var b = geschickt.ToJsonString().Trim('"');
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;

        // Datum: der Server gibt „2027-03-01T00:00:00" zurueck.
        if (a.StartsWith(b, StringComparison.Ordinal)) return true;

        // …und bei UTC-Feldern in Weltzeit: aus dem Ortsdatum 2027-03-01 wird
        // 2027-02-28T23:00:00Z. Das ist richtig, kein Verlust — verglichen wird
        // deshalb der Zeitpunkt, nicht der Text.
        if (DateTime.TryParse(a, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal
                | System.Globalization.DateTimeStyles.AssumeLocal, out var da)
            && DateTime.TryParse(b, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal
                | System.Globalization.DateTimeStyles.AssumeLocal, out var db))
        {
            return Math.Abs((da - db).TotalHours) < 1;
        }

        // Zahl: 3 gegen 3.0.
        return double.TryParse(a, System.Globalization.NumberStyles.Any,
                   System.Globalization.CultureInfo.InvariantCulture, out var x)
            && double.TryParse(b, System.Globalization.NumberStyles.Any,
                   System.Globalization.CultureInfo.InvariantCulture, out var y)
            && Math.Abs(x - y) < 0.0001;
    }

    private static string Kurz(string text)
        => text.Length <= 160 ? text.Replace("\n", " ") : text[..160].Replace("\n", " ") + "…";
}
