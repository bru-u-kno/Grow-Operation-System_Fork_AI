using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI (forkai.22): Sammelt alle Entitäten, die der Fork irgendwo benutzt,
/// und ordnet sie Geräten zu.
/// </summary>
/// <remarks>
/// <para><b>Nur lesend.</b> Diese Etappe ändert keine der bestehenden Tabellen —
/// sie legt die gemeinsame Sicht darüber. Wer eine Entität ändern will, tut das
/// vorerst weiter dort, wo sie heute gepflegt wird.</para>
///
/// <para><b>Reihenfolge der Wahrheit.</b> Erstens die Zuordnung des Nutzers,
/// zweitens das Geräteregister von Home Assistant. Schweigt beides, gehört die
/// Entität in das Sammelfach „Nicht zugeordnet" — der Fork rät nichts. Das
/// Inventar definiert KEIN Gerät:
/// es legt je Messgröße einen Eintrag an („pH", „EC", „Wassertemperatur"), und
/// die sind drei Sensoren EINES Bluelab, nicht drei Geräte. Ein Inventar-Eintrag
/// hängt sich deshalb an das Gerät seiner Entität.</para>
///
/// <para><b>Zwei Stufen.</b> Ein Controller ist ein Gerät, und was in seinen Ports
/// steckt, ist wieder eines. Home Assistant legt je Port ein eigenes Gerät an; die
/// MAC aus der <c>unique_id</c> klammert sie zum Controller zusammen, und die
/// Steckstelle steht als „Port 5" am Kind. Was AM Port hängt — Ventil, Ventilator,
/// Chiller — weiß Home Assistant nicht; den Namen trägt der Nutzer ein.</para>
/// </remarks>
public sealed class GeraeteUebersichtService
{
    /// <param name="RegisterErreichbar">Home Assistant hat sein Geräteregister geliefert.</param>
    public sealed record GeraeteStand(IReadOnlyList<Geraet> Geraete, bool RegisterErreichbar);

    private readonly GeraeteRepository _geraete;
    private readonly TentRepository _zelte;
    private readonly HardwareRepository _hardware;
    private readonly DosingRepository _dosierung;
    private readonly SteuerungGeraeteService _steuerung;
    private readonly KostenSeiteService _kosten;
    private readonly HomeAssistantRegistryService _register;
    private readonly HomeAssistantSettingsRepository _haEinstellungen;

    public GeraeteUebersichtService(
        GeraeteRepository geraete,
        TentRepository zelte,
        HardwareRepository hardware,
        DosingRepository dosierung,
        SteuerungGeraeteService steuerung,
        KostenSeiteService kosten,
        HomeAssistantRegistryService register,
        HomeAssistantSettingsRepository haEinstellungen)
    {
        _register = register;
        _haEinstellungen = haEinstellungen;
        _geraete = geraete;
        _zelte = zelte;
        _hardware = hardware;
        _dosierung = dosierung;
        _steuerung = steuerung;
        _kosten = kosten;
    }

    /// <summary>
    /// Alle Geräte mit ihren Entitäten und deren Verwendungen. Holt die Herkunft aus
    /// dem HA-Register. Ist es nicht erreichbar, belegt nichts ein Gerät — dann
    /// steht alles im Sammelfach, und <see cref="GeraeteStand.RegisterErreichbar"/>
    /// sagt der Seite, warum.
    /// </summary>
    public async Task<GeraeteStand> AlleAsync(CancellationToken ct)
    {
        var herkunft = await _register.HerkunftAsync(_haEinstellungen.GetEffectiveHomeAssistantSettings(), ct);
        var geraete = Zusammenfassen(
            Verwendungen(),
            herkunft,
            _hardware.GetHardwareItems(),
            _geraete.Geraete(),
            _geraete.Zuordnungen());
        return new GeraeteStand(geraete, herkunft.Count > 0);
    }

    // ------------------------------------------------------- Quellen einsammeln

    /// <summary>Jede Entität, die der Fork kennt, mit allem, wofür sie benutzt wird.</summary>
    public IReadOnlyDictionary<string, List<GeraetVerwendung>> Verwendungen()
    {
        var treffer = new Dictionary<string, List<GeraetVerwendung>>(StringComparer.OrdinalIgnoreCase);

        void Merken(string? entityId, string zweck, string quelle)
        {
            if (string.IsNullOrWhiteSpace(entityId)) return;
            var id = entityId.Trim();
            if (!treffer.TryGetValue(id, out var liste))
            {
                liste = new List<GeraetVerwendung>();
                treffer[id] = liste;
            }
            if (!liste.Any(v => v.Zweck == zweck && v.Quelle == quelle)) liste.Add(new GeraetVerwendung(zweck, quelle));
        }

        foreach (var zelt in _zelte.GetTents(includeArchived: true))
        {
            foreach (var sensor in zelt.Sensors)
            {
                Merken(sensor.HaEntityId, $"Messgröße {sensor.MetricType}", GeraetQuellen.Messgroesse);
            }

            Merken(zelt.LightControllerEntityId, "Licht-Controller", GeraetQuellen.ZeltTechnik);
            Merken(zelt.HvacControllerEntityId, "Klima-Controller", GeraetQuellen.ZeltTechnik);
            Merken(zelt.ChillerSwitchEntityId, "Chiller-Schalter", GeraetQuellen.ZeltTechnik);
            Merken(zelt.WaterTargetEntityId, "Wasser-Zielwert", GeraetQuellen.ZeltTechnik);
            foreach (var kamera in TentCameraList.Parse(zelt.CameraEntityIds, zelt.CameraEntityId))
            {
                Merken(kamera, "Kamera", GeraetQuellen.ZeltTechnik);
            }
        }

        foreach (var pumpe in _dosierung.GetPumps())
        {
            Merken(pumpe.HaEntityId, $"Dosierpumpe {pumpe.Name}", GeraetQuellen.Dosierung);
        }

        foreach (var modul in SteuerungGeraeteRollen.Module)
        {
            foreach (var rolle in SteuerungGeraeteRollen.FuerModul(modul))
            {
                Merken(_steuerung.Entity(modul, rolle.Schluessel),
                    $"Steuerung {modul.ToUpperInvariant()} · {rolle.Label}", GeraetQuellen.Steuerung);
            }
        }

        var strom = _kosten.StromQuelle;
        Merken(strom?.ZaehlerEntityId, "Stromzähler", GeraetQuellen.Strom);
        Merken(strom?.LeistungEntityId, "Leistungsmessung", GeraetQuellen.Strom);
        var zeltNamen = _zelte.GetTents(includeArchived: true).ToDictionary(z => z.Id, z => z.Name);
        foreach (var zelt in strom?.Zelte ?? [])
        {
            Merken(zelt.ZaehlerEntityId,
                $"Stromzähler {zeltNamen.GetValueOrDefault(zelt.TentId) ?? $"Zelt {zelt.TentId}"}", GeraetQuellen.Strom);
        }

        return treffer;
    }

    // ------------------------------------------------------------ Zusammenfassen

    /// <summary>Rein rechnend und ohne Datenbank — deshalb prüfbar.</summary>
    public static IReadOnlyList<Geraet> Zusammenfassen(
        IReadOnlyDictionary<string, List<GeraetVerwendung>> verwendungen,
        IReadOnlyDictionary<string, HerkunftEintrag> herkunft,
        IReadOnlyList<HardwareItem> hardware,
        IReadOnlyDictionary<string, GespeichertesGeraet> gespeichert,
        IReadOnlyDictionary<string, string> zuordnungen)
    {
        var eimer = new Dictionary<string, Eimer>(StringComparer.OrdinalIgnoreCase);

        Eimer Holen(string schluessel)
        {
            if (!eimer.TryGetValue(schluessel, out var vorhanden))
            {
                vorhanden = new Eimer();
                eimer[schluessel] = vorhanden;
            }
            return vorhanden;
        }

        foreach (var (entityId, liste) in verwendungen)
        {
            herkunft.TryGetValue(entityId, out var quelle);
            var (schluessel, vomNutzer) = SchluesselFuer(entityId, quelle, zuordnungen);

            var eintrag = Holen(schluessel);
            eintrag.Entitaeten.Add(new GeraetEntitaet(entityId, liste, vomNutzer, vomNutzer ? quelle?.DeviceName : null));

            // Das Sammelfach hat keine Herkunft, aus der sich Name, Modell oder
            // Controller lesen liessen — und keine Verbindung zum Register.
            if (schluessel == GeraeteSchluessel.Unzugeordnet)
            {
                eintrag.Name = "Nicht zugeordnet";
                eintrag.IstRubrik = true;
                eintrag.IstUnzugeordnet = true;
                continue;
            }

            // Eine zugewanderte Entität benennt ihr neues Gerät NICHT um: der Name
            // kam sonst von dem Gerät, aus dem sie stammt — eine Kamera hieß nach
            // dem Verschieben „RDWC CO2 + Light Sensor", und niemand fand sie wieder.
            if (vomNutzer) continue;

            eintrag.Name ??= quelle?.DeviceName;
            eintrag.Modell ??= quelle?.DeviceModell;

            // Zweite Stufe: an welchem Gerät hängt dieses? Home Assistant sagt es
            // selbst über via_device; die MAC ist nur der Notnagel für Integrationen,
            // die kein via_device setzen.
            var (mac, anschluss) = GeraeteHerkunft.Lesen(quelle?.UniqueId);
            eintrag.Anschluss ??= anschluss;

            string? controller = null;
            string? controllerName = null;
            string? controllerModell = null;
            if (!string.IsNullOrWhiteSpace(quelle?.ViaDeviceId))
            {
                controller = HaSchluessel(quelle!.ViaDeviceId!);
                controllerName = quelle.ViaDeviceName;
                controllerModell = quelle.ViaDeviceModell;
            }
            else if (mac is not null)
            {
                controller = GeraeteHerkunft.ControllerSchluessel(mac);
                controllerName = GeraeteHerkunft.ControllerName(mac);
            }

            if (controller is not null && !controller.Equals(schluessel, StringComparison.OrdinalIgnoreCase))
            {
                eintrag.ElternSchluessel ??= controller;

                var eltern = Holen(controller);
                eltern.IstController = true;
                eltern.Name ??= controllerName;
                eltern.Modell ??= controllerModell;
            }
        }

        // Inventar-Einträge OHNE Entität sind trotzdem Geräte: CO₂-Flasche,
        // Druckminderer, Netze. Sie fielen sonst aus der Liste, obwohl gerade sie
        // Wartung und Verschleiß tragen.
        foreach (var eintrag in hardware.Where(h => string.IsNullOrWhiteSpace(h.HaEntityId)))
        {
            var eimerEintrag = Holen(HardwareSchluessel(eintrag.Id));
            eimerEintrag.Name ??= eintrag.Name;
        }

        // Selbst angelegte Rubriken sind Faecher ohne Entitaeten — sie entstehen
        // nur hier, sonst faenden sie sich in keiner Quelle wieder.
        foreach (var (schluessel, eintrag) in gespeichert.Where(eintrag => eintrag.Value.IstRubrik))
        {
            var fach = Holen(schluessel);
            fach.IstRubrik = true;
            fach.Name ??= eintrag.Name;
        }

        MacPlatzhalterAufloesen(eimer);

        var hardwareNachEntity = hardware
            .Where(h => !string.IsNullOrWhiteSpace(h.HaEntityId))
            .GroupBy(h => h.HaEntityId!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var geraete = new List<Geraet>();
        foreach (var (schluessel, eintrag) in eimer)
        {
            gespeichert.TryGetValue(schluessel, out var eigen);
            // Das Sammelfach ist kein Gerät und hängt an keinem Inventar-Eintrag, auch
            // wenn eine seiner Entitäten dort eingetragen ist.
            var hardwareItem = eintrag.IstUnzugeordnet
                ? null
                : HardwareZu(schluessel, eintrag.Entitaeten, hardwareNachEntity, hardware, eigen);

            geraete.Add(new Geraet(
                schluessel,
                // Ein leer gespeicherter Name ist keine Korrektur, sondern das
                // Fehlen einer — dann gilt weiter, was Home Assistant sagt.
                (string.IsNullOrWhiteSpace(eigen?.Name) ? null : eigen!.Name)
                    ?? eintrag.Name ?? hardwareItem?.Name ?? schluessel,
                eigen?.TentId ?? hardwareItem?.TentId,
                eigen?.HardwareItemId ?? hardwareItem?.Id,
                eintrag.Entitaeten.OrderBy(e => e.EntityId, StringComparer.OrdinalIgnoreCase).ToList())
            {
                // Ein LEERER Eltern-Schlüssel ist die ausdrückliche Ansage „hängt an
                // nichts" — nicht dasselbe wie „nichts eingetragen", sonst liesse sich
                // ein von Home Assistant geerbter Controller nie aushängen.
                ElternSchluessel = eigen?.ElternSchluessel is { } gesetzt
                    ? (gesetzt.Length == 0 ? null : gesetzt)
                    : eintrag.ElternSchluessel,
                Anschluss = eigen?.Anschluss ?? eintrag.Anschluss,
                IstController = eintrag.IstController,
                Modell = eintrag.Modell,
                IstRubrik = eintrag.IstRubrik || (eigen?.IstRubrik ?? false),
                IstUnzugeordnet = eintrag.IstUnzugeordnet,
                ElternVomNutzer = eigen?.ElternSchluessel is not null,
                NameVomNutzer = !string.IsNullOrWhiteSpace(eigen?.Name),
                AbgeleiteterEltern = eintrag.ElternSchluessel,
            });
        }

        // Sortierung: Controller zuerst, darunter ihre Ports der Reihe nach.
        // Ein Eltern-Schlüssel, den es nicht (mehr) gibt — etwa nach dem Löschen
        // einer Rubrik — wird still fallen gelassen. Sonst hinge das Kind an einem
        // Geist: die Liste zeigt nur Wurzeln und ihre Kinder, das Gerät wäre weg.
        var bekannt = geraete.Select(g => g.Schluessel).ToHashSet(StringComparer.OrdinalIgnoreCase);
        geraete = geraete
            .Select(g => g.ElternSchluessel is { } eltern && !bekannt.Contains(eltern)
                ? g with { ElternSchluessel = null, Anschluss = null }
                : g)
            .ToList();

        return geraete
            .OrderBy(g => g.ElternSchluessel ?? g.Schluessel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.ElternSchluessel is null ? 0 : 1)
            .ThenBy(g => g.Anschluss, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Zu jeder Entität im Sammelfach ein Satz für die Seite: was fehlt, und — wenn
    /// die Steuerung es belegt — wohin sie vermutlich gehört. „Belegt" heißt: ein
    /// anderes Gerät trägt Entitäten desselben Steuerungs-Moduls, und es ist das
    /// EINZIGE. Bei mehreren Geräten im Modul gibt es keinen Vorschlag; der Fork
    /// wählt nicht aus.
    /// </summary>
    public static IReadOnlyList<string> Zuordnungshinweise(IReadOnlyList<Geraet> geraete)
    {
        var fach = geraete.FirstOrDefault(g => g.IstUnzugeordnet);
        if (fach is null) return [];

        var hinweise = new List<string>();
        foreach (var entitaet in fach.Entitaeten)
        {
            var module = entitaet.Verwendungen
                .Where(v => v.Quelle == GeraetQuellen.Steuerung)
                .Select(v => SteuerungsModul(v.Zweck))
                .Where(m => m is not null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            string? vorschlag = null;
            foreach (var modul in module)
            {
                var kandidaten = geraete
                    .Where(g => !g.IstRubrik && g.Entitaeten.Any(e => e.Verwendungen.Any(v =>
                        v.Quelle == GeraetQuellen.Steuerung
                        && string.Equals(SteuerungsModul(v.Zweck), modul, StringComparison.OrdinalIgnoreCase))))
                    .Select(g => g.Name)
                    .Distinct()
                    .ToList();
                if (kandidaten.Count == 1)
                {
                    vorschlag = $"{kandidaten[0]} (Steuerung {modul})";
                    break;
                }
            }

            hinweise.Add(vorschlag is null
                ? $"{entitaet.EntityId} gehört zu keinem Gerät — bitte einem Gerät zuweisen."
                : $"{entitaet.EntityId} gehört zu keinem Gerät. Die Steuerung stellt sie zu {vorschlag} — dorthin zuweisen?");
        }

        return hinweise;
    }

    /// <summary>„Steuerung BLUELAB · Grenze setzen" → „BLUELAB".</summary>
    private static string? SteuerungsModul(string zweck)
    {
        const string praefix = "Steuerung ";
        if (!zweck.StartsWith(praefix, StringComparison.Ordinal)) return null;
        var ende = zweck.IndexOf(" · ", StringComparison.Ordinal);
        return ende < 0 ? zweck[praefix.Length..].Trim() : zweck[praefix.Length..ende].Trim();
    }

    /// <summary>Schlüssel eines Inventar-Eintrags ohne Entität — eigener Namensraum.</summary>
    public static string HardwareSchluessel(int hardwareItemId) => $"hw:{hardwareItemId}";

    /// <summary>Schlüssel eines Geräts aus dem HA-Register.</summary>
    public static string HaSchluessel(string deviceId) => $"ha:{deviceId}";

    private sealed class Eimer
    {
        public List<GeraetEntitaet> Entitaeten { get; } = new();
        public bool IstController { get; set; }
        public string? Name { get; set; }
        public string? ElternSchluessel { get; set; }
        public string? Anschluss { get; set; }
        public string? Modell { get; set; }
        public bool IstRubrik { get; set; }
        public bool IstUnzugeordnet { get; set; }
    }

    private static (string Schluessel, bool VomNutzer) SchluesselFuer(
        string entityId,
        HerkunftEintrag? herkunft,
        IReadOnlyDictionary<string, string> zuordnungen)
    {
        if (zuordnungen.TryGetValue(entityId, out var gesetzt) && !string.IsNullOrWhiteSpace(gesetzt))
        {
            return (gesetzt, true);
        }

        if (!string.IsNullOrWhiteSpace(herkunft?.DeviceId))
        {
            return (HaSchluessel(herkunft!.DeviceId!), false);
        }

        return (GeraeteSchluessel.Unzugeordnet, false);
    }

    /// <summary>
    /// Ein Controller ist EIN Gerät. Die MAC in der <c>unique_id</c> klammert Ports
    /// nur dann zu einem Platzhalter-Controller, wenn Home Assistant keinen
    /// Controller kennt. Kennt er einen — erkennbar daran, dass er selbst Ports
    /// über <c>via_device</c> trägt und eine eigene Entität mit derselben MAC
    /// hat —, hängen die Ports an ihm, und der Platzhalter entfällt.
    /// </summary>
    /// <remarks>
    /// Der Controller trug sonst doppelt: als „RDWC" und als Platzhalter
    /// „Controller 4C16", und „RDWC" hing als „Fühler 7" unter dem Platzhalter,
    /// weil die unique_id seiner eigenen Sensoren (<c>…_sensor_7_…</c>) wie die
    /// eines Kindes aussieht.
    /// </remarks>
    private static void MacPlatzhalterAufloesen(Dictionary<string, Eimer> eimer)
    {
        var macZuController = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (schluessel, eintrag) in eimer)
        {
            if (eintrag.IstController
                && schluessel.StartsWith("ha:", StringComparison.Ordinal)
                && eintrag.ElternSchluessel is { } eltern
                && eltern.StartsWith("mac:", StringComparison.OrdinalIgnoreCase))
            {
                macZuController[eltern] = schluessel;
            }
        }

        foreach (var (schluessel, eintrag) in eimer.ToList())
        {
            if (eintrag.ElternSchluessel is not { } platzhalter
                || !macZuController.TryGetValue(platzhalter, out var controller))
            {
                continue;
            }

            if (controller.Equals(schluessel, StringComparison.OrdinalIgnoreCase))
            {
                // Der Controller selbst, nicht sein eigenes Kind.
                eintrag.ElternSchluessel = null;
                eintrag.Anschluss = null;
            }
            else
            {
                eintrag.ElternSchluessel = controller;
            }
        }

        foreach (var platzhalter in macZuController.Keys)
        {
            if (eimer.TryGetValue(platzhalter, out var leer) && leer.Entitaeten.Count == 0)
            {
                eimer.Remove(platzhalter);
            }
        }
    }

    private static HardwareItem? HardwareZu(
        string schluessel,
        IReadOnlyList<GeraetEntitaet> entitaeten,
        IReadOnlyDictionary<string, HardwareItem> hardwareNachEntity,
        IReadOnlyList<HardwareItem> hardware,
        GespeichertesGeraet? eigen)
    {
        if (eigen?.HardwareItemId is { } id) return hardware.FirstOrDefault(h => h.Id == id);

        if (schluessel.StartsWith("hw:", StringComparison.Ordinal)
            && int.TryParse(schluessel[3..], out var hardwareId))
        {
            return hardware.FirstOrDefault(h => h.Id == hardwareId);
        }

        // Ein Inventar-Eintrag hängt sich an das Gerät SEINER Entität — er definiert
        // keines. Trägt ein Gerät mehrere Einträge (Bluelab: pH, EC, Temperatur),
        // ist der erste die Verbindung; die Geräteseite zeigt später alle.
        foreach (var entitaet in entitaeten)
        {
            if (hardwareNachEntity.TryGetValue(entitaet.EntityId, out var item)) return item;
        }

        return null;
    }
}
