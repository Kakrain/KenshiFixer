using KenshiCore.Mods;
using KenshiCore.ReverseEngineering;
using KenshiFixer.Mod_Analysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KenshiFixer.ModAnalysis
{
    public abstract class Problem
    {
        public HashSet<string> involvedMods { get; } = new();

        public abstract override string ToString();
    }
    public class FileOverride : Problem
    {
        public string Filename { get; }
        public string OverriddenMod { get; }
        public string OverridingMod { get; }

        public FileOverride(string filename, string overriddenMod, string overridingMod)
        {
            Filename = filename;
            OverriddenMod = overriddenMod;
            OverridingMod = overridingMod;

            involvedMods.Add(overriddenMod);
            involvedMods.Add(overridingMod);
        }

        public override string ToString()
        {
            return $"FileOverride: {Filename} - " + $"{OverridingMod} overrides {OverriddenMod}";
        }
    }
    public class ReKenshi : Problem
    {

        public ReKenshi(string modName)
        {
            involvedMods.Add(modName);
        }

        public override string ToString()
        {
            return $"ReKenshi mod: {involvedMods.ElementAt(0)}";
        }
    }
    public abstract class RecordProblem : Problem
    {
        public string RecordId { get; }
        public string RecordName { get; }

        //public HashSet<string> involvedMods { get; } = new HashSet<string>();

        public RecordProblem(RecordInfo info)
        {
            RecordId = info.Record.StringId;
            RecordName = info.Record.Name;
            involvedMods.Add(info.ModName);
        }
    }

    public class TypeMismatch : RecordProblem
    {
        public int InitialType { get; }
        public int FinalType { get; }

        public TypeMismatch(RecordInfo info, RecordInfo other) : base(info)
        {
            InitialType = info.Record.getRecordTypeCode();
            FinalType = other.Record.getRecordTypeCode();

            involvedMods.Add(info.ModName);
            involvedMods.Add(other.ModName);
        }
        public override string ToString()
        {
            return $"{RecordId} ({RecordName}) - from: {ModRecord.getRecordTypeName(InitialType)} ({involvedMods.ElementAt(0)}), to: {ModRecord.getRecordTypeName(FinalType)} ( {involvedMods.ElementAt(1)})";
        }
    }

    public class MissingReference : RecordProblem
    {
        public string Category { get; } = string.Empty;
        public string StringId { get; } = string.Empty;

        public MissingReference(RecordInfo info,string category, string strid) : base(info)
        {
            Category = category;
            StringId = strid;
        }
        public override string ToString()
        {
            return $"MissingReference: {RecordId} ({RecordName}) - Category: {Category}, StringId: {StringId}";
        }
    }
    public class EmptiedFilename : RecordProblem
    {
        public string key { get; } = string.Empty;
        public string validValue { get; } = string.Empty;

        public EmptiedFilename(RecordInfo info, string key, string validValue) : base(info)
        {
            this.key = key;
            this.validValue = validValue;
        }
        public override string ToString()
        {
            return $"EmptiedFilename: {RecordId} ({RecordName}) - Field: {key}, Valid Value: {validValue}";
        }
    }
    
}
