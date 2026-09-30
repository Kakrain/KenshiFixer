using KenshiCore.Mods;
using KenshiCore.ReverseEngineering;
using KenshiCore.UI;
using KenshiCore.Utilities;
using KenshiFixer.ModAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace KenshiFixer.Fixers
{
    public class KenshiFixerGenerator
    {
        public const string TEMPLATE_NAME = "-KenshiFixer_Fix-";
        private ReverseEngineer RE;
        private ReverseEngineerRepository RERepository = ReverseEngineerRepository.Instance;
        private Dictionary<(int, string), int> can_crash_registry = new();
        private Dictionary<string, ModRecord> replacements = new Dictionary<string, ModRecord>();
        private Dictionary<int, ModRecord> fallbackTemplates = new();

        public KenshiFixerGenerator()
        {
            RE = new ReverseEngineer(TEMPLATE_NAME+".mod");
            LoadTemplate();
            AddCrashMapping("SQUAD_TEMPLATE", "faction", "FACTION");
            AddCrashMapping("SQUAD_TEMPLATE", "leader", "CHARACTER");
            AddCrashMapping("SQUAD_TEMPLATE", "squad", "CHARACTER");
            AddCrashMapping("SQUAD_TEMPLATE", "squad2", "CHARACTER");
            AddCrashMapping("SQUAD_TEMPLATE", "animals", "ANIMAL_CHARACTER");
            AddCrashMapping("SQUAD_TEMPLATE", "animals2", "ANIMAL_CHARACTER");
            AddCrashMapping("RACE", "hair colors", "COLOR_DATA");
            AddCrashMapping("RACE", "hairs", "ATTACHMENT");
            AddCrashMapping("BUILDING_PART", "material", "MATERIAL_SPEC");


            AddCrashMapping("TOWN", "faction", "FACTION");

        }
        private void AddCrashMapping(string sourceType,string category,string targetType)
        {
            can_crash_registry[(ModRecord.getRecordTypeInt(sourceType), category)] = ModRecord.getRecordTypeInt(targetType);
        }
        private void LoadFallbackTemplates()
        {
            fallbackTemplates.Clear();

            foreach (ModRecord record in RE.modData.GetRecords())
            {
                int type = record.getRecordTypeCode();

                if (!fallbackTemplates.ContainsKey(type))
                {
                    fallbackTemplates[type] = record;
                }
            }
        }
        private void LoadTemplate()
        {
            RE = new ReverseEngineer(TEMPLATE_NAME+".mod");
            string exeDir = AppContext.BaseDirectory;
            string fixTemplatePath = Path.Combine(
                exeDir,
                "FixTemplates",
                TEMPLATE_NAME
            );
            RE.LoadModFile(fixTemplatePath);
            LoadFallbackTemplates();
        }
        private bool TryGetCulprit(RecordProblem problem, out ModRecord? culprit)
        {
            culprit = null;

            string modname = problem.involvedMods.ElementAt(0);

            ReverseEngineer? re = RERepository.GetReverseEngineer(modname);

            if (re == null || re.modData?.Count== 0)
                return false;

            culprit = re.modData!.GetRecordByStringId(problem.RecordId);

            if (culprit == null)
            {
                CoreUtils.Print(
                    $"Could not find culprit record {problem.RecordId} in {modname}.");

                return false;
            }

            return true;
        }
        public void SolveProblems(List<Problem> problems)
        {
            foreach(Problem problem in problems)
            {
                if (problem is not RecordProblem rprob|| !TryGetCulprit(rprob, out ModRecord? culprit))
                    continue;
                switch(problem)
                {
                    case MissingReference misref:
                        SolveMissingReference(misref, culprit!);
                        break;
                    case EmptiedFilename emptied:
                        SolveEmptiedFilename(emptied, culprit!);
                        break;
                    default:
                        CoreUtils.Print($"Cannot fix problem of type {problem.GetType().Name}");
                        break;
                }
            }
        }

        private void SolveMissingReference(MissingReference problem, ModRecord culprit)
        {
            if (culprit.ExtraDataFields == null || !culprit.ExtraDataFields.ContainsKey(problem.Category))
            {
                CoreUtils.Print("Warning category not found");
                return;
            }

            int[] vars = culprit.ExtraDataFields[problem.Category][problem.StringId];

            ModRecord fixedRecord = RE.EnsureRecordExists(culprit);

            // Every missing reference gets removed.
            fixedRecord.DeleteExtraData(problem.Category, problem.StringId);

            // Some references additionally require a fallback.
            if (!can_crash_registry.TryGetValue((culprit.getRecordTypeCode(), problem.Category),out int fallbackType))
            {
                return;
            }

            if (!fallbackTemplates.TryGetValue(fallbackType,out ModRecord? fallbackRecord))
            {
                CoreUtils.Print($"No fallback template for record type " +$"{ModRecord.getRecordTypeName(fallbackType)}.");
                return;
            }

            RE.AddExtraData(fixedRecord,getReplacementRecord(problem.StringId, fallbackRecord),problem.Category,vars);
        }
        private void SolveEmptiedFilename(EmptiedFilename problem,ModRecord culprit)
        {
            ModRecord fixedRecord = RE.EnsureRecordExists(culprit);
            if (fixedRecord.FilenameFields == null)
            {
                fixedRecord.FilenameFields = new Dictionary<string, string>(); 
            }
            fixedRecord.FilenameFields[problem.key] = problem.validValue;
        }
        private ModRecord getReplacementRecord(string strid,ModRecord fallbackRecord)
        {
            if (!replacements.TryGetValue(strid, out ModRecord? cloned))
            {
                cloned = RE.CloneRecord(fallbackRecord, 1)[0];
                cloned.Name = strid + "_fallback_by_KenshiFixer";
                replacements[strid] = cloned;
            }
            return cloned;

        }
        public static string GetFullPathForFixMod()
        {
            string modsRoot = ModManager.gamedirModsPath
                ?? throw new InvalidOperationException("Mods directory not set");
            string fixFolder = Path.Combine(modsRoot, $"{TEMPLATE_NAME}");
            string fixModFile = Path.Combine(fixFolder, $"{TEMPLATE_NAME}.mod");
            return fixModFile;
        }
        public static string GetFullPathForTemplate()
        {
            string exeDir = AppContext.BaseDirectory;
            string fixTemplatePath = Path.Combine(
                exeDir,
                "FixTemplates",
                TEMPLATE_NAME
            );
            return fixTemplatePath;
        }



        public void Save()
        {
            string modsRoot = ModManager.gamedirModsPath
                ?? throw new InvalidOperationException("Mods directory not set");

            string fixFolder = Path.Combine(modsRoot, $"{TEMPLATE_NAME}");
            string fixModFile = Path.Combine(fixFolder, $"{TEMPLATE_NAME}.mod");

            Directory.CreateDirectory(fixFolder);

            RE.SaveModFile(fixModFile);
            UiService.ShowMessage($"{TEMPLATE_NAME}.mod saved!");
        }
    }
}
