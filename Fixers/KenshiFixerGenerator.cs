using KenshiCore.Mods;
using KenshiCore.ReverseEngineering;
using KenshiCore.UI;
using KenshiCore.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KenshiFixer.Fixers
{
    public class KenshiFixerGenerator
    {
        public const string TEMPLATE_NAME = "-KenshiFixer_Fix-";
        private ReverseEngineer RE;
        private ReverseEngineerRepository RERepository = ReverseEngineerRepository.Instance;
        private Dictionary<(string, string), string> can_crash_registry = new();
        private Dictionary<string, string> fallbacks_sids = new Dictionary<string, string>();
        private Dictionary<string, ModRecord> replacements = new Dictionary<string, ModRecord>();

        public KenshiFixerGenerator()
        {
            RE = new ReverseEngineer();
            LoadTemplate();
            can_crash_registry[("SQUAD_TEMPLATE", "faction")] = "FACTION";
            can_crash_registry[("SQUAD_TEMPLATE", "leader")] = "CHARACTER";
            can_crash_registry[("SQUAD_TEMPLATE", "squad")] = "CHARACTER";
            can_crash_registry[("SQUAD_TEMPLATE", "squad2")] = "CHARACTER";
            can_crash_registry[("SQUAD_TEMPLATE", "animals")] = "ANIMAL_CHARACTER";
            can_crash_registry[("SQUAD_TEMPLATE", "animals2")] = "ANIMAL_CHARACTER";

            fallbacks_sids["FACTION"] = $"10-{TEMPLATE_NAME}.mod";
            fallbacks_sids["CHARACTER"] = $"11-{TEMPLATE_NAME}.mod";
            fallbacks_sids["ANIMAL_CHARACTER"] = $"12-{TEMPLATE_NAME}.mod";
        }
        private void LoadTemplate()
        {
            RE = new ReverseEngineer();
            string exeDir = AppContext.BaseDirectory;
            string fixTemplatePath = Path.Combine(
                exeDir,
                "FixTemplates",
                TEMPLATE_NAME
            );
            RE.LoadModFile(fixTemplatePath);
        }
        public void AddEmergencyFallbacks()
        {
            foreach ((string, string) elem in can_crash_registry.Keys)
            {
                string category = elem.Item2;
                IReadOnlyDictionary<string, ModRecord> main_records = RERepository.GetAllRecordsMerged(elem.Item1);
                IReadOnlyDictionary<string, ModRecord> XD_records = RERepository.GetAllRecordsMerged(can_crash_registry[elem]);
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
                                ModRecord fallbackRecord = RE.searchModRecordByStringId(fallback_sid)!;
                                CoreUtils.Print(fallbackRecord.ToString());
                                ModRecord fixed_record = RE.EnsureRecordExists(record);
                                fixed_record.DeleteExtraData(category, sid);

                                replacements.TryGetValue(sid, out ModRecord? cloned);
                                if (cloned == null)
                                {
                                    cloned = RE.CloneRecord(fallbackRecord, 1)[0];
                                    cloned.Name = "restored_" + sid + "_fallback";
                                    replacements[sid] = cloned;
                                }
                                RE.AddExtraData(fixed_record, cloned, category, XDCategory[sid]);
                            }
                        }

                    }
                }
            }
            foreach (string id in fallbacks_sids.Values)
            {
                RE.deleteRecord(RE.searchModRecordByStringId(id)!);
            }
        }
        public void RestoreEmptiedFilenames()
        {
            Dictionary<string, Dictionary<string,string>> current_value=new();
            Dictionary<string, Dictionary<string, bool>> should_update = new();
            Dictionary<string, ModRecord> records_to_restore = new();
            ProgressController progress = ProgressController.Instance;
            progress.Initialize(RERepository._loadOrder.Count);

            int i = 0;
            foreach (var modName in RERepository._loadOrder.AsEnumerable().Reverse())
            {
                i++;
                progress.Report(i, $"Processing {modName}: {i}/{RERepository._loadOrder.Count}...");
                if (RERepository._reverseEngineers.TryGetValue(modName, out var re))
                {
                    foreach (ModRecord record in re.modData.Records!)
                    {
                        if (!current_value.ContainsKey(record.StringId))
                        {
                            current_value[record.StringId] = new Dictionary<string, string>();
                            should_update[record.StringId] = new Dictionary<string, bool>();
                            records_to_restore[record.StringId] = record;
                        }
                        foreach (string filename in record.FilenameFields.Keys)
                        {
                            if (!current_value[record.StringId].ContainsKey(filename))
                            {
                                current_value[record.StringId][filename] = record.FilenameFields[filename];
                                should_update[record.StringId][filename] = false;
                            }
                            else
                            {
                                if (should_update[record.StringId][filename])
                                    continue;

                                if (string.IsNullOrEmpty(current_value[record.StringId][filename]) && !string.IsNullOrEmpty(record.FilenameFields[filename]))
                                {
                                    current_value[record.StringId][filename] = record.FilenameFields[filename];
                                    should_update[record.StringId][filename] = true;
                                }
                                else
                                {
                                    current_value[record.StringId][filename] = record.FilenameFields[filename];
                                    should_update[record.StringId][filename] = false;
                                }
                            }
                        }
                    }
                }
            }
            progress.Finish();
            foreach(string sid in current_value.Keys)
            {
                foreach (string filename in current_value[sid].Keys)
                {
                    if (should_update[sid][filename])
                    {
                        ModRecord record = RE.EnsureRecordExists(records_to_restore[sid]);
                        record.FilenameFields[filename] = current_value[sid][filename];
                    }
                }
            }
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
