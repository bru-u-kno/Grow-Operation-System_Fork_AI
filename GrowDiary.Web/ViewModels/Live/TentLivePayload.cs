namespace GrowDiary.Web.ViewModels.Live;

public sealed class TentLivePayload
{
    public int TentId { get; set; }
    public string StateTone { get; set; } = "neutral";
    public string StateLabel { get; set; } = "neutral";
    public List<MetricPayload> Metrics { get; set; } = new();
    public string? CameraUrl { get; set; }
    public DateTime RefreshedAtUtc { get; set; }

    /// <summary>Was der Kühler-Regler gerade tut; <c>null</c>, wenn er für dieses Zelt aus ist.</summary>
    /// <remarks>
    /// Absichtlich nur beim Einschalten gefüllt: eine Kachel, die dauerhaft
    /// „nicht eingerichtet" sagt, ist Rauschen auf dem Bildschirm, den man am
    /// häufigsten ansieht.
    /// </remarks>
    public KuehlerLivePayload? Chiller { get; set; }
}

/// <summary>Der Kühler auf der Live-Seite — Lage und Begründung.</summary>
/// <remarks>
/// <b>Warum der Grund mitkommt.</b> Ohne ihn sieht ein stehender Kühler bei
/// 21 °C wie ein Fehler aus, obwohl gerade die Mindestpause läuft. Der Satz
/// stammt aus <c>KuehlerService.Entscheiden</c> — derselben Rechnung, die
/// auch schaltet, nicht einer zweiten fürs Anzeigen.
/// </remarks>
public sealed class KuehlerLivePayload
{
    /// <summary>Die Steckdose, an der der Kühler hängt.</summary>
    public string SwitchEntityId { get; set; } = string.Empty;

    /// <summary>Der Sollwert, der jetzt gilt — Tag- oder Nachtwert.</summary>
    public double? SollC { get; set; }

    /// <summary>Die gemessene Wassertemperatur.</summary>
    public double? IstC { get; set; }

    /// <summary>Alter des Messwerts in Minuten; null, wenn unbekannt.</summary>
    public int? MesswertAlterMinuten { get; set; }

    /// <summary>Brennt das Licht? Entscheidet, welcher der beiden Sollwerte gilt.</summary>
    public bool Tagbetrieb { get; set; }

    /// <summary>Läuft der Kühler gerade? <c>null</c> = Zustand der Steckdose unbekannt.</summary>
    public bool? LaeuftGerade { get; set; }

    /// <summary>Was der Regler jetzt vorhat: „ein", „aus" oder „nichts".</summary>
    public string Schaltung { get; set; } = "nichts";

    /// <summary>Der ausgeschriebene Grund — genau der Satz, der auch ins Protokoll geht.</summary>
    public string Grund { get; set; } = string.Empty;
}

public sealed class MetricPayload
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = "-";
    public string? Unit { get; set; }
    public string Tone { get; set; } = "default";
    public string? Hint { get; set; }
    public double? NumericValue { get; set; }
    public double? TargetMin { get; set; }
    public double? TargetMax { get; set; }

    /// <summary>Woran der Zielbereich haengt, wenn er zurueckgerechnet ist — „bei 46 % RLF".</summary>
    public string? TargetNote { get; set; }

    /// <summary>Zurueckgerechnet statt aus dem Wissen: wird gezeigt, zaehlt aber nicht extra im Score.</summary>
    public bool TargetDerived { get; set; }

    /// <summary>Tag- und Nachtband nebeneinander, wo die Messgroesse eins hat.</summary>
    public double? TargetDayMin { get; set; }
    public double? TargetDayMax { get; set; }
    public double? TargetNightMin { get; set; }
    public double? TargetNightMax { get; set; }

    /// <summary>Welches der beiden Baender gerade gilt: <c>day</c> oder <c>night</c>.</summary>
    public string? TargetPhase { get; set; }

    /// <summary>Kurzer Status in der Ecke, wo es keine Bewertung gibt — „12/12" beim Licht.</summary>
    public string? StatusNote { get; set; }

    /// <summary>Schaltzeiten des Lichts als <c>HH:mm</c>; die Restzeit rechnet die Oberflaeche.</summary>
    public string? LightOnAt { get; set; }
    public string? LightOffAt { get; set; }

    /// <summary>Woher der Wert kommt: live (Sensor) oder hand (erfasste Messung).</summary>
    public string? ValueSource { get; set; }

    /// <summary>Alter der Handmessung in Minuten; null bei Live-Werten.</summary>
    public int? MeasuredAgeMinutes { get; set; }
}
