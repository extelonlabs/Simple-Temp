# Setup guide

This takes about 2 minutes. You need 64-bit Windows and an admin account.

Tested on Windows 11. Windows 10 should work, but it's untested.

## 1. Install PawnIO (for CPU temp)

PawnIO is a free driver that lets programs read the CPU temp sensor. Without it, everything else works, but CPU temp shows `—`.

Open PowerShell and run:

```
winget install namazso.PawnIO
```

Click **Yes** if Windows asks. You can also get it from [pawnio.eu](https://pawnio.eu/).

## 2. Download Simple Temp

Download `SimpleTemp-win-x64.zip` from the [latest release](https://github.com/extelonlabs/Simple-Temp/releases/latest).

Right-click the zip and pick **Extract All**. Don't run it from inside the zip.

## 3. Run the installer

1. Open the folder you extracted.
2. Double-click `install.cmd`.
3. If Windows says "Windows protected your PC", click **More info**, then **Run anyway**. This shows up because the app isn't code-signed.
4. Click **Yes** on the admin prompt.
5. A window shows the results. Check that `cpu_temp` has a number, then press **Enter** to close it.

You can delete the extracted folder and the zip afterward.

## 4. Open it

Double-click **Simple Temp** on your desktop. It opens with no prompt.

## Using it

| To do this | Do this |
|---|---|
| Show the mini bar | Click **Mini bar** in the window |
| Go back to the full window | Double-click the mini bar, or left-click the tray icon |
| Close the app | Click the X on the full window, or right-click the bar or tray icon and pick **Close app** |
| Start with Windows | Tick **Start with Windows** at the bottom of the window |
| Start as the mini bar | Tick **Start as mini bar**. It appears after you tick the box above |

Windows 11 often hides new tray icons under the **^** arrow by the clock. Drag the icon out to keep it visible.

## Update to a new version

Download the new zip, extract it, and run `install.cmd` again. It closes the app first if a shortcut or startup task opened it. Your startup settings stay as they are.

## Uninstall

Double-click `uninstall.cmd` from the extracted folder. If you deleted that folder, download the zip again to get it.

It removes everything the installer added. PawnIO stays, because other apps may use it. To remove that too:

```
winget uninstall namazso.PawnIO
```

## What the installer changes

- Copies the app to `C:\Program Files\Simple Temp`
- Makes a scheduled task called `Simple Temp`, which opens the app as admin
- Puts a `Simple Temp` shortcut on your desktop

If you tick **Start with Windows**, the app also makes a task called `Simple Temp Autostart`.

## Problems

**CPU temp shows `—` and "not available".** PawnIO isn't installed or isn't running. Do step 1, then restart your PC.

**CPU temp shows "needs admin".** The app isn't running as admin. Open it from the desktop shortcut, or click **Run as admin to show CPU temp**.

**The checkboxes are missing.** You're running the exe from the zip, not the installed app. Run `install.cmd`, then use the desktop shortcut.

**The mini bar doesn't show over a game.** It can't draw over exclusive fullscreen. Set the game to borderless window.

**An old icon stays by the clock.** Move your mouse over it and it goes away. This is a Windows quirk and it's harmless.

**Something else.** Please [open an issue](https://github.com/extelonlabs/Simple-Temp/issues).
