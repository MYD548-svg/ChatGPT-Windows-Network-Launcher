using System;
using System.IO;
using System.Runtime.Serialization.Json;
using ChatGPTAntiBanLauncher;

class Program
{
    static void Main()
    {
        string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChatGPTAntiBanLauncher", "settings.json");
        Console.WriteLine("File path: " + p);
        Console.WriteLine("File content: " + File.ReadAllText(p));
        try
        {
            string err;
            LauncherSettings s = SettingsStore.Load(out err);
            Console.WriteLine("Load err: " + err);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Direct ex: " + ex);
        }
    }
}
