using System;
using System.Windows.Forms;
using PolygonEditor.UI; // lub GK1_Polygons.UI – zale¿nie od u¿ytego namespace

namespace GK1_Polygons
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm()); // lub new Main_form(), jeœli zmieni³aœ nazwê klasy
        }
    }
}