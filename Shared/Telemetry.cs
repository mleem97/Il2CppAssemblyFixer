using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Il2CppAssemblyFixer.Shared;

/// <summary>
/// Telemetry sender (opt-out via fixer_config.json). Posts a single event per
/// run to a user-configured HTTP endpoint. Supports two formats:
///   • "json" — plain JSON POST, optional bearer authorization.
///   • "loki" — Loki push v1; works with self-hosted Loki. Basic auth and the
///              multi-tenant `X-Scope-OrgID` header are sent when configured.
/// All exceptions are caught — telemetry can never break a fixer run.
/// </summary>
internal static class Telemetry
{
    public sealed class Event
    {
        // ── Identity ──────────────────────────────────────────────────────
        public string Variant            { get; set; } = string.Empty;   // "exe" | "plugin"
        public string Version            { get; set; } = string.Empty;
        public string MachineKey         { get; set; } = string.Empty;   // anonymousId
        public string RunId              { get; set; } = Guid.NewGuid().ToString("N");
        public string TimestampUtc       { get; set; } = DateTime.UtcNow.ToString("O");

        // ── Environment ───────────────────────────────────────────────────
        public string Os                 { get; set; } = string.Empty;   // "Windows" | "Unix" | …
        public string OsVersion          { get; set; } = string.Empty;   // detailed
        public string OsDescription      { get; set; } = string.Empty;   // RuntimeInformation.OSDescription
        public string ProcessArch        { get; set; } = string.Empty;   // X64, ARM64, …
        public string OsArch             { get; set; } = string.Empty;
        public string RuntimeVersion     { get; set; } = string.Empty;   // .NET 10.0.x / 6.0.x
        public int    CpuCount           { get; set; }
        public string CultureName        { get; set; } = string.Empty;
        public bool   Is64BitProcess     { get; set; }

        // ── Game / Loader (plugin only — empty for exe) ───────────────────
        public string GameName           { get; set; } = string.Empty;
        public string GameDeveloper      { get; set; } = string.Empty;
        public string GameVersion        { get; set; } = string.Empty;
        public string UnityVersion       { get; set; } = string.Empty;
        public string MelonLoaderVersion { get; set; } = string.Empty;

        // ── Run results ───────────────────────────────────────────────────
        public string Outcome            { get; set; } = "success"; // success | partial | failed
        public int    AssembliesScanned  { get; set; }
        public int    AssembliesModified { get; set; }
        public int    AssembliesSkipped  { get; set; }   // unmodified or cache-hit
        public int    TypesRemoved       { get; set; }
        public int    RewritesPerformed  { get; set; }
        public int    Errors             { get; set; }
        public long   DurationMs         { get; set; }

        // Optional detail (gated by config.IncludeAssemblyList)
        public List<AssemblyDetail>? Assemblies { get; set; }
    }

    public sealed class AssemblyDetail
    {
        public string Name         { get; set; } = string.Empty;
        public int    TypesRemoved { get; set; }
        public bool   Rewritten    { get; set; }
        public bool   Conservative { get; set; }
    }

    /// <summary>Fill the platform/runtime fields that every variant can collect.</summary>
    public static void PopulateEnvironment(Event evt)
    {
        TryPopulate(() => evt.Os             = Environment.OSVersion.Platform.ToString());
        TryPopulate(() => evt.OsVersion      = Environment.OSVersion.VersionString);
        TryPopulate(() => evt.OsDescription  = RuntimeInformation.OSDescription);
        TryPopulate(() => evt.ProcessArch    = RuntimeInformation.ProcessArchitecture.ToString());
        TryPopulate(() => evt.OsArch         = RuntimeInformation.OSArchitecture.ToString());
        TryPopulate(() => evt.RuntimeVersion = RuntimeInformation.FrameworkDescription);
        TryPopulate(() => evt.CpuCount       = Environment.ProcessorCount);
        TryPopulate(() => evt.CultureName    = CultureInfo.CurrentCulture.Name);
        TryPopulate(() => evt.Is64BitProcess = Environment.Is64BitProcess);
    }

    private static void TryPopulate(Action action)
    {
        try
        {
            action();
        }
        catch (Exception)
        {
            // Best-effort environment collection: missing fields are acceptable
            // because telemetry must never affect the fixer run.
        }
    }

    /// <summary>
    /// Best-effort reflective lookup of MelonLoader's UnityInformationHandler.
    /// Returns empty strings when MelonLoader isn't loaded (i.e. the EXE).
    /// </summary>
    public static void PopulateMelonInfo(Event evt)
    {
        try
        {
            Type? handler = Type.GetType("MelonLoader.InternalUtils.UnityInformationHandler, MelonLoader");
            if (handler != null)
            {
                evt.GameName      = ReadStaticString(handler, "GameName");
                evt.GameDeveloper = ReadStaticString(handler, "GameDeveloper");
                evt.GameVersion   = ReadStaticString(handler, "GameVersion");
                evt.UnityVersion  = ReadStaticString(handler, "EngineVersion");
            }
            Type? buildInfo = Type.GetType("MelonLoader.BuildInfo, MelonLoader");
            if (buildInfo != null)
                evt.MelonLoaderVersion = ReadStaticString(buildInfo, "Version");
        }
        catch (Exception)
        {
            // Reflection targets differ between MelonLoader versions. Ignore and
            // leave the optional game/loader fields empty.
        }
    }

    private static string ReadStaticString(Type t, string memberName)
    {
        const BindingFlags F = BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic;
        try
        {
            var p = t.GetProperty(memberName, F);
            if (p != null) return p.GetValue(null)?.ToString() ?? string.Empty;
            var f = t.GetField(memberName, F);
            if (f != null) return f.GetValue(null)?.ToString() ?? string.Empty;
        }
        catch (Exception)
        {
            // Optional reflection field lookup failed; return an empty value.
        }
        return string.Empty;
    }

    /// <summary>Set Outcome based on the error count + scanned/modified counters.</summary>
    public static void DeriveOutcome(Event evt)
    {
        if (evt.Errors == 0)            evt.Outcome = "success";
        else if (evt.AssembliesScanned > 0 && evt.Errors < evt.AssembliesScanned) evt.Outcome = "partial";
        else                            evt.Outcome = "failed";
    }

    /// <summary>
    /// Effective telemetry settings after merging user-config with the
    /// build-time defaults from <see cref="TelemetryDefaults"/>. User-config
    /// always wins — empty fields fall back to the embedded defaults.
    /// </summary>
    private sealed class Resolved
    {
        public string Endpoint  { get; }
        public string Username  { get; }
        public string AuthToken { get; }
        public string TenantId  { get; }
        public string Format    { get; }
        public int    TimeoutSeconds { get; }

        public Resolved(FixerConfig.TelemetrySection cfg)
        {
            Endpoint  = !string.IsNullOrWhiteSpace(cfg.Endpoint)  ? cfg.Endpoint  : TelemetryDefaults.Resolve(TelemetryDefaults.Endpoint);
            Username  = !string.IsNullOrEmpty(cfg.Username)       ? cfg.Username  : TelemetryDefaults.Resolve(TelemetryDefaults.Username);
            AuthToken = !string.IsNullOrEmpty(cfg.AuthToken)      ? cfg.AuthToken : TelemetryDefaults.Resolve(TelemetryDefaults.AuthToken);
            TenantId  = !string.IsNullOrEmpty(cfg.TenantId)       ? cfg.TenantId  : TelemetryDefaults.Resolve(TelemetryDefaults.TenantId);
            Format    = string.IsNullOrEmpty(cfg.Format) ? "loki" : cfg.Format;
            TimeoutSeconds = Math.Max(1, cfg.TimeoutSeconds);
        }
    }

    public static void Send(FixerConfig.TelemetrySection cfg, Event evt, Action<string>? logLine)
    {
        if (!cfg.Enabled) return;

        var r = new Resolved(cfg);
        if (string.IsNullOrWhiteSpace(r.Endpoint))
        {
            logLine?.Invoke("[telemetry] enabled but no endpoint configured — skipping.");
            return;
        }

        // Drop the heavy per-assembly list when the user opted out of it.
        if (!cfg.IncludeAssemblyList) evt.Assemblies = null;

        // Strip the heavy machine fields when machine info opt-out.
        if (!cfg.IncludeMachineInfo)
        {
            evt.OsVersion = string.Empty; evt.OsDescription = string.Empty; evt.OsArch = string.Empty;
            evt.ProcessArch = string.Empty; evt.RuntimeVersion = string.Empty; evt.CpuCount = 0;
            evt.CultureName = string.Empty; evt.Is64BitProcess = false;
        }

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(r.TimeoutSeconds) };
            using HttpRequestMessage req = string.Equals(r.Format, "loki", StringComparison.OrdinalIgnoreCase)
                ? BuildLokiRequest(r, evt)
                : BuildJsonRequest(r, evt);

            using var resp = http.Send(req);
            int status = (int)resp.StatusCode;
            if (status >= 200 && status < 300)
                logLine?.Invoke($"[telemetry] sent ({r.Format}, HTTP {status}).");
            else
                logLine?.Invoke($"[telemetry] endpoint replied HTTP {status}.");
        }
        catch (Exception ex)
        {
            logLine?.Invoke($"[telemetry] send failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented        = false,
    };

    private static HttpRequestMessage BuildJsonRequest(Resolved r, Event evt)
    {
        string body = JsonSerializer.Serialize(evt, JsonOpts);
        var req = new HttpRequestMessage(HttpMethod.Post, r.Endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrEmpty(r.AuthToken))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", r.AuthToken);
        return req;
    }

    private static HttpRequestMessage BuildLokiRequest(Resolved r, Event evt)
    {
        // Loki push v1:  { "streams": [ { "stream": {labels}, "values": [["<unixns>", "<line>"]] } ] }
        long unixNs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000L;
        string line = JsonSerializer.Serialize(evt, JsonOpts);

        var labels = new Dictionary<string, string>
        {
            ["app"]     = "il2cpp-assembly-fixer",
            ["variant"] = evt.Variant,
            ["version"] = evt.Version,
            ["os"]      = evt.Os,
            ["outcome"] = evt.Outcome,
        };

        var payload = new
        {
            streams = new[]
            {
                new
                {
                    stream = labels,
                    values = new[] { new[] { unixNs.ToString(), line } },
                },
            },
        };

        string body = JsonSerializer.Serialize(payload, JsonOpts);
        var req = new HttpRequestMessage(HttpMethod.Post, r.Endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrEmpty(r.Username) || !string.IsNullOrEmpty(r.AuthToken))
        {
            string raw = $"{r.Username}:{r.AuthToken}";
            string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", b64);
        }
        if (!string.IsNullOrEmpty(r.TenantId))
            req.Headers.TryAddWithoutValidation("X-Scope-OrgID", r.TenantId);
        return req;
    }
}
