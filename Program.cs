using KenshiCore;
using KenshiCore.OgreEngineering;
using KenshiCore.Utilities;
using KenshiFixer.Forms;
using ScintillaNET;
using static KenshiCore.OgreEngineering.SkeletonEngineer;

namespace KenshiFixer;
static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}