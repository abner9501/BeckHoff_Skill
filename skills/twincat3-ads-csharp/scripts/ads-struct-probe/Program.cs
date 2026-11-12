using AdsShared;
using TwinCAT.Ads;

// 结构体（UDT）探针 —— 薄封装，实际逻辑在 ..\ads-shared\StructProbe.cs
//
//   dotnet run -- [符号名] [--notify|--write|--mem|--arr]
//   dotnet run -- [符号名] [--netId=x.x.x.x.x.x] [--port=851]
//
// 不带符号名 = 自动列出所有结构体实例并读第一个。

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

return StructProbe.Run(client, positional.FirstOrDefault(), flags);
