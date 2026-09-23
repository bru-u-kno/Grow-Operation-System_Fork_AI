namespace GrowDiary.Web.Models;

public sealed class MetricCard
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = "-";
    public string? Unit { get; set; }
    public string Tone { get; set; } = "default";
    public string? Hint { get; set; }
    public string? Target { get; set; }   // z. B. "5.8-6.2" fuer Sollwert-Anzeige

    /// <summary>
    /// Der Wert als Zahl, zusaetzlich zum formatierten <see cref="Value"/>.
    /// Die Anzeige zeichnet daraus die Skala; aus "25,4 °C" laesst sich das nicht
    /// zurueckrechnen, ohne die Formatierung wieder aufzudroeseln.
    /// </summary>
    public double? NumericValue { get; set; }

    /// <summary>Zielbereich fuer diese Phase, null wo es keinen gibt (Licht, Fuellstand).</summary>
    public double? TargetMin { get; set; }
    public double? TargetMax { get; set; }

    /// <summary>
    /// Tag- und Nachtband nebeneinander, wenn die Messgroesse eins hat.
    /// </summary>
    /// <remarks>
    /// Fork AI, 13.09.2026. <see cref="TargetMin"/>/<see cref="TargetMax"/>
    /// bleibt das Band, das GERADE gilt — daran haengen Score, Skala und
    /// Abweichungsanalyse, und die sollen nicht zwei Baender gegeneinander
    /// abwaegen muessen. Die vier Felder hier sind allein fuer die Anzeige:
    /// die Kachel zeigt beide Baender, das aktive hell.
    ///
    /// Alle null heisst: diese Messgroesse kennt keinen Unterschied zwischen
    /// Tag und Nacht (pH, EC), und die Kachel bleibt wie bisher.
    /// </remarks>
    public double? TargetDayMin { get; set; }
    public double? TargetDayMax { get; set; }
    public double? TargetNightMin { get; set; }
    public double? TargetNightMax { get; set; }

    /// <summary>Welches der beiden Baender gerade gilt: <c>day</c> oder <c>night</c>.</summary>
    public string? TargetPhase { get; set; }

    /// <summary>
    /// Fork AI (F-041): die Grenzwerte, bei denen gerade gemeldet wird — getrennt vom Ziel.
    /// </summary>
    /// <remarks>
    /// Vorher legten sich feste Grenzwert-Regeln (Luft, Feuchte) als Ziel auf die
    /// Kachel: dort stand „21–27 °C", gemeint war „ab hier kommt eine Meldung".
    /// Das Ziel kommt jetzt aus dem Plan (<see cref="TargetMin"/>/<see cref="TargetMax"/>),
    /// die Grenzen stehen hier — für das Band (gelbe Striche) und den Status „Grenze".
    /// </remarks>
    public double? AlarmMin { get; set; }
    public double? AlarmMax { get; set; }

    /// <summary>Fork AI (F-041): Grenzwerte tags und nachts, wo die Messgröße ein Nachtband hat.</summary>
    public double? AlarmDayMin { get; set; }
    public double? AlarmDayMax { get; set; }
    public double? AlarmNightMin { get; set; }
    public double? AlarmNightMax { get; set; }

    /// <summary>
    /// Kurzer Status in der Ecke, wo es keine Bewertung gibt — „12/12" beim Licht.
    /// </summary>
    /// <remarks>
    /// Fork AI, 13.09.2026. Der Lichtzyklus ist ein Status, keine Fussnote: er
    /// aendert sich nicht mit der Stunde und gehoert deshalb nach oben zu „im
    /// Ziel"/„daneben", nicht in die Zeile mit den Uhrzeiten.
    /// </remarks>
    public string? StatusNote { get; set; }

    /// <summary>Schaltzeiten des Lichts als <c>HH:mm</c> — fuer die Restzeit bis zum Wechsel.</summary>
    /// <remarks>
    /// Als Uhrzeit und nicht als fertige Restzeit: eine vom Server gerechnete
    /// Angabe („noch 3 Std 25 Min") altert zwischen zwei Abrufen und steht
    /// dann falsch da. Die Oberflaeche rechnet sie jede Minute neu.
    /// </remarks>
    public string? LightOnAt { get; set; }
    public string? LightOffAt { get; set; }

    /// <summary>
    /// Woher der WERT kommt: <c>live</c> (Sensor) oder <c>hand</c> (erfasste Messung).
    /// </summary>
    /// <remarks>
    /// Dasselbe Herkunfts-Prinzip wie beim Ziel. Viele Betreiber haben nur
    /// Handmessgeraete — deren Werte sollen auf der Kachel stehen, aber nie so
    /// aussehen, als kaemen sie gerade aus einem Sensor.
    /// </remarks>
    public string? ValueSource { get; set; }

    /// <summary>Alter der Handmessung in Minuten; null bei Live-Werten.</summary>
    public int? MeasuredAgeMinutes { get; set; }

    /// <summary>
    /// Woran der Zielbereich haengt, wenn er aus einem anderen Wert stammt —
    /// „bei 46 % RLF". Ohne den Zusatz liest sich „Ziel 15,8–19,6 °C" als
    /// Aufforderung zu kuehlen, obwohl in Wahrheit die Feuchte zu niedrig ist.
    /// </summary>
    public string? TargetNote { get; set; }

    /// <summary>
    /// True, wenn der Zielbereich nicht aus dem Wissen stammt, sondern aus einem
    /// anderen Messwert zurueckgerechnet wurde. Solche Werte werden angezeigt,
    /// zaehlen aber nicht eigenstaendig in den Score: Luft, Feuchte und VPD
    /// beschreiben dieselbe Lage, und dreimal abziehen macht aus einem Problem
    /// drei.
    /// </summary>
    public bool TargetDerived { get; set; }
}
