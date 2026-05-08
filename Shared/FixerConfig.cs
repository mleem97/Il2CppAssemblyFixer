using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Il2CppAssemblyFixer.Shared;

/// <summary>
/// User-editable runtime config, located at &lt;MelonLoaderDir&gt;/fixer_config.json.
/// Created with safe defaults on first run. Telemetry is opt-in.
/// </summary>
internal sealed class FixerConfig
{
    public const string FileName = "fixer_config.json";

    public LoggingSection  Logging   { get; set; } = new();
    public TelemetrySection Telemetry { get; set; } = new();

    internal sealed class LoggingSection
    {
        public bool   WriteLogFile { get; set; } = true;
        public string LogFileName  { get; set; } = "fixer.log";
        // Currently informational only — file logger writes everything; level filter
        // can be added later without changing the on-disk schema.
        public string MinimumLevel { get; set; } = "Debug";
    }

    internal sealed class TelemetrySection
    {
        // Telemetry is opt-out: enabled by default so the maintainer receives anonymous
        // run summaries (errors, removed-type counts, durations). To opt out, set this
        // to false in fixer_config.json — see README_TELEMETRY.md in the release.
        public bool   Enabled              { get; set; } = true;

        // Free-form HTTP endpoint. Populated from build-time secret in shipped configs.
        // Self-hosted Loki examples:
        //   "https://loki.example.com/loki/api/v1/push"            (Format=loki)
        //   "https://ingest.example.com/fixer"                     (Format=json)
        public string Endpoint             { get; set; } = "";

        // "json" → POST a single JSON event to Endpoint with optional Bearer.
        // "loki" → wrap event into Loki push format (works with self-hosted Loki
        //          and any compatible ingester).
        public string Format               { get; set; } = "loki";

        // Optional HTTP Basic auth credentials. Useful when Loki sits behind
        // nginx/traefik with basic auth, or when using a Grafana-Cloud-style
        // user:key pair. Leave empty for unauthenticated self-hosted ingest.
        public string Username             { get; set; } = "";

        // Loki/Basic auth: password. JSON format: sent as `Authorization: Bearer
        // <ApiKey>` instead, when non-empty.
        public string ApiKey               { get; set; } = "";

        // Self-hosted Loki tenant id (`X-Scope-OrgID` header). Required only when
        // the target Loki runs in multi-tenant mode. Leave empty otherwise.
        public string TenantId             { get; set; } = "";

        // Stable per-installation UUID. Auto-generated on first run.
        public string AnonymousId          { get; set; } = "";

        public bool   IncludeAssemblyList  { get; set; } = true;
        public bool   IncludeMachineInfo   { get; set; } = true;
        public int    TimeoutSeconds       { get; set; } = 5;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented              = true,
        PropertyNamingPolicy       = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition     = JsonIgnoreCondition.Never,
        ReadCommentHandling        = JsonCommentHandling.Skip,
        AllowTrailingCommas        = true,
    };

    /// <summary>Load (or create with defaults) the config file in the given dir.</summary>
    public static FixerConfig LoadOrCreate(string melonLoaderDir, Action<string>? warn = null)
    {
        string path = Path.Combine(melonLoaderDir, FileName);
        FixerConfig cfg;

        if (File.Exists(path))
        {
            try
            {
                string json = File.ReadAllText(path);
                cfg = JsonSerializer.Deserialize<FixerConfig>(json, JsonOpts) ?? new FixerConfig();
            }
            catch (Exception ex)
            {
                warn?.Invoke($"Could not parse {FileName}: {ex.Message}. Using defaults.");
                cfg = new FixerConfig();
            }
        }
        else
        {
            cfg = new FixerConfig();
        }

        bool dirty = false;
        if (string.IsNullOrWhiteSpace(cfg.Telemetry.AnonymousId))
        {
            cfg.Telemetry.AnonymousId = Guid.NewGuid().ToString("N");
            dirty = true;
        }

        if (!File.Exists(path) || dirty)
        {
            try { File.WriteAllText(path, JsonSerializer.Serialize(cfg, JsonOpts)); }
            catch (Exception ex) { warn?.Invoke($"Could not write {FileName}: {ex.Message}"); }
        }

        return cfg;
    }
}
