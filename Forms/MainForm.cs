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
    using KenshiFixer.Mod_Analysis;
    using KenshiFixer.ModAnalysis;
    using ScintillaNET;
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using System.Windows.Forms;
    using System.Xml.Linq;
    using static ScintillaNET.Style;
    using static System.Windows.Forms.VisualStyles.VisualStyleElement.Tab;
    using static System.Windows.Forms.VisualStyles.VisualStyleElement.Window;
    //TODO: meshes and textures should be loaded in pairs, if one is missing action should be taken.
    //TODO: decoupling of multiple files overriding each other in case one file is named exactly as another one, but how to know intention?

    public class MainForm : ProtoMainForm
    {
        private const string KenshiFix = "-KenshiFixer_Fix-";
        private ProblemAnalyzer analyzer;
        public MainForm()
        {
            Text = "Kenshi Fixer";
            Width = 800;
            Height = 700;

            analyzer = new ProblemAnalyzer();
            ThemeManager.Set(
                new AppTheme
                {
                    Background = Color.FromArgb(unchecked((int)0xFF8A3A3A)),
                    Secondary = Color.FromArgb(unchecked((int)0xFFD4CFC2)),
                    Foreground = Color.FromArgb(unchecked((int)0xFF2A2520))
                });


            this.ForeColor = Color.FromArgb(unchecked((int)0xFF2A2520)); 
            
            AddColumn("Status", mod => getModStatus(mod), 150);
            shouldResetLog = false;
            

            AddToggle("Show Type Mismatches","mismatch", (mod) => ShowTypeMismatches(mod),true);
            AddToggle("Show Missing References", "missing_refs", (mod) => ShowMissingReferences(mod), true);
            AddToggle("Show Emptied Filepaths", "emptied_paths", (mod) => ShowEmptiedFilepaths(mod), true);
            AddToggle("Show File Overrides", "file_overrides", (mod) => ShowFileOverrides(mod), true);

            AddButton("Search Problems", SearchProblemsButton_Click);

            AddButton("Generate Fix", GenerateFix);
            AddButton("Reset Fix", ResetFix);
            AddButton("Sort Mods", SortMods);
        }
        private void SearchProblemsButton_Click(object? sender, EventArgs e)
        {
            ReSearchProblems();
            UiService.ShowMessage("Analysis complete.");
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
        private void ShowTypeMismatches(ModItem mod)
        {
            var logform = getLogForm();

            string body_mismatch = analyzer.GetProblemsForMod(mod.Name, p => p is TypeMismatch);

            if (!string.IsNullOrEmpty(body_mismatch))
            {
                logform.LogString("TYPE MISMATCHES:\n", Color.Crimson);

                logform.LogString(body_mismatch, Color.MediumVioletRed);
                logform.LogString(
                    "If two records with the same String ID have different record types, " +
                    "this may cause unexpected behaviour or crashes if the game tries to " +
                    "use the record as a different type than expected.\n For example, the " +
                    "game may expect a Dialogue record but encounter an Animation record instead.\n You should pick one mod or the other, not both\n\n",

                    Color.Gray);
            }
        }

        private void ShowMissingReferences(ModItem mod) {

            var logform = getLogForm();
            string body_missingrefs =
                analyzer.GetProblemsForMod(mod.Name, p => p is MissingReference);

            if (!string.IsNullOrEmpty(body_missingrefs))
            {
                logform.LogString("MISSING REFERENCES:\n", Color.OrangeRed);
                logform.LogString(body_missingrefs, Color.DarkOrange);
                logform.LogString(
                    "These records contain an ExtraData reference to a String ID that does " +
                    "not exist in the current load order.\n If the game tries to use this " +
                    "reference, it may cause unexpected behaviour or a crash.\n\n",
                    Color.Gray);
            }
        }

        private void ShowEmptiedFilepaths(ModItem mod)
        {
            var logform = getLogForm();
            string body_emptiedfilepaths =
                analyzer.GetProblemsForMod(mod.Name, p => p is EmptiedFilename);

            if (!string.IsNullOrEmpty(body_emptiedfilepaths))
            {
                logform.LogString("EMPTIED FILEPATHS:\n", Color.Gold);
                logform.LogString(body_emptiedfilepaths, Color.Goldenrod);
                logform.LogString(
                    "These records have empty file paths where a non-empty path was previously assigned.\n " +
                    "That means animations may break, bodies may be invisible or weird crashes.\n\n",
                    Color.Gray);
            }



        }
        private void ShowFileOverrides(ModItem mod)
        {   
            var logform = getLogForm();
            string body_overridenfiles = analyzer.GetGeneralProblemsForMod(mod.Name,p=>p is FileOverride);
            if (!string.IsNullOrEmpty(body_overridenfiles))
            {
                logform.LogString("OVERRIDEN FILES:\n", Color.Yellow);
                logform.LogString(body_overridenfiles, Color.LightYellow);
                logform.LogString(
                    "Not a gamebreaking issue necesarily, but will override the files shown.\n " +
                    "Choose carefully which mod goes below the other.\n\n",
                    Color.Gray);
            }
        }
        private string getModStatus(ModItem mod)
        {
            //if (broken_paths_mods.Contains(mod.Name))
            //    return "broken_path";
            return "ok";
        }
        public async void GenerateFix(object? sender, EventArgs e)
        {
            await Task.Run(() => GenerateFixAsync());
            await Task.Run(() =>
            {
                analyzer.AnalyzeAll();
                analyzer.findProblems();
            });
            RefreshColors();
        }
        private void GenerateFixAsync()
        {
            KenshiFixerGenerator kfixer = new KenshiFixerGenerator();
            List<Problem>? probs = analyzer.GetProblems();
            if (probs == null)
            {
                UiService.ShowMessage("No problems found. Please run 'Search Problems' first.");
                return;
            }

            kfixer.SolveProblems(probs);
            kfixer.Save();
            ReverseEngineerRepository.Instance.ReloadMod(KenshiFixerGenerator.GetFullPathForFixMod());
        }
        private async void ReSearchProblems()
        {
            await Task.Run(() =>
            {
                analyzer.AnalyzeAll();
                analyzer.findProblems();
            });
            RefreshColors();
        }
        public async void ResetFix(object? sender, EventArgs e)
        {
            await Task.Run(() => ResetFixAsync());
            ReSearchProblems();
            UiService.ShowMessage("KenshiFixer_Fix has been reset");
        }
        private void ResetFixAsync()
        {
            ReverseEngineer RE = new ReverseEngineer(); 
            string fixpath = KenshiFixerGenerator.GetFullPathForFixMod();
            RE.LoadModFile(KenshiFixerGenerator.GetFullPathForTemplate());
            RE.SaveModFile(fixpath);
            ReverseEngineerRepository.Instance.ReloadMod(fixpath);
        }
        private void SortMods(object? sender, EventArgs e)
        {
            LoadOrderSorter sorter = new LoadOrderSorter(ModRepository.Instance.SelectedMods.ToList());//KenshiFix);//, KenshiBridge);

            sorter.Categorize("Invalid Mods", name => ReverseEngineerRepository.Instance.GetReverseEngineer(name) == null, true);
            sorter.Categorize("Patches", name => CoreUtils.isModAPatch(ModRepository.Instance.Mods.GetValueOrDefault(name)!), true);
            sorter.Categorize("KenshiFixer", name => name == KenshiFix+".mod",true);
            sorter.Categorize("KCF autogenerated patch", name => name == "-KCF autogenerated patch-.mod", true);
            
            sorter.Categorize("Creator", name => ReverseEngineerRepository.Instance.GetReverseEngineer(name)!.GetStringIdsNewRecords().Any());


            sorter.sortCategory(null, sorter.ApplyFileOverrideProximitySort);
            
            sorter.sortCategory("Creator", sorter.ApplyRecordPrecedenceSort);
            sorter.sortCategory("Creator", sorter.ApplyDirectDependencySort);


            sorter.sortCategory("Patches", sorter.ApplyRecordPrecedenceSort);
            ModRepository.Instance.SetSelectedMods(sorter.GetLoadOrder());
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
            await Task.Run(() => ReverseEngineerRepository.Instance.LoadFromMods( mergedMods));
            ReSearchProblems();
        }
        protected override Color GetModColor(ModItem mod)
        {
            if (mod.Name == KenshiFix+".mod")
                return Color.LightGreen;
            if(analyzer.hasRecordProblems(mod.Name, p => p is TypeMismatch)&&(CoreUtils.toggles.GetValueOrDefault("mismatch", false)))
                return Color.Red;
            if (analyzer.hasRecordProblems(mod.Name, p => p is MissingReference) && (CoreUtils.toggles.GetValueOrDefault("missing_refs", false)))
                return Color.Purple;
            if (analyzer.hasRecordProblems(mod.Name, p => p is EmptiedFilename) && (CoreUtils.toggles.GetValueOrDefault("emptied_paths", false)))
                return Color.Gold;
            if (analyzer.hasGeneralProblems(mod.Name)&&CoreUtils.toggles.GetValueOrDefault("file_overrides", false))
                return Color.LightBlue;
            return base.GetModColor(mod);
        }

    }
}
