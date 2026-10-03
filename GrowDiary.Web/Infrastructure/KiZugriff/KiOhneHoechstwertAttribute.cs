namespace GrowDiary.Web.Infrastructure.KiZugriff;

/// <summary>
/// Fork AI (A-003, 03.10.2026): Diese Aktion zählt nicht ins Stundenfenster der
/// Schaltbefehle und scheitert nie an ihm.
/// </summary>
/// <remarks>
/// Für Sicherheitsbefehle, allen voran den Pumpen-Stopp
/// (<c>POST /api/dosing/pumps/{id}/stop</c>): ein Assistent, der sein
/// Stundenkontingent verbraucht hat, muss eine laufende Pumpe trotzdem anhalten
/// können. Die Stufe (<see cref="KiStufeAttribute"/>) gilt weiter — dieses
/// Attribut nimmt nur den Höchstwert weg, nicht die Freigabe.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class KiOhneHoechstwertAttribute(string grund) : Attribute
{
    public string Grund { get; } = grund;
}
