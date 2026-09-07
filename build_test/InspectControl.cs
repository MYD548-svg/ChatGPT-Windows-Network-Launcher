using System;
using System.Reflection;
using System.Windows.Forms;

public class InspectControl
{
    public static void Main()
    {
        Type t = typeof(Control);
        MethodInfo[] mis = t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        foreach (var m in mis)
        {
            if (m.Name.Contains("PaintTransparentBackground") || m.Name.Contains("PaintBackground"))
            {
                Console.WriteLine(m.ToString());
            }
        }
    }
}
