using Microsoft.AspNetCore.DataProtection;

namespace Namorix.Scout.Services;

public sealed class ScoutSecretProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("Scout.CameraCredentials");

    public string? Protect(string? value) =>
        string.IsNullOrEmpty(value) ? null : _protector.Protect(value);

    public string? Unprotect(string? value) =>
        string.IsNullOrEmpty(value) ? null : _protector.Unprotect(value);
}
