using System.Text.Json;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Der Slug ist die einzige Zutat für einen Weg aufs Handy, der hält.
/// </summary>
/// <remarks>
/// Der Ingress-Pfad trägt ein Token, das pro Anfrage wechselt — ein Lesezeichen
/// darauf stirbt. Stabil ist nur <c>/app/&lt;slug&gt;</c>. Raten geht
/// nicht: je nach Installationsweg heisst das Add-on <c>local_grow_os</c> oder
/// <c>&lt;repo-hash&gt;_grow_os</c>.
/// </remarks>
public sealed class SupervisorInfoServiceTests
{
    private static JsonDocument Json(string text) => JsonDocument.Parse(text);

    [Fact]
    public void ReadsTheSlugFromTheSupervisorEnvelope()
    {
        // Der Supervisor antwortet immer in { result, data }.
        using var document = Json("""{"result":"ok","data":{"slug":"a0d7b954_grow_os","name":"Grow OS"}}""");

        Assert.Equal("a0d7b954_grow_os", SupervisorInfoService.ReadSlug(document));
    }

    [Fact]
    public void ReadsTheLocallyInstalledSlugToo()
    {
        using var document = Json("""{"result":"ok","data":{"slug":"local_grow_os"}}""");

        Assert.Equal("local_grow_os", SupervisorInfoService.ReadSlug(document));
    }

    [Theory]
    [InlineData("""{"result":"ok"}""")]
    [InlineData("""{"result":"ok","data":{}}""")]
    [InlineData("""{"result":"ok","data":{"slug":""}}""")]
    [InlineData("""{"result":"ok","data":{"slug":"   "}}""")]
    [InlineData("""{"result":"ok","data":{"slug":42}}""")]
    public void WithoutAUsableSlug_NothingIsClaimed(string body)
    {
        // Lieber gar kein Pfad als ein erfundener: ein QR-Code auf eine falsche
        // Adresse fällt erst auf, wenn jemand mit dem Handy davorsteht.
        using var document = Json(body);

        Assert.Null(SupervisorInfoService.ReadSlug(document));
    }

    [Fact]
    public void TheSlugBecomesThePanelPath()
    {
        // /app/<slug> ist das feste App-Panel von HA. /<slug> gibt es nur mit
        // „In Seitenleiste anzeigen" — ohne das antwortete HA auf jeden Tipp
        // auf eine Push-Meldung mit „404: Not Found" (04.10.2026).
        Assert.Equal("/app/local_grow_os", SupervisorInfoService.PanelPath("local_grow_os"));
        Assert.Equal("/app/a0d7b954_grow_os", SupervisorInfoService.PanelPath("a0d7b954_grow_os"));
    }

    [Theory]
    [InlineData("aufgaben", "/app/d48160c2_grow_os_fork_ai/aufgaben")]
    [InlineData("/sensoren", "/app/d48160c2_grow_os_fork_ai/sensoren")]
    [InlineData("zelte/3", "/app/d48160c2_grow_os_fork_ai/zelte/3")]
    [InlineData("", "/app/d48160c2_grow_os_fork_ai")]
    [InlineData(null, "/app/d48160c2_grow_os_fork_ai")]
    public void ASeiteHangsBehindTheSlug(string? seite, string erwartet)
    {
        Assert.Equal(erwartet, SupervisorInfoService.PanelPath("d48160c2_grow_os_fork_ai", seite));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void WithoutASlug_ThereIsNoPanelPath(string? slug)
    {
        Assert.Null(SupervisorInfoService.PanelPath(slug));
    }
}
