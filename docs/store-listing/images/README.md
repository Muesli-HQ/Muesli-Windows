# Store listing images

Drop the Partner Center upload assets here. The filenames below are referenced from `../copy.md` and the plan file at `~/.claude/plans/wise-imagining-truffle.md`.

| File | Partner Center slot | Spec |
|---|---|---|
| `store-logo-300.png` | Store logo | 300×300 PNG, square |
| `store-logo-1080.png` | Store logo (high res) | 1080×1080 PNG, square |
| `hero-2400x1200.png` | Hero image | 2400×1200 PNG |
| `screenshot-01-onboarding.png` | Screenshot 1 | 1366×768 minimum PNG. Onboarding mic + hotkey + crash-reporting opt-in. |
| `screenshot-02-dictation.png` | Screenshot 2 | Dictating into Notepad with the floating indicator visible. |
| `screenshot-03-meeting-toast.png` | Screenshot 3 | Meeting-detection toast over a Teams / Meet window — the "Join & Record" call to action. |
| `screenshot-04-meeting-transcript.png` | Screenshot 4 | Recorded meeting with notes + transcript split. |
| `screenshot-05-privacy.png` | Screenshot 5 | About → Privacy card showing the crash-reporting toggle. |
| `screenshot-06-models.png` | Screenshot 6 | Models page with bundled Whisper cache populated. |

Source the 300×300 logo from `windows-native/Muesli.Windows/Assets/muesli_app_icon.png` (upscale / crop as needed). The hero and high-res logo can be created from the same source plus a tagline overlay.

All screenshots should be captured from a clean Windows 11 VM running the production installer — reviewers will compare these to what they see during their certification install.
