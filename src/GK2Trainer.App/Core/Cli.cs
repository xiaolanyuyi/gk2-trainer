using System.Runtime.InteropServices;
using System.Text;

namespace GK2Trainer.App.Core;

/// <summary>
/// Head-less commands, useful for verification and scripting:
///   GK2Trainer.exe diagnose | deploy | uninstall | dump-state
///   GK2Trainer.exe op setRes name=money value=9999
/// Output goes to the console (when attached) and to %LOCALAPPDATA%\GK2Trainer\cli-output.txt
/// </summary>
public static class Cli
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    public static int Run(string[] args)
    {
        var command = args[0].TrimStart('-', '/').ToLowerInvariant();
        var output = new StringBuilder();

        void Write(string text)
        {
            output.AppendLine(text);
            Console.WriteLine(text);
        }

        // "GK2Trainer.exe deploy --game=<path>" - useful when the game is not
        // running and no path was stored yet.
        var explicitGame = args
            .FirstOrDefault(a => a.StartsWith("--game=", StringComparison.OrdinalIgnoreCase))
            ?["--game=".Length..];
        if (!string.IsNullOrWhiteSpace(explicitGame))
        {
            var settings = AppSettings.Load();
            settings.GameDirectory = explicitGame.Trim('"');
            settings.Save();
        }

        var exitCode = 0;
        try
        {
            AttachConsole(-1);

            switch (command)
            {
                case "diagnose": Diagnose(Write); break;
                case "deploy": Deploy(Write); break;
                case "uninstall": Uninstall(Write); break;
                case "dump-state": DumpState(Write); break;
                case "op": SendSingleOp(Write, args); break;
                case "bepinex-zip": InstallBepInEx(Write, args); break;
                case "help":
                case "--help":
                    Write("用法: GK2Trainer.exe [diagnose|deploy|uninstall|dump-state|op|bepinex-zip]");
                    break;
                default:
                    Write($"未知命令：{command}");
                    exitCode = 1;
                    break;
            }
        }
        catch (Exception ex)
        {
            Write("异常：" + ex);
            exitCode = 1;
        }

        try
        {
            File.WriteAllText(Path.Combine(Paths.AppData, "cli-output.txt"), output.ToString(),
                new UTF8Encoding(false));
        }
        catch
        {
            // Nothing else to do.
        }

        return exitCode;
    }

    private static void Diagnose(Action<string> write)
    {
        write("== 环境 ==");
        write($"游戏用户目录 : {Paths.GameStorage}（{(Directory.Exists(Paths.GameStorage) ? "存在" : "不存在")}）");
        write($"桥接目录     : {Paths.BridgeDir}（{(Directory.Exists(Paths.BridgeDir) ? "存在" : "不存在")}）");
        write($"游戏进程     : {(GameInstall.IsGameRunning() ? "运行中" : "未运行")}");
        write($"状态文件新鲜度: {GameInstall.StateAge().TotalSeconds:F1} 秒");
        write("");

        var install = GameInstall.Find();
        if (install == null)
        {
            write("== 游戏 ==  未找到（请在界面里手动选择 Graveyard Keeper 2 目录）");
            return;
        }

        write("== 游戏 ==");
        write($"根目录       : {install.Root}");
        write($"BepInEx      : {(install.BepInExPresent ? "已安装" : "未安装")}");
        write($"插件         : {(install.PluginPresent ? install.PluginPath : "未部署")}");
        write($"插件是最新的 : {(install.PluginPresent ? (install.PluginUpToDate ? "是" : "否（点「安装/更新插件」）") : "—")}");
        write("");
        DumpState(write);
    }

    private static void Deploy(Action<string> write)
    {
        var install = GameInstall.Find();
        if (install == null)
        {
            write("未找到游戏目录。");
            return;
        }

        var bepinex = PluginDeployer.EnsureBepInEx(install);
        foreach (var m in bepinex.Messages) write(m);
        foreach (var e in bepinex.Errors) write("错误：" + e);

        var plugin = PluginDeployer.DeployPlugin(install);
        foreach (var m in plugin.Messages) write(m);
        foreach (var e in plugin.Errors) write("错误：" + e);
    }

    private static void InstallBepInEx(Action<string> write, string[] args)
    {
        var install = GameInstall.Find();
        if (install == null)
        {
            write("未找到游戏目录。");
            return;
        }

        var zip = args.Length > 1 ? args[1] : null;
        var result = PluginDeployer.EnsureBepInEx(install, zip);
        foreach (var m in result.Messages) write(m);
        foreach (var e in result.Errors) write("错误：" + e);
    }

    private static void Uninstall(Action<string> write)
    {
        var install = GameInstall.Find();
        if (install == null)
        {
            write("未找到游戏目录。");
            return;
        }

        var result = PluginDeployer.RemovePlugin(install);
        foreach (var m in result.Messages) write(m);
        foreach (var e in result.Errors) write("错误：" + e);
    }

    private static void DumpState(Action<string> write)
    {
        var bridge = new BridgeClient(AppSettings.Load());
        bridge.Poll();

        var state = bridge.State;
        if (state == null)
        {
            write("== 状态 ==  还没有状态文件（游戏需要先进存档）");
            return;
        }

        write("== 状态 ==");
        write($"插件版本 {state.Version}  seq={state.Seq}  inGame={state.InGame}  " +
              $"天数={state.Game.Day}  存档版本={state.Game.SaveVersion}  Unity={state.Game.Unity}");

        var player = state.Player;
        if (player == null)
        {
            write("（没有目标数据）");
            return;
        }

        write($"角色: {player.Display}");
        foreach (var (id, info) in player.Resources)
        {
            write($"  资源 {id,-10} {info.Label} = {info.Value}  (范围 {info.Min}~{info.Max})");
        }

        write($"  生命 {player.Vitals.Hp}/{player.Vitals.MaxHp}  免伤={player.Vitals.Immune}");
        write($"  移动速度 倍率={player.MoveSpeed.Multiplier} 基础={player.MoveSpeed.Base} " +
              $"有效={player.MoveSpeed.Effective} 锁定={player.MoveSpeed.Locked}");
        write($"  游戏速度 当前={player.GameSpeed.Current} 锁定={player.GameSpeed.Locked} 暂停={player.GameSpeed.Paused}");
        write($"  天赋 {player.Perks.Count} 个: {string.Join(", ", player.Perks)}");
        write($"  科技 {player.Knowledge.UnlockedTechs}/{player.Knowledge.TotalTechs}  " +
              $"配方={player.Knowledge.UnlockedCrafts}  建筑={player.Knowledge.UnlockedBuildings}  " +
              $"（物品定义 {player.Knowledge.TotalItemDefs}，天赋定义 {player.Knowledge.TotalPerkDefs}）");

        foreach (var error in state.Errors) write("  插件错误: " + error);
    }

    private static void SendSingleOp(Action<string> write, string[] args)
    {
        if (args.Length < 2)
        {
            write("用法: GK2Trainer.exe op <op> [key=value ...]");
            write("     键: name / id / value / amount / count / kind / filter / limit / message");
            return;
        }

        var bridge = new BridgeClient(AppSettings.Load());
        bridge.Poll();

        var op = new Op { OpName = args[1] };
        foreach (var argument in args.Skip(2))
        {
            var split = argument.Split('=', 2);
            var key = split[0].ToLowerInvariant();
            var raw = split.Length > 1 ? split[1] : "";
            switch (key)
            {
                case "name": op.Name = raw; break;
                case "id": op.Id = raw; break;
                case "value": op.Value = double.Parse(raw); break;
                case "amount": op.Amount = double.Parse(raw); break;
                case "count": op.Count = int.Parse(raw); break;
                case "kind": op.Kind = raw; break;
                case "filter": op.Filter = raw; break;
                case "limit": op.Limit = int.Parse(raw); break;
                case "message": op.Message = raw; break;
                default: write($"未知参数：{argument}"); break;
            }
        }

        write($"发送 {op.OpName} name={op.Name} id={op.Id} value={op.Value} amount={op.Amount} count={op.Count}");
        bridge.Enqueue(op);
        bridge.Flush();

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            bridge.Poll();
            bridge.Flush();
            if (bridge.State != null && bridge.State.Seq >= bridge.LastSentSequence) break;
            Thread.Sleep(200);
        }

        var state = bridge.State;
        if (state == null || state.Seq < bridge.LastSentSequence)
        {
            write("超时：没有收到确认（游戏进存档了吗？）");
            return;
        }

        write($"已确认 seq={state.Seq}");
        foreach (var error in state.Errors) write("  错误: " + error);
        foreach (var result in bridge.PendingResults)
        {
            write($"  结果: {result.Op} -> {result.Value}");
            if (result.Ids.Count > 0) write($"    ids({result.Ids.Count}): {string.Join(", ", result.Ids.Take(20))}");
        }
    }
}
