using Microsoft.AspNetCore.Http;

namespace GrowDiary.Web.Api.Contracts;

/// <summary>
/// Einheitliches Error-Format für alle API-Endpoints.
/// Bewusst schlank - kein RFC 7807 / ProblemDetails.
/// </summary>
public sealed record ApiError(
    string Code,
    string Message,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null,
    int? Status = null,
    string? TraceId = null,
    string SchemaVersion = ApiErrorFactory.SchemaVersion
);

public static class ApiErrorFactory
{
    public const string SchemaVersion = "grow-os.api-error.v1";

    public static ApiError Create(
        string code,
        string message,
        int status,
        IReadOnlyDictionary<string, string[]>? fieldErrors = null,
        string? traceId = null)
        => new(
            Code: NormalizeCode(code),
            Message: string.IsNullOrWhiteSpace(message) ? "API-Fehler." : message.Trim(),
            FieldErrors: NormalizeFieldErrors(fieldErrors),
            Status: status,
            TraceId: string.IsNullOrWhiteSpace(traceId) ? null : traceId.Trim());

    public static ApiError Validation(
        string message = "Eingaben konnten nicht validiert werden.",
        IReadOnlyDictionary<string, string[]>? fieldErrors = null,
        string? traceId = null)
        => Create("validation_failed", message, StatusCodes.Status400BadRequest, fieldErrors, traceId);

    public static ApiError NotFound(string code, string message, string? traceId = null)
        => Create(code, message, StatusCodes.Status404NotFound, traceId: traceId);

    public static ApiError Conflict(string code, string message, string? traceId = null)
        => Create(code, message, StatusCodes.Status409Conflict, traceId: traceId);

    public static ApiError Forbidden(string code, string message, string? traceId = null)
        => Create(code, message, StatusCodes.Status403Forbidden, traceId: traceId);

    public static ApiError BadRequest(string code, string message, string? traceId = null)
        => Create(code, message, StatusCodes.Status400BadRequest, traceId: traceId);

    public static ApiError ServerError(string code, string message, string? traceId = null)
        => Create(code, message, StatusCodes.Status500InternalServerError, traceId: traceId);

    private static string NormalizeCode(string code)
        => string.IsNullOrWhiteSpace(code) ? "api_error" : code.Trim().ToLowerInvariant();

    private static IReadOnlyDictionary<string, string[]>? NormalizeFieldErrors(IReadOnlyDictionary<string, string[]>? fieldErrors)
    {
        if (fieldErrors is null || fieldErrors.Count == 0)
        {
            return null;
        }

        return fieldErrors
            .Where(entry => entry.Value.Length > 0)
            .ToDictionary(
                entry => string.IsNullOrWhiteSpace(entry.Key) ? "$" : entry.Key.Trim(),
                entry => entry.Value
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .DefaultIfEmpty("Ungültiger Wert.")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }
}
