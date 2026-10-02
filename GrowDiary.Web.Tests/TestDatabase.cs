using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests;

internal static class TestDatabase
{
    public static void Initialize(AppPaths paths)
    {
        // Die Kosten- und Geraete-Tabellen legen ihre Repositories selbst an,
        // je Datenbankdatei (EigenesSchema). Bis zum 02.10.2026 galt der
        // Merker je Prozess, und hier stand eine Umgehung per Reflexion.
        new DatabaseInitializer(paths, NullLogger<DatabaseInitializer>.Instance).Initialize();
    }

    public static Tent InitializeWithDefaultTent(AppPaths paths, string name = "Testzelt", TentType tentType = TentType.MultiPurpose)
    {
        Initialize(paths);
        return EnsureDefaultTent(paths, name, tentType);
    }

    public static Tent EnsureDefaultTent(AppPaths paths, string name = "Testzelt", TentType tentType = TentType.MultiPurpose)
    {
        var repository = new GrowRepository(paths);
        var existing = repository.GetTents().FirstOrDefault();
        if (existing is not null)
        {
            return existing;
        }

        return repository.CreateTent(new Tent
        {
            Name = name,
            Kind = "Grow Tent",
            TentType = tentType,
            AccentColor = "#69b578"
        });
    }
}
