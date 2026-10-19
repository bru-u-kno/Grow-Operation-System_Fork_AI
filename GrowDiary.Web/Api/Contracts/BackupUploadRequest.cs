using Microsoft.AspNetCore.Http;

namespace GrowDiary.Web.Api.Contracts;

/// <summary>Die hochgeladene Sicherungsdatei (Zip) beim Zurückspielen auf einer anderen Installation.</summary>
public sealed class BackupUploadRequest
{
    public IFormFile? File { get; set; }
}
