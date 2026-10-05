using System.Globalization;
using GrowDiary.Web.Services.Tagebuch;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Die Sprungerkennung des Grow-Tagebuchs (A-006) — an Brus echten Rohwerten.
/// </summary>
/// <remarks>
/// Die Reihen unten sind aus der laufenden Anlage gelesen (Fork AI, Zelt 1,
/// <c>/api/tents/1/history?resolution=raw</c>, am 05.10.2026), Zeiten in UTC.
/// Nichts geglättet: der Ausreißer 1,29 und das pH 3,66 beim Nachfüllen stehen
/// so in der Datenbank.
/// </remarks>
public sealed class SprungerkennungTests
{
    /// <summary>03.10.2026, EC: nachgefüllt, ohne Eintrag. Ortszeit 16:10–16:35.</summary>
    private const string Ec0310 = "12:00=1.74 12:05=1.73 12:10=1.74 12:15=1.74 12:20=1.74 12:25=1.74 12:30=1.74 12:35=1.74 12:40=1.74 12:45=1.74 12:50=1.74 12:55=1.74 13:00=1.74 13:05=1.74 13:10=1.74 13:15=1.74 13:20=1.74 13:25=1.74 13:30=1.74 13:35=1.73 13:40=1.75 13:45=1.73 13:50=1.74 13:55=1.75 14:00=1.73 14:05=1.74 14:10=1.74 14:15=1.29 14:20=1.67 14:25=1.62 14:30=1.7 14:35=1.61 14:40=1.6 14:45=1.61 14:50=1.6 14:55=1.61 15:00=1.61 15:05=1.6 15:07=1.59 15:12=1.6 15:17=1.6 15:22=1.6 15:27=1.6 15:32=1.6 15:37=1.6 15:42=1.61 15:47=1.61 15:52=1.61 15:57=1.61 16:02=1.61 16:07=1.61 16:12=1.61 16:17=1.61 16:22=1.6 16:27=1.6 16:32=1.61 16:37=1.6 16:42=1.61 16:47=1.6 16:52=1.61 16:57=1.61";

    /// <summary>03.10.2026, pH zur selben Zeit — ein Ausreißer auf 3,66, danach 0,1 tiefer.</summary>
    private const string Ph0310 = "12:00=6.19 12:05=6.2 12:10=6.2 12:15=6.24 12:20=6.23 12:25=6.24 12:30=6.22 12:35=6.22 12:40=6.23 12:45=6.21 12:50=6.2 12:55=6.2 13:00=6.2 13:05=6.2 13:10=6.19 13:15=6.21 13:20=6.22 13:25=6.21 13:30=6.22 13:35=6.24 13:40=6.23 13:45=6.25 13:50=6.24 13:55=6.23 14:00=6.23 14:05=6.23 14:10=6.22 14:15=6.31 14:20=6.31 14:25=5.79 14:30=3.66 14:35=6.15 14:40=6.13 14:45=6.13 14:50=6.14 14:55=6.13 15:00=6.13 15:05=6.14 15:07=6.13 15:12=6.12 15:17=6.12 15:22=6.12 15:27=6.12 15:32=6.12 15:37=6.13 15:42=6.12 15:47=6.12 15:52=6.11 15:57=6.1 16:02=6.09 16:07=6.09 16:12=6.09 16:17=6.09 16:22=6.1 16:27=6.09 16:32=6.09 16:37=6.1 16:42=6.09 16:47=6.11 16:52=6.1 16:57=6.11";

    /// <summary>04.10.2026, EC: Komplettwechsel 160 L — Ablassen, Füllen, Anmischen.</summary>
    private const string Ec0410 = "12:34=1.65 12:39=1.64 12:44=1.64 12:49=1.64 12:54=1.64 12:59=1.63 13:04=1.64 13:09=1.63 13:14=1.64 13:19=1.63 13:24=1.64 13:29=1.65 13:34=1.64 13:39=1.64 13:44=1.63 13:49=1.63 13:54=0.41 13:59=0.4 14:04=0.46 14:09=0.51 14:14=0.59 14:19=0.6 14:24=0.89 14:29=1.14 14:34=1.11 14:39=1.16 14:44=1.17 14:49=1.16 14:54=1.16 14:59=1.18 15:04=1.18 15:09=1.18 15:14=1.18 15:19=1.17 15:24=1.18 15:29=1.18 15:34=1.18 15:39=1.18 15:44=1.17 15:49=1.18 15:54=1.18 15:59=1.17 16:04=1.18 16:09=1.18 16:14=1.18 16:19=1.18 16:24=1.17 16:29=1.19 16:34=1.18 16:39=1.17 16:44=1.18 16:49=1.18 16:54=1.17 16:59=1.17 17:04=1.18 17:09=1.18 17:14=1.17 17:19=1.18 17:24=1.17 17:29=1.18";

    /// <summary>04.10.2026, pH beim Wechsel: wild, endet aber auf der alten Stufe.</summary>
    private const string Ph0410 = "12:34=6.13 12:39=6.13 12:44=6.14 12:49=6.13 12:54=6.14 12:59=6.14 13:04=6.13 13:09=6.14 13:14=6.14 13:19=6.15 13:24=6.14 13:29=6.13 13:34=6.14 13:39=6.12 13:44=6.12 13:49=6.13 13:54=6.56 13:59=6.45 14:04=6.5 14:09=6.78 14:14=6.91 14:19=7.24 14:24=7.48 14:29=7.03 14:34=7.13 14:39=5.89 14:44=4.41 14:49=5.67 14:54=5.38 14:59=6.25 15:04=6.19 15:09=6.14 15:14=6.14 15:19=6.14 15:24=6.13 15:29=6.14 15:34=6.14 15:39=6.12 15:44=6.14 15:49=6.14 15:54=6.14 15:59=6.14 16:04=6.13 16:09=6.13 16:14=6.12 16:19=6.12 16:24=6.13 16:29=6.11 16:34=6.12 16:39=6.12 16:44=6.11 16:49=6.12 16:54=6.12 16:59=6.11 17:04=6.1 17:09=6.11 17:14=6.1 17:19=6.11 17:24=6.11 17:29=6.1";

    public static IReadOnlyList<Rohwert> Reihe(string tag, string werte) => werte
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(paar => paar.Split('='))
        .Select(teile => new Rohwert(
            DateTime.SpecifyKind(DateTime.ParseExact($"{tag} {teile[0]}", "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), DateTimeKind.Utc),
            double.Parse(teile[1], CultureInfo.InvariantCulture)))
        .ToList();

    public static IReadOnlyList<Rohwert> BrusNachfuellen() => Reihe("2026-10-03", Ec0310);

    [Fact]
    public void BrusNachfuellenAm0310_WirdAlsEinSprungErkannt()
    {
        var funde = Sprungerkennung.Finden("reservoir-ec", BrusNachfuellen());

        var sprung = Assert.Single(funde);
        // Letzter ruhiger Wert 14:10 UTC (16:10 Ortszeit), erster ruhiger danach 14:35.
        Assert.Equal(new DateTime(2026, 10, 3, 14, 10, 0, DateTimeKind.Utc), sprung.LetzteRuheUtc);
        Assert.Equal(new DateTime(2026, 10, 3, 14, 35, 0, DateTimeKind.Utc), sprung.NeueRuheUtc);
        Assert.Equal(25, sprung.DauerMinuten);
        Assert.Equal(1.74, sprung.Vorher, 2);
        Assert.InRange(sprung.Nachher, 1.595, 1.61);
        Assert.InRange(sprung.Aenderung, -0.15, -0.13);
    }

    [Fact]
    public void BrusPhAm0310_BleibtUnterDerSchwelle_TrotzAusreisser()
    {
        // 6,23 → 6,13: 0,10, die Schwelle ist ein halbes Zielband (0,2). Der
        // Ausreißer 3,66 mitten im Übergang zählt nicht — er ist keine Stufe.
        Assert.Empty(Sprungerkennung.Finden("reservoir-ph", Reihe("2026-10-03", Ph0310)));
    }

    [Fact]
    public void BrusWasserwechselAm0410_WirdErkannt_ObwohlDasMischen50MinutenDauert()
    {
        var sprung = Assert.Single(Sprungerkennung.Finden("reservoir-ec", Reihe("2026-10-04", Ec0410)));
        Assert.Equal(new DateTime(2026, 10, 4, 13, 49, 0, DateTimeKind.Utc), sprung.LetzteRuheUtc);
        Assert.InRange(sprung.Vorher, 1.63, 1.645);
        Assert.InRange(sprung.Nachher, 1.16, 1.18);
    }

    [Fact]
    public void BrusPhBeimWechsel_EndetAufDerAltenStufe_IstKeinSprung()
    {
        Assert.Empty(Sprungerkennung.Finden("reservoir-ph", Reihe("2026-10-04", Ph0410)));
    }

    [Fact]
    public void Messrauschen_UeberEinenGanzenTag_WirdNieGemeldet()
    {
        // Ein ganzer Tag im 5-Minuten-Takt (Leitplanke 15: Zeitabhängiges über
        // den ganzen Tag): EC 1,70 mit ±0,02 Zittern, Tagesgang ±0,015 und
        // +0,03 Aufnahme über den Tag — so sieht Brus Becken in Ruhe aus.
        var zufall = new Random(4711);
        var start = new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc);
        var werte = Enumerable.Range(0, 288)
            .Select(i => new Rohwert(start.AddMinutes(i * 5),
                Math.Round(1.70 + i * 0.0001 + 0.015 * Math.Sin(i / 288.0 * 2 * Math.PI) + (zufall.NextDouble() - 0.5) * 0.04, 2)))
            .ToList();
        var ph = werte.Select((w, i) => w with { Wert = Math.Round(6.1 + 0.08 * Math.Sin(i / 288.0 * 2 * Math.PI) + (zufall.NextDouble() - 0.5) * 0.06, 2) }).ToList();

        Assert.Empty(Sprungerkennung.Finden("reservoir-ec", werte));
        Assert.Empty(Sprungerkennung.Finden("reservoir-ph", ph));
    }

    [Fact]
    public void Kalibrierung_ImBecher_KehrtZurueck_IstKeinSprung()
    {
        // Sonde 20 Minuten in Pufferlösung pH 4,0 und 7,0, dann zurück ins Becken.
        var start = new DateTime(2026, 10, 2, 8, 0, 0, DateTimeKind.Utc);
        var werte = Enumerable.Range(0, 40).Select(i =>
        {
            var minute = i * 5;
            var wert = minute is >= 60 and < 70 ? 4.0 : minute is >= 70 and < 80 ? 7.0 : 6.12;
            return new Rohwert(start.AddMinutes(minute), wert);
        }).ToList();

        Assert.Empty(Sprungerkennung.Finden("reservoir-ph", werte));
    }

    [Fact]
    public void LueckeImVerlauf_IstKeinSprung()
    {
        // Sensor 40 Minuten weg, danach ein anderer Wert: wie schnell es ging, weiß niemand.
        var werte = BrusNachfuellen().Where(w => w.Utc < new DateTime(2026, 10, 3, 14, 5, 0, DateTimeKind.Utc)
                                                 || w.Utc > new DateTime(2026, 10, 3, 14, 45, 0, DateTimeKind.Utc)).ToList();
        Assert.Empty(Sprungerkennung.Finden("reservoir-ec", werte));
    }

    [Fact]
    public void NochNichtFertigGemessen_WirdNichtGemerkt_DanachSchon()
    {
        // Endet der Verlauf 20 Minuten nach der neuen Stufe, ist sie noch nicht
        // belegt — sonst stünde ein Fund im Speicher, den die nächsten Werte widerlegen.
        var alles = BrusNachfuellen();
        var frisch = alles.Where(w => w.Utc <= new DateTime(2026, 10, 3, 14, 55, 0, DateTimeKind.Utc)).ToList();

        Assert.Empty(Sprungerkennung.Finden("reservoir-ec", frisch));
        Assert.Single(Sprungerkennung.Finden("reservoir-ec", alles));
    }

    [Fact]
    public void Viertelstundentakt_DesTestbestands_ReichtFuerEineErkennung()
    {
        var start = new DateTime(2026, 10, 2, 10, 0, 0, DateTimeKind.Utc);
        var werte = Enumerable.Range(0, 16)
            .Select(i => new Rohwert(start.AddMinutes(i * 15), i < 8 ? 1.68 : 1.56))
            .ToList();

        var sprung = Assert.Single(Sprungerkennung.Finden("reservoir-ec", werte));
        Assert.Equal(15, sprung.DauerMinuten);
    }

    [Fact]
    public void Gleiche_Rohwerte_GebenDenselbenBeginn()
    {
        // Darauf baut das Merken (eindeutig über Zelt, Messgröße, Beginn):
        // ein zweites Erkennen darf keinen zweiten Fund anlegen.
        var a = Sprungerkennung.Finden("reservoir-ec", BrusNachfuellen());
        var b = Sprungerkennung.Finden("reservoir-ec", BrusNachfuellen().Reverse());
        Assert.Equal(a, b);
    }

    [Theory]
    [InlineData("reservoir-ec", 1.74, 0.087)]
    [InlineData("reservoir-ec", 0.6, 0.05)]
    [InlineData("reservoir-ph", 6.2, 0.2)]
    [InlineData("reservoir-level", 160, 8)]
    public void Schwellen_FolgenIhrerBegruendung(string schluessel, double vorher, double erwartet)
    {
        Assert.Equal(erwartet, Sprungerkennung.Schwelle(schluessel, vorher)!.Value, 3);
        Assert.False(string.IsNullOrWhiteSpace(Sprungerkennung.Regel(schluessel)));
    }

    [Fact]
    public void LuftUndFeuchte_WerdenNichtGeprueft()
    {
        // Sie springen bei jedem Lichtwechsel — das ist der Plan, kein Ereignis.
        Assert.Null(Sprungerkennung.Schwelle("temperature", 25));
        Assert.Empty(Sprungerkennung.Finden("humidity", BrusNachfuellen()));
    }
}
