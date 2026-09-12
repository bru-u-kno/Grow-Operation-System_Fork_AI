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
/// vorerst weiter dort, wo sie heute gepflegt wird; die spätere Etappe dreht das
/// um.</para>
///
/// <para><b>Reihenfolge der Wahrheit.</b> Erstens die Zuordnung des Nutzers
/// (<see cref="GeraeteRepository"/>), zweitens das Inventar (ein Hardware-Eintrag
/// mit dieser Entity-ID IST das Gerät), drittens die Vermutung aus dem Namen.
/// Geraten wird nur, was sonst niemand beantwortet — und es ist als Vermutung
/// gekennzeichnet, damit die Oberfläche es nicht als Tatsache zeigt.</para>
/// </remarks>
public sealed class GeraeteUebersichtService
{
    private readonly GeraeteRepository _geraete;
    private readonly TentRepository _zelte;
    private readonly HardwareRepository _hardware;
    private readonly DosingRepository _dosierung;
    private readonly SteuerungGeraeteService _steuerung;
    private readonly KostenSeiteService _kosten;

    public GeraeteUebersichtService(
        GeraeteRepository geraete,
        TentRepository zelte,
        HardwareRepository hardware,
        DosingRepository dosierung,
        SteuerungGeraeteService steuerung,
        KostenSeiteService kosten)
    {
        _geraete = geraete;
        _zelte = zelte;
        _hardware = hardware;
        _dosierung = dosierung;
        _steuerung = steuerung;
        _kosten = kosten;
    }

    /// <summary>Alle Geräte mit ihren Entitäten und deren Verwendungen, nach Namen.</summary>
    public IReadOnlyList<Geraet> Alle()
    {
        var verwendungen = Verwendungen();
        var hardware = _hardware.GetHardwareItems();
        return Zusammenfassen(verwendungen, hardware, _geraete.Geraete(), _geraete.Zuordnungen());
    }

    // ------------------------------------------------------- Quellen einsammeln

    /// <summary>
    /// Jede Entität, die der Fork kennt, mit allem, wofür sie benutzt wird.
    /// Öffentlich, damit der Test die Zusammenfassung ohne Datenbank fahren kann.
    /// </summary>
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

    /// <summary>
    /// Aus Verwendungen, Inventar und den Korrekturen des Nutzers die Geräteliste
    /// bauen. Rein rechnend und ohne Datenbank — deshalb prüfbar.
    /// </summary>
    public static IReadOnlyList<Geraet> Zusammenfassen(
        IReadOnlyDictionary<string, List<GeraetVerwendung>> verwendungen,
        IReadOnlyList<HardwareItem> hardware,
        IReadOnlyDictionary<string, GespeichertesGeraet> gespeichert,
        IReadOnlyDictionary<string, string> zuordnungen)
    {
        var hardwareNachEntity = hardware
            .Where(h => !string.IsNullOrWhiteSpace(h.HaEntityId))
            .GroupBy(h => h.HaEntityId!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var eimer = new Dictionary<string, (List<GeraetEntitaet> Entitaeten, bool Bestaetigt)>(StringComparer.OrdinalIgnoreCase);

        foreach (var (entityId, liste) in verwendungen)
        {
            var (schluessel, bestaetigt) = SchluesselFuer(entityId, hardwareNachEntity, zuordnungen);
            if (!eimer.TryGetValue(schluessel, out var eintrag))
            {
                eintrag = (new List<GeraetEntitaet>(), false);
            }
            eintrag.Entitaeten.Add(new GeraetEntitaet(entityId, liste));
            eimer[schluessel] = (eintrag.Entitaeten, eintrag.Bestaetigt || bestaetigt);
        }

        // Inventar-Einträge OHNE Entität sind trotzdem Geräte: CO₂-Flasche,
        // Druckminderer, Netze. Sie fielen sonst aus der Liste, obwohl gerade sie
        // Wartung und Verschleiß tragen.
        foreach (var eintrag in hardware.Where(h => string.IsNullOrWhiteSpace(h.HaEntityId)))
        {
            var schluessel = HardwareSchluessel(eintrag.Id);
            if (!eimer.ContainsKey(schluessel)) eimer[schluessel] = (new List<GeraetEntitaet>(), true);
        }

        var geraete = new List<Geraet>();
        foreach (var (schluessel, eintrag) in eimer)
        {
            gespeichert.TryGetValue(schluessel, out var eigen);
            var hardwareItem = HardwareZu(schluessel, eintrag.Entitaeten, hardwareNachEntity, hardware, eigen);

            geraete.Add(new Geraet(
                schluessel,
                eigen?.Name ?? hardwareItem?.Name ?? GeraeteSchluessel.AlsName(schluessel),
                eigen?.TentId ?? hardwareItem?.TentId,
                eigen?.HardwareItemId ?? hardwareItem?.Id,
                eintrag.Entitaeten.OrderBy(e => e.EntityId, StringComparer.OrdinalIgnoreCase).ToList())
            {
                Bestaetigt = eintrag.Bestaetigt || eigen is not null,
            });
        }

        return geraete.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Schlüssel eines Inventar-Eintrags ohne Entität — eigener Namensraum, damit nichts kollidiert.</summary>
    public static string HardwareSchluessel(int hardwareItemId) => $"hw:{hardwareItemId}";

    private static (string Schluessel, bool Bestaetigt) SchluesselFuer(
        string entityId,
        IReadOnlyDictionary<string, HardwareItem> hardwareNachEntity,
        IReadOnlyDictionary<string, string> zuordnungen)
    {
        if (zuordnungen.TryGetValue(entityId, out var gesetzt) && !string.IsNullOrWhiteSpace(gesetzt))
        {
            return (gesetzt, true);
        }

        if (hardwareNachEntity.TryGetValue(entityId, out var item))
        {
            return (HardwareSchluessel(item.Id), true);
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

        // Ein geratenes Gerät kann trotzdem einen Inventar-Eintrag haben, wenn EINE
        // seiner Entitäten dort steht — das ist der übliche Fall beim Bluelab, wo
        // nur der pH-Fühler im Inventar geführt wird.
        foreach (var entitaet in entitaeten)
        {
            if (hardwareNachEntity.TryGetValue(entitaet.EntityId, out var item)) return item;
        }

        return null;
    }
}
