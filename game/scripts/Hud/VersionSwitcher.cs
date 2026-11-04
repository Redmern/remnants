using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using Remnants.Core.Versions;

namespace Remnants.Game.Hud;

/// <summary>
/// Settings > Game "Versions" rows: the running version, a picker filled from the GitHub releases (Refresh), and Switch,
/// which downloads the chosen build's zip into <c>user://versions/&lt;tag&gt;/</c>, extracts it, starts its exe and quits this
/// one, recording it as the active version so later boots run it too (see <see cref="RedirectAtBoot"/>). Saves and settings
/// live elsewhere in <c>user://</c> and are left alone. Every failure lands in the status line and keeps this game running.
/// </summary>
public partial class VersionSwitcher : VBoxContainer
{
    private const string VersionsDir = "user://versions";
    private const string ActiveVersionFile = "user://" + ActiveVersion.FileName;
    private static readonly string[] Headers = { "User-Agent: Remnants-Game", "Accept: application/vnd.github+json" };

    private readonly OptionButton _pick = new() { FocusMode = FocusModeEnum.None, Disabled = true };
    private readonly Button _refresh;
    private readonly Button _switch;
    private readonly Label _status = Widgets.Wrapped("Refresh to list the published versions.", "Small", Palette.TextMuted);
    private readonly ProgressBar _progress = new() { Visible = false, MinValue = 0, MaxValue = 1, Step = 0, CustomMinimumSize = new Vector2(0, 16) };
    private readonly HttpRequest _api = new() { Timeout = 20, UseThreads = true };
    private readonly HttpRequest _download = new() { UseThreads = true };
    private readonly List<GameRelease> _releases = new();
    private GameRelease? _downloading;
    private bool _busy;

    /// <summary>The running build's version, from <c>application/config/version</c> in project.godot.</summary>
    public static string CurrentVersion => ProjectSettings.GetSetting("application/config/version", "").AsString();

    private static string Root => ProjectSettings.GlobalizePath(VersionsDir);

    private static string ActiveFile => ProjectSettings.GlobalizePath(ActiveVersionFile);

    private static bool _bootChecked;

    /// <summary>
    /// Once per process, before the menu is built: starts the active version when this exported Windows build is not it. True
    /// when it did and this game is quitting. Editor and dev runs stay put, and a failed start just carries on booting.
    /// </summary>
    public static bool RedirectAtBoot(SceneTree tree)
    {
        if (_bootChecked) return false;
        _bootChecked = true;
        if (!OS.HasFeature("template") || OS.GetName() != "Windows") return false;
        var boot = ActiveVersion.DecideBoot(ActiveVersion.Read(ActiveFile), CurrentVersion, Root, OS.GetExecutablePath());
        if (boot.PersistTag is { } tag) Persist(tag);
        if (boot.RedirectExe is not { } exe) return false;
        if (OS.CreateProcess(exe, Array.Empty<string>()) <= 0)
        {
            GD.PushWarning($"[versions] Could not start the active version at {exe}.");
            return false;
        }
        tree.Quit();
        return true;
    }

    private static void Persist(string tag)
    {
        if (!ActiveVersion.Write(ActiveFile, tag)) GD.PushWarning($"[versions] Could not record {tag} as the active version.");
    }

    public VersionSwitcher()
    {
        AddThemeConstantOverride("separation", 8);
        AddChild(_api);
        AddChild(_download);
        _api.RequestCompleted += OnListed;
        _download.RequestCompleted += OnDownloaded;

        _refresh = Widgets.TextButton("Refresh", Refresh, "Fetch the list of published versions from GitHub.", minWidth: 90);
        _switch = Widgets.TextButton("Switch", Switch, "Download the chosen version if needed, start it and close this one.", "Primary", 90);
        _switch.Disabled = true;
        _pick.ItemSelected += _ => UpdateSwitch();

        var row = Widgets.Row(8);
        _pick.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(_pick);
        row.AddChild(_refresh);
        row.AddChild(_switch);
        AddChild(row);
        AddChild(_progress);
        AddChild(_status);
        SetProcess(false);
    }

    /// <summary>
    /// Capture runs: <c>--versions-refresh</c> fetches at once; <c>--versions-url=</c> points the fetch elsewhere (to see the
    /// error states); <c>--versions-switch=&lt;tag&gt;</c> then picks that tag and presses Switch.
    /// </summary>
    public override void _Ready()
    {
        if (DebugCapture.HasFlag("--versions-refresh")) Refresh();
    }

    private void Refresh()
    {
        if (_busy) return;
        var err = _api.Request(DebugCapture.Value("--versions-url") ?? ReleaseCatalog.ReleasesUrl, Headers);
        if (err != Error.Ok)
        {
            Status($"Could not start the request ({err}).", Palette.Danger);
            return;
        }
        SetBusy(true);
        Status("Fetching versions…", Palette.TextMuted);
    }

    private void OnListed(long result, long code, string[] headers, byte[] body)
    {
        SetBusy(false);
        if (result != (long)HttpRequest.Result.Success)
        {
            Status($"Could not reach GitHub; are you offline? ({(HttpRequest.Result)result})", Palette.Danger);
            return;
        }
        var list = ReleaseCatalog.Parse(System.Text.Encoding.UTF8.GetString(body));
        if (code != 200)
        {
            Status($"GitHub answered HTTP {code}." + (list.Error is { } why && why.StartsWith("GitHub:") ? $" {why}" : ""), Palette.Danger);
            return;
        }
        if (list.Error is not null)
        {
            Status(list.Error, Palette.Danger);
            return;
        }
        _releases.Clear();
        _releases.AddRange(list.Releases);
        _pick.Clear();
        var current = CurrentVersion;
        var select = 0;
        for (var i = 0; i < _releases.Count; i++)
        {
            _pick.AddItem(ReleaseCatalog.Label(_releases[i], current, IsInstalled(_releases[i])));
            if (ReleaseCatalog.IsVersion(_releases[i].Tag, current)) select = i;
        }
        if (_releases.Count == 0)
        {
            Status("No versions are published yet.", Palette.Warn);
            UpdateSwitch();
            return;
        }
        _pick.Select(select);
        Status($"{_releases.Count} version{(_releases.Count == 1 ? "" : "s")} found.", Palette.TextMuted);
        UpdateSwitch();
        if (DebugCapture.Value("--versions-switch") is { } tag && _releases.FindIndex(r => r.Tag == tag) is var at and >= 0)
        {
            _pick.Select(at);
            UpdateSwitch();
            if (!_switch.Disabled) Switch();
        }
    }

    private GameRelease? Selected => _pick.Selected >= 0 && _pick.Selected < _releases.Count ? _releases[_pick.Selected] : null;

    private static string InstallDir(GameRelease release) => Path.Combine(Root, VersionInstall.DirName(release.Tag));

    private static bool IsInstalled(GameRelease release) => VersionInstall.IsInstalled(InstallDir(release));

    private void UpdateSwitch()
    {
        _pick.Disabled = _busy || _releases.Count == 0;
        _refresh.Disabled = _busy;
        var release = Selected;
        _switch.Disabled = _busy || !ReleaseCatalog.CanSwitch(release, CurrentVersion, release is not null && IsInstalled(release));
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        UpdateSwitch();
    }

    private void Switch()
    {
        var release = Selected;
        if (_busy || release is null) return;
        if (IsInstalled(release))
        {
            Launch(release);
            return;
        }
        try
        {
            Directory.CreateDirectory(Root);
        }
        catch (Exception e)
        {
            Status($"Could not create the versions folder: {e.Message}", Palette.Danger);
            return;
        }
        _download.DownloadFile = ZipPath(release);
        var err = _download.Request(release.ZipUrl!, Headers);
        if (err != Error.Ok)
        {
            Status($"Could not start the download ({err}).", Palette.Danger);
            return;
        }
        _downloading = release;
        SetBusy(true);
        _progress.Value = 0;
        _progress.Visible = true;
        SetProcess(true);
        Status($"Downloading {release.Tag}…", Palette.TextMuted);
    }

    private static string ZipPath(GameRelease release) => InstallDir(release) + ".zip.download";

    public override void _Process(double delta)
    {
        if (_downloading is null) return;
        var total = _download.GetBodySize() > 0 ? _download.GetBodySize() : _downloading.ZipSize;
        var done = _download.GetDownloadedBytes();
        if (total > 0)
        {
            _progress.Value = Math.Clamp((double)done / total, 0, 1);
            Status($"Downloading {_downloading.Tag}… {done / 1048576.0:0.0} / {total / 1048576.0:0.0} MB", Palette.TextMuted);
        }
    }

    private async void OnDownloaded(long result, long code, string[] headers, byte[] body)
    {
        SetProcess(false);
        var release = _downloading!;
        _downloading = null;
        var zip = ZipPath(release);
        if (result != (long)HttpRequest.Result.Success || code != 200)
        {
            Fail(result != (long)HttpRequest.Result.Success
                ? $"Download failed; are you offline? ({(HttpRequest.Result)result})"
                : $"Download failed: HTTP {code}.", zip);
            return;
        }
        _progress.Value = 1;
        Status($"Unpacking {release.Tag}…", Palette.TextMuted);
        try
        {
            var dir = InstallDir(release);
            await Task.Run(() => VersionInstall.Extract(zip, dir));
        }
        catch (Exception e)
        {
            if (IsInstanceValid(this)) Fail($"Could not unpack {release.Tag}: {e.Message}", zip);
            return;
        }
        TryDelete(zip);
        if (!IsInstanceValid(this)) return;
        _progress.Visible = false;
        SetBusy(false);
        Launch(release);
    }

    private void Fail(string message, string zip)
    {
        TryDelete(zip);
        _progress.Visible = false;
        SetBusy(false);
        Status(message, Palette.Danger);
    }

    private void Launch(GameRelease release)
    {
        var exe = VersionInstall.FindExe(InstallDir(release));
        if (exe is null)
        {
            Status($"{release.Tag} is not installed correctly; no {VersionInstall.ExeName} found.", Palette.Danger);
            return;
        }
        if (OS.GetName() != "Windows")
        {
            Status($"{release.Tag} is installed, but starting it only works on Windows. It is in {Path.GetDirectoryName(exe)}.", Palette.Warn);
            RefreshLabels();
            return;
        }
        var pid = OS.CreateProcess(exe, Array.Empty<string>());
        if (pid <= 0)
        {
            Status($"Could not start {release.Tag}.", Palette.Danger);
            RefreshLabels();
            return;
        }
        Persist(release.Tag);
        GameSettings.Save();
        GetTree().Quit();
    }

    private void RefreshLabels()
    {
        var current = CurrentVersion;
        for (var i = 0; i < _releases.Count; i++) _pick.SetItemText(i, ReleaseCatalog.Label(_releases[i], current, IsInstalled(_releases[i])));
        UpdateSwitch();
    }

    private void Status(string text, Color color)
    {
        _status.Text = text;
        _status.AddThemeColorOverride("font_color", color);
        if (DebugCapture.HasFlag("--versions-refresh")) GD.Print($"[versions] {text}");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
