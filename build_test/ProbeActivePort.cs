using System;

namespace ChatGPTAntiBanLauncher.Tests
{
    public class ProbeActivePort
    {
        public static void Main()
        {
            Console.WriteLine("Testing DetectActiveProxyPort...");
            int port = ProxyService.DetectActiveProxyPort(0);
            Console.WriteLine("Active port detected: " + port);
            bool listening = ProxyService.IsPortListening("127.0.0.1", port, 200);
            Console.WriteLine("Is listening: " + listening);
        }
    }
}
