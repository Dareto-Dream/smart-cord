# SmartCord

Discord Rich Presence that actually shows what I'm doing, plus a little dashboard on a
64x64 LED panel on my desk. Built for me, running on my machine. If you found this
because you're also trying to make your Discord status less useless, hey, feel free to
steal whatever's useful, but don't expect support.

## what it does

- Sets a Discord Rich Presence card based on either a preset you pick manually, or
  auto-detected activity (right now: which dev process is running — VS Code, a
  terminal, `git`, `cargo`, etc).
- Pulls real project/language data from WakaTime or Hackatime if you've got either
  wired up, so "coding" actually says what language and how long instead of guessing
  off a window title.
- Pushes a rotating dashboard to a Divoom Pixoo64 — system stats, GPU usage (via
  `nvidia-smi`), a "now playing" screen that reads a Spectralis OBS overlay if it finds
  one running (falls back to whatever Windows itself says is playing — Spotify desktop,
  a browser tab, anything with System Media Transport Controls), and whatever else I
  bolt on next. Screens aren't hardcoded either — drop a JSON file describing your own
  into the custom screens folder (Help menu has a shortcut) and it shows up in the
  rotation; see `STANDARDS.md` Part 3 for the format.
- Shows a "Listening" card when I've stepped away from the keyboard but music's still
  playing, via the same Windows media-session broker.
- One-click "Streaming / DND" button on the Dashboard: fixed presence card, blanks the
  Pixoo, doesn't touch whatever mode/preset was active underneath.
- Optional local HTTP server (off by default, Settings page) serving current status as
  JSON plus a ready-made overlay page — point an OBS Browser Source at it.
- A weekly recap toast (Sunday evenings, or "Show weekly recap" in the Presence menu)
  summarizing hours logged per project from a small local log — not a real time
  tracker, just enough memory of what WakaTime already told us to say "your week" back.
- Lives in the tray. Has a real settings window now instead of just a context menu,
  because clicking through a tray menu to change a preset got old fast.
- Remembers what you had set last time. Restarts don't wipe it.

## running it

You need .NET 8 and Windows (WinForms, sorry). Then:

```
git clone <this repo>
cd SmartCord
dotnet build
dotnet run --project SmartCord
```

Copy `.env.example` to `.env` and fill in what you're actually using — at minimum you
want a Discord application client ID in `appsettings.json` (`Discord.ClientId`) if
you're not using mine. WakaTime/Hackatime auth happens through the app itself
(Integrations page → paste your API key, or OAuth if the backend supports it) — it
gets DPAPI-encrypted to `%AppData%\SmartCord\secrets.dat`, not stored in plain config.

`dotnet run -- --download-icons` fetches and rasterizes the Discord asset art into
`icons/` if you want to regenerate that folder.

Pixoo64 stuff is off by default — flip it on in Settings and point it at your panel's
LAN IP. If you don't have one, ignore that whole page, everything else works fine
without it.

## known rough edges

Things I know about and haven't gotten around to:

- No installer, on purpose. `scripts/release.ps1 -Version x.y.z` publishes
  self-contained and zips it — that's the whole release process. Not an installer
  with an update feed, this isn't a thing I'm going to be maintaining for other
  people.
- No tests. The resolver logic that picks what to show on the card is the one place
  that'd actually benefit from some, and it doesn't have any.
- Signal detection used to be pure "is this process running anywhere" first-match.
  Now there's a `SmartCord/Signals/ForegroundWindowSource.cs` that checks what's
  actually *focused* and parses the title bar for file/language before falling back
  to the old blind process-scan — but it's still a priority cascade (WakaTime beats
  focused-window beats process-scan), not real weighted blending. No git branch/diff
  info either — window titles don't give a real filesystem path to shell out with.
- `STANDARDS.md` describes an activity-heartbeat API (for pushing status to a public
  page) and a LAN device-ingest API (for other stuff on my network — a Jetson doing
  inference, whatever's on the bench — to report into this same dashboard). Both are
  specced. Neither is implemented yet. It's there so future-me builds it to spec
  instead of freehanding it again.

## why it's built this way

WinForms because I didn't want to fight a UI framework for a tray app nobody else is
going to run. `DiscordRichPresence` for the IPC pipe. WakaTime/Hackatime because I
already track time there and didn't want a second system. The Pixoo stuff exists
because I had a 64x64 panel doing nothing on my desk and system tray icons are boring.

None of this is trying to be a "product." It's the kind of project where the git log
has commits like `damn :(` in it. If that's not what you're looking for, this probably
isn't the repo for you — and that's fine.

## license

Didn't add one. Don't republish this as your own thing; otherwise do what you want
with the pieces.
