namespace Il2CppAssemblyFixer.Shared;

/// <summary>
/// Build-time-substituted defaults for the telemetry endpoint.
///
/// The committed values are placeholders. The release workflow rewrites this
/// file before <c>dotnet publish</c>, replacing each <c>__TELEMETRY_*__</c>
/// token with the matching GitHub Actions secret. Locally compiled bits keep
/// the placeholders, are detected as such by <see cref="Resolve"/>, and the
/// telemetry sender no-ops.
///
/// SECURITY NOTE: these constants end up as plain string literals in the
/// shipped assembly. .NET assemblies decompile trivially (dnSpy / ILSpy),
/// so anyone with a copy of the binary can read the values. They are NOT a
/// security control — the real abuse mitigation lives at the ingest side
/// (rate limiting + CrowdSec in the reverse proxy).
/// </summary>
internal static class TelemetryDefaults
{
    public const string Endpoint  = "__TELEMETRY_ENDPOINT__";
    public const string Username  = "__TELEMETRY_USERNAME__";
    public const string AuthToken = "__TELEMETRY_AUTH_TOKEN__";
    public const string TenantId  = "__TELEMETRY_TENANT_ID__";

    /// <summary>
    /// Returns an empty string when <paramref name="raw"/> is still an
    /// unsubstituted <c>__TELEMETRY_*__</c> placeholder; otherwise returns it
    /// unchanged. Lets the runtime treat local dev builds the same as a config
    /// with no value set.
    /// </summary>
    public static string Resolve(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        if (raw.Length > 4 && raw.StartsWith("__TELEMETRY_") && raw.EndsWith("__"))
            return string.Empty;
        return raw;
    }
}
