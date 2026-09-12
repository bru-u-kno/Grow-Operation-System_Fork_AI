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
/// zweitens das Geräteregister von Home Assistant, drittens — nur wenn beides
/// schweigt — die Vermutung aus dem Namen. Das Inventar definiert KEIN Gerät:
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
    /// dem HA-Register; ist es nicht erreichbar, greift die Namensvermutung.
    /// </summary>
    public async Task<IReadOnlyList<Geraet>> AlleAsync(CancellationToken ct)
    {
        var herkunft = await _register.HerkunftAsync(_haEinstellungen.GetEffectiveHomeAssistantSettings(), ct);
        return Zusammenfassen(
            Verwendungen(),
            herkunft,
            _hardware.GetHardwareItems(),
            _geraete.Geraete(),
            _geraete.Zuordnungen());
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
            var (schluessel, bestaetigt) = SchluesselFuer(entityId, quelle, zuordnungen);

            var eintrag = Holen(schluessel);
            eintrag.Entitaeten.Add(new GeraetEntitaet(entityId, liste));
            eintrag.Bestaetigt |= bestaetigt;
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
                eltern.Bestaetigt = true;
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
            eimerEintrag.Bestaetigt = true;
            eimerEintrag.Name ??= eintrag.Name;
        }

        var hardwareNachEntity = hardware
            .Where(h => !string.IsNullOrWhiteSpace(h.HaEntityId))
            .GroupBy(h => h.HaEntityId!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var geraete = new List<Geraet>();
        foreach (var (schluessel, eintrag) in eimer)
        {
            gespeichert.TryGetValue(schluessel, out var eigen);
            var hardwareItem = HardwareZu(schluessel, eintrag.Entitaeten, hardwareNachEntity, hardware, eigen);

            geraete.Add(new Geraet(
                schluessel,
                eigen?.Name ?? eintrag.Name ?? hardwareItem?.Name ?? GeraeteSchluessel.AlsName(schluessel),
                eigen?.TentId ?? hardwareItem?.TentId,
                eigen?.HardwareItemId ?? hardwareItem?.Id,
                eintrag.Entitaeten.OrderBy(e => e.EntityId, StringComparer.OrdinalIgnoreCase).ToList())
            {
                Bestaetigt = eintrag.Bestaetigt || eigen is not null,
                ElternSchluessel = eigen?.ElternSchluessel ?? eintrag.ElternSchluessel,
                Anschluss = eigen?.Anschluss ?? eintrag.Anschluss,
                IstController = eintrag.IstController,
                Modell = eintrag.Modell,
            });
        }

        // Sortierung: Controller zuerst, darunter ihre Ports der Reihe nach.
        return geraete
            .OrderBy(g => g.ElternSchluessel ?? g.Schluessel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.ElternSchluessel is null ? 0 : 1)
            .ThenBy(g => g.Anschluss, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Schlüssel eines Inventar-Eintrags ohne Entität — eigener Namensraum.</summary>
    public static string HardwareSchluessel(int hardwareItemId) => $"hw:{hardwareItemId}";

    /// <summary>Schlüssel eines Geräts aus dem HA-Register.</summary>
    public static string HaSchluessel(string deviceId) => $"ha:{deviceId}";

    private sealed class Eimer
    {
        public List<GeraetEntitaet> Entitaeten { get; } = new();
        public bool Bestaetigt { get; set; }
        public bool IstController { get; set; }
        public string? Name { get; set; }
        public string? ElternSchluessel { get; set; }
        public string? Anschluss { get; set; }
        public string? Modell { get; set; }
    }

    private static (string Schluessel, bool Bestaetigt) SchluesselFuer(
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
            return (HaSchluessel(herkunft!.DeviceId!), true);
        }

        return (GeraeteSchluessel.AusEntity(entityId), false);
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
