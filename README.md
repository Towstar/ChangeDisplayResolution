# ChangeDisplayResolution

A small Windows utility that changes a selected display to a configured resolution and refresh rate. This utility shows it's full strength when combined with a keyboard shortcut to execute the program.

## Config File

On first run, the app creates `ResolutionToggle.config` next to the built `.exe`.

For a normal Debug build, that is usually:

```text
...\ChangeDisplayResolution\bin\Debug\net10.0\ResolutionToggle.config
```

The app reads this file every time it runs, so you can change the target display or resolution without typing command-line arguments or editing and recompiling code.

Default config values:

```ini
Display=\\.\DISPLAY1
Mode=Toggle
LowResolution=1280x960
NormalResolution=1920x1080
TargetResolution=1280x960
RefreshHz=144
```

## Install / Run

Open the repo in Visual Studio or VS Code, build the project, then run the generated `.exe` from:

```text
bin\Debug\net10.0\ChangeDisplayResolution.exe
```

Use `--list` once if you need to see which Windows display name maps to each monitor.
