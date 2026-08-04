# AGENTS.md — Muesli Windows

## Project Context
- Windows-native WPF clone of the macOS Muesli app (dictation + meeting transcription)
- OG reference repo: `C:\Users\madha\Downloads\muesli-main\muesli-main`
- Active app lives in `windows-native/Muesli.Windows/`
- Legacy Electron/web files exist on disk but are excluded from git

## User Preferences

### Git / Version Control
- **NEVER auto-push to git unless user explicitly says "push to git"**
- User will review and push manually when ready
- Keep changes in working directory until explicitly approved for commit

### Code Style
- Follow existing WPF/XAML patterns in the codebase
- Prefer exact clone of macOS behavior where possible
- No placeholders or fake data

### Testing Workflow
- **After every project update made by Codex, automatically launch or relaunch Muesli with its dashboard visibly open and foregrounded before reporting completion. Do not wait for the user to ask, and do not use a hidden-window launch.**
- For source or project-file changes, rebuild first; for documentation or workflow-only changes, reuse the current successful build unless a rebuild is relevant.
- Kill running `Muesli.exe` before building when needed
- Build with `dotnet build --no-restore`
- Run executable directly from `bin/Debug/net8.0-windows/`
- After any code fix that affects the WPF app, build and launch Muesli before reporting completion.
- Kill existing `Muesli.exe` before build/launch when needed.
- Use:
  `dotnet build windows-native\Muesli.Windows\Muesli.Windows.csproj --no-restore`
- Launch with Start-Process so the shell does not block:
  `Start-Process -FilePath "C:\Users\madha\projects\muesli\windows-native\Muesli.Windows\bin\Debug\net8.0-windows\Muesli.exe" -WorkingDirectory "C:\Users\madha\projects\muesli\windows-native\Muesli.Windows\bin\Debug\net8.0-windows"`
- After launch, inspect the latest `%APPDATA%\muesli\logs\muesli-*.log` slice and confirm no fresh `ERROR`, `Unhandled UI exception`, or `XamlParseException`.
- If the change is packaging-related, also run package smoke tests.

## Architecture Notes
- `MainWindow.xaml` — Main UI (sidebar, pages, dictation list)
- `MainWindow.xaml.cs` — Code-behind (theme switching, data binding, event handlers)
- `App.xaml` — Shared resources, brushes, styles
- `Services/` — Meeting detection, prompt service, tray icon, etc.
- Data stored in `%APPDATA%/muesli/` (SQLite + JSON settings)

## Known Risks
- `MainWindow.xaml.cs` was severely corrupted (~3444→979 lines) and reconstructed from fragments
- Build passes but subtle bugs may remain from reconstruction
- Always verify UI behavior after changes, don't rely solely on build success
