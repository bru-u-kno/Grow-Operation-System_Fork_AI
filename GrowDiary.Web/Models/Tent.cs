using GrowDiary.Web.Services;

namespace GrowDiary.Web.Models;

public sealed class Tent
{
    public int Id { get; set; }

    /// <summary>Die Entität, in die der Wassertemperatur-Sollwert geschrieben wird.</summary>
    /// <remarks>
    /// Ein Thermostat (`climate.…`) oder ein Zahlenfeld (`number.…`,
    /// `input_number.…`). Bewusst getrennt vom Chiller-SENSOR: das eine liest,
    /// das andere stellt. Leer heisst: es wird nichts geschrieben.
    /// </remarks>
    public string? WaterTargetEntityId { get; set; }

    /* ---------- Kuehler ueber eine smarte Steckdose ---------- */

    /// <summary>Die Steckdose, an der der Wasserkuehler haengt.</summary>
    /// <remarks>
    /// <para>Ein Schalter (<c>switch.…</c>). <b>Drei Entitaeten, drei
    /// Aufgaben</b>, und sie sind bewusst getrennt:</para>
    /// <list type="bullet">
    ///   <item><see cref="WaterTargetEntityId"/> STELLT einen Sollwert an einem
    ///   Thermostat, das selbst regelt.</item>
    ///   <item>Der Chiller-SENSOR (<c>SensorMetricType.Chiller</c>) LIEST, ob
    ///   der Kuehler laeuft — daran haengt der Waechter.</item>
    ///   <item>Diese hier SCHALTET ihn.</item>
    /// </list>
    /// <para>Wer sie verwechselt, schreibt einen Sollwert an eine Steckdose
    /// oder schaltet einen Sensor.</para>
    /// </remarks>
    public string? ChillerSwitchEntityId { get; set; }

    /// <summary>Darf Grow OS den Kuehler selbst schalten?</summary>
    /// <remarks>
    /// Standard <b>aus</b>. Etwas, das einen Kompressor taktet, schaltet sich
    /// nicht von selbst ein, weil jemand ein Update eingespielt hat.
    /// </remarks>
    public bool ChillerControlEnabled { get; set; }

    /// <summary>Das halbe Totband um den Sollwert, in Grad.</summary>
    /// <remarks>
    /// An bei Soll + Hysterese, aus bei Soll − Hysterese. Am Zelt und nicht im
    /// Profil, weil es an der Traegheit des Beckens haengt und nicht an der
    /// Pflanzenphase: 100 Liter schwingen anders als 30.
    /// </remarks>
    public double ChillerHysteresisC { get; set; } = KuehlerService.StandardHystereseC;

    /// <summary>Mindestlaufzeit des Kompressors in Minuten.</summary>
    public int ChillerMinRunMinutes { get; set; } = KuehlerService.StandardMindestlaufMinuten;

    /// <summary>Mindestpause des Kompressors in Minuten.</summary>
    /// <remarks>
    /// Die wichtigere der beiden Sperren: ein Kaeltekompressor braucht die
    /// Druckangleichung, bevor er wieder anlaufen darf.
    /// </remarks>
    public int ChillerMinPauseMinutes { get; set; } = KuehlerService.StandardMindestpauseMinuten;

    /// <summary>Wie alt die gemessene Wassertemperatur hoechstens sein darf.</summary>
    /// <remarks>
    /// Auf einen halbstuendigen Wert zu regeln ist etwas anderes, als ihn
    /// anzuzeigen. Dieselbe Sicherung wie <c>DosingPump.MaxReadingAgeMinutes</c>.
    /// </remarks>
    public int ChillerMaxReadingAgeMinutes { get; set; } = KuehlerService.StandardHoechstalterMinuten;
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = "Grow Tent";
    public TentType TentType { get; set; } = TentType.MultiPurpose;
    public TentStatus Status { get; set; } = TentStatus.Active;
    public string? Notes { get; set; }
    public int DisplayOrder { get; set; }
    public string AccentColor { get; set; } = "#69b578";

    public int? WidthCm { get; set; }
    public int? DepthCm { get; set; }
    public int? TentHeightCm { get; set; }
    public string? LightType { get; set; }
    public int? LightWatt { get; set; }
    public LightControllerType? LightController { get; set; }
    public string? LightControllerEntityId { get; set; }
    public int? ExhaustFanCount { get; set; }
    public int? ExhaustM3h { get; set; }
    public int? CirculationFanCount { get; set; }
    public HvacControllerType? HvacController { get; set; }
    public string? HvacControllerEntityId { get; set; }
    public bool Co2Available { get; set; }

    /// <summary>
    /// Gibt es eine CO₂-ANREICHERUNG (Brenner, Flasche, Generator)?
    /// </summary>
    /// <remarks>
    /// Nicht dasselbe wie ein CO₂-Sensor: der misst nur. Ohne Anreicherung
    /// steht die Luft bei ~400–500 ppm, und ein Anreicherungsziel von
    /// 800–1400 ppm stuende fuer immer „daneben" — die Kachel bekommt dann
    /// kein Ziel statt ein unerreichbares.
    /// </remarks>
    public bool HasCo2Enrichment { get; set; }
    public string? CameraEntityId { get; set; }

    /// <summary>
    /// All camera entities for this tent, newline-separated (a tent can have several — e.g.
    /// one per plant). <see cref="CameraEntityId"/> mirrors the first one for backward
    /// compatibility (snapshot automation, camera-proxy default).
    /// </summary>
    public string? CameraEntityIds { get; set; }

    /// <summary>
    /// How many °C the leaf sits below air temperature, used for leaf VPD (0 = plain air VPD).
    ///
    /// Defaults to 2 °C, which is what the workshop material specifies for RDWC and what the
    /// reference VPD calculator uses in its worked example (air 28 °C, leaf 26 °C). It used
    /// to default to 0, so a new tent silently computed air VPD — a different number than
    /// the one every RDWC chart is drawn for.
    /// </summary>
    public double LeafTempOffsetC { get; set; } = DefaultLeafTempOffsetC;

    /// <summary>The documented RDWC leaf offset, per the workshop material and the Ben Green calculator.</summary>
    public const double DefaultLeafTempOffsetC = 2.0;

    /// <summary>
    /// Home-Assistant-Dienst, der den Blattversatz an die Hardware weiterreicht —
    /// z. B. <c>pyscript.acinfinity_offset_setzen</c>. Leer heisst: nichts
    /// weiterreichen, der Wert bleibt eine reine Rechengroesse in Grow OS.
    /// </summary>
    /// <remarks>
    /// <para>Hintergrund: Beim AC-Infinity-AI+ liegt der Blattversatz NICHT in
    /// Home Assistant, sondern als Kalibrierung pro Sensor in der Hersteller-Cloud.
    /// Die Integration legt dafuer bei AI-Controllern keine Entitaet an. Wer den
    /// Wert hier aendert, aendert sonst nur die Rechnung in Grow OS — die Sonde
    /// meldet weiter ihren alten VPD, und beide Zahlen laufen auseinander.</para>
    /// <para><b>Vorzeichen:</b> Grow OS fuehrt den Versatz positiv („Blatt 2 °C
    /// kuehler"), die AC-Infinity-App negativ (−2). Beim Aufruf wird das
    /// Vorzeichen gedreht.</para>
    /// </remarks>
    public string? LeafOffsetSyncService { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public int ActiveGrowCount { get; set; }
    public int ArchivedGrowCount { get; set; }
    public int ActiveSetupCount { get; set; }
    public int ArchivedSetupCount { get; set; }
    public List<GrowRun> ActiveGrows { get; set; } = new();
    public List<TentSensor> Sensors { get; set; } = new();
}
