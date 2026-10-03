using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;

namespace GrowMcp.Tests;

/// <summary>
/// Die gebaute <c>GrowDiary.Web.dll</c> — zum Nachsehen, nicht zum Benutzen.
/// </summary>
/// <remarks>
/// <para>Fork AI (A-004, 03.10.2026): Die Schreib-Werkzeuge schicken Feldnamen
/// und Wege an Grow OS. „Bezeichner nie aus dem Kopf" (CLAUDE.md, Prüfung 3)
/// heisst hier: nicht gegen eine abgetippte Liste prüfen, sondern gegen die
/// Typen, die Grow OS wirklich hat. Grow OS wird deshalb mitgebaut
/// (<c>ReferenceOutputAssembly="false"</c> im Testprojekt) und hier in einen
/// eigenen Ladekontext geladen.</para>
///
/// <para>Gesucht wird die Fassung in derselben Konfiguration wie dieser Test
/// (Debug lokal, Release im Tor). Fehlt sie, schlägt der Test fehl — er
/// überspringt sich nicht.</para>
/// </remarks>
internal static class GrowOsBau
{
    private static readonly Lazy<Assembly> Geladen = new(Laden);

    public static Assembly Assembly => Geladen.Value;

    /// <summary>Ein Typ aus Grow OS, etwa <c>GrowDiary.Web.Api.Contracts.MeasurementUpsertRequest</c>.</summary>
    public static Type Typ(string vollerName)
        => Assembly.GetType(vollerName, throwOnError: false)
           ?? throw new InvalidOperationException($"Grow OS hat keinen Typ {vollerName} (mehr).");

    private static Assembly Laden()
    {
        var hier = AppContext.BaseDirectory;
        var wurzel = hier;
        while (wurzel is not null && !File.Exists(Path.Combine(wurzel, "repository.yaml")))
            wurzel = Path.GetDirectoryName(wurzel);
        if (wurzel is null) throw new InvalidOperationException("Repository-Wurzel nicht gefunden.");

        // .../GrowMcp.Tests/bin/<Konfiguration>/net8.0/
        var konfiguration = new DirectoryInfo(hier.TrimEnd(Path.DirectorySeparatorChar)).Parent!.Name;
        var pfad = Path.Combine(wurzel, "GrowDiary.Web", "bin", konfiguration, "net8.0", "GrowDiary.Web.dll");
        if (!File.Exists(pfad))
        {
            throw new InvalidOperationException(
                $"{pfad} fehlt. Das Testprojekt baut Grow OS mit (ProjectReference ohne Referenz) — ist der Bau durchgelaufen?");
        }

        return new Ladekontext(pfad).LoadFromAssemblyPath(pfad);
    }

    /// <summary>Lädt Grow OS samt seiner Pakete aus dessen eigenem Ausgabeordner.</summary>
    private sealed class Ladekontext(string pfad) : AssemblyLoadContext("grow-os-zum-nachsehen", isCollectible: false)
    {
        private readonly AssemblyDependencyResolver _aufloeser = new(pfad);

        protected override Assembly? Load(AssemblyName name)
        {
            // Rahmenwerk (System.*, Microsoft.AspNetCore.*) kommt aus dem
            // gemeinsamen Kontext; nur was Grow OS selbst mitbringt, von dort.
            var datei = _aufloeser.ResolveAssemblyToPath(name);
            return datei is null ? null : LoadFromAssemblyPath(datei);
        }
    }

    /// <summary>Ein Weg, den ein Controller in Grow OS anbietet.</summary>
    public sealed record Weg(string Methode, string Vorlage, Regex Muster, string Controller, string Aktion);

    /// <summary>Alle Wege aller Controller in Grow OS — aus den Routing-Attributen gelesen.</summary>
    public static IReadOnlyList<Weg> Wege()
    {
        var wege = new List<Weg>();
        foreach (var controller in Assembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("Controller", StringComparison.Ordinal)))
        {
            var vorsatz = controller.GetCustomAttributesData()
                .Where(a => a.AttributeType.Name == "RouteAttribute")
                .Select(a => (string)a.ConstructorArguments[0].Value!)
                .FirstOrDefault() ?? string.Empty;

            foreach (var aktion in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                foreach (var attribut in aktion.GetCustomAttributesData())
                {
                    var name = attribut.AttributeType.Name;
                    if (!name.StartsWith("Http", StringComparison.Ordinal) || !name.EndsWith("Attribute", StringComparison.Ordinal)) continue;

                    var methode = name["Http".Length..^"Attribute".Length].ToUpperInvariant();
                    var vorlage = attribut.ConstructorArguments.Count > 0 ? attribut.ConstructorArguments[0].Value as string : null;
                    var ganz = vorlage is not null && vorlage.StartsWith('/')
                        ? vorlage.TrimStart('/')
                        : string.Join('/', new[] { vorsatz, vorlage ?? string.Empty }.Where(t => t.Length > 0));

                    wege.Add(new Weg(methode, ganz, Muster(ganz), controller.Name, aktion.Name));
                }
            }
        }
        return wege;
    }

    /// <summary>Aus <c>api/grows/{id:int}/journal</c> ein Muster für einen echten Pfad machen.</summary>
    private static Regex Muster(string vorlage)
    {
        var teile = vorlage.Split('/').Select(teil =>
        {
            if (!teil.StartsWith('{')) return Regex.Escape(teil);
            return teil.Contains(":int", StringComparison.Ordinal) ? @"\d+" : "[^/]+";
        });
        return new Regex("^" + string.Join('/', teile) + "$", RegexOptions.IgnoreCase);
    }
}
