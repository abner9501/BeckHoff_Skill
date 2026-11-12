using System.Runtime.InteropServices;
using System.Text;
using TwinCAT;
using TwinCAT.Ads;
using TwinCAT.Ads.TypeSystem;
using TwinCAT.TypeSystem;

namespace AdsShared;

/// <summary>
/// 结构体（UDT）探针：符号表递归、句柄整块读、C# 布局、整块写、Ex 通知、内存寻址、数组套结构体。
/// 由 ads-speedtest 的 struct 模式和 ads-struct-probe 共用。
/// </summary>
public static class StructProbe
{
    public static int Run(AdsClient plc, string? wanted, ISet<string> flags)
    {
        bool optNotify = Has(flags, "--notify");
        bool optWrite = Has(flags, "--write");
        bool optMem = Has(flags, "--mem");
        bool optArr = Has(flags, "--arr");
        string arrSymbol = GetOption(flags, "--arr-symbol") ?? "MAIN.arrSt";
        string arraySymbol = GetOption(flags, "--array-symbol") ?? "GVL_AdsDemo.aInts";

        Console.WriteLine($"连接 {plc.Address}  IsConnected={plc.IsConnected}");
        Console.WriteLine();

        // ---------- 1. 加载符号表，找结构体实例 ----------
        // 注意：loader.Symbols 只给顶层（GVL / MAIN 这些容器本身 Category=Struct、ByteSize=0），
        //       真正的结构体实例要递归 SubSymbols 才找得到。
        ISymbolLoader loader = SymbolLoaderFactory.Create(plc, SymbolLoaderSettings.Default);
        ISymbolCollection<ISymbol> symbols = loader.Symbols;
        Console.WriteLine($"[1] 顶层符号 {symbols.Count} 个（容器本身不算，继续递归 SubSymbols）");

        var structSyms = new List<ISymbol>();
        void Walk(ISymbol s)
        {
            if (s.Category == DataTypeCategory.Struct && s.ByteSize > 0)
                structSyms.Add(s);
            foreach (ISymbol c in s.SubSymbols)
                Walk(c);
        }
        foreach (ISymbol s in symbols)
            Walk(s);

        Console.WriteLine($"    递归后找到结构体实例 {structSyms.Count} 个：");
        foreach (ISymbol s in structSyms)
            Console.WriteLine($"      {s.InstancePath} : {s.TypeName}  ByteSize={s.ByteSize}");

        if (structSyms.Count == 0)
        {
            Console.WriteLine("    → 运行中的 PLC 里没有结构体实例。改完 PLC 工程要【编译 + 下载 + 进 Run】后才会出现在符号表里。");
            return 1;
        }

        // ---------- 2. 选中目标并打印元信息 ----------
        ISymbol target = wanted == null
            ? structSyms[0]
            : structSyms.FirstOrDefault(s => s.InstancePath.Equals(wanted, StringComparison.OrdinalIgnoreCase))
              ?? structSyms.FirstOrDefault(s => s.InstancePath.EndsWith("." + wanted, StringComparison.OrdinalIgnoreCase))
              ?? throw new InvalidOperationException($"符号表里找不到结构体 {wanted}");

        IAdsSymbol sym = plc.ReadSymbol(target.InstancePath);

        Console.WriteLine();
        Console.WriteLine($"[2] 目标 {sym.InstancePath}");
        Console.WriteLine($"    TypeName    = {sym.TypeName}");
        Console.WriteLine($"    Category    = {sym.Category}");
        Console.WriteLine($"    BitSize     = {sym.BitSize}   ByteSize = {sym.ByteSize}");
        Console.WriteLine($"    IsBitType   = {sym.IsBitType}   IsByteAligned = {sym.IsByteAligned}");
        Console.WriteLine($"    IndexGroup  = 0x{sym.IndexGroup:X}   IndexOffset = {sym.IndexOffset}");
        Console.WriteLine($"    成员 {sym.SubSymbols.Count} 个（声明顺序）：");

        int sumBytes = 0;
        foreach (ISymbol sub in sym.SubSymbols)
        {
            string addr = sub is IAdsSymbol sa ? $"IG=0x{sa.IndexGroup:X} IO={sa.IndexOffset}" : "";
            Console.WriteLine($"      {sub.InstanceName,-16} {sub.TypeName,-14} BitSize={sub.BitSize,4} ByteSize={sub.ByteSize,4}  {addr}");
            sumBytes += sub.ByteSize;
        }
        Console.WriteLine($"    成员 ByteSize 合计 = {sumBytes}，结构体 ByteSize = {sym.ByteSize}（差值=对齐填充）");

        // ---------- 3. 句柄读取 ----------
        Console.WriteLine();
        Console.WriteLine("[3] 建句柄 → 按句柄读整块");
        uint handle = 0;
        try
        {
            handle = plc.CreateVariableHandle(sym.InstancePath);
            Console.WriteLine($"    CreateVariableHandle({sym.InstancePath}) = 0x{handle:X}（{handle}）");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    ✗ CreateVariableHandle 失败：{ex.GetType().Name}: {ex.Message}");
        }

        if (handle != 0)
        {
            try
            {
                byte[] buf = new byte[sym.ByteSize];
                int n = plc.Read(0xF005, handle, buf.AsMemory());
                Console.WriteLine($"    Read(0xF005, handle, {buf.Length}B) 返回 {n} 字节");
                Console.WriteLine($"    原始字节：{Convert.ToHexString(buf)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    ✗ 句柄读失败：{ex.GetType().Name}: {ex.Message}");
            }

            // ReadAny<byte[]> 已知会失败，这里留证据
            try
            {
                var arr = plc.ReadAny<byte[]>(handle);
                Console.WriteLine($"    ReadAny<byte[]>(handle) 成功，长度 {arr?.Length}（预期是失败的）");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    ReadAny<byte[]>(handle) 抛：{ex.GetType().Name}: {ex.Message}");
            }

            plc.DeleteVariableHandle(handle);
        }

        // 句柄建不出来的兜底：按符号名读 byte[]
        try
        {
            var raw = plc.ReadValue(sym.InstancePath, typeof(byte[]));
            Console.WriteLine($"    ReadValue(path, typeof(byte[])) 成功，长度 {(raw as byte[])?.Length}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    ReadValue(path, typeof(byte[])) 抛：{ex.GetType().Name}: {ex.Message}");
        }

        // ---------- 4. 逐成员按符号名读（对照组） ----------
        Console.WriteLine();
        Console.WriteLine("[4] 逐成员按符号名读（对照组）");
        foreach (ISymbol sub in sym.SubSymbols)
        {
            string path = sub.InstancePath;
            try
            {
                object? v = sub is IValueSymbol vs ? vs.ReadValue() : plc.ReadValue(path, typeof(object));
                string shown = v switch
                {
                    null => "<null>",
                    byte[] bytes => $"0x{Convert.ToHexString(bytes)}（{bytes.Length} 字节）",
                    _ => v.ToString() ?? "<null>",
                };
                Console.WriteLine($"    {path,-40} = {shown}   [{v?.GetType().Name}]");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    {path,-40} ✗ {ex.GetType().Name}: {ex.Message}");
            }
        }

        // ---------- 5. 按 C# 结构体接住整块（ReadAny<T>） ----------
        bool isTestStruct = sym.TypeName.Equals("stAxisInput", StringComparison.OrdinalIgnoreCase);
        Console.WriteLine();
        Console.WriteLine("[5] ReadAny<T> 用 C# 结构体接住");

        if (isTestStruct)
        {
            Console.WriteLine($"    Marshal.SizeOf<StAxisInput>() = {Marshal.SizeOf<StAxisInput>()}  （PLC 侧 {sym.ByteSize}）");
            uint h2 = plc.CreateVariableHandle(sym.InstancePath);
            try
            {
                StAxisInput v = plc.ReadAny<StAxisInput>(h2);
                Console.WriteLine($"    ReadAny<StAxisInput>(handle) 成功");
                Console.WriteLine($"      lSetPos1={v.lSetPos1}  lSetPos2={v.lSetPos2}  dSelectMode={v.dSelectMode}  iCycleNumber={v.iCycleNumber}");
                Console.WriteLine($"      lHomeOffset={v.lHomeOffset}  bWavebusy={v.bWavebusy}  lLimit={v.lLimit}");
                Console.WriteLine($"      arrString       = {string.Join(" | ", SplitFixed(v.arrString, 81))}");
                Console.WriteLine($"      arrAxisModeString = {string.Join(" | ", SplitWide(v.arrAxisModeString, 162))}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    ✗ ReadAny<StAxisInput> 抛：{ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                plc.DeleteVariableHandle(h2);
            }
        }
        else
        {
            Console.WriteLine("    目标不是 stAxisInput，跳过（需要按符号表逐成员生成 C# 布局）");
        }

        // 结构体符号自身 ReadValue() 返回什么（符号路线）
        try
        {
            object? v = ((IValueSymbol)sym).ReadValue();
            Console.WriteLine($"    IValueSymbol.ReadValue() 返回 {v?.GetType().Name ?? "<null>"}" +
                              (v is byte[] b ? $"，{b.Length} 字节" : ""));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    IValueSymbol.ReadValue() 抛：{ex.GetType().Name}: {ex.Message}");
        }

        // ---------- 6. Ex 通知订阅整个结构体（--notify） ----------
        if (optNotify && isTestStruct)
        {
            Console.WriteLine();
            Console.WriteLine("[6] AddDeviceNotificationEx 订阅整个结构体（typeof(StAxisInput)）");

            int count = 0;
            void OnEx(object? sender, AdsNotificationExEventArgs e)
            {
                count++;
                string desc = e.Value switch
                {
                    StAxisInput v => $"lSetPos1={v.lSetPos1} lSetPos2={v.lSetPos2} dSelectMode={v.dSelectMode} bWavebusy={v.bWavebusy}",
                    byte[] raw => $"byte[{raw.Length}] {Convert.ToHexString(raw, 0, Math.Min(16, raw.Length))}…",
                    null => "<null>",
                    _ => e.Value.GetType().Name,
                };
                Console.WriteLine($"    通知#{count}  handle={e.Handle}  value=({desc})");
            }

            plc.AdsNotificationEx += OnEx;

            var settings = new NotificationSettings(AdsTransMode.OnChange, 0, 0);

            // 6.1 用 C# 结构体类型注册
            uint nh = 0;
            try
            {
                nh = plc.AddDeviceNotificationEx(sym.InstancePath, settings, "ST1", typeof(StAxisInput));
                Console.WriteLine($"    AddDeviceNotificationEx(MAIN.ST1, …, typeof(StAxisInput)) = 0x{nh:X}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    ✗ Ex 注册（C# 结构体类型）失败：{ex.GetType().Name}: {ex.Message}");
            }

            if (nh != 0)
            {
                Console.WriteLine("    改两个成员的值，看通知里拿到什么（800ms 一拍）…");
                plc.WriteValue(sym.InstancePath + ".lSetPos1", 12.34);
                Thread.Sleep(800);
                plc.WriteValue(sym.InstancePath + ".bWavebusy", true);
                Thread.Sleep(800);

                // 还原，避免影响后续测试
                plc.WriteValue(sym.InstancePath + ".lSetPos1", 10.0);
                plc.WriteValue(sym.InstancePath + ".bWavebusy", false);
                Thread.Sleep(800);

                Console.WriteLine($"    → 共收到 {count} 条通知");
                plc.DeleteDeviceNotification(nh);
            }

            // 6.2 对照：byte[] 不给长度
            try
            {
                uint nh2 = plc.AddDeviceNotificationEx(sym.InstancePath, settings, "raw", typeof(byte[]));
                Console.WriteLine($"    Ex 注册 typeof(byte[])（不给 args）居然成功，handle=0x{nh2:X}");
                plc.DeleteDeviceNotification(nh2);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    Ex 注册 typeof(byte[])（不给 args）抛：{ex.GetType().Name}: {ex.Message}");
            }

            // 6.3 对照：byte[] + args[0] = 字节数
            uint nhRaw = 0;
            try
            {
                nhRaw = plc.AddDeviceNotificationEx(sym.InstancePath, settings, "raw", typeof(byte[]), new[] { sym.ByteSize });
                Console.WriteLine($"    Ex 注册 typeof(byte[]) + args[0]={sym.ByteSize} 成功，handle=0x{nhRaw:X}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    Ex 注册 typeof(byte[]) + args 抛：{ex.GetType().Name}: {ex.Message}");
            }

            if (nhRaw != 0)
            {
                int before = count;
                plc.WriteValue(sym.InstancePath + ".lSetPos1", 23.45);
                Thread.Sleep(800);
                plc.WriteValue(sym.InstancePath + ".lSetPos1", 10.0);
                Thread.Sleep(800);
                Console.WriteLine($"    → byte[] 通知本轮 {count - before} 条");
                plc.DeleteDeviceNotification(nhRaw);
            }

            // 6.4 直接用 IG/IO 注册（完全绕开符号名/变量句柄）
            try
            {
                uint nhAddr = plc.AddDeviceNotificationEx(sym.IndexGroup, sym.IndexOffset, settings, "addr", typeof(StAxisInput));
                Console.WriteLine($"    Ex 用 IG/IO 注册（0x{sym.IndexGroup:X}/{sym.IndexOffset}）= 0x{nhAddr:X}");
                int before = count;
                plc.WriteValue(sym.InstancePath + ".lSetPos2", 66.6);
                Thread.Sleep(800);
                plc.WriteValue(sym.InstancePath + ".lSetPos2", 100.0);
                Thread.Sleep(800);
                Console.WriteLine($"    → 地址注册本轮 {count - before} 条");
                plc.DeleteDeviceNotification(nhAddr);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    Ex 用 IG/IO 注册抛：{ex.GetType().Name}: {ex.Message}");
            }

            plc.AdsNotificationEx -= OnEx;
        }

        // ---------- 7. 整块写（--write） ----------
        if (optWrite && isTestStruct)
        {
            Console.WriteLine();
            Console.WriteLine("[7] 整块写 WriteAny<StAxisInput>");

            uint hw = plc.CreateVariableHandle(sym.InstancePath);
            StAxisInput orig = plc.ReadAny<StAxisInput>(hw);
            Console.WriteLine($"    写前快照：lSetPos1={orig.lSetPos1} lSetPos2={orig.lSetPos2} " +
                              $"dSelectMode={orig.dSelectMode} bWavebusy={orig.bWavebusy}");

            // 7.1 改几个字段 → 整块写 → 回读比对
            StAxisInput v = orig;
            v.lSetPos1 = 111.11;
            v.lSetPos2 = 222.22;
            v.dSelectMode = 2;
            v.bWavebusy = 1;
            plc.WriteAny(hw, v);

            StAxisInput back1 = plc.ReadAny<StAxisInput>(hw);
            Console.WriteLine($"    7.1 整块写后回读：lSetPos1={back1.lSetPos1} lSetPos2={back1.lSetPos2} " +
                              $"dSelectMode={back1.dSelectMode} bWavebusy={back1.bWavebusy}");
            Console.WriteLine($"        arrString = {string.Join(" | ", SplitFixed(back1.arrString, 81))}");

            // 7.2 只改一个成员 → 再用「旧快照」整块写回 → 看那个成员会不会被覆盖
            plc.WriteValue(sym.InstancePath + ".lSetPos1", 333.33);
            StAxisInput afterMember = plc.ReadAny<StAxisInput>(hw);
            Console.WriteLine($"    7.2 只写成员后：lSetPos1={afterMember.lSetPos1}");

            plc.WriteAny(hw, v);   // v 仍是快照里的 111.11
            StAxisInput afterWhole = plc.ReadAny<StAxisInput>(hw);
            Console.WriteLine($"        再用旧快照整块写回：lSetPos1={afterWhole.lSetPos1} " +
                              $"（=111.11 说明整块写把 333.33 覆盖了）");

            // 7.3 写结构体里的数组元素（按符号名，逐元素）
            try
            {
                plc.WriteValue(sym.InstancePath + ".arrString[1]", "0.9s");
                plc.WriteValue(sym.InstancePath + ".arrAxisModeString[2]", "维护模式");
                StAxisInput afterArr = plc.ReadAny<StAxisInput>(hw);
                Console.WriteLine($"    7.3 写数组元素后：arrString[1]={string.Join(" | ", SplitFixed(afterArr.arrString, 81))}");
                Console.WriteLine($"        arrAxisModeString = {string.Join(" | ", SplitWide(afterArr.arrAxisModeString, 162))}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    7.3 写数组元素抛：{ex.GetType().Name}: {ex.Message}");
            }

            // 7.4 整个结构体还原
            plc.WriteAny(hw, orig);
            StAxisInput restored = plc.ReadAny<StAxisInput>(hw);
            bool ok = restored.lSetPos1 == orig.lSetPos1 && restored.lSetPos2 == orig.lSetPos2
                      && restored.dSelectMode == orig.dSelectMode && restored.bWavebusy == orig.bWavebusy
                      && string.Join("|", SplitFixed(restored.arrString, 81)) == "0.1s|0.3s|0.5s|0.8s"
                      && string.Join("|", SplitWide(restored.arrAxisModeString, 162)) == "单轴模式|群控模式";
            Console.WriteLine($"    7.4 还原 {(ok ? "成功（与原始快照一致）" : "失败（有成员没还原）")}");

            plc.DeleteVariableHandle(hw);
        }

        // ---------- 8. 内存寻址 vs 句柄/符号（--mem） ----------
        if (optMem)
        {
            Console.WriteLine();
            Console.WriteLine("[8] 内存寻址（IndexGroup/IndexOffset）对比");

            byte[] viaMem = new byte[sym.ByteSize];
            plc.Read(sym.IndexGroup, sym.IndexOffset, viaMem.AsMemory());

            uint hm = plc.CreateVariableHandle(sym.InstancePath);
            byte[] viaHandle = new byte[sym.ByteSize];
            plc.Read(0xF005, hm, viaHandle.AsMemory());
            plc.DeleteVariableHandle(hm);

            Console.WriteLine($"    结构体 内存读 {sym.IndexGroup:X}/{sym.IndexOffset} = {viaMem.Length}B，" +
                              $"句柄读 {viaHandle.Length}B，逐字节相同 = {viaMem.AsSpan().SequenceEqual(viaHandle)}");

            // 第一个成员：内存直接读 vs 按符号名读
            if (sym.SubSymbols.Count > 0 && sym.SubSymbols[0] is IAdsSymbol sub0 && sub0.ByteSize == 8)
            {
                double byMem = plc.ReadAny<double>(sub0.IndexGroup, sub0.IndexOffset);
                double byName = (double)plc.ReadValue(sub0.InstancePath, typeof(double))!;
                Console.WriteLine($"    {sub0.InstancePath}: 内存={byMem}  符号={byName}  相等={byMem == byName}");
            }

            // 普通数组：父符号地址 + 元素地址是否等距
            try
            {
                IAdsSymbol arr = plc.ReadSymbol(arraySymbol);
                Console.WriteLine($"    数组 {arr.InstancePath}: {arr.TypeName}  ByteSize={arr.ByteSize}  " +
                                  $"IG=0x{arr.IndexGroup:X} IO={arr.IndexOffset}");
                Console.WriteLine($"      子符号 {arr.SubSymbols.Count} 个：");
                foreach (IAdsSymbol e in arr.SubSymbols)
                    Console.WriteLine($"        {e.InstanceName,-10} {e.TypeName,-10} IG=0x{e.IndexGroup:X} IO={e.IndexOffset}  " +
                                      $"相对={e.IndexOffset - arr.IndexOffset}");

                byte[] arrMem = new byte[arr.ByteSize];
                plc.Read(arr.IndexGroup, arr.IndexOffset, arrMem.AsMemory());
                byte[] arrSym = (byte[])plc.ReadValue(arr.InstancePath, typeof(byte[]))!;
                Console.WriteLine($"      内存读 {arrMem.Length}B，符号读 {arrSym.Length}B，逐字节相同 = " +
                                  $"{arrMem.AsSpan().SequenceEqual(arrSym)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"      数组对照跳过：{ex.GetType().Name}: {ex.Message}");
            }
        }

        // ---------- 9. 数组元素是结构体（--arr） ----------
        if (optArr)
        {
            Console.WriteLine();
            Console.WriteLine("[9] 数组元素是结构体：ARRAY OF stAxisInput");

            try
            {
                IAdsSymbol arrS = plc.ReadSymbol(arrSymbol);
                Console.WriteLine($"    {arrS.InstancePath}: {arrS.TypeName}  ByteSize={arrS.ByteSize}  " +
                                  $"IG=0x{arrS.IndexGroup:X} IO={arrS.IndexOffset}  Category={arrS.Category}");
                Console.WriteLine($"    元素 {arrS.SubSymbols.Count} 个：");
                foreach (IAdsSymbol e in arrS.SubSymbols)
                    Console.WriteLine($"      {e.InstanceName,-10} {e.TypeName,-14} ByteSize={e.ByteSize,4} " +
                                      $"IG=0x{e.IndexGroup:X} IO={e.IndexOffset}  相对={e.IndexOffset - arrS.IndexOffset}  {e.Category}");

                // 整块内存读 vs 每个元素的句柄读
                byte[] whole = new byte[arrS.ByteSize];
                plc.Read(arrS.IndexGroup, arrS.IndexOffset, whole.AsMemory());
                Console.WriteLine($"    内存整块读 {whole.Length}B");
                foreach (IAdsSymbol e in arrS.SubSymbols)
                {
                    uint eh = plc.CreateVariableHandle(e.InstancePath);
                    byte[] eb = new byte[e.ByteSize];
                    plc.Read(0xF005, eh, eb.AsMemory());
                    int rel = (int)(e.IndexOffset - arrS.IndexOffset);
                    Console.WriteLine($"      {e.InstancePath}: 句柄读 {eb.Length}B，与整块对应切片逐字节相同 = " +
                                      $"{whole.AsSpan(rel, e.ByteSize).SequenceEqual(eb)}");
                    plc.DeleteVariableHandle(eh);
                }

                // ReadAny<StAxisInput[]> 能不能用
                uint ah = plc.CreateVariableHandle(arrS.InstancePath);
                try
                {
                    StAxisInput[] arr = plc.ReadAny<StAxisInput[]>(ah);
                    Console.WriteLine($"    ReadAny<StAxisInput[]> 成功，长度 {arr.Length}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"    ReadAny<StAxisInput[]> 抛：{ex.GetType().Name}: {ex.Message}");
                }
                plc.DeleteVariableHandle(ah);

                // 单个元素整块读写：句柄能建在“数组元素”上吗
                string el2 = arrS.InstancePath + "[2]";
                uint h2 = 0;
                StAxisInput orig2 = default;
                try
                {
                    h2 = plc.CreateVariableHandle(el2);
                    Console.WriteLine($"    CreateVariableHandle({el2}) = 0x{h2:X}");
                    orig2 = plc.ReadAny<StAxisInput>(h2);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"    元素句柄失败：{ex.GetType().Name}: {ex.Message}");
                }

                if (h2 != 0)
                {
                    double v1Before = (double)plc.ReadValue(arrS.InstancePath + "[1].lSetPos1", typeof(double))!;
                    double v3Before = (double)plc.ReadValue(arrS.InstancePath + "[3].lSetPos1", typeof(double))!;

                    // 9.1 只写元素 2 的一个成员
                    plc.WriteValue(el2 + ".lSetPos1", 55.5);
                    double v2 = (double)plc.ReadValue(el2 + ".lSetPos1", typeof(double))!;
                    double v1After = (double)plc.ReadValue(arrS.InstancePath + "[1].lSetPos1", typeof(double))!;
                    double v3After = (double)plc.ReadValue(arrS.InstancePath + "[3].lSetPos1", typeof(double))!;
                    Console.WriteLine($"    9.1 写元素2.lSetPos1=55.5 → 读回={v2}；邻居 [{v1Before}→{v1After}] [{v3Before}→{v3After}]");

                    // 9.2 整块写元素 2
                    StAxisInput mod = orig2;
                    mod.lSetPos2 = 777.7;
                    mod.dSelectMode = 9;
                    mod.bWavebusy = 1;
                    plc.WriteAny(h2, mod);
                    StAxisInput back2 = plc.ReadAny<StAxisInput>(h2);
                    double v1After2 = (double)plc.ReadValue(arrS.InstancePath + "[1].lSetPos1", typeof(double))!;
                    double v3After2 = (double)plc.ReadValue(arrS.InstancePath + "[3].lSetPos1", typeof(double))!;
                    Console.WriteLine($"    9.2 整块写元素2：lSetPos1={back2.lSetPos1} lSetPos2={back2.lSetPos2} " +
                                      $"dSelectMode={back2.dSelectMode} bWavebusy={back2.bWavebusy}");
                    Console.WriteLine($"        邻居 lSetPos1：[{v1After2}] [{v3After2}]（应仍等于原值）");

                    // 9.3 还原元素 2
                    plc.WriteAny(h2, orig2);
                    StAxisInput rest2 = plc.ReadAny<StAxisInput>(h2);
                    Console.WriteLine($"    9.3 还原元素2：lSetPos1={rest2.lSetPos1} lSetPos2={rest2.lSetPos2} " +
                                      $"dSelectMode={rest2.dSelectMode} bWavebusy={rest2.bWavebusy}");
                    plc.DeleteVariableHandle(h2);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    ✗ 数组套结构体测试失败：{ex.GetType().Name}: {ex.Message}");
            }
        }

        return 0;
    }

    private static bool Has(ISet<string> flags, string name) =>
        flags.Contains(name) || flags.Contains(name.TrimStart('-'));

    private static string? GetOption(ISet<string> flags, string name)
    {
        foreach (string f in flags)
            if (f.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                return f[(name.Length + 1)..];
        return null;
    }

    private static IEnumerable<string> SplitFixed(byte[] raw, int slotSize)
    {
        for (int i = 0; i + slotSize <= raw.Length; i += slotSize)
        {
            int n = Array.IndexOf(raw, (byte)0, i, slotSize);
            if (n < 0) n = i + slotSize;
            yield return Encoding.ASCII.GetString(raw, i, n - i);
        }
    }

    private static IEnumerable<string> SplitWide(byte[] raw, int slotSize)
    {
        for (int i = 0; i + slotSize <= raw.Length; i += slotSize)
        {
            int end = i + slotSize;
            int n = end;
            for (int p = i; p + 1 < end; p += 2)
            {
                if (raw[p] == 0 && raw[p + 1] == 0) { n = p; break; }
            }
            yield return Encoding.Unicode.GetString(raw, i, n - i);
        }
    }

    // 按 PLC 符号表给出的偏移手写（不是猜的）：
    //   LREAL=8 / DINT=4 / INT=2 / BOOL=1 / STRING(80)=81B / WSTRING(80)=162B
    // TwinCAT 按自然对齐排布（LREAL 8 字节对齐），所以这里用 Sequential + 默认 Pack=8；
    // 只有 arrAxisModeString 前那 1 字节对齐填充需要手写补（offset 485）。
    // ⚠ 不能用 LayoutKind.Explicit：结构体里带 byte[] 引用字段时 CLR 直接 TypeLoadException。
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct StAxisInput
    {
        public double lSetPos1;        // 0
        public double lSetPos2;        // 8
        public double lSetVel;         // 16
        public double lIntervalTime;   // 24
        public double lJogVel;         // 32
        public double lJogInch;        // 40
        public int dSelectMode;        // 48
        public byte bLimitSenser;      // 52
        public double lMaxVel;         // 56（CLR 自动补 3 字节）
        public double lMaxAcc;         // 64
        public double lMaxLimit;       // 72
        public double LminLimit;       // 80
        public double lSetAcc;         // 88
        public double lSetDeacc;       // 96
        public double lSetJerk;        // 104
        public short iCycleNumber;     // 112
        public byte Enable;            // 114
        public byte bHome;             // 115
        public byte bGoHome;           // 116
        public byte bGoFirstPos;       // 117
        public byte bMoveAbs;          // 118
        public byte bSetPos;           // 119
        public byte bStop;             // 120
        public byte bJogForwards;      // 121
        public byte bJogbackwards;     // 122
        public double lHomeOffset;     // 128（CLR 自动补 5 字节）
        public double lHomeTeach;      // 136
        public byte bReversingStart;   // 144
        public double lWorkTime;       // 152（CLR 自动补 7 字节）
        public byte bReset;            // 160
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 324)] public byte[] arrString;   // 161
        public byte alignPad;          // 485（WSTRING 数组要 2 字节对齐，PLC 侧有这 1 字节）
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 324)] public byte[] arrAxisModeString; // 486
        public byte bWavebusy;         // 810
        public byte bWaveDone;         // 811
        public byte rLimit;            // 812
        public byte lLimit;            // 813
        // 814-815 = 末尾对齐填充，由 Pack=8 自动补齐（总大小 816）
    }
}
