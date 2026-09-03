# STANDARDS.md — Activity, Telemetry & Screen Contracts

_Last updated: 2026-09-10_

Three independent, versioned contracts live here:

1. **Outbound Activity Heartbeat API** — SmartCord (or any client) pushes "what I'm
   currently doing" to a backend over the internet (e.g. `api.deltavdevs.com`).
   Implementation-agnostic: a client that only knows `base_url` + `api_key` can point
   at any backend that implements this doc.
2. **Inbound Local Ingest API** — other devices on the same LAN (a Jetson Orin Nano
   running live inference, a servo tester reporting channel draw, whatever shows up on
   the bench next) push live stats *into SmartCord itself*. No internet, no cloud
   account, no discovery beyond "know SmartCord's LAN IP."
3. **Custom Pixoo Screen Format** — a JSON file format for adding a screen to the
   Pixoo64 rotation without recompiling SmartCord. Unlike Parts 1 and 2, **this one is
   implemented** (`SmartCord/Pixoo/Screens/CustomScreen.cs`) — it's documentation for
   an existing feature, not a spec waiting to be built.

They share vocabulary (`target`, `activity_type`, `stats`, `metadata`) so the mental
model transfers, but they are **separate specs with separate versioning**. A backend
implementing Part 1 doesn't need to know Part 2 exists. A device pushing to Part 2
doesn't need internet access, an API key, or any awareness that Part 1 exists either.
SmartCord may relay something it receives on Part 2 out through Part 1 (a training run
at 42% becomes a line in today's Discord presence) — that's an application choice made
in `SmartCordController`, not part of either contract.

---

## Part 1 — Outbound Activity Heartbeat API (v1)

### Route index

| Method | Path | Auth | Purpose |
|---|---|---|---|
| `GET` | `/.well-known/activity-api.json` | none | discovery — what this backend supports |
| `POST` | `/api/v1/heartbeat` | bearer | push a current-activity snapshot |
| `GET` | `/api/v1/status` | public or gated, backend's choice | most recent activity, for display |
| `GET` | `/api/v1/targets` | public or gated | discover valid `target.id` values |
| `GET` | `/api/v1/projects` | public or gated | aggregated per-project stats |

### 1.1 Discovery

`GET /.well-known/activity-api.json` — optional but recommended. Lets a client (or any
tool) probe what a backend supports instead of hardcoding assumptions.

```json
{
  "spec_version": "1.0",
  "endpoints": {
    "heartbeat": "/api/v1/heartbeat",
    "status": "/api/v1/status",
    "projects": "/api/v1/projects",
    "targets": "/api/v1/targets"
  },
  "auth": "bearer",
  "public_reads": true,
  "heartbeat_interval_seconds": 120,
  "target_types": ["project", "track"]
}
```

### 1.2 Auth

`Authorization: Bearer <api_key>` on every write (`POST`). Read endpoints (`status`,
`projects`, `targets`) can be public or gated — the backend's choice, declared via
`"public_reads"` in the discovery doc.

### 1.3 `POST /api/v1/heartbeat`

The main write. Client fires this every `heartbeat_interval_seconds` (default 120s,
backend-configurable via discovery doc) while activity is detected. Mirrors WakaTime's
model — heartbeats are cheap, deduped server-side, aggregated into sessions.

**Targets, not just projects.** "Currently working on project X" and "currently
listening to track Y" are the same shape — the client claims to be engaged with *some
entity the backend already knows about* — so heartbeat references a generic `target`
instead of a project-only field. One endpoint, not two.

**`stats` vs `metadata` — not the same field.** These look similar but mean different
things and a backend must not conflate them:

| | `stats` | `metadata` |
|---|---|---|
| Contains | numeric / progress data | descriptive tags |
| Meant for | live rendering — progress bars, counters, gauges | context, filtering, debugging |
| Shape | open — no required sub-fields, varies per `activity_type` and per user's tooling | open — free-form key/value |
| Backend validation | none beyond "is a JSON object" | none beyond "is a JSON object" |

If a number is meant to move a bar or tick a counter on a dashboard, it goes in
`stats`. If it just describes the activity, it goes in `metadata`. Backends store both
opaque and let the frontend decide what's worth rendering — don't validate either
shape server-side beyond "is an object."

**Coding example:**
```json
{
  "timestamp": "2026-09-02T14:03:11Z",
  "target": { "type": "project", "id": "proj_a1b2c3" },
  "activity_type": "coding",
  "task": "writing heartbeat client",
  "editor": "vscode",
  "language": "rust",
  "is_idle": false,
  "metadata": { "branch": "main", "file": "src/heartbeat.rs" }
}
```

**Music example** — same endpoint, different `target.type`:
```json
{
  "timestamp": "2026-09-02T14:03:11Z",
  "target": { "type": "track", "id": "song_9f8e7d" },
  "activity_type": "listening",
  "metadata": { "source": "spotify", "position_seconds": 42 }
}
```

**Training example** — `stats` carries the live numbers, `metadata` carries the tags:
```json
{
  "timestamp": "2026-09-02T14:03:11Z",
  "target": { "type": "project", "id": "proj_train01" },
  "activity_type": "training",
  "task": "training run — resnet50 v3",
  "stats": {
    "epoch": 42,
    "total_epochs": 100,
    "step": 18234,
    "loss": 0.0231,
    "elapsed_seconds": 14520,
    "eta_seconds": 20040,
    "gpu": {
      "name": "RTX 4090",
      "utilization_pct": 97,
      "vram_used_mb": 21800,
      "temp_c": 71
    }
  },
  "metadata": { "framework": "pytorch", "dataset": "custom-v2" }
}
```

**Fields:**

| field | type | required | notes |
|---|---|---|---|
| `timestamp` | ISO 8601 | yes | client-generated |
| `target` | object | yes\* | `{ type, id, label? }` — see below |
| `activity_type` | enum | yes | `coding`, `writing`, `designing`, `meeting`, `listening`, `training`, `idle`, `other` |
| `task` | string | no | free text, current focus |
| `editor` / `tool` | string | no | what app sent this |
| `language` | string | no | for coding activity |
| `is_idle` | bool | no | default `false` |
| `stats` | object | no | open-schema, numeric/progress data meant to be displayed live |
| `metadata` | object | no | open-schema, descriptive tags; backend may ignore unknown keys |

\* `target` is required for anything other than `idle`. For `is_idle: true`
heartbeats, `target` may be omitted entirely.

`target` object:

| field | type | required | notes |
|---|---|---|---|
| `type` | string | yes | backend-defined namespace: `project`, `track`, `task`, or any custom type the backend registers |
| `id` | string | yes | backend's own ID for that entity — the client never generates these, only echoes them |
| `label` | string | no | fallback display text if the backend doesn't recognize the ID (lets a client degrade gracefully against an unknown/new backend) |

**ID resolution is the backend's job, not the client's.** The client doesn't need to
know what a "project" or "track" *is* — it just needs a `type` + `id` pair, obtained by
querying `GET /api/v1/targets?type=project` once and letting the user pick, or by
config (a `.activityrc` mapping local repo → `proj_a1b2c3`). This keeps the app
decoupled from any one backend's data model.

**Response:** `202 Accepted`, empty body or `{ "accepted": true }`.

Backend must **never reject on unknown fields, an unrecognized `target.type`, or an
unrecognized `target.id`** — that's what keeps this extensible across forks without
breaking compatibility. An unrecognized `target.id` should still be accepted and stored
as an opaque reference, using `target.label` for display, rather than rejected.

### 1.4 `GET /api/v1/status`

Current/most-recent activity, for public display (a "now" widget on a site). Backend
resolves `target` into a display-friendly object.

```json
{
  "target": {
    "type": "track",
    "id": "song_9f8e7d",
    "name": "PARASOCIAL",
    "url": "https://deltavdevs.example/albums/parasocial"
  },
  "activity_type": "listening",
  "task": null,
  "stats": null,
  "last_seen": "2026-09-02T14:03:11Z",
  "is_idle": false
}
```

### 1.5 `GET /api/v1/targets?type=project`

Lets a client discover valid IDs to heartbeat against instead of hardcoding them —
the piece that makes ID-based targeting actually usable. Without it, a client setting
up against a new backend has no way to know what IDs exist.

```json
{
  "targets": [
    { "type": "project", "id": "proj_a1b2c3", "name": "activity-api-client" },
    { "type": "project", "id": "proj_d4e5f6", "name": "M.I.C.D.R.O.P." }
  ]
}
```

`type` is optional — omit to list all registered types. The backend declares which
`type` values it supports via `"target_types"` in the discovery doc.

### 1.6 `GET /api/v1/projects`

Aggregated stats per project — kept separate from generic `/targets` since project
stats have a richer shape (total time, language breakdown) than a track would.

```json
{
  "projects": [
    {
      "id": "proj_a1b2c3",
      "name": "Activity API Client",
      "total_seconds": 184320,
      "last_active": "2026-09-02T14:03:11Z",
      "languages": { "rust": 0.7, "toml": 0.3 }
    }
  ]
}
```

### 1.7 Versioning

Path-based: `/api/v1/...`. Breaking changes bump to `/api/v2/...`; backends may run
both concurrently. The discovery doc's `spec_version` lets clients detect drift.

### 1.8 Rate limiting / idempotency

- Backend should dedupe heartbeats within a small window (e.g. same `target` +
  `timestamp` within 5s) — protects against client retry storms.
- `429` with a `Retry-After` header on throttle.

### 1.9 Reference implementation checklist

- [ ] `POST /api/v1/heartbeat` — accepts unknown fields + unknown `target.id` without 400ing
- [ ] `GET /api/v1/status`
- [ ] `GET /api/v1/targets` — so clients can discover valid IDs
- [ ] `GET /api/v1/projects`
- [ ] Serves `/.well-known/activity-api.json` with `target_types` declared
- [ ] Documents `heartbeat_interval_seconds`
- [ ] Bearer token auth on writes
- [ ] `stats` and `metadata` stored opaque, validated only as "is an object"

### 1.10 Why this shape

- **Push, not pull** — client controls frequency, backend just ingests. The backend
  never needs to know how to reach an arbitrary client machine.
- **Unknown-field tolerance** — forks can extend `metadata` / `stats`, or add top-level
  fields, without breaking the base client. Same spirit as ActivityPub/JSON-LD
  extensibility, without the complexity of full JSON-LD.
- **`stats` split from `metadata`** — a renderer needs to know at a glance which keys
  are "draw this as a number/bar" versus "this is just a tag." Merging them back into
  one open bag would force every frontend to guess.
- **Discovery endpoint** — optional, but lets tooling (a CLI that auto-detects backend
  capabilities) exist later without hardcoding.
- **Generic `target` over separate `project_id`/`track_id` fields** — "what am I
  currently engaged with" is one concept whether it's a repo, a song, or a training
  run. One field + a `type` tag scales to future target types (a book, a game, a
  stream) without new heartbeat fields or client-side branching.

---

## Part 2 — Inbound Local Ingest API (v1)

**LAN-only. No internet, no cloud account, no bearer token by default.** This is the
contract for the *other direction*: a device on the same network as the machine
running SmartCord — a Jetson Orin Nano doing live inference, a servo tester on the
bench, whatever shows up next — pushes its own stats straight into SmartCord, which can
render them on its dashboard pages and rotate them into the Pixoo64 screen deck
alongside `SystemScreen` / `GpuScreen` / `ComputeScreen`.

This is deliberately **not** the same endpoint as Part 1. Part 1 is "what is a human
currently engaged with" (a target the backend already knows about). Part 2 is "here is
raw telemetry about a device" — there's no `target` to resolve, no backend-owned ID
space, and no internet round-trip. SmartCord is the server here, not the client.

### Route index

| Method | Path | Auth | Purpose |
|---|---|---|---|
| `GET` | `/local/.well-known/ingest.json` | none | discovery — what this SmartCord instance accepts |
| `POST` | `/local/v1/report` | optional `X-Device-Key` | device pushes a snapshot of itself |
| `GET` | `/local/v1/devices` | none by default | list known devices, last-seen, latest snapshot |
| `GET` | `/local/v1/devices/{device_id}` | none by default | one device's full latest snapshot |
| `DELETE` | `/local/v1/devices/{device_id}` | optional `X-Device-Key` | forget a stale/decommissioned device |

SmartCord binds this listener to the machine's LAN interface (not just loopback) on a
configurable port, default `7420`. There is no dynamic discovery in v1 — a device is
pointed at SmartCord's IP:port by static config, same as pointing a Prometheus
exporter at a scrape target.

### 2.1 Discovery

`GET /local/.well-known/ingest.json` — mirrors Part 1's discovery doc so device
firmware can auto-detect the expected report cadence and staleness window without it
being hardcoded on both ends.

```json
{
  "spec_version": "1.0",
  "report_interval_seconds": 10,
  "stale_after_seconds": 30,
  "auth": "none"
}
```

`auth` is `"none"` or `"device_key"`. When `"device_key"`, `POST /local/v1/report`
requires `X-Device-Key: <key>` matching a key configured per-device in SmartCord's
Settings → Devices page. Off by default — the LAN itself is the trust boundary — but
available for a shared or semi-trusted network (a dorm, a shared house, an IoT VLAN
with bleed-through).

### 2.2 `POST /local/v1/report`

The only write. A device fires this on its own cadence (`report_interval_seconds` from
discovery, default 10s — telemetry needs a tighter loop than a human's 120s heartbeat).

**Jetson Orin Nano — live inference:**
```json
{
  "device_id": "jetson-orin-01",
  "device_type": "jetson-orin-nano",
  "timestamp": "2026-09-02T14:03:11Z",
  "activity_type": "inference",
  "task": "yolov8 live inference — dock cam",
  "stats": {
    "fps": 28.4,
    "latency_ms": 34,
    "gpu_util_pct": 91,
    "power_draw_w": 12.3,
    "temp_c": 58
  },
  "metadata": { "model": "yolov8n", "input": "csi0" }
}
```

**Servo tester — raw channel telemetry:**
```json
{
  "device_id": "servo-bench-1",
  "device_type": "servo-tester",
  "timestamp": "2026-09-02T14:03:11Z",
  "activity_type": "telemetry",
  "stats": {
    "channel_1": { "position_us": 1500, "current_ma": 220 },
    "channel_2": { "position_us": 1420, "current_ma": 340 }
  }
}
```

**Fields:**

| field | type | required | notes |
|---|---|---|---|
| `device_id` | string | yes | stable identifier the device chooses for itself — SmartCord treats it as opaque, first report registers it |
| `device_type` | string | no | free text — drives default icon/label in the UI (`jetson-orin-nano`, `servo-tester`, …), unrecognized values just show generic |
| `timestamp` | ISO 8601 | yes | device-generated |
| `activity_type` | enum | yes | `inference`, `training`, `telemetry`, `sensor`, `other` — shares the Part 1 enum where the concept overlaps (`training` means the same thing whether it's this machine's GPU or a Jetson's) |
| `task` | string | no | free text, current focus |
| `stats` | object | no | open-schema, numeric/progress data meant to be displayed live — same rule as Part 1 §1.3 |
| `metadata` | object | no | open-schema, descriptive tags — same rule as Part 1 §1.3 |

There is no `target` field — a device report is about the device itself, not about the
device's engagement with some other entity. `stats` is fully open-schema: no required
sub-fields, arbitrarily nested (see the servo example's per-channel objects), validated
only as "is a JSON object." What's worth tracking varies per `device_type` and per
whatever tooling put the device together — SmartCord stores it opaque and a dashboard
page or Pixoo screen decides what to render.

**Response:** `202 Accepted`, empty body or `{ "accepted": true }`. `401` only if
`device_key` auth is enabled and the header is missing/wrong. Unknown fields, unknown
`device_type`, unknown `activity_type` values are **never** rejected — same
extensibility rule as Part 1.

### 2.3 `GET /local/v1/devices`

Every device SmartCord has seen, for the Devices settings page and the Pixoo screen
picker.

```json
{
  "devices": [
    {
      "device_id": "jetson-orin-01",
      "device_type": "jetson-orin-nano",
      "last_seen": "2026-09-02T14:03:11Z",
      "stale": false,
      "activity_type": "inference",
      "stats": { "fps": 28.4, "gpu_util_pct": 91 }
    },
    {
      "device_id": "servo-bench-1",
      "device_type": "servo-tester",
      "last_seen": "2026-09-02T13:58:02Z",
      "stale": true,
      "activity_type": "telemetry",
      "stats": { "channel_1": { "position_us": 1500 } }
    }
  ]
}
```

### 2.4 `GET /local/v1/devices/{device_id}`

Full latest snapshot for one device — same shape as a single entry above, but
including `metadata` and `task`.

### 2.5 `DELETE /local/v1/devices/{device_id}`

Forgets a device — drops it from `/devices` and the Pixoo rotation. Useful once a
board's been swapped or a test rig retired. Gated behind `device_key` when auth is
enabled; otherwise anyone on the LAN can call it, same trust boundary as everything
else here.

### 2.6 Staleness

SmartCord marks a device `stale: true` when no report has arrived within
`stale_after_seconds` (from the discovery doc, default `report_interval_seconds * 3` =
30s). Stale devices grey out on the Devices page and drop out of the Pixoo screen
rotation — the same "auto-hide when the data source is unavailable" behavior
`Pixoo/Screens/*` already implements for `SpectralisSampler` and `GpuSampler`; a
`DeviceScreen` reads from the same device store those local samplers already feed into
via `PixooDashboard`, so it doesn't need its own staleness logic.

### 2.7 Versioning

Path-based: `/local/v1/...`, same pattern as Part 1. Since this is a single machine's
local server rather than a public API, a version bump just means old device firmware
keeps hitting `/local/v1/report` while new firmware moves to `/local/v2/report` — no
coordination needed beyond what's on the bench.

### 2.8 Reference implementation checklist

- [ ] `POST /local/v1/report` bound to the LAN interface, not just loopback
- [ ] Accepts unknown fields + unknown `device_type`/`activity_type` without rejecting
- [ ] `stats` and `metadata` stored opaque, validated only as "is an object"
- [ ] `GET /local/v1/devices` and `GET /local/v1/devices/{id}`
- [ ] `DELETE /local/v1/devices/{id}`
- [ ] Serves `/local/.well-known/ingest.json`
- [ ] Staleness computed from `stale_after_seconds`, never stored as a device field
- [ ] Optional `X-Device-Key` — off by default, per-device key configurable in Settings

### 2.9 Why this shape

- **Push, not pull, mirrors Part 1** — SmartCord doesn't need to know how to reach a
  Jetson's IP or auth into a servo tester's firmware. The device already knows its own
  cadence; it just fires and forgets.
- **No `target`, unlike Part 1** — there's no backend-owned ID space to resolve
  against. A device report is a first-person statement about itself, not a claim about
  engagement with some other entity.
- **Auth optional, not absent** — a home LAN and a shared LAN are different trust
  boundaries. Defaulting off keeps a one-off bench device (an Arduino sending three
  numbers over Wi-Fi) friction-free; the `device_key` escape hatch exists for the
  networks where "anyone on the LAN" is too wide.
- **Shared `stats`/`metadata` vocabulary with Part 1** — a training run looks the same
  shape whether it's this machine's GPU (Part 1, `activity_type: "training"`) or a
  Jetson's (Part 2, `activity_type: "inference"`). One mental model, two trust
  boundaries.

---

## Appendix — shared vocabulary

`activity_type` values that mean the same thing in both parts:

| value | part 1 (human, over internet) | part 2 (device, LAN) |
|---|---|---|
| `training` | this machine is training a model | — |
| `inference` | — | a device is running live inference |
| `telemetry` / `sensor` | — | raw device readings, no "activity" framing |
| `coding`, `writing`, `designing`, `meeting`, `listening`, `idle`, `other` | human activity | — |

`stats` vs `metadata` — identical rule in both parts: `stats` is numeric/progress data
meant to be rendered live (a bar, a counter, a gauge); `metadata` is descriptive tags.
Neither is validated server-side beyond "is a JSON object" — the shape is owned by
whatever tooling is producing it, not by this spec.

---

## Part 3 — Custom Pixoo Screen Format (v1, implemented)

**No plugin DLLs, no dynamic code loading.** A custom screen is one JSON file
describing a background colour and a list of `text` / `bar` / `rect` elements, each
optionally bound to a small set of documented data tokens. This is deliberately less
powerful than a real plugin API — it can't do conditional logic, animation state, or
anything a token doesn't expose — in exchange for being trivially safe to load (it's
data, not code) and simple enough that a screen definition is a five-minute edit.

### 3.1 Where files live

`%AppData%\SmartCord\pixoo-screens\*.json` — created automatically on first run.
Help menu → "Open custom Pixoo screens folder" jumps straight there. Drop a `.json`
file in, hit "Reload custom screens" on the Pixoo64 page (or restart), and it appears
in the screen checklist alongside the built-ins.

### 3.2 File shape

```json
{
  "id": "my-clock",
  "label": "Big Clock",
  "background": "#0b0b12",
  "availableWhen": "always",
  "elements": [
    { "type": "text", "x": 0, "y": 4, "center": true, "scale": 2, "color": "#ffffff", "text": "{clock.hhmm}" },
    { "type": "text", "x": 0, "y": 24, "center": true, "color": "#9aa0ab", "text": "{wakatime.project}" },
    { "type": "bar", "x": 4, "y": 44, "w": 56, "h": 4, "color": "#23a55a", "track": "#1c1e24", "value": "{system.cpuFraction}" },
    { "type": "rect", "x": 0, "y": 0, "w": 64, "h": 64, "fill": false, "color": "#202020" }
  ]
}
```

| field | type | required | notes |
|---|---|---|---|
| `id` | string | yes | stable, unique across all files — becomes the settings key in `Pixoo.Screens[]` |
| `label` | string | no | shown in the UI checklist; falls back to `id` |
| `background` | string | no | hex color, default `#000000` |
| `availableWhen` | string | no | `always` (default), `gpu`, `compute`, `nowplaying`, or `coding` — reuses the same auto-hide signals the built-in screens use |
| `elements` | array | no | see below |

**Element fields** (all optional except `type`):

| field | applies to | notes |
|---|---|---|
| `type` | all | `"text"`, `"bar"`, or `"rect"` |
| `x`, `y` | all | top-left corner, 0-63 |
| `w`, `h` | `bar`, `rect` | size in pixels |
| `scale` | `text` | integer pixel scale, default 1 |
| `center` | `text` | horizontally centers on `y`, ignoring `x` |
| `color` | all | hex, default `#ffffff` |
| `track` | `bar` | background color of the unfilled portion |
| `fill` | `rect` | `true` (default) = filled, `false` = outline |
| `text` | `text` | literal string, may contain `{token}` placeholders |
| `value` | `bar` | a `0`–`1` literal, or a `{token}` that resolves to one |

A malformed file (bad JSON, missing `id`, or an `id` that collides with another file)
is skipped with a warning in the log — it never crashes the rotation.

### 3.3 Tokens

Substituted wherever they appear inside a `text` or `value` string as `{token.name}`.
An unrecognized token is left as literal text (so a typo is visible, not silently
blank).

| token | resolves to |
|---|---|
| `clock.hhmm` | current time, `HH:mm` |
| `clock.hhmmss` | current time, `HH:mm:ss` |
| `system.cpuPct` | CPU load, whole percent |
| `system.cpuFraction` | CPU load, `0`–`1` |
| `system.ramUsedMb` / `system.ramTotalMb` | RAM in MB |
| `system.ramFraction` | RAM used, `0`–`1` |
| `gpu.utilPct` / `gpu.utilFraction` | GPU load (0 when no GPU sample yet) |
| `gpu.tempC` | GPU temperature °C |
| `gpu.name` | short GPU name (e.g. "RTX 5080") |
| `wakatime.project` / `wakatime.language` | from whatever's currently driving presence (WakaTime/Hackatime or the foreground-window fallback) |
| `wakatime.today` | humanized duration, e.g. "2h 14m" |
| `nowplaying.title` / `nowplaying.artist` | Spectralis if running, else Windows' media session broker |
| `nowplaying.progress` | track position, `0`–`1` |
| `discord.status` | the same text shown in the sidebar's connection line |
| `project.title` | the active project selected on the Dashboard |

### 3.4 Why this shape (not a plugin API)

- **Data, not code** — a JSON file can't do anything malicious or crash the app beyond
  "fail to parse," so there's no sandboxing to get right.
- **The token list is the extension surface** — adding a new token (a new sampler, a
  new integration) is a small, reviewable change in one file
  (`CustomScreen.cs`'s `Tokens` table), versus a plugin API needing a stable ABI.
- **Deliberately less powerful than `IPixooScreen`** — the built-in screens
  (`StatusScreen`, `GpuScreen`, …) still exist as real C# for anything a token table
  can't express. This format is for "I want a specific number on the panel," not
  "I want to build a new kind of screen."
