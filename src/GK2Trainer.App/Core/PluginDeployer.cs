using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace GK2Trainer.App.Core;

public sealed class DeployResult
{
    public List<string> Messages { get; } = new();
    public List<string> Errors { get; } = new();
    public bool Ok => Errors.Count == 0;
}

/// <summary>
/// Installs the trainer plugin into the game's BepInEx folder, and the BepInEx
/// loader itself when it is missing.
/// </summary>
public static class PluginDeployer
{
    private const string EmbeddedPluginName = "plugin.GK2Trainer.Plugin.dll";

    /// <summary>Release BepInEx 5 that matches Unity 6000 (it bundles Doorstop 4.5).</summary>
    public const string BepInExUrl =
        "https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip";

    public static byte[]? ReadEmbeddedPlugin()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(EmbeddedPluginName);
        if (stream == null) return null;

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    public static DeployResult DeployPlugin(GameInstall install, byte[]? pluginBytes = null)
    {
        var result = new DeployResult();

        pluginBytes ??= ReadEmbeddedPlugin();
        if (pluginBytes == null)
        {
            result.Errors.Add("程序里没有嵌入插件（构建顺序：先 plugin，再 App）");
            return result;
        }

        var target = Path.Combine(Paths.PluginsDir(install.Root), Paths.PluginFileName);
        try
        {
            Directory.CreateDirectory(Paths.PluginsDir(install.Root));

            // A running game memory-maps the loaded assembly, which blocks
            // overwriting it but not renaming it.
            if (File.Exists(target))
            {
                var stale = target + ".old";
                try
                {
                    File.Delete(stale);
                    File.Move(target, stale);
                    result.Messages.Add($"游戏正在运行：旧插件已改名为 {Path.GetFileName(stale)}（下次启动生效）");
                }
                catch
                {
                    File.Delete(target);
                }
            }

            File.WriteAllBytes(target, pluginBytes);
            result.Messages.Add($"插件已部署：{target}（{pluginBytes.Length / 1024} KB）");
        }
        catch (Exception ex)
        {
            result.Errors.Add($"写入插件失败：{ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Installs BepInEx when it is missing: either from a local zip or by
    /// downloading the release that works with Unity 6000.
    /// </summary>
    public static DeployResult EnsureBepInEx(GameInstall install, string? localZip = null)
    {
        var result = new DeployResult();

        if (Directory.Exists(Paths.BepInExDir(install.Root)))
        {
            result.Messages.Add("BepInEx 已安装");
            return result;
        }

        try
        {
            byte[] zip;
            if (!string.IsNullOrWhiteSpace(localZip))
            {
                zip = File.ReadAllBytes(localZip);
                result.Messages.Add($"使用本地 BepInEx 包：{localZip}");
            }
            else
            {
                result.Messages.Add($"下载 BepInEx：{BepInExUrl}");
                using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                client.DefaultRequestHeaders.UserAgent.ParseAdd("GK2Trainer");
                zip = client.GetByteArrayAsync(BepInExUrl).GetAwaiter().GetResult();
            }

            using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
            var count = 0;
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;   // directory entry
                var destination = Path.Combine(install.Root, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
                count++;
            }

            result.Messages.Add($"BepInEx 已安装（{count} 个文件）");
        }
        catch (Exception ex)
        {
            result.Errors.Add($"安装 BepInEx 失败：{ex.Message}\n" +
                              "可以手动下载后解压到游戏根目录：" + BepInExUrl);
        }

        return result;
    }

    /// <summary>Uninstalls just the trainer plugin (BepInEx itself is left alone).</summary>
    public static DeployResult RemovePlugin(GameInstall install)
    {
        var result = new DeployResult();
        var target = Path.Combine(Paths.PluginsDir(install.Root), Paths.PluginFileName);

        try
        {
            if (File.Exists(target))
            {
                File.Delete(target);
                result.Messages.Add($"已删除插件：{target}");
            }
            else
            {
                result.Messages.Add("插件本来就不在");
            }

            var stale = target + ".old";
            if (File.Exists(stale))
            {
                File.Delete(stale);
                result.Messages.Add($"已删除旧版残留：{Path.GetFileName(stale)}");
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add($"删除失败（游戏运行时需要先关游戏）：{ex.Message}");
        }

        return result;
    }
}
