using KenshiCore.Mods;
using KenshiCore.ReverseEngineering;
using KenshiCore.UI;
using KenshiCore.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KenshiFixer.Forms
{
    using KenshiCore;
    using KenshiFixer.Fixers;
    using ScintillaNET;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Drawing;
    using System.Drawing.Interop;
    using System.IO;
    using System.Linq;
    using System.Reflection.Metadata;
    using System.Threading.Tasks;
    using System.Windows.Forms;
    using System.Xml.Linq;
    using static ScintillaNET.Style;
    using static System.Windows.Forms.VisualStyles.VisualStyleElement.Window;
    //TODO: meshes and textures should be loaded in pairs, if one is missing action should be taken.
    //TODO: decoupling of multiple files overriding each other in case one file is named exactly as another one, but how to know intention?

    public class MainForm : ProtoMainForm
    {
        //private ReverseEngineerRepository RERepository = ReverseEngineerRepository.Instance;
        //private Dictionary<(string, string), string> can_crash_registry = new();
        //private Dictionary<string,string> fallbacks_sids = new Dictionary<string,string>();
        //private Dictionary<string, ModRecord> replacements = new Dictionary<string, ModRecord>();
        //private ReverseEngineer RE_fixed=new ReverseEngineer();
        private List<string> nocrash_strings;
        //public ReverseEngineer fixEngineer;
        private HashSet<string> broken_paths_mods = new HashSet<string>();
        private const string KenshiFix = "-KenshiFixer_Fix-";

        public MainForm()
        {
            Text = "Kenshi Fixer";
            Width = 800;
            Height = 700;


            ThemeManager.Set(
                new AppTheme
                {
                    Background = Color.FromArgb(unchecked((int)0xFF8A3A3A)),
                    Secondary = Color.FromArgb(unchecked((int)0xFFD4CFC2)),
                    Foreground = Color.FromArgb(unchecked((int)0xFF2A2520))
                });


            this.ForeColor = Color.FromArgb(unchecked((int)0xFF2A2520)); 
            
            AddColumn("Status", mod => getModStatus(mod), 150);
            //AddButton("Diagnose FilePaths", DiagnosePathsClick);
            AddButton("Generate Fix", GenerateFix);
            //AddButton("Regenerate Bridge", GenerateBridge);
            shouldResetLog = false;
            //fixEngineer = new ReverseEngineer();

            

            nocrash_strings = new List<string>
            {
                "has no faction",
                "no faction for homeless squad"
            };
            AddToggle("Show Recent Infos", (mod) => ShowInfos());
            AddToggle("Show Recent Warnings", (mod) => ShowWarnings());
            AddToggle("Show Recent Errors", (mod) => ShowErrors());
            AddToggle("Show Special Errors", (mod) => ShowSpecialErrors());
            AddButton("Sort Mods", SortMods);
        }
        protected override void LoadMods()
        {
            var repo = ModRepository.Instance;

            repo.LoadBaseGameMods();
            repo.LoadGameDirMods();
            repo.LoadWorkshopMods();
            repo.LoadSelectedMods();
            repo.excludeUnselectedMods = true;
        }
        private string searchInKenshiInfoLog(Func<string, bool> condition)
        {
            string path = Path.Combine(ModManager.kenshiPath!, "kenshi_info.log");

            if (!File.Exists(path))
                return string.Empty;

            StringBuilder sb = new StringBuilder();

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            using var reader = new StreamReader(stream);

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (condition(line))
                {
                    sb.AppendLine(line);
                }
            }

            return sb.ToString();
        }
        private void ShowInfos()
        {
            var logform = getLogForm();
            logform.LogString("--- INFORMATION: ---\n", Color.Blue);
            logform.LogString(searchInKenshiInfoLog(s => s.Contains("[info]")), Color.LightBlue);
        }
        private void ShowWarnings()
        {
            var logform = getLogForm();
            logform.LogString("--- WARNINGS: ---\n", Color.Yellow);
            logform.LogString(searchInKenshiInfoLog(s => s.Contains("[warning]")), Color.LightYellow);

        }
        private void ShowErrors()
        {
            var logform = getLogForm();
            logform.LogString("--- ERRORS: ---\n", Color.Red);
            logform.LogString(searchInKenshiInfoLog(s => s.Contains("[error]") && nocrash_strings.Any(n => s.Contains(n))), Color.Orange);
        }
        private void ShowSpecialErrors()
        {
            var logform = getLogForm();
            logform.LogString("--- SPECIAL ERRORS: ---\n", Color.Red);
            logform.LogString(searchInKenshiInfoLog(s => (s.Contains("[error]")&& !nocrash_strings.Any(n => s.Contains(n)))|| s.Contains("[fatal]")), Color.OrangeRed);
        }
        private string getModStatus(ModItem mod)
        {
            if (broken_paths_mods.Contains(mod.Name))
                return "broken_path";
            return "ok";
        }
        public async void GenerateFix(object? sender, EventArgs e)
        {
            await Task.Run(() => GenerateFixAsync());
        }
        private void GenerateFixAsync()
        {
            KenshiFixerGenerator kfixer = new KenshiFixerGenerator();
            kfixer.AddEmergencyFallbacks();
            kfixer.RestoreEmptiedFilenames();
            kfixer.Save();
        }
        private void SortMods(object? sender, EventArgs e)
        {
            var sorter = new LoadOrderSorter(KenshiFix);//, KenshiBridge);
            var modNames = ModRepository.Instance.SelectedMods.ToList();
            sorter.ApplyNewestSort(modNames);
            sorter.ApplySortToCreators(modNames, sorter.ApplyMinimumConflictSort);
            sorter.ApplySortToCreators(modNames, sorter.ApplyDependencyAwareSort);
            sorter.ApplySortToCreators(modNames, sorter.ApplyDirectDependencySort); 
            ModRepository.Instance.SetSelectedMods(modNames);
            PopulateModsListView();
            saveLoadOrder();
        }
        private void saveLoadOrder()
        {
            string loadorderdir = Path.Combine(ModManager.kenshiPath!, "data");
            string backuppath = Path.Combine(loadorderdir, $"mods_backup.txt");
            string loadorderpath = Path.Combine(loadorderdir, $"mods.cfg");
            if (!File.Exists(backuppath))
            {
                File.Copy(loadorderpath, backuppath);
            }
            File.WriteAllLines(loadorderpath, ModRepository.Instance.SelectedMods.ToList());
            UiService.ShowMessage("load order saved!");
        }
        protected override async Task AfterModsLoadedAsync()
        {
            await Task.Run(() =>
                ReverseEngineerRepository.Instance.LoadFromMods(
                    mergedMods,
                    CoreUtils.GetRealModPath
                )
            );
        }


        /*public async void DiagnosePathsClick(object? sender, EventArgs e)
        {
            await Task.Run(() => DiagnosePathsClickAsync());
        }
        private void DiagnosePathsClickAsync()
        {
            var logform = getLogForm();
            broken_paths_mods= new HashSet<string>();
            int ocurrences = 0;
            StringBuilder sb= new StringBuilder();

            ProgressController controller = ProgressController.Instance;

            controller.Initialize(mergedMods.Count);
            int i = 0;
            foreach (var kvp in mergedMods)
            {
                if (ModRepository.Instance.BaseGameMods.Contains(kvp.Key))
                    continue;
                string modName = kvp.Key;
                ModItem mod = kvp.Value;
                if (!RERepository.TryGet(modName, out var re) || re == null)
                    continue;
                string? modpath = mod.getModFilePath();
                if (string.IsNullOrEmpty(modpath))
                    continue;

                List<string> brokenForThisMod = new List<string>();
                foreach (ModRecord record in re.modData.Records!)
                {
                    string record_name= record.Name;
                    foreach(string fieldname in record.FilenameFields.Keys)
                    {
                        string filepath = record.FilenameFields[fieldname];
                        if (!string.IsNullOrWhiteSpace(filepath) && !ModFileExists(mod, filepath))
                        {
                            brokenForThisMod.Add($"{record.getRecordType()}|Record:{record_name}|{record.StringId} Missing Path:{fieldname}|{filepath}");
                            ocurrences++;
                        }
                    }
                }
                if (brokenForThisMod.Count > 0)
                {
                    string modname = re.modname;
                    broken_paths_mods.Add(modname);
                    sb.AppendLine("------:"+modname + "("+ brokenForThisMod.Count() + "):------");
                    foreach (var entry in brokenForThisMod)
                        sb.AppendLine(entry);
                }
                i++;
                controller.Report(i, $"{modName} ({i} analyzed)");
            }
            controller.Finish("Diagnosis complete");
            if (logform.InvokeRequired)
            {
                logform.BeginInvoke((Action)(() =>
                {
                    logform.LogString($"-----------BROKEN FILEPATHS({ocurrences}):------------\n", Color.IndianRed);
                    logform.LogString(sb.ToString(), Color.Red);
                    logform.Refresh();
                }));
            }
            else
            {
                logform.LogString($"-----------BROKEN FILEPATHS({ocurrences}):------------\n", Color.IndianRed);
                logform.LogString(sb.ToString(), Color.Red);
                logform.Refresh();
            }
            RefreshColumn(1);

        }*/
        public bool ModFileExists(ModItem mod, string filePath)
        {
            if (mod == null || string.IsNullOrEmpty(filePath))
                return false;
            return !ModRepository.Instance.ResolveRealPath(filePath).StartsWith("E_");
        }
        protected override Color GetModColor(ModItem mod)
        {
            if (mod.Name == KenshiFix+".mod")
                return Color.LightGreen;
            return base.GetModColor(mod);
        }

    }
}
