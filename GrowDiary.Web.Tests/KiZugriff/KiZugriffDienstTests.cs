using System.Net;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Infrastructure.KiZugriff;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;

namespace GrowDiary.Web.Tests.KiZugriff;

/// <summary>Eine Uhr, die nur weitergeht, wenn der Test es sagt.</summary>
internal sealed class VerstellbareUhr : TimeProvider
{
    public DateTimeOffset Jetzt { get; set; } = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Jetzt;
}

/// <summary>
/// Fork AI (A-003, 03.10.2026): Schlüssel, Hash, Fehlversuch-Sperre, Stundenfenster.
/// </summary>
public sealed class KiZugriffDienstTests : IDisposable
{
    private readonly string _ordner;
    private readonly AppPaths _pfade;
    private readonly VerstellbareUhr _uhr = new();
    private readonly KiZugriffDienst _dienst;

    private static readonly IPAddress Nachbar = IPAddress.Parse("172.30.33.5");

    public KiZugriffDienstTests()
    {
        _ordner = Path.Combine(Path.GetTempPath(), "KiZugriff_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_ordner);
        _pfade = new AppPaths(_ordner);
        TestDatabase.Initialize(_pfade);
        _dienst = new KiZugriffDienst(new KiSchluesselRepository(_pfade), new AppSettingsRepository(_pfade), _uhr);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_ordner, recursive: true); } catch { }
    }

    private void Einschalten(int maxSchalt = 20)
        => _dienst.EinstellungenSpeichern(new KiZugriffEinstellungen(true, new(10, maxSchalt)));

    // ------------------------------------------------------- Klartext, Hash

    [Fact]
    public void DerKlartextHatDieVerabredeteForm()
    {
        var klartexte = Enumerable.Range(0, 50).Select(_ => KiZugriffDienst.NeuerKlartext()).ToList();

        Assert.All(klartexte, k =>
        {
            Assert.StartsWith("gok_", k);
            Assert.Equal(4 + 43, k.Length);
            Assert.True(KiZugriffDienst.HatSchluesselForm(k), k);
            Assert.DoesNotContain('=', k);
            Assert.DoesNotContain('+', k);
            Assert.DoesNotContain('/', k);
        });
        // 256 Bit Zufall: keine zwei gleich.
        Assert.Equal(klartexte.Count, klartexte.Distinct().Count());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("gok_")]
    [InlineData("gok_zukurz")]
    [InlineData("GOK_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("gok_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("gok_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")]
    [InlineData("gok_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void FalscheFormIstKeinSchluessel(string? text)
        => Assert.False(KiZugriffDienst.HatSchluesselForm(text));

    [Fact]
    public void DerHashIstSha256Hex()
    {
        // Bekannter Wert: SHA-256 von "abc".
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", KiZugriffDienst.Hash("abc"));
    }

    [Fact]
    public void GespeichertWerdenHashUndPraefix_NieDerKlartext()
    {
        var (schluessel, klartext) = _dienst.Anlegen("  Claude  ", KiStufe.Dokumentieren);

        Assert.Equal("Claude", schluessel.Name);
        Assert.Equal(KiZugriffDienst.Hash(klartext), schluessel.Hash);
        Assert.Equal(klartext.Substring(4, 8), schluessel.Praefix);

        using var verbindung = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _pfade.DatabasePath }.ToString());
        verbindung.Open();
        using var befehl = verbindung.CreateCommand();
        befehl.CommandText = "SELECT * FROM ForkKiSchluessel;";
        using var leser = befehl.ExecuteReader();
        var zellen = new List<string>();
        while (leser.Read())
        {
            for (var i = 0; i < leser.FieldCount; i++) zellen.Add(leser.GetValue(i)?.ToString() ?? "");
        }
        Assert.True(zellen.Count >= 8, "Keine Zeile gelesen — dann prüft der Fall nichts.");
        Assert.DoesNotContain(zellen, z => z.Contains(klartext[4..], StringComparison.Ordinal));
    }

    // -------------------------------------------------------------- Prüfen

    [Fact]
    public void GueltigerSchluesselGibtDenKontext()
    {
        Einschalten();
        var (schluessel, klartext) = _dienst.Anlegen("Claude", KiStufe.Dokumentieren | KiStufe.GeraeteSchalten);

        var ergebnis = _dienst.Pruefen(klartext, Nachbar);

        Assert.Equal(KiPruefung.Gueltig, ergebnis.Ergebnis);
        Assert.Equal(schluessel.Id, ergebnis.Kontext!.SchluesselId);
        Assert.Equal("Claude", ergebnis.Kontext.SchluesselName);
        Assert.True(ergebnis.Kontext.Darf(KiStufe.GeraeteSchalten));
        Assert.False(ergebnis.Kontext.Darf(KiStufe.Verwaltung));
        Assert.Equal(20, ergebnis.Kontext.Hoechstwerte.MaxSchaltbefehleJeStunde);
    }

    [Fact]
    public void AbWerkIstDerZugriffAus()
    {
        var einstellungen = _dienst.Einstellungen();
        Assert.False(einstellungen.Aktiv);
        Assert.Equal(10, einstellungen.Hoechstwerte.MaxDosisMlJeBefehl);
        Assert.Equal(20, einstellungen.Hoechstwerte.MaxSchaltbefehleJeStunde);

        var (_, klartext) = _dienst.Anlegen("Claude", KiStufe.Dokumentieren);
        Assert.Equal(KiPruefung.ZugriffAus, _dienst.Pruefen(klartext, Nachbar).Ergebnis);
    }

    [Fact]
    public void HoechstwerteBleibenWieGespeichert()
    {
        _dienst.EinstellungenSpeichern(new KiZugriffEinstellungen(true, new(2.5, 7)));
        var einstellungen = _dienst.Einstellungen();
        Assert.Equal(2.5, einstellungen.Hoechstwerte.MaxDosisMlJeBefehl);
        Assert.Equal(7, einstellungen.Hoechstwerte.MaxSchaltbefehleJeStunde);
    }

    // ------------------------------------------------- Rückfrage je Schlüssel (A-005)

    [Fact]
    public void RueckfrageStehtJeSchluessel_UndNurBeiFreigegebenenStufen()
    {
        Einschalten();
        var (schluessel, klartext) = _dienst.Anlegen("Claude", KiStufe.Dokumentieren | KiStufe.GrowPlanen,
            KiStufe.GrowPlanen | KiStufe.Verwaltung);

        // Verwaltung ist nicht freigegeben — eine Rückfrage dafür wird nicht gespeichert.
        Assert.Equal(KiStufe.GrowPlanen, schluessel.Rueckfrage);
        Assert.Equal(new[] { "GrowPlanen" }, KiZugriffDienst.ZuDto(schluessel).RueckfrageBei);
        Assert.Equal(KiStufe.GrowPlanen, _dienst.Pruefen(klartext, Nachbar).Kontext!.Rueckfrage);

        // Stufe sperren nimmt die Rückfrage mit.
        _dienst.Aendern(schluessel.Id, "Claude", KiStufe.Dokumentieren, KiStufe.GrowPlanen);
        Assert.Equal(KiStufe.Keine, _dienst.Hole(schluessel.Id)!.Rueckfrage);

        // Ein neuer Schlüssel ohne Angabe fragt bei keiner Stufe.
        Assert.Equal(KiStufe.Keine, _dienst.Anlegen("Ohne", KiStufe.Dokumentieren).Schluessel.Rueckfrage);
    }

    [Fact]
    public void FalscherUndGesperrterSchluesselWerdenAbgewiesen()
    {
        Einschalten();
        var (schluessel, klartext) = _dienst.Anlegen("Claude", KiStufe.Dokumentieren);

        // Gleiches Präfix, anderer Rest: der Hash entscheidet, nicht das Präfix.
        var gefaelscht = klartext[..12] + new string('A', klartext.Length - 12);
        Assert.Equal(KiPruefung.Ungueltig, _dienst.Pruefen(gefaelscht, Nachbar).Ergebnis);
        Assert.Equal(KiPruefung.Ungueltig, _dienst.Pruefen("gok_kaputt", Nachbar).Ergebnis);

        _dienst.Sperren(schluessel.Id);
        Assert.Equal(KiPruefung.Gesperrt, _dienst.Pruefen(klartext, Nachbar).Ergebnis);
    }

    [Fact]
    public void ZehnFehlversucheSperrenDieAdresseFuenfzehnMinuten()
    {
        Einschalten();
        var (_, gueltig) = _dienst.Anlegen("Claude", KiStufe.Dokumentieren);
        var falsch = KiZugriffDienst.NeuerKlartext();

        for (var i = 1; i < KiZugriffDienst.FehlversucheBisSperre; i++)
        {
            var e = _dienst.Pruefen(falsch, Nachbar);
            Assert.Equal(KiPruefung.Ungueltig, e.Ergebnis);
            Assert.False(e.SperreBegonnen);
        }
        var zehnter = _dienst.Pruefen(falsch, Nachbar);
        Assert.Equal(KiPruefung.Ungueltig, zehnter.Ergebnis);
        Assert.True(zehnter.SperreBegonnen);

        // Gesperrt: ein falscher Schlüssel bekommt ZuVieleVersuche — auch als IPv4-in-IPv6.
        Assert.Equal(KiPruefung.ZuVieleVersuche, _dienst.Pruefen(falsch, Nachbar).Ergebnis);
        Assert.Equal(KiPruefung.ZuVieleVersuche, _dienst.Pruefen(falsch, Nachbar.MapToIPv6()).Ergebnis);
        Assert.Equal(KiPruefung.ZuVieleVersuche, _dienst.Pruefen("gok_kaputt", Nachbar).Ergebnis);
        // Fork AI (A-005): der gültige kommt trotzdem durch — die Sperre trifft nie einen gültigen Schlüssel.
        Assert.Equal(KiPruefung.Gueltig, _dienst.Pruefen(gueltig, Nachbar).Ergebnis);
        Assert.Equal(KiPruefung.Gueltig, _dienst.Pruefen(gueltig, Nachbar.MapToIPv6()).Ergebnis);
        Assert.True(_dienst.IstAdresseGesperrt(Nachbar), "Der gültige Schlüssel darf die Sperre nicht aufheben.");
        // Andere Adresse: frei.
        Assert.Equal(KiPruefung.Gueltig, _dienst.Pruefen(gueltig, IPAddress.Parse("172.30.33.6")).Ergebnis);

        _uhr.Jetzt += KiZugriffDienst.SperrDauer - TimeSpan.FromSeconds(1);
        Assert.Equal(KiPruefung.ZuVieleVersuche, _dienst.Pruefen(falsch, Nachbar).Ergebnis);

        // Die falschen Versuche während der Sperre haben nicht weitergezählt: nach Ablauf
        // ist die Adresse frei, und ein einzelner Fehlversuch sperrt nicht sofort wieder.
        _uhr.Jetzt += TimeSpan.FromSeconds(2);
        Assert.False(_dienst.IstAdresseGesperrt(Nachbar));
        Assert.Equal(KiPruefung.Gueltig, _dienst.Pruefen(gueltig, Nachbar).Ergebnis);
        // Nach der Sperre fängt die Zählung von vorn an: ein Fehlversuch sperrt nicht sofort wieder.
        Assert.Equal(KiPruefung.Ungueltig, _dienst.Pruefen(falsch, Nachbar).Ergebnis);
        Assert.Equal(KiPruefung.Gueltig, _dienst.Pruefen(gueltig, Nachbar).Ergebnis);
    }

    [Fact]
    public void FehlversucheVerfallenNachZehnMinuten()
    {
        Einschalten();
        var falsch = KiZugriffDienst.NeuerKlartext();

        for (var i = 0; i < KiZugriffDienst.FehlversucheBisSperre - 1; i++) _dienst.Pruefen(falsch, Nachbar);
        _uhr.Jetzt += KiZugriffDienst.FehlversuchFenster + TimeSpan.FromSeconds(1);

        var e = _dienst.Pruefen(falsch, Nachbar);
        Assert.False(e.SperreBegonnen);
        Assert.False(_dienst.IstAdresseGesperrt(Nachbar));
    }

    [Fact]
    public void BeiAusgeschaltetemZugangZaehltNichtsAlsFehlversuch()
    {
        var falsch = KiZugriffDienst.NeuerKlartext();
        for (var i = 0; i < KiZugriffDienst.FehlversucheBisSperre + 5; i++)
        {
            Assert.Equal(KiPruefung.ZugriffAus, _dienst.Pruefen(falsch, Nachbar).Ergebnis);
        }
        Assert.False(_dienst.IstAdresseGesperrt(Nachbar));
    }

    // ------------------------------------------------------- Stundenfenster

    [Fact]
    public void DasStundenfensterGleitet()
    {
        Assert.True(_dienst.SchaltbefehlZulassen(2));
        _uhr.Jetzt += TimeSpan.FromMinutes(30);
        Assert.True(_dienst.SchaltbefehlZulassen(2));
        Assert.False(_dienst.SchaltbefehlZulassen(2));

        // Der erste fällt nach einer Stunde heraus, der zweite noch nicht.
        _uhr.Jetzt += TimeSpan.FromMinutes(30);
        Assert.True(_dienst.SchaltbefehlZulassen(2));
        Assert.False(_dienst.SchaltbefehlZulassen(2));

        Assert.False(_dienst.SchaltbefehlZulassen(0));
    }

    // --------------------------------------------------------------- Stufen

    [Fact]
    public void StufenGehenAlsNamenUeberDieLeitung()
    {
        Assert.Equal(4, KiZugriffDienst.AlleStufen.Count);
        Assert.Equal(new[] { "Dokumentieren", "GeraeteSchalten" },
            KiZugriffDienst.StufenNamen(KiStufe.Dokumentieren | KiStufe.GeraeteSchalten));

        var stufen = KiZugriffDienst.StufenLesen(["dokumentieren", "Verwaltung"], out var unbekannt);
        Assert.Equal(KiStufe.Dokumentieren | KiStufe.Verwaltung, stufen);
        Assert.Empty(unbekannt);

        // Keine Zahlen, kein „Keine": Enum.TryParse nähme beides.
        KiZugriffDienst.StufenLesen(["15", "Keine", "Fliegen"], out unbekannt);
        Assert.Equal(new[] { "15", "Keine", "Fliegen" }, unbekannt);
    }

    [Theory]
    [InlineData("Bearer gok_abc", "gok_abc")]
    [InlineData("bearer   gok_abc  ", "gok_abc")]
    [InlineData("Bearer eyJhbGciOi", null)]
    [InlineData("Basic Z29rX2FiYw==", null)]
    [InlineData("", null)]
    public void NurEinBearerMitGokIstEinSchluessel(string kopf, string? erwartet)
    {
        var context = new DefaultHttpContext();
        if (kopf.Length > 0) context.Request.Headers.Authorization = kopf;
        Assert.Equal(erwartet, KiZugriffDienst.SchluesselAusKopf(context.Request));
    }
}
