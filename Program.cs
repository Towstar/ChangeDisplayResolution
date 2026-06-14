using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

internal static class Program
{
    private const string ConfigFileName = "ResolutionToggle.config";

    // Built-in defaults. If the config file does not exist, the app writes these
    // values to ResolutionToggle.config next to the executable, then reads them.
    private const string TargetDisplay = @"\\.\DISPLAY1";

    private const uint LowWidth = 1280;
    private const uint LowHeight = 960;

    private const uint NormalWidth = 1920;
    private const uint NormalHeight = 1080;

    private const uint RefreshHz = 144;

    private const int ENUM_CURRENT_SETTINGS = -1;

    private const uint CDS_UPDATEREGISTRY = 0x00000001;
    private const uint CDS_TEST = 0x00000002;

    // Safer default: apply dynamically only, do not intentionally write the setting to registry.
    // If you later want Windows to remember the change as a saved display setting, change this to:
    // private const uint APPLY_FLAGS = CDS_UPDATEREGISTRY;
    private const uint APPLY_FLAGS = 0;

    private const int DISP_CHANGE_SUCCESSFUL = 0;
    private const int DISP_CHANGE_RESTART = 1;
    private const int DISP_CHANGE_FAILED = -1;
    private const int DISP_CHANGE_BADMODE = -2;
    private const int DISP_CHANGE_NOTUPDATED = -3;
    private const int DISP_CHANGE_BADFLAGS = -4;
    private const int DISP_CHANGE_BADPARAM = -5;
    private const int DISP_CHANGE_BADDUALVIEW = -6;

    private const uint DM_BITSPERPEL = 0x00040000;
    private const uint DM_PELSWIDTH = 0x00080000;
    private const uint DM_PELSHEIGHT = 0x00100000;
    private const uint DM_DISPLAYFREQUENCY = 0x00400000;

    private const int DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x00000001;
    private const int DISPLAY_DEVICE_PRIMARY_DEVICE = 0x00000004;

    private const uint MinPixelDimension = 320;
    private const uint MaxPixelDimension = 16384;
    private const uint MinRefreshHz = 1;
    private const uint MaxRefreshHz = 1000;

    private const uint MB_OK = 0x00000000;
    private const uint MB_ICONERROR = 0x00000010;
    private const uint MB_ICONINFORMATION = 0x00000040;

    private static readonly HashSet<string> KnownConfigKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Display",
        "Mode",
        "LowResolution",
        "NormalResolution",
        "TargetResolution",
        "RefreshHz"
    };

    private enum RunMode
    {
        Toggle,
        Low,
        Normal,
        Target
    }

    private readonly record struct Resolution(uint Width, uint Height)
    {
        public override string ToString()
        {
            return $"{Width}x{Height}";
        }
    }

    private sealed record AppConfig(
        string DisplayName,
        RunMode Mode,
        Resolution LowResolution,
        Resolution NormalResolution,
        Resolution TargetResolution,
        uint RefreshHz
    );

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public int StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        private const int CCHDEVICENAME = 32;
        private const int CCHFORMNAME = 32;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
        public string dmDeviceName;

        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;

        public short dmOrientation;
        public short dmPaperSize;
        public short dmPaperLength;
        public short dmPaperWidth;
        public short dmScale;
        public short dmCopies;
        public short dmDefaultSource;
        public short dmPrintQuality;

        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHFORMNAME)]
        public string dmFormName;

        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;

        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(
        string? lpDevice,
        uint iDevNum,
        ref DISPLAY_DEVICE lpDisplayDevice,
        uint dwFlags
    );

    [DllImport("user32.dll", EntryPoint = "EnumDisplaySettingsW", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(
        string? lpszDeviceName,
        int iModeNum,
        ref DEVMODE lpDevMode
    );

    [DllImport("user32.dll", EntryPoint = "ChangeDisplaySettingsExW", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(
        string? lpszDeviceName,
        ref DEVMODE lpDevMode,
        IntPtr hwnd,
        uint dwflags,
        IntPtr lParam
    );

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(
        IntPtr hWnd,
        string text,
        string caption,
        uint type
    );

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            ValidateArgs(args);

            AppConfig config = LoadOrCreateConfig();
            string displayName = ParseDisplayName(args, config.DisplayName);
            uint refreshHz = ParseRefreshHz(args, config.RefreshHz);
            Resolution? explicitResolution = ParseExplicitResolution(args);

            // Run:
            // ResolutionToggle.exe --list
            //
            // Useful before binding it to Keychron, so you can verify DISPLAY1/DISPLAY2/DISPLAY3.
            if (HasArg(args, "--list"))
            {
                ShowInfo(BuildDisplayList(config), "Detected Displays");
                return 0;
            }

            if (!IsAttachedDesktopDisplay(displayName))
            {
                throw new InvalidOperationException(
                    $"{displayName} is not an attached desktop display.\n\n" +
                    "Run this first to see your displays:\n\n" +
                    "ResolutionToggle.exe --list\n\n" +
                    $"Then edit Display= in:\n\n{GetConfigPath()}"
                );
            }

            DEVMODE current = GetCurrentMode(displayName);

            // Optional one-way modes:
            //
            // ResolutionToggle.exe --low
            // ResolutionToggle.exe --normal
            //
            // No argument = follow Mode= in ResolutionToggle.config.
            Resolution targetResolution = ResolveTargetResolution(args, config, current, explicitResolution);

            ApplyExactMode(displayName, targetResolution.Width, targetResolution.Height, refreshHz);

            return 0;
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            return 1;
        }
    }

    private static void ValidateArgs(string[] args)
    {
        foreach (string arg in args)
        {
            if (
                string.Equals(arg, "--list", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "--low", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "--normal", StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith("--display=", StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith("--resolution=", StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith("--refresh=", StringComparison.OrdinalIgnoreCase)
            )
            {
                continue;
            }

            throw new InvalidOperationException(
                $"Unknown argument: {arg}\n\n" +
                "Supported arguments are:\n\n" +
                "  --list\n" +
                "  --low\n" +
                "  --normal\n" +
                "  --display=2\n" +
                "  --resolution=2560x1440\n" +
                "  --refresh=144"
            );
        }
    }

    private static AppConfig LoadOrCreateConfig()
    {
        string path = GetConfigPath();

        if (!File.Exists(path))
        {
            string? directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, BuildDefaultConfigText(), Encoding.UTF8);
        }

        return LoadConfig(path);
    }

    private static AppConfig LoadConfig(string path)
    {
        Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string[] lines = File.ReadAllLines(path, Encoding.UTF8);

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();

            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            int equalsIndex = line.IndexOf('=');

            if (equalsIndex <= 0)
            {
                throw new InvalidOperationException(
                    $"{ConfigFileName} line {i + 1} must use Key=Value format.\n\n{path}"
                );
            }

            string key = line.Substring(0, equalsIndex).Trim();
            string value = line.Substring(equalsIndex + 1).Trim();

            if (!KnownConfigKeys.Contains(key))
            {
                throw new InvalidOperationException(
                    $"{ConfigFileName} line {i + 1} uses an unknown key: {key}\n\n{path}"
                );
            }

            if (value.Length == 0)
            {
                throw new InvalidOperationException(
                    $"{ConfigFileName} line {i + 1} needs a value for {key}.\n\n{path}"
                );
            }

            if (!values.TryAdd(key, value))
            {
                throw new InvalidOperationException(
                    $"{ConfigFileName} line {i + 1} repeats the key {key}.\n\n{path}"
                );
            }
        }

        string displayName = NormalizeDisplayName(GetConfigValue(values, "Display", TargetDisplay), "Display");
        RunMode mode = ParseRunMode(GetConfigValue(values, "Mode", "Toggle"), "Mode");
        Resolution lowResolution = ParseResolution(
            GetConfigValue(values, "LowResolution", $"{LowWidth}x{LowHeight}"),
            "LowResolution"
        );
        Resolution normalResolution = ParseResolution(
            GetConfigValue(values, "NormalResolution", $"{NormalWidth}x{NormalHeight}"),
            "NormalResolution"
        );
        Resolution targetResolution = ParseResolution(
            GetConfigValue(values, "TargetResolution", $"{LowWidth}x{LowHeight}"),
            "TargetResolution"
        );
        uint refreshHz = ParseUIntInRange(
            GetConfigValue(values, "RefreshHz", RefreshHz.ToString(CultureInfo.InvariantCulture)),
            "RefreshHz",
            MinRefreshHz,
            MaxRefreshHz
        );

        return new AppConfig(
            displayName,
            mode,
            lowResolution,
            normalResolution,
            targetResolution,
            refreshHz
        );
    }

    private static string BuildDefaultConfigText()
    {
        StringBuilder sb = new StringBuilder();

        sb.AppendLine("# ResolutionToggle.config");
        sb.AppendLine("# The app reads this file on every run.");
        sb.AppendLine("# Lines starting with # are notes and are ignored.");
        sb.AppendLine();
        sb.AppendLine("# Display accepts 1, DISPLAY1, or \\\\.\\DISPLAY1.");
        sb.AppendLine("# Run the app with --list to see attached displays.");
        sb.AppendLine($"Display={TargetDisplay}");
        sb.AppendLine();
        sb.AppendLine("# Mode accepts Toggle, Low, Normal, or Target.");
        sb.AppendLine("# Toggle switches between LowResolution and NormalResolution.");
        sb.AppendLine("# Target always applies TargetResolution.");
        sb.AppendLine("Mode=Toggle");
        sb.AppendLine();
        sb.AppendLine($"LowResolution={LowWidth}x{LowHeight}");
        sb.AppendLine($"NormalResolution={NormalWidth}x{NormalHeight}");
        sb.AppendLine();
        sb.AppendLine("# Used only when Mode=Target. For one-time overrides, use --resolution=2560x1440.");
        sb.AppendLine($"TargetResolution={LowWidth}x{LowHeight}");
        sb.AppendLine();
        sb.AppendLine($"RefreshHz={RefreshHz}");

        return sb.ToString();
    }

    private static string GetConfigValue(
        IReadOnlyDictionary<string, string> values,
        string key,
        string defaultValue
    )
    {
        if (values.TryGetValue(key, out string? value))
        {
            return value;
        }

        return defaultValue;
    }

    private static string GetConfigPath()
    {
        return Path.Combine(AppContext.BaseDirectory, ConfigFileName);
    }

    private static string ParseDisplayName(string[] args, string configuredDisplayName)
    {
        const string prefix = "--display=";

        foreach (string arg in args)
        {
            if (!arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string value = arg.Substring(prefix.Length).Trim();
            return NormalizeDisplayName(value, "--display");
        }

        return configuredDisplayName;
    }

    private static uint ParseRefreshHz(string[] args, uint configuredRefreshHz)
    {
        const string prefix = "--refresh=";

        foreach (string arg in args)
        {
            if (!arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string value = arg.Substring(prefix.Length).Trim();
            return ParseUIntInRange(value, "--refresh", MinRefreshHz, MaxRefreshHz);
        }

        return configuredRefreshHz;
    }

    private static Resolution? ParseExplicitResolution(string[] args)
    {
        const string prefix = "--resolution=";

        foreach (string arg in args)
        {
            if (!arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string value = arg.Substring(prefix.Length).Trim();
            return ParseResolution(value, "--resolution");
        }

        return null;
    }

    private static Resolution ResolveTargetResolution(
        string[] args,
        AppConfig config,
        DEVMODE current,
        Resolution? explicitResolution
    )
    {
        bool forceLow = HasArg(args, "--low");
        bool forceNormal = HasArg(args, "--normal");

        if (forceLow && forceNormal)
        {
            throw new InvalidOperationException("Use either --low or --normal, not both.");
        }

        if (explicitResolution.HasValue && (forceLow || forceNormal))
        {
            throw new InvalidOperationException("Use --resolution by itself, without --low or --normal.");
        }

        if (explicitResolution.HasValue)
        {
            return explicitResolution.Value;
        }

        if (forceLow)
        {
            return config.LowResolution;
        }

        if (forceNormal)
        {
            return config.NormalResolution;
        }

        return config.Mode switch
        {
            RunMode.Low => config.LowResolution,
            RunMode.Normal => config.NormalResolution,
            RunMode.Target => config.TargetResolution,
            RunMode.Toggle when
                current.dmPelsWidth == config.LowResolution.Width &&
                current.dmPelsHeight == config.LowResolution.Height => config.NormalResolution,
            RunMode.Toggle => config.LowResolution,
            _ => throw new InvalidOperationException($"Unsupported mode: {config.Mode}")
        };
    }

    private static string NormalizeDisplayName(string value, string sourceName)
    {
        string trimmed = value.Trim();

        if (trimmed.Length == 0)
        {
            throw new InvalidOperationException($"{sourceName} needs a display value.");
        }

        if (int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out int number) && number > 0)
        {
            return $@"\\.\DISPLAY{number}";
        }

        const string fullPrefix = @"\\.\DISPLAY";
        const string shortPrefix = "DISPLAY";

        if (trimmed.StartsWith(fullPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeDisplaySuffix(trimmed.Substring(fullPrefix.Length), sourceName);
        }

        if (trimmed.StartsWith(shortPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeDisplaySuffix(trimmed.Substring(shortPrefix.Length), sourceName);
        }

        throw new InvalidOperationException(
            $"{sourceName} must be a display number like 1, a short name like DISPLAY1, " +
            "or a Win32 display name like \\\\.\\DISPLAY1."
        );
    }

    private static string NormalizeDisplaySuffix(string suffix, string sourceName)
    {
        if (int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out int number) && number > 0)
        {
            return $@"\\.\DISPLAY{number}";
        }

        throw new InvalidOperationException(
            $"{sourceName} must point to a display number greater than zero."
        );
    }

    private static RunMode ParseRunMode(string value, string sourceName)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "toggle" => RunMode.Toggle,
            "low" => RunMode.Low,
            "normal" => RunMode.Normal,
            "target" => RunMode.Target,
            "set" => RunMode.Target,
            "custom" => RunMode.Target,
            _ => throw new InvalidOperationException(
                $"{sourceName} must be Toggle, Low, Normal, or Target."
            )
        };
    }

    private static Resolution ParseResolution(string value, string sourceName)
    {
        string[] parts = value.Split(
            new[] { 'x', 'X' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );

        if (parts.Length != 2)
        {
            throw new InvalidOperationException($"{sourceName} must look like 1920x1080.");
        }

        uint width = ParseUIntInRange(parts[0], $"{sourceName} width", MinPixelDimension, MaxPixelDimension);
        uint height = ParseUIntInRange(parts[1], $"{sourceName} height", MinPixelDimension, MaxPixelDimension);

        return new Resolution(width, height);
    }

    private static uint ParseUIntInRange(string value, string sourceName, uint min, uint max)
    {
        if (
            !uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out uint result) ||
            result < min ||
            result > max
        )
        {
            throw new InvalidOperationException(
                $"{sourceName} must be a whole number from {min} to {max}."
            );
        }

        return result;
    }

    private static bool HasArg(string[] args, string name)
    {
        foreach (string arg in args)
        {
            if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void ApplyExactMode(string displayName, uint width, uint height, uint refreshHz)
    {
        DEVMODE? selectedMode = FindExactMode(displayName, width, height, refreshHz);

        if (!selectedMode.HasValue)
        {
            string availableRates = GetRefreshRatesForResolution(displayName, width, height);

            throw new InvalidOperationException(
                $"Could not find an exact mode for {displayName}:\n\n" +
                $"{width}x{height} @ {refreshHz} Hz\n\n" +
                $"Available refresh rates for {width}x{height} on {displayName}:\n\n" +
                $"{availableRates}\n\n" +
                "Because this app applies the configured refresh rate exactly, it will not fall back automatically."
            );
        }

        DEVMODE mode = selectedMode.Value;

        mode.dmFields |=
            DM_PELSWIDTH |
            DM_PELSHEIGHT |
            DM_BITSPERPEL |
            DM_DISPLAYFREQUENCY;

        int testResult = ChangeDisplaySettingsEx(
            displayName,
            ref mode,
            IntPtr.Zero,
            CDS_TEST,
            IntPtr.Zero
        );

        if (testResult != DISP_CHANGE_SUCCESSFUL)
        {
            throw new InvalidOperationException(
                $"Windows rejected the test for {displayName}:\n\n" +
                $"{width}x{height} @ {refreshHz} Hz\n\n" +
                $"Return code: {testResult} ({DisplayChangeResultToText(testResult)})"
            );
        }

        int applyResult = ChangeDisplaySettingsEx(
            displayName,
            ref mode,
            IntPtr.Zero,
            APPLY_FLAGS,
            IntPtr.Zero
        );

        if (applyResult != DISP_CHANGE_SUCCESSFUL)
        {
            throw new InvalidOperationException(
                $"Failed to apply mode for {displayName}:\n\n" +
                $"{width}x{height} @ {refreshHz} Hz\n\n" +
                $"Return code: {applyResult} ({DisplayChangeResultToText(applyResult)})"
            );
        }
    }

    private static DEVMODE? FindExactMode(string displayName, uint width, uint height, uint refreshHz)
    {
        DEVMODE? bestMode = null;

        for (int modeIndex = 0; ; modeIndex++)
        {
            DEVMODE mode = NewDevMode();

            if (!EnumDisplaySettings(displayName, modeIndex, ref mode))
            {
                break;
            }

            bool matches =
                mode.dmPelsWidth == width &&
                mode.dmPelsHeight == height &&
                mode.dmDisplayFrequency == refreshHz;

            if (!matches)
            {
                continue;
            }

            // Prefer the highest color depth if Windows exposes multiple matching modes.
            if (!bestMode.HasValue || mode.dmBitsPerPel > bestMode.Value.dmBitsPerPel)
            {
                bestMode = mode;
            }
        }

        return bestMode;
    }

    private static string GetRefreshRatesForResolution(string displayName, uint width, uint height)
    {
        SortedSet<uint> refreshRates = new SortedSet<uint>();

        for (int modeIndex = 0; ; modeIndex++)
        {
            DEVMODE mode = NewDevMode();

            if (!EnumDisplaySettings(displayName, modeIndex, ref mode))
            {
                break;
            }

            if (mode.dmPelsWidth == width && mode.dmPelsHeight == height)
            {
                refreshRates.Add(mode.dmDisplayFrequency);
            }
        }

        if (refreshRates.Count == 0)
        {
            return "No modes found for that resolution.";
        }

        return string.Join(", ", refreshRates) + " Hz";
    }

    private static DEVMODE GetCurrentMode(string displayName)
    {
        DEVMODE mode = NewDevMode();

        if (!EnumDisplaySettings(displayName, ENUM_CURRENT_SETTINGS, ref mode))
        {
            throw new InvalidOperationException($"Could not read current mode for {displayName}.");
        }

        return mode;
    }

    private static bool IsAttachedDesktopDisplay(string displayName)
    {
        for (uint i = 0; ; i++)
        {
            DISPLAY_DEVICE device = NewDisplayDevice();

            if (!EnumDisplayDevices(null, i, ref device, 0))
            {
                break;
            }

            bool attached = (device.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0;

            if (attached && string.Equals(device.DeviceName, displayName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildDisplayList(AppConfig config)
    {
        StringBuilder sb = new StringBuilder();

        sb.AppendLine("Attached desktop displays:");
        sb.AppendLine();

        bool foundAny = false;

        for (uint i = 0; ; i++)
        {
            DISPLAY_DEVICE device = NewDisplayDevice();

            if (!EnumDisplayDevices(null, i, ref device, 0))
            {
                break;
            }

            bool attached = (device.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0;

            if (!attached)
            {
                continue;
            }

            foundAny = true;

            string currentModeText;

            try
            {
                DEVMODE current = GetCurrentMode(device.DeviceName);
                currentModeText =
                    $"{current.dmPelsWidth}x{current.dmPelsHeight} @ {current.dmDisplayFrequency} Hz, " +
                    $"{current.dmBitsPerPel}-bit";
            }
            catch
            {
                currentModeText = "Could not read current mode";
            }

            sb.AppendLine(device.DeviceName);
            sb.AppendLine($"  Name:   {device.DeviceString}");
            sb.AppendLine($"  Flags:  {FormatDisplayFlags(device.StateFlags)}");
            sb.AppendLine($"  Mode:   {currentModeText}");
            sb.AppendLine();
        }

        if (!foundAny)
        {
            sb.AppendLine("No attached desktop displays were returned by Windows.");
        }

        sb.AppendLine("Config file:");
        sb.AppendLine($"  {GetConfigPath()}");
        sb.AppendLine();
        sb.AppendLine("Configured defaults:");
        sb.AppendLine($"  Display:          {config.DisplayName}");
        sb.AppendLine($"  Mode:             {config.Mode}");
        sb.AppendLine($"  LowResolution:    {config.LowResolution}");
        sb.AppendLine($"  NormalResolution: {config.NormalResolution}");
        sb.AppendLine($"  TargetResolution: {config.TargetResolution}");
        sb.AppendLine($"  RefreshHz:        {config.RefreshHz}");
        sb.AppendLine();
        sb.AppendLine("To target another display at runtime:");
        sb.AppendLine("  ResolutionToggle.exe --display=2");
        sb.AppendLine("  ResolutionToggle.exe --display=3");
        sb.AppendLine();
        sb.AppendLine("To target another display permanently:");
        sb.AppendLine("  Edit Display= in the config file above.");

        return sb.ToString();
    }

    private static string FormatDisplayFlags(int flags)
    {
        List<string> parts = new List<string>();

        if ((flags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0)
        {
            parts.Add("attached");
        }

        if ((flags & DISPLAY_DEVICE_PRIMARY_DEVICE) != 0)
        {
            parts.Add("primary");
        }

        if (parts.Count == 0)
        {
            return "none";
        }

        return string.Join(", ", parts);
    }

    private static DISPLAY_DEVICE NewDisplayDevice()
    {
        return new DISPLAY_DEVICE
        {
            cb = Marshal.SizeOf<DISPLAY_DEVICE>(),
            DeviceName = string.Empty,
            DeviceString = string.Empty,
            DeviceID = string.Empty,
            DeviceKey = string.Empty
        };
    }

    private static DEVMODE NewDevMode()
    {
        return new DEVMODE
        {
            dmDeviceName = string.Empty,
            dmFormName = string.Empty,
            dmSize = (ushort)Marshal.SizeOf<DEVMODE>(),
            dmDriverExtra = 0
        };
    }

    private static string DisplayChangeResultToText(int result)
    {
        return result switch
        {
            DISP_CHANGE_SUCCESSFUL => "successful",
            DISP_CHANGE_RESTART => "restart required",
            DISP_CHANGE_FAILED => "display driver failed the mode",
            DISP_CHANGE_BADMODE => "unsupported graphics mode",
            DISP_CHANGE_NOTUPDATED => "registry could not be updated",
            DISP_CHANGE_BADFLAGS => "invalid flags",
            DISP_CHANGE_BADPARAM => "invalid parameter",
            DISP_CHANGE_BADDUALVIEW => "bad DualView state",
            _ => "unknown result"
        };
    }

    private static void ShowInfo(string message, string title)
    {
        MessageBox(IntPtr.Zero, message, title, MB_OK | MB_ICONINFORMATION);
    }

    private static void ShowError(string message)
    {
        MessageBox(IntPtr.Zero, message, "Resolution Toggle Error", MB_OK | MB_ICONERROR);
    }
}
