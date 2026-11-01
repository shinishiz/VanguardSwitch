# Testing

Results of one manual test session of VanguardSwitch on a real PC with Riot Vanguard installed.
Only what was actually run is listed as passed or failed. Nothing here says the app works on other
Windows versions, languages, screen scales or games.

## How to read this

Each test is one of:

- **Real**: the action was done through VanguardSwitch on the real Vanguard install, and the result was
  checked with tools that don't use the app's code (see [Independent checks](#independent-checks)).
- **Manual (tester)**: a step the tester did by hand (opening a game, the UAC prompt) and reported. When
  logs could back it up, the log evidence is listed too.
- **Simulated**: mocks in [`tests/Simulated`](tests/Simulated). No Vanguard service or driver is involved.

Status is **Passed**, **Failed**, **Blocked** or **Not run**. Building the app and the simulated tests
don't prove anything about the real Vanguard; they're kept apart below.

## Environment

| | |
| --- | --- |
| Date | 2026-11-01 |
| Commit | `3c0628751221ed7704d08597d67634f3ecb163dc` (tag `v1.0.5`) |
| App version | 1.0.5 (file version 1.0.5.0, product version `1.0.5+3c06287`) |
| Tested exe | `out/VanguardSwitch.exe`, local Release build, 462,336 bytes, SHA-256 `d7d1b76861364a8da92836060888e64fa6cddec8537c479b28efe8b7aa4f269b` |
| Release exe | The v1.0.5 release asset has SHA-256 `cc10a9ecdcee4880081b3806c1efbaee99fc0799bbead635a351051ee428acd8`. It's a different build (GitHub Actions, other SDK patch), so the hashes differ. The release exe was only used as the update target in 7.3 |
| Build tools | .NET SDK 8.0.427 (portable install, nothing installed system-wide) |
| Windows | Windows 10 Pro 22H2, build 19045.6466, x64, Portuguese (Brazil) display language |
| .NET Framework | 4.8.1 (release 533325) |
| Screen | 1920×1080 at 100% (96 DPI) |
| Boot | UEFI, Secure Boot off, Fast Startup off |
| Vanguard mode | Traditional. Windows 10 doesn't qualify for On-Demand |
| Riot software installed | Riot Client, League of Legends, Teamfight Tactics, VALORANT |
| Games launched in the tests | VALORANT, League of Legends |

State before the first run (restored at the end, see [Restoring the PC](#restoring-the-pc)):

| | |
| --- | --- |
| `vgk` | `SYSTEM_START` (1), running, loaded at boot (newest `vgk_*.log` 2 s after boot) |
| `vgc` | `DEMAND_START` (3), stopped (set by hand by the tester before the session) |
| Tray | `HKLM\…\Run` value `Riot Vanguard` present, `StartupApproved` first byte `02` (enabled), `vgtray` running |
| App state | `%ProgramData%\VanguardSwitch` didn't exist (the app had never run on this PC) |

## Independent checks

After each step the state was read without VanguardSwitch:

- `sc.exe query` / `sc.exe qc` for `vgc` and `vgk`, and the `Start` value in the registry
- the kernel's loaded-driver list (`EnumDeviceDrivers`, looking for `vgk.sys`) and `driverquery /v`
- running processes, with the integrity level of Riot Client and `vgtray`
- the `StartupApproved\Run` bytes for the tray entry
- Windows event log: Service Control Manager 7040 (start type changed), event 1074 (who restarted the PC)
- creation time of Vanguard's `Logs\vgk_*.log` compared with the boot time
- Riot Client, VALORANT (`ShooterGame.log`) and League (`GameLogs`) logs to back up the game tests

The window was driven with synthetic mouse clicks. Screenshots and the text of the app's own log were kept
as evidence, but the app's log and messages were never taken as proof on their own.

## Summary

| # | Test | Type | Status |
| --- | --- | --- | --- |
| 1.1 | Release build | Build | Passed |
| 1.2 | Admin prompt (UAC) | Manual (tester) | Passed |
| 1.3 | First reading of the state | Real | Passed |
| 2.1 | Turn off, keep the driver loaded | Real | Passed |
| 2.2 | Nothing turns it back on (70 s) | Real | Passed |
| 2.3 | Close and reopen: state recognized | Real | Passed |
| 3.1 | Turn on without restarting | Real | Passed |
| 3.2 | Open Riot Client from the app, not elevated | Real | Passed |
| 3.3 | Games after Turn on | Manual (tester), backed by logs | Passed (short sessions, see notes) |
| 3.4 | Four off/on cycles | Real | Passed |
| 4.1 | Five quick clicks on Turn off | Real | Passed |
| 4.2 | Turn on, then Turn off clicked during the operation | Real | Passed |
| 4.3 | Controls locked during operations, unlocked after | Real | Passed |
| 4.4 | "Game running" notice: No | Real (game opened by tester) | Passed |
| 4.5 | "Game running" notice: Yes | Real (game opened by tester) | Passed |
| 4.6 | "Vanguard is off" notice on Open Riot Client: No | Real | Passed |
| 5.1 | Full turn off (driver unloaded) | Real | Passed |
| 5.2 | Driver refuses to unload → restart prompt | Real | Not run (the driver always unloaded) |
| 5.3 | Turn on after full turn off: late-start and VAN 57 warnings | Real | Passed |
| 5.4 | VAN 57 really happens in that state | Manual (tester), backed by logs | Passed (the warning was right) |
| 5.5 | Restart from the app's dialog | Real | Passed |
| 5.6 | After restart: driver from boot, games work | Real + Manual (tester), backed by logs | Passed (short sessions) |
| 6.1 | Riot default: No | Real | Passed |
| 6.2 | Riot default and restart: Yes | Real | Not run (tester chose not to restart again) |
| 7.1 | Layout and text at 100% | Real | Passed, with minor issues |
| 7.2 | Other screen scales | | Not run |
| 7.3 | Update v1.0.4 → v1.0.5 in a throwaway folder | Real | Passed |
| 7.4 | Update with a missing/invalid/wrong checksum or missing file | Simulated | Passed |
| 7.5 | Window state with Vanguard missing, `vgc` missing, unreadable status; restart text | Simulated | Passed |
| 7.6 | English UI | | Not run |
| 8 | Restart with `vgc` blocked, then Turn on (fixed build) | Real + Manual (tester), backed by logs | Passed (short sessions) |

## 1. Build and first start

### 1.1 Release build — Build — Passed

```sh
dotnet build src/VanguardSwitch.csproj -c Release -o out -p:Version=1.0.5
```

- Expected: builds with no errors.
- Observed: 0 warnings, 0 errors. The exe's manifest has `requireAdministrator` and `dpiAware=true`.

### 1.2 Admin prompt — Manual (tester) — Passed

- Steps: from Explorer, as a normal user, double-click `out\VanguardSwitch.exe`.
- Expected: Windows asks for admin rights.
- Observed (tester): the UAC prompt appeared and the app opened after accepting.

### 1.3 First reading — Real — Passed

- Steps: open the app (from an elevated session).
- Expected: rows match the real state.
- Observed: "O Vanguard está ligado"; Driver *Carregado*, loaded at boot *Sim*, Service *Parado*, Tray
  *Aberto*, Riot Client and games *Fechados*, When Windows starts *O Vanguard liga junto*. This matches
  `sc.exe`, the driver list and the startup entry. No update notice (1.0.5 was the latest release).

## 2. Turn off, keeping the driver loaded

### 2.1 Turn off — Real — Passed

- Before: `vgk` loaded since boot, `vgc` running, tray open, no game running.
- Steps: *Manter o driver carregado* on → **Desligar Vanguard**.
- Expected: Riot processes closed, `vgc` stopped and `DISABLED`, `vgk` still loaded at `SYSTEM_START`,
  tray disabled at startup.
- Observed (independent check, 9 s after the click):
  `vgc STOPPED DISABLED`, `vgk RUNNING SYSTEM_START`, `vgk.sys` in the loaded-driver list, no Riot or
  Vanguard process, `StartupApproved` byte `03` (disabled). One event 7040 (`vgc` automatic → disabled).
  The window showed "O Vanguard está desligado", Service *Parado e bloqueado*, When Windows starts
  *Só o driver*.

### 2.2 No reactivation — Real — Passed

- Steps: check `vgc` and the processes every 5 s.
- Observed: `vgc` stayed `STOPPED`/`4` and no `vgc`, `vgtray` or Riot Client process appeared for about 70 s.
  Riot Client wasn't running during this check; whether an open Riot Client tries to start or repair a
  blocked `vgc` wasn't tested.

### 2.3 Reopen — Real — Passed

- Steps: close the app, open it again.
- Observed: same state and same rows as after 2.1.

## 3. Turn on without restarting

### 3.1 Turn on — Real — Passed

- Before: state from 2.1.
- Steps: **Ligar Vanguard**.
- Expected: `vgk` stays at boot start, `vgc` automatic and running, tray back at startup and open.
- Observed: `vgc RUNNING AUTO_START`, `vgk RUNNING SYSTEM_START` (still the boot instance, no new
  `vgk_*.log`), `vgc` and `vgtray` running, `StartupApproved` byte `02`. The log said "já estava na
  memória desde o boot".

Note: `vgc` was `DEMAND_START` (3) at the start only because the tester had set it that way by hand.
Turn on sets it to `AUTO_START` (2), as intended.

### 3.2 Open Riot Client — Real — Passed

- Steps: **Abrir Riot Client** while Vanguard is on.
- Expected: no warning, Riot Client opens as a normal user.
- Observed: no dialog; `RiotClientServices` and `vgtray` ran at Medium integrity, the app at High.

### 3.3 Games after Turn on — Manual (tester), backed by logs — Passed (short sessions)

- Steps: after four off/on cycles with the driver kept loaded (no restart), the tester opened VALORANT and
  League of Legends and joined a game in each.
- Tester's report: VALORANT Deathmatch and League Practice Tool worked with no error.
- Logs: VALORANT connected to a Deathmatch server and returned to the menu about 18 s later, after opening
  the in-game settings (left on purpose). No Vanguard error in `ShooterGame.log`. League reached
  `GAMESTATE_GAMELOOP` and exited with `Quit Game Confirmed` about 23 s later.
- What this shows: both games got into a match with the driver kept loaded across turn off/on. It doesn't
  show that a full match works.

### 3.4 Repeated cycles — Real — Passed

- Steps: four Turn off → Turn on cycles (including the ones in section 4).
- Observed: each step gave the expected state in the independent check, and the app's log had no `[!!]`.

## 4. Repeated clicks and cancelling

### 4.1 Quick clicks on Turn off — Real — Passed

- Steps: with Vanguard on, five clicks on **Desligar Vanguard** in about 0.5 s.
- Expected: one operation.
- Observed: one "Fechando Riot…" block in the app's log and one event 7040.

### 4.2 Click Turn off during Turn on — Real — Passed

- Steps: **Ligar Vanguard**, then three clicks on **Desligar Vanguard** 0.6 s later.
- Observed: only Turn on ran (one event 7040, disabled → automatic). Final state on.

### 4.3 Locked controls — Real — Passed

- Observed in screenshots: during an operation all four buttons and the toggle are greyed out and the
  status says "Aguarde…". They come back when it ends, also after a cancelled dialog.

### 4.4 "Game running": No — Real — Passed

- Before: VALORANT open in the main menu (opened by the tester), Vanguard on.
- Steps: **Desligar Vanguard** → dialog "Jogo aberto … (VALORANT-Win64-Shipping). Fechar mesmo assim?" →
  **Não**.
- Expected: nothing is closed or changed.
- Observed: same VALORANT and `vgc` process ids, `vgc` still `AUTO_START`/running, tray still enabled,
  nothing in the app's log, buttons unlocked.

### 4.5 "Game running": Yes — Real — Passed

- Steps: same as 4.4, then **Sim** (the tester agreed to close the game).
- Observed: VALORANT, Riot Client and `vgtray` closed, state as in 2.1. The log also listed
  `EpicWebHelper`: that's `VALORANT\live\Engine\Binaries\Win64\EpicWebHelper.exe`, with a valid
  Riot Games signature, so closing it is correct. The Epic Games Launcher installed on the same PC
  wasn't touched.

### 4.6 "Vanguard is off" on Open Riot Client: No — Real — Passed

- Before: Vanguard off (2.1).
- Steps: **Abrir Riot Client** → "Vanguard desligado … Abrir o Riot Client mesmo assim?" → **Não**.
- Observed: Riot Client didn't open after 9 s and the state didn't change.

## 5. Full turn off and VAN 57

### 5.1 Full turn off — Real — Passed

- Before: Vanguard on, `vgk` loaded since boot.
- Steps: turn *Manter o driver carregado* off (writes `light=0` to `settings.ini`) → **Desligar Vanguard**.
- Expected: `vgk` and `vgc` disabled; driver unloaded or a restart prompt if it can't unload.
- Observed: `vgk STOPPED DISABLED` and `vgk.sys` gone from the loaded-driver list, `vgc STOPPED DISABLED`,
  tray disabled. Two events 7040. The window: "O driver também foi descarregado…", Driver *Não carregado*,
  When Windows starts *Nada liga*.

### 5.2 Driver refuses to unload — Not run

On this PC `vgk` always unloaded, so the "restart?" prompt for a stuck driver never appeared.

### 5.3 Turn on after a full turn off — Real — Passed

- Steps: **Ligar Vanguard** → prompt "Reiniciar o PC? O driver ligou agora, mas não junto com o Windows…
  pode causar o erro VAN 57" → **Não**.
- Expected: driver started now, VAN 57 warning, no restart unless asked.
- Observed: Vanguard created a new `vgk_2026-11-01_02-43-00.log`, 85 min after boot (driver started late).
  `vgk`/`vgc` running, `vgk SYSTEM_START`, `vgc AUTO_START`, tray enabled. No `shutdown.exe` was started.
  The window: "Reinicie para jogar", loaded at boot *Não (pode dar VAN 57)*. After closing and reopening the
  app it still showed this.

### 5.4 VAN 57 in that state — Manual (tester), backed by logs — Passed

- Steps: open VALORANT from Riot Client.
- Tester's report: error VAN 57.
- Logs: `ShooterGame.log` has `Marking Platform Faulted for Reason: VanguardNeedsReboot`.
- Note: Riot Client's own log said `restartRequired=false, restartWaivedReason=ON_BOOT` both before and
  after the late start, so that line can't tell the two apart.

### 5.5 Restart from the app — Real — Passed

- Steps: **Ligar Vanguard** again → same prompt → **Sim** (the tester saved their work and agreed).
- Observed: event 1074 from `shutdown.exe`, type restart, comment "VanguardSwitch". The PC restarted.
- Minor: this time the driver was already in memory (log: "já estava na memória, mas foi ligado depois do
  boot"), but the prompt still says "O driver ligou agora". See [issue 1](#issues-found).

### 5.6 After the restart — Real + Manual (tester), backed by logs — Passed (short sessions)

- Observed: new `vgk_*.log` 2 s after boot, `vgk` and `vgc` running, tray enabled and open. The app showed
  "O Vanguard está ligado", loaded at boot *Sim*, even though its `boot.ini` still held a mark from the
  previous boot, which is correctly ignored.
- Tester's report: matches in VALORANT and League both worked.
- Logs: VALORANT joined a Deathmatch server and returned to the menu 11 s later, with no
  `VanguardNeedsReboot`. League reached `GAMESTATE_GAMELOOP` and quit 6 s later. Short sessions, not full
  matches.

## 6. Riot default

### 6.1 Cancel — Real — Passed

- Before: Vanguard off with the driver kept (toggle back on): `vgc` disabled, tray disabled.
- Steps: **Padrão da Riot e reiniciar** → **Não**.
- Observed: no event 7040, no `shutdown.exe`, same state, nothing in the app's log.

### 6.2 Restore and restart — Not run

The tester chose not to do a second restart. So it wasn't checked here whether the settings are really
restored.

## 7. Interface and updates

### 7.1 Layout at 100% — Real — Passed, with minor issues

Main window, Logs panel, "Como funciona" and every dialog seen in these tests: no cut-off text, no overlaps,
buttons aligned. See minor issues 2 and 3 below.

### 7.2 Other scales — Not run

The app is system-DPI-aware, so a new scale needs signing out and back in. The tester chose to skip it.

### 7.3 Update — Real — Passed

1. In an empty throwaway folder, download the v1.0.4 `VanguardSwitch.exe` and `.sha256` from Releases.
   The hash matched (`41b24695…`).
2. Run it (no other copy of the app open). It showed "Nova versão disponível — 1.0.5 (você tem a 1.0.4)".
3. **Atualizar**.

Observed: `VanguardSwitch.exe.new` appeared, the old process exited, and a new process started from the
same path with `--updated` about 3 s after the click. The exe in the folder now has the v1.0.5 release
SHA-256 (`cc10a9ec…`) and product version `1.0.5+3c06287`. No `.new` or `.old` was left. The new window
showed v1.0.5, read the state correctly and didn't offer another update.

Limit: the update runs the old version's code. `Updater.cs` and `Program.cs` are the same in v1.0.4 and
v1.0.5, but the v1.0.5 changes to the update notice in `MainForm.cs` (waiting for other dialogs, locking
actions) weren't exercised.

### 7.4 Update failures — Simulated — Passed

`tests/Simulated` calls `Updater.Install` with local `file://` URLs:

| Case | Observed |
| --- | --- |
| Release without `.sha256` | refused: "A release não tem o arquivo .sha256…" |
| `.sha256` isn't a hash | refused: "O arquivo .sha256 da release é inválido." |
| Hash doesn't match | refused: "O arquivo baixado não bate com o SHA-256 da release." |
| Download missing | refused (file not found) |

In every case the running exe was unchanged and no `.new`/`.old` was left.

### 7.5 Missing or unreadable Vanguard — Simulated — Passed

The same project calls the window's `Apply` with made-up reports, without showing the window, so it never
reads or changes the real Vanguard:

| Report | Status shown |
| --- | --- |
| Nothing installed | "O Vanguard não está instalado" |
| `vgk` installed, `vgc` missing | "Serviço do Vanguard não encontrado" |
| `vgc` start type read, status unreadable | "Não deu para confirmar o estado" |
| Boot check unknown | "O Vanguard está ligado", loaded at boot *Não dá para saber* |
| Driver loaded after boot, 7 Riot processes (added after the session) | "Reinicie para jogar", *Processos da Riot em execução* = 7, no breakable "VAN 57" |

Added after the session, for the fixes in [Issues found](#issues-found): R1 and R2 build the "Reiniciar o
PC?" text for a driver started by Turn on and for one already in memory, check the wording and the no-break
space, and draw the real dialog to see where lines break.

Run it:

```sh
dotnet build tests/Simulated/Simulated.csproj -c Release -o tests/Simulated/bin/out
tests/Simulated/bin/out/Simulated.exe <folder for screenshots>
```

Exit code 0 means all passed.

### 7.6 English UI — Not run

Windows was in Portuguese, so only the Portuguese text was seen.

## 8. Restart with `vgc` blocked, then Turn on

Run after the fixes in [Issues found](#issues-found), with the fixed build (`out/VanguardSwitch.exe`,
SHA-256 `e3c2ebcba23781bb84ea5e4bd8416e5136c3738eec9b209a50c91a3f82582ce8`, same commit plus the local
changes). It checks the idea behind the app: only the driver has to load at boot, and `vgc` can start later.

- Before: Vanguard on, `vgk` loaded at boot, no game running, *Manter o driver carregado* on.
- Steps:
  1. **Desligar Vanguard**. Independent check: `vgc STOPPED DISABLED`, `vgk RUNNING SYSTEM_START`, tray `03`,
     no Riot process.
  2. Restart Windows (`shutdown /r`, not through the app).
  3. Wait, open the app, **Ligar Vanguard**.
  4. The tester opens VALORANT and League and joins a game in each.
- Expected: only `vgk` loads at boot. Turn on starts `vgc` without asking for a restart. No VAN 57.
- Observed after the restart (boot 03:40:55):
  - Only the driver loaded at boot: new `vgk_*.log` at 03:40:57. `vgc` stayed `STOPPED`/`DISABLED`, with no
    `vgc_*.log` since boot. No `vgtray`, tray `03`. Nothing changed during about 2 minutes, 30 s of it
    checked every 10 s.
  - The app showed "O Vanguard está desligado", loaded at boot *Sim*, *Parado e bloqueado*, *Só o driver*.
- Observed after Turn on (03:43:04):
  - No restart prompt. Log: "já estava na memória desde o boot".
  - `vgc` started at 03:43:07 (first `vgc_*.log` since boot), `AUTO_START`. Tray `02` and open.
  - No new `vgk_*.log`, so it was still the driver loaded at boot.
- Games (tester: "both worked", backed by logs):
  - VALORANT connected to a game server and loaded the map *Triad*, staying about 22 s, with no
    `VanguardNeedsReboot`.
  - League reached `GAMESTATE_GAMELOOP` and exited with `Quit Game Confirmed` about 4 s later.
  - As in 3.3 and 5.6, these are short sessions, not full matches.

## Issues found

1. **Restart prompt wording.** When the driver was started after boot but is already in memory, the
   prompt still said "O driver ligou agora". The log line was right ("já estava na memória, mas foi ligado
   depois do boot"). Minor.
   **Fixed after the session:** `Vg.Start` now reports whether this Turn on started the driver
   (`StartResult.StartedNow`), and `MainForm.RestartReason` picks the matching text.
2. **"Riot Client e jogos" counts processes.** It showed "7 abertos" with only Riot Client open and
   "15 abertos" with Riot Client + VALORANT, because each helper process counts. Minor.
   **Fixed after the session:** the row is now *Processos da Riot em execução* (*Riot processes running*)
   and shows the number or *Nenhum*.
3. **"VAN 57" split across lines** in the restart prompt ("…erro VAN" / "57."). Cosmetic.
   **Fixed after the session:** every "VAN 57" shown in the window, dialogs and log uses a no-break space.
4. **Simulated only:** with `vgc`'s start type read but its status unreadable, the *Serviço (vgc)*
   row said *Não encontrado*, even though the service exists. Minor; it needs both the service API and
   `sc.exe` to fail.
   **Fixed after the session:** the row now says *Não deu para ler* (*Couldn't read*). M3 checks it.

The fixes for 1–4 were checked only by the simulated tests R1, R2, M3 and M5 (text, no-break space, row
values) and by drawing the real dialog and window. They weren't run again on the real Vanguard. The fixed build is
no longer the exe identified in [Environment](#environment).

Not issues:

- `vgc` was `DEMAND_START` at the start because the tester had set it that way by hand. Turn on setting it
  to `AUTO_START` is the intended behavior.
- *Quando o Windows inicia* only looks at `vgk`, on purpose: only the driver has to load at boot. `vgc` and
  the tray can start later, which is how Turn on works (3.1, 3.3, and test 8 after a restart with `vgc`
  blocked).

No crash, hang or wrong service state was seen in the real tests.

## Limitations

- One PC: Windows 10 22H2, Portuguese, 100% scale, traditional Vanguard mode. Windows 11, On-Demand,
  English and other scales weren't tested.
- Game sessions were short (6–23 s inside a match). They show the games got past Vanguard's checks
  and into a match, not that a full match works.
- The stuck-driver path (5.2) and *Riot default and restart* (6.2) weren't run.
- Riot Client open while Vanguard is off (could it repair or start `vgc`?) wasn't tested.
- The tested exe is a local build. The release exe of v1.0.5 was only run as the result of the update in 7.3.
- Clicks were synthetic. Twice a click didn't reach the app (one Turn on, one link). They worked when
  repeated, and there's no evidence the app was at fault. At 02:01 the tester clicked Turn off and Turn on
  by hand while the automation was running; those two runs only have the app's own log as evidence and
  aren't counted above.

## Restoring the PC

At the end the PC was put back to the initial state with `sc.exe` and the registry, not through the app:
`vgc` back to `DEMAND_START`, the tray entry back to `02`, `vgtray` started as the normal user and
`%ProgramData%\VanguardSwitch` deleted. The independent check then matched the initial state (`vgc` stopped
manual, `vgk` loaded since boot at `SYSTEM_START`, tray enabled and open).

Test 8 was run after that. At the tester's request, the PC was left as Turn on leaves it: `vgc` automatic,
`vgk` loaded at boot, tray enabled and open, and the app's `%ProgramData%\VanguardSwitch` kept. At the end
`vgc` was stopped. Each `vgc_*.log` session ended when a game closed (03:45:54 VALORANT, 03:48:45 League),
and there was no start-type change besides Turn on, so Vanguard itself stopped it, not the app. The same
happened earlier in the session. No restart is pending.
