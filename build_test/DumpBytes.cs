using System;
using System.IO;

class Program
{
    static void Main()
    {
        string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChatGPTAntiBanLauncher", "settings.json");
        byte[] b = File.ReadAllBytes(p);
        Console.WriteLine("Length: " + b.Length);
        for (int i = 0; i < Math.Min(10, b.Length); i++)
        {
            Console.Write("{0:X2} ", b[i]);
        }
        Console.WriteLine();
    }
}
