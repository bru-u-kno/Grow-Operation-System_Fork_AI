using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Die erfundenen Werte für den Entwicklungsrechner. Was hier geprüft wird,
/// ist nicht die Physik, sondern zweierlei: dass die Werte plausibel bleiben,
/// und dass Kurve und Kachel dieselbe Quelle haben — sonst endet der Verlauf
/// woanders, als die Kachel steht.
/// </summary>
public sealed class DemoDataTests
{
    private static readonly DateTime Now = new(2026, 7, 28, 14, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void EveryKnownMetric_HasAValue()
    {
        foreach (var key in DemoData.MetricKeys)
        {
            Assert.NotNull(DemoData.ValueFor(key, Now));
        }
    }

    [Fact]
    public void AnUnknownMetric_HasNone()
    {
        Assert.Null(DemoData.ValueFor("gibt-es-nicht", Now));
    }

    [Fact]
    public void TheSameMomentAlwaysGivesTheSameValue()
    {
        // Sonst passten zwei Abrufe kurz hintereinander nicht zusammen, und
        // nach einem Neustart spränge die Kurve.
        Assert.Equal(DemoData.ValueFor("temperature", Now), DemoData.ValueFor("temperature", Now));
    }

    [Fact]
    public void ValuesMoveOverTime()
    {
        // Ein starrer Wert waere zum Pruefen wertlos: keine Kurve, kein Trend.
        var jetzt = DemoData.ValueFor("temperature", Now)!.Value;
        var spaeter = DemoData.ValueFor("temperature", Now.AddHours(6))!.Value;

        Assert.NotEqual(jetzt, spaeter);
    }

    /// <summary>
    /// Was in einem echten Zelt überhaupt vorkommen kann — je Messgröße.
    /// </summary>
    /// <remarks>
    /// <b>Plausibilitätsgrenzen, keine Kurvenmaße.</b> Sie sagen „das gibt es
    /// in keinem Zelt", nicht „die Kurve läuft heute genau hier". Wer sie eng
    /// an die Kurve legt, muss sie bei jeder Änderung nachziehen und prüft
    /// am Ende nur noch sich selbst.
    /// </remarks>
    private static readonly Dictionary<string, (double Min, double Max)> Moeglich = new()
    {
        ["temperature"] = (15, 35),
        ["humidity"] = (30, 90),
        ["co2"] = (300, 1600),
        ["ppfd"] = (0, 1200),               // 0 ist richtig: nachts ist es dunkel
        ["reservoir-ph"] = (4.5, 7.5),
        ["reservoir-ec"] = (0.4, 3.0),
        ["reservoir-temp"] = (14, 28),      // die obere Spitze ist der Kühlerausfall
        ["reservoir-level-cm"] = (5, 60),
        ["orp"] = (200, 600),
        ["dissolved-oxygen"] = (4, 10),     // die untere Spitze ist derselbe Ausfall
        // Die Raumluft um das Zelt: ein bewohnter Raum, kein Keller, keine Sauna.
        ["outside-temperature"] = (12, 30),
        ["outside-humidity"] = (25, 85),
        ["outside-vpd"] = (0.2, 2.5),
        // Steckdosenleiste vor beiden Zelten: Grundlast 160 W, mit beiden LED 880 W.
        ["power"] = (100, 1000),
    };

    /// <summary>
    /// Keine Kurve verlässt über die ganzen 42 Tage das Mögliche.
    /// </summary>
    /// <remarks>
    /// <para><b>Der Anlass.</b> Die erste Fassung prüfte <b>einen festen
    /// Kalendertag</b> (28.07.2026) über 24 Stunden. Die Demokurve ist aber an
    /// <c>DateTime.Today</c> verankert: mit jedem echten Tag wandert dieser
    /// Punkt weiter durch den Sägezahn. Am 20.08. war der Test grün, am 23.08.
    /// rot — bei einem EC von 1,10 gegen eine untere Schranke von 1,20. Der
    /// Code hatte sich nicht geändert, nur das Datum.</para>
    ///
    /// <para><b>Und er prüfte zu wenig.</b> Sieben von zehn Kurven, und drei
    /// der sieben Schranken waren gegen den echten Verlauf falsch — nur lag
    /// der eine geprüfte Tag zufällig günstig. Deshalb geht die Prüfung jetzt
    /// über die <b>Grundmenge</b> <see cref="Demoverlauf.Schluessel"/> und über
    /// das ganze Fenster, das die App zeigt.</para>
    /// </remarks>
    [Fact]
    public void JedeKurveBleibtImMoeglichen()
    {
        Assert.True(Demoverlauf.Schluessel.Length >= 8,
            "Die Grundmenge ist leer oder geschrumpft — dann liefe diese Prüfung "
            + "null Mal durch und wäre grundlos grün.");

        var heute = DateTime.Now;
        var befunde = new List<string>();

        foreach (var key in Demoverlauf.Schluessel)
        {
            Assert.True(Moeglich.ContainsKey(key),
                $"Für „{key}\" gibt es keine Plausibilitätsgrenze. Neue Kurve ohne "
                + "Schranke: entweder eine eintragen oder die Kurve wieder entfernen.");

            var (min, max) = Moeglich[key];

            // Stündlich über die ganzen 42 Tage — genau das Fenster, das die
            // App anzeigt. Ein einzelner Tag beweist nichts über den Rest.
            for (var stunde = 0; stunde < Demoverlauf.TageRueckwaerts * 24; stunde++)
            {
                var zeitpunkt = heute.AddHours(-stunde);
                if (Demoverlauf.Wert(key, zeitpunkt) is not { } wert) continue;
                if (wert < min || wert > max)
                {
                    befunde.Add($"{key} = {wert:0.##} am {zeitpunkt:dd.MM. HH}h "
                        + $"(möglich wären {min:0.##} bis {max:0.##})");
                    break;   // ein Beleg je Kurve reicht
                }
            }
        }

        Assert.True(befunde.Count == 0,
            "Diese Testdaten gibt es in keinem Zelt:\n" + string.Join("\n", befunde));
    }

    /// <summary>Beisst die Prüfung? Eine unmögliche Schranke muss auffallen.</summary>
    [Fact]
    public void Die_Pruefung_wuerde_einen_unmoeglichen_Wert_finden()
    {
        // Der EC-Verlauf liegt über die 42 Tage bei etwa 1,02 bis 1,24. Gegen
        // eine Schranke, die erst bei 2,0 beginnt, MUSS er auffallen — sonst
        // prüft die Schleife oben nichts.
        var heute = DateTime.Now;
        var getroffen = false;

        for (var stunde = 0; stunde < Demoverlauf.TageRueckwaerts * 24; stunde++)
        {
            if (Demoverlauf.Wert("reservoir-ec", heute.AddHours(-stunde)) is { } wert && wert < 2.0)
            {
                getroffen = true;
                break;
            }
        }

        Assert.True(getroffen,
            "Kein EC-Wert unter 2,0 gefunden — dann liest die Prüfung oben nicht, "
            + "was sie zu lesen glaubt.");
    }

    /// <summary>Der pH steigt zwischen zwei Dosierungen — sonst gibt es nichts zu korrigieren.</summary>
    /// <remarks>
    /// <para><b>Was hier vorher stand, hat die Drift nie gemessen.</b> Der Test
    /// verglich 06:00 mit 18:00 <i>desselben Tages</i>. Die Drift kommt aber aus
    /// den Tagen seit der letzten Dosierung — die war in beiden Punkten gleich.
    /// Gemessen wurde also die Tag/Nacht-Welle, und die kippte das Vorzeichen,
    /// sobald der Tagesgang ans Licht gebunden wurde. Ein Test, der beim
    /// Verschieben einer Sinuskurve rot wird, hat nie den pH geprüft.</para>
    ///
    /// <para>Jetzt läuft er über den ganzen Dosierzyklus und misst zur selben
    /// Tageszeit — damit die Welle herausfällt und nur die Drift bleibt.</para>
    /// </remarks>
    [Fact]
    public void Der_pH_steigt_zwischen_zwei_Dosierungen()
    {
        var mittags = DateTime.Today.AddDays(-Demoverlauf.DosierAlleTage).AddHours(12);
        var werte = Enumerable.Range(0, Demoverlauf.DosierAlleTage)
            .Select(tag => Demoverlauf.Ph(mittags.AddDays(tag)))
            .ToList();

        // Mengenwaechter: ohne mehrere Punkte vergleicht die Schleife nichts.
        Assert.True(werte.Count >= 2, "Zu wenige Punkte im Dosierzyklus.");

        for (var i = 1; i < werte.Count; i++)
        {
            Assert.True(werte[i] > werte[i - 1],
                $"Tag {i} liegt bei {werte[i]:0.000} und damit nicht ueber Tag {i - 1} ({werte[i - 1]:0.000}).");
        }
    }

    [Fact]
    public void TheHistoryEndsWhereTheTileStands()
    {
        // Kurve und Kachel kommen aus demselben Generator. Liefe das
        // auseinander, zeigte der Verlauf etwas anderes als der Messwert
        // darueber — und niemand wuesste, welchem zu glauben ist.
        var verlauf = DemoData.SeedHistory(tentId: 1, Now).ToList();
        var letzterPh = verlauf.Where(r => r.MetricKey == "reservoir-ph").MaxBy(r => r.CapturedAtUtc)!;

        Assert.Equal(DemoData.ValueFor("reservoir-ph", letzterPh.CapturedAtUtc), letzterPh.Value);
    }

    [Fact]
    public void TheHistoryCoversTheAdvertisedWindow()
    {
        var verlauf = DemoData.SeedHistory(tentId: 1, Now).ToList();
        var aeltester = verlauf.Min(r => r.CapturedAtUtc);

        Assert.True(Now - aeltester <= TimeSpan.FromHours(DemoData.HistoryHours));
        Assert.True(Now - aeltester >= TimeSpan.FromHours(DemoData.HistoryHours - 1));
        Assert.All(verlauf, reading => Assert.Equal(1, reading.TentId));
    }

    [Fact]
    public void StatesCoverEveryMetric_PlusTheLight()
    {
        var states = DemoData.StatesFor(Now);

        foreach (var key in DemoData.MetricKeys)
        {
            Assert.True(states.ContainsKey(key), $"Messwert {key} fehlt.");
            Assert.NotNull(states[key].NumericValue);
        }

        Assert.Contains(states.Values, state => state.State is "on" or "off");
    }

    [Fact]
    public void EveryDemoEntity_IsRecognisableAsOne()
    {
        // Man muss auf den ersten Blick sehen, dass nichts davon echt ist.
        var entities = DemoData.Entities(Now);

        Assert.All(entities, entity => Assert.Contains(DemoData.EntityPrefix, entity.EntityId));
        Assert.All(entities, entity => Assert.StartsWith("Demo", entity.FriendlyName));
    }

    [Fact]
    public void ThereAreSwitchableEntities_SoPumpsCanBeMapped()
    {
        var schalter = DemoData.Entities(Now).Where(entity => entity.Domain == "switch").ToList();

        Assert.True(schalter.Count >= 2, "Ohne schaltbare Entitaeten laesst sich keine Pumpe zuordnen.");
    }

    [Fact]
    public void TheDemoConnectionCountsAsConfigured()
    {
        // Sonst hielten Watchdog, Live und Dosierung den Rechner fuer
        // unkonfiguriert und blockierten, bevor die Werte gefragt waeren.
        Assert.True(DemoData.Settings().IsConfigured);
    }

    /// <summary>
    /// Der Schaltzustand des Lichts und die Lichtkurven muessen ueber den
    /// ganzen Tag dasselbe sagen.
    /// </summary>
    /// <remarks>
    /// <para><b>Warum eine Zaehlung und keine zwei Stichproben.</b> Vorher
    /// standen hier genau zwei Stunden — 03:00 und 12:00 UTC. Beide lagen
    /// zufaellig auf der richtigen Seite, waehrend <c>LightOn</c> in UTC und
    /// die Kurven in Ortszeit rechneten. Zwei Stunden am Tag meldete der
    /// Testbestand deshalb „Licht an" bei PPFD 0. Der Test war gruen.</para>
    ///
    /// <para>Jetzt laeuft er ueber alle 24 Stunden und vergleicht die beiden
    /// Quellen miteinander, statt beiden dieselbe Annahme zu unterstellen.</para>
    /// </remarks>
    [Fact]
    public void Lichtschalter_und_Lichtkurve_widersprechen_sich_nie()
    {
        var tag = DateTime.Today.AddDays(-1);
        var widersprueche = new List<string>();
        var anStunden = 0;

        for (var stunde = 0; stunde < 24; stunde++)
        {
            var ortszeit = tag.AddHours(stunde).AddMinutes(30);
            var utc = ortszeit.ToUniversalTime();

            var schalter = DemoData.LightOn(utc);
            var ppfd = Demoverlauf.Wert("ppfd", ortszeit) ?? 0;
            if (schalter) anStunden++;

            if (schalter != ppfd > 0)
            {
                widersprueche.Add($"{ortszeit:HH:mm} Ortszeit: Schalter {(schalter ? "an" : "aus")}, PPFD {ppfd}");
            }
        }

        // Mengenwaechter: liefe die Schleife leer oder braechte die Kurve
        // ueberall 0, waere der Test gruen ohne etwas zu pruefen.
        Assert.InRange(anStunden, 1, 23);
        Assert.Empty(widersprueche);
    }

    /// <summary>Der Testbestand faehrt einen Zyklus, den es wirklich gibt.</summary>
    /// <remarks>
    /// 18/6 oder 12/12 — eine krumme Zahl waere ein Fehler in den Testdaten
    /// und liefe durch alles hindurch, was daran misst.
    /// </remarks>
    [Fact]
    public void Der_Testbestand_faehrt_einen_echten_Zyklus()
    {
        var tag = DateTime.Today.AddDays(-1);
        var an = Enumerable.Range(0, 24)
            .Count(h => DemoData.LightOn(tag.AddHours(h).AddMinutes(30).ToUniversalTime()));

        Assert.True(an is 18 or 12, $"Der Testbestand faehrt {an}/{24 - an} — das ist kein ueblicher Zyklus.");
    }

    /// <summary>Was einen Zustand hat, steht auch in der Auswahlliste — und umgekehrt.</summary>
    /// <remarks>
    /// <para><b>Der Fund.</b> Der Testbestand hatte ZWEI Schreibweisen fuer
    /// dieselbe Messgroesse: die Zustaende hiessen <c>demo.reservoir_ph</c>, die
    /// Auswahlliste bot <c>sensor.demo_reservoir_ph</c> an. Wer im Testbetrieb
    /// einen Sensor zuordnete, bekam nie einen Wert — die Zuordnung zeigte auf
    /// eine Kennung, unter der nichts lag. Dieselbe Verwechslung wie beim
    /// Kuehler: Metrik-Schluessel gegen Entitaets-Kennung.</para>
    ///
    /// <para>Die Auswahlliste ist auch der einzige Weg, im Testbetrieb etwas
    /// EINZURICHTEN. Fehlt dort die Kuehler-Steckdose, laesst sich die
    /// Kuehler-Steuerung im Testbestand ueberhaupt nicht durchspielen.</para>
    /// </remarks>
    [Fact]
    public void Jeder_Zustand_steht_auch_in_der_Auswahlliste()
    {
        var jetzt = DateTime.UtcNow;
        var zustaende = DemoData.StatesFor(jetzt).Values
            .Select(z => z.EntityId)
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var auswahl = DemoData.Entities(jetzt)
            .Select(e => e.EntityId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Mengenwaechter fuer beide Seiten.
        Assert.True(zustaende.Count >= 10, $"Nur {zustaende.Count} Zustaende — die Zaehlung sieht ihre Grundmenge nicht.");
        Assert.True(auswahl.Count >= 10, $"Nur {auswahl.Count} Eintraege in der Auswahl.");

        var ohneAuswahl = zustaende.Where(k => !auswahl.Contains(k)).ToList();
        Assert.True(ohneAuswahl.Count == 0,
            "Diese Entitaeten liefern einen Wert, stehen aber in keiner Auswahlliste: "
            + string.Join(", ", ohneAuswahl));
    }

    /// <summary>Die Geraete des AC-Versuchs sind im Testbetrieb auswaehlbar.</summary>
    /// <remarks>
    /// Ohne sie laesst sich der Versuchsaufbau im Testbestand nicht einrichten —
    /// und was niemand einrichten kann, prueft auch niemand.
    /// </remarks>
    [Fact]
    public void Der_AC_Versuch_ist_im_Testbetrieb_einrichtbar()
    {
        var auswahl = DemoData.Entities(DateTime.UtcNow)
            .Select(e => e.EntityId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains(DemoData.LichtLeistung, auswahl);
        Assert.Contains(DemoData.LichtEinZeit, auswahl);
        Assert.Contains(DemoData.LichtAusZeit, auswahl);
        Assert.Contains(DemoData.KuehlerSteckdose, auswahl);
    }

    /// <summary>Eine Entitaet, die es nicht gibt, kennt der Testbestand auch nicht.</summary>
    /// <remarks>
    /// Der Testbetrieb nahm anfangs jeden Schaltbefehl an. Eine erfundene
    /// Steckdose meldete danach „laeuft" — weil das Schaltbrett vermerkt hatte,
    /// was der Regler geschickt hat. Damit verdeckte der Testbestand genau die
    /// Sorte Tippfehler, an der der Kuehler schon einmal gescheitert ist.
    /// </remarks>
    [Fact]
    public void Eine_erfundene_Entitaet_kennt_der_Testbestand_nicht()
    {
        Assert.True(DemoData.KennstEntitaet(DemoData.KuehlerSteckdose));
        Assert.True(DemoData.KennstEntitaet(DemoData.LichtLeistung));

        Assert.False(DemoData.KennstEntitaet("switch.demo_wasserkuehler"));
        Assert.False(DemoData.KennstEntitaet("sensor.gibt_es_nicht"));
        Assert.False(DemoData.KennstEntitaet(""));
    }
}
