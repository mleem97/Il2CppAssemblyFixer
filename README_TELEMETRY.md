# Il2CppAssemblyFixer

## Telemetry notice

To help me develop and improve this tool I have integrated a small telemetry
service that reports **which errors are occurring when starting** and a few
non-identifying counters (number of assemblies scanned, number of duplicate
types removed, run duration). This makes it possible to spot regressions
across game updates without having to ask every user for a log.

The endpoint is a self-hosted Grafana Loki instance with rate limiting and
**no credentials shipped in this ZIP** — the reverse proxy in front of Loki
accepts unauthenticated `POST /loki/api/v1/push`, so there are no API keys to
leak. The shipped `fixer_config.json` contains only the URL.

**What is sent**

* Tool variant (`exe` / `plugin`) and version
* Operating system
* Counters: assemblies scanned / modified, duplicate types removed, errors
* Run duration in milliseconds
* A randomly-generated, stable `anonymousId` (so repeated runs from the same
  install can be grouped — it is **not** linked to your name, IP, account or
  any other personal info)

**What is NOT sent**

* No file paths, no usernames, no hostnames
* No assembly contents, no game data, no save files
* No data at all if your network connection is offline (telemetry fails
  silently and never blocks the fixer)

## How to opt out

If you do not wish to participate, open `fixer_config.json` (it sits next to
MelonLoader's `Latest.log`, in `<GameRoot>/MelonLoader/`) and change:

```json
"telemetry": {
  "enabled": false
}
```

That is enough — you can leave every other field as-is. The next run will not
contact any server.

You can also delete the entire `fixer_config.json` and replace it with:

```json
{ "telemetry": { "enabled": false } }
```

The fixer will fill in the remaining fields with safe defaults on the next run.

## Logging

Independently of telemetry, every run also writes a structured log to
`<GameRoot>/MelonLoader/fixer.log` (alongside MelonLoader's own `Latest.log`).
This file is **local only** — it is never uploaded.

You can disable file logging via:

```json
"logging": { "writeLogFile": false }
```

## Issues

If you run into anything, please open a ticket:
<https://github.com/mleem97/Il2CppAssemblyFixer/issues>
