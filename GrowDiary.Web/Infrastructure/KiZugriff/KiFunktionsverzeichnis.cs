using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using GrowDiary.Web.Api.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace GrowDiary.Web.Infrastructure.KiZugriff;

/// <summary>
/// Fork AI (Paket D, 18.10.2026): Das Verzeichnis aller Funktionen von Grow OS unter <c>/api</c> —
/// erzeugt aus den Routen der laufenden App, nicht von Hand gepflegt.
/// </summary>
/// <remarks>
/// <para><b>Wozu.</b> Über den Grow MCP soll sich alles im Fork steuern lassen. Statt für jede Funktion ein
/// eigenes Werkzeug zu bauen, liest der Assistent dieses Verzeichnis und ruft die Funktion über ein allgemeines
/// Werkzeug auf. Was erlaubt ist, entscheidet weiter die Sperre (<see cref="KiZugriffSperre"/>).</para>
/// <para><b>Eine Wahrheit.</b> Die Einstufung jedes Eintrags kommt aus <see cref="KiZugriffSperre.Einstufen"/> —
/// derselben Funktion, mit der die Sperre jede Anfrage entscheidet. Das Verzeichnis kann deshalb nicht
/// versprechen, was die Sperre ablehnt. Gehalten von <c>KiFunktionsverzeichnisTests</c>.</para>
/// <para><b>Beschreibungen</b> stammen aus der XML-Doku (<c>GenerateDocumentationFile</c>, die Datei liegt neben
/// der Assembly); fehlt sie, steht dort Controller und Aktion.</para>
/// </remarks>
public sealed class KiFunktionsverzeichnis
{
    /// <summary>Die Methoden, die das Verzeichnis führt. HEAD und OPTIONS sind keine Funktionen.</summary>
    public static readonly string[] Methoden = ["GET", "POST", "PUT", "PATCH", "DELETE"];

    private const int BeschreibungHoechstens = 240;

    private readonly EndpointDataSource _quelle;
    private readonly Lazy<KiFunktionenDto> _verzeichnis;

    public KiFunktionsverzeichnis(EndpointDataSource quelle)
    {
        _quelle = quelle;
        // Die Routen ändern sich zur Laufzeit nicht — einmal erzeugen genügt.
        _verzeichnis = new Lazy<KiFunktionenDto>(() => Erzeugen(_quelle.Endpoints, XmlDoku.Laden(typeof(KiFunktionsverzeichnis).Assembly)));
    }

    public KiFunktionenDto Lesen() => _verzeichnis.Value;

    public const string Hinweis =
        "Je Methode und Weg ein Eintrag. zugang: frei = jeder gültige Schlüssel; stufe = der Schlüssel braucht alle Stufen in stufen; "
        + "nie = über einen Schlüssel nie (Grund in nieGrund); nicht-eingestuft = gesperrt. schaltbefehl = zählt gegen die Schaltbefehle je Stunde, "
        + "sicherungVorher = Grow OS legt vorher eine Sicherung an. Platzhalter wie {id} im pfad durch den Wert ersetzen.";

    /// <summary>Das Verzeichnis aus einer Menge von Endpunkten — öffentlich für die Zählung.</summary>
    public static KiFunktionenDto Erzeugen(IEnumerable<Endpoint> endpunkte, XmlDoku doku)
    {
        var liste = new List<KiFunktionDto>();
        foreach (var endpunkt in endpunkte.OfType<RouteEndpoint>())
        {
            if (endpunkt.Metadata.GetMetadata<ControllerActionDescriptor>() is not { } aktion) continue;
            var vorlage = Vorlage(endpunkt);
            if (!IstApiWeg(vorlage)) continue;

            foreach (var methode in MethodenVon(endpunkt))
            {
                liste.Add(Eintrag(endpunkt, aktion, methode, vorlage, doku));
            }
        }

        var sortiert = liste
            .OrderBy(f => f.Pfad, StringComparer.Ordinal)
            .ThenBy(f => Array.IndexOf(Methoden, f.Methode))
            .ToList();
        return new KiFunktionenDto(sortiert.Count, Hinweis, sortiert);
    }

    /// <summary>Die Routenvorlage mit führendem Schrägstrich, etwa <c>/api/plants/{id:int}</c>.</summary>
    public static string Vorlage(RouteEndpoint endpunkt) => "/" + (endpunkt.RoutePattern.RawText ?? string.Empty).TrimStart('/', '~');

    public static bool IstApiWeg(string vorlage)
        => vorlage.Equals("/api", StringComparison.OrdinalIgnoreCase) || vorlage.StartsWith("/api/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Die Methoden eines Endpunkts. Ohne Verb-Angabe nimmt ASP.NET jede Methode an — das Verzeichnis führt dann
    /// alle fünf, damit die Einstufung je Methode ehrlich dasteht (GET frei, Schreiben nach Einstufung).
    /// </summary>
    public static IEnumerable<string> MethodenVon(Endpoint endpunkt)
    {
        var angegeben = endpunkt.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;
        if (angegeben is null || angegeben.Count == 0) return Methoden;
        return Methoden.Where(m => angegeben.Contains(m, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Ein Pfad, wie ihn eine Anfrage hätte: jeder Platzhalter durch „1" ersetzt.</summary>
    /// <remarks>Die Sperre prüft den Verwaltungsweg am Anfragepfad; Platzhalter stehen nie in einem Verwaltungs-Präfix.</remarks>
    public static string BeispielPfad(string vorlage) => Regex.Replace(vorlage, @"\{[^}]+\}", "1");

    /// <summary>Die Vorlage ohne Einschränkungen und Vorgaben: <c>{id:int}</c> → <c>{id}</c>, <c>{**rest}</c> → <c>{rest}</c>.</summary>
    public static string PfadOhneEinschraenkungen(string vorlage)
        => Regex.Replace(vorlage, @"\{\**([^}:=?]+)[^}]*\}", "{$1}");

    private static KiFunktionDto Eintrag(RouteEndpoint endpunkt, ControllerActionDescriptor aktion, string methode, string vorlage, XmlDoku doku)
    {
        var einstufung = KiZugriffSperre.Einstufen(endpunkt, methode, new PathString(BeispielPfad(vorlage)))
                         // Ein Controller-Endpunkt ist nie der Fallback; null käme nur ohne Endpunkt.
                         ?? throw new InvalidOperationException($"Keine Einstufung für {methode} {vorlage}.");

        var zugang = einstufung.NieGrund is not null ? "nie"
            : einstufung.Frei ? "frei"
            : einstufung.NichtEingestuft ? "nicht-eingestuft"
            : "stufe";

        var stufen = zugang is "frei" or "nie"
            ? Array.Empty<string>()
            : KiZugriffDienst.StufenNamen(einstufung.BenoetigteStufen).ToArray();

        var (routen, abfrage, rumpf, datei) = Angaben(endpunkt, aktion);

        return new KiFunktionDto(
            Methode: methode,
            Pfad: PfadOhneEinschraenkungen(vorlage),
            Bereich: Bereich(vorlage),
            Art: methode switch { "GET" => "lesen", "POST" => "anlegen", "DELETE" => "löschen", _ => "ändern" },
            Zugang: zugang,
            Stufen: stufen,
            Verwaltungsweg: einstufung.Verwaltungsweg,
            NieGrund: einstufung.NieGrund,
            Schaltbefehl: einstufung.Schaltbefehl,
            SicherungVorher: einstufung.SicherungVorher,
            Beschreibung: Beschreibung(aktion, doku),
            Routenparameter: routen,
            Abfrage: abfrage,
            Rumpf: rumpf,
            DateiHochladen: datei,
            LiefertDatei: LiefertDatei(endpunkt, aktion));
    }

    private static string Bereich(string vorlage)
    {
        var teile = vorlage.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return teile.Length >= 2 ? teile[1].ToLowerInvariant() : "api";
    }

    // ------------------------------------------------------------ Beschreibung

    private static string Beschreibung(ControllerActionDescriptor aktion, XmlDoku doku)
    {
        var text = doku.Zusammenfassung(aktion.MethodInfo);
        if (string.IsNullOrWhiteSpace(text)) return $"{aktion.ControllerName}.{aktion.ActionName}";

        // „Fork AI (A-003, 03.10.2026): …" sagt dem Assistenten nichts — die Herkunft weglassen.
        text = Regex.Replace(text, @"^\s*Fork AI(\s*\([^)]*\))?\s*:\s*", string.Empty);
        if (text.Length > BeschreibungHoechstens) text = text[..(BeschreibungHoechstens - 1)].TrimEnd() + "…";
        return text;
    }

    // ------------------------------------------------------------ Angaben

    private static (IReadOnlyList<KiParameterDto> Routen, IReadOnlyList<KiParameterDto> Abfrage, KiRumpfDto? Rumpf, bool Datei)
        Angaben(RouteEndpoint endpunkt, ControllerActionDescriptor aktion)
    {
        var routenNamen = endpunkt.RoutePattern.Parameters.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        var methodenParameter = aktion.MethodInfo.GetParameters().ToDictionary(p => p.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        var routen = endpunkt.RoutePattern.Parameters
            .Select(p => new KiParameterDto(
                p.Name,
                methodenParameter.TryGetValue(p.Name, out var mp) ? TypText(mp.ParameterType, 0) : RoutenTyp(p),
                !p.IsOptional && p.Default is null))
            .ToList();

        var abfrage = new List<KiParameterDto>();
        KiRumpfDto? rumpf = null;
        var datei = false;

        foreach (var parameter in aktion.Parameters)
        {
            var typ = parameter.ParameterType;
            var quelle = parameter.BindingInfo?.BindingSource;

            if (typeof(IFormFile).IsAssignableFrom(typ) || typeof(IFormFileCollection).IsAssignableFrom(typ)
                || typeof(IEnumerable<IFormFile>).IsAssignableFrom(typ)
                || quelle == BindingSource.Form || quelle == BindingSource.FormFile)
            {
                datei = true;
                continue;
            }

            if (quelle == BindingSource.Services || quelle == BindingSource.Special || quelle == BindingSource.Header
                || typ == typeof(CancellationToken) || typ == typeof(HttpContext))
            {
                continue;
            }

            if (routenNamen.ContainsKey(parameter.Name) || quelle == BindingSource.Path) continue;

            if (quelle == BindingSource.Body || (quelle is null && !IstEinfach(typ)))
            {
                rumpf = new KiRumpfDto(TypName(typ), Felder(typ, 0));
                continue;
            }

            var name = parameter.BindingInfo?.BinderModelName ?? parameter.Name;
            var methodenParameterInfo = methodenParameter.GetValueOrDefault(parameter.Name);
            var pflicht = methodenParameterInfo is not null && !methodenParameterInfo.HasDefaultValue
                          && Nullable.GetUnderlyingType(typ) is null && typ.IsValueType;
            abfrage.Add(new KiParameterDto(name, TypText(typ, 0), pflicht));
        }

        return (routen, abfrage, rumpf, datei);
    }

    private static string RoutenTyp(RoutePatternParameterPart teil)
    {
        var einschraenkung = teil.ParameterPolicies.Select(p => p.Content).FirstOrDefault(c => c is not null) ?? string.Empty;
        return einschraenkung switch
        {
            "int" or "long" => "Ganzzahl",
            "double" or "decimal" or "float" => "Zahl",
            "bool" => "ja/nein",
            "guid" => "Kennung",
            _ => "Text",
        };
    }

    private static bool LiefertDatei(RouteEndpoint endpunkt, ControllerActionDescriptor aktion)
    {
        var produces = endpunkt.Metadata.OfType<ProducesAttribute>().SelectMany(p => p.ContentTypes);
        if (produces.Any(t => !t.Contains("json", StringComparison.OrdinalIgnoreCase) && !t.StartsWith("text/", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var antwortTypen = endpunkt.Metadata.OfType<ProducesResponseTypeAttribute>().Select(p => p.Type)
            .Append(Ausgepackt(aktion.MethodInfo.ReturnType));
        return antwortTypen.Any(t => t is not null && typeof(FileResult).IsAssignableFrom(t));
    }

    private static Type Ausgepackt(Type typ)
    {
        while (typ.IsGenericType && (typ.GetGenericTypeDefinition() == typeof(Task<>) || typ.GetGenericTypeDefinition() == typeof(ValueTask<>)
                                     || typ.GetGenericTypeDefinition() == typeof(ActionResult<>)))
        {
            typ = typ.GetGenericArguments()[0];
        }
        return typ;
    }

    // ------------------------------------------------------------ Typen in Worten

    private const int TiefeHoechstens = 2;

    private static bool IstEinfach(Type typ)
    {
        typ = Nullable.GetUnderlyingType(typ) ?? typ;
        return typ.IsPrimitive || typ.IsEnum || typ == typeof(string) || typ == typeof(decimal) || typ == typeof(DateTime)
               || typ == typeof(DateTimeOffset) || typ == typeof(DateOnly) || typ == typeof(TimeOnly) || typ == typeof(TimeSpan)
               || typ == typeof(Guid);
    }

    private static string TypName(Type typ)
    {
        typ = Nullable.GetUnderlyingType(typ) ?? typ;
        if (!typ.IsGenericType) return typ.Name;
        var name = typ.Name[..typ.Name.IndexOf('`')];
        return $"{name}<{string.Join(", ", typ.GetGenericArguments().Select(TypName))}>";
    }

    /// <summary>Ein Typ in Worten — verschachtelte Objekte bis <see cref="TiefeHoechstens"/> Ebenen mit ihren Feldern.</summary>
    public static string TypText(Type typ, int tiefe)
    {
        var innen = Nullable.GetUnderlyingType(typ);
        if (innen is not null) return TypText(innen, tiefe) + "?";

        if (typ == typeof(string)) return "Text";
        if (typ == typeof(bool)) return "ja/nein";
        if (typ == typeof(int) || typ == typeof(long) || typ == typeof(short) || typ == typeof(byte)) return "Ganzzahl";
        if (typ == typeof(double) || typ == typeof(float) || typ == typeof(decimal)) return "Zahl";
        if (typ == typeof(DateTime) || typ == typeof(DateTimeOffset)) return "Zeitpunkt";
        if (typ == typeof(DateOnly)) return "Datum (yyyy-MM-dd)";
        if (typ == typeof(TimeOnly)) return "Uhrzeit (HH:mm)";
        if (typ == typeof(TimeSpan)) return "Dauer (hh:mm:ss)";
        if (typ == typeof(Guid)) return "Kennung";
        if (typ.IsEnum) return "eins von: " + string.Join(", ", Enum.GetNames(typ));
        if (typ == typeof(JsonElement) || typ == typeof(object)) return "beliebiges JSON";

        if (typ != typeof(string) && typeof(IDictionary).IsAssignableFrom(typ)
            || (typ.IsGenericType && typ.GetInterfaces().Append(typ).Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>))))
        {
            return "Zuordnung (Name → Wert)";
        }

        var element = typ.IsArray ? typ.GetElementType()
            : typ.GetInterfaces().Append(typ).FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0];
        if (element is not null) return "Liste von " + TypText(element, tiefe);

        if (tiefe >= TiefeHoechstens) return $"Objekt ({TypName(typ)})";
        var felder = Felder(typ, tiefe + 1);
        return felder.Count == 0
            ? $"Objekt ({TypName(typ)})"
            : "Objekt {" + string.Join(", ", felder.Select(f => $"{f.Name}: {f.Typ}")) + "}";
    }

    /// <summary>Die Felder eines Rumpf-Typs unter ihren JSON-Namen (camelCase, wie <c>Program.cs</c> serialisiert).</summary>
    public static IReadOnlyList<KiFeldDto> Felder(Type typ, int tiefe)
    {
        typ = Nullable.GetUnderlyingType(typ) ?? typ;
        if (IstEinfach(typ)) return [];

        return typ.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0 && p.CanWrite || IstRecordParameter(typ, p))
            .Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>() is not { Condition: JsonIgnoreCondition.Always })
            .Select(p => new KiFeldDto(
                p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(p.Name),
                TypText(p.PropertyType, tiefe)))
            .ToList();
    }

    /// <summary>Positionale Records haben nur init-Eigenschaften (CanWrite ist dort wahr) — und Konstruktorparameter gleichen Namens.</summary>
    private static bool IstRecordParameter(Type typ, PropertyInfo eigenschaft)
        => typ.GetConstructors().Any(k => k.GetParameters().Any(p => string.Equals(p.Name, eigenschaft.Name, StringComparison.OrdinalIgnoreCase)));
}

/// <summary>
/// Fork AI (Paket D, 18.10.2026): Die Zusammenfassungen (<c>&lt;summary&gt;</c>) aus der XML-Doku der Assembly.
/// </summary>
/// <remarks>Fehlt die Datei (etwa ein Bau ohne <c>GenerateDocumentationFile</c>), bleibt sie leer — nie eine Ausnahme.</remarks>
public sealed class XmlDoku
{
    private readonly Dictionary<string, string> _methoden;

    private XmlDoku(Dictionary<string, string> methoden) => _methoden = methoden;

    public static XmlDoku Leer { get; } = new(new Dictionary<string, string>(StringComparer.Ordinal));

    /// <summary>Wie viele Methoden eine Zusammenfassung haben — für den Selbsttest.</summary>
    public int Anzahl => _methoden.Count;

    public static XmlDoku Laden(Assembly assembly)
    {
        try
        {
            var datei = Path.ChangeExtension(assembly.Location, ".xml");
            if (string.IsNullOrEmpty(assembly.Location) || !File.Exists(datei)) return Leer;

            var methoden = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var member in XDocument.Load(datei).Descendants("member"))
            {
                var name = member.Attribute("name")?.Value;
                if (name is null || !name.StartsWith("M:", StringComparison.Ordinal)) continue;
                var summary = member.Element("summary");
                if (summary is null) continue;

                // Schlüssel ohne Parameterliste: Überladungen teilen sich die erste Zusammenfassung.
                var klammer = name.IndexOf('(');
                var schluessel = klammer < 0 ? name : name[..klammer];
                var text = Text(summary);
                if (text.Length > 0) methoden.TryAdd(schluessel, text);
            }
            return new XmlDoku(methoden);
        }
        catch (Exception ex) when (ex is IOException or System.Xml.XmlException or UnauthorizedAccessException)
        {
            return Leer;
        }
    }

    public string? Zusammenfassung(MethodInfo methode)
    {
        var typ = (methode.DeclaringType?.FullName ?? string.Empty).Replace('+', '.');
        return _methoden.GetValueOrDefault($"M:{typ}.{methode.Name}");
    }

    /// <summary>Der Text einer Zusammenfassung: <c>&lt;see cref&gt;</c> als letzter Namensteil, Leerraum zusammengezogen.</summary>
    private static string Text(XElement element)
    {
        var text = new StringBuilder();
        foreach (var knoten in element.Nodes())
        {
            switch (knoten)
            {
                case XText t:
                    text.Append(t.Value);
                    break;
                case XElement e when e.Attribute("cref") is { } cref:
                    var wert = cref.Value;
                    var ende = wert.IndexOf('(');
                    if (ende >= 0) wert = wert[..ende];
                    text.Append(wert[(wert.LastIndexOf('.') + 1)..]);
                    break;
                case XElement e when e.Attribute("name") is { } verweis:
                    text.Append(verweis.Value);
                    break;
                case XElement e:
                    text.Append(Text(e));
                    break;
            }
        }
        return Regex.Replace(text.ToString(), @"\s+", " ").Trim();
    }
}
