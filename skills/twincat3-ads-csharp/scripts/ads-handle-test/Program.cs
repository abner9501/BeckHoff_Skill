using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using TwinCAT.Ads;
using TwinCAT.Ads.SumCommand;

// ============================================================
// 句柄缓存 / 生命周期 —— 逐条验证同事 skill 里的说法
//
// 目标 PLC 必须提供 MAIN.arrSum[1..3000]（BYTE 数组，元素有独立句柄）。
//
// 只读为主：所有写操作都不碰 PLC 业务变量；
// 建句柄本身不改任何值，跑完释放本工具创建的句柄。
//
// Usage:
//   ads-handle-test <t1|t2|t3|t4|t6|all> (--local | --netId=x.x.x.x.x.x) [--port=851]
// ============================================================

string? netId = null;
bool useLocal = false;
int port = 851;
string? mode = null;

foreach (string arg in args)
{
    if (arg.Equals("--local", StringComparison.OrdinalIgnoreCase)) useLocal = true;
    else if (arg.StartsWith("--netId=", StringComparison.OrdinalIgnoreCase)) netId = arg[8..];
    else if (arg.StartsWith("--port=", StringComparison.OrdinalIgnoreCase) && int.TryParse(arg[7..], out int parsedPort)) port = parsedPort;
    else if (!arg.StartsWith("--", StringComparison.Ordinal) && mode == null) mode = arg.ToLowerInvariant();
    else FailUsage($"Unknown argument: {arg}");
}

if (mode is not ("t1" or "t2" or "t3" or "t4" or "t6" or "all"))
    FailUsage("Choose exactly one test mode.");
if (useLocal == (netId != null))
    FailUsage("Choose exactly one target: --local or --netId=x.x.x.x.x.x.");
if (port is < 1 or > 65535)
    FailUsage("Port must be between 1 and 65535.");

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine($"Target: {(useLocal ? "local ADS route" : netId)}:{port}; mode: {mode}");

if (mode is "all" or "t1") T1();
if (mode is "all" or "t2") T2();
if (mode is "all" or "t3") T3();
if (mode is "all" or "t4") T4();
if (mode is "all" or "t6") T6();

void Connect(AdsClient client)
{
    if (useLocal) client.Connect(port);
    else client.Connect(new AmsNetId(netId!), port);
}

static void FailUsage(string message)
{
    Console.Error.WriteLine(message);
    Console.Error.WriteLine("Usage: ads-handle-test <t1|t2|t3|t4|t6|all> (--local | --netId=x.x.x.x.x.x) [--port=851]");
    Environment.Exit(2);
}

// ------------------------------------------------------------
// T1：断链之后删句柄，到底抛不抛
// 同事说法：TryDeleteVariableHandle 在链路已断 / 客户端内部表失效时会抛 NRE
// ------------------------------------------------------------
void T1()
{
    Console.WriteLine("=== T1 断链后删句柄 ===");

    var c = new AdsClient();
    Connect(c);

    var hs = new List<uint>();
    for (int i = 1; i <= 10; i++)
        hs.Add(c.CreateVariableHandle($"MAIN.arrSum[{i}]"));
    Console.WriteLine($"已建 10 个句柄：{string.Join(",", hs)}");

    c.Disconnect();
    Console.WriteLine("已 Disconnect（连接已断，客户端对象还在）");

    foreach (var h in hs.Take(5))
    {
        Console.WriteLine($"  h={h}  TryDelete → {Invoke(c, "TryDeleteVariableHandle", h)}");
        Console.WriteLine($"  h={h}  Delete    → {Invoke(c, "DeleteVariableHandle", h)}");
    }

    c.Dispose();
    Console.WriteLine("已 Dispose 客户端，再试一次：");
    Console.WriteLine($"  h={hs[0]}  TryDelete → {Invoke(c, "TryDeleteVariableHandle", hs[0])}");
    Console.WriteLine($"  h={hs[0]}  Delete    → {Invoke(c, "DeleteVariableHandle", hs[0])}");
    Console.WriteLine();
}

// 反射调用，规避"返回值类型是 void 还是有返回值"的编译期差异
static string Invoke(AdsClient c, string method, uint handle)
{
    var mi = typeof(AdsClient).GetMethod(method, new[] { typeof(uint) });
    if (mi == null) return "方法不存在";
    try
    {
        var r = mi.Invoke(c, new object[] { handle });
        return $"未抛，返回 {(r?.ToString() ?? "void")}";
    }
    catch (TargetInvocationException tie)
    {
        var e = tie.InnerException!;
        return $"抛出 {e.GetType().Name}: {e.Message}";
    }
}

// ------------------------------------------------------------
// T2：并发建句柄会不会破坏 SDK 内部状态
// 同事说法：ConcurrentDictionary 只保护字典本身，护不住 SDK 内部表
//          → 症状是「通知大量注册失败 + IndexOutOfRangeException」
// 做法：8 线程 × 200 次并发建（路径互不相同，避开缓存），建完再注册 50 个通知；
//       同一套量在"串行"下再跑一遍做对照。
// ------------------------------------------------------------
void T2()
{
    Console.WriteLine("=== T2 并发建句柄 vs 串行 ===");
    const int threads = 8, per = 200;

    RunPhase("并发", parallel: true, threads, per);
    RunPhase("串行", parallel: false, threads, per);

    void RunPhase(string label, bool parallel, int threads, int per)
    {
        var c = new AdsClient();
        Connect(c);

        var created = new ConcurrentBag<uint>();
        var errs = new ConcurrentBag<string>();
        var sw = Stopwatch.StartNew();

        if (parallel)
        {
            Parallel.For(0, threads, t =>
            {
                for (int j = 0; j < per; j++)
                {
                    string p = $"MAIN.arrSum[{t * 1000 + j + 1}]";
                    try { created.Add(c.CreateVariableHandle(p)); }
                    catch (Exception ex) { errs.Add(ex.GetType().Name); }
                }
            });
        }
        else
        {
            for (int t = 0; t < threads; t++)
                for (int j = 0; j < per; j++)
                {
                    string p = $"MAIN.arrSum[{t * 1000 + j + 1}]";
                    try { created.Add(c.CreateVariableHandle(p)); }
                    catch (Exception ex) { errs.Add(ex.GetType().Name); }
                }
        }
        sw.Stop();

        // 建完之后注册 50 个通知 —— 同事说这里会成片失败
        int ok = 0;
        var notiErrs = new ConcurrentBag<string>();
        var notiHandles = new List<uint>();
        for (int i = 1; i <= 50; i++)
        {
            try
            {
                var h = c.AddDeviceNotificationEx($"MAIN.arrSum[{i}]",
                    new NotificationSettings(AdsTransMode.OnChange, 0, 0), null, typeof(byte));
                notiHandles.Add(h);
                ok++;
            }
            catch (Exception ex) { notiErrs.Add(ex.GetType().Name + ": " + ex.Message); }
        }

        Console.WriteLine($"[{label}] 建句柄 {created.Count}/{threads * per}，异常 {errs.Count} 次" +
                          (errs.Count > 0 ? $"（{string.Join(",", errs.GroupBy(x => x).Select(g => $"{g.Key}×{g.Count()}"))}）" : "") +
                          $"，耗时 {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"[{label}] 通知注册成功 {ok}/50" +
                          (notiErrs.Count > 0 ? $"，失败 {notiErrs.Count}（{string.Join(" | ", notiErrs.Distinct().Take(3))}）" : ""));

        foreach (var nh in notiHandles) { try { c.DeleteDeviceNotification(nh); } catch { } }
        foreach (var h in created) { try { c.DeleteVariableHandle(h); } catch { } }
        c.Dispose();
    }
    Console.WriteLine();
}

// ------------------------------------------------------------
// T3：断连重建的竞态窗口
// 同事说法：只加"客户端生命周期锁"不够，还要一把串行闸门；
//          否则「检查 IsConnected」与「调用 SDK」之间 _client 被置空 → NRE
// 做法：线程 A 不停建/删句柄，线程 B 每 200ms Connect/Disconnect 交替，跑 30 秒
// ------------------------------------------------------------
void T3()
{
    Console.WriteLine("=== T3 断连重建窗口（30 秒） ===");

    var c = new AdsClient();
    Connect(c);

    var errs = new ConcurrentBag<string>();
    int creates = 0;
    var stop = false;

    var worker = Task.Run(() =>
    {
        int i = 1;
        while (!Volatile.Read(ref stop))
        {
            try
            {
                var h = c.CreateVariableHandle($"MAIN.arrSum[{i}]");
                Interlocked.Increment(ref creates);
                c.DeleteVariableHandle(h);
            }
            catch (Exception ex)
            {
                if (ex is NullReferenceException) errs.Add("NRE_STACK\n" + ex);
                else if (ex is not TwinCAT.ClientNotConnectedException) errs.Add("OTHER " + ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace);
            }
            i = i % 5000 + 1;
        }
    });

    var churn = Stopwatch.StartNew();
    int cycles = 0;
    while (churn.Elapsed < TimeSpan.FromSeconds(30))
    {
        try { c.Disconnect(); } catch (Exception ex) { errs.Add("Disconnect: " + ex.GetType().Name); }
        Thread.Sleep(200);
        try { Connect(c); } catch (Exception ex) { errs.Add("Connect: " + ex.GetType().Name + ": " + ex.Message); }
        Thread.Sleep(200);
        cycles++;
    }
    Volatile.Write(ref stop, true);
    worker.Wait();

    Console.WriteLine($"连断循环 {cycles} 轮，期间成功建/删句柄 {creates} 次，异常 {errs.Count} 次");
    if (errs.Count > 0)
        foreach (var g in errs.GroupBy(x => x.Split(':')[0] + ":" + x.Split(':').Skip(1).FirstOrDefault()).OrderByDescending(g => g.Count()).Take(8))
            Console.WriteLine($"  {g.Count()}× {g.Key}");

    Console.WriteLine("--- 非 ClientNotConnected 的异常全文（最多 2 条）---");
    foreach (var e in errs.Where(x => !x.StartsWith("ClientNotConnected")).Take(2))
        Console.WriteLine(e);
    try { c.Dispose(); } catch { }
    Console.WriteLine();
}

// ------------------------------------------------------------
// T4/T5：Sum 一次建 N 个句柄 —— 条数 + 字符数两种预算
// 同事说法：按「200 条 / 4096 字符」切块，否则部分链路直接拒收
// ------------------------------------------------------------
void T4()
{
    Console.WriteLine("=== T4/T5 SumCreateHandles 规模上限 ===");

    var c = new AdsClient();
    Connect(c);

    foreach (int n in new[] { 200, 250, 500, 1000, 2000, 3000 })
    {
        var paths = Enumerable.Range(1, n).Select(i => $"MAIN.arrSum[{i}]").ToArray();
        int chars = paths.Sum(p => p.Length);

        var sw = Stopwatch.StartNew();
        string verdict;
        uint[]? got = null;
        try
        {
            var cmd = new SumCreateHandles(c, paths);
            var res = cmd.CreateHandles();
            sw.Stop();
            got = res.Handles;
            int zeros = got?.Count(h => h == 0) ?? 0;
            verdict = $"Succeeded={res.Succeeded} 返回 {got?.Length ?? 0} 个 零句柄 {zeros} 个";
        }
        catch (Exception ex)
        {
            sw.Stop();
            verdict = $"抛出 {ex.GetType().Name}: {ex.Message}";
        }

        Console.WriteLine($"  N={n,5}  字符={chars,6}  {sw.ElapsedMilliseconds,5}ms  {verdict}");

        if (got != null && got.Length > 0)
        {
            try
            {
                var rel = new SumReleaseHandles(c, got);
                rel.ReleaseHandles();
            }
            catch (Exception ex) { Console.WriteLine($"    释放失败：{ex.GetType().Name}: {ex.Message}"); }
        }
    }
    c.Dispose();
    Console.WriteLine();
}

// ------------------------------------------------------------
// T6：失败路径没有冷却，代价有多大
// 同事说法：失败不冷却，100ms 一刷的页面会变成每秒十几次建句柄往返，把界面拖死
// 做法：连打 200 次不存在的变量；再拿"已缓存路径连打 200 次"做对照
// ------------------------------------------------------------
void T6()
{
    Console.WriteLine("=== T6 失败路径 vs 缓存命中 ===");

    var c = new AdsClient();
    Connect(c);

    // ① 失败路径
    var codes = new ConcurrentDictionary<AdsErrorCode, int>();
    var sw = Stopwatch.StartNew();
    for (int i = 0; i < 200; i++)
    {
        try { c.CreateVariableHandle($"GVL_NoSuch.bX{i}"); }
        catch (AdsErrorException ex) { codes.AddOrUpdate(ex.ErrorCode, 1, (_, v) => v + 1); }
        catch (Exception ex) { codes.AddOrUpdate(0, 1, (_, v) => v + 1); _ = ex; }
    }
    sw.Stop();
    Console.WriteLine($"  不存在的变量 ×200：{sw.ElapsedMilliseconds}ms，均摊 {sw.ElapsedMilliseconds / 200.0:F3}ms/次，" +
                      $"错误码 {string.Join(",", codes.Select(kv => $"0x{(int)kv.Key:X}({kv.Value})"))}");

    // ② 缓存命中（同一个有效路径反复建）
    var h0 = c.CreateVariableHandle("MAIN.arrSum[1]");
    sw.Restart();
    for (int i = 0; i < 200; i++) c.CreateVariableHandle("MAIN.arrSum[1]");
    sw.Stop();
    Console.WriteLine($"  已缓存路径 ×200：{sw.ElapsedMilliseconds}ms，均摊 {sw.ElapsedMilliseconds / 200.0:F3}ms/次");
    c.DeleteVariableHandle(h0);

    // ③ 每次换一个新路径（模拟"一堆不同符号逐个失败"）
    sw.Restart();
    for (int i = 0; i < 200; i++)
    {
        try { c.CreateVariableHandle($"GVL_NoSuch2.bY{i}"); } catch { }
    }
    sw.Stop();
    Console.WriteLine($"  200 个不同不存在变量：{sw.ElapsedMilliseconds}ms，均摊 {sw.ElapsedMilliseconds / 200.0:F3}ms/次");

    c.Dispose();
    Console.WriteLine();
}
