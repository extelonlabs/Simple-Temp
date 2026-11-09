<p align="center"><img src="assets/icon.png" width="128" alt="Simple Temp icon"></p>

<h1 align="center">Simple Temp</h1>

<p align="center">A small Windows app that shows your CPU and GPU temperature, CPU and GPU usage, and RAM usage.</p>

<p align="center"><img src="assets/screenshot.png" width="402" alt="The Simple Temp window, showing CPU and GPU temperature and usage, and RAM usage"></p>

## What you get

- **Full window:** all five numbers, with bars. Updates every second.
- **Mini bar:** one line of text at the top middle of your screen, on top of other windows:
  `CPU 39°C 2%  |  GPU 37°C 5%  |  RAM 42%`
- **Tray icon:** your CPU temp by the clock. It turns amber at 80 °C and red at 90 °C. Hover to see all the numbers.
- **Start with Windows:** a checkbox in the app, with an option to start as the mini bar.
- **No admin prompt:** after setup, it opens without asking.

## Get it

**[Download SimpleTemp.exe](https://github.com/extelonlabs/Simple-Temp/releases/latest/download/SimpleTemp.exe)** and double-click it. It installs itself and opens. See the [setup guide](SETUP.md) if Windows warns you.

It needs 64-bit Windows and an admin account. If the free [PawnIO](https://pawnio.eu/) driver is missing, the installer adds it, because CPU temp needs it.

## Good to know

**Why it needs admin rights:** Windows only lets admin programs read the CPU temp sensor. Setup asks you once, then makes a scheduled task that opens the app as admin with no prompt. The trade-off is that a bad program already on your PC would have an easier path to admin rights. The app sits in Program Files, where normal programs can't change it, to reduce that risk.

**It isn't code-signed:** your browser or Windows SmartScreen may warn you. The setup guide shows what to do.

**What it's tested on:** Windows 11, an Intel i7-13700K and an NVIDIA RTX 4070 Ti. Other hardware should work, but it's untested. If a number shows `—`, please [open an issue](https://github.com/extelonlabs/Simple-Temp/issues).

**Exclusive fullscreen games:** the mini bar can't draw over them. Use borderless window mode instead.

## Build from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet publish -c Release -o dist
```

Then double-click `dist\SimpleTemp.exe` to install your build. To open it without installing, run `dist\SimpleTemp.exe --no-install`.

## Credits

Sensor reading comes from [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor). See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## License

[MIT](LICENSE). The third-party parts keep their own licenses.
