# Changelog

## v1.0.5

- Turn off locks the buttons before checking for open games, so a second click can't start another action
  at the same time. The update notice also waits until no other dialog is open.
- The window only says "Vanguard is on" when the `vgc` service was found and read. A missing service or an
  incomplete read now gets its own message instead.
- Release notes come from this changelog.

## v1.0.4

- Riot processes are only closed if Windows confirms a valid Riot Games signature on the exe (`WinVerifyTrust`),
  not just a matching name or folder.
- A process that exits while the list is being read no longer causes an error.
- *Riot default and restart* tells you when Vanguard's tray entry doesn't exist instead of reporting it as restored.
- GitHub Actions: only the release job can write to the repo, and a tag must match the version in the project.

## v1.0.3

- Before closing a process, the app checks its folder or signature, not only its name, and checks again that
  the process id still belongs to the same program.
- *Riot default and restart* checks each step (driver and service startup, tray, service running) and only
  restarts if the essential ones worked.

## v1.0.2

- Riot Client is never started with admin rights. If it can't open as a normal user, the app asks you to open
  it from its shortcut.
- `sc.exe` calls time out after 30 seconds instead of hanging the app.
- VAN 57 messages now say "can cause" and mention it applies to Vanguard's traditional mode.

## v1.0.1

- Updates are refused if the release has no valid SHA-256 file.
- An error while reading the state at startup no longer crashes the window.
- README: clearer wording about the boot check and network access.

## v1.0.0

- First release.
