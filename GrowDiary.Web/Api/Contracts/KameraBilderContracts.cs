namespace GrowDiary.Web.Api.Contracts;

// Fork AI (Paket C2, 18.10.2026): Tagesbilder der Zelt-Kamera und Kamerafoto speichern.

/// <summary>Die Tagesbilder eines Zelts, neueste zuerst, und seine Kameras (die erste macht die Tagesbilder).</summary>
public sealed record TagesbilderDto(int ZeltId, IReadOnlyList<string> Kameras, IReadOnlyList<TagesbildDto> Bilder);

/// <summary>Ein Tagesbild: Datum (yyyy-MM-dd), Größe in Bytes und der Weg zur Datei.</summary>
public sealed record TagesbildDto(string Datum, long Bytes, string Pfad);

/// <summary>Das aktuelle Kamerabild als Grow-Foto speichern.</summary>
/// <param name="Kamera">Entity der Kamera; leer = die erste des Zelts. Muss zum Zelt gehören.</param>
/// <param name="Motiv">Overview, Canopy, Leaf, Root, Training, Flower, Problem, Comparison oder Other; leer = Overview.</param>
/// <param name="Bildunterschrift">Höchstens 500 Zeichen; leer = „Kamerabild &lt;Entity&gt;".</param>
public sealed record KamerafotoRequest(string? Kamera, string? Motiv, string? Bildunterschrift);
