using System.Diagnostics;
using AdsShared;
using TwinCAT;
using TwinCAT.Ads;
using TwinCAT.Ads.SumCommand;
using TwinCAT.Ads.TypeSystem;
using TwinCAT.TypeSystem;

// ADS 现场体检工具：功能 + 性能 + 结构体
//
//   dotnet run -- <mode> [参数] [--netId=x.x.x.x.x.x] [--port=851]
//
//     check   <symbol> [type]             连通性 + 读 + 写 + 回读 + 还原
//     speed   <symbol> [type] [n]         符号名 vs 句柄（先测符号名再建句柄）
//     bench   <symbol> [size] [n] [--write] 数据量（默认只读；写要显式 --write）
//     sum     <basePath> [n]              批量 Sum（basePath[1..n]）
//     mixed   <symbol> <ig> <io> <len>    混装内存 + 句柄（同一块数据对比）
//     notify  <symbol> [seconds]          通知（Ex；结构体自动走 byte[] + 长度）
//     struct  <symbol> [--notify|--write|--mem|--arr]   结构体探针
//     symbols [filter]                    列出符号表（含子符号）

var positional = new List<string>();
var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
string? netId = null;
int port = 851;

foreach (string a in args)
{
    if (a.StartsWith("--netId=", StringComparison.OrdinalIgnoreCase)) netId = a[8..];
    else if (a.StartsWith("--port=", StringComparison.OrdinalIgnoreCase)) port = int.Parse(a[7..]);
    else if (a.StartsWith("--")) flags.Add(a);
    else positional.Add(a);
}

if (positional.Count == 0 || positional[0] is "help" or "-h" or "--help")
{
    PrintUsage();
    return 0;
}

string mode = positional[0].ToLowerInvariant();
string[] rest = positional.Skip(1).ToArray();

using var client = new AdsClient();
try
{
    if (netId != null) client.Connect(new AmsNetId(netId), port);
    else client.Connect(port);
}
catch (Exception ex)
{
    Console.WriteLine($"连接失败（{(netId ?? "127.0.0.1.1.1")}:{port}）：{ex.GetType().Name}: {ex.Message}");
    return 1;
}

Console.WriteLine($"已连接 {client.Address}  IsConnected={client.IsConnected}");
Console.WriteLine();

try
{
    return mode switch
    {
        "info" => Info(client),
        "read" => Read(client, rest),
        "write" => Write(client, rest),
        "pollload" => PollLoad(client, rest),
        "check" => Check(client, rest),
        "speed" => Speed(client, rest),
        "lat" => Lat(client, rest, flags),
        "bench" => Bench(client, rest, flags),
        "sum" => Sum(client, rest),
        "mixed" => Mixed(client, rest),
        "notify" => Notify(client, rest),
        "struct" => StructProbe.Run(client, rest.FirstOrDefault(), flags),
        "symbols" => Symbols(client, rest.FirstOrDefault()),
        "addr" => AddrMap(client, rest.FirstOrDefault()),
        _ => Unknown(mode),
    };
}
catch (Exception ex)
{
    Console.WriteLine($"✗ {ex.GetType().Name}: {ex.Message}");
    return 2;
}

static int Unknown(string mode)
{
    Console.WriteLine($"未知模式：{mode}");
    PrintUsage();
    return 2;
}

static void PrintUsage()
{
    Console.WriteLine("""
        用法：dotnet run -- <mode> [参数] [--netId=x.x.x.x.x.x] [--port=851]

          info                                 设备信息（名称 / 版本 / ADS 状态）
          read    <symbol> [type]               只读一次（绝不写）
          write   <symbol> <value>              写一次并回读
          pollload <symbol> [秒/档] [速率档...]  按速率档逐步加载（默认 10,20,50,100,200 /s）
                                                每档后读 PLC 任务统计；一有下降就停
          check   <symbol> [type]               连通性 + 读 + 写 + 回读 + 还原
          speed   <symbol> [type] [n]           符号名 vs 句柄（默认 n=200）
          lat     <symbol> [n] [--members] [--write]
                                                句柄 / 内存 / 符号名 三条路的时延
                                                （--members：逐成员读一遍 vs 整块读一次；
                                                  --write：再量写回原值，不改内容）
          bench   <symbol> [size] [n] [--write] 数据量（默认只读；写要 --write）
          sum     <basePath> [n]                批量 Sum（basePath[1..n]）
          mixed   <symbol> <ig> <io> <len>      混装内存 + 句柄（同一块数据对比）
          notify  <symbol> [seconds]            通知（Ex；结构体走 byte[] + 长度）
          struct  <symbol> [--notify|--write|--mem|--arr]
                                                结构体探针（句柄/布局/整块写/Ex/数组套结构体）
          symbols [filter]                      列出符号表（含子符号）
          addr    [filter]                      只读：按 IndexGroup 汇总符号，打印 IO / 大小

        示例：
          check GVL_AdsDemo.lrReal
          speed GVL_AdsDemo.lrReal 200
          bench MAIN.arrSum 20000 20
          sum GVL_AdsDemo.aInts 5
          mixed MAIN.ST1.lSetPos1 0x4020 405152 8
          notify GVL_AdsDemo.bHeartbeat 5
          struct MAIN.ST1 --mem --arr
        """);
}

// ---------------- info ----------------
static int Info(AdsClient c)
{
    var d = c.ReadDeviceInfo();
    var s = c.ReadState();
    Console.WriteLine($"  设备名：{d.Name}");
    Console.WriteLine($"  版本：  {d.Version.Version}.{d.Version.Revision}.{d.Version.Build}");
    Console.WriteLine($"  ADS 状态：{s.AdsState}   设备状态：{s.DeviceState}");
    return 0;
}

// ---------------- read ----------------
static int Read(AdsClient c, string[] rest)
{
    if (rest.Length == 0) { Console.WriteLine("read 需要 <symbol>"); return 2; }
    string symbol = rest[0];
    string? typeName = rest.Length > 1 ? rest[1] : null;

    var sym = c.ReadSymbol(symbol);
    Type? t = MapType(typeName) ?? MapType(sym.TypeName);
    if (t == null)
    {
        object? raw = c.ReadValue(symbol, typeof(byte[]));
        Console.WriteLine($"  {symbol} = {Fmt(raw)}  [byte[]]");
        return 0;
    }

    object? value = c.ReadValue(symbol, t);
    Console.WriteLine($"  {symbol} = {Fmt(value)}  [{t.Name}]");
    return 0;
}

// ---------------- write ----------------
static int Write(AdsClient c, string[] rest)
{
    if (rest.Length < 2) { Console.WriteLine("write 需要 <symbol> <value>"); return 2; }
    string symbol = rest[0];
    string text = rest[1];

    var sym = c.ReadSymbol(symbol);
    Type t = MapType(sym.TypeName) ?? typeof(string);

    object value;
    if (t == typeof(bool))
        value = text is "1" or "true" or "TRUE" or "True";
    else if (t == typeof(string))
        value = text;
    else
        value = Convert.ChangeType(text, t);

    c.WriteValue(symbol, value);
    Console.WriteLine($"  write {symbol} = {Fmt(value)}  [{t.Name}]");
    Console.WriteLine($"  回读 = {Fmt(c.ReadValue(symbol, t))}");
    return 0;
}

// ---------------- pollload ----------------
static int PollLoad(AdsClient c, string[] rest)
{
    if (rest.Length == 0) { Console.WriteLine("pollload 需要 <symbol>"); return 2; }
    string symbol = rest[0];
    int seconds = rest.Length > 1 ? int.Parse(rest[1]) : 3;
    int[] rates = rest.Length > 2
        ? rest.Skip(2).Select(int.Parse).ToArray()
        : new[] { 10, 20, 50, 100, 200 };

    var sym = c.ReadSymbol(symbol);
    Type? t = MapType(sym.TypeName);
    uint h = c.CreateVariableHandle(symbol);
    byte[] buf = new byte[sym.ByteSize];

    Console.WriteLine($"  负载对象 {symbol} [{sym.TypeName}]  每档 {seconds}s  " +
                      $"速率档：{string.Join(", ", rates)}");
    PrintTaskStats(c, "基线");

    double baselineP95 = 0;
    for (int step = 0; step < rates.Length; step++)
    {
        int rate = rates[step];
        double interval = 1000.0 / rate;
        var lat = new List<double>(rate * seconds + 10);
        int errors = 0;
        int ops = 0;

        var swAll = Stopwatch.StartNew();
        double next = 0;

        while (swAll.Elapsed.TotalSeconds < seconds)
        {
            double now = swAll.Elapsed.TotalMilliseconds;
            if (now < next)
            {
                double wait = next - now;
                if (wait > 1.5) Thread.Sleep((int)(wait - 1));
                while (swAll.Elapsed.TotalMilliseconds < next) Thread.SpinWait(100);
            }

            var sw = Stopwatch.StartNew();
            try
            {
                if (t != null) c.ReadAny(h, t);
                else c.Read(0xF005, h, buf.AsMemory());
                ops++;
            }
            catch (Exception ex)
            {
                errors++;
                if (errors == 1) Console.WriteLine($"    首个错误：{ex.GetType().Name}: {ex.Message}");
            }
            sw.Stop();
            lat.Add(sw.Elapsed.TotalMilliseconds);

            next += interval;
            if (next < swAll.Elapsed.TotalMilliseconds) next = swAll.Elapsed.TotalMilliseconds;
        }
        swAll.Stop();

        var sorted = lat.OrderBy(x => x).ToArray();
        double p50 = Percentile(sorted, 50);
        double p95 = Percentile(sorted, 95);
        double p99 = Percentile(sorted, 99);
        double achieved = ops / swAll.Elapsed.TotalSeconds;
        if (step == 0) baselineP95 = p95;

        Console.WriteLine($"  目标 {rate}/s → 实际 {achieved:F1}/s  成功 {ops} 错误 {errors}  " +
                          $"p50={p50:F2}ms p95={p95:F2}ms p99={p99:F2}ms");
        PrintTaskStats(c, $"{rate}/s 后");

        bool degraded = errors > 0 || (step > 0 && p95 > Math.Max(baselineP95 * 2, baselineP95 + 1.0));
        if (degraded)
        {
            Console.WriteLine("  ⚠ 性能下降：已停止升档（等你判断是否继续）");
            break;
        }
    }

    c.DeleteVariableHandle(h);
    return 0;
}

static double Percentile(double[] sorted, double p)
{
    if (sorted.Length == 0) return 0;
    double idx = (p / 100.0) * (sorted.Length - 1);
    int lo = (int)Math.Floor(idx);
    int hi = (int)Math.Ceiling(idx);
    return sorted[lo] + (sorted[hi] - sorted[lo]) * (idx - lo);
}

static void PrintTaskStats(AdsClient c, string label)
{
    string[] paths =
    {
        "TwinCAT_SystemInfoVarList.__PlcTask.dwLastCycleTime",
        "TwinCAT_SystemInfoVarList.__PlcTask.dwAverageCycleTime",
        "TwinCAT_SystemInfoVarList.__PlcTask.dwMaxCycleTime",
        "TwinCAT_SystemInfoVarList.__PlcTask.diJitter",
        "TwinCAT_SystemInfoVarList.__PlcTask.diJitterMax",
        "TwinCAT_SystemInfoVarList._TaskInfo[1].LastExecTime",
        "TwinCAT_SystemInfoVarList._TaskInfo[1].CycleTimeExceeded",
        "TwinCAT_SystemInfoVarList._TaskInfo[1].RTViolation",
    };

    var parts = new List<string>();
    foreach (string p in paths)
    {
        try
        {
            var s = c.ReadSymbol(p);
            Type? t = MapType(s.TypeName);
            if (t == null) continue;
            object? v = c.ReadValue(p, t);
            parts.Add($"{p[(p.LastIndexOf('.') + 1)..]}={Fmt(v)}");
        }
        catch { /* 现场不一定有这些符号 */ }
    }
    Console.WriteLine($"  [{label}] {string.Join("  ", parts)}");
}

// ---------------- check ----------------
static int Check(AdsClient c, string[] rest)
{
    if (rest.Length == 0) { Console.WriteLine("check 需要 <symbol>"); return 2; }
    string symbol = rest[0];
    string? typeName = rest.Length > 1 ? rest[1] : null;

    var sym = c.ReadSymbol(symbol);
    Type? t = MapType(typeName) ?? MapType(sym.TypeName);
    if (t == null)
    {
        Console.WriteLine($"无法确定 {symbol} 的类型（TypeName={sym.TypeName}），请显式给 type");
        return 2;
    }

    object? original = c.ReadValue(symbol, t);
    Console.WriteLine($"读 {symbol} = {Fmt(original)}   [{t.Name}]");

    if (t == typeof(string))
    {
        Console.WriteLine("字符串：跳过写测试（长度由 PLC 决定）");
        return 0;
    }

    try
    {
        object next = t == typeof(bool)
            ? !(bool)original!
            : Convert.ChangeType(Convert.ToDouble(original) + 1, t);

        c.WriteValue(symbol, next);
        object? back = c.ReadValue(symbol, t);
        Console.WriteLine($"写 {Fmt(next)} → 回读 {Fmt(back)}   {(Equals(back, next) ? "✅" : "❌")}");

        c.WriteValue(symbol, original!);
        object? restored = c.ReadValue(symbol, t);
        Console.WriteLine($"还原 {Fmt(original)} → 回读 {Fmt(restored)}   {(Equals(restored, original) ? "✅" : "❌")}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"写测试失败：{ex.GetType().Name}: {ex.Message}");
        return 3;
    }

    return 0;
}

// ---------------- speed ----------------
static int Speed(AdsClient c, string[] rest)
{
    if (rest.Length == 0) { Console.WriteLine("speed 需要 <symbol>"); return 2; }
    string symbol = rest[0];
    string? typeName = rest.Length > 1 ? rest[1] : null;
    int n = rest.Length > 2 ? int.Parse(rest[2]) : 200;

    var sym = c.ReadSymbol(symbol);
    Type? t = MapType(typeName) ?? MapType(sym.TypeName);
    if (t == null) { Console.WriteLine($"无法确定类型（TypeName={sym.TypeName}），请显式给 type"); return 2; }

    // ★ 先测符号名、再建句柄 —— 顺序反了会让库缓存干扰，测出 1.0x 假象
    c.ReadValue(symbol, t); c.ReadValue(symbol, t);
    var sw = Stopwatch.StartNew();
    for (int i = 0; i < n; i++) c.ReadValue(symbol, t);
    sw.Stop();
    double byName = sw.Elapsed.TotalMilliseconds / n;

    uint h = c.CreateVariableHandle(symbol);
    c.ReadAny(h, t);
    sw.Restart();
    for (int i = 0; i < n; i++) c.ReadAny(h, t);
    sw.Stop();
    double byHandle = sw.Elapsed.TotalMilliseconds / n;
    c.DeleteVariableHandle(h);

    Console.WriteLine($"  {symbol}  [{t.Name}]  n={n}");
    Console.WriteLine($"  符号名：{byName:F3} ms / 次");
    Console.WriteLine($"  句柄：  {byHandle:F3} ms / 次");
    Console.WriteLine($"  句柄快 {(byName / Math.Max(byHandle, 0.0001)):F2} 倍（解析成本 {(byName - byHandle):F3} ms/次）");
    return 0;
}

// ---------------- lat ----------------
static int Lat(AdsClient c, string[] rest, ISet<string> flags)
{
    if (rest.Length == 0) { Console.WriteLine("lat 需要 <symbol>"); return 2; }
    string symbol = rest[0];
    int n = rest.Length > 1 ? int.Parse(rest[1]) : 500;

    var sym = c.ReadSymbol(symbol);
    int size = sym.ByteSize;
    Console.WriteLine($"  {symbol}  {sym.TypeName}  ByteSize={size}  IG=0x{sym.IndexGroup:X}/{sym.IndexOffset}");

    var sw = Stopwatch.StartNew();

    // 1. 符号名读（先测：此时句柄缓存里还没有这个变量）
    Type? t = MapType(sym.TypeName);
    c.ReadValue(symbol, t ?? typeof(byte[]));
    sw.Restart();
    for (int i = 0; i < n; i++) c.ReadValue(symbol, t ?? typeof(byte[]));
    sw.Stop();
    Console.WriteLine($"  符号名读 {size}B（无缓存句柄）：{sw.Elapsed.TotalMilliseconds / n:F3} ms/次" +
                      $"  [{(t?.Name ?? "byte[]，每次新分配")}]");

    // 2. 句柄读
    uint h = c.CreateVariableHandle(symbol);
    byte[] buf = new byte[size];
    c.Read(0xF005, h, buf.AsMemory());
    sw.Restart();
    for (int i = 0; i < n; i++) c.Read(0xF005, h, buf.AsMemory());
    sw.Stop();
    double byHandle = sw.Elapsed.TotalMilliseconds / n;
    Console.WriteLine($"  句柄读 {size}B：{byHandle:F3} ms/次");

    // 3. 符号名读（句柄已经在客户端缓存里）
    c.ReadValue(symbol, t ?? typeof(byte[]));
    sw.Restart();
    for (int i = 0; i < n; i++) c.ReadValue(symbol, t ?? typeof(byte[]));
    sw.Stop();
    Console.WriteLine($"  符号名读 {size}B（句柄缓存命中）：{sw.Elapsed.TotalMilliseconds / n:F3} ms/次");

    // 4. 按名读 ReadAny(path, type) → 0xF004
    try
    {
        if (t != null)
        {
            c.ReadAny(symbol, t);
            sw.Restart();
            for (int i = 0; i < n; i++) c.ReadAny(symbol, t);
            sw.Stop();
        }
        else
        {
            c.ReadAny(symbol, typeof(byte[]), new[] { size });
            sw.Restart();
            for (int i = 0; i < n; i++) c.ReadAny(symbol, typeof(byte[]), new[] { size });
            sw.Stop();
        }
        Console.WriteLine($"  按名读 ReadAny（0xF004）{size}B：{sw.Elapsed.TotalMilliseconds / n:F3} ms/次");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  按名读 ReadAny（0xF004）失败：{ex.GetType().Name}: {ex.Message}");
    }

    // 5. 内存读
    c.Read(sym.IndexGroup, sym.IndexOffset, buf.AsMemory());
    sw.Restart();
    for (int i = 0; i < n; i++) c.Read(sym.IndexGroup, sym.IndexOffset, buf.AsMemory());
    sw.Stop();
    double byMem = sw.Elapsed.TotalMilliseconds / n;
    Console.WriteLine($"  内存读 {size}B：{byMem:F3} ms/次");

    // 6. 写（把刚读到的原值写回去，内容不变）
    if (flags.Contains("--write"))
    {
        c.Read(0xF005, h, buf.AsMemory());                  // 取一份当前值
        c.Write(0xF005, h, buf.AsMemory());                 // 预热
        sw.Restart();
        for (int i = 0; i < n; i++) c.Write(0xF005, h, buf.AsMemory());
        sw.Stop();
        Console.WriteLine($"  句柄写 {size}B：{sw.Elapsed.TotalMilliseconds / n:F3} ms/次（写回原值）");

        c.Write(sym.IndexGroup, sym.IndexOffset, buf.AsMemory());
        sw.Restart();
        for (int i = 0; i < n; i++) c.Write(sym.IndexGroup, sym.IndexOffset, buf.AsMemory());
        sw.Stop();
        Console.WriteLine($"  内存写 {size}B：{sw.Elapsed.TotalMilliseconds / n:F3} ms/次");
    }

    // 7. 逐成员读 vs 整块读
    if (flags.Contains("--members") && sym.SubSymbols.Count > 0)
    {
        var handles = new List<(uint Handle, byte[] Buf)>();
        foreach (ISymbol m in sym.SubSymbols)
        {
            uint mh = c.CreateVariableHandle(m.InstancePath);
            handles.Add((mh, new byte[m.ByteSize]));
        }

        foreach (var (mh, b) in handles) c.Read(0xF005, mh, b.AsMemory());   // 预热
        sw.Restart();
        for (int i = 0; i < n; i++)
            foreach (var (mh, b) in handles)
                c.Read(0xF005, mh, b.AsMemory());
        sw.Stop();

        double perPass = sw.Elapsed.TotalMilliseconds / n;
        Console.WriteLine($"  逐成员读 {handles.Count} 个：{perPass:F3} ms/轮（均摊 {perPass / handles.Count:F3} ms/成员）");
        Console.WriteLine($"  → 整块 1 次 vs 逐成员 {handles.Count} 次：{byHandle:F3} ms vs {perPass:F3} ms");

        foreach (var (mh, _) in handles) c.DeleteVariableHandle(mh);
    }

    c.DeleteVariableHandle(h);
    return 0;
}

// ---------------- bench ----------------
static int Bench(AdsClient c, string[] rest, ISet<string> flags)
{
    if (rest.Length == 0) { Console.WriteLine("bench 需要 <symbol>"); return 2; }
    string symbol = rest[0];
    var sym = c.ReadSymbol(symbol);
    int size = rest.Length > 1 ? int.Parse(rest[1]) : sym.ByteSize;
    int n = rest.Length > 2 ? int.Parse(rest[2]) : 100;

    // ★ 硬保护：绝不允许越过符号大小；大块默认拒绝（现场 2MB 打停过控制器）
    if (size > sym.ByteSize)
    {
        Console.WriteLine($"  ✗ 拒绝：请求 {size}B 超过符号 {symbol} 的大小 {sym.ByteSize}B —— 会写到相邻变量");
        return 2;
    }
    if (size > 1024 * 1024 && !flags.Contains("--allow-large"))
    {
        Console.WriteLine($"  ✗ 拒绝：请求 {size}B > 1MB。AMS 路由器默认 2048kB，大块可能打停控制器。");
        Console.WriteLine("     确认要测请加 --allow-large（仍禁止 >2,000,000B）");
        return 2;
    }
    if (size > 2_000_000)
    {
        Console.WriteLine($"  ✗ 拒绝：请求 {size}B > 2,000,000B（AMS 路由器默认 2048kB 上限附近）");
        return 2;
    }
    if (size > 1024 * 1024)
        Console.WriteLine($"  ⚠ 大块模式：{size}B，接近 AMS 路由器上限，建议分段");

    byte[] buf = new byte[size];

    if (flags.Contains("--asresult"))
    {
        // 不经过 "收进内部缓冲再拷给用户 buffer" 的 Read() 便利重载
        c.ReadAsResult(sym.IndexGroup, sym.IndexOffset, size);
        var swr = Stopwatch.StartNew();
        for (int i = 0; i < n; i++) c.ReadAsResult(sym.IndexGroup, sym.IndexOffset, size);
        swr.Stop();
        Console.WriteLine($"  {symbol}  读(ReadAsResult) {size}B × {n} = {swr.Elapsed.TotalMilliseconds / n:F3} ms / 次");

        c.WriteAsResult(sym.IndexGroup, sym.IndexOffset, buf.AsMemory());
        var sww = Stopwatch.StartNew();
        for (int i = 0; i < n; i++) c.WriteAsResult(sym.IndexGroup, sym.IndexOffset, buf.AsMemory());
        sww.Stop();
        Console.WriteLine($"  {symbol}  写(WriteAsResult) {size}B × {n} = {sww.Elapsed.TotalMilliseconds / n:F3} ms / 次");
        return 0;
    }

    c.Read(sym.IndexGroup, sym.IndexOffset, buf.AsMemory());   // 预热

    var sw = Stopwatch.StartNew();
    for (int i = 0; i < n; i++) c.Read(sym.IndexGroup, sym.IndexOffset, buf.AsMemory());
    sw.Stop();
    Console.WriteLine($"  {symbol}  读 {size}B × {n} = {sw.Elapsed.TotalMilliseconds / n:F3} ms / 次");

    if (!flags.Contains("--write"))
    {
        Console.WriteLine("  写测试跳过（加 --write 才会往这块内存写 0）");
        return 0;
    }

    // 写回刚读到的原内容（不改数据），只测写路径
    c.Write(sym.IndexGroup, sym.IndexOffset, buf.AsMemory());  // 预热
    sw.Restart();
    for (int i = 0; i < n; i++) c.Write(sym.IndexGroup, sym.IndexOffset, buf.AsMemory());
    sw.Stop();
    Console.WriteLine($"  {symbol}  写 {size}B × {n} = {sw.Elapsed.TotalMilliseconds / n:F3} ms / 次");
    return 0;
}

// ---------------- sum ----------------
static int Sum(AdsClient c, string[] rest)
{
    if (rest.Length == 0) { Console.WriteLine("sum 需要 <basePath>"); return 2; }
    string basePath = rest[0];
    int n = rest.Length > 1 ? int.Parse(rest[1]) : 100;
    int iters = rest.Length > 2 ? int.Parse(rest[2]) : 1;

    string[] paths = Enumerable.Range(1, n).Select(i => $"{basePath}[{i}]").ToArray();

    Type?[] types = paths.Select(p =>
    {
        try { return MapType(c.ReadSymbol(p).TypeName); }
        catch { return null; }
    }).ToArray();

    var sw = Stopwatch.StartNew();
    var create = new SumCreateHandles(c, paths);
    var handles = create.CreateHandles();
    sw.Stop();
    string[] got = handles.Handles?.Select(h => $"0x{h:X}").ToArray() ?? Array.Empty<string>();
    Console.WriteLine($"  建句柄 {paths.Length} 个：{sw.Elapsed.TotalMilliseconds:F1} ms");
    Console.WriteLine($"    前 5 个：{string.Join(", ", got.Take(5))}");

    var known = types.Select((t, i) => (t, i)).Where(x => x.t != null).ToList();
    var knownHandles = known.Select(x => handles.Handles![x.i]).ToArray();
    var knownTypes = known.Select(x => x.t!).ToArray();

    if (knownHandles.Length > 0)
    {
        var cmd = new SumHandleReadAnyType(c, knownHandles, knownTypes, SumFallbackMode.All);

        // 预热
        var warm = cmd.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
        if (!warm.Succeeded)
        {
            Console.WriteLine($"  预热失败：ErrorCode={warm.ErrorCode}");
        }

        sw.Restart();
        var rr = warm;
        for (int i = 0; i < iters; i++)
            rr = cmd.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
        sw.Stop();

        double avgMs = sw.Elapsed.TotalMilliseconds / iters;
        Console.WriteLine($"  批量读 {knownHandles.Length} 个 × {iters} 次：均摊 {avgMs:F3} ms/次  " +
                          $"请求 {1000.0 / avgMs:F1}/s  变量 {knownHandles.Length * 1000.0 / avgMs:F0}/s  " +
                          $"Succeeded={rr.Succeeded}");
        if (rr.Succeeded)
            Console.WriteLine($"    前 5 个值：{string.Join(", ", rr.Values.Take(5).Select(Fmt))}");
        else
            Console.WriteLine($"    ErrorCode={rr.ErrorCode}");
    }

    var release = new SumReleaseHandles(c, handles.Handles!);
    release.ReleaseHandlesAsync(CancellationToken.None).GetAwaiter().GetResult();
    Console.WriteLine("  已释放句柄（SumReleaseHandles）");
    return 0;
}

// ---------------- mixed ----------------
static int Mixed(AdsClient c, string[] rest)
{
    if (rest.Length < 4) { Console.WriteLine("mixed 需要 <symbol> <ig> <io> <len>"); return 2; }
    string symbol = rest[0];
    uint ig = ParseUInt(rest[1]);
    uint io = ParseUInt(rest[2]);
    int len = int.Parse(rest[3]);

    uint h = c.CreateVariableHandle(symbol);
    try
    {
        var entities = new List<SumDataEntity>
        {
            new IgIoSumReadEntity(0xF005, h, len),
            new IgIoSumReadEntity(ig, io, len),
        };
        var cmd = new SumRead(c, entities, SumAccessMode.IndexGroupIndexOffset, SumCommandErrorStrategy.Strict);
        var r0 = cmd.ReadRaw();
        Console.WriteLine($"  子命令 {r0.SubCommandCount} 个，返回块 {r0.ReadBlocks?.Count}");
        if (r0.ReadBlocks != null)
        {
            for (int i = 0; i < r0.ReadBlocks.Count; i++)
            {
                var b = r0.ReadBlocks[i];
                Console.WriteLine($"    块{i + 1}：{b.Length}B  首字节 {Convert.ToHexString(b.Span[..Math.Min(8, b.Length)])}");
            }
            if (r0.ReadBlocks.Count >= 2)
                Console.WriteLine($"    两块逐字节相同 = {r0.ReadBlocks[0].Span.SequenceEqual(r0.ReadBlocks[1].Span)}");
        }

        const int n = 100;
        cmd.ReadRaw();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < n; i++) cmd.ReadRaw();
        sw.Stop();
        Console.WriteLine($"  混合 Sum {n} 次：{sw.Elapsed.TotalMilliseconds / n:F3} ms / 次");
    }
    finally
    {
        c.DeleteVariableHandle(h);
    }
    return 0;
}

// ---------------- notify ----------------
static int Notify(AdsClient c, string[] rest)
{
    if (rest.Length == 0) { Console.WriteLine("notify 需要 <symbol>"); return 2; }
    string symbol = rest[0];
    int seconds = rest.Length > 1 ? int.Parse(rest[1]) : 5;

    var sym = c.ReadSymbol(symbol);
    int count = 0;
    void OnEx(object? sender, AdsNotificationExEventArgs e)
    {
        count++;
        Console.WriteLine($"  [{count}] handle={e.Handle}  {Fmt(e.Value)}");
    }

    c.AdsNotificationEx += OnEx;

    var settings = new NotificationSettings(AdsTransMode.OnChange, 0, 0);
    Type? t = MapType(sym.TypeName);
    uint nh;

    if (t == typeof(string))
    {
        int chars = sym.BitSize / 8 - 1;
        nh = c.AddDeviceNotificationEx(symbol, settings, symbol, typeof(string), new[] { chars });
    }
    else if (t != null)
    {
        nh = c.AddDeviceNotificationEx(symbol, settings, symbol, t);
    }
    else
    {
        // 结构体 / 数组：按原始字节订阅，必须给长度
        nh = c.AddDeviceNotificationEx(symbol, settings, symbol, typeof(byte[]), new[] { sym.ByteSize });
    }

    Console.WriteLine($"  注册 {symbol}（{(t?.Name ?? $"byte[{sym.ByteSize}]")}）= 0x{nh:X}，观察 {seconds} 秒…");
    Thread.Sleep(seconds * 1000);
    Console.WriteLine($"  共收到 {count} 条通知");

    c.DeleteDeviceNotification(nh);
    c.AdsNotificationEx -= OnEx;
    return 0;
}

// ---------------- symbols ----------------
static int Symbols(AdsClient c, string? filter)
{
    ISymbolLoader loader = SymbolLoaderFactory.Create(c, SymbolLoaderSettings.Default);
    int count = 0;

    void Walk(ISymbol s, int depth)
    {
        // 注意：父节点不匹配时不能直接 return —— 子符号可能匹配
        bool match = filter == null ||
                     s.InstancePath.Contains(filter, StringComparison.OrdinalIgnoreCase);
        if (match)
        {
            count++;
            string indent = new string(' ', depth * 2);
            string size = s.ByteSize > 0 ? $"{s.ByteSize}B" : "";
            Console.WriteLine($"{indent}{s.InstancePath,-50} {s.TypeName,-24} {s.Category,-12} {size}");
        }
        foreach (ISymbol child in s.SubSymbols)
            Walk(child, depth + 1);
    }

    foreach (ISymbol s in loader.Symbols)
        Walk(s, 0);

    Console.WriteLine($"共 {count} 个（filter={(filter ?? "<无>")}）");
    return 0;
}

// ---------------- addr ----------------
static int AddrMap(AdsClient c, string? filter)
{
    ISymbolLoader loader = SymbolLoaderFactory.Create(c, SymbolLoaderSettings.Default);
    var groups = new Dictionary<uint, (int Count, List<IAdsSymbol> Samples)>();

    // filter 以 0x 开头 = 按 IG 过滤；否则按 InstancePath 子串过滤
    uint? igFilter = null;
    if (filter != null && filter.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        igFilter = Convert.ToUInt32(filter[2..], 16);
    int sampleCap = igFilter != null ? 500 : 20;

    void Walk(ISymbol s)
    {
        if (s is IAdsSymbol a && a.ByteSize > 0)
        {
            bool match = igFilter != null
                ? a.IndexGroup == igFilter.Value
                : filter == null || s.InstancePath.Contains(filter, StringComparison.OrdinalIgnoreCase);

            if (match)
            {
                if (!groups.TryGetValue(a.IndexGroup, out var g))
                    g = (0, new List<IAdsSymbol>());
                g.Count++;
                if (g.Samples.Count < sampleCap) g.Samples.Add(a);
                groups[a.IndexGroup] = g;
            }
        }

        foreach (ISymbol child in s.SubSymbols)
            Walk(child);
    }

    foreach (ISymbol s in loader.Symbols)
        Walk(s);

    Console.WriteLine($"  匹配符号 {groups.Values.Sum(g => g.Count)} 个，按 IndexGroup 分组：");
    foreach (var kv in groups.OrderBy(k => k.Key))
    {
        Console.WriteLine();
        Console.WriteLine($"  IG = 0x{kv.Key:X}   数量 = {kv.Value.Count}（下面最多列 {sampleCap} 个）");
        foreach (IAdsSymbol a in kv.Value.Samples)
            Console.WriteLine($"    {a.InstancePath,-60} IO={a.IndexOffset,-10} size={a.ByteSize,-6} {a.TypeName}");
    }

    return 0;
}

// ---------------- helpers ----------------
static string Fmt(object? v) => v switch
{
    null => "<null>",
    byte[] b => $"byte[{b.Length}] {Convert.ToHexString(b, 0, Math.Min(16, b.Length))}…",
    string s => $"\"{s}\"",
    _ => v.ToString() ?? "<null>",
};

static uint ParseUInt(string s) => s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
    ? Convert.ToUInt32(s[2..], 16)
    : uint.Parse(s);

static Type? MapType(string? name)
{
    if (string.IsNullOrWhiteSpace(name)) return null;
    string n = name.Trim();
    int paren = n.IndexOf('(');
    if (paren > 0) n = n[..paren].Trim();

    return n.ToUpperInvariant() switch
    {
        "BOOL" or "BIT" => typeof(bool),
        "SINT" => typeof(sbyte),
        "USINT" or "BYTE" => typeof(byte),
        "INT" => typeof(short),
        "UINT" or "WORD" => typeof(ushort),
        "DINT" => typeof(int),
        "UDINT" or "DWORD" => typeof(uint),
        "LINT" => typeof(long),
        "ULINT" or "LWORD" => typeof(ulong),
        "REAL" => typeof(float),
        "LREAL" => typeof(double),
        "STRING" or "WSTRING" => typeof(string),
        _ => null,
    };
}
