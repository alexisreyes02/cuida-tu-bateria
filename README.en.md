# Cuida tu Batería

A free Windows app that reminds you when to plug in and unplug your laptop, and tells you how long the battery will really last with the way you use it, above all when the power goes out. Spanish and English interface.

I once brought a Canaima school laptop back from the dead. This app warns you in time so your laptop never gets to that point.

*Alexis Reyes, Reyes Project*

[Español](README.md)

## What it does

- Reminds you to unplug when the battery reaches your upper number (80% by default) and to plug in when it drops to your lower number (20%). The reminder appears next to the clock, wherever your taskbar is, and never takes the focus away from your work. During a presentation, a game or a full-screen video, it waits.
- The **Later** button reminds you again in 15 minutes. The X silences the reminder until the next cycle.
- Tells you how much time you have left. On battery, it uses your own drain rate from the past few days. On the charger, it answers the question everyone asks in Venezuela: if the power goes out right now, how long do I have? It counts down to the level where Windows shuts the laptop down, not to 0%.
- On battery, it shows how many watts the laptop is drawing right now and whether that is light, normal or heavy use. That explains why the battery lasts three hours one day and an hour and a half the next.
- While charging, it tells you when the battery will reach your upper number, at your charger's real pace.
- Blackout mode: on days with power cuts, one button lets the battery charge to 100% without asking you to unplug for 24 hours.
- Always plugged in: for a laptop used as a point of sale or one that lives on a desk. It never asks you to unplug and still reminds you when it runs low.
- If your laptop already stops charging by itself (Lenovo, ASUS, Dell, Windows 11), it notices and stays quiet.
- Reads battery health against what the battery held when new, tells you when the battery was made (if the battery stores the date), shows health month by month and saves it as a picture with a verdict for the repair technician.
- Counts charge cycles when the laptop does not.
- Explains the Windows battery report in plain words.
- Tells you where to turn on the factory charge limit on Lenovo, ASUS, Dell, Samsung, Acer and HP business laptops. That limit really stops charging.
- On Sunday evenings, a short summary of your week. No scolding.
- Works with the keyboard (Tab, Enter, Esc), in Spanish and English, and stays sharp on any screen.

## What it does not do

- It does not stop the charge. Windows does not let a program switch the charger off. The app reminds you, and you unplug.
- It never connects to the internet. Nothing leaves your laptop.
- It does not ask for administrator rights. It installs for your user only.
- It does not run on Canaima GNU/Linux: it is for Windows 10 and 11 only (32-bit or 64-bit).
- On a desktop PC with a UPS it stays quiet: the battery Windows sees belongs to the UPS.

## Install

Download `CuidaTuBateriaSetup-6.1.0.exe` from the [releases page](https://github.com/alexisreyes02/cuida-tu-bateria/releases) and run it. Nothing else is needed (it uses .NET Framework 4.6.1, which ships with Windows 10 and 11).

Until the installer is code-signed, Windows may show "Windows protected your PC". To continue, choose "More info" and then "Run anyway". To check that the file is the original, run this in PowerShell and compare the result with the SHA-256 published next to the download:

```
Get-FileHash ".\CuidaTuBateriaSetup-6.1.0.exe" -Algorithm SHA256
```

Silent install (for winget and scripts): `CuidaTuBateriaSetup-6.1.0.exe /silent`. A fresh silent install turns on "Start with Windows"; an update keeps your settings. Silent removal: `uninstall.exe /uninstall /silent`, from the install folder (`%LOCALAPPDATA%\Programs\CuidaTuBateria`).

## Where your data lives

Everything stays on this laptop, in `%LOCALAPPDATA%\CuidaTuBateria`:

- `settings.ini`: your numbers and preferences.
- `uso.csv`: one line each time the percentage or the charger changes, plus one every 10 minutes, kept for 30 days and written to disk every half hour, on sleep and on exit.
- `salud.txt`: the latest health reading and the monthly history. Measured at most once a day, a few minutes after startup.
- `errores.log`: only if something goes wrong.

The uninstaller asks whether to keep these files. The health picture you save to Pictures shows your laptop's brand and model; sharing it is up to you.

## How the time left is estimated

It takes the stretches on battery from the last 14 days, adds up how much the percentage dropped, and divides by the hours. Once the laptop has been unplugged for more than 5 minutes, it blends the current rate (60%) with the earlier days (40%). This is a practical rule, not a lab figure. Gaps longer than 16 minutes (sleep, app closed) are ignored, and so are readings of 98% or more on battery: many laptops, HP among them, keep showing 100% for a few minutes after unplugging and then catch up all at once. The time runs down to the Windows critical level (5% by default), where the laptop shuts down. Until it has your data, it shows the Windows figure with "About" in front and says it is not its own yet: Windows bases it on other weeks, and heavier use can make it fall short.

## Why 80% and 20%

Lithium-ion cells wear faster when they sit at full charge for hours, especially in the heat, and when they are drained deeply. Battery University does not prescribe "80 and 20"; it recommends partial charges and avoiding full charge combined with heat ([BU-808](https://batteryuniversity.com/article/bu-808-how-to-prolong-lithium-based-batteries)). 80 and 20 is a practical rule that follows that advice. You can change the numbers in the app at any time.

Health is rated like this: 90% or more, excellent; 80 to 89, good; 60 to 79, worn; below 60, weak. Apple, for example, treats 80% as the expected level at the end of a battery's rated life ([Apple](https://support.apple.com/en-us/ht201585)).

## Sources for the brand guidance

Every path comes from the manufacturer's own support page: [Lenovo](https://support.lenovo.com/hn/en/solutions/ht103159), [ASUS](https://www.asus.com/ar/support/FAQ/1032726/), [Dell](https://www.dell.com/support/kbdoc/en-bw/000123091), [Samsung](https://www.samsung.com/us/support/galaxy-battery/galaxy-book-battery/), [Acer](https://community.acer.com/es/kb/articles/19027-funcion-de-limite-de-carga-de-la-bateria-en-portatiles-acer), [HP business models](https://support.hp.com/document/c06179452).

The new-battery capacity for the HP 14-cf, 14-ce, 15-cs and 15-da (about 41 Wh, HT03XL battery) comes from that battery's spec sheet and is marked "per the spec sheet": the HT03XL also exists in a 45 Wh version, so it is an estimate.

## Build

You need the .NET SDK 8 or newer.

```
./build.sh
```

The app lands in `build/app` and the installer in `build/installer`, and the script prints the installer's SHA-256. `./build.sh preview` builds a test version with simulated data; it is never published, and the normal build refuses to finish if it finds any trace of it. Public releases are built by GitHub Actions, and theirs is the SHA-256 that counts.

## Privacy, warranty and trademarks

Cuida tu Batería never connects to the internet and sends no data to anyone: your settings, your usage log and your battery's health stay on your laptop, and you can delete them at any time.

It is offered free and as is, with no warranty of any kind, under the GPL-3.0-or-later license. Health and battery-life figures are estimates.

Reyes Project is not affiliated with Lenovo, ASUS, Dell, Samsung, Acer, HP or Microsoft. Lenovo Vantage, MyASUS, Dell Power Manager, Samsung Settings, Acer Care Center, HP Battery Health Manager, Galaxy Book and IBM Plex are trademarks of their respective owners, named only to say where each setting is.

## License

Cuida tu Batería is free software under the [GNU General Public License, version 3 or later](LICENSE). Copyright (C) 2026 Alexis Reyes (Reyes Project).

The typefaces are embedded under the SIL Open Font License 1.1: Literata (The Literata Project Authors) and IBM Plex Sans (IBM Corp., reserved font name "Plex"). Their licenses are in `assets/fonts`.
