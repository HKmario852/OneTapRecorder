# Validation notes — v0.4.1

Tested on one Windows x64 development PC, 2026-10-10 (Hong Kong date).

## v0.4.1 recording indicators

- Release build: zero warnings/errors. The audio/tray regression suite passes with actual audio and screen capture.
- Hidden audio and video capture select the red recording icon in the NotifyIcon and WPF Window, and set the taskbar overlay.
- Stopping selects the amber saving icon; successful completion restores the original tray/window icon and clears the overlay, including consecutive sessions.
- A rejected audio start leaves the idle icon intact. Close-to-tray and tray Exit still work and finalize files.
- Three icon images are created once, reused on transitions, and disposed on exit. No animation timer was added.
- Exported icon bitmaps were visually inspected. Shell icon assignment is checked in the running application; pinned/grouped taskbar rendering across Windows configurations has not been manually verified.

The functional baseline and short resource measurements below were collected for v0.4.0; performance was not re-benchmarked for this icon-only change.

## Functional checks

- English/Traditional Chinese switch updates the main UI, shortcut editor, tray menu and validation errors. English preference survives a new process.
- Audio-only capture creates PCM 16-bit, 48 kHz, stereo WAV files, with no video track. Known local tone playback yielded non-silent audio.
- Audio-only rejects both sound sources being disabled.
- Consecutive recording sessions complete successfully.
- Screen capture creates H.264 MP4 at 1920 × 1080 / 30 fps on the attached display.
- Closing the title-bar window leaves audio/video recording active in the tray.
- Tray actions reopen, stop/save, and exit after completing a file; the icon is disposed on exit.
- The global shortcut starts/stops while hidden/minimized; held keys do not toggle repeatedly.
- Reopening the executable signals the existing instance and the second process exits.

Tests invoke actual application handlers and native hotkey messages. Tray menu items were invoked programmatically; this is not a claim of manual physical-mouse testing of each Windows tray interaction. Microphone/mixed-source listening, second-monitor recording, long-duration stress, protected video, elevated apps and other computers remain unverified.

## Resource measurements

The same harness measured a v0.3 source snapshot and the optimized v0.4 source on the same computer. Each phase uses three 5-second samples, after startup/capture warm-up. CPU is process CPU time / elapsed time, normalized to **one core**, not the whole machine. Memory is the average of the three process measurements. These are short samples, include test overhead, and may vary between runs.

| Phase | v0.3 CPU, one core | v0.4 CPU, one core | v0.3 private MiB | v0.4 private MiB | UI ticks, old → new |
|---|---:|---:|---:|---:|---:|
| Hidden idle | 0.31% | 0.21% | 101.72 | 102.48 | 0 → 0 |
| Hidden audio recording | 1.77% | 0.94% | 107.61 | 103.83 | 60 → 0 |
| Hidden idle after recording | 0.21% | 0.10% | 106.99 | 105.03 | 0 → 0 |

Hidden audio working set averaged 144.92 MiB before and 137.27 MiB after. Idle memory is essentially unchanged; no working-set trimming tricks or forced periodic garbage collection are used. Audio remains 48 kHz stereo PCM; video remains up to 1080p/30 fps.

Implementation changes: hidden/minimized UI timer disabled, visible timer reduced to 1 Hz, tray state text updated on transitions, audio writes batched at 100 ms with cancellation-aware stopping, bounded 2-second audio buffers, lazy native video initialization, and only the NAudio WASAPI dependency rather than the full NAudio bundle.

## Diagnostics

Build first. The following switches write results into the final absolute folder argument:

- `--language-test <folder>`; add `--verify-reload` for a second run that checks saved language.
- `--hotkey-test <folder>`; add `--verify-reload` for saved shortcut checks.
- `--instance-test <folder>` checks second-launch behavior.
- `--audio-tray-test <folder>` needs `tone.wav` in the folder.
- `--smoke-test <folder>` needs `tone.wav` in the folder.
- `--resource-test <folder>` runs three 15-second measurements plus warm-up.
- `--render-preview --english <image.png>` or `--chinese`; add `--audio-mode` or `--show-hotkey-editor` for those states.

**Capture diagnostics record real screen/system audio to the supplied folder.** Use a suitable local environment and do not commit recordings. Audio/microphone mixing is not automatically validated. Do not run the hotkey and audio-tray tests concurrently: both reserve F23 for their own native-key test. Diagnostics use isolated settings where needed; resource/preview tests do not save normal preferences.
