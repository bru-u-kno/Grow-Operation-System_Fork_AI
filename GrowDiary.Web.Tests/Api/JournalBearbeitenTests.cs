using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Controllers;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// Ein Journaleintrag lässt sich nachträglich korrigieren.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (04.10.2026).</b> Ein Wasserwechsel stand mit
/// „CANNA pH- Pro Bloom: Menge nicht notiert" im Journal. Die Menge war
/// bekannt, ließ sich aber nicht nachtragen: der Controller kannte Anlegen und
/// Entfernen, kein Ändern. Wer korrigieren wollte, musste löschen und neu
/// anlegen — und verlor dabei Zeitpunkt und Reihenfolge.</para>
///
/// <para><b>Was hier gehalten wird.</b> Ändern trifft genau die geschickten
/// Felder; ein fehlendes Feld (<c>null</c>) verschiebt nicht still den
/// Zeitpunkt auf „jetzt" oder die Art auf „Notiz". Herkunft, Messungsbezug und
/// Anlagezeitpunkt bleiben. Die Chronik bekommt eine Zeile.</para>
/// </remarks>
public sealed class JournalBearbeitenTests : IDisposable
{
    private readonly string _temp;
    private readonly AppPaths _paths;
    private readonly GrowRepository _grows;
    private readonly JournalRepository _journal;
    private readonly AuditRepository _chronik;
    private readonly JournalApiController _controller;
    private readonly int _growId;

    public JournalBearbeitenTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), "JournalBearbeiten_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temp);
        _paths = new AppPaths(_temp);
        var zelt = TestDatabase.InitializeWithDefaultTent(_paths);
        _grows = new GrowRepository(_paths);
        _journal = new JournalRepository(_paths);
        _chronik = new AuditRepository(_paths);
        _controller = new JournalApiController(_grows, _journal, _chronik)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        _growId = _grows.CreateGrow(new GrowRun
        {
            TentId = zelt.Id, Name = "Lauf", StartDate = new DateTime(2026, 8, 1),
        });
    }

    public void Dispose()
    {
        try { Directory.Delete(_temp, recursive: true); } catch { }
    }

    private int Anlegen(string? titel = "Wasserwechsel RDWC 160 L",
        string? text = "CANNA pH- Pro Bloom: Menge nicht notiert",
        JournalEntryType art = JournalEntryType.ReservoirChange)
        => _journal.Create(new JournalEntry
        {
            GrowId = _growId,
            Title = titel,
            Body = text,
            EntryType = art,
            Source = ValueOrigin.Manual,
            OccurredAtUtc = new DateTime(2026, 10, 4, 10, 30, 0, DateTimeKind.Utc),
        });

    private JournalEntryDto Erfolg(ActionResult<JournalEntryDto> antwort)
    {
        var ok = Assert.IsType<OkObjectResult>(antwort.Result);
        return Assert.IsType<JournalEntryDto>(ok.Value);
    }

    [Fact]
    public void DerTextLaesstSichKorrigieren_UndKommtNachDemNeuladenWieder()
    {
        var id = Anlegen();
        var neu = "CANNA pH- Pro Bloom: ca. 25 ml\nPurolyt: 200 ml direkt nach dem Wechsel → ORP 450 mV";

        var dto = Erfolg(_controller.Update(id, new JournalEntryUpdateRequest { Body = neu }));

        Assert.Equal(neu, dto.Body);
        var gelesen = _journal.Get(id)!;
        Assert.Equal(neu, gelesen.Body);
        Assert.NotNull(gelesen.UpdatedAtUtc);
    }

    [Fact]
    public void NurGeschickteFelderAendernSich()
    {
        // Ein Assistent, der nur die vergessene Menge nachträgt, schickt nur
        // den Text. Titel, Art, Zeitpunkt, Herkunft dürfen sich dabei nicht
        // bewegen — der Anlage-Vertrag hätte den Zeitpunkt auf „jetzt" gesetzt.
        var id = Anlegen();
        var vorher = _journal.Get(id)!;

        Erfolg(_controller.Update(id, new JournalEntryUpdateRequest { Body = "nachgetragen" }));

        var nachher = _journal.Get(id)!;
        Assert.Equal(vorher.Title, nachher.Title);
        Assert.Equal(vorher.EntryType, nachher.EntryType);
        Assert.Equal(vorher.OccurredAtUtc, nachher.OccurredAtUtc);
        Assert.Equal(vorher.Source, nachher.Source);
        Assert.Equal(vorher.CreatedAtUtc, nachher.CreatedAtUtc);
        Assert.Equal(vorher.GrowId, nachher.GrowId);
    }

    [Fact]
    public void TitelArtUndZeitpunktLassenSichAendern()
    {
        var id = Anlegen();

        var dto = Erfolg(_controller.Update(id, new JournalEntryUpdateRequest
        {
            Title = "  Wasserwechsel – Blütewoche 7  ",
            EntryType = JournalEntryType.Action,
            OccurredAtLocal = "2026-10-03T18:15",
        }));

        Assert.Equal("Wasserwechsel – Blütewoche 7", dto.Title);
        Assert.Equal(JournalEntryType.Action, dto.EntryType);
        var erwartet = DateTime.SpecifyKind(new DateTime(2026, 10, 3, 18, 15, 0), DateTimeKind.Local).ToUniversalTime();
        Assert.Equal(erwartet, _journal.Get(id)!.OccurredAtUtc);
    }

    [Fact]
    public void DieHerkunftBleibt()
    {
        // Ein importierter Eintrag bleibt importiert, auch wenn man ihn korrigiert.
        var id = _journal.Create(new JournalEntry
        {
            GrowId = _growId, Title = "aus dem Archiv", Source = ValueOrigin.Imported,
        });

        Erfolg(_controller.Update(id, new JournalEntryUpdateRequest { Title = "korrigiert" }));

        Assert.Equal(ValueOrigin.Imported, _journal.Get(id)!.Source);
    }

    [Fact]
    public void UnbekannteIdGibt404()
    {
        var antwort = _controller.Update(987654, new JournalEntryUpdateRequest { Body = "x" });
        Assert.IsType<NotFoundObjectResult>(antwort.Result);
    }

    [Fact]
    public void TitelUndTextBeideLeer_WirdAbgelehnt()
    {
        var id = Anlegen();

        var antwort = _controller.Update(id, new JournalEntryUpdateRequest { Title = " ", Body = "" });

        Assert.IsType<BadRequestObjectResult>(antwort.Result);
        // …und der Eintrag ist unverändert.
        Assert.Equal("Wasserwechsel RDWC 160 L", _journal.Get(id)!.Title);
    }

    [Fact]
    public void NurDenTextLeeren_GehtWennDerTitelBleibt()
    {
        var id = Anlegen();

        var dto = Erfolg(_controller.Update(id, new JournalEntryUpdateRequest { Body = "" }));

        Assert.Null(dto.Body);
        Assert.Equal("Wasserwechsel RDWC 160 L", dto.Title);
    }

    [Fact]
    public void EinUnlesbarerZeitpunktWirdAbgelehnt()
    {
        var id = Anlegen();
        var vorher = _journal.Get(id)!.OccurredAtUtc;

        var antwort = _controller.Update(id, new JournalEntryUpdateRequest { OccurredAtLocal = "gestern abend" });

        Assert.IsType<BadRequestObjectResult>(antwort.Result);
        Assert.Equal(vorher, _journal.Get(id)!.OccurredAtUtc);
    }

    [Fact]
    public void EineUnbekannteArtWirdAbgelehnt()
    {
        // ASP.NET bindet {"entryType":99} still als Enum. Gespeichert waere das
        // eine Art, die keine Oberflaeche kennt — „Speichern" machte daraus
        // spaeter still eine Beobachtung (Pruefer 05.10.2026).
        var id = Anlegen();

        var antwort = _controller.Update(id, new JournalEntryUpdateRequest { EntryType = (JournalEntryType)99 });

        Assert.IsType<BadRequestObjectResult>(antwort.Result);
        Assert.Equal(JournalEntryType.ReservoirChange, _journal.Get(id)!.EntryType);
    }

    [Fact]
    public void AuchBeimAnlegenWirdEineUnbekannteArtAbgelehnt()
    {
        var antwort = _controller.Create(_growId, new JournalEntryCreateRequest
        {
            Title = "x", EntryType = (JournalEntryType)99, OccurredAtLocal = "2026-10-04T10:00",
        });

        Assert.IsType<BadRequestObjectResult>(antwort.Result);
        Assert.Empty(_journal.GetForGrow(_growId));
    }

    [Fact]
    public void DieChronikBekommtEineZeile()
    {
        var id = Anlegen();

        Erfolg(_controller.Update(id, new JournalEntryUpdateRequest { Body = "nachgetragen" }));

        var zeilen = _chronik.GetForGrow(_growId);
        Assert.Contains(zeilen, z => z.Action == "Journal geändert"
            && z.EntityType == "JournalEntry"
            && z.EntityId == id
            && z.Summary.Contains("Wasserwechsel RDWC 160 L", StringComparison.Ordinal));
    }

    [Fact]
    public void ZweimalSpeichern_BeideMaleKommtDerNeueStandAn()
    {
        // „Speichern, dann nochmal speichern" (CLAUDE.md, Reparatur wiederholen).
        var id = Anlegen();

        Erfolg(_controller.Update(id, new JournalEntryUpdateRequest { Body = "erste Fassung" }));
        var erstesMal = _journal.Get(id)!.UpdatedAtUtc;
        Erfolg(_controller.Update(id, new JournalEntryUpdateRequest { Body = "zweite Fassung" }));

        var gelesen = _journal.Get(id)!;
        Assert.Equal("zweite Fassung", gelesen.Body);
        Assert.True(gelesen.UpdatedAtUtc >= erstesMal);
        Assert.Equal(2, _chronik.GetForGrow(_growId).Count(z => z.Action == "Journal geändert"));
    }

    [Fact]
    public void EinNeuerEintragGiltAlsNieGeaendert()
    {
        var id = Anlegen();
        Assert.Null(_journal.Get(id)!.UpdatedAtUtc);
    }
}
