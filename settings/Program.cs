using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

internal enum RightStickMode { FullCamera, HorizontalTurn }
internal enum AutoBattleMode { NormalAttackOnly, SkillPriority }
[Flags] internal enum ControllerContext { None = 0, Field = 1, Battle = 2, Puzzle = 4, WorldMap = 8, Menu = 16, All = 31 }
internal enum ControllerButton { None, A, B, X, Y, LB, RB, LT, RT, L3, R3, Start, Select, DPadUp, DPadDown, DPadLeft, DPadRight }
internal enum ControllerActionBehavior { Press, Hold, LongPress, Toggle, DoublePress }

internal sealed class SettingsModel
{
    public string UiLanguage { get; set; } = "Auto";
    public RightStickMode RightStickMode { get; set; } = RightStickMode.FullCamera;
    public bool InvertX { get; set; }
    public bool InvertY { get; set; }
    public float SensitivityX { get; set; } = 1.0f;
    public float SensitivityY { get; set; } = 1.0f;
    public float DeadZone { get; set; } = 0.15f;
    public AutoBattleMode AutoBattleMode { get; set; } = AutoBattleMode.NormalAttackOnly;
    public float AutoBattleSpeed { get; set; } = 1.0f;
    public bool RightStickEnabled { get; set; } = true;
    public bool DashEnabled { get; set; } = true;
    public bool QuickHealEnabled { get; set; } = true;
    public bool ForceEncounterEnabled { get; set; } = true;
    public bool SmartAutoEnabled { get; set; } = true;
}

internal sealed class ActionDefinition
{
    public string ModId { get; set; } = string.Empty;
    public string ActionId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ControllerContext Contexts { get; set; }
    public ControllerActionBehavior Behavior { get; set; }
}

internal sealed class BindingEntry
{
    public ControllerContext Context { get; set; }
    public List<ControllerButton> Buttons { get; set; } = new();
    public string ActionId { get; set; } = string.Empty;
}

internal sealed class FeatureMetadata
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string Category { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool RequiresRestart { get; set; }
    public bool ReadOnly { get; set; }
    public string Version { get; set; } = string.Empty;
    public string Warning { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

internal sealed class FeatureProviderMetadata
{
    public string ProviderId { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public List<FeatureMetadata> Features { get; set; } = new();
    public string Error { get; set; } = string.Empty;
}

internal sealed class FeatureToggleRequest
{
    public string ProviderId { get; set; } = string.Empty;
    public string FeatureId { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string settings = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "NocturneModernController.settings.json");
        string registry = args.Length > 1 ? args[1] : Path.Combine(Path.GetDirectoryName(settings) ?? string.Empty, "NocturneModernController.actions.json");
        string bindings = args.Length > 2 ? args[2] : Path.Combine(Path.GetDirectoryName(settings) ?? string.Empty, "NocturneModernController.bindings.json");
        string features = args.Length > 3 ? args[3] : Path.Combine(Path.GetDirectoryName(settings) ?? string.Empty, "NocturneModernController.features.json");
        string featureRequests = args.Length > 4 ? args[4] : Path.Combine(Path.GetDirectoryName(settings) ?? string.Empty, "NocturneModernController.feature-requests.json");
        int gamePid = args.Length > 5 && int.TryParse(args[5], out int parsedPid) ? parsedPid : 0;
        Application.Run(new SettingsForm(settings, registry, bindings, features, featureRequests, gamePid));
    }
}

internal sealed class SettingsForm : Form
{
    private readonly string _settingsPath;
    private readonly string _bindingsPath;
    private readonly string _featureRequestsPath;
    private readonly int _gamePid;
    private readonly List<ActionDefinition> _actions;
    private readonly List<BindingEntry> _bindings;
    private readonly List<FeatureProviderMetadata> _featureProviders;
    private readonly Dictionary<CheckBox, (string ProviderId, string FeatureId, bool Initial)> _featureToggles = new();
    private readonly ComboBox _mode = NewCombo();
    private readonly ComboBox _horizontalAxis = NewCombo();
    private readonly ComboBox _verticalAxis = NewCombo();
    private readonly NumericUpDown _sensitivityX = NewNumber(0.10M, 3.00M, 0.05M);
    private readonly NumericUpDown _sensitivityY = NewNumber(0.10M, 3.00M, 0.05M);
    private readonly NumericUpDown _deadZone = NewNumber(0.00M, 0.95M, 0.01M);
    private readonly ComboBox _context = NewCombo();
    private readonly ComboBox _autoBattleMode = NewCombo();
    private readonly ComboBox _autoBattleSpeed = NewCombo();
    private readonly ControllerCanvas _padPanel = new ControllerCanvas();
    private readonly Dictionary<ControllerButton, Button> _padButtons = new();
    private readonly ToolTip _bindingTips = new ToolTip();
    private readonly ComboBox _uiLanguage = NewCombo();
    private readonly bool _japanese;

    internal SettingsForm(string settingsPath, string registryPath, string bindingsPath, string featuresPath, string featureRequestsPath, int gamePid)
    {
        _settingsPath = settingsPath;
        _bindingsPath = bindingsPath;
        _featureRequestsPath = featureRequestsPath;
        _gamePid = gamePid;
        _actions = Read<List<ActionDefinition>>(registryPath) ?? new List<ActionDefinition>();
        _bindings = Read<List<BindingEntry>>(bindingsPath) ?? new List<BindingEntry>();
        _featureProviders = Read<List<FeatureProviderMetadata>>(featuresPath) ?? new List<FeatureProviderMetadata>();
        SettingsModel initialSettings = Read<SettingsModel>(_settingsPath) ?? new SettingsModel();
        _japanese = initialSettings.UiLanguage.Equals("Japanese", StringComparison.OrdinalIgnoreCase) ||
            (initialSettings.UiLanguage.Equals("Auto", StringComparison.OrdinalIgnoreCase) &&
             CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ja", StringComparison.OrdinalIgnoreCase));
        _padPanel.Japanese = _japanese;

        Text = L("Nocturne Modern Controller - 統合キーコンフィグ", "Nocturne Modern Controller - Settings");
        ClientSize = new Size(940, 650);
        MinimumSize = new Size(900, 620);
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        Font = new Font("Yu Gothic UI", 10F);
        BackColor = Color.FromArgb(22, 28, 37);
        ForeColor = Color.WhiteSmoke;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildBindingsPage());
        tabs.TabPages.Add(BuildCameraPage());
        tabs.TabPages.Add(BuildAutoBattlePage());
        tabs.TabPages.Add(BuildFeaturesPage());
        Controls.Add(tabs);
        Controls.Add(BuildButtons());

        LoadCameraSettings();
        RefreshPadLabels();
        Shown += (_, _) => SetGameWindowState(minimize: true);
        FormClosed += (_, _) => SetGameWindowState(minimize: false);
    }

    private TabPage BuildBindingsPage()
    {
        var page = NewPage(L("キーコンフィグ", "Key Bindings"));
        page.Controls.Add(new Label
        {
            Text = L("場面を選び、パッドのボタンをクリックして機能を割り当てます。最大3ボタンの同時押しに対応します。",
                     "Choose a context, then click a controller button to assign an action. Chords support up to three buttons."),
            Location = new Point(22, 18), AutoSize = true, ForeColor = Color.Gainsboro
        });
        _context.Items.AddRange(new object[] { ControllerContext.Field, ControllerContext.Battle, ControllerContext.Puzzle, ControllerContext.Menu });
        _context.FormattingEnabled = true;
        _context.Format += (_, e) =>
        {
            if (e.ListItem is ControllerContext context)
            {
                e.Value = GetContextDisplayName(context);
            }
        };
        _context.SelectedIndex = 0;
        _context.Location = new Point(22, 50);
        _context.Width = 180;
        _context.SelectedIndexChanged += (_, _) => RefreshPadLabels();
        page.Controls.Add(_context);

        _padPanel.Location = new Point(22, 90);
        _padPanel.Size = new Size(850, 450);
        _padPanel.BackColor = Color.FromArgb(31, 39, 51);
        page.Controls.Add(_padPanel);
        AddPadButton(ControllerButton.LT, 120, 24, 112, 36);
        AddPadButton(ControllerButton.LB, 145, 70, 104, 36);
        AddPadButton(ControllerButton.RT, 618, 24, 112, 36);
        AddPadButton(ControllerButton.RB, 600, 70, 104, 36);
        AddPadButton(ControllerButton.L3, 218, 170, 72, 52);
        AddPadButton(ControllerButton.R3, 480, 266, 72, 52);
        AddPadButton(ControllerButton.DPadUp, 302, 246, 72, 38);
        AddPadButton(ControllerButton.DPadLeft, 254, 286, 72, 38);
        AddPadButton(ControllerButton.DPadRight, 350, 286, 72, 38);
        AddPadButton(ControllerButton.DPadDown, 302, 326, 72, 38);
        AddPadButton(ControllerButton.Y, 629, 168, 58, 46);
        AddPadButton(ControllerButton.X, 574, 218, 58, 46);
        AddPadButton(ControllerButton.B, 684, 218, 58, 46);
        AddPadButton(ControllerButton.A, 629, 268, 58, 46);
        AddPadButton(ControllerButton.Select, 374, 190, 70, 34);
        AddPadButton(ControllerButton.Start, 451, 190, 70, 34);
        return page;
    }

    private static string GetContextDisplayName(ControllerContext context)
    {
        return context switch
        {
            ControllerContext.Field => "FIELD/DUNGEON",
            ControllerContext.Battle => "BATTLE",
            ControllerContext.Puzzle => "PUZZLE",
            ControllerContext.Menu => "MENU",
            _ => context.ToString()
        };
    }

    private TabPage BuildCameraPage()
    {
        var page = NewPage(L("右スティック", "Right Stick"));
        _mode.Items.AddRange(new object[] { "Full Camera", "Horizontal Turn" });
        _horizontalAxis.Items.AddRange(new object[] { "Normal", "Inverted" });
        _verticalAxis.Items.AddRange(new object[] { "Normal", "Inverted" });
        var grid = new TableLayoutPanel { Location = new Point(30, 30), Size = new Size(650, 330), ColumnCount = 2 };
        AddRow(grid, "Right Stick Mode", _mode);
        AddRow(grid, "Horizontal Axis", _horizontalAxis);
        AddRow(grid, "Vertical Axis", _verticalAxis);
        AddRow(grid, "Sensitivity X", _sensitivityX);
        AddRow(grid, "Sensitivity Y", _sensitivityY);
        AddRow(grid, "Dead Zone", _deadZone);
        page.Controls.Add(grid);
        return page;
    }

    private TabPage BuildAutoBattlePage()
    {
        var page = NewPage(L("オートバトル", "Auto Battle"));
        _autoBattleMode.Items.AddRange(new object[] { "Normal Attack Only", "Skill Priority (Test)" });
        _autoBattleSpeed.Items.AddRange(new object[] { "1.0x", "1.5x", "2.0x" });
        _autoBattleMode.SelectedIndex = 0;
        _autoBattleSpeed.SelectedIndex = 0;
        var grid = new TableLayoutPanel
        {
            Location = new Point(30, 35), Size = new Size(650, 130), ColumnCount = 2
        };
        AddRow(grid, "Auto Battle Mode", _autoBattleMode);
        AddRow(grid, "Auto Battle Speed", _autoBattleSpeed);
        page.Controls.Add(grid);
        page.Controls.Add(new Label
        {
            Text = L("Normal Attack Only はゲーム標準Autoの安全なコマンド経路を使用します。\n" +
                     "Skill Priority は安全な弱点スキルを優先し、危険時は通常攻撃へ戻ります。\n" +
                     "速度変更は標準AutoがONの戦闘中だけ適用されます。",
                     "Normal Attack Only uses the game's safe standard Auto command path.\n" +
                     "Skill Priority favors safe weakness attacks and falls back to normal attacks when needed.\n" +
                     "Speed changes apply only while standard Auto is active in battle."),
            Location = new Point(30, 185), AutoSize = true, ForeColor = Color.Gainsboro
        });
        return page;
    }

    private TabPage BuildFeaturesPage()
    {
        var page = NewPage(L("MOD機能", "MOD Features"));
        var list = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(18, 16, 18, 16),
            BackColor = page.BackColor
        };

        if (_featureProviders.Count == 0)
        {
            list.Controls.Add(new Label
            {
                Text = L("機能メタデータを公開しているNocturneModern Providerはありません。",
                         "No NocturneModern providers are publishing feature metadata."),
                AutoSize = true,
                ForeColor = Color.Gainsboro,
                Margin = new Padding(8, 10, 8, 10)
            });
        }

        foreach (FeatureProviderMetadata provider in _featureProviders)
        {
            string providerTitle = provider.ProviderName;
            if (!string.IsNullOrWhiteSpace(provider.Version))
            {
                providerTitle += "  " + provider.Version;
            }
            list.Controls.Add(new Label
            {
                Text = providerTitle,
                AutoSize = false,
                Width = 840,
                Height = 34,
                Font = new Font("Yu Gothic UI", 12F, FontStyle.Bold),
                ForeColor = Color.FromArgb(74, 204, 188),
                Margin = new Padding(4, 12, 4, 2)
            });

            if (!string.IsNullOrWhiteSpace(provider.Error))
            {
                list.Controls.Add(BuildFeatureMessage(
                    L("Providerのメタデータを取得できませんでした: ", "Could not read provider metadata: ") + provider.Error,
                    Color.FromArgb(225, 164, 82)));
                continue;
            }
            if (provider.Features.Count == 0)
            {
                list.Controls.Add(BuildFeatureMessage(L("公開中の機能はありません。", "No features are currently published."), Color.Silver));
                continue;
            }

            foreach (FeatureMetadata feature in provider.Features
                         .OrderBy(item => item.SortOrder).ThenBy(item => item.Name))
            {
                Panel card = BuildFeatureCard(provider, feature);
                list.Controls.Add(card);
            }
        }
        page.Controls.Add(list);
        return page;
    }

    private Panel BuildFeatureCard(FeatureProviderMetadata provider, FeatureMetadata feature)
    {
        int descriptionHeight = string.IsNullOrWhiteSpace(feature.Description) ? 0 : 42;
        int extraHeight = string.IsNullOrWhiteSpace(feature.Warning) ? 0 : 28;
        var card = new Panel
        {
            Width = 840,
            Height = 78 + descriptionHeight + extraHeight,
            BackColor = Color.FromArgb(31, 39, 51),
            Margin = new Padding(4, 5, 4, 5),
            Padding = new Padding(14)
        };
        var toggle = new CheckBox
        {
            Checked = feature.Enabled,
            Enabled = !feature.ReadOnly,
            AutoSize = true,
            Location = new Point(16, 19),
            ForeColor = Color.WhiteSmoke
        };
        _featureToggles[toggle] = (provider.ProviderId, feature.Id, feature.Enabled);
        card.Controls.Add(toggle);
        card.Controls.Add(new Label
        {
            Text = GetFeatureName(provider, feature),
            Location = new Point(62, 12),
            Size = new Size(740, 26),
            Font = new Font("Yu Gothic UI", 10.5F, FontStyle.Bold),
            ForeColor = Color.White
        });
        string category = string.IsNullOrWhiteSpace(feature.Category) ? "Other" : feature.Category;
        if (feature.RequiresRestart)
        {
            category += L("  •  再起動が必要", "  •  Restart required");
        }
        card.Controls.Add(new Label
        {
            Text = category,
            Location = new Point(62, 39),
            Size = new Size(740, 22),
            ForeColor = Color.FromArgb(130, 198, 190)
        });
        if (descriptionHeight > 0)
        {
            card.Controls.Add(new Label
            {
                Text = GetFeatureDescription(provider, feature),
                Location = new Point(62, 64),
                Size = new Size(740, descriptionHeight),
                AutoEllipsis = false,
                ForeColor = Color.Gainsboro
            });
        }
        if (extraHeight > 0)
        {
            card.Controls.Add(new Label
            {
                Text = feature.Warning,
                Location = new Point(62, 66 + descriptionHeight),
                Size = new Size(740, extraHeight),
                ForeColor = Color.FromArgb(225, 164, 82)
            });
        }
        return card;
    }

    private static Label BuildFeatureMessage(string text, Color color) => new Label
    {
        Text = text,
        AutoSize = false,
        Width = 840,
        Height = 38,
        ForeColor = color,
        Margin = new Padding(8, 4, 8, 8)
    };

    private Control BuildButtons()
    {
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 58, BackColor = Color.FromArgb(15, 20, 27) };
        _uiLanguage.Items.AddRange(new object[] { "Auto", "日本語", "English" });
        SettingsModel current = Read<SettingsModel>(_settingsPath) ?? new SettingsModel();
        _uiLanguage.SelectedIndex = current.UiLanguage.Equals("Japanese", StringComparison.OrdinalIgnoreCase) ? 1 :
            current.UiLanguage.Equals("English", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
        _uiLanguage.Location = new Point(485, 14); _uiLanguage.Size = new Size(145, 30);
        var languageLabel = new Label { Text = L("言語（再表示時に反映）", "Language (applies on reopen)"), Location = new Point(280, 18), AutoSize = true };
        var cancel = new Button { Text = L("キャンセル", "Cancel"), Location = new Point(700, 12), Size = new Size(105, 34) };
        var ok = new Button { Text = L("OK / 保存", "OK / Save"), Location = new Point(815, 12), Size = new Size(105, 34), BackColor = Color.FromArgb(74, 204, 188) };
        cancel.Click += (_, _) => Close();
        ok.Click += (_, _) => SaveAndClose();
        bar.Controls.Add(languageLabel);
        bar.Controls.Add(_uiLanguage);
        bar.Controls.Add(cancel);
        bar.Controls.Add(ok);
        AcceptButton = ok;
        CancelButton = cancel;
        return bar;
    }

    private void AddPadButton(ControllerButton button, int x, int y, int width, int height)
    {
        var control = new Button
        {
            Tag = button, Text = button.ToString(), Location = new Point(x, y), Size = new Size(width, height),
            FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(55, 68, 85), ForeColor = Color.White,
            Cursor = Cursors.Hand, Font = new Font("Yu Gothic UI", 8.5F, FontStyle.Bold)
        };
        control.Click += (_, _) => EditButton(button);
        _padButtons[button] = control;
        _padPanel.Controls.Add(control);
    }

    private void EditButton(ControllerButton primary)
    {
        ControllerContext context = (ControllerContext)_context.SelectedItem;
        using var dialog = new AssignmentDialog(context, primary, _actions, _bindings, _japanese);
        TopMost = false;
        dialog.TopMost = true;
        try
        {
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                RefreshPadLabels();
            }
        }
        finally
        {
            TopMost = true;
            Activate();
        }
    }

    private void SetGameWindowState(bool minimize)
    {
        if (_gamePid <= 0) return;
        try
        {
            Process process = Process.GetProcessById(_gamePid);
            IntPtr window = process.MainWindowHandle;
            if (window == IntPtr.Zero) return;
            ShowWindowAsync(window, minimize ? 6 : 9);
            if (!minimize) SetForegroundWindow(window);
        }
        catch { }
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    private void RefreshPadLabels()
    {
        if (_context.SelectedItem == null) return;
        ControllerContext context = (ControllerContext)_context.SelectedItem;
        foreach (KeyValuePair<ControllerButton, Button> pair in _padButtons)
        {
            string[] names = _bindings
                .Where(binding => binding.Context == context && binding.Buttons.Contains(pair.Key))
                .Select(binding => GetActionDisplayName(binding.ActionId))
                .Distinct().Take(2).ToArray();
            pair.Value.Text = pair.Key.ToString();
            string assignment = names.Length == 0 ? L("未割当", "Unassigned") : string.Join(" / ", names);
            _bindingTips.SetToolTip(pair.Value, pair.Key + "\n" + assignment + "\n" + L("クリックして割当を編集", "Click to edit bindings"));
        }
    }

    private void LoadCameraSettings()
    {
        SettingsModel model = Read<SettingsModel>(_settingsPath) ?? new SettingsModel();
        _mode.SelectedIndex = model.RightStickMode == RightStickMode.HorizontalTurn ? 1 : 0;
        _horizontalAxis.SelectedIndex = model.InvertX ? 1 : 0;
        _verticalAxis.SelectedIndex = model.InvertY ? 1 : 0;
        _sensitivityX.Value = Clamp((decimal)model.SensitivityX, _sensitivityX);
        _sensitivityY.Value = Clamp((decimal)model.SensitivityY, _sensitivityY);
        _deadZone.Value = Clamp((decimal)model.DeadZone, _deadZone);
        _autoBattleMode.SelectedIndex = model.AutoBattleMode == AutoBattleMode.SkillPriority ? 1 : 0;
        _autoBattleSpeed.SelectedIndex = model.AutoBattleSpeed >= 1.9f
            ? 2
            : model.AutoBattleSpeed >= 1.4f ? 1 : 0;
    }

    private void SaveAndClose()
    {
        SettingsModel settings = Read<SettingsModel>(_settingsPath) ?? new SettingsModel();
        settings.UiLanguage = _uiLanguage.SelectedIndex == 1 ? "Japanese" : _uiLanguage.SelectedIndex == 2 ? "English" : "Auto";
        settings.RightStickMode = _mode.SelectedIndex == 1 ? RightStickMode.HorizontalTurn : RightStickMode.FullCamera;
        settings.InvertX = _horizontalAxis.SelectedIndex == 1;
        settings.InvertY = _verticalAxis.SelectedIndex == 1;
        settings.SensitivityX = (float)_sensitivityX.Value;
        settings.SensitivityY = (float)_sensitivityY.Value;
        settings.DeadZone = (float)_deadZone.Value;
        settings.AutoBattleMode = _autoBattleMode.SelectedIndex == 1
                ? AutoBattleMode.SkillPriority
                : AutoBattleMode.NormalAttackOnly;
        settings.AutoBattleSpeed = _autoBattleSpeed.SelectedIndex == 2
                ? 2.0f
                : _autoBattleSpeed.SelectedIndex == 1 ? 1.5f : 1.0f;
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, options));
        File.WriteAllText(_bindingsPath, JsonSerializer.Serialize(_bindings, options));
        var requests = _featureToggles
            .Where(pair => pair.Key.Checked != pair.Value.Initial)
            .Select(pair => new FeatureToggleRequest
            {
                ProviderId = pair.Value.ProviderId,
                FeatureId = pair.Value.FeatureId,
                Enabled = pair.Key.Checked
            })
            .ToList();
        File.WriteAllText(_featureRequestsPath, JsonSerializer.Serialize(requests, options));
        Close();
    }

    private static T? Read<T>(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) : default; }
        catch { return default; }
    }

    private string L(string japanese, string english) => _japanese ? japanese : english;

    private string GetActionDisplayName(string actionId)
    {
        if (!_japanese)
        {
            return actionId switch
            {
                "nocturne-modern-controller.dash" => "Dash",
                "nocturne-modern-controller.dash-keep" => "Toggle Dash Keep",
                "nocturne-modern-controller.quick-heal" => "Quick Heal",
                "nocturne-modern-controller.force-encounter" => "Force Encounter",
                "nocturne-modern-controller.open-settings" => "Open Settings",
                _ => _actions.FirstOrDefault(action => action.ActionId == actionId)?.DisplayName ?? actionId
            };
        }
        return _actions.FirstOrDefault(action => action.ActionId == actionId)?.DisplayName ?? actionId;
    }

    private string GetFeatureName(FeatureProviderMetadata provider, FeatureMetadata feature) => feature.Name;

    private string GetFeatureDescription(FeatureProviderMetadata provider, FeatureMetadata feature)
    {
        if (_japanese || !provider.ProviderId.Equals("nocturne_modern_controller", StringComparison.OrdinalIgnoreCase))
            return feature.Description;
        return feature.Id switch
        {
            "right_stick_camera" => "Use the right stick for dungeon turning and vertical camera control.",
            "dash" => "Increase movement speed in dungeons and on the world map.",
            "quick_heal" => "Use learned recovery skills and real MP to heal the party while exploring.",
            "force_encounter" => "Request a battle only where normal encounters are available.",
            "smart_auto" => "Choose standard Auto commands using weaknesses, resistances, MP, and attack predictions.",
            _ => feature.Description
        };
    }

    private static TabPage NewPage(string text) => new TabPage(text) { BackColor = Color.FromArgb(22, 28, 37), ForeColor = Color.WhiteSmoke };
    private static ComboBox NewCombo() => new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
    private static NumericUpDown NewNumber(decimal min, decimal max, decimal step) => new NumericUpDown { Minimum = min, Maximum = max, Increment = step, DecimalPlaces = 2, Width = 130 };
    private static decimal Clamp(decimal value, NumericUpDown control) => Math.Min(control.Maximum, Math.Max(control.Minimum, value));
    private static void AddRow(TableLayoutPanel grid, string label, Control control)
    {
        int row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        grid.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = Color.WhiteSmoke, Anchor = AnchorStyles.Left }, 0, row);
        grid.Controls.Add(control, 1, row);
    }
}

internal sealed class ControllerCanvas : Panel
{
    internal bool Japanese { get; set; } = true;
    internal ControllerCanvas()
    {
        DoubleBuffered = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var bodyBrush = new SolidBrush(Color.FromArgb(38, 48, 62));
        using var innerBrush = new SolidBrush(Color.FromArgb(24, 31, 41));
        using var outline = new Pen(Color.FromArgb(142, 161, 180), 3F);
        using var detail = new Pen(Color.FromArgb(86, 105, 124), 2F);
        using var glow = new Pen(Color.FromArgb(74, 204, 188), 2F);

        using var body = new GraphicsPath();
        body.StartFigure();
        body.AddBezier(205, 112, 255, 84, 324, 100, 360, 128);
        body.AddBezier(360, 128, 400, 108, 460, 108, 500, 128);
        body.AddBezier(500, 128, 548, 96, 620, 87, 673, 120);
        body.AddBezier(673, 120, 735, 160, 755, 282, 716, 370);
        body.AddBezier(716, 370, 691, 421, 642, 406, 608, 342);
        body.AddBezier(608, 342, 579, 299, 551, 300, 515, 342);
        body.AddBezier(515, 342, 484, 377, 377, 377, 345, 342);
        body.AddBezier(345, 342, 310, 299, 278, 299, 249, 342);
        body.AddBezier(249, 342, 216, 405, 166, 421, 141, 370);
        body.AddBezier(141, 370, 102, 282, 122, 160, 205, 112);
        body.CloseFigure();
        g.FillPath(bodyBrush, body);
        g.DrawPath(outline, body);

        g.FillEllipse(innerBrush, 207, 155, 94, 94);
        g.DrawEllipse(detail, 207, 155, 94, 94);
        g.FillEllipse(innerBrush, 469, 251, 94, 94);
        g.DrawEllipse(detail, 469, 251, 94, 94);
        g.DrawEllipse(glow, 230, 178, 48, 48);
        g.DrawEllipse(glow, 492, 274, 48, 48);

        g.DrawEllipse(detail, 563, 150, 190, 175);
        g.DrawEllipse(detail, 230, 224, 210, 150);
        g.DrawLine(detail, 337, 272, 337, 346);
        g.DrawLine(detail, 286, 309, 389, 309);

        using var titleFont = new Font("Yu Gothic UI", 11F, FontStyle.Bold);
        using var subFont = new Font("Yu Gothic UI", 9F);
        using var mainText = new SolidBrush(Color.FromArgb(233, 239, 245));
        using var subText = new SolidBrush(Color.FromArgb(160, 174, 188));
        g.DrawString(Japanese ? "汎用ゲームパッド" : "Generic Gamepad", titleFont, mainText, 18, 14);
        g.DrawString(Japanese ? "ボタンをクリックして場面別の機能を割り当て" : "Click a button to assign context-specific actions", subFont, subText, 18, 40);
        g.DrawString(Japanese ? "割当内容はボタンへマウスを重ねると表示されます" : "Hover over a button to view its bindings", subFont, subText, 18, 408);
    }
}

internal sealed class AssignmentDialog : Form
{
    private readonly ControllerContext _context;
    private readonly ControllerButton _primary;
    private readonly List<ActionDefinition> _actions;
    private readonly List<BindingEntry> _bindings;
    private readonly bool _japanese;
    private readonly ComboBox _action = new ComboBox();
    private readonly CheckedListBox _buttons = new CheckedListBox();
    private readonly ListBox _current = new ListBox();

    internal AssignmentDialog(ControllerContext context, ControllerButton primary, List<ActionDefinition> actions, List<BindingEntry> bindings, bool japanese)
    {
        _context = context; _primary = primary; _actions = actions; _bindings = bindings; _japanese = japanese;
        Text = context + " / " + primary + (japanese ? " の割当" : " Bindings");
        ClientSize = new Size(610, 430);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Yu Gothic UI", 10F);
        _action.DropDownStyle = ComboBoxStyle.DropDownList;
        _action.Location = new Point(20, 42); _action.Size = new Size(560, 30);
        foreach (ActionDefinition item in actions.Where(item => (item.Contexts & context) != 0)) _action.Items.Add(new ActionItem(item, japanese));
        if (_action.Items.Count > 0) _action.SelectedIndex = 0;
        _buttons.Location = new Point(20, 100); _buttons.Size = new Size(260, 210);
        foreach (ControllerButton button in Enum.GetValues(typeof(ControllerButton)))
            if (button != ControllerButton.None) _buttons.Items.Add(button, button == primary);
        _current.Location = new Point(310, 100); _current.Size = new Size(270, 210);
        RefreshCurrent();
        var add = new Button { Text = japanese ? "割当を追加／置換" : "Add / Replace", Location = new Point(20, 330), Size = new Size(160, 36) };
        var remove = new Button { Text = japanese ? "選択した割当を解除" : "Remove Selected", Location = new Point(310, 330), Size = new Size(160, 36) };
        var close = new Button { Text = japanese ? "閉じる" : "Close", Location = new Point(480, 375), Size = new Size(100, 32), DialogResult = DialogResult.OK };
        add.Click += (_, _) => AddBinding();
        remove.Click += (_, _) => RemoveBinding();
        Controls.AddRange(new Control[] {
            new Label { Text = japanese ? "機能（この場面に対応する登録機能のみ）" : "Action (available in this context)", Location = new Point(20, 18), AutoSize = true }, _action,
            new Label { Text = japanese ? "入力ジェスチャー（最大3ボタン）" : "Input chord (up to 3 buttons)", Location = new Point(20, 78), AutoSize = true }, _buttons,
            new Label { Text = japanese ? "現在このボタンを含む割当" : "Bindings containing this button", Location = new Point(310, 78), AutoSize = true }, _current,
            add, remove, close });
    }

    private void AddBinding()
    {
        if (!(_action.SelectedItem is ActionItem selected)) return;
        List<ControllerButton> chord = _buttons.CheckedItems.Cast<ControllerButton>().Distinct().Take(3).OrderBy(x => x).ToList();
        if (chord.Count == 0 || !chord.Contains(_primary)) { MessageBox.Show(_japanese ? "クリックしたボタンを含めてください。" : "The chord must include the button you clicked."); return; }
        _bindings.RemoveAll(binding => binding.Context == _context && binding.Buttons.OrderBy(x => x).SequenceEqual(chord));
        _bindings.Add(new BindingEntry { Context = _context, Buttons = chord, ActionId = selected.Definition.ActionId });
        RefreshCurrent();
    }

    private void RemoveBinding()
    {
        if (_current.SelectedItem is BindingItem item) _bindings.Remove(item.Binding);
        RefreshCurrent();
    }

    private void RefreshCurrent()
    {
        _current.Items.Clear();
        foreach (BindingEntry binding in _bindings.Where(binding => binding.Context == _context && binding.Buttons.Contains(_primary)))
        {
            string name = LocalActionName(binding.ActionId);
            _current.Items.Add(new BindingItem(binding, string.Join(" + ", binding.Buttons) + " → " + name));
        }
    }

    private sealed class ActionItem
    {
        internal ActionDefinition Definition { get; }
        private readonly string _name;
        internal ActionItem(ActionDefinition definition, bool japanese) { Definition = definition; _name = TranslateAction(definition, japanese); }
        public override string ToString() => Definition.ModId + " / " + _name + " [" + Definition.Behavior + "]";
    }
    private sealed class BindingItem
    {
        internal BindingEntry Binding { get; }
        private readonly string _text;
        internal BindingItem(BindingEntry binding, string text) { Binding = binding; _text = text; }
        public override string ToString() => _text;
    }

    private string LocalActionName(string actionId)
    {
        ActionDefinition? action = _actions.FirstOrDefault(item => item.ActionId == actionId);
        return action == null ? actionId : TranslateAction(action, _japanese);
    }

    private static string TranslateAction(ActionDefinition action, bool japanese)
    {
        if (japanese) return action.DisplayName;
        return action.ActionId switch
        {
            "nocturne-modern-controller.dash" => "Dash",
            "nocturne-modern-controller.dash-keep" => "Toggle Dash Keep",
            "nocturne-modern-controller.quick-heal" => "Quick Heal",
            "nocturne-modern-controller.force-encounter" => "Force Encounter",
            "nocturne-modern-controller.open-settings" => "Open Settings",
            _ => action.DisplayName
        };
    }
}
