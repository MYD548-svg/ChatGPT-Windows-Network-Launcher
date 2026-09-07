using System;
using ChatGPTAntiBanLauncher;

public class TestProbe
{
    public static void Main()
    {
        ProxyConfig p = new ProxyConfig("127.0.0.1", 7897);
        int token = NetworkProbeService.NextGeneration();
        ProbeResult res = NetworkProbeService.ProbeNode(p, true, token, 5000);
        Console.WriteLine("Success: " + res.Success);
        Console.WriteLine("IP: " + res.Ip);
        Console.WriteLine("Country: " + res.Country);
        Console.WriteLine("Timezone: " + res.IanaTimezone);
        Console.WriteLine("Offset: " + TimezoneHelper.FormatOffset(res.UtcOffset));
        Console.WriteLine("ErrorKind: " + res.ErrorKind);
        Console.WriteLine("ErrorMsg: " + res.ErrorMessage);
    }
}
