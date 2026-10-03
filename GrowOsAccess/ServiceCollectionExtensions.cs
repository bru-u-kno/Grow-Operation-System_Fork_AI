using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GrowOsAccess;

/// <summary>Die Anbindung an Grow OS in einem Rutsch registrieren.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Supervisor-Draht, Suche und Leser eintragen.</summary>
    /// <param name="adresse">
    /// Eine von Hand eingetragene Grow-OS-Adresse; leer lassen, wenn gesucht werden soll.
    /// </param>
    /// <param name="eigenerSlug">
    /// Der Slug dieses Add-ons aus seiner <c>config.yaml</c>, derzeit
    /// <c>grow_mcp</c>. Daraus wird der Name von Grow OS abgeleitet.
    /// </param>
    public static IServiceCollection AddGrowOsAccess(
        this IServiceCollection services, string? adresse, string eigenerSlug)
    {
        services.AddSingleton(new GrowOsOptions
        {
            Adresse = adresse ?? string.Empty,
            EigenerSlug = eigenerSlug,
        });
        services.AddHttpClient(SupervisorClient.HttpClientName, klient =>
        {
            klient.BaseAddress = new Uri("http://supervisor/");
            klient.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddSingleton<SupervisorClient>();

        // Die Suche merkt sich die gefundene Adresse, muss also ein Singleton sein
        // — ein typisierter Klient waere transient und der Merker damit wertlos.
        services.AddHttpClient(GrowOsDiscovery.HttpClientName,
            klient => klient.Timeout = TimeSpan.FromSeconds(10));
        services.AddSingleton<GrowOsDiscovery>();

        // Ohne eigene Quelle geht kein Schlüssel mit — nur lesen, wie bisher.
        // Der Grow MCP trägt vorher seine eigene ein (Schlüssel aus der
        // laufenden HTTP-Anfrage); TryAdd lässt sie dann stehen.
        services.TryAddSingleton<IForkSchluesselQuelle, KeinForkSchluessel>();

        services.AddHttpClient<GrowOsReader>(klient => klient.Timeout = TimeSpan.FromSeconds(30));
        return services;
    }
}
