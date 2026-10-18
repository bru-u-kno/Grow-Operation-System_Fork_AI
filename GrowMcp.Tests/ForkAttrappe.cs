using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using GrowMcp.Tools;
using GrowOsAccess;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Server;

namespace GrowMcp.Tests;

/// <summary>Eine Anfrage, wie sie bei Grow OS ankam.</summary>
internal sealed record Angekommen(string Methode, string Pfad, string? Authorization, string? Rumpf)
{
    /// <summary>Der Pfad ohne Abfrageteil und ohne führenden Schrägstrich.</summary>
    public string Weg => Pfad.Split('?')[0].TrimStart('/');

    /// <summary>Die Suche, mit der GrowOsDiscovery anklopft — keine Anfrage eines Werkzeugs.</summary>
    public bool IstAnklopfen => Weg == "api/agent-export/mappe";
}

/// <summary>
/// Grow OS zum Mitschreiben: beantwortet jede Anfrage und merkt sich, was kam —
/// samt Kopfzeile <c>Authorization</c> und Rumpf.
/// </summary>
internal sealed class ForkAttrappe : HttpMessageHandler
{
    private readonly List<(string Methode, Regex Weg, Func<Angekommen, (int Status, string Rumpf)> Antwort)> _antworten = [];

    public List<Angekommen> Anfragen { get; } = [];

    /// <summary>Was die Werkzeuge gefragt haben — ohne das Anklopfen der Suche.</summary>
    public IEnumerable<Angekommen> VonWerkzeugen => Anfragen.Where(a => !a.IstAnklopfen);

    /// <summary>Für diese Methode und diesen Weg (Regex auf den Pfad ohne Abfrage) so antworten.</summary>
    public ForkAttrappe Antwort(string methode, string weg, int status, string rumpf)
    {
        _antworten.Add((methode, new Regex("^" + weg + "$"), _ => (status, rumpf)));
        return this;
    }

    /// <summary>Wie <see cref="Antwort(string,string,int,string)"/>, aber die Antwort hängt von der Anfrage (und vom Stand) ab.</summary>
    public ForkAttrappe Antwort(string methode, string weg, Func<Angekommen, (int Status, string Rumpf)> antwort)
    {
        _antworten.Add((methode, new Regex("^" + weg + "$"), antwort));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var rumpf = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var angekommen = new Angekommen(
            request.Method.Method,
            request.RequestUri!.PathAndQuery,
            request.Headers.Authorization?.ToString(),
            rumpf);
        Anfragen.Add(angekommen);

        // Die zuletzt eingetragene passende Antwort gewinnt — ein Test kann so
        // die Vorgabe für einen Weg überschreiben.
        foreach (var (methode, weg, antwort) in Enumerable.Reverse(_antworten))
        {
            if ((methode == "*" || methode == angekommen.Methode) && weg.IsMatch(angekommen.Weg))
            {
                var (status, text) = antwort(angekommen);
                // Fork AI (Paket D): Bilder und Dateien — als Bytes mit eigenem Inhaltstyp.
                if (Dateien.FirstOrDefault(d => d.Weg.IsMatch(angekommen.Weg)) is { Typ: not null } datei)
                {
                    var inhalt = new ByteArrayContent(datei.Bytes);
                    inhalt.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(datei.Typ);
                    return new HttpResponseMessage((HttpStatusCode)status) { Content = inhalt };
                }
                return new HttpResponseMessage((HttpStatusCode)status)
                {
                    Content = new StringContent(text, Encoding.UTF8, "application/json"),
                };
            }
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
    }

    /// <summary>Fork AI (Paket D): Wege, die statt JSON eine Datei liefern (Regex auf den Pfad, Inhaltstyp, Bytes).</summary>
    public List<(Regex Weg, string? Typ, byte[] Bytes)> Dateien { get; } = [];

    /// <summary>Für GET auf diesen Weg ein Bild (oder eine Datei) liefern.</summary>
    public ForkAttrappe Datei(string weg, string typ, byte[] bytes, int status = 200)
    {
        Dateien.Add((new Regex("^" + weg + "$"), typ, bytes));
        return Antwort("GET", weg, status, string.Empty);
    }

    /// <summary>Eine Attrappe, die wie ein laufendes Grow OS mit einem Grow in der Blüte antwortet.</summary>
    public static ForkAttrappe MitGrow()
        => new ForkAttrappe()
            .Antwort("GET", @"api/grows/\d+", 200, """{"id":1,"tentId":1,"currentStage":"Flower","name":"Test"}""")
            .Antwort("GET", @"api/measurements/\d+", 200,
                """{"id":7,"growId":1,"takenAt":"2026-10-01T08:00:00","stage":"Flower","source":"Manual","notes":"alt","reservoirPh":5.8,"orpMv":450,"reservoirEc":1.2,"solutionChange":false}""")
            .Antwort("POST", @"api/grows/\d+/measurements", 201, """{"id":42,"growId":1,"stage":"Flower"}""")
            .Antwort("POST", @"api/dosing/pumps/\d+/dose", 200, """{"dosed":true,"ml":2,"seconds":3.1,"reason":"Dosiert."}""")
            .Antwort("GET", @"api/steuerung/raum", 200, """{"id":"haupt","name":"Aufstellraum","vorgabe":"Aufstellraum","zelte":[]}""")
            .Antwort("PUT", @"api/steuerung/raum", 200, """{"id":"haupt","name":"Keller","vorgabe":"Aufstellraum","zelte":[]}""")
            .Antwort("GET", @"api/settings/tents", 200, """[{"id":1,"name":"Blütezelt","raumId":null}]""")
            .Antwort("GET", @"api/settings/tents/\d+", 200, """{"id":1,"name":"Blütezelt","raumId":null,"status":"Active"}""")
            .Antwort("PUT", @"api/settings/tents/\d+", 200, """{"id":1,"name":"Blütezelt","raumId":"haupt"}""")
            // Fork AI (Paket C1): Kosten, CO₂-Artikel, Pflanze, Sorten, ein laufender Probelauf.
            .Antwort("GET", @"api/kosten", 200, KostenSeite)
            .Antwort("GET", @"api/steuerung/co2", 200, """{"einstellungen":{"kostenArtikelId":1}}""")
            .Antwort("POST", @"api/kosten/verbrauch", 201, """[{"id":5,"artikelId":2,"menge":20}]""")
            .Antwort("POST", @"api/kosten/nachfuellungen", 201, """{"id":12,"artikelId":1,"menge":10,"kostenEur":38,"growId":1,"zeitpunktUtc":"2026-10-18T10:00:00Z"}""")
            .Antwort("POST", @"api/kosten/anschaffungen", 201, """{"id":3,"name":"Luftbefeuchter","stueck":1,"einzelpreisEur":49,"gesamtEur":49,"growId":1}""")
            .Antwort("GET", @"api/plants/\d+", 200, Pflanze)
            .Antwort("PUT", @"api/plants/\d+", 200, Pflanze)
            .Antwort("GET", @"api/strains", 200, """[{"id":1,"name":"White Widow"},{"id":2,"name":"Gorilla Glue"}]""")
            .Antwort("GET", @"api/steuerung/probelauf", 200, """[{"id":7,"modul":"entfeuchter","modulTitel":"Entfeuchter","status":"Laeuft","restSekunden":600}]""")
            .Antwort("POST", @"api/steuerung/probelauf/\d+/abbrechen", 200, """{"id":7,"modulTitel":"Entfeuchter","status":"Nachlauf","abbruchGrund":"Von Hand abgebrochen."}""");

    /// <summary>Fork AI (Paket C1): die Kostenseite eines Grows in der Form von <c>GET /api/kosten?growId=</c>.</summary>
    public const string KostenSeite = """
        {"grow":{"id":1,"name":"Test","tag":74,"phase":"Blüte","pflanzen":4},
         "summe":{"gesamtEur":165.27,"stromEur":145.77,"artikelEur":19.5,"anschaffungenEur":0,"proTagEur":2.23,"proPflanzeEur":41.32,"prognoseErnteEur":227.8,"prognoseHinweis":"bei 9 Wochen Blüte"},
         "strom":{"eingerichtet":true,"preisCentProKwh":32,"kwhSeitStart":455.5,"kwhProTag":6.24,"eurProTag":2.0},
         "artikel":[
           {"id":1,"name":"CO₂-Flasche","einheit":"kg","preisEur":39,"gebinde":10,"aktiv":true,
            "aktuell":{"id":2,"zeitpunktUtc":"2026-09-02T10:00:00Z","menge":10,"kostenEur":39,"fuellstandProzent":50,"fuellstandQuelle":"gemessen","prognoseLeerAmUtc":"2026-11-19T19:00:00Z","prognoseTage":64.4,"prognoseQuelle":"gemessen","eurProTag":0.61}},
           {"id":2,"name":"pH-Minus","einheit":"ml","preisEur":9,"gebinde":1000,"aktiv":true},
           {"id":3,"name":"Purolyt","einheit":"ml","preisEur":49.9,"gebinde":5000,"aktiv":true},
           {"id":4,"name":"Purolyt alt","einheit":"ml","preisEur":45,"gebinde":5000,"aktiv":false}],
         "anschaffungen":[]}
        """;

    /// <summary>Fork AI (Paket C1): eine Pflanze wie <c>GET /api/plants/{id}</c> (Null-Felder fehlen wie bei Grow OS).</summary>
    public const string Pflanze = """
        {"id":2,"strainId":1,"growId":1,"siteIndex":2,"label":"Pflanze 2","plantRole":"Production","plantStatus":"Active",
         "startedAt":"2026-08-06T12:00:00+02:00","notes":"alt","strainName":"White Widow","createdAtUtc":"2026-10-18T14:31:05Z","updatedAtUtc":"2026-10-18T14:31:05Z"}
        """;
}

/// <summary>Ein fester Schlüssel — anstelle der laufenden HTTP-Anfrage.</summary>
internal sealed class FesterSchluessel(string? wert) : IForkSchluesselQuelle
{
    public string? Schluessel => wert;
}

internal static class Werkzeugkasten
{
    /// <summary>Ein Schlüssel in der Form, die Grow OS ausgibt (gok_ + 43 Zeichen base64url).</summary>
    public const string ForkSchluessel = "gok_Abc123_-Abc123_-Abc123_-Abc123_-Abc123_";

    /// <summary>So sieht der MCP-Schlüssel dieses Add-ons aus — er darf Grow OS nie erreichen.</summary>
    public const string McpSchluessel = "mcpXyz789_-mcpXyz789_-mcpXyz789_-mcpXyz789";

    private sealed class EinKlient(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>Den Leser bauen, wie AddGrowOsAccess es täte — nur gegen die Attrappe.</summary>
    /// <param name="schluessel">Was die Quelle liefert: ein gok_-Schlüssel, oder <c>null</c> wie beim MCP-Schlüssel.</param>
    public static GrowOsReader Leser(ForkAttrappe fork, string? schluessel)
    {
        var fabrik = new EinKlient(fork);
        var discovery = new GrowOsDiscovery(
            fabrik,
            new SupervisorClient(fabrik, NullLogger<SupervisorClient>.Instance),
            new GrowOsOptions { Adresse = "http://grow-os.test:5076" },
            NullLogger<GrowOsDiscovery>.Instance);
        return new GrowOsReader(fabrik.CreateClient("grow-os"), discovery, new FesterSchluessel(schluessel));
    }

    /// <summary>Alle Klassen mit Werkzeugen.</summary>
    public static Type[] Werkzeugklassen()
        => typeof(GrowTools).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .ToArray();

    /// <summary>Alle Methoden, die als Werkzeug angeboten werden — über alle Klassen.</summary>
    public static MethodInfo[] Werkzeugmethoden()
        => Werkzeugklassen()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .ToArray();

    public static string Name(MethodInfo methode) => methode.GetCustomAttribute<McpServerToolAttribute>()!.Name!;

    public static MethodInfo Werkzeug(string name) => Werkzeugmethoden().Single(m => Name(m) == name);

    /// <summary>Eine Instanz der Klasse, zu der das Werkzeug gehört.</summary>
    public static object Instanz(MethodInfo methode, GrowOsReader leser)
        => Activator.CreateInstance(methode.DeclaringType!, leser)!;

    /// <summary>Ein Werkzeug aufrufen wie ein Klient beim ersten Mal: Pflichtfelder plausibel, der Rest auf Vorgabe.</summary>
    public static async Task<object?> AufrufenAsync(MethodInfo methode, GrowOsReader leser, IReadOnlyDictionary<string, object?>? werte = null)
    {
        var argumente = methode.GetParameters()
            .Select(p => werte is not null && werte.TryGetValue(p.Name!, out var wert) ? wert : StandardWert(p))
            .ToArray();
        var aufgabe = (Task)methode.Invoke(Instanz(methode, leser), argumente)!;
        await aufgabe;
        return aufgabe.GetType().GetProperty("Result")!.GetValue(aufgabe);
    }

    public static async Task<string> TextAsync(string werkzeug, GrowOsReader leser, IReadOnlyDictionary<string, object?>? werte = null)
        => (string)(await AufrufenAsync(Werkzeug(werkzeug), leser, werte))!;

    public static object? StandardWert(ParameterInfo p)
    {
        if (p.HasDefaultValue) return p.DefaultValue;
        if (p.ParameterType == typeof(CancellationToken)) return CancellationToken.None;
        var beispiel = p.GetCustomAttribute<BeispielAttribute>()?.Wert;
        if (p.ParameterType == typeof(int)) return beispiel is null ? 1 : int.Parse(beispiel, System.Globalization.CultureInfo.InvariantCulture);
        if (p.ParameterType == typeof(double)) return beispiel is null ? 1.0 : double.Parse(beispiel, System.Globalization.CultureInfo.InvariantCulture);
        if (p.ParameterType == typeof(string)) return beispiel ?? "test";
        if (p.ParameterType == typeof(bool)) return false;
        return p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null;
    }
}
