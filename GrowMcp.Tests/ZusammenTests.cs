using System.Text.Json;
using GrowMcp.Tools;

namespace GrowMcp.Tests;

/// <summary>
/// Mehrere Antworten von Cultivate Control zu einer machen.
/// </summary>
/// <remarks>
/// Sieben der Werkzeuge holen zwei oder drei Dinge und geben sie gemeinsam
/// zurück — Pumpen mit ihrem Protokoll, Lichtplan mit den beobachteten
/// Schaltzeiten. Zusammengeklebt wird der rohe Text, nicht neu geschrieben; also
/// muss geprüft sein, dass am Ende gültiges JSON steht.
/// </remarks>
public sealed class ZusammenTests
{
    [Fact]
    public void TwoAnswersBecomeOneReadableObject()
    {
        var zusammen = GrowTools.Zusammen(
            ("pumpen", """[{"id":1,"name":"pH Minus"}]"""),
            ("protokoll", """[{"id":9,"ml":1.5}]"""));

        using var json = JsonDocument.Parse(zusammen);
        Assert.Equal(1, json.RootElement.GetProperty("pumpen")[0].GetProperty("id").GetInt32());
        Assert.Equal(9, json.RootElement.GetProperty("protokoll")[0].GetProperty("id").GetInt32());
    }

    [Fact]
    public void AMissingPartStaysNullWithoutBreakingTheRest()
    {
        // Kein Pheno Hunt zu diesem Grow: der Teil ist null, die Pflanzenliste
        // daneben bleibt lesbar. Ohne das riss ein fehlender Teil das ganze
        // Ergebnis mit.
        var zusammen = GrowTools.Zusammen(
            ("pflanzen", """[{"id":4}]"""),
            ("phenoHunt", "null"));

        using var json = JsonDocument.Parse(zusammen);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("phenoHunt").ValueKind);
        Assert.Equal(1, json.RootElement.GetProperty("pflanzen").GetArrayLength());
    }

    [Fact]
    public void ThreePartsAlsoHoldTogether()
    {
        var zusammen = GrowTools.Zusammen(
            ("geraete", "[]"), ("wartung", "[]"), ("kalibrierung", "[]"));

        using var json = JsonDocument.Parse(zusammen);
        Assert.Equal(3, json.RootElement.EnumerateObject().Count());
    }

    [Fact]
    public void EmptyArraysStayEmptyArraysNotNull()
    {
        // „Es gibt keine laufenden Ablaeufe" ist eine Antwort. Wuerde daraus null,
        // liesse sich das nicht mehr von „nicht abgefragt" unterscheiden.
        using var json = JsonDocument.Parse(GrowTools.Zusammen(("plan", "[]")));

        Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("plan").ValueKind);
        Assert.Equal(0, json.RootElement.GetProperty("plan").GetArrayLength());
    }
}
