namespace GrowDiary.Web.Infrastructure;

/// <summary>
/// Fork AI: Stellen, an denen der Fork Teile des Originals bewusst abschaltet —
/// an einem Ort, damit ein Rückweg eine Zeile ist.
/// </summary>
public static class ForkAiSchalter
{
    /// <summary>
    /// Crop Steering des Entwicklers (Nachtabsenkung und Kühler-Regler im Add-on).
    /// </summary>
    /// <remarks>
    /// Seit forkai.136 aus: Die Wassertemperatur führt der Grow-Plan, geregelt wird
    /// über Steuerung → Chiller (Steckdose oder Sollwert-Gerät als Rolle). Zwei
    /// Regler für denselben Kühler und zwei Stellen für dasselbe Ziel wären genau
    /// das, was der Fork vermeiden will. Die Dateien bleiben stehen, damit der
    /// Abstand zum Original klein bleibt.
    /// </remarks>
    public const bool CropSteeringAktiv = false;
}
