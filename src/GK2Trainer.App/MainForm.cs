using GK2Trainer.App.Core;

namespace GK2Trainer.App;

public sealed class MainForm : Form
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly BridgeClient _bridge;

    private GameInstall? _install;

    // header
    private readonly Label _gameLabel = new() { AutoSize = true };
    private readonly Label _loaderLabel = new() { AutoSize = true };
    private readonly Label _pluginLabel = new() { AutoSize = true };
    private readonly Label _connectionLabel = new() { AutoSize = true };
    private readonly Button _browseButton = new() { Text = "选择游戏目录…", AutoSize = true };
    private readonly Button _launchButton = new() { Text = "启动游戏", AutoSize = true };
    private readonly Button _deployButton = new() { Text = "安装 / 更新插件", AutoSize = true };
    private readonly Button _uninstallButton = new() { Text = "卸载插件", AutoSize = true };

    // 资源
    private readonly DataGridView _resourceGrid = MakeGrid();
    private readonly NumericUpDown _resourceDelta = new() { Minimum = -1000000, Maximum = 1000000, Value = 1000 };

    // 速度
    private readonly Label _moveSpeedLabel = new() { AutoSize = true };
    private readonly NumericUpDown _moveSpeedValue = new() { DecimalPlaces = 2, Minimum = 0.1m, Maximum = 20m, Increment = 0.25m, Value = 2m, Width = 100 };
    private readonly Label _gameSpeedLabel = new() { AutoSize = true };
    private readonly NumericUpDown _gameSpeedValue = new() { DecimalPlaces = 2, Minimum = 0.1m, Maximum = 10m, Increment = 0.25m, Value = 1.5m, Width = 100 };

    // 生命
    private readonly Label _hpLabel = new() { AutoSize = true };
    private readonly NumericUpDown _hpValue = new() { Minimum = 0, Maximum = 100000, Width = 100 };
    private readonly CheckBox _immune = new() { Text = "伤害免疫（IsImmuneToDamage）", AutoSize = true };
    private readonly Label _energyLabel = new() { AutoSize = true };

    // 天赋
    private readonly CheckedListBox _perkList = new() { Dock = DockStyle.Fill, CheckOnClick = true };
    private readonly TextBox _perkFilter = new() { Dock = DockStyle.Top, Height = 26 };
    private readonly Label _perkStatus = new() { Dock = DockStyle.Bottom, Height = 24 };
    private readonly List<string> _allPerkIds = new();
    private string _perkSignature = "";

    // 科技
    private readonly Label _techLabel = new() { AutoSize = true };
    private readonly TextBox _techId = new() { Width = 240 };

    // 背包
    private readonly ComboBox _itemPicker = new() { Width = 320, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly NumericUpDown _itemCount = new() { Minimum = 1, Maximum = 9999, Value = 1, Width = 80 };
    private readonly Label _itemStatus = new() { AutoSize = true };
    private readonly List<string> _allItemIds = new();

    // 日志
    private readonly TextBox _logBox = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true, Dock = DockStyle.Fill };

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 250 };
    private readonly ToolStripStatusLabel _status = new() { Text = "就绪" };
    private TabControl _tabs = new();

    private string? _pendingListKind;

    public MainForm(int initialTab = 0)
    {
        _bridge = new BridgeClient(_settings);
        Text = "守墓人 2 修改器 · GK2 Trainer";
        MinimumSize = new Size(1080, 700);
        StartPosition = FormStartPosition.CenterScreen;

        BuildLayout();
        RefreshInstallation();
        if (initialTab > 0 && initialTab < _tabs.TabCount) _tabs.SelectedIndex = initialTab;

        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
    }

    // ---------------------------------------------------------------- layout

    private void BuildLayout()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 106, Padding = new Padding(12, 8, 12, 8) };

        var row1 = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32, WrapContents = false };
        row1.Controls.Add(_gameLabel);
        row1.Controls.Add(_browseButton);
        row1.Controls.Add(_launchButton);
        row1.Controls.Add(_deployButton);
        row1.Controls.Add(_uninstallButton);

        var row2 = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 28, WrapContents = false };
        row2.Controls.Add(_loaderLabel);
        row2.Controls.Add(new Label { Text = "   ", AutoSize = true });
        row2.Controls.Add(_pluginLabel);

        var row3 = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 28, WrapContents = false };
        row3.Controls.Add(_connectionLabel);

        header.Controls.Add(row3);
        header.Controls.Add(row2);
        header.Controls.Add(row1);

        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(_status);

        Controls.Add(BuildTabs());
        Controls.Add(header);
        Controls.Add(statusStrip);

        _browseButton.Click += (_, _) => BrowseForGame();
        _launchButton.Click += (_, _) => LaunchGame();
        _deployButton.Click += (_, _) => Deploy();
        _uninstallButton.Click += (_, _) => Uninstall();
    }

    private TabControl BuildTabs()
    {
        _tabs = new TabControl { Dock = DockStyle.Fill };
        _tabs.TabPages.Add(BuildResourceTab());
        _tabs.TabPages.Add(BuildSpeedTab());
        _tabs.TabPages.Add(BuildHealthTab());
        _tabs.TabPages.Add(BuildPerkTab());
        _tabs.TabPages.Add(BuildTechTab());
        _tabs.TabPages.Add(BuildInventoryTab());
        _tabs.TabPages.Add(BuildLogTab());
        return _tabs;
    }

    private static DataGridView MakeGrid() => new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        SelectionMode = DataGridViewSelectionMode.CellSelect,
        EditMode = DataGridViewEditMode.EditOnEnter,
        BackgroundColor = SystemColors.Window,
    };

    private TabPage BuildResourceTab()
    {
        var page = new TabPage("资源");
        ConfigureGrid(_resourceGrid, new[] { "资源", "当前", "游戏内范围", "新值（可输入）" });

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(8, 6, 8, 6) };
        bar.Controls.Add(MakeButton("应用所有改动", () => ApplyResourceGrid()));
        bar.Controls.Add(new Label { Text = "批量增减：", AutoSize = true, Anchor = AnchorStyles.Left });
        bar.Controls.Add(_resourceDelta);
        bar.Controls.Add(MakeButton("全部 +", () => NudgeResources(1)));
        bar.Controls.Add(MakeButton("全部 -", () => NudgeResources(-1)));
        bar.Controls.Add(MakeButton("清空输入", () => ResetGridInput(_resourceGrid)));

        var hint = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            Padding = new Padding(10, 4, 10, 4),
            ForeColor = SystemColors.GrayText,
            Text = "「游戏内范围」是游戏自己的资源系统给出的上下限（例：红/绿/蓝球上限 999）。" +
                   "填好「新值」后点「应用所有改动」，只对填了数字的行生效。",
        };

        page.Controls.Add(_resourceGrid);
        page.Controls.Add(bar);
        page.Controls.Add(hint);
        return page;
    }

    private TabPage BuildSpeedTab()
    {
        var page = new TabPage("速度");

        var move = new GroupBox { Text = "移动速度", Dock = DockStyle.Top, Height = 120, Padding = new Padding(12) };
        var moveRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, WrapContents = false };
        moveRow.Controls.Add(_moveSpeedLabel);
        moveRow.Controls.Add(new Label { Text = "   倍率：", AutoSize = true, Anchor = AnchorStyles.Left });
        moveRow.Controls.Add(_moveSpeedValue);
        moveRow.Controls.Add(MakeButton("应用并锁定", () => Send("setMoveSpeed", o => o.Value = (double)_moveSpeedValue.Value)));
        moveRow.Controls.Add(MakeButton("解除锁定", () => Send("clearMoveSpeed")));
        var moveHint = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = SystemColors.GrayText,
            Text = "走路速度 = 基础速度 × 倍率。倍率会被攻击等状态临时改动（攻击时 ×0.5），" +
                   "「锁定」会每帧把它改回来，所以战斗中也不会掉速。",
        };
        move.Controls.Add(moveHint);
        move.Controls.Add(moveRow);

        var game = new GroupBox { Text = "游戏速度（Time.timeScale）", Dock = DockStyle.Top, Height = 120, Padding = new Padding(12) };
        var gameRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, WrapContents = false };
        gameRow.Controls.Add(_gameSpeedLabel);
        gameRow.Controls.Add(new Label { Text = "   倍率：", AutoSize = true, Anchor = AnchorStyles.Left });
        gameRow.Controls.Add(_gameSpeedValue);
        gameRow.Controls.Add(MakeButton("应用并锁定", () => Send("setGameSpeed", o => o.Value = (double)_gameSpeedValue.Value)));
        gameRow.Controls.Add(MakeButton("解除锁定", () => Send("clearGameSpeed")));
        var gameHint = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = SystemColors.GrayText,
            Text = "整体加速：动画、昼夜推进、机器与工作台进度一起变快（昼夜也会更快消耗，注意权衡）。" +
                   "游戏暂停/冻结时不会去抢 timeScale，解除后恢复原值。",
        };
        game.Controls.Add(gameHint);
        game.Controls.Add(gameRow);

        page.Controls.Add(game);
        page.Controls.Add(move);
        return page;
    }

    private TabPage BuildHealthTab()
    {
        var page = new TabPage("生命");

        var panel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 160, Padding = new Padding(12), FlowDirection = FlowDirection.TopDown, WrapContents = false };

        panel.Controls.Add(_hpLabel);

        var hpRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        hpRow.Controls.Add(new Label { Text = "设为：", AutoSize = true, Anchor = AnchorStyles.Left });
        hpRow.Controls.Add(_hpValue);
        hpRow.Controls.Add(MakeButton("应用", () => Send("setHp", o => o.Value = (int)_hpValue.Value)));
        hpRow.Controls.Add(MakeButton("回满", () => Send("heal")));
        panel.Controls.Add(hpRow);

        panel.Controls.Add(_immune);
        _immune.CheckedChanged += (_, _) => Send("setImmune", o => o.Value = _immune.Checked ? 1 : 0, silent: true);
        panel.Controls.Add(_energyLabel);

        page.Controls.Add(panel);
        return page;
    }

    private TabPage BuildPerkTab()
    {
        var page = new TabPage("天赋");
        _perkFilter.PlaceholderText = "过滤天赋 id（例如 axeman / perk_）";

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(8, 6, 8, 6) };
        bar.Controls.Add(MakeButton("从游戏读取天赋清单", () => RequestIdList("perk")));
        bar.Controls.Add(MakeButton("应用勾选改动", ApplyPerks));
        bar.Controls.Add(MakeButton("刷新当前状态", () => Send("log", o => o.Message = "refresh", silent: true)));

        _perkFilter.TextChanged += (_, _) => RefreshPerkList(force: true);

        page.Controls.Add(_perkList);
        page.Controls.Add(bar);
        page.Controls.Add(_perkFilter);
        page.Controls.Add(_perkStatus);
        return page;
    }

    private TabPage BuildTechTab()
    {
        var page = new TabPage("科技 / 解锁");
        var panel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 170, Padding = new Padding(12), FlowDirection = FlowDirection.TopDown, WrapContents = false };

        panel.Controls.Add(_techLabel);

        var row1 = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row1.Controls.Add(new Label { Text = "解锁指定科技 id：", AutoSize = true, Anchor = AnchorStyles.Left });
        row1.Controls.Add(_techId);
        row1.Controls.Add(MakeButton("解锁", () => Send("unlockTech", o => o.Id = _techId.Text.Trim())));
        row1.Controls.Add(MakeButton("从游戏读取科技清单", () => RequestIdList("tech")));
        panel.Controls.Add(row1);

        var row2 = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row2.Controls.Add(MakeButton("一键解锁全部科技", UnlockAllTechs));
        row2.Controls.Add(new Label
        {
            Text = "（会遍历游戏自己的科技定义逐个解锁，不可逆，建议先备份存档）",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Anchor = AnchorStyles.Left,
        });
        panel.Controls.Add(row2);

        page.Controls.Add(panel);
        return page;
    }

    private TabPage BuildInventoryTab()
    {
        var page = new TabPage("背包");
        var panel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 170, Padding = new Padding(12), FlowDirection = FlowDirection.TopDown, WrapContents = false };

        var row1 = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row1.Controls.Add(new Label { Text = "物品 id：", AutoSize = true, Anchor = AnchorStyles.Left });
        row1.Controls.Add(_itemPicker);
        row1.Controls.Add(new Label { Text = "  数量：", AutoSize = true, Anchor = AnchorStyles.Left });
        row1.Controls.Add(_itemCount);
        row1.Controls.Add(MakeButton("加入背包", () => Send("addItem", o =>
        {
            o.Id = _itemPicker.Text.Trim();
            o.Count = (int)_itemCount.Value;
        })));
        panel.Controls.Add(row1);

        var row2 = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row2.Controls.Add(MakeButton("从游戏读取物品清单（814 项）", () => RequestIdList("item")));
        row2.Controls.Add(MakeButton("刷新当前状态", () => Send("log", o => o.Message = "refresh", silent: true)));
        panel.Controls.Add(row2);

        panel.Controls.Add(_itemStatus);
        panel.Controls.Add(new Label
        {
            Text = "提示：加入前会检查背包能否放下；物品 id 可以直接输入，也可以从游戏清单里挑。",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
        });

        page.Controls.Add(panel);
        return page;
    }

    private TabPage BuildLogTab()
    {
        var page = new TabPage("日志 / 调试");

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(8, 6, 8, 6) };
        bar.Controls.Add(MakeButton("刷新日志", RefreshLogs));
        bar.Controls.Add(MakeButton("BepInEx 日志", () => AppendBepInExLog()));
        bar.Controls.Add(MakeButton("游戏内探针（probe）", () => Send("probe")));
        bar.Controls.Add(MakeButton("打印状态到日志", PrintStateSummary));

        page.Controls.Add(_logBox);
        page.Controls.Add(bar);
        return page;
    }

    private static void ConfigureGrid(DataGridView grid, string[] headers)
    {
        foreach (var header in headers)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                ReadOnly = header != headers[^1],
            });
        }
    }

    private Button MakeButton(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, Margin = new Padding(3) };
        button.Click += (_, _) =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log("操作失败：" + ex.Message);
            }
        };
        return button;
    }

    // ------------------------------------------------------------- behaviour

    private void OnTick()
    {
        _bridge.Poll();
        _bridge.Flush();
        ConsumeResults();

        UpdateConnectionStatus();
        RefreshState();

        _status.Text = _bridge.QueuedCount > 0
            ? $"待发送操作：{_bridge.QueuedCount}"
            : _bridge.LastError is { Length: > 0 } error ? "错误：" + error : "就绪";
    }

    private void ConsumeResults()
    {
        foreach (var result in _bridge.PendingResults.ToList())
        {
            _bridge.PendingResults.Remove(result);

            if (result.Op == "listIds" && result.Ids.Count > 0)
            {
                var kind = result.Ids.Count > 0 && _pendingListKind != null ? _pendingListKind : null;
                switch (kind)
                {
                    case "perk":
                        _allPerkIds.Clear();
                        _allPerkIds.AddRange(result.Ids);
                        RefreshPerkList(force: true);
                        Log($"已读取天赋清单：{result.Ids.Count} 项（{result.Value}）");
                        break;
                    case "item":
                        _allItemIds.Clear();
                        _allItemIds.AddRange(result.Ids);
                        _itemPicker.Items.Clear();
                        _itemPicker.Items.AddRange(result.Ids.Take(1000).Cast<object>().ToArray());
                        _itemStatus.Text = $"已读取物品清单：{result.Ids.Count} 项（{result.Value}）";
                        Log($"已读取物品清单：{result.Ids.Count} 项");
                        break;
                    case "tech":
                        Log($"科技清单：{result.Ids.Count} 项（{result.Value}）");
                        Log("  前 20 个：" + string.Join(", ", result.Ids.Take(20)));
                        break;
                }
                _pendingListKind = null;
            }
            else if (result.Op == "unlockAllTechs")
            {
                Log("一键解锁结果：" + result.Value);
            }
            else
            {
                Log($"{result.Op} → {result.Value}");
            }
        }
    }

    private void UpdateConnectionStatus()
    {
        var state = _bridge.State;
        var age = GameInstall.StateAge();

        if (state == null)
        {
            _connectionLabel.Text = Directory.Exists(Paths.BridgeDir)
                ? "连接：等待插件回应（需要：装好插件 → 启动游戏 → 读取存档）"
                : $"连接：未找到桥接目录 {Paths.BridgeDir}";
            return;
        }

        var status = age.TotalSeconds < 4 ? "已连接" : $"已断开（最后回应 {age.TotalSeconds:F1} 秒前）";
        var inGame = state.InGame ? "存档中" : "未进存档";
        _connectionLabel.Text = $"连接：{status} · 插件 v{state.Version} · {inGame} · 第 {state.Game.Day} 天 · seq={state.Seq}";

        foreach (var error in state.Errors.Take(2))
        {
            _status.Text = "插件错误：" + error;
        }
    }

    private void RefreshInstallation()
    {
        _install = GameInstall.Find();

        if (_install == null)
        {
            _gameLabel.Text = "游戏：未找到（请手动选择 Graveyard Keeper 2 目录）";
            _loaderLabel.Text = "BepInEx：—";
            _pluginLabel.Text = "插件：—";
            return;
        }

        _gameLabel.Text = $"游戏：{_install.Root}";
        _loaderLabel.Text = $"BepInEx：{(_install.BepInExPresent ? "已安装" : "未安装（点「安装 / 更新插件」会自动装）")}";
        _pluginLabel.Text = !_install.PluginPresent
            ? "插件：未部署"
            : _install.PluginUpToDate ? "插件：已部署且是最新" : "插件：需要更新（点「安装 / 更新插件」）";
    }

    private void BrowseForGame()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择 Graveyard Keeper 2 目录（里面应有 GraveyardKeeper2.exe）",
            SelectedPath = _install?.Root ?? "",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var install = GameInstall.Inspect(dialog.SelectedPath);
        if (install == null)
        {
            MessageBox.Show(this, "这个目录里没找到 GraveyardKeeper2.exe。", "路径不对",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _settings.GameDirectory = install.Root;
        _settings.Save();
        RefreshInstallation();
    }

    private void LaunchGame()
    {
        if (_install == null)
        {
            MessageBox.Show(this, "还没有找到游戏目录。", "无法启动", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _install.AppPath,
                WorkingDirectory = _install.Root,
                UseShellExecute = true,
            });
            Log("已启动游戏。");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Deploy()
    {
        var install = _install ?? GameInstall.Find();
        if (install == null)
        {
            MessageBox.Show(this, "还没有找到游戏目录。", "无法安装", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var bepinex = PluginDeployer.EnsureBepInEx(install);
        var plugin = PluginDeployer.DeployPlugin(install);

        foreach (var m in bepinex.Messages.Concat(plugin.Messages)) Log(m);
        foreach (var e in bepinex.Errors.Concat(plugin.Errors)) Log("错误：" + e);

        MessageBox.Show(this,
            string.Join("\n", bepinex.Messages.Concat(plugin.Messages).Concat(bepinex.Errors).Concat(plugin.Errors)),
            bepinex.Ok && plugin.Ok ? "完成" : "遇到问题",
            MessageBoxButtons.OK,
            bepinex.Ok && plugin.Ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

        RefreshInstallation();
    }

    private void Uninstall()
    {
        var install = _install ?? GameInstall.Find();
        if (install == null) return;

        if (MessageBox.Show(this, "确定要删除插件吗？（BepInEx 本身会保留）", "确认",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        var result = PluginDeployer.RemovePlugin(install);
        foreach (var m in result.Messages) Log(m);
        foreach (var e in result.Errors) Log("错误：" + e);
        RefreshInstallation();
    }

    // ------------------------------------------------------------ state sync

    private void RefreshState()
    {
        var state = _bridge.State;
        var player = state?.Player;
        if (player == null) return;

        RefreshResourceGrid(player);
        RefreshSpeed(player);
        RefreshHealth(player);
        RefreshPerkList(force: false);
        RefreshTech(player);
    }

    private void RefreshResourceGrid(TargetInfo player)
    {
        var ids = player.Resources.Keys.ToList();
        var signature = string.Join(",", ids);

        if (_resourceGrid.Tag as string != signature)
        {
            _resourceGrid.Tag = signature;
            _resourceGrid.Rows.Clear();
            foreach (var id in ids)
            {
                _resourceGrid.Rows.Add(IdLabel(id, player.Resources[id]), "—", "—", "");
            }
            _resourceGrid.ClearSelection();
        }

        for (var i = 0; i < ids.Count && i < _resourceGrid.Rows.Count; i++)
        {
            var info = player.Resources[ids[i]];
            var row = _resourceGrid.Rows[i];
            row.Cells[0].Value = IdLabel(ids[i], info);
            row.Cells[1].Value = Format(info.Value);
            row.Cells[2].Value = $"{Format(info.Min)} ~ {Format(info.Max)}";
            row.Tag = ids[i];
        }
    }

    private static string IdLabel(string id, ResourceInfo info) =>
        string.IsNullOrWhiteSpace(info.Label) ? id : $"{info.Label} ({id})";

    private void RefreshSpeed(TargetInfo player)
    {
        var move = player.MoveSpeed;
        _moveSpeedLabel.Text = move.Multiplier == null
            ? "（未进存档）"
            : $"当前倍率 {Format(move.Multiplier)} · 基础速度 {Format(move.Base)} · 有效速度 {Format(move.Effective)}" +
              (move.Locked ? $" · 已锁定 x{Format(move.LockValue)}" : "");

        var game = player.GameSpeed;
        _gameSpeedLabel.Text = game.Current == null
            ? "（未进存档）"
            : $"当前 {Format(game.Current)}" + (game.Locked ? $" · 已锁定 x{Format(game.LockValue)}" : "") +
              (game.Paused ? " · 游戏暂停中" : "");
    }

    private void RefreshHealth(TargetInfo player)
    {
        var vitals = player.Vitals;
        _hpLabel.Text = $"生命：{vitals.Hp} / {vitals.MaxHp}" +
                        (vitals.Immune == true ? "（免伤中）" : "");
        _energyLabel.Text = $"距上次睡眠：{Format(vitals.TimeWithoutSleep)}";

        if (vitals.Immune.HasValue && _immune.Checked != vitals.Immune.Value)
        {
            _immune.CheckedChanged -= null;
            _immune.Checked = vitals.Immune.Value;
        }
    }

    private void RefreshPerkList(bool force)
    {
        var player = _bridge.State?.Player;
        if (player == null) return;

        var filter = _perkFilter.Text.Trim();
        var ids = (_allPerkIds.Count > 0 ? _allPerkIds : player.Perks)
            .Where(id => filter.Length == 0 || id.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .ToList();

        var signature = string.Join(",", ids) + "|" + string.Join(",", player.Perks);
        if (!force && _perkSignature == signature) return;
        _perkSignature = signature;

        _perkList.BeginUpdate();
        _perkList.Items.Clear();
        var owned = new HashSet<string>(player.Perks, StringComparer.Ordinal);
        foreach (var id in ids) _perkList.Items.Add(id, owned.Contains(id));
        _perkList.EndUpdate();

        _perkStatus.Text = _allPerkIds.Count > 0
            ? $"已拥有 {player.Perks.Count} 个 · 清单 {_allPerkIds.Count} 项 · 显示 {ids.Count} 项"
            : $"已拥有 {player.Perks.Count} 个（点左边按钮读取完整清单）";
    }

    private void RefreshTech(TargetInfo player)
    {
        var knowledge = player.Knowledge;
        _techLabel.Text = $"科技 {knowledge.UnlockedTechs}/{knowledge.TotalTechs} · " +
                          $"配方 {knowledge.UnlockedCrafts} · 建筑 {knowledge.UnlockedBuildings} · " +
                          $"（物品定义 {knowledge.TotalItemDefs}，天赋定义 {knowledge.TotalPerkDefs}）";
    }

    private void RequestIdList(string kind)
    {
        _pendingListKind = kind;
        Send("listIds", o =>
        {
            o.Kind = kind;
            o.Limit = kind == "item" ? 1200 : 400;
        });
    }

    // --------------------------------------------------------------- sending

    private void Send(string opName, Action<Op>? configure = null, bool silent = false)
    {
        var op = new Op { OpName = opName };
        configure?.Invoke(op);
        _bridge.Enqueue(op);
        _bridge.Flush();
        if (!silent) Log("→ " + Describe(op));
    }

    private static string Describe(Op op)
    {
        var parts = new List<string> { op.OpName };
        if (op.Name != null) parts.Add($"name={op.Name}");
        if (op.Id != null) parts.Add($"id={op.Id}");
        if (op.Value != null) parts.Add($"value={op.Value}");
        if (op.Amount != null) parts.Add($"amount={op.Amount}");
        if (op.Count != null) parts.Add($"count={op.Count}");
        if (op.Kind != null) parts.Add($"kind={op.Kind}");
        if (op.Message != null) parts.Add($"message={op.Message}");
        return string.Join(" ", parts);
    }

    private void ApplyResourceGrid()
    {
        var applied = 0;
        foreach (DataGridViewRow row in _resourceGrid.Rows)
        {
            if (row.Tag is not string id) continue;

            var input = row.Cells[^1].Value?.ToString()?.Trim();
            if (string.IsNullOrEmpty(input) || !double.TryParse(input, out var value)) continue;

            Send("setRes", o =>
            {
                o.Name = id;
                o.Value = value;
            }, silent: true);
            row.Cells[^1].Value = null;
            applied++;
        }

        Log(applied == 0 ? "没有需要应用的改动。" : $"已提交 {applied} 项资源改动。");
        _bridge.Flush();
    }

    private void NudgeResources(int sign)
    {
        var delta = (double)_resourceDelta.Value * sign;
        foreach (DataGridViewRow row in _resourceGrid.Rows)
        {
            if (row.Tag is not string id) continue;
            Send("addRes", o =>
            {
                o.Name = id;
                o.Amount = delta;
            }, silent: true);
        }
        Log($"已提交所有资源 {delta:+#;-#;0}");
        _bridge.Flush();
    }

    private static void ResetGridInput(DataGridView grid)
    {
        foreach (DataGridViewRow row in grid.Rows) row.Cells[^1].Value = null;
    }

    private void ApplyPerks()
    {
        var player = _bridge.State?.Player;
        if (player == null) return;

        var owned = new HashSet<string>(player.Perks, StringComparer.Ordinal);
        var changes = 0;

        foreach (var entry in _perkList.Items)
        {
            if (entry is not string id) continue;

            var shouldHave = _perkList.CheckedItems.Contains(entry);
            if (shouldHave == owned.Contains(id)) continue;

            Send(shouldHave ? "addPerk" : "removePerk", o => o.Id = id, silent: true);
            changes++;
        }

        Log(changes == 0 ? "天赋没有改动。" : $"已提交 {changes} 项天赋改动。");
        _bridge.Flush();
    }

    private void UnlockAllTechs()
    {
        if (MessageBox.Show(this,
                "这会遍历游戏自己的科技定义并全部解锁。\n\n这是不可逆的进度改动，建议先备份存档。要继续吗？",
                "确认一键解锁", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        Send("unlockAllTechs");
    }

    private void PrintStateSummary()
    {
        var state = _bridge.State;
        if (state == null)
        {
            Log("还没有状态数据（游戏需要先进存档）。");
            return;
        }

        var player = state.Player;
        Log($"插件 v{state.Version} seq={state.Seq} inGame={state.InGame} 第 {state.Game.Day} 天 " +
            $"Unity {state.Game.Unity} 存档 {state.Game.SaveVersion}");

        if (player == null) return;
        Log("  资源：" + string.Join("  ", player.Resources.Select(kv => $"{kv.Key}={Format(kv.Value.Value)}")));
        Log($"  生命 {player.Vitals.Hp}/{player.Vitals.MaxHp} 免伤={player.Vitals.Immune}  " +
            $"移动速度 x{Format(player.MoveSpeed.Multiplier)} (锁定={player.MoveSpeed.Locked})  " +
            $"游戏速度 x{Format(player.GameSpeed.Current)} (锁定={player.GameSpeed.Locked})");
        Log("  天赋：" + string.Join(", ", player.Perks));
    }

    private void RefreshLogs()
    {
        var log = _bridge.ReadGameLog();
        if (log.Length == 0)
        {
            Log("插件还没有写日志（游戏启动并加载插件后才有）。");
            return;
        }

        _logBox.Text += "----- 插件日志（末尾 40 行）-----" + Environment.NewLine +
                        string.Join(Environment.NewLine, log.Split('\n').TakeLast(40)) + Environment.NewLine;
        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.ScrollToCaret();
    }

    private void AppendBepInExLog()
    {
        if (_install == null)
        {
            Log("还没有找到游戏目录。");
            return;
        }

        var log = BridgeClient.ReadBepInExLog(_install.Root);
        if (log.Length == 0)
        {
            Log("没有 BepInEx 日志（BepInEx 还没装或没跑过）。");
            return;
        }

        _logBox.Text += "----- BepInEx LogOutput.log（末尾 40 行）-----" + Environment.NewLine +
                        log + Environment.NewLine;
        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.ScrollToCaret();
    }

    private static string Format(double? value) =>
        value.HasValue ? value.Value.ToString("0.##") : "—";

    private void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        var text = _logBox.Text.Length > 40000 ? _logBox.Text[^20000..] : _logBox.Text;
        _logBox.Text = text + line + Environment.NewLine;
        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.ScrollToCaret();
    }
}
