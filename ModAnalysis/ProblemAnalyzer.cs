using KenshiCore.Mods;
using KenshiCore.ReverseEngineering;
using KenshiCore.UI;
using KenshiCore.Utilities;
using KenshiFixer.ModAnalysis;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KenshiFixer.Mod_Analysis
{
    public class RecordInfo
    {
        public string ModName { get; init; } = "";
        public ModRecord Record { get; init; } = null!;
        public Dictionary<string, List<string>> ExtraDataIds { get; set; } = new Dictionary<string, List<string>>();
        public Dictionary<string, List<string>> DeletedExtraDataIds { get; set; } = new Dictionary<string, List<string>>();
        public Dictionary<string, string> Filepaths { get; set; } = new Dictionary<string, string>();


        public RecordInfo(string modName, ModRecord record)
        {
            ModName = modName;
            Record = record;
            if (record.ExtraDataFields != null)
            {
                foreach (string category in record.ExtraDataFields.Keys)
                {
                    foreach (string stringid in record.ExtraDataFields[category].Keys)
                    {
                        if (ModRecord.IsDeleted(record.ExtraDataFields[category][stringid]))
                        {
                            if (!DeletedExtraDataIds.ContainsKey(category))
                            {
                                DeletedExtraDataIds[category] = new();
                            }
                            DeletedExtraDataIds[category].Add(stringid);
                        }
                        else
                        {
                            if (!ExtraDataIds.ContainsKey(category))
                            {
                                ExtraDataIds[category] = new();
                            }
                            ExtraDataIds[category].Add(stringid);
                        }
                    }
                }
            }
            if (record.FilenameFields != null)
            {
                foreach (string key in record.FilenameFields.Keys)
                {
                    Filepaths[key] = record.FilenameFields[key];
                }
            }
            
        }
    }
    public class ProblemAnalyzer
    {
        public ProblemAnalyzer() { }
        private Dictionary<string, List<RecordInfo>> collectedInfos = new Dictionary<string, List<RecordInfo>>();
        private Dictionary<string, List<Problem>>? Problems;
        private Dictionary<string,List<Problem>>? ProblemsByOverriden;

        public List <Problem>? GetProblems()
        {
            if (Problems == null)
                return null;
            return Problems.Values.SelectMany(p => p).ToList();
        }
        public void AnalyzeAll()
        {
            ReverseEngineerRepository repo = ReverseEngineerRepository.Instance;
            ProgressController progress = ProgressController.Instance;
            progress.Initialize(repo._loadOrder.Count);

            collectedInfos.Clear();
            Problems = null;
            ProblemsByOverriden = null;
            foreach (string modName in repo._loadOrder)
            {
                progress.ReportStep($"Analyzing mod: {modName}");

                ReverseEngineer? re;
                re= repo.GetReverseEngineer(modName);
                ModItem? mod = ModRepository.Instance.GetMergedMods().GetValueOrDefault(modName);
                if(mod==null || CoreUtils.isModAPatch(mod))
                {
                    continue;
                }
                if (re == null || re.modData == null )
                    continue;

                foreach (ModRecord record in re.modData.GetRecords())
                {
                    if (!collectedInfos.TryGetValue(record.StringId, out var infos))
                    {
                        infos = new List<RecordInfo>();
                        collectedInfos[record.StringId] = infos;
                    }

                    infos.Add(new RecordInfo(modName, record));
                }
            }

        }
        public void findProblems()
        {
            ProgressController progress = ProgressController.Instance;
            progress.Initialize(collectedInfos.Count);
            Problems = new Dictionary<string, List<Problem>>();
            ProblemsByOverriden= new Dictionary<string, List<Problem>>();
            foreach (string key in collectedInfos.Keys)
            {
                progress.ReportStep($"Analyzing Information: {key}");
                findProblemsInRecord(key);
            }
            FindFileOverrides();
            FindReKenshi();
            progress.Finish("Analysis complete");
        }
        private void FindFileOverrides()
        {
            var overrides = ModRepository.Instance.FindAssetOverrides();

            foreach (var pair in overrides)
            {
                string filename = pair.Key;
                List<string> mods = pair.Value.ToList();

                for (int i = 1; i < mods.Count; i++)
                {
                    FileOverride problem =new FileOverride(filename,mods[i - 1],mods[i]);
                    if (!ProblemsByOverriden!.TryGetValue(mods[i - 1], out List<Problem>? ov_problems))
                    {
                        ov_problems = new List<Problem>();
                        ProblemsByOverriden[mods[i - 1]] = ov_problems;
                    }
                    ov_problems.Add(problem);

                    if (!Problems!.TryGetValue(mods[i], out List<Problem>? problems))
                    {
                        problems = new List<Problem>();
                        Problems[mods[i]] = problems;
                    }

                    problems.Add(problem);
                }
            }
        }
        private void FindReKenshi()
        {
            foreach (var mod in ModRepository.Instance.Mods.Values)
            {
                string? modPath = Path.GetDirectoryName(mod.getModFilePath());
                if (modPath == null)
                    continue;
                foreach (string file in Directory.GetFiles(modPath, "*", SearchOption.AllDirectories))
                {
                    string filename = Path.GetFileName(file);
                    if(filename.Equals("RE_Kenshi.json", StringComparison.OrdinalIgnoreCase))
                    {
                        ReKenshi problem = new ReKenshi(mod.Name);
                        if (!Problems!.TryGetValue(mod.Name, out List<Problem>? problems))
                        {
                            problems = new List<Problem>();
                            Problems[mod.Name] = problems;
                        }
                        problems.Add(problem);
                    }
                }
            }
        }
        public void findProblemsInRecord(string strid)
        {
            Dictionary<string, EmptiedFilename> emptiedFilenames = new Dictionary<string, EmptiedFilename>();

            Dictionary<string, string> currentFilepaths = new Dictionary<string, string>();


            List <Problem> problems = new List<Problem>();
            int initialType = -1;
            RecordInfo? initialInfo = null;
            foreach (RecordInfo info in collectedInfos[strid])
            {
                if(initialType == -1)
                {
                    initialType = info.Record.getRecordTypeCode();
                    initialInfo = info;
                }

                if (info.Record.getRecordTypeCode() != initialType && initialType != -1)
                {
                    problems.Add(new TypeMismatch(initialInfo!, info));
                }

                foreach(string category in info.ExtraDataIds.Keys)
                {
                    foreach (string stringid in info.ExtraDataIds[category])
                    {
                        if (!collectedInfos.ContainsKey(stringid))
                        {
                            problems.Add(new MissingReference(info, category, stringid));
                        }
                    }
                }
                foreach (string category in info.DeletedExtraDataIds.Keys)
                {
                    foreach (string stringid in info.DeletedExtraDataIds[category])
                    {
                        problems.RemoveAll(p =>p is MissingReference missing && missing.Category == category && missing.StringId == stringid);
                    }
                }
                foreach (string key in info.Filepaths.Keys)
                {
                    string value = info.Filepaths[key];

                    if (!currentFilepaths.TryGetValue(key, out string? previousValue))
                    {
                        currentFilepaths[key] = value;
                        continue;
                    }

                    if (string.IsNullOrEmpty(value) &&
                        !string.IsNullOrEmpty(previousValue))
                    {
                        EmptiedFilename problem =new EmptiedFilename(info, key, previousValue);

                        problems.Add(problem);
                        emptiedFilenames[key] = problem;
                    }
                    else if (!string.IsNullOrEmpty(value))
                    {
                        // A later real value means the previous emptying is no longer the final effective state.
                        if (emptiedFilenames.TryGetValue(key, out EmptiedFilename? problem))
                        {
                            problems.Remove(problem);
                            emptiedFilenames.Remove(key);
                        }
                    }
                    currentFilepaths[key] = value;
                }
            }
            if(problems.Count > 0)
            {
                Problems![strid] = problems;
            }
        }
        public string GetGeneralProblemsForMod(string modName, Predicate<Problem>? condition = null)
        {
            if (Problems==null||!Problems.ContainsKey(modName)) {
                return "";
            }
            StringBuilder result = new StringBuilder(); 
            List<Problem> var_problems = Problems![modName];
            if (condition != null)
            {
                var_problems = var_problems.Where(p => condition(p)).ToList();
            }
            foreach (Problem p in Problems![modName])
            {
                if (p.involvedMods.Contains(modName))
                {
                    result.AppendLine(p.ToString());
                }
            }
            return result.ToString();
        }
        public string GetGeneralProblemsCausedForMod(string modName, Predicate<Problem>? condition = null)
        {
            if (ProblemsByOverriden == null || !ProblemsByOverriden.ContainsKey(modName))
            {
                return "";
            }
            StringBuilder result = new StringBuilder();
            List<Problem> var_problems = ProblemsByOverriden![modName];
            if (condition != null)
            {
                var_problems = var_problems.Where(p => condition(p)).ToList();
            }
            foreach (Problem p in ProblemsByOverriden![modName])
            {
                if (p.involvedMods.Contains(modName))
                {
                    result.AppendLine(p.ToString());
                }
            }
            return result.ToString();
        }
        public string GetProblemsForMod(string modName, Predicate<Problem>? condition=null)
        {
            if (Problems == null)
                return "Problems have not been analyzed yet. Click on Search Problems first.";
            ReverseEngineer? re = ReverseEngineerRepository.Instance.GetReverseEngineer(modName);
            if (re == null || re.modData == null)
                return "Empty mod data.";
            StringBuilder result = new StringBuilder();
            foreach (ModRecord record in re.modData.GetRecords())
            {
                if (Problems!.TryGetValue(record.StringId, out var problems))
                {
                    List<Problem> var_problems = problems.ToList();
                    if(condition != null)
                    {
                        var_problems = var_problems.Where(p => condition(p)).ToList();
                    }
                    foreach (Problem p in var_problems)
                    {
                        if (p.involvedMods.Contains(modName))
                        {
                            result.AppendLine(p.ToString());
                        }
                    }
               }
            }
            if(result.Length > 0)
            {
                return result.ToString();
            }
            return "";// "no problems found.";
        }
        public bool hasRecordProblems(string modName, Predicate<Problem>? condition=null)
        {
            if (condition == null)
            {
                condition = p => true;
            }
            if (Problems == null)
                return false;
            ReverseEngineer? re = ReverseEngineerRepository.Instance.GetReverseEngineer(modName);
            if (re == null || re.modData == null)
                return false;
            foreach (ModRecord record in re.modData.GetRecords())
            {
                if (Problems!.ContainsKey(record.StringId)&& Problems![record.StringId].Any(p => condition(p)&&p.involvedMods.Contains(modName)))
                {
                    return true;
                }
            }
            return false;
        }
        public bool hasGeneralProblems(string modName, Predicate<Problem>? condition=null)
        {
            if (Problems == null)
                return false;
            if (Problems!.ContainsKey(modName)&& (condition == null || Problems![modName].Any(p=>condition(p))))
            {
                return true;
            }
            return false;
        }
        public bool isCausedByGeneralProblems(string modName)
        {
            if (ProblemsByOverriden == null)
                return false;
            if (!ProblemsByOverriden!.ContainsKey(modName))
            {
                return false;
            }
            return true;
        }
    }
}