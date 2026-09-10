# Project Zomboid dedicated server: runtime facts

Research resolving [#5](https://github.com/MCrank/ZWarden/issues/5). Researched **2026-09-10**.

Facts only. Where a PRD assumption is contradicted, it is stated plainly with the
contradicting source. Nothing here recommends a ZWarden design.

## How to read this

Every fact carries a source URL and the release it was verified against. Confidence
labels are per claim group:

- **High** — official/primary source, or verified by direct execution during this research.
- **Medium** — primary source but indirect, or verified at one patch version and assumed
  stable across the patch line.
- **Low** — community/vendor sources only; a lead, not a fact.

Source tiers used:

| Tier | Source | Notes |
| --- | --- | --- |
| Primary — official | `pzwiki.net` (official wiki), `projectzomboid.com/blog/news`, `store.steampowered.com/eula/108600_eula_1` | pzwiki blocks normal fetches (403); pages were retrieved via `?action=raw` with a browser user agent. |
| Primary — Valve | `partner.steamgames.com`, `api.steampowered.com`, `store.steampowered.com`, Valve's own SteamCMD artifacts | `developer.valvesoftware.com` is behind an Anubis proof-of-work wall and could **not** be read live; the Source RCON spec came from a `web.archive.org` capture. |
| Primary — executed | SteamCMD client `1788292693` run during this research on 2026-09-10 | Marked "verified by execution". |
| Primary-adjacent — game code | Third-party decompilations of the shipped `zombie.*` classes, version-pinned | Not Indie Stone-published, but it is the game's own logic. High confidence for behaviour, unofficial as a document. |
| Lower confidence | Hosting vendors, community guides, OSS client libraries | Always labelled inline. |

---

## 1. Which release this is

**Confidence: High.**

- Current **stable** build is **42.20.4**. The site header reads verbatim
  `Stable Build: 42.20.4 | Unstable Build: 42.20.4` — stable and unstable are currently
  the same build. — <https://projectzomboid.com/blog/news/> (2026-09-10)
- **Build 42 is out of beta and is the default branch.** "Project Zomboid version 42.20
  has been released to the public Stable branch!" — 42.20 went stable **29 July 2026**. —
  <https://projectzomboid.com/blog/news/2026/07/project-zomboid-build-42-20-released/>,
  <https://projectzomboid.com/blog/news/2026/07/build-42-stable-plans/> (42.20)
- Stable point releases: 42.20.0 (29 Jul 2026), 42.20.1/.2 (5 Aug), 42.20.3 (17 Aug),
  **42.20.4 (26 Aug 2026)**. Build 42 first hit unstable 17 Dec 2024. —
  <https://pzwiki.net/wiki/Version_history> (42.20.4)
- **Build 41 is still patched in parallel**: 41.78.20 (29 Jul 2026), **41.78.21**
  (26 Aug 2026). — <https://pzwiki.net/wiki/Version_history> (42.20.4)
- Build 43 (NPCs) is the next major build and is not released. —
  <https://pzwiki.net/wiki/Build_42> (42.20.4)

### Build 42 has multiplayer, and it is the current MP server

This is the single most consequential finding for sequencing.

- **Multiplayer shipped in Build 42.13.0 on 11 December 2025.** "Unstable Build 42.13,
  which contains multiplayer support, has been released." At release: "Multiplayer is WIP
  and has been released for stress testing… It is currently inadvisable to have more than
  20 players on a server." — <https://projectzomboid.com/blog/news/2025/12/unstable-42-mp-released/> (42.13)
- The wiki concurs: "online multiplayer starting with Build 42.13.0… reimplemented in
  Build 42.13.0." — <https://pzwiki.net/wiki/Multiplayer> (written for 42.13.0)
- **MP is in the stable 42.20 line.** The 42.20.0 changelog carries a dedicated `MP`
  section ("Re-Worked and Re-Enabled anti-cheats", "Fixed a server-side exception that
  could occur during dedicated server startup"). — <https://pzwiki.net/wiki/Build_42.20.0> (42.20.0)
- Ongoing MP work is promised through the B42 patch cycle. —
  <https://projectzomboid.com/blog/news/2026/07/project-zomboid-build-42-20-released/> (42.20)

So B41.78 is **not** the only MP-capable dedicated server any more, and B42 is not
"multiplayer-less" as of this research date.

### Steam branches on the dedicated-server app

**Confidence: High** — read from Valve's own app metadata via
`steamcmd +login anonymous +app_info_print 380870 +quit` (verified by execution, 2026-09-10).

| Branch | Build id | Description | Updated |
| --- | --- | --- | --- |
| `public` (default) | 24909836 | *(none)* — this is the 42.20.x line | 2026-08-26 11:15 UTC |
| `legacy41` | 24928750 | "Build 41.78.21" | 2026-08-26 11:16 UTC |
| `42.19` | 24929695 | "Build 42.19.2" | 2026-08-26 11:15 UTC |

Also `"privatebranches" "1"` — password-protected branches exist but are not enumerable
anonymously. App 108600 (the client) carries an identical branch set.

- **`unstable` and `b41multiplayer` no longer exist as branch names.** Neither string
  appears in either app's branch list. The official switch-back instructions now name
  `legacy41` and `42.19`, under a Steam UI section renamed "Game Versions & Betas". —
  <https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/?appid=108600> (official
  announcement "42.20.4 STABLE & 42.19.2 UNSTABLE & 41.78.21 LEGACY Hotfixes Released",
  2026-08-26); <https://projectzomboid.com/blog/news/2026/07/project-zomboid-build-42-20-released/>
- The older `-beta unstable` line has been removed from the current wiki dedicated-server
  page; `-beta legacy41` is what is documented now. —
  <https://pzwiki.net/wiki/Dedicated_server> (42.20.0)
- Historical note (**Low confidence, stale**): `-beta b41multiplayer` was the documented
  flag in Dec 2021. — <https://steamcommunity.com/sharedfiles/filedetails/?id=2678359176>

---

## 2. System and runtime requirements

### Operating system

**Confidence: High.**

- "Dedicated server can be used to host Project Zomboid on either **Windows or Linux**."
  macOS is not offered for the server. — <https://pzwiki.net/wiki/Dedicated_server> (42.20.0)
- Only Debian and Ubuntu have official instructions. Documented prep:
  `sudo add-apt-repository multiverse; sudo dpkg --add-architecture i386; sudo apt update;
  sudo apt install steamcmd`. **i386 multiarch is required** (for SteamCMD, see §4). —
  <https://pzwiki.net/wiki/Dedicated_server> (42.20.0)
- Valve's app metadata lists `"oslist" "windows,macos,linux"` for app 380870, i.e. a macOS
  depot exists (380872, 205 MB) even though the wiki does not document macOS hosting.
  (verified by execution, 2026-09-10)

### RAM, CPU, disk

**Confidence: Low for sizing — there is no official dedicated-server specification.**

This is worth stating flatly: **The Indie Stone publishes no system requirements for the
dedicated server.** App 380870 has no Steam store page —
`https://store.steampowered.com/api/appdetails?appids=380870` returns
`{"380870":{"success":false}}` and `steamcommunity.com/app/380870` redirects to the store
front (verified by execution, 2026-09-10). Neither pzwiki nor theindiestone.com publishes
server CPU/RAM/disk minimums.

What *is* verifiable:

- **Disk**: the public-branch download for Linux totals **7,212,532,083 bytes (~6.72 GiB)**
  — depot 380871 shared content (6,886,641,123 B) + 380873 linux (217,979,308 B) + 1006
  Steamworks redistributable (107,911,652 B). The Windows total is exactly
  **7,160,173,345 B**, which matched the observed download denominator byte-for-byte.
  (verified by execution via `app_info_print 380870` and a partial `app_update`, 2026-09-10)
  **Confidence: High.**
- **The only official RAM number anywhere** is the ship default in the launch script: the
  shipped `StartServer64.bat` "specifies **16GB** of starting memory for the server. You
  must edit the StartServer64 file and change the -Xms and -Xmx… or the server will fail
  to start with memory errors." That is a ship default, not a recommendation. —
  <https://pzwiki.net/wiki/Dedicated_server> (42.20.0). Note this sentence is unchanged
  from the 41.78-era page, so it may be stale for 42.20.4. **Confidence: Medium.**
- **Official soft player cap**: "inadvisable to have more than 20 players on a server. This
  will be further improved." — <https://projectzomboid.com/blog/news/2025/12/unstable-42-mp-released/> (42.13).
  Separately, `MaxPlayers` defaults to 32 with an explicit warning: "Server player counts
  above 32 will potentially result in poor map streaming and desync." —
  <https://pzwiki.net/wiki/Server_settings> (42.20.0)
- **Deprecated official figure — do not use**: "Generally 2GB gets around 10-15 players and
  4GB can cover 20-30 possibly." This is on a page pzwiki explicitly banner-marks as
  *deprecated* and it is Build 41-or-earlier era. — <https://pzwiki.net/wiki/Multiplayer_FAQ>
- **Third-party sizing consensus (Low confidence, hosting vendors)**: roughly 6 GB base
  plus ~0.5 GB per player; 4–5 GB for ≤10 players, 6–8 GB for 10–20, 10–16 GB+ for large
  or heavily modded; RAM climbs over a server's lifetime as map cells load. CPU is
  described as single-thread-bound, so clock speed beats core count. 50 GB+ NVMe suggested
  for `Saves`/`db` growth. —
  <https://pinehosting.com/blog/project-zomboid-build-42-server-requirements-ram-cpu-hosting/>,
  <https://dedicatedgameservers.net/articles/project-zomboid-dedicated-server-requirements-2026/> (2026)
- Client-side comparison point, official: the game client's default heap is 3 GB via
  `ProjectZomboid64.json`, unmodded gameplay uses ~2.4–2.8 GB, and the Steam store client
  minimum is 8 GB RAM / 10 GB disk / Intel 2.77 GHz quad-core. —
  <https://pzwiki.net/wiki/Tech_Support> (42.20+), <https://store.steampowered.com/app/108600/Project_Zomboid/>

### Java runtime

**Confidence: High.**

- **The server bundles its own JRE. No system Java is needed.** "The game **ships with its
  own runtime**, so this is needed for modding only, not for playing." The runtime lives in
  `jre64/`; the Windows launch line invokes `".\jre64\bin\java.exe"`. —
  <https://pzwiki.net/wiki/Java>, <https://pzwiki.net/wiki/Dedicated_server> (42.20.4 / 42.20.0)
- **Java version is 25.** "As of **Build 42.13.0**, Project Zomboid runs on **Java 25**…
  Use JDK 25. A class file built for a higher version is rejected at load time with
  `UnsupportedClassVersionError`." Build 41.78 shipped Java 17 — that is the `legacy41`
  figure. — <https://pzwiki.net/wiki/Java> (42.20.4)
- The JVM is launched with `--add-exports=java.base/jdk.internal.misc=ALL-UNNAMED` because
  game code reaches into internal JDK packages. — <https://pzwiki.net/wiki/Java> (42.20.4)

### Launch scripts and heap

**Confidence: High.**

- **Windows**: `StartServer32.bat` (Steam, 32-bit), `StartServer64.bat` (Steam, 64-bit),
  `StartServer64_nosteam.bat` (non-Steam 64-bit). "**Do not launch the server via Steam.**
  If accidentally done, verify the integrity of the files." —
  <https://pzwiki.net/wiki/Dedicated_server> (42.20.0)
- **Linux**: `start-server.sh`, run as `bash start-server.sh`; `-nosteam` for GOG;
  `-servername SERVERNAME` to pick a config set. tmux is recommended for session
  persistence. — <https://pzwiki.net/wiki/Dedicated_server> (42.20.0)
- Valve's registered launch configs for 380870 are exactly `StartServer64.bat`,
  `StartServer32.bat`, `start-server.sh`, all described "Start Server". (verified by
  execution, 2026-09-10)
- **Heap is set inside `StartServer64.bat` / `start-server.sh`, not in
  `ProjectZomboid64.json`.** `ProjectZomboid64.json` is the *client* launcher config. —
  <https://pzwiki.net/wiki/Dedicated_server>, <https://pzwiki.net/wiki/Tech_Support>
- Verbatim server JVM line from the 42.20-era wiki (6 GB example):

  ```text
  ".\jre64\bin\java.exe" -Djava.awt.headless=true -Dzomboid.steam=1 -Dzomboid.znetlog=1 \
    -XX:+UseZGC -XX:-CreateCoredumpOnCrash -XX:-OmitStackTraceInFastThrow -Xms6g -Xmx6g \
    -Djava.library.path=natives/;natives/win64/;. -cp %PZ_CLASSPATH% \
    zombie.network.GameServer -statistic 0
  ```

  — <https://pzwiki.net/wiki/Dedicated_server> (42.20.0). The collector is **ZGC**; the
  wiki notes `-XX:+AlwaysPreTouch` is "officially recommended… if you are using ZGC as of
  Java 21." — <https://pzwiki.net/wiki/Startup_parameters> (42.20.4)
- Heap semantics: `-Xms` is a minimum and "The game will not start if there is not enough
  memory available on the system to allocate"; `-Xmx` above physical RAM "will end up using
  virtual memory". In `StartServer64.bat` "the `--` is not needed because the script already
  separates the JVM and game arguments". — <https://pzwiki.net/wiki/Startup_parameters> (42.20.4)
- Useful server arguments (all 42.20.4-verified): `-port`, `-udpport`, `-servername`,
  `-adminusername`, `-adminpassword`, `-ip` (bind address), `-cachedir=`, `-nosteam`,
  `-statistic {int}`, `-coop`, `-steamvac`, `-anti-cheats`, `-debuglog=`,
  `-console_dot_txt_size_kb={int}`, and `-gui` (documented as broken: "unfinished, doesn't
  render properly, causes lots of exceptions"). Linux-only:
  `-Ddeployment.user.cachedir={path}`. Known 42.20.4 bug: `-Dsoftreset` "does not work as
  of 42.20.4." — <https://pzwiki.net/wiki/Startup_parameters> (42.20.4)
- Gotcha for hosts that previously ran B41 on the same box: "Verify that `steam_appid.txt`
  contains only one line with the numbers **108600**", else you get "Assertion Failed:
  Illegal termination of worker thread". — <https://pzwiki.net/wiki/Dedicated_server> (42.20.0)

### Native library dependencies

**Confidence: Low. Could not verify from any official source.**

- `start-server.sh` execs `./ProjectZomboid64` with `LD_PRELOAD="${LD_PRELOAD}:${JSIG}"`
  (libjsig), and signals sent to the *script* are **not** propagated to the server process
  unless `exec` is used. — <https://steamcommunity.com/app/108600/discussions/6/3812906855248680121/>
  (user bug report, Jun 2023, B41 era — **Low**)
- Third-party package manifests list `lib32gcc-s1` + `lib32stdc++6` on apt distros /
  `glibc.i686` on dnf distros; LinuxGSM states glibc ≥ 2.15 and tmux ≥ 1.6; Arch AUR lists
  `lib32-glibc`. **These are SteamCMD's requirements, not demonstrably the PZ server's.** —
  <https://lgsm.vincy.ru/linuxgsm.com/servers/pzserver/index.html>,
  <https://aur.archlinux.org/packages/project-zomboid-server> (**Low**)
- Official-adjacent hint, for the client not the server: the Steam store's Linux minimum is
  "Ubuntu LTS 16.04/Steam Machine. **Requires Libgcc 6 or higher**." —
  <https://store.steampowered.com/app/108600/Project_Zomboid/>
- The wiki *does* document that signal handling on shutdown is a real concern and that the
  devs "explicitly discourage" SIGTERM-based stops, recommending a FIFO
  (`ListenFIFO=/opt/pzserver/zomboid.control`) with `echo save` then `echo quit` on stop. —
  <https://pzwiki.net/wiki/Dedicated_server> (42.20.0), citing
  <https://theindiestone.com/forums/index.php?/topic/63563-4178-multiplayer-zomboid-dedicated-server-does-not-handle-sigterm/#comment-376957>

---

## 3. Ports and protocols

**Confidence: High.**

### The current set is exactly two UDP ports

- "Project Zomboid dedicated servers require the following open ports to successfully
  connect to clients: **16261 UDP**; **16262 UDP — Direct Connection Port**." The wiki's
  own firewall example is `ufw allow 16261/udp` and `ufw allow 16262/udp`. —
  <https://pzwiki.net/wiki/Dedicated_server> (42.20.0, page last edited 3 Aug 2026)
- **Role split**, from the Build 41.77 changelog that defines the current scheme (41.77
  went stable 4 Oct 2022): "*Important for server providers: Servers now have two ports for
  clients to connect to. The **game port (UDP 16261 by default) is used to handle Steam
  queries**. The additional port (**UDP 16262 by default) is used to handle the direct
  connection**. These ports can be configured through the server options.*" Plus: "Clients
  will first try to use a direct connection. If direct connection fails, clients will try
  to connect via Steam." — <https://pzwiki.net/wiki/Build_41.77> (41.77)
- **There is no separate Steam query port.** Steam queries ride on the game port
  16261/UDP, per the 41.77 entry above.
- If 16262 is closed, the client falls back to the Steam relay and displays "WARNING:
  SERVER HAS PORT %1 CLOSED. PERFORMANCE MAY BE SEVERELY AFFECTED". —
  <https://pzwiki.net/wiki/Build_41.77> (41.77)

### INI keys that exist now, versus those that were removed

Present in 42.20.0 — all quotes from <https://pzwiki.net/wiki/Server_settings> (42.20.0):

- `DefaultPort=16261` — "Default starting port for player data. If UDP, this is this one of
  two ports used. Min: 0 Max: 65535 Default: 16261" with the note "This port will need to be
  open on your router and firewall".
- `UDPPort=16262` — "Min: 0 Max: 65535 Default: 16262".
- `RCONPort=27015` — "The port for the RCON (Remote Console) Min: 0 Max: 65535 Default: 27015".
- `RCONPassword=` — "RCON password (Pick a strong password)".
- `UPnP=true` — "Attempt to configure a UPnP-enabled internet gateway to automatically setup
  port forwarding rules. The server will fall back to default ports if this fails".
- `server_browser_announced_ip=` — "Set the IP from which the server is broadcast. This is
  for network configurations with multiple IP addresses, such as server farms".

**`SteamPort1` / `SteamPort2` (8766 / 8767) are gone from the current documentation.**

- They are absent from the 42.20.0 `Server settings` list, a full-text pzwiki search for
  `SteamPort` returns "There were no results matching the query", as does `insource:"8766"`,
  and the 42.20.4 `Startup parameters` page documents `-port` and `-udpport` but no
  `-steamport1`/`-steamport2`. — <https://pzwiki.net/wiki/Server_settings>,
  <https://pzwiki.net/w/index.php?search=SteamPort&title=Special%3ASearch&fulltext=1&ns0=1>,
  <https://pzwiki.net/wiki/Startup_parameters> (42.20.0 / 42.20.4)
- The B41-era page (marked "revised for the current stable version (41.78.16)") listed
  `DefaultPort=16261`, `RCONPort=27015`, `SteamPort1=8766`, `SteamPort2=8767` — and had
  **no `UDPPort` at all**. —
  <https://web.archive.org/web/20260111180735/https://pzwiki.net/wiki/Server_settings> (41.78.16)
- The cutover is dated by pzwiki itself: a since-deleted section headed "**Ports used
  before version 41.77**" listed "16261 UDP / 8766, 8767 UDP". —
  <https://web.archive.org/web/20260204123426/https://pzwiki.net/wiki/Dedicated_server> (41.78 era)
- **Caveat, Confidence Medium**: this is proof of *documentation* removal. It was not
  verified from a binary or a shipped `servertest.ini` that the 42.20.4 server rejects or
  ignores the `SteamPort1`/`SteamPort2` keys.

### The "one extra UDP port per player" requirement is dead — and was never UDP

- The historical rule was 16261/**UDP** for handshake plus **one TCP port per player slot**
  for map streaming, e.g. "16261 UDP / 16262 - 16272 **TCP**" for 10 slots. —
  <https://pzwiki.net/wiki/Multiplayer_FAQ>
- **The settling citation**, inline on that same page: "*(**Version 41.65**, after testing,
  multiplayer online requires **only one UDP port 16261**)*". —
  <https://pzwiki.net/wiki/Multiplayer_FAQ> (41.65)
- 41.77 then re-added a *fixed* second port (16262/UDP, direct connection), not a per-player
  one. — <https://pzwiki.net/wiki/Build_41.77> (41.77)
- Current-release confirmation that scaling is per-*instance*, not per-*player*: "Before
  running the 2nd server instance, be sure to open further UDP ports on your Server (e.g.
  16274 + 16275)… **Each instance will require 2 clear UDP ports**." —
  <https://pzwiki.net/wiki/Dedicated_server> (42.20.0)
- Note the `Multiplayer_FAQ` page is banner-marked deprecated and still carries the
  pre-41.65 per-player text in two places. It is dated evidence for the change, not current
  guidance.

### Publish / forward matrix

| Port | Proto | Host-publish? | Purpose | Source |
| --- | --- | --- | --- | --- |
| **16261** | **UDP** | **Yes — mandatory** | Game port; also carries Steam queries; `DefaultPort` | pzwiki `Dedicated_server` 42.20.0; `Build_41.77` |
| **16262** | **UDP** | **Yes — required** | Direct Connection Port; `UDPPort`. Closed ⇒ Steam-relay fallback and a "PERFORMANCE MAY BE SEVERELY AFFECTED" client warning | pzwiki `Dedicated_server` 42.20.0; `Build_41.77` |
| 27015 | TCP | **No — internal/admin only** | RCON. Can be pinned to loopback via the `rconlo` system property | pzwiki `Server_settings` 42.20.0; Valve RCON spec |
| 8766 / 8767 | UDP | **No — obsolete** | Old `SteamPort1`/`SteamPort2`; needed only before 41.77 | archived pzwiki `Dedicated_server` (41.78 era) |
| 16263–16272 | TCP | **No — obsolete** | Old per-player map-streaming ports; retired at 41.65 | pzwiki `Multiplayer_FAQ` (deprecated) |

Clients connect on the **game port** (16261) — "Enter port (default: 16261)". —
<https://pzwiki.net/wiki/Dedicated_server> (42.20.0)

---

## 4. SteamCMD

### Obtaining it

**Confidence: High** (direct artifact inspection). **Medium** on the exact `lib32*` package
names — `developer.valvesoftware.com/wiki/SteamCMD` could not be read (Anubis PoW wall), so
anything that page uniquely documents is unconfirmed.

- Live Valve CDN artifacts, all verified 2026-09-10:
  - <https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz> — HTTP 200,
    2,428,561 bytes, `Last-Modified: Fri, 05 Jan 2018`
  - <https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip> — HTTP 200, 774,825
    bytes, containing exactly one file: `steamcmd.exe`, 1,687,464 bytes, dated 2013-12-03
  - <https://steamcdn-a.akamaihd.net/client/installer/steamcmd_osx.tar.gz>
  - `media.steampowered.com` aliases serve the same files.
- Linux tarball contents: `steamcmd.sh`, `linux32/steamcmd`, `linux32/steamerrorreporter`,
  `linux32/libstdc++.so.6`, `linux32/crashhandler.so`. It **ships its own
  `libstdc++.so.6`** but no `libgcc_s.so.1`.
- `linux32/steamcmd` is a **32-bit x86 ELF** (`EI_CLASS=1`, `e_machine=0x0003`/EM_386).
  That is the root cause of the 32-bit library requirement on x86-64 hosts. Dynamic library
  names embedded in the binary: `libc.so.6`, `libdl.so.2`, `libm.so.6`, `libpthread.so.0`,
  `librt.so.1`.
- `steamcmd.sh` sets `LD_LIBRARY_PATH="$STEAMROOT/linux32:$LD_LIBRARY_PATH"`,
  `ulimit -n 2048`, and defines `MAGIC_RESTART_EXITCODE=42`; if the inner binary exits 42
  the wrapper re-`exec`s itself, otherwise it does `exit $STATUS` — so **the wrapper
  propagates the real exit code**.
- **It self-updates. Confirmed by execution twice.** The 2013-vintage `steamcmd.exe` printed
  `Extracting package… / Installing update… / Update complete, launching…` on first run;
  the following invocation ran normally. Client version reported afterwards: `Steam Console
  Client (c) Valve Corporation - version 1788292693`. (verified by execution, 2026-09-10)
- **Exit codes are not documented by Valve.** Documenting them is Valve's own open tracker
  issue, opened 2016-02-29, assigned to a Valve employee, never answered. —
  <https://github.com/ValveSoftware/steam-for-linux/issues/4341>. Codes observed directly:
  `0` = success, `7` = self-update-and-restart-needed (Windows bootstrap), `42` = the
  documented magic restart code on Linux. Also parse stdout for
  `Success! App '<id>' fully installed` and `Error! App '<id>' state is 0x…`, because the
  numeric codes are unspecified. **Confidence: Medium** for the parsing advice (derived,
  not stated by Valve).
- `@ShutdownOnFailedCommand` and `@NoPromptForPassword 1` are real script directives; the
  official wiki page uses `@ShutdownOnFailedCommand 1` with the comment "set to 0 if
  updating multiple servers at once". — <https://pzwiki.net/wiki/Dedicated_server> (42.20.0)

### App IDs

**Confidence: High** — read from Valve's own app metadata (verified by execution, 2026-09-10).

- **380870 = "Project Zomboid Dedicated Server"**, `"type" "Tool"`,
  `"oslist" "windows,macos,linux"`, `"ReleaseState" "released"`,
  **`"freetodownload" "1"`**, `"installdir" "Project Zomboid Dedicated Server"`.
  Change number 38763773, last change 2026-09-10.
- **108600 = "Project Zomboid"**, `"type" "Game"`, developer and publisher The Indie Stone,
  with a `"Project Zomboid EULA"` association, `"workshop_visible" "1"`,
  **`"workshopdepot" "108600"`**.
- Depots of 380870, public branch: `380871` shared content 6,886,641,123 B; `380874`
  windows 208,884,294 B; `380873` linux 217,979,308 B; `380872` macos 205,132,542 B; plus
  Steamworks redistributables `1004`/`1005`/`1006` (`"depotfromapp" "1007"`).
- **Workshop items live under app 108600, not 380870.** Valve's Web API returns
  `"creator_app_id":108600,"consumer_app_id":108600` for a PZ Workshop item, and app
  380870's metadata contains no workshop keys at all. So `workshop_download_item` must be
  given **108600**. — <https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/>

### Anonymous login

**Confidence: High — proven end-to-end by execution.**

- `steamcmd +force_install_dir <dir> +login anonymous +app_update 380870 +quit` reached
  `Update state (0x61) downloading, progress: 6.40 (458103807 / 7160173345)` with no
  account and no ownership of 108600. (verified by execution, 2026-09-10)
- Mechanism: 380870 is flagged `"freetodownload" "1"`, so no license is needed. Nuance —
  `app_status 380870` reports `release state: released (No License)` yet the download
  succeeds.
- **380870 is NOT in the anonymous package.** The anonymous account holds exactly one
  license, `packageID 17906` (Active, 737 apps, 1249 depots), and `package_info_print 17906`
  contains zero occurrences of `380870`. PZ's server is anonymously downloadable via the
  free-to-download flag, not via pkg 17906. (verified by execution, 2026-09-10)
- Valve's general mechanism, for contrast: create a TOOL app, then "Go to Installation ->
  Redistributables and turn on Dedicated Server Redistributables" and the app "will be added
  to the anonymous steamcmd package (pkg 17906) to be downloadable using SteamCMD in
  anonymous mode". — <https://partner.steamgames.com/doc/sdk/uploading/distributing_gs>
- The **official** invocation, verbatim from the wiki's `update_zomboid.txt`:

  ```text
  @ShutdownOnFailedCommand 1 //set to 0 if updating multiple servers at once
  @NoPromptForPassword 1
  force_install_dir /opt/pzserver/
  //for servers which don't need a login
  login anonymous
  app_update 380870 validate
  quit
  ```

  run as `steamcmd +runscript $HOME/update_zomboid.txt`. Legacy build:
  `app_update 380870 -beta legacy41 validate`. —
  <https://pzwiki.net/wiki/Dedicated_server> (42.20.0)
- `-beta <branch>`: **Confidence Medium.** The Steamworks branches doc does not mention
  SteamCMD syntax at all, and the Valve wiki that does is unreachable — but the official PZ
  wiki uses `-beta legacy41` verbatim, and the bootstrap binary contains the literal string
  `-beta`. `-betapassword` is **unverified — Low confidence**, though
  `"privatebranches" "1"` on both apps implies password-protected branches exist. —
  <https://partner.steamgames.com/doc/store/application/branches>

### Workshop download — server-side, anonymous: YES

**Confidence: High — verified by execution during this research.**

This was the ticket's highest-uncertainty item and it is now settled empirically.

```text
$ steamcmd +login anonymous +workshop_download_item 108600 2392709985 +quit
Connecting anonymously to Steam Public...OK
Downloading item 2392709985 ...
Success. Downloaded item 2392709985 to
  "<steamroot>\steamapps\workshop\content\108600\2392709985" (9609530 bytes)
```

(verified by execution, 2026-09-10, SteamCMD client 1788292693, no Steam account, no
ownership of 108600). Three further items — 2335368829 (877,881,540 B), 2196102849
(171,330,271 B), 2822286426 (66,687,958 B) — all downloaded successfully in one anonymous
session. So **Project Zomboid has Valve's per-app "Enable anonymous game servers to download
workshop items" setting switched on**, and ZWarden can pre-fetch Workshop content with
SteamCMD without any credential.

Supporting primary sources:

- Valve, `ISteamUGC::DownloadItem`: "If the user is not subscribed to the item (e.g. **a
  Game Server using anonymous login**), the workshop item will be downloaded and cached
  temporarily." — <https://partner.steamgames.com/doc/api/ISteamUGC>
- Valve: "Game servers can also download and install items" via `ISteamUGC::DownloadItem`.
  — <https://partner.steamgames.com/doc/features/workshop/implementation>
- The capability is a **per-app publisher opt-in** ("Enable anonymous game servers to
  download workshop items", Steamworks → app → Workshop tab). It is not documented on any
  public Steamworks page; it is attested only in developer-facing threads. —
  <https://github.com/FakeFishGames/Barotrauma/issues/7707>,
  <https://steamcommunity.com/app/285110/discussions/0/686363730790031270/> (**Medium**)
- **No official Valve statement was found announcing that anonymous Workshop downloads were
  changed, deprecated, or broken.** Reports to that effect (e.g. DayZ, Valve tracker issue
  13474 opened 2026-08-01) are apps that never enabled the flag. Treat blanket "Valve broke
  it" claims as unsubstantiated. — <https://github.com/ValveSoftware/steam-for-linux/issues/13474>

### The server also downloads Workshop content itself

**Confidence: Medium-High.**

- Driven by `WorkshopItems=` in the server INI. From an Indie Stone developer (nasKo) on the
  official PZ Steam forum, 2015-12-07: mods are "by default downloaded into the
  `SteamApps\workshop\content\108600\` folder", and you "edit the `WorkshopItems=` line with
  the ID of the workshop items you want to install." —
  <https://steamcommunity.com/app/108600/discussions/0/485624149167016526>
- The INI setting itself: `WorkshopItems=` — "List Workshop Mod IDs for the server to
  download. Each must be separated by a semicolon. Example:
  `WorkshopItems=514427485;513111049`". — <https://pzwiki.net/wiki/Server_settings> (42.20.0)
- On-disk landing spot **confirmed by execution**:
  `<steamroot>/steamapps/workshop/content/108600/<workshopid>/`. Matches Valve's generic UGC
  layout and `ISteamUGC::GetItemInstallInfo`. — <https://partner.steamgames.com/doc/api/ISteamUGC>
- Workshop fetching happens only in **Steam mode** — `StartServer64.bat` /
  `start-server.sh`, not `StartServer64_nosteam.bat` / `-nosteam`. **Confidence: Medium**
  (the `_nosteam` split is official; the Workshop consequence is community-attested). —
  <https://pzwiki.net/wiki/Dedicated_server>, <https://steamcommunity.com/sharedfiles/filedetails/?id=2678359176> (**Low** for the consequence)
- **Not verified by an Indie Stone statement**: that the server uses the anonymous Steam
  *game server* identity for those fetches. Inferred from Valve's `DownloadItem` contract
  plus the absence of any credential field in the PZ server INI. **Confidence: Medium.**
- Behaviour worth designing around, **Low confidence (hosting vendors only, no primary
  source)**: the download is asynchronous, so a newly added `WorkshopItems=` entry is often
  absent on the first restart and present on the second. —
  <https://nodecraft.com/support/games/project-zomboid/how-to-download-and-enable-workshop-mods-on-your-project-zomboid-server>

### Steam Web API for Workshop metadata

**Confidence: High — tested live against the API.**

- **`ISteamRemoteStorage/GetPublishedFileDetails/v1` — POST, NO API KEY.** Verified by a
  live keyless call:
  `curl -X POST https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/
  -d "itemcount=1" -d "publishedfileids[0]=2392709985"` → HTTP 200 with `title`, `file_size`,
  `time_updated`, `creator`, `consumer_app_id`, `preview_url`, `description`. Valve's docs
  agree: params `itemcount` and `publishedfileids[0]`, no publisher key required, unlike
  other methods in the same interface. — <https://partner.steamgames.com/doc/webapi/ISteamRemoteStorage>
- Valve's own machine-readable proof of keylessness:
  <https://api.steampowered.com/ISteamWebAPIUtil/GetSupportedAPIList/v1/> called *without* a
  key returns only the keyless surface, and it lists `ISteamRemoteStorage` →
  `GetPublishedFileDetails` and `GetCollectionDetails`. `IPublishedFileService` is absent.
- **`IPublishedFileService/GetDetails/v1` — GET only, API KEY REQUIRED.** Verified live:
  POST → HTTP 405 "This API must be called with a HTTP GET request"; GET without a key →
  HTTP 401 "Unauthorized… Please verify your `key=` parameter." `GetDetails` is not
  documented on the public `IPublishedFileService` page at all; the methods that are listed
  are mostly flagged "This call requires a publisher API key… MUST be called from a secure
  server". — <https://partner.steamgames.com/doc/webapi/IPublishedFileService>
- `ISteamRemoteStorage/GetCollectionDetails` is also keyless (`collectioncount`,
  `publishedfileids[0]`) — useful to expand a Workshop *collection* into item ids.
- **Rate limits**: "You are limited to one hundred thousand (100,000) calls to the Steam Web
  API per day." — <https://steamcommunity.com/dev/apiterms> (last updated July 2010).
  Caveat: that figure is expressed as a **per-key** limit, and `GetPublishedFileDetails`
  takes no key; Valve documents no numeric quota for keyless calls. The only enforcement
  language found is "Requests generating 403 status codes… will incur strict rate limits for
  the connecting IP." — <https://partner.steamgames.com/doc/webapi_overview>.
  **Could not verify** any published per-IP requests-per-second ceiling.
- Other binding API terms: keys must be kept confidential and not shared with third parties;
  Steam Data may only be retrieved about a user as requested by that user; Valve "may
  change, suspend or discontinue the Steam Web API… at any time for any reason, without
  notice." — <https://steamcommunity.com/dev/apiterms>

---

## 5. On-disk layout and the config file set

**Confidence: High** for everything sourced to `Dedicated_server` / `Tech_Support`;
**High (verified by execution)** for the Workshop subtree.

The server's install directory and its **user data directory are separate**. Config, saves,
logs and the player database all live under the user data directory, deliberately: an Indie
Stone developer's stated reason is that "this way settings and the world would not be
corrupted in case of an update." —
<https://steamcommunity.com/app/108600/discussions/0/485624149167016526>

### User data directory

- Windows: `%USERPROFILE%\Zomboid` · Linux: `$HOME/Zomboid` · macOS: `$HOME/Zomboid`. —
  <https://pzwiki.net/wiki/Tech_Support> (42.20+)
- **Relocatable** with `-cachedir={path}` — "Sets the absolute path for the game's cache
  directory", example `-cachedir="C:\Zomboid"`. Linux also accepts
  `-Ddeployment.user.cachedir={path}` — "Sets the game's cache directory. The same as
  setting `-cachedir`. Only works on Linux." Placed as a game argument in the launch script.
  — <https://pzwiki.net/wiki/Startup_parameters> (42.20.4)

### Install directory defaults

- Windows via Steam: `C:\Program Files (x86)\Steam\steamapps\common\Project Zomboid Dedicated Server`
- Linux via Steam: `~/.steam/steam/steamapps/common/Project Zomboid Dedicated Server`
- The wiki's own SteamCMD walkthrough installs to `/opt/pzserver`. —
  <https://pzwiki.net/wiki/Dedicated_server> (42.20.0)

### The config file set

"By default the server will look for game settings and world data named ***servertest***
inside `C:\Users\YourUsername\Zomboid`. **If this data does not exist, the server will
generate it automatically using default game settings.**" The four files, verbatim from the
wiki (identical lists given for Windows and Linux) —
<https://pzwiki.net/wiki/Dedicated_server> (42.20.0):

| File | Location | Purpose (verbatim) |
| --- | --- | --- |
| `servertest.ini` | `Zomboid/Server` | "This file contains the server configuration settings. Editable with text editor." |
| `servertest_SandboxVars.lua` | `Zomboid/Server` | "This file contains the server sandbox configuration settings. Editable with text editor." |
| `servertest_spawnpoints.lua` | `Zomboid/Server` | "This file contains the spawnpoints available in your server. Setting custom spawn points is possible. Editable with text editor." |
| `servertest_spawnregions.lua` | `Zomboid/Server` | "This file contains the regions available for spawning (i.e. Muldraugh, Rosewood, etc). Editable with text editor." |
| `servertest` (folder) | `Zomboid/Saves/Multiplayer` | "This folder contains the generated/saved world data of the server." |

- **All four are generated on first run**; none must be authored by hand. `servertest` is
  just the default value of `-servername`, and the wiki documents renaming the whole set to
  run multiple worlds.
- **Sandbox and spawn config are Lua, not INI.** Only `servertest.ini` is key=value. This
  matters for PRD 32's "structured editing": three of the four files are Lua tables. The
  wiki's `Server_settings` page embeds the full default `SandboxVars.lua` structure,
  including nested tables such as `MultiplierConfig = { … }`, and the spawnregions form
  `{ name = "Muldraugh, KY", file = "media/maps/Muldraugh, KY/spawnpoints.lua" }`. —
  <https://pzwiki.net/wiki/Server_settings> (42.20.0)
- **`servertest.ini` can be edited while the server is running**: "Changes can be saved to
  `servertest.ini` while the server is running. After `servertest.ini` is saved, use admin
  command `reloadoptions` to make the changes live." —
  <https://pzwiki.net/wiki/Dedicated_server> (42.20.0)

### Saves, logs, database

- **Saves**: `Zomboid/Saves/Multiplayer/<servername>/`. —
  <https://pzwiki.net/wiki/Dedicated_server> (42.20.0)
- **Player database**: `Zomboid/db/`, holding a file named after the server. "If you've
  already created a server with a name you want to use with all the settings set the way you
  want, use the same name as the file at `C:\Users\%your username%\Zomboid\db`." —
  <https://pzwiki.net/wiki/Dedicated_server> (42.20.0).
  **Could not verify from a primary source that this file is SQLite**, nor its exact
  extension, nor definitively that it is what holds accounts / whitelist / bans (that is
  strongly implied by `adduser` / `setpassword` / whitelist commands persisting across
  restarts with no other store, but it is an inference). **Confidence: Low** on the format.
- **Logs**:
  - Main dedicated-server log: **`server-console.txt`** in the `Zomboid` folder. "You can
    find the main game log in the `console.txt` file inside the **Zomboid** folder. For
    server issues, check `coop-console.txt` for hosted co-op servers or
    **`server-console.txt` for dedicated servers**." —
    <https://pzwiki.net/wiki/Tech_Support> (42.20+)
  - A **`Logs` folder** exists in the `Zomboid` folder and is explicitly distinguished from
    `logs.zip`: "do not confuse it with the `Logs` folder." —
    <https://pzwiki.net/wiki/Tech_Support> (42.20+)
  - `Zomboid/logs.zip` bundles "logs from the last five game launches, server logs, co-op
    logs, settings files, enabled mod lists, and several files from the last loaded save" —
    the artefact Tech Support asks for. — <https://pzwiki.net/wiki/Tech_Support> (42.20+)
  - Named server log files, each documented via the INI setting that writes it:
    **`cmd.txt`** (`ClientCommandFilter` — "commands that will not be written to the cmd.txt
    server log"), **`ClientActionLogs.txt`** (`ClientActionLogs`), **`PerkLog.txt`**
    (`PerkLogs=true` — "Track changes in player perk levels in PerkLog.txt server log"). —
    <https://pzwiki.net/wiki/Server_settings> (42.20.0)
  - `console.txt` size is capped by `-console_dot_txt_size_kb={int}`. —
    <https://pzwiki.net/wiki/Startup_parameters> (42.20.4)
  - **Could not verify** the exact directory each named log file lands in (`Zomboid/Logs/`
    is the strong presumption but was not confirmed by any page), nor whether they are
    date-rotated. **Confidence: Low** on log paths and rotation.
- **Workshop content**: `<steamroot>/steamapps/workshop/content/108600/<workshopid>/`,
  relative to the SteamCMD/Steam root, **not** the PZ install or user data directory.
  (verified by execution, 2026-09-10; also
  <https://steamcommunity.com/app/108600/discussions/0/485624149167016526>)
- **Manually installed mods**: the `Mods=` documentation points at
  `\Steam\steamapps\workshop\modID\mods\modName\`; a separate `Zomboid/mods/` location for
  hand-installed mods is widely used but **could not be verified from a primary source**
  during this research. **Confidence: Low.**

### Canonical layout

```text
<install dir>/                                   # e.g. /opt/pzserver — SteamCMD app 380870
├── jre64/bin/java                               # bundled Java 25 runtime
├── natives/  natives/win64/
├── StartServer64.bat  StartServer32.bat  StartServer64_nosteam.bat    # Windows
├── start-server.sh                                                     # Linux
├── ProjectZomboid64                             # Linux native launcher (LD_PRELOAD=libjsig)
└── steam_appid.txt                              # must contain only "108600"

<steam root>/steamapps/workshop/content/108600/   # Workshop cache — NOT under the install dir
└── <workshopid>/
    └── mods/
        └── <modFolder>/
            ├── mod.info                         # B41-era metadata
            ├── media/
            ├── common/                          # B42 layout
            └── 42/
                ├── mod.info                     # B42 metadata, version=42
                └── media/

$HOME/Zomboid/            (or %USERPROFILE%\Zomboid, or -cachedir=<path>)
├── Server/
│   ├── servertest.ini                   # key=value; reloadable live via `reloadoptions`
│   ├── servertest_SandboxVars.lua       # Lua table
│   ├── servertest_spawnpoints.lua       # Lua
│   └── servertest_spawnregions.lua      # Lua
├── Saves/
│   └── Multiplayer/
│       └── servertest/                  # world data
├── db/
│   └── servertest.<ext>                 # player accounts / whitelist / bans (format unverified)
├── Logs/                                # exists; exact contents unverified
├── server-console.txt                   # main dedicated-server log
├── logs.zip                             # last 5 launches, bundled for Tech Support
└── Statistic/                           # only when -statistic {int} is set
```

---

## 6. Workshop item ids versus PZ Mod ids

**Confidence: High — verified by downloading and inspecting four real Workshop items.**

The two identifiers are structurally different things and the relationship is **one Workshop
item to many Mod ids**.

- A **Workshop item id** is a numeric Steam `publishedfileid` (e.g. `2335368829`).
- A **Mod id** is a string from the `id=` line of a `mod.info` file inside that item.
- `mod.info` lives at `<workshopid>/mods/<modFolder>/mod.info`, and in Build 42 additionally
  at `<workshopid>/mods/<modFolder>/42/mod.info`. (verified by execution, 2026-09-10)

Live evidence, all four items downloaded anonymously on 2026-09-10:

| Workshop item | `mod.info` files found | Mod ids (`id=`) |
| --- | --- | --- |
| `2392709985` | 1 | `tsarslib` |
| `2196102849` | 1 | `RavenCreek` |
| `2822286426` | **2** | `RV_Interior_MP`, `RV_Interior_Vanilla` |
| `2335368829` | **6** (3 mods × B41 root + B42 `42/`) | `Authentic Z - Current`, `Authentic Z - Lite`, `Authentic Z - Backpacks+` |

Three traps this exposes, each observed directly:

1. **One-to-many is real and common.** Item `2335368829` ships three separate mods; item
   `2822286426` ships two. Modelling Workshop id and Mod id separately (PRD 34) is
   correct — and required, not merely tidy.
2. **The mod folder name is not the Mod id.** Item `2822286426` has folder
   `mods/RV_Interior/` but `id=RV_Interior_MP`. So resolving `Mods=` entries by folder name
   is wrong. Note that the official wiki's `Map=` guidance *does* correctly say "foldername",
   while its `Mods=` guidance says "Enter the mod loading ID here", pointing at the
   `mod.info` file — those are two different lookups and the wiki mislabels the file as
   `info.txt`. — <https://pzwiki.net/wiki/Server_settings> (42.20.0)
3. **Mod ids may contain spaces.** `id=Authentic Z - Current`. Any parser or CLI shelling
   must handle that.

Observed `mod.info` keys across the four items: `name`, `id`, `description` (**repeatable —
multiple `description=` lines appear in one file**), `poster` (**also repeatable**), `icon`,
`url`, `tags`, `authors` / `author` (both spellings observed), `versionMin`, `pzversion`,
`version`, `require` (dependency — `require=RV_Interior_MP`), `incompatible` (B42 only —
`incompatible=\AuthenticZLite,\AuthenticZBackpacks+`), `pack`, `tiledef`. So PRD 34's
"dependencies where discoverable" and "compatibility state where discoverable" are both
backed by real `mod.info` fields — `require` and `incompatible`.

### Build 42 changed the mod folder structure

**Confidence: High — verified by execution.** A B42-aware mod contains, alongside the legacy
root `mod.info` + `media/`:

- `<modFolder>/common/` — shared assets
- `<modFolder>/42/` — a version folder with its **own** `mod.info`

The two `mod.info` files differ in content, not just location. For `Authentic Z - Current`
the root file carries `pzversion=41`, `authors=`, and no `incompatible` key; the `42/` file
carries `version=42`, `author=`, and
`incompatible=\AuthenticZLite,\AuthenticZBackpacks+`. So **metadata must be read from the
version-appropriate `mod.info`**, and a single Workshop item can declare different
dependency/compatibility data per game build.

### How mods are declared to the server

All from `servertest.ini` — <https://pzwiki.net/wiki/Server_settings> (42.20.0):

| Key | Format | Verbatim documentation |
| --- | --- | --- |
| `WorkshopItems=` | semicolon-separated numeric Workshop ids | "List Workshop Mod IDs for the server to download. Each must be separated by a semicolon. Example: `WorkshopItems=514427485;513111049`" |
| `Mods=` | Mod ids | "Enter the mod loading ID here. It can be found in `\Steam\steamapps\workshop\modID\mods\modName\info.txt`" |
| `Map=Muldraugh, KY` | map **folder** name | "Enter the foldername of the mod found in `\Steam\steamapps\workshop\modID\mods\modName\media\maps\`" |

- The wiki's own install-mods procedure: "Save all mods to a steam workshop collection →
  Paste collection url into PZ ID Grabber → Open your `SERVERNAME.ini` file → In `Mods=`
  section paste the mods into the file → In `WorkshopItems=` section paste the workshop
  Items Ids." — <https://pzwiki.net/wiki/Dedicated_server> (42.20.0). Note the official
  procedure routes users through a **third-party website** to derive Mod ids from Workshop
  ids, which is itself evidence that the mapping is non-trivial.
- `WorkshopItems=` (what Steam downloads) and `Mods=` (what PZ activates) are **separate and
  both required**. — <https://pzwiki.net/wiki/Server_settings> (42.20.0)
- **The separator for `Mods=` is not documented** on the `Server_settings` page. Semicolon is
  the community convention and matches `WorkshopItems=`. **Confidence: Medium.**
- `Mods=` values are case-sensitive per community sources; the wiki notes generally "Some
  things are case-sensitive (ex: Base.Axe works, but not base.axe)". **Confidence: Medium.**
- For the `Map=` key, `2196102849` (Raven Creek) confirms the shape:
  `mods/RavenCreek/media/maps/RavenCreek/`, so the value is `RavenCreek`.
  (verified by execution, 2026-09-10)
- Mods are **not** changeable over RCON — `WorkshopItems=`/`Mods=` are config-file-only and
  require a restart. `reloadoptions` reloads server options; it does not load or unload
  mods.

---

## 7. RCON: dialect, quirks, and the admin command surface

### It is Source RCON, reimplemented in Java

**Confidence: High** (game code) / **Medium** (no Indie Stone document says "Source RCON").

- `zombie.network.RCONServer` declares Valve's constants by name and value:

  ```java
  public static final int SERVERDATA_RESPONSE_VALUE = 0;
  public static final int SERVERDATA_AUTH_RESPONSE  = 2;
  public static final int SERVERDATA_EXECCOMMAND    = 2;
  public static final int SERVERDATA_AUTH           = 3;
  ```

  — <https://github.com/Ketum-Git/PZ-Javacode/blob/main/PZ_42.20.2/zombie/network/RCONServer.java> (42.20.2);
  same in <https://github.com/piromasta/PZDecompiled/blob/master/zombie/network/RCONServer.java> (41.78)
- Framing matches the spec exactly: a 4-byte `ByteOrder.LITTLE_ENDIAN` size, then `size`
  bytes containing `id` (4), `type` (4), body, two NULs. Size excludes itself; PZ writes
  `bb.putInt(bb.capacity() - 4)`.
- It is a **from-scratch Java reimplementation** — plain `java.net.ServerSocket`, one
  `Thread` per client, no Valve code. Thread named `"RCONServer"`, per-client threads
  `"RCONClient<port>"`.
- Valve's spec, confirmed against the archived page: Size/ID/Type are "32-bit little-endian
  Signed Integer", body is a "Null-terminated ASCII String" plus a trailing empty string;
  `3 SERVERDATA_AUTH`, `2 SERVERDATA_AUTH_RESPONSE`, `2 SERVERDATA_EXECCOMMAND`,
  `0 SERVERDATA_RESPONSE_VALUE`; "maximum possible value of packet size is 4096"; auth
  failure ID = `-1 (0xFFFFFFFF)`. —
  <https://web.archive.org/web/2024id_/https://developer.valvesoftware.com/wiki/Source_RCON_Protocol>
  (rev 384118; the live page is behind a proof-of-work wall)
- Transport is **TCP**, per Valve's spec ("The Source RCON Protocol is a TCP/IP-based
  communication protocol… By default, SRCDS listens for RCON connections on TCP port
  27015") and PZ's own Build 32 changelog ("the server accepts console commands through a
  **TCP connection**"). — <https://pzwiki.net/wiki/Build_32>
- Lower-confidence corroboration: `gorcon/rcon` README — "Works for any game using the
  Source RCON Protocol. Tested on: Project Zomboid". — <https://github.com/gorcon/rcon>

### Configuration

| Key | Default | Range | Source |
| --- | --- | --- | --- |
| `RCONPort` | **27015** | 0–65535 | `ServerOptions.java:111`; <https://pzwiki.net/wiki/Server_settings> (42.20.0) |
| `RCONPassword` | **empty** | — | `ServerOptions.java:112`; <https://pzwiki.net/wiki/Server_settings> (42.20.0) |

**An empty password silently disables RCON; the server starts normally.** `GameServer.java:837`:

```java
if (rconPort != 0 && rconPwd != null && !rconPwd.isEmpty()) {
    RCONServer.init(rconPort, rconPwd, isLocal != null);
}
```

No listener is bound, no error, no refusal to start. `RCONPort=0` also disables it. Official
corroboration, Build 32.27 changelog: "Added RCON (remote console) support on the server…
There are two options in the server INI file, RCONPort and RCONPassword. **If RCONPassword is
left empty, then RCON is disabled.**" — <https://pzwiki.net/wiki/Build_32>

Two further facts relevant to credential handling:

- JVM property **`-Drconlo`** binds the RCON listener to `127.0.0.1` only; otherwise it binds
  `GameServer.IPCommandline` if set, else all interfaces.
- `RCONPort` and `RCONPassword` (along with `Password` and the Discord tokens) are removed
  from `publicOptions` (`ServerOptions.java:222-228`), so **`showoptions` never prints
  them** — but `changeoption` is *not* restricted to `publicOptions`, so RCON **can** write
  a new RCON password into the INI. It does not take effect on the running listener, because
  `RCONServer.password` is captured as `private final String` at init — no re-auth, no
  socket drop.

### Quirks and limits

**Confidence: High** where sourced to the 42.20.2 code; **Medium** that 42.20.3/.4 did not
change `handleResponse` again (only 41.78 and 42.20.2 were diffed, and it already changed
once between those).

1. **Response chunking changed between builds.**
   - **41.78: no split at all.** `handleResponse` allocates `12 + var2.length() + 2` and does
     one `out.write` — a 40 KB `showoptions` becomes a single oversized packet. Worse, the
     size field uses `String.length()` (char count) while the body is written with
     `getBytes()` (default charset bytes), so any non-ASCII response desyncs the client's
     framing. — `PZ_41.78/zombie/network/RCONServer.java:335-355`
   - **42.20.2: it does split**, into body chunks of **max 4086 bytes** (`12 + 4086 + 2 =
     4100`), all sharing the same `id` and `type = 0`, correctly UTF-8 encoded:
     `byte[] data = s.getBytes(StandardCharsets.UTF_8); while (sendBytes < data.length) {
     int size = Math.min(data.length - sendBytes, 4086); … }`
2. **There is no end-of-response sentinel.** PZ never emits Valve's terminator, so a client
   can only detect the last chunk by its being shorter than 4086 bytes. Lower-confidence
   corroboration naming this exactly — `cbrgm/rcon` README: "For servers like **Project
   Zomboid** that split large replies (e.g. `help`) but still mishandle the terminator, so
   read until the connection goes idle instead of waiting for a terminator", exposed as
   `WithReadUntilIdle(window)` with a default 100 ms window. — <https://github.com/cbrgm/rcon>
3. **An empty response sends no packet at all** in 42.20.2 — `while (sendBytes <
   data.length)` never executes for `data.length == 0`. Any command returning `""`/`null`
   leaves the client waiting until its own timeout. 41.78 always sent a 14-byte empty packet.
   This is the most consequential quirk for a control plane: "no response" is a valid empty
   result, not a fault. **Confidence: Medium** — this is a direct read of the loop condition,
   not an observed packet capture, and is the one item worth confirming against a live server.
4. **The Koraktor multi-packet probe does not work.** If a client sends a `type 0` packet,
   PZ's `case 0:` writes the same 14-byte empty type-0 packet **twice**
   (`this.out.write(bb.array()); this.out.write(bb.array());`). It does not mirror the
   request nor send Valve's `0x0000 0001 0000 0000` body, so the standard "send an empty
   RESPONSE_VALUE to find the end" trick cannot be used as a sentinel.
5. **The connection is kept alive.** `ClientThread.run()` loops until `read < 0` or a
   `SocketException`; **no `setSoTimeout`, no idle timeout, no per-command close.** One
   authenticated socket serves unlimited sequential commands.
6. **Auth failure sends id `-1`, then closes the socket.** `case 3:` compares with exact
   `String.equals` (case-sensitive, no trimming); on mismatch it logs `RCON: password
   doesn't match`, writes an empty type-0 packet followed by an AUTH_RESPONSE with `id = -1`,
   sets `quit = true`, and the socket closes. An unauthenticated `EXECCOMMAND` hits
   `checkAuth()`, which writes a type-2 packet with `id = -1` and likewise closes.
7. **Hard cap of 5 simultaneous connections.** `if (this.connections.size() >= 5) {
   socket.close(); return; }` — the 6th connection is *accepted then immediately closed*, so
   the client sees a connect that instantly EOFs rather than a refusal. Dead `ClientThread`s
   are reaped only on the next `accept()`, so a leaked socket occupies a slot until then.
   Identical in 41.78 and 42.20.2.
8. **Execution is serialized regardless of connection count.** Every `ExecCommand` from
   every connection is queued to a single `ConcurrentLinkedQueue toMain` and run on the main
   server tick via `GameServer.rcon()`; the client thread blocks in a `Thread.sleep(50)` poll
   loop awaiting its response. So each command costs at least one server tick plus up to
   50 ms, and **commands cannot be pipelined**.
9. **Trailing newline.** Multi-line commands switch separator on `connection == null`:
   `String var7 = " <LINE> "; if (this.connection == null) { var7 = "\n"; }`. Over RCON you
   get real `\n`; in-game you get the literal string ` <LINE> `. The separator is appended
   **after every entry including the last**, so responses carry a trailing newline.
10. **Argument quoting is mandatory.** `CommandBase`'s tokenizer is
    `Pattern.compile("([^\"]\\S*|\".*?\")\\s*")` and it strips all `"` characters from each
    token; combined with per-command `@CommandArgs` regexes, an unquoted multi-word argument
    becomes multiple tokens and fails to parse. Hence `servermsg "My Message"`. Command
    *names* are matched case-insensitively; argument values are not.
11. **Request bodies are decoded with the platform default charset**, not ASCII or UTF-8
    (`new String(bytes, off, len)`, 42.20.2 line 175).
12. **Input framing is not hardened.** The 4-byte length prefix is read with a single
    un-looped `in.read(bytes,0,4)`, so a TCP segment boundary inside the length prefix
    desyncs the stream; and `new byte[packetSize]` is allocated with no bound check on the
    caller-supplied size (negative → `NegativeArraySizeException`, huge → OOM attempt).
    Relevant to anything that proxies untrusted input to the RCON port.
13. **`players` is excluded from RCON debug logging** (`if (!"players".equals(body))`), so it
    can be polled without flooding the server log.
14. Two changelog-recorded fixes worth knowing: the `rconlo` loopback property, and a fix for
    "server hanging on quit command because of active RCON connection". —
    <https://pzwiki.net/w/index.php?search=RCON&title=Special%3ASearch&fulltext=1&ns0=1>

### RCON and the server console are the same surface

**Confidence: High.** Both paths converge on one function with a `null` connection:

- stdin: `GameServer.launchCommandHandler()` reads `System.in`, queues to `consoleCommands`,
  and the main loop calls `System.out.println(handleServerCommand(cmd, null))`
  (`GameServer.java:998`).
- RCON: `public static String rcon(String command) { return handleServerCommand(command, null); }`
  (`GameServer.java:1282`).

And `handleServerCommand` grants that caller full admin:

```java
String adminUsername = "admin";
Role accessLevel = Roles.getDefaultForAdmin();
if (connection != null) { … connection.getRole() … }
```

`CommandBase.isCommandComeFromServerConsole()` is literally `return this.connection == null`
— **PZ cannot distinguish RCON from stdin.**

Therefore **there is no command that is server-console-only but not RCON-reachable.** The
only real split is *console/RCON (`connection == null`)* versus *in-game chat*. Commands the
wiki marks "username is optional except from the server console" (`lightning`, `thunder`,
`createhorde`) simply **require an explicit username over RCON**, because there is no acting
player. No leading `/` is needed over RCON; unknown input returns `Unknown command <input>`.

The wiki agrees on the console side: "The admin commands can be executed either on the server
console window or in-game (preceded by a forward slash when used in-game) provided the user
has admin status." — <https://pzwiki.net/wiki/Dedicated_server> (42.20.0)

### Three commands are disabled in 42.20.2

**Confidence: High.** `addusertowhitelist`, `addalltowhitelist`, and `connections` carry
`@DisabledCommand` in 42.20.2. `findCommandCls` skips them, so they return `Unknown command`
— they are reachable **nowhere**, not over RCON, not from the console. This is why pzwiki's
42.20.2 table omits them. Verified via
`gh api search/code?q=DisabledCommand+repo:Ketum-Git/PZ-Javacode` and the annotations in
`AddUserToWhiteListCommand.java` / `AddAllToWhiteListCommand.java`.

### Operation matrix

Source key: **W** = <https://pzwiki.net/wiki/Admin_commands> (page version 42.20.2) ·
**C** = `PZ_42.20.2/zombie/commands/serverCommands/<Class>.java` under
<https://github.com/Ketum-Git/PZ-Javacode> · **S** = <https://pzwiki.net/wiki/Server_settings> (42.20.0)

| Operation | Reachable via | Command / key | Response parseable? | Source |
| --- | --- | --- | --- | --- |
| List players | RCON + console + in-game | `players` | **Yes.** `"Players connected (N): "` then one `-<username>` per line; `\n`-separated over RCON, trailing separator present | W; C `PlayersCommand` |
| Kick | RCON + console + in-game | `kickuser "user" -r "reason"` (alias `kick`) | Stable strings: `User X kicked.` / `User X doesn't exist.` / `This user can't be kicked.` | W; C `KickUserCommand` |
| Ban / unban user | RCON + console + in-game | `banuser "user" [-ip] [-r "reason"]` · `unbanuser "user"` | Prose | W; C `BanUserCommand`, `UnbanUserCommand` |
| Ban / unban Steam ID | RCON + console + in-game | `banid <SteamID>` · `unbanid <SteamID>` | Prose | W; C `BanSteamIDCommand`, `UnbanSteamIDCommand` |
| Ban / unban IP | RCON + console + in-game | `banip <IP>` · `unbanip <IP>` | Prose | W; C `BanIPCommand`, `UnbanIPCommand` |
| Whitelist: add | RCON + console + in-game | **`adduser "user" "password"`** — use this | Prose | W; C `AddUserCommand` |
| Whitelist: `addusertowhitelist` | **NOWHERE — disabled** | returns `Unknown command` | — | C `AddUserToWhiteListCommand` (`@DisabledCommand`) |
| Whitelist: `addalltowhitelist` | **NOWHERE — disabled** | returns `Unknown command` | — | C `AddAllToWhiteListCommand` (`@DisabledCommand`) |
| Whitelist: remove | RCON + console + in-game | `removeuserfromwhitelist "user"` | Prose | W; C `RemoveUserFromWhiteList` |
| Whitelist **mode** | **config**, or `changeoption` over RCON | `Open=true` (default). "Clients may join without already having an account in the whitelist. If set to false, administrators must manually create username/password combos." Runtime: `changeoption Open false` | `Option : Open is now : false` | S; `ServerOptions.java:42`; C `ChangeOptionCommand` |
| Set a user's password | RCON + console + in-game | `setpassword "user" "newpassword"` | Prose | W; C `SetPasswordCommand` |
| Steam-ID allowlist | RCON + console + in-game | `addsteamid "steamid"` · `removesteamid "steamid"` | Prose | W; C |
| Server-wide message | RCON + console + in-game | `servermsg "My Message"` — **must be quoted** | `Message sent.` Over RCON the broadcast has **no author**; in-game it is attributed | W; C `ServerMessageCommand` |
| Save world | RCON + console + in-game | `save` | `World saved` | W; C `SaveCommand` |
| Quit / shutdown | RCON + console + in-game | `quit` ("Save and quit the server") | `Quit`, then the process exits and the socket dies | W; C `QuitCommand` |
| Grant admin / moderator | RCON + console + in-game | `setaccesslevel "user" "level"`; also `grantadmin` / `removeadmin` (real commands, **absent from pzwiki**) | Whatever `GameServer.changeRole` returns; `"none"` clears the level | W; C `SetAccessLevelCommand`, `GrantAdminCommand`, `RemoveAdminCommand` |
| Mod update check | RCON + console + in-game | `checkModsNeedUpdate` | **No.** Returns only "Checking started. The answer will be written in the log file and in the chat" — the verdict never reaches the RCON caller | W; C `CheckModsNeedUpdate` |
| Reload options | RCON + console + in-game | `reloadoptions` ("Reload server options (ServerOptions.ini) and send to clients") | `Options reloaded` | W; C `ReloadOptionsCommand` |
| Change option | RCON + console + in-game | `changeoption <name> "<value>"` — persists to the INI | `Option : K is now : V` / `Option K doesn't exist.` | W; C `ChangeOptionCommand`; `ServerOptions.java:367` |
| Show options | RCON + console + in-game | `showoptions` | Semi-parseable: `List of Server Options:` then `* Key=Value` lines. **Omits `Password`, `RCONPort`, `RCONPassword`, Discord tokens** | W; C `ShowOptionsCommand` |
| `connections` / `disconnect` | **NOWHERE in 42.20.2** | Existed in B32; `ConnectionsCommand` is now `@DisabledCommand` and no `disconnect` class exists. `list` exists but shows as `WIP: UI_ServerOptionDesc_List` | — | <https://pzwiki.net/wiki/Build_32>; W; C |
| Mods (`WorkshopItems=`, `Mods=`) | **config only**, then restart | INI keys | — | S |

Access levels in 42.20.2: `user, priority, observer, gm, moderator, admin`. The 41.78 wiki
listed `Admin, Moderator, Overseer, GM, Observer`. — W (42.20.2);
<https://pzwiki.net/w/index.php?title=Admin_commands&oldid=648141> (41.78.16)

Full 42.20.2 RCON-reachable set (66 dispatchable classes, identical over RCON and stdin),
from `CommandBase.childrenClasses` plus the `serverCommands` directory listing:
`additem, addkey, addsteamid, addtosafehouse, adduser, addvehicle, addxp, alarm, banid,
banip, banuser, changeoption, checkModsNeedUpdate, chopper, clear, createhorde, createhorde2,
debugplayer, godmode, godmodeplayer, grantadmin, gunshot, help, invisible, invisibleplayer,
kick/kickuser, kickfromsafehouse, lightning, list, log, noclip, players, quit,
releasesafehouse, reloadalllua, reloadlua, reloadoptions, remove, removeadmin, removeitem,
removemapsymbolsforuser, removesteamid, removeuserfromwhitelist, removezombies, save,
servermsg, setaccesslevel, setpassword, settimespeed, showoptions, startrain, startstorm,
stats, stoprain, stopweather, teleport, teleportplayer, teleportto, thunder, unbanid, unbanip,
unbanuser, voiceban, worldgen`. pzwiki **omits** `grantadmin`, `removeadmin`, `clear`,
`debugplayer`, `settimespeed` from its table.

An additional non-RCON control channel is officially documented: a systemd FIFO
(`ListenFIFO=/opt/pzserver/zomboid.control`) into the server's stdin, used as
`echo "command" > /opt/pzserver/zomboid.control`, with `ExecStop` doing `echo save`, sleep 15,
`echo quit`. — <https://pzwiki.net/wiki/Dedicated_server> (42.20.0)

---

## 8. Redistribution constraints

**Confidence: High on the terms; Medium on the practical conclusion (this is a reading of the
terms, not legal advice).**

### The Indie Stone's own EULA is decisive

The official PZ EULA is hosted by Valve as "Project Zomboid - Terms and Conditions" and is
associated with app 108600 in Valve's metadata (`"name" "Project Zomboid EULA"`, seen in
`app_info_print 108600`). — <https://store.steampowered.com/eula/108600_eula_1> (verified 2026-09-10)

- **§3.2, in the "you are not permitted to" section: "*Distribute Project Zomboid yourself,
  or host its download. In order to ensure the game's integrity we recommend it should only
  ever be downloaded from established portals on which we've placed it (e.g. Steam or
  GOG).*"** Baking PZ server binaries into a distributed Docker image is hosting its
  download, and is prohibited.
- **§2.1, the licence grant, closes the obvious loophole**: "*Change or distribute the base
  files or contents in any way you like, provided that those changes do not result in you
  making Project Zomboid available to play or download (this includes making it available
  open source)…*"
- §3.1 prohibits modifying the base files "to include malicious code or other naughtiness".
- §4.1: "*If you are a server owner then charging those who play on it is, of course,
  totally fine.*" §4.2: The Indie Stone "*does NOT encourage or allow server owners to charge
  for specific items, mods or gameplay that is made and distributed exclusively for said
  server.*" So **operating a server, even commercially, is fine; redistributing the files is
  not.**
- Escalation path they explicitly offer: "*If you have any doubts or questions about whether
  you can or can't do something, contact us first at info@theindiestone.com instead of just
  doing/not doing it!*"
- The `projectzomboid.com/blog/support/terms-conditions/` mirror returned 403, so its
  last-updated date **could not be verified**; the Valve-hosted copy is the one users accept.

### Steam Subscriber Agreement

— <https://store.steampowered.com/subscriber_agreement/> (verified 2026-09-10; revision line
reads "This Agreement was last updated on **September 10, 2026**")

- Licence scope: "*Valve hereby grants, and you accept, a non-exclusive license and right, to
  use the Content and Services for your personal, non-commercial use.*"
- Prohibitions: "*you may not, in whole or in part, copy, photocopy, reproduce, publish,
  distribute, translate, reverse engineer, derive source code from, modify, disassemble,
  decompile*"; nor "*sell, grant a security interest in or transfer reproductions of the
  Content and Services to other parties in any way, nor to rent, lease or license the Content
  and Services to others*".
- §2.E covers **Valve** Dedicated Server Software: "*you may use the Valve Dedicated Server
  Software on an unlimited number of computers for the purpose of hosting online multiplayer
  games of Valve products.*" **Note the scope: "Valve products".** This clause grants
  unlimited *hosting*, not redistribution, and PZ is not a Valve product — so it does not
  help here.

### Steamworks

- The Steamworks dedicated-server distribution doc was fetched in full. It covers TOOL app
  creation, `Installation -> Redistributables -> Dedicated Server Redistributables`, the
  `steam_appid.txt` requirement ("*a `steam_appid.txt` file, which contains only your game's
  AppID. Include that file with your dedicated server package*"), and the pkg 17906 anonymous
  path — but **contains no redistribution grant, no third-party hosting policy, and no
  licensing terms**. — <https://partner.steamgames.com/doc/sdk/uploading/distributing_gs>
- **Could not verify** the Steamworks *Distribution Agreement* text (partner-only). Any claim
  about what publishers are contractually permitted to allow is unverifiable from public
  sources. The EULA §3.2 conclusion does not depend on it.

### What existing PZ Docker images do

**Lower confidence — third-party observation, one repo inspected end-to-end.**

- `Renegade-Master/zomboid-dedicated-server` **does not embed PZ files**. Its Dockerfile is
  `FROM docker.io/renegademaster/steamcmd-minimal:2.0.0-root`, installs only
  `python3-minimal iputils-ping tzdata`, copies scripts, and sets
  `ENTRYPOINT ["/bin/bash", "/home/steam/run_server.sh"]` — the server is fetched **at
  container start**. —
  <https://raw.githubusercontent.com/Renegade-Master/zomboid-dedicated-server/main/docker/zomboid-dedicated-server.Dockerfile>
- Its entrypoint calls `steamcmd.sh +runscript "$STEAM_INSTALL_FILE"` in an `update_server()`
  function and rewrites the branch at runtime with
  `sed -i "s/beta .* /beta $GAME_VERSION /g"`. Mods are injected into the INI from
  `MOD_WORKSHOP_IDS` and `MOD_NAMES` environment variables. —
  <https://raw.githubusercontent.com/Renegade-Master/zomboid-dedicated-server/main/src/run_server.sh>
- Its SteamCMD script, verbatim: `@ShutdownOnFailedCommand 0`, `@NoPromptForPassword 1`,
  `force_install_dir /home/steam/ZomboidDedicatedServer`, `login anonymous`,
  `app_update 380870 -beta GAME_VERSION validate`, `quit`. —
  <https://raw.githubusercontent.com/Renegade-Master/zomboid-dedicated-server/main/src/install_server.scmd>
- Pattern confirmed in one repo, and it is the pattern §3.2 requires: ship SteamCMD plus
  orchestration in the image, download 380870 from Valve at container start into a mounted
  volume. `Danixu/project-zomboid-server-docker` was found in search but not fetched — do not
  treat "all images do this" as verified.

**Bearing on PRD 22.** PRD 22 says server files "should normally be obtained from Steam
through SteamCMD rather than embedded into the distributable image **unless redistribution
rights and operational requirements clearly support another approach**." The escape clause is
closed: EULA §3.2 prohibits distributing or hosting PZ's download, and §2.1's proviso closes
the modified-files route. There is no public source granting redistribution rights, so the
"unless" branch cannot be satisfied without a written exception from
`info@theindiestone.com`. Also relevant operationally: the payload is ~6.72 GiB, which is a
poor image layer regardless.

---

## 9. Where the PRD's assumptions do not hold

| PRD | Assumption | Finding |
| --- | --- | --- |
| **28** | Models "Steam/query requirements" as a distinct concern, and "additional player ports where required" | **Both are obsolete.** There is no separate Steam query port — Steam queries ride on 16261/UDP since 41.77. Per-player ports were retired at 41.65 and were TCP, never UDP. The current set is exactly **16261/UDP + 16262/UDP** to publish, plus 27015/TCP kept private. A per-server port model needs **two UDP ports per instance**, nothing per player. |
| **36** | Lists "whitelist" as a supported administrative mechanism | **`addusertowhitelist` and `addalltowhitelist` are `@DisabledCommand` in 42.20.2 and reachable nowhere** — not over RCON, not from the console. Whitelist *addition* must go through `adduser "user" "password"`; whitelist *mode* is the `Open` INI key (settable at runtime via `changeoption Open false`). Removal (`removeuserfromwhitelist`) still works. |
| **29 / 30 / 37** | RCON is a normal request/response protocol that a library connects, authenticates, executes and parses | Directionally right — it is Source RCON — but with four behaviours a naive client gets wrong: **no end-of-response terminator** (drain until idle, ~100 ms); **an empty result sends no packet at all**, so "silence" is a valid empty response and not a timeout; a **hard cap of 5 concurrent connections**, with the 6th accepted-then-closed rather than refused; and **serialized execution** on the main server tick with a 50 ms poll, so ≥1 tick of latency per command and no pipelining. Also: responses are prose except `players` and (partly) `showoptions`. |
| **32** | "server INI, Sandbox configuration, spawn configuration… Structured editing is preferred" | The file set is right, but **three of the four files are Lua, not INI** — `<name>_SandboxVars.lua`, `<name>_spawnpoints.lua`, `<name>_spawnregions.lua`, with nested tables. Only `<name>.ini` is key=value. Structured editing means a Lua-table reader/writer, not an INI parser. Upside: all four are auto-generated on first run, and the INI is live-reloadable via `reloadoptions`. |
| **34** | "Workshop item identity and Project Zomboid Mod ID shall be modeled separately" | **Correct, and stronger than stated.** Verified live: one Workshop item routinely contains several mods (3 in item 2335368829, 2 in 2822286426). Additionally the **mod folder name is not the Mod id** (folder `RV_Interior` → `id=RV_Interior_MP`), Mod ids may contain **spaces**, and in Build 42 one mod carries **two `mod.info` files** (root for B41, `42/` for B42) whose dependency and compatibility data differ. `require` and `incompatible` are real `mod.info` keys, so PRD 34's "dependencies/compatibility where discoverable" is achievable. |
| **22** | Embedding server files is permitted "unless redistribution rights… clearly support another approach" | **The escape clause cannot be satisfied.** PZ EULA §3.2 prohibits distributing PZ or hosting its download; §2.1's proviso closes the modified-files route. SteamCMD-at-runtime is not a preference here, it is the only compliant option absent a written exception. |
| **23** | Logical layout `/pz/{server,runtime,data/{config,saves,logs,workshop,backups}}` | Compatible, with two frictions worth knowing: the **Workshop cache lives under the Steam root** (`steamapps/workshop/content/108600/`), not under the PZ install or the user data directory, so `data/workshop` is a relocation rather than a passthrough; and **`config`, `saves`, `logs` and the player `db` all live under a single `Zomboid/` user-data root** that moves as one unit via `-cachedir=`, so splitting them into separate mounts means either separate bind mounts inside one tree or symlinks. |
| **31** | SteamCMD lifecycle: Missing → Installing → Validating → Installed → Ready | Workable, but **SteamCMD's exit codes are undocumented by Valve** (their own tracker issue, open since 2016, never answered). Observed: `0` success, `7` self-update-restart-needed on Windows, `42` the magic restart code on Linux. Reliable state detection needs stdout parsing (`Success! App '<id>' fully installed` / `Error! App '<id>' state is 0x…`) alongside the exit code. Also: `-Dsoftreset` "does not work as of 42.20.4", and the devs **explicitly discourage** SIGTERM-based shutdown, documenting a stdin FIFO with `save` then `quit` instead. |

Assumptions that held up, for completeness: RCON exists, is TCP, defaults to 27015, and is
disabled by an empty password (PRD 29's "private by default" is the shipped default);
`WorkshopItems=`/`Mods=` are config-file-only and need a restart (PRD 32's mod configuration
belongs with the INI, not RCON); and anonymous SteamCMD works for both the server app and
Workshop content, so no Steam credentials need to be held anywhere.

---

## 10. Not verified

Ordered by how much it would matter.

1. **The 42.20.2 "empty response sends no packet" quirk was not observed on the wire.** It is
   a direct read of the loop condition in decompiled code. Worth one live-server test before
   anything depends on distinguishing "empty result" from "timeout".
2. **Whether 42.20.3 / 42.20.4 changed `handleResponse` again.** Only 41.78 and 42.20.2 were
   diffed, and it already changed once between those. The 4086-byte chunking is verified at
   42.20.2 only.
3. **No official dedicated-server system requirements exist.** App 380870 has no store page;
   every RAM-per-player and "single-thread bound" figure available is from hosting vendors.
   The one official RAM number (16 GB `-Xms`/`-Xmx` in `StartServer64.bat`) is a ship default
   and its wiki sentence is unchanged since the 41.78 era, so it may be stale.
4. **That `SteamPort1`/`SteamPort2` are removed from the 42.20.4 binary**, as opposed to
   removed from the docs. No changelog entry announcing removal was found, and
   `insource:"8766"` returns zero hits across pzwiki's main namespace.
5. **The format of `Zomboid/db/<servername>`.** SQLite is the widespread assumption; no
   primary source confirms the format, the extension, or definitively that it is what stores
   accounts, whitelist and bans.
6. **Exact log file locations and rotation.** `server-console.txt` sits in the `Zomboid`
   folder and a `Logs` folder exists, both officially. Which files land in `Logs/` (`cmd.txt`,
   `ClientActionLogs.txt`, `PerkLog.txt` are documented by name via their INI settings) and
   whether they are date-rotated is unconfirmed.
7. **The separator for `Mods=`.** Semicolon is the community convention and matches
   `WorkshopItems=`, but the `Server_settings` page does not state it.
8. **`Zomboid/mods/` as the manual-mod location.** Widely used, not confirmed by any primary
   source read here.
9. **Current Linux native-library requirements for the 42.20.4 server itself**, as distinct
   from SteamCMD's i386 dependencies. No official dependency list exists; the
   `LD_PRELOAD`/libjsig detail comes from a 2023 (Build 41) user bug report.
10. **`developer.valvesoftware.com` was never read live** (Anubis proof-of-work wall).
    Anything the SteamCMD wiki page uniquely documents is unconfirmed: exact per-distro 32-bit
    package names (`lib32gcc-s1` vs `lib32gcc1`), the canonical `+`-argument ordering, and
    `-betapassword`. The Source RCON spec came from a 2024 `web.archive.org` capture
    (rev 384118). Substitutes used: direct artifact inspection and live client runs.
11. **`-betapassword` syntax** — unverified, though `"privatebranches" "1"` on both apps
    confirms password-protected branches exist.
12. **That the PZ dedicated server uses the anonymous Steam *game server* identity** for its
    own Workshop fetches. Inferred from Valve's `ISteamUGC::DownloadItem` contract plus the
    absence of any credential field in the server INI; no Indie Stone statement found.
13. **Whether Workshop fetching is genuinely asynchronous** such that a newly added
    `WorkshopItems=` entry needs two restarts. Hosting-vendor claim only.
14. **Any per-IP rate limit on keyless `GetPublishedFileDetails`.** The documented 100,000
    calls/day is expressed per API key, and this endpoint takes no key.
15. **No Indie Stone statement that PZ implements Source RCON.** pzwiki has no RCON page
    (`/wiki/RCON` is a 404); the Build 32 changelog says only "console commands through a TCP
    connection". The identification rests on the decompiled Valve constants plus
    `gorcon/rcon`'s "Tested on: Project Zomboid".
16. **`theindiestone.com` returns 403** to automated fetches, so official forum threads on
    RCON tooling and the SIGTERM discussion were reachable only as pzwiki citations, not read
    directly.
17. **`RCONPassword` maximum length or character restrictions.**
    `StringServerOption(…, "", -1)` suggests unbounded; `StringServerOption` was not read.
18. **Whether an idle RCON socket is reaped by anything outside `RCONServer`** (OS keepalive,
    host-provider proxy). No application-level timeout exists; infrastructure behaviour
    untested.
19. **Whether the `unstable` Steam branch still exists as a selectable channel.** It is absent
    from both apps' branch lists as read from Valve's metadata, yet projectzomboid.com still
    renders an "Unstable Build" header field (currently equal to stable).
20. **Official activity after 42.20.4.** The newest projectzomboid.com news post is 6 Aug
    2026; nothing was published between then and 2026-09-10, so there is no evidence of a
    42.20.5+ or of the promised "Build 42 Support Update" having landed.
