using GrowDiary.Web.Models;
using Xunit;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI: Tests, die eine Anlage mit den alten (handgebauten) Kennungen nachstellen, laufen im
/// <see cref="KennungsRaum.Bestand"/>. Der Raum ist ein Wert des ganzen Prozesses; deshalb laufen alle Klassen dieser
/// Sammlung nacheinander und niemals neben anderen Tests, und der Raum wird danach zurückgesetzt.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BestandRaumSammlung
{
    public const string Name = "Kennungsraum Bestand";
}

/// <summary>Stellt für die Lebensdauer des Objekts den Bestand-Raum ein.</summary>
public sealed class BestandRaumFuerTests : IDisposable
{
    public BestandRaumFuerTests() => Kennungen.Festlegen(KennungsRaum.Bestand);

    public void Dispose() => Kennungen.Festlegen(KennungsRaum.Neu);
}
