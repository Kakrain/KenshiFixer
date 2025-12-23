using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KenshiFixer.Forms
{
    using KenshiCore;
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
    using static System.Windows.Forms.VisualStyles.VisualStyleElement.Window;

    public class MainForm : ProtoMainForm
    {
        private ReverseEngineerRepository RERepository = ReverseEngineerRepository.Instance;
        private Dictionary<(string, string), string> can_crash_registry = new();
        private Dictionary<string,string> fallbacks_sids = new Dictionary<string,string>();
        private Dictionary<string, ModRecord> replacements = new Dictionary<string, ModRecord>();
        private ReverseEngineer RE_fixed=new ReverseEngineer();

        public ReverseEngineer fixEngineer;
        private HashSet<string> hidden_dependencies = new HashSet<string>();
        private HashSet<string> broken_paths_mods = new HashSet<string>();
        public MainForm()
        {
            Text = "Kenshi Fixer";
            Width = 800;
            Height = 500;
            this.ForeColor = Color.FromArgb(unchecked((int)0xFF2A2520)); 
            setColors(Color.FromArgb(unchecked((int)0xFF8A3A3A)), Color.FromArgb(unchecked((int)0xFFD4CFC2)));

            AddColumn("Status", mod => getModStatus(mod), 150);
            AddButton("Diagnose FilePaths", DiagnosePathsClick, mod => true);
            AddButton("Generate Fix", GenerateFix, mod => true);
            shouldResetLog = false;
            fixEngineer = new ReverseEngineer();

            can_crash_registry[("SQUAD_TEMPLATE", "faction")] = "FACTION";
            can_crash_registry[("SQUAD_TEMPLATE", "leader")] = "CHARACTER";
            can_crash_registry[("SQUAD_TEMPLATE", "squad")] = "CHARACTER";
            can_crash_registry[("SQUAD_TEMPLATE", "squad2")] = "CHARACTER";
            can_crash_registry[("SQUAD_TEMPLATE", "animals")] = "ANIMAL_CHARACTER";
            can_crash_registry[("SQUAD_TEMPLATE", "animals2")] = "ANIMAL_CHARACTER";
            //modsListView.SelectedIndexChanged += Mainform_SelectedIndexChanged;

            fallbacks_sids["FACTION"] = "10--KenshiFixer_Fix-.mod";
            fallbacks_sids["CHARACTER"] = "11--KenshiFixer_Fix-.mod";
            fallbacks_sids["ANIMAL_CHARACTER"] = "12--KenshiFixer_Fix-.mod";
        }
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ShowLogButton.Enabled = true;
        }
        private string getModStatus(ModItem mod)
        {
            if (broken_paths_mods.Contains(mod.Name))
                return "broken_path";
            return "ok";
        }
        private void LoadTemplate()
        {
            RE_fixed = new ReverseEngineer();
            string exeDir = AppContext.BaseDirectory;
            string fixTemplatePath = Path.Combine(
                exeDir,
                "FixTemplates",
                "-KenshiFixer_Fix-"
            );
            RE_fixed.LoadModFile(fixTemplatePath);
        }
        private void GenerateFix(object? sender, EventArgs e)
        {
            LoadTemplate();
            foreach ((string,string) elem in can_crash_registry.Keys)
            {
                string category = elem.Item2;
                IReadOnlyDictionary<string,ModRecord> main_records = RERepository.GetAllRecordsMerged(elem.Item1);
                IReadOnlyDictionary<string,ModRecord> XD_records = RERepository.GetAllRecordsMerged(can_crash_registry[elem]);
                foreach (var record in main_records.Values)
                {
                    Dictionary<string, int[]>? XDCategory = record.GetExtraData(category);
                    if (XDCategory != null)
                    {
                        foreach (string sid in XDCategory.Keys)
                        {
                            if (!XD_records.ContainsKey(sid))
                            {
                                string fallback_sid = fallbacks_sids[can_crash_registry[elem]];
                                ModRecord fallbackRecord = RE_fixed.searchModRecordByStringId(fallback_sid)!;
                                ModRecord fixed_record=RE_fixed.EnsureRecordExists(record);
                                fixed_record.DeleteExtraData(category,sid);

                                replacements.TryGetValue(sid, out ModRecord? cloned);
                                if (cloned == null)
                                {
                                    cloned = RE_fixed.CloneRecord(fallbackRecord, 1)[0];
                                    cloned.Name = "restored_" + sid + "_fallback";
                                    replacements[sid]= cloned;
                                }
                                RE_fixed.AddExtraData(fixed_record, cloned, category, XDCategory[sid]);
                            }
                        }

                    }
                }
            }

            foreach(string id in fallbacks_sids.Values)
            {
                RE_fixed.deleteRecord(RE_fixed.searchModRecordByStringId(id)!);
            }
            SaveFixMod();
        }
        public void SaveFixMod()
        {
            string modsRoot = ModManager.gamedirModsPath
                ?? throw new InvalidOperationException("Mods directory not set");

            string fixFolder = Path.Combine(modsRoot, "-KenshiFixer_Fix-");
            string fixModFile = Path.Combine(fixFolder, "-KenshiFixer_Fix-.mod");

            Directory.CreateDirectory(fixFolder); // safe even if it exists

            RE_fixed.SaveModFile(fixModFile);
            CoreUtils.Print("-KenshiFixer_Fix- saved!",1);
        }
        private void DiagnosePathsClick(object? sender, EventArgs e)
        {
            var logform = getLogForm();
            hidden_dependencies = new HashSet<string>();
            broken_paths_mods= new HashSet<string>();
            int ocurrences = 0;
            StringBuilder sb= new StringBuilder();



            //foreach(ModItem mod in ReverseEngineersCache.Keys)
            foreach (var kvp in mergedMods)
            {
                string modName = kvp.Key;
                ModItem mod = kvp.Value;
                if (!RERepository.TryGet(modName, out var re) || re == null)
                    continue;
                string? modpath = mod.getModFilePath();
                if (string.IsNullOrEmpty(modpath))
                    continue;
                //ReverseEngineer re= ReverseEngineersCache[mod];
                //string? modpath = mod.getModFilePath()!;

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
            }
            if (logform.InvokeRequired)
            {
                logform.BeginInvoke((Action)(() =>
                {
                    logform.LogString($"-----------BROKEN FILEPATHS({ocurrences}):------------\n", Color.IndianRed);
                    logform.LogString(sb.ToString(), Color.Red);
                    logform.LogString($"-----------HIDDEN DEPENDENCIES({hidden_dependencies.Count()}):------------\n", Color.IndianRed);
                    logform.LogString(string.Join(Environment.NewLine, hidden_dependencies), Color.Orange);
                    logform.Refresh();
                }));
            }
            else
            {
                logform.LogString($"-----------BROKEN FILEPATHS({ocurrences}):------------\n", Color.IndianRed);
                logform.LogString(sb.ToString(), Color.Red);
                logform.LogString($"-----------HIDDEN DEPENDENCIES({hidden_dependencies.Count()}):------------\n", Color.OrangeRed);
                logform.LogString(string.Join(Environment.NewLine, hidden_dependencies), Color.Orange);
                logform.Refresh();
            }
            RefreshColumn(1);

        }
        public bool ModFileExists(ModItem mod, string filePath)
        {
            if (mod == null || string.IsNullOrEmpty(filePath))
                return false;
            string normalizedPath = filePath.Replace('\\','/');
            if(mod.WorkshopId != -1 && !normalizedPath.StartsWith(@"./data", StringComparison.OrdinalIgnoreCase))
            {   
                string[] parts = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                string[] remainingParts = parts.Skip(3).ToArray();
                string relativePathInsideMod = Path.Combine(remainingParts);
                if (parts.Count()<=2)
                    return false;
                string key = mergedMods.Keys.FirstOrDefault(k => string.Equals(k, parts[2] + ".mod", StringComparison.OrdinalIgnoreCase))!;
                if (key==null)//!mergedMods.ContainsKey(parts[2] + ".mod"))
                {
                    hidden_dependencies.Add(parts[2] + ".mod");
                    return false;
                }
                return File.Exists(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(mergedMods[key].getModFilePath()!)!, relativePathInsideMod)));
            }
            return File.Exists(Path.GetFullPath(Path.Combine(Path.GetFullPath(Path.Combine(ModManager.gamedirModsPath!, "..")), normalizedPath)));
        }
        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            await OnShownAsync(e);
        }
        private async Task OnShownAsync(EventArgs e)
        {
            if (InitializationTask != null)
                await InitializationTask;
            ExcludeUnselectedMods();
            LoadBaseGameData();
            InitializeProgress(0, mergedMods.Count);
            int i = 0;
            List<string> modsToRemove = new();
            foreach (var mod in mergedMods)
            {
                ReverseEngineer re = new ReverseEngineer();
                string realmodpath = mod.Value.getModFilePath()!;
                if (string.IsNullOrEmpty(realmodpath))
                {
                    modsToRemove.Add(mod.Key);
                    continue;
                }
                re.LoadModFile(realmodpath);
                RERepository.AddOrUpdate(mod.Key, re);
                i++;
                ReportProgress(i, $"Engineered mod:{i}");
            }
            foreach (var key in modsToRemove)
                mergedMods.Remove(key);

            modsListView.BeginUpdate();
            try
            {
                foreach (ListViewItem item in modsListView.Items.Cast<ListViewItem>().ToList())
                {
                    if (item.Tag is ModItem mod && modsToRemove.Contains(mod.Name))
                        modsListView.Items.Remove(item);
                }
            }
            finally
            {
                modsListView.EndUpdate();
            }


        }
    }
    

}
