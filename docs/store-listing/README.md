# Microsoft Store listing materials

Source-controlled copy and assets for Muesli's Microsoft Store submission. **Not consumed by the build** — this directory is reference material for Partner Center submissions and resubmissions.

The Store path is unpackaged Win32: we submit the existing `Muesli-win-Setup.exe` produced by `scripts/package-windows-v1.ps1`. There is no MSIX repackage.

## Files

- [`copy.md`](copy.md) — every text field that goes into the Partner Center listing (short description, long description, search terms, notes for certification, etc.). Update this file when copy changes; it is the canonical source.
- [`images/`](images/) — store logo, hero image, screenshots. Filenames document the Partner Center field each image is uploaded to.

## Workflow

1. Build a release via `.github/workflows/release-package.yml` (manual `workflow_dispatch`). Download `Muesli-win-Setup.exe` from the produced draft GitHub Release.
2. Smoke-test silent install / uninstall on a clean Windows 11 VM. Capture screenshots while you're there.
3. Drop screenshots into `images/` with the names referenced in `copy.md`.
4. Open Partner Center → Muesli → Submissions → Start your submission. Paste each field from `copy.md`. Upload the binary and screenshots.
5. Submit for certification.

See `~/.claude/plans/wise-imagining-truffle.md` for the full plan.
