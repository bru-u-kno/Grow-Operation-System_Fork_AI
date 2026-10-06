using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests;

// Fork AI (A-010): Attrappen für die Tests des Probelaufs.

/// <summary>Eine Anlage zum Anfassen: Zustände, Protokoll, Fehler auf Wunsch.</summary>
internal sealed class FakeProbelaufHa : IProbelaufHa
{
    public Dictionary<string, string> Zustaende { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Protokoll { get; } = [];
    public List<ProbelaufRolle> AngefragteRollen { get; } = [];
    public Dictionary<string, string?> Geraete { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Regelungen { get; } = [];
    public HashSet<string> Haengt { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> AusschaltHaengt { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Action? BeiAenderung { get; set; }

    public Task<IReadOnlyList<string>> AutomationenAsync(string modul, ProbelaufRolle rolle, CancellationToken ct)
    {
        AngefragteRollen.Add(rolle);
        return Task.FromResult<IReadOnlyList<string>>(rolle == ProbelaufRolle.Pausieren ? Regelungen : []);
    }

    public string? Entity(string modul, string rolle) => Geraete.GetValueOrDefault(rolle);

    public Task<string?> ZustandAsync(string entityId, CancellationToken ct)
        => Task.FromResult(Zustaende.GetValueOrDefault(entityId));

    public Task<bool> AutomationSetzenAsync(string entityId, bool an, CancellationToken ct)
    {
        BeiAenderung?.Invoke();
        Protokoll.Add($"automation {(an ? "an" : "aus")} {entityId}");
        if (Haengt.Contains(entityId)) return Task.FromResult(false);
        Zustaende[entityId] = an ? "on" : "off";
        return Task.FromResult(true);
    }

    public Task<bool> AusschaltenAsync(string entityId, CancellationToken ct)
    {
        BeiAenderung?.Invoke();
        Protokoll.Add($"aus {entityId}");
        if (Haengt.Contains(entityId) || AusschaltHaengt.Contains(entityId)) return Task.FromResult(false);
        Zustaende[entityId] = "off";
        return Task.FromResult(true);
    }

    public Task<bool> ZustandHerstellenAsync(string entityId, string zustand, CancellationToken ct)
    {
        Protokoll.Add($"herstellen {entityId}={zustand}");
        if (Haengt.Contains(entityId)) return Task.FromResult(false);
        Zustaende[entityId] = zustand;
        return Task.FromResult(true);
    }
}

internal sealed class FakeChillerRegler : IChillerRegler
{
    public List<string> Protokoll { get; } = [];
    public IReadOnlyList<int> Betroffen { get; set; } = [1];

    public IReadOnlyList<int> Ausschalten() { Protokoll.Add("regler aus"); return Betroffen; }

    public void Einschalten(IEnumerable<int> zeltIds) => Protokoll.Add("regler an " + string.Join(",", zeltIds));
}


/// <summary>Das Zelt zum Anfassen: Werte setzen, Fühler ausfallen lassen, die Messung werfen lassen.</summary>
internal sealed class FakeProbelaufMessung : IProbelaufMessung
{
    public ProbelaufMesswerte? Aktuell { get; set; }
    public bool? TagPhase { get; set; } = true;
    public bool Wirft { get; set; }
    public IReadOnlyList<ProbelaufMesswerte> Verlauf { get; set; } = [];
    public ProbelaufGrenzen Voreinstellung { get; set; } = new(60, 27.5, 0.9, 1.4);

    public Task<ProbelaufMomentaufnahme?> JetztAsync(CancellationToken ct)
    {
        if (Wirft) throw new InvalidOperationException("Messung kaputt");
        return Task.FromResult(Aktuell is null ? null : new ProbelaufMomentaufnahme(Aktuell, TagPhase));
    }

    public Task<IReadOnlyList<ProbelaufMesswerte>> VerlaufAsync(DateTime vonUtc, DateTime bisUtc, CancellationToken ct)
        => Task.FromResult(Verlauf);

    public Task<ProbelaufGrenzen> VoreinstellungAsync(CancellationToken ct) => Task.FromResult(Voreinstellung);
}

internal sealed class FakeProbelaufMeldung : IProbelaufMeldung
{
    public List<string> Gesendet { get; } = [];

    public Task SendenAsync(string titel, string text, CancellationToken ct)
    {
        Gesendet.Add(titel);
        return Task.CompletedTask;
    }
}
