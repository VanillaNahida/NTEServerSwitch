using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NTEServerSwitch.Core;

internal static class Program
{
    private const string RealGameRoot = @"D:\Neverness To Everness";

    private static int _failures;
    private static int _cheks;

    private static void Check(bool condition, string name, string detail = null)
    {
        _cheks++;
        if (condition)
        {
            Console.WriteLine("  PASS  " + name);
            return;
        }
        _failures++;
        Console.WriteLine("  FAIL  " + name + (detail == null ? "" : "  (" + detail + ")"));
    }

    private static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine("=== " + title + " ===");
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("  (清理失败 " + path + ": " + ex.Message + ")");
        }
    }

    private static int CountFiles(string root)
    {
        if (!Directory.Exists(root))
        {
            return 0;
        }
        return Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length;
    }

    private static long SumSize(string root)
    {
        if (!Directory.Exists(root))
        {
            return 0;
        }
        long total = 0;
        foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            total += new FileInfo(file).Length;
        }
        return total;
    }

    private static async Task<int> Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("exe 目录：" + AppPaths.BaseDirectory);
        Console.WriteLine("缓存目录：" + AppPaths.CacheRoot);

        SafeDelete(AppPaths.CacheRoot);

        var cache = new ResourceCache();
        var syncer = new ResourceSyncer(cache);
        var progress = new Progress<SyncProgress>(p =>
            Console.WriteLine("   > " + p.Stage + " | " + p.Detail));

        Section("诊断：渠道地址");
        {
            string cfg = Path.Combine(RealGameRoot, "NTELauncher", "Config", "Config.ini");
            IniFile ini = IniFile.Parse(TextFile.Read(cfg).Text);
            Console.WriteLine("  [VERSION] VersionInfoFileURL = " + ini.Get("VERSION", "VersionInfoFileURL"));
            Console.WriteLine("  官服 Version.ini = " + Endpoints.VersionIniUrl(ini, ServerChannel.Official));
            Console.WriteLine("  B服  Version.ini = " + Endpoints.VersionIniUrl(ini, ServerChannel.Bilibili));
            Console.WriteLine("  官服 备用      = " + Endpoints.BackupVersionIniUrl(ini, ServerChannel.Official));
            Console.WriteLine("  B服  备用      = " + Endpoints.BackupVersionIniUrl(ini, ServerChannel.Bilibili));
        }

        Section("测试 A：真实网络同步缓存");
        SyncReport report = await syncer.SyncAsync(RealGameRoot, true, progress, CancellationToken.None);
        foreach (LogEntry entry in LogService.Instance.Entries.ToArray())
        {
            Console.WriteLine("  [" + entry.LevelText + "] " + entry.Message);
        }
        Check(report.Success, "同步成功", report.Message);
        Check(report.NetworkAvailable, "网络可用");
        Check(report.VersionMatches, "版本一致", report.LocalVersion + " vs " + report.OfficialVersion);
        Check(report.Official != null && report.Official.FileCount == 12,
              "官服差异文件 = 12", report.Official == null ? "null" : report.Official.FileCount.ToString());
        Check(report.Bilibili != null && report.Bilibili.FileCount == 82,
              "B服差异文件 = 82", report.Bilibili == null ? "null" : report.Bilibili.FileCount.ToString());
        Check(report.Official != null && report.Official.Ready, "官服缓存就绪");
        Check(report.Bilibili != null && report.Bilibili.Ready, "B服缓存就绪");
        Check(File.Exists(AppPaths.StateFilePath), "state.json 已生成");

        CacheState state = cache.LoadState();
        ChannelCacheState officialState = state.GetChannel(ServerChannel.Official);
        ChannelCacheState bilibiliState = state.GetChannel(ServerChannel.Bilibili);
        Check(officialState != null && officialState.RemovePaths.Count == 71, "官服 RemovePaths = 71（全部在 channelSdk/bilibili 下）",
              officialState == null ? "null" : officialState.RemovePaths.Count.ToString());
        Check(officialState != null && officialState.RemovePaths.All(p => p.StartsWith("/channelSdk/bilibili/")),
              "官服删除列表都在 channelSdk/bilibili 下");
        Check(bilibiliState != null && bilibiliState.RemovePaths.Count == 1, "B服 RemovePaths = 1（taptap 独有）",
              bilibiliState == null ? "null" : bilibiliState.RemovePaths.Count.ToString());

        // 逐项校验缓存文件真的和清单一致（尤其是 Config.ini 的两个渠道版本必须不同）
        int bad = 0;
        foreach (CacheFileEntry entry in officialState.Files)
        {
            if (!cache.IsValid(ServerChannel.Official, entry, true))
            {
                bad++;
                Console.WriteLine("        官服缓存不符：" + entry.Path);
            }
        }
        foreach (CacheFileEntry entry in bilibiliState.Files)
        {
            if (!cache.IsValid(ServerChannel.Bilibili, entry, true))
            {
                bad++;
                Console.WriteLine("        B服缓存不符：" + entry.Path);
            }
        }
        Check(bad == 0, "缓存全部文件哈希正确", bad + " 个不符");

        CacheFileEntry officialConfig = officialState.Files.First(e => e.Path == "/Config/Config.ini");
        CacheFileEntry bilibiliConfig = bilibiliState.Files.First(e => e.Path == "/Config/Config.ini");
        Check(officialConfig.Md5 != bilibiliConfig.Md5, "两渠道 Config.ini 内容不同（缓存分离正确）",
              officialConfig.Md5 + " / " + bilibiliConfig.Md5);
        string officialConfigHash = HashUtil.Md5File(cache.CachedPath(ServerChannel.Official, officialConfig));
        Check(HashUtil.Md5Equals(officialConfigHash, officialConfig.Md5), "官服缓存 Config.ini 哈希正确");
        string bilibiliConfigHash = HashUtil.Md5File(cache.CachedPath(ServerChannel.Bilibili, bilibiliConfig));
        Check(HashUtil.Md5Equals(bilibiliConfigHash, bilibiliConfig.Md5), "B服缓存 Config.ini 哈希正确");

        long officialBytes = SumSize(AppPaths.ChannelFilesDirectory(ServerChannel.Official));
        long bilibiliBytes = SumSize(AppPaths.ChannelFilesDirectory(ServerChannel.Bilibili));
        Console.WriteLine("  官服缓存体积：" + PathUtil.FormatBytes(officialBytes) + " / 文件 " +
                          CountFiles(AppPaths.ChannelFilesDirectory(ServerChannel.Official)));
        Console.WriteLine("  B服缓存体积：" + PathUtil.FormatBytes(bilibiliBytes) + " / 文件 " +
                          CountFiles(AppPaths.ChannelFilesDirectory(ServerChannel.Bilibili)));

        Section("测试 B：在假目录里模拟切服");

        // 假游戏目录，避免动到真实启动器
        string fake = Path.Combine(AppPaths.BaseDirectory, "_fakegame");
        SafeDelete(fake);
        string fakeLauncher = Path.Combine(fake, "NTELauncher");
        Directory.CreateDirectory(Path.Combine(fakeLauncher, "Config"));

        string realConfig = Path.Combine(RealGameRoot, "NTELauncher", "Config", "Config.ini");
        File.Copy(realConfig, Path.Combine(fakeLauncher, "Config", "Config.ini"), true);

        // 造几份"上一个渠道残留"的文件：官方删除列表里的前几个
        var junkPaths = new List<string>();
        foreach (string path in officialState.RemovePaths.Take(4))
        {
            string full = PathUtil.SafeCombine(fakeLauncher, path);
            if (full == null)
            {
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, "residue");
            junkPaths.Add(full);
        }
        Directory.CreateDirectory(Path.Combine(fake, "NTELauncher", "UserData"));
        File.WriteAllText(Path.Combine(fake, "NTELauncher", "UserData", "keep.txt"), "userdata");
        Console.WriteLine("  假目录初始文件数：" + CountFiles(fake) + "，残留文件：" + junkPaths.Count);

        var switchService = new SwitchService(cache);

        // B1: 切官服
        Console.WriteLine();
        Console.WriteLine("--- 切到官服 ---");
        SwitchReport toOfficial = switchService.Apply(fake, ServerChannel.Official, state, progress, CancellationToken.None);
        Console.WriteLine("  " + toOfficial.Message);
        Check(toOfficial.Failed == 0, "切官服无失败文件", string.Join("; ", toOfficial.Problems.ToArray()));
        Check(toOfficial.Deleted == junkPaths.Count, "残留文件被删除",
              toOfficial.Deleted + " / " + junkPaths.Count);
        Check(junkPaths.All(p => !File.Exists(p)), "残留文件确实消失");
        Check(File.Exists(Path.Combine(fakeLauncher, "UserData", "keep.txt")), "UserData 未被删除");
        string configAfterOfficial = File.ReadAllText(Path.Combine(fakeLauncher, "Config", "Config.ini"));
        Check(!configAfterOfficial.Contains("Partner=123"), "官服配置无 Partner=123");
        Check(configAfterOfficial.Contains("serialNumber=1"), "官服配置 serialNumber=1");
        Check(configAfterOfficial.Contains("Branch=publish_ob"), "官服配置 Branch=publish_ob");
        Check(!configAfterOfficial.Contains("publish_ob_B"), "官服配置无 publish_ob_B");
        int officialFiles = CountFiles(fakeLauncher) + CountFiles(Path.Combine(fake, "NTELauncher", "UserData")) - CountFiles(Path.Combine(fake, "NTELauncher", "UserData"));
        Console.WriteLine("  切官服后 NTELauncher 文件数：" + CountFiles(fakeLauncher));

        // B2: 切 B 服
        Console.WriteLine();
        Console.WriteLine("--- 切到 B服 ---");
        SwitchReport toBilibili = switchService.Apply(fake, ServerChannel.Bilibili, state, progress, CancellationToken.None);
        Console.WriteLine("  " + toBilibili.Message);
        Check(toBilibili.Failed == 0, "切B服无失败文件", string.Join("; ", toBilibili.Problems.ToArray()));
        string configAfterB = File.ReadAllText(Path.Combine(fakeLauncher, "Config", "Config.ini"));
        Check(configAfterB.Contains("Partner=123"), "B服配置含 Partner=123");
        Check(configAfterB.Contains("publish_ob_B"), "B服配置含 publish_ob_B");
        Check(configAfterB.Contains("serialNumber=2"), "B服配置 serialNumber=2");
        Check(configAfterB.Contains("Branch=publish_ob_B"), "B服配置 Branch=publish_ob_B");

        CacheFileEntry libcef = bilibiliState.Files.FirstOrDefault(e => e.Path == "/channelSdk/bilibili/libcef.dll");
        if (libcef != null)
        {
            string copied = cache.CachedPath(ServerChannel.Bilibili, libcef);
            string destination = PathUtil.SafeCombine(fakeLauncher, libcef.Path);
            Check(File.Exists(destination), "B服 libcef.dll 已落地");
            Check(HashUtil.Matches(destination, libcef.Size, libcef.Md5), "落地文件哈希正确");
        }
        long fakeBytesB = SumSize(fakeLauncher);
        Console.WriteLine("  切B服后体积：" + PathUtil.FormatBytes(fakeBytesB) +
                          "，文件数 " + CountFiles(fakeLauncher));

        // B3: 切回官服，体积必须回落
        Console.WriteLine();
        Console.WriteLine("--- 切回官服（验证体积不累积） ---");
        SwitchReport back = switchService.Apply(fake, ServerChannel.Official, state, progress, CancellationToken.None);
        Console.WriteLine("  " + back.Message);
        Check(back.Failed == 0, "切回官服无失败文件", string.Join("; ", back.Problems.ToArray()));
        long fakeBytesOfficial = SumSize(fakeLauncher) - 6; // UserData\keep.txt = "userdata" 8 字节?
        int fileCountOfficial = CountFiles(fakeLauncher);
        Console.WriteLine("  切回官服后体积：" + PathUtil.FormatBytes(SumSize(fakeLauncher)) +
                          "，文件数 " + fileCountOfficial);
        Check(!File.Exists(Path.Combine(fakeLauncher, "channelSdk", "bilibili", "libcef.dll")),
              "B服 libcef.dll 已被清理");
        Check(officialState.RemovePaths.All(p =>
        {
            string full = PathUtil.SafeCombine(fakeLauncher, p);
            return full == null || !File.Exists(full);
        }), "B服独有文件全部清理干净");
        Check(File.Exists(PathUtil.SafeCombine(fakeLauncher, "/channelSdk/taptap/taptap_api.dll")),
              "官服独有的 taptap_api.dll 已恢复");
        Check(fileCountOfficial <= 13, "文件数回落到官服差异集规模（12 + UserData）", fileCountOfficial.ToString());
        long officialTotal = officialState.Files.Sum(e => e.Size);
        Check(Math.Abs((SumSize(fakeLauncher) - 8) - officialTotal) < 1024 * 1024,
              "体积回到官服差异集大小", PathUtil.FormatBytes(SumSize(fakeLauncher)) + " vs " +
              PathUtil.FormatBytes(officialTotal));

        Section("测试 C：缓存不可用时只改配置");
        SwitchReport noCache = switchService.Apply(fake, ServerChannel.Bilibili, null, null, CancellationToken.None);
        Check(noCache.Success, "无缓存也能切服", noCache.Message);
        Check(!noCache.UsedCache, "确认未使用缓存");
        string configFallback = File.ReadAllText(Path.Combine(fakeLauncher, "Config", "Config.ini"));
        Check(configFallback.Contains("Partner=123") && configFallback.Contains("publish_ob_B"),
              "兜底路径的配置改对了");

        Section("清理");
        syncer.Dispose();
        SafeDelete(fake);
        SafeDelete(AppPaths.CacheRoot);
        Console.WriteLine("已删除假目录与缓存目录。");

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "全部通过：" + _cheks + " 项检查"
            : "失败 " + _failures + " / " + _cheks + " 项检查");
        return _failures == 0 ? 0 : 1;
    }
}
