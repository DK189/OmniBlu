# OmniBlu

OmniBlu turns your Windows PC into a Bluetooth helper for your phone. The phone is the host, and this PC works as:

- **a Bluetooth speaker** (A2DP): the sound of the phone plays on the PC;
- **a keyboard and a mouse** (Bluetooth LE, HID over GATT): you control the phone with the keyboard and mouse of the PC.

OmniBlu runs on **Windows only** for now. The Linux code is kept in the project, but it is turned off because its Bluetooth part is not tested.

## What you need

- Windows 10 version 2004 (build 19041) or later.
- A Bluetooth adapter. For the keyboard and mouse part, the adapter must support the Bluetooth LE peripheral role. Not every adapter does.
- A phone that is already paired with this PC.

## Install

1. Download `OmniBlu-<version>-win-x64.zip` from the [Releases](https://github.com/DK189/OmniBlu/releases) page.
2. Unzip it and run `OmniBlu.exe`. It is one file and it needs no .NET installation.
3. The first time, before any window appears, OmniBlu asks if it should move to `C:\ProgramData\OmniBlu` (a fixed folder for all users) or keep running from where it is. You must choose one.
4. Later, if you run a downloaded copy while OmniBlu is already installed in that folder, nothing is asked: the installed copy starts. If the downloaded copy is newer, it replaces the installed one first.
5. If OmniBlu is already running and you open a newer downloaded copy, the running OmniBlu asks if it should replace itself with the newer file and restart. An older or equal copy only shows the window of the running one.

The file is not signed, so Windows SmartScreen can show a warning the first time. This is normal for an unsigned program.

## How to use

The window has three rows:

1. **Available**: the paired phones and computers. Press **Connect** to make this PC ready as a speaker for a device.
2. **Audio**: drag a connected device here to open its audio stream. Drag it back to Available to close the stream. Drag cards to change the priority.
3. **Controllable**: drag a device here to control it from this PC. OmniBlu starts advertising, and the phone connects after you pair this PC on the phone. Press **Start control** on a card to begin.

**Control mode** hides and locks the mouse cursor, and sends every key and mouse action to the phone. It ends with **Right Ctrl**, when the window loses focus, or when the phone stops accepting input. While it runs, the Windows key, Alt+Tab, Alt+Esc and Ctrl+Esc go to the phone and not to this PC. Ctrl+Alt+Del always stays with the PC, because no program can take it.

**Control shortcut** (default **Win+/**, works in every program):

- With no control running: it starts control on the device used last (or the first connected one). With no device connected, it only shows the window.
- While control runs: it switches to the next connected device, round and round.

You can change the shortcut in Settings. If another program already uses it, Settings and the log tell you.

When the main window is hidden or minimized, control runs from a small panel in the bottom-left corner of the screen. The tray icon brings the window back with one click. The buttons in the title bar minimize the window, hide it to the tray, or quit.

### Settings

| Setting | What it does |
| --- | --- |
| Mouse speed | How fast the pointer moves on the phone. |
| Use raw mouse input | Reads the mouse without Windows pointer acceleration, like a real mouse. |
| Show all paired devices | Also lists headsets, game controllers and other devices. |
| Check for updates | Looks for a new version on GitHub when OmniBlu starts. |
| Start with Windows | Do not start, start and open the window, or start in the tray with no window. |
| Control shortcut | The shortcut for the whole system. |

## Updates

When a new version exists, OmniBlu asks what to do: update now, ask again at the next start, or turn the check off. "Update now" downloads the new version, checks it against the SHA-256 file of the release, replaces the program file, and restarts OmniBlu. Nothing is installed without your answer.

The SHA-256 file comes from the same release, so it shows that the download is complete, but it does not show who made it.

## Files and logs

- Settings: `%LocalAppData%\OmniBlu\settings.json`
- Logs (one file per day, 14 days kept): `%LocalAppData%\OmniBlu\logs\omniblu-yyyyMMdd.log`

If something does not work, the log is the first place to look. The event log in the window shows the same messages.

On some PCs, a long path to `OmniBlu.exe` crashes the graphics driver when the window starts. If you see this, run the program from a short path such as `C:\ProgramData\OmniBlu`.

## Build from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet build OmniBlu.slnx
dotnet test OmniBlu.slnx
dotnet run --project OmniBlu
```

The project is written with Avalonia 12 and CommunityToolkit.Mvvm. The platform code (Bluetooth, keyboard hook, shortcut and similar) is in `OmniBlu/Natives/`. The rest of the code does not depend on the platform.

A build made from source never checks for updates and never asks to move.

## Make a release

Push a tag that looks like a version, for example `v1.2.3`. The GitHub Actions workflow in `.github/workflows/release.yml` builds the program on Windows, runs the tests, and publishes a release with the zip file and its SHA-256 file. A tag with a dash, such as `v1.2.3-beta.1`, is published as a pre-release. The workflow can also be started by hand from the Actions tab as a dry run: it makes no tag and no release.

See [CHANGELOG.md](CHANGELOG.md) for the changes of each version.

## License

OmniBlu is free software under the [GNU General Public License v3.0](LICENSE).
