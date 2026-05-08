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
///   • "json" — plain JSON POST, optional `Authorization: Bearer &lt;ApiKey&gt;`.
///   • "loki" — Loki push v1; works with self-hosted Loki. Basic auth and the
///              multi-tenant `X-Scope-OrgID` header are sent when configured.
/// All exceptions are caught — telemetry can never break a fixer run.
/// </summary>
internal static class Telemetry
{
    public sealed class Event
    {
        // ── Identity ──────────────────────────────────────────────────────
        public string Variant            { get; set; } = "";   // "exe" | "plugin"
        public string Version            { get; set; } = "";
        public string MachineKey         { get; set; } = "";   // anonymousId
        public string RunId              { get; set; } = Guid.NewGuid().ToString("N");
        public string TimestampUtc       { get; set; } = DateTime.UtcNow.ToString("O");

        // ── Environment ───────────────────────────────────────────────────
        public string Os                 { get; set; } = "";   // "Windows" | "Unix" | …
        public string OsVersion          { get; set; } = "";   // detailed
        public string OsDescription      { get; set; } = "";   // RuntimeInformation.OSDescription
        public string ProcessArch        { get; set; } = "";   // X64, ARM64, …
        public string OsArch             { get; set; } = "";
        public string RuntimeVersion     { get; set; } = "";   // .NET 10.0.x / 6.0.x
        public int    CpuCount           { get; set; }
        public string CultureName        { get; set; } = "";
        public bool   Is64BitProcess     { get; set; }

        // ── Game / Loader (plugin only — empty for exe) ───────────────────
        public string GameName           { get; set; } = "";
        public string GameDeveloper      { get; set; } = "";
        public string GameVersion        { get; set; } = "";
        public string UnityVersion       { get; set; } = "";
        public string MelonLoaderVersion { get; set; } = "";

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
        public string Name         { get; set; } = "";
        public int    TypesRemoved { get; set; }
        public bool   Rewritten    { get; set; }
        public bool   Conservative { get; set; }
    }

    /// <summary>Fill the platform/runtime fields that every variant can collect.</summary>
    public static void PopulateEnvironment(Event evt)
    {
        try { evt.Os                 = Environment.OSVersion.Platform.ToString(); } catch { }
        try { evt.OsVersion          = Environment.OSVersion.VersionString; }       catch { }
        try { evt.OsDescription      = RuntimeInformation.OSDescription; }          catch { }
        try { evt.ProcessArch        = RuntimeInformation.ProcessArchitecture.ToString(); } catch { }
        try { evt.OsArch             = RuntimeInformation.OSArchitecture.ToString(); }      catch { }
        try { evt.RuntimeVersion     = RuntimeInformation.FrameworkDescription; }   catch { }
        try { evt.CpuCount           = Environment.ProcessorCount; }                catch { }
        try { evt.CultureName        = CultureInfo.CurrentCulture.Name; }           catch { }
        try { evt.Is64BitProcess     = Environment.Is64BitProcess; }                catch { }
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
                evt.GameName     = ReadStaticString(handler, "GameName");
                evt.GameDeveloper= ReadStaticString(handler, "GameDeveloper");
                evt.GameVersion  = ReadStaticString(handler, "GameVersion");
                evt.UnityVersion = ReadStaticString(handler, "EngineVersion");
            }
            Type? buildInfo = Type.GetType("MelonLoader.BuildInfo, MelonLoader");
            if (buildInfo != null)
                evt.MelonLoaderVersion = ReadStaticString(buildInfo, "Version");
        }
        catch { /* best effort */ }
    }

    private static string ReadStaticString(Type t, string memberName)
    {
        const BindingFlags F = BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic;
        try
        {
            var p = t.GetProperty(memberName, F);
            if (p != null) return p.GetValue(null)?.ToString() ?? "";
            var f = t.GetField(memberName, F);
            if (f != null) return f.GetValue(null)?.ToString() ?? "";
        }
        catch { }
        return "";
    }

    /// <summary>Set Outcome based on the error count + scanned/modified counters.</summary>
    public static void DeriveOutcome(Event evt)
    {
        if (evt.Errors == 0)            evt.Outcome = "success";
        else if (evt.AssembliesScanned > 0 && evt.Errors < evt.AssembliesScanned) evt.Outcome = "partial";
        else                            evt.Outcome = "failed";
    }

    public static void Send(FixerConfig.TelemetrySection cfg, Event evt, Action<string>? logLine)
    {
        if (!cfg.Enabled) return;
        if (string.IsNullOrWhiteSpace(cfg.Endpoint))
        {
            logLine?.Invoke("[telemetry] enabled but no endpoint configured — skipping.");
            return;
        }

        // Drop the heavy per-assembly list when the user opted out of it.
        if (!cfg.IncludeAssemblyList) evt.Assemblies = null;

        // Strip the heavy machine fields when machine info opt-out.
        if (!cfg.IncludeMachineInfo)
        {
            evt.OsVersion = ""; evt.OsDescription = ""; evt.OsArch = "";
            evt.ProcessArch = ""; evt.RuntimeVersion = ""; evt.CpuCount = 0;
            evt.CultureName = ""; evt.Is64BitProcess = false;
        }

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(1, cfg.TimeoutSeconds)) };
            HttpRequestMessage req;

            if (string.Equals(cfg.Format, "loki", StringComparison.OrdinalIgnoreCase))
            {
                req = BuildLokiRequest(cfg, evt);
            }
            else
            {
                req = BuildJsonRequest(cfg, evt);
            }

            using var resp = http.Send(req);
            int status = (int)resp.StatusCode;
            if (status >= 200 && status < 300)
                logLine?.Invoke($"[telemetry] sent ({cfg.Format}, HTTP {status}).");
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

    private static HttpRequestMessage BuildJsonRequest(FixerConfig.TelemetrySection cfg, Event evt)
    {
        string body = JsonSerializer.Serialize(evt, JsonOpts);
        var req = new HttpRequestMessage(HttpMethod.Post, cfg.Endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrEmpty(cfg.ApiKey))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.ApiKey);
        return req;
    }

    private static HttpRequestMessage BuildLokiRequest(FixerConfig.TelemetrySection cfg, Event evt)
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
        var req = new HttpRequestMessage(HttpMethod.Post, cfg.Endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrEmpty(cfg.Username) || !string.IsNullOrEmpty(cfg.ApiKey))
        {
            string raw  = $"{cfg.Username}:{cfg.ApiKey}";
            string b64  = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", b64);
        }
        if (!string.IsNullOrEmpty(cfg.TenantId))
            req.Headers.TryAddWithoutValidation("X-Scope-OrgID", cfg.TenantId);
        return req;
    }
}
