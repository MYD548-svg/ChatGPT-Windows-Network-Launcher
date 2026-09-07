using System;
using System.Reflection;
using System.Windows.Forms;

public class Disasm
{
    public static void Main()
    {
        MethodInfo m = typeof(Control).GetMethod("PaintTransparentBackground", BindingFlags.Instance | BindingFlags.NonPublic, null, new Type[] { typeof(PaintEventArgs), typeof(System.Drawing.Rectangle), typeof(System.Drawing.Region) }, null);
        if (m != null)
        {
            var body = m.GetMethodBody();
            Console.WriteLine("PaintTransparentBackground IL length: " + (body != null ? body.GetILAsByteArray().Length : 0));
        }

        MethodInfo m2 = typeof(ScrollableControl).GetMethod("OnPaintBackground", BindingFlags.Instance | BindingFlags.NonPublic, null, new Type[] { typeof(PaintEventArgs) }, null);
        if (m2 != null)
        {
            var body2 = m2.GetMethodBody();
            Console.WriteLine("ScrollableControl.OnPaintBackground IL length: " + (body2 != null ? body2.GetILAsByteArray().Length : 0));
        }
    }
}
