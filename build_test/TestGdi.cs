using System;
using System.Drawing;
using System.Drawing.Drawing2D;

public class TestGdi
{
    public static void Main()
    {
        // Test case 1: Normal
        Test(new Rectangle(0, 0, 100, 100), 8);
        
        // Test case 2: width = 0
        Test(new Rectangle(0, 0, 0, 50), 8);
        
        // Test case 3: width = -1
        Test(new Rectangle(0, 0, -1, -1), 8);
        
        // Test case 4: width = 10, radius = 8 (d = 16 > width)
        Test(new Rectangle(0, 0, 10, 50), 8);
        
        // Test case 5: width = 1, radius = 8
        Test(new Rectangle(0, 0, 1, 1), 8);

        // Test case 6: width = 100, radius = 0
        Test(new Rectangle(0, 0, 100, 100), 0);

        // Test case 7: radius negative
        Test(new Rectangle(0, 0, 100, 100), -5);
    }

    static void Test(Rectangle rect, int radius)
    {
        try
        {
            GraphicsPath path = new GraphicsPath();
            if (radius <= 0)
            {
                path.AddRectangle(rect);
                Console.WriteLine(string.Format("Radius <= 0: Rect={0} OK", rect));
                return;
            }

            int d = radius * 2;
            if (d > rect.Width) d = rect.Width;
            if (d > rect.Height) d = rect.Height;

            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            Console.WriteLine(string.Format("Rect={0}, Radius={1} -> OK (d={2})", rect, radius, d));
        }
        catch (Exception ex)
        {
            Console.WriteLine(string.Format("FAILED: Rect={0}, Radius={1} -> {2}: {3}", rect, radius, ex.GetType().Name, ex.Message));
        }
    }
}
