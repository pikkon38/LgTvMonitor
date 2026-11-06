# LG TV Monitor

A small Windows tray app that makes an LG webOS TV (built for an LG C6 OLED used as a PC monitor) turn on and off like a real monitor.

| When this happens on the PC | The TV |
|---|---|
| Windows turns the display off (screen timeout, locked or not) | turns off |
| You press a key or move the mouse and the display comes back | turns on |
| PC shuts down (a **restart** leaves the TV on) | turns off |
| PC starts (works before you sign in) | turns on |
| PC unlocks (Windows Hello, PIN, password) | turns on |
| PC goes to sleep / wakes up | turns off / turns on |

Each of these can be switched off in Setup. If the TV is showing something other than the PC (Netflix, a console on another HDMI port), the automatic power-off is skipped so the app never shuts the TV off while you're watching it.

## How it works

* **Off:** the app sends the "power off" command over your home network through the TV's own remote-control API, the same one the LG ThinQ phone app uses. The TV asks once to allow the PC; after that a saved key is used.
* **On:** a Wake-on-LAN "magic packet", which the TV accepts when **Turn on via Wi-Fi** is enabled. If the TV only has its screen off (see "Screen off only" below), the app sends "screen on" instead.
* Windows tells the app about display, lock, sleep and shutdown events. Turning the TV on at startup is handled by a scheduled task that runs before anyone signs in, so the TV is on for the sign-in screen.

## Before you install

1. **TV settings:** make sure **Turn on via Wi-Fi** is on. On recent LG TVs it's under **Settings > All Settings > General > Devices > External Devices > TV On With Mobile**. If the TV uses a network cable, Wake-on-LAN works over the cable too.
2. **Give the TV a fixed IP address.** In your router, add a "DHCP reservation" for the TV. If its IP changes later, the PC can no longer reach it.
3. The PC and TV must be on the same network.

## Install

1. Download **LgTvMonitor-Setup.exe** from the [latest release](https://github.com/pikkon38/LgTvMonitor/releases/latest) and run it. Accept the admin prompt: the installer sets up the "turn the TV on at startup" task, which runs before you sign in.
2. At the end, leave **Open LG TV Monitor** ticked and click Finish.
3. The app window opens (the TV must be on):
   1. Click **Find TV**. This fills in the IP and MAC address. If nothing is found, type the IP yourself and click **Detect** for the MAC.
   2. Click **Pair with TV** and choose **Allow** on the TV with the remote. Do this while the TV is showing the PC: the app records that HDMI input as "the PC's input".
   3. Pick **Power off** (default) or **Screen off only**.
   4. Under **App**, leave **Start with Windows** ticked. Tick **Start minimized to the tray** if you want it to start without opening the window.
   5. Click **Test off and on**. The TV should turn off and come back on by itself.
   6. Click **Save**.

The app now lives in the system tray (TV icon). Right-click it for: Turn TV on / off, **Pause automatic control** (for example while you watch something on the TV with the PC idle), Open LG TV Monitor, and Open log. The window uses the same dark look as Device Switch and Joystick Keeper.

Closing the window asks whether to **minimize to the tray** (the TV automation keeps running) or **exit**. Tick "Remember my choice", or change it later under **App > When I close the window**. Also under **App**: **Start with Windows** and **Start minimized to the tray**. Opening the app while it is already running brings its window back.

## Updates

When a new version is out, the tray icon shows a notice and an **Update to vX** button appears at the bottom of the window (also in the tray menu). One click downloads the new installer, installs it and reopens the app; Windows asks for admin once. **Check for updates** at the bottom of the window checks right away. Your settings and pairing are kept.

If Windows SmartScreen says "Windows protected your PC", click **More info > Run anyway**. This happens because the app isn't code-signed.

## Windows settings to check

* **When the TV turns off:** Settings > System > Power & battery > **Screen and sleep** > "Turn my screen off after...". That timer is what turns the TV off.
* **When locked:** Windows turns the display off about 1 minute after you lock, and the TV turns off with it.
* **Fast Startup:** the startup task also triggers on Fast Startup boots. If the TV still doesn't come on when the PC powers on, turn Fast Startup off (Control Panel > Power Options > Choose what the power buttons do > uncheck "Turn on fast startup").

## Power off vs. screen off

* **Power off** (default): the TV fully turns off, the same as pressing the remote's power button. Turning it back on uses Wake-on-LAN and takes a few seconds.
* **Screen off only:** the panel goes dark but the TV stays on the network. It comes back faster and more reliably, but it uses a little more power. Try this if Wake-on-LAN is unreliable on Wi-Fi.

## Troubleshooting

Everything the app does is written to `C:\ProgramData\LgTvMonitor\log.txt` (tray menu > Open log).

* **"TV not reachable"**: check the IP (Setup > Find TV) and that the TV has a fixed IP.
* **TV doesn't turn on**: check "Turn on via Wi-Fi" on the TV. Wi-Fi Wake-on-LAN can stop working if the TV has been off for a long time. A network cable or **Screen off only** fixes that.
* **The TV asks to pair again**: click Pair with TV again. This happens after a TV factory reset.
* **The TV turned off while I was watching Netflix**: in Setup, make sure "Don't turn off automatically if the TV is on another input/app" is ticked. Pair again while the TV is showing the PC.
* To run it by hand: `LgTvMonitor.exe --on`, `--off`, or `--setup`.

## Uninstall

Settings > Apps > Installed apps > **LG TV Monitor** > Uninstall. This also removes the startup task. Your settings in `C:\ProgramData\LgTvMonitor` are kept, so a reinstall doesn't need pairing again; delete that folder to remove them too.

## Building from source

Requires the .NET 10 SDK:

```
dotnet publish src/LgTvMonitor -c Release -o publish
```

This produces a single self-contained `publish\LgTvMonitor.exe`, so the PC doesn't need .NET installed. The build also works from Linux or macOS because the project sets `EnableWindowsTargeting`. The installer is built from `installer/LgTvMonitor.iss` with Inno Setup 6. To redraw the icon, run `python assets/make-icon.py` (needs Pillow).

## Releasing

CI builds every push and pull request. Merging to `main` with a new `<Version>` in `src/LgTvMonitor/LgTvMonitor.csproj` publishes release `v<Version>` with `LgTvMonitor-Setup.exe`, which the in-app updater installs.
