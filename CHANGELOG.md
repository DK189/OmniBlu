# Changelog

All notable changes to OmniBlu are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

### Added
- Project skeleton: `OmniBlu.slnx` and the `OmniBlu` Avalonia 12 desktop app on .NET 10.
- Entry point with a generic host (`Host.CreateApplicationBuilder`), service registration in `Extensions.cs`,
  and an empty main window.
- Multi-targeting `net10.0-windows10.0.19041.0` and `net10.0`, with `WINDOWS` / `LINUX` compile constants for code in `Natives/`.
- Device connection management. This PC acts as a Bluetooth A2DP sink (speaker) and the phone is the host.
  - Row 1 (Available) lists paired devices with a Connect / Disconnect button. Connect makes the PC ready (`AudioPlaybackConnection.Start`).
    Devices without A2DP support are shown but cannot be connected. The name and icon of each device can be edited and are saved.
  - Row 2 (Audio) opens the audio stream (`OpenAsync`) when a ready device is dropped on it. Cards can be dragged to change their order.
    Dragging a card back to Available closes the stream and keeps the device ready.
  - Row 3 (Controllable) lets this PC act as a Bluetooth LE keyboard and mouse (HID over GATT) for the phone.
    Dropping a device here starts advertising; advertising stops when no device is left in the row and is never started at app launch.
    The phone connects by itself after pairing with this PC. Each card shows the HID state and has a "Start control" button.
    Cards can be dragged to change their order. Rows 2 and 3 are independent: a device can be in both, and dragging a card back to
    Available removes only the role of the row it came from. Disconnect in row 1 clears both roles.
  - HID support cannot be detected in advance, so any device can be dropped in row 3. A device is marked "verified" (and remembered)
    the first time it connects over HID. A phone that connects over HID but is not in the list is added to row 1.
  - The test buttons that were on each Controllable card (Click, keys, "Send text") were removed. A Controllable card now only has
    "Start control" (control mode sends all keys and mouse input anyway).
  - Control mode ("Start control"): hides and locks the cursor, sends every key and mouse movement, click and wheel to the phone,
    and ends with Right Ctrl, when the window loses focus, when the window closes, or when the phone stops accepting input.
    All keys and buttons are released on the phone when it ends. Ctrl+Alt+Del still belongs to this PC (no program can take it).
  - While control mode runs, the Windows key, Alt+Tab, Alt+Esc and Ctrl+Esc are kept away from this PC (a low-level keyboard hook,
    only on during control). They are sent to the phone instead, so the Start menu and the window switcher no longer open and
    control mode no longer ends by accident. If the hook cannot start, the log says so and those keys stay with the PC.
  - Mouse movement in control mode is read from the cursor itself (whole screen pixels, then the cursor is put back on a fixed
    centre pixel), not from the position in each mouse event. Queued events carry old positions, and counting them moved the
    pointer on the phone too far, and a half-pixel offset of the centre made it creep.
  - Mouse reports are sent at a steady pace (one every 8 ms, at most 40 units each) instead of in bursts. A burst looked like a sudden
    fast flick to the phone, because the phone works out pointer speed from the size of each report. Movement that was not sent when
    control ends is dropped.
  - Control mode reads the mouse as raw input (Windows Raw Input) instead of the cursor position: the movement is exactly what the
    mouse reports, with no Windows pointer acceleration, like a real mouse on the phone. It is read on its own thread, so a slow
    window cannot delay it. While control mode runs the cursor is frozen on one pixel. If raw input cannot start, the log says why
    and the cursor position is used as before. The Settings panel (or `UseRawMouse` in the settings file) turns it off.
    Raw counts depend on the DPI of the mouse, so the Mouse speed slider may need adjusting.
  - Settings panel (the "Settings" button at the top right of the window). It holds the Mouse speed slider (moved here from the
    Controllable row), a check box for raw mouse input, and "Reset to defaults". Choices are saved. The panel closes when control
    mode starts, when you click outside it, and when the window loses focus.
  - The Available row lists only phones and computers (by the Bluetooth device class that Windows reports), because this PC
    works as a speaker and as a keyboard and mouse for them. Headsets, game controllers and similar devices are hidden. A device
    with no class, or one that can send audio (A2DP), is always shown. "Show all paired devices" in Settings brings the others back.
  - Control shortcut for the whole system (default Win+` , the key above Tab), so control mode can start from any program. Press it: if a device is
    connected over HID, this window comes to the front and control starts (the device used last, or the first one); if none is
    connected, the window is only shown. While control runs, press it again to switch to the next connected device (round and
    round). After control ends, the next press goes back to the device that was controlled last. The shortcut can be changed in
    Settings (click the button, then press the new keys); at least one of Ctrl, Alt, Shift or Win is needed. If another program
    already owns the shortcut, Settings and the log say so. Control still ends with Right Ctrl.
    A shortcut with the Windows key works while control runs too: the Windows key is blocked then, so the key blocker finds the
    shortcut itself, does not send its last key to the phone, and switches device. Hold the Windows key and press the key again
    to switch more than once. The key blocker keeps running across such a switch.
  - Small control panel (about 300x200, bottom-left corner of the main screen, always on top, not in the task bar). It shows who is
    controlled, the place of the device in the Controllable row ("Device #2 of 3"), "Press Right Ctrl to stop", and the shortcut for
    the next device. It is used only when the main window is hidden or minimized: then control runs from this panel and the main
    window is not brought back. When the main window is shown, control runs in it as before and the panel does not appear. The
    panel is a real window that takes the keyboard focus, because control mode needs it. A switch to the next device keeps it on
    screen. With no device connected, the shortcut still shows the main window.
  - Tray icon (no menu). One click brings the main window back (also from minimized). The icon is removed when the app exits.
    A simple icon image (`Assets/omniblu.png`) was added; it is also the icon of the windows.
  - Smaller window with a fixed size (980 px wide, about 600 px high). It cannot be resized or maximized. The Available row
    no longer has empty space at the bottom. The event log is hidden when the app starts; the "Event log" button below the rows
    shows or hides it, and only then does the window become taller (the window never changes size for any other reason, also not
    for the Settings panel, which scrolls if it is too long).
  - New title bar in the colour of the app (the window frame of Windows is not used). Drag it to move the window. The buttons are:
    minimize to the task bar, hide to the tray, and quit. The tray icon (or the control shortcut) brings a hidden window back.
    The small control panel in the corner has no frame either.
  - "Start with Windows" in Settings, with three choices: do not start, start and open the window, or start in the tray (no
    window; the tray icon and the control shortcut still work). It is saved for the current user in the Windows "Run" key (no
    administrator rights), as the full path of the program that is running when the choice is made, so choose it again if the
    program is moved. Windows keeps the choice, so Settings reads it from Windows each time. "Reset to defaults" turns it off.
    A start in the tray uses the `--tray` argument; the window is created but not shown until the tray icon or the shortcut asks.
  - Only one copy of the app runs for each user (a named mutex). Starting a second copy shows the window of the first one
    (also when it is in the tray) and the second copy exits. A copy that Windows starts with `--tray` stays quiet and does not
    show the window.
  - Mouse speed setting (saved). Slow movement is not lost to rounding at low speeds.
  - When control mode ends, the log gets one line about the mouse (movements, report sizes, backlog, slowest send), to tell a stalled
    Bluetooth link from a mouse that moved too fast.
- The event log is also saved to `%LocalAppData%\OmniBlu\logs\omniblu-yyyyMMdd.log` (one file per day, 14 days kept). Crashes
  (unhandled exceptions) are written there too. The path is shown under the event log in the window.
- HID diagnostics in the log: Bluetooth LE link up/down, report reads, and how many phones subscribed.
- Windows devices are joined by container ID, because the paired-device list and the A2DP list use different IDs for the same device.
  HID clients are matched to devices by Bluetooth address instead, because the classic and LE records of one phone can have different container IDs.
- Unit tests (xUnit) for device merging and settings.
