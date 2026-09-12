using KenshiCore.Mods;
using KenshiCore.ReverseEngineering;
using KenshiCore.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;

namespace KenshiFixer.Fixers
{
    public class LoadOrderSorter
    {
        private readonly List<string> _allMods;
        private readonly Dictionary<string, List<string>> _preCategories;
        private readonly Dictionary<string, List<string>> _postCategories;
        public LoadOrderSorter(List<string> modnames)
        {
            _allMods = modnames;
            _preCategories = new();
            _postCategories = new();
        }
        public List<string> GetLoadOrder()
        {
            var result = new List<string>();

            foreach (var category in _preCategories)
            {
                result.AddRange(category.Value);
            }

            // Anything not categorized goes here
            result.AddRange(_allMods);

            foreach (var category in _postCategories.Reverse())
                result.AddRange(category.Value);

            return result;
        }

        public void Categorize(string name,Predicate<string> predicate,Boolean last=false)
        {
            if(last)
                _postCategories[name] = Take(predicate);
            else
                _preCategories[name] = Take(predicate);
        }
        private List<string> Take(Predicate<string> predicate)
        {
            var selected = _allMods.Where(s => predicate(s)).ToList();

            _allMods.RemoveAll(predicate);

            return selected;
        }
        public void sortCategory(string? name, Action<List<string>> sortingStrategy)
        {
            if (name == null)
            {
                sortingStrategy(_allMods);
                return;
            }
            if (_preCategories.TryGetValue(name, out var preList))
                sortingStrategy(preList);
            else if (_postCategories.TryGetValue(name, out var postList))
                sortingStrategy(postList);
        }
        public void ApplyRecordPrecedenceSort(List<string> modnames)
        {
            Dictionary<string, HashSet<string>> newOwners = new();
            Dictionary<string, HashSet<string>> oldOwners = new();

            foreach (var mod in modnames)
            {
                var re = ReverseEngineerRepository.Instance.GetReverseEngineer(mod);

                foreach (var id in re!.GetStringIdsNewRecords())
                {
                    if (!newOwners.TryGetValue(id, out var mods))
                        newOwners[id] = mods = new HashSet<string>();

                    mods.Add(mod);
                }

                foreach (var id in re.GetStringIdsOldRecords())
                {
                    if (!oldOwners.TryGetValue(id, out var mods))
                        oldOwners[id] = mods = new HashSet<string>();

                    mods.Add(mod);
                }
            }

            // Build precedence relationships:
            // creator -> modifier
            // The creator must come before the modifier.
            Dictionary<string, HashSet<string>> before = new();

            foreach (var id in newOwners.Keys)
            {
                if (!oldOwners.TryGetValue(id, out var modifiedMods))
                    continue;

                foreach (var creator in newOwners[id])
                {
                    foreach (var modifier in modifiedMods)
                    {
                        if (creator == modifier)
                            continue;

                        if (!before.TryGetValue(creator, out var mods))
                            before[creator] = mods = new HashSet<string>();

                        mods.Add(modifier);
                    }
                }
            }

            // Invert the graph so that each mod knows which mods
            Dictionary<string, HashSet<string>> after = new();

            foreach (var (creator, modifiers) in before)
            {
                foreach (var modifier in modifiers)
                {
                    if (!after.TryGetValue(modifier, out var mods))
                        after[modifier] = mods = new HashSet<string>();

                    mods.Add(creator);
                }
            }

            // Topological sort.
            var remaining = new HashSet<string>(modnames);
            var result = new List<string>();

            while (remaining.Count > 0)
            {
                var available = remaining
                    .Where(mod =>
                        !after.TryGetValue(mod, out var dependencies) ||
                        !dependencies.Any(remaining.Contains))
                    .ToList();

                // No mod can be placed: the precedence graph contains a cycle.
                if (available.Count == 0)
                {
                    foreach (var mod in modnames)
                    {
                        if (remaining.Contains(mod))
                            result.Add(mod);
                    }
                    break;
                }
                foreach (var mod in available)
                {
                    result.Add(mod);
                    remaining.Remove(mod);
                }
            }

            modnames.Clear();
            modnames.AddRange(result);
        }
        public void ApplyFileOverrideProximitySort(List<string> modnames)
        {
            var overrides = ModRepository.Instance.FindAssetOverrides();

            // Build: mod -> all mods it shares files with.
            var neighbors = new Dictionary<string, HashSet<string>>(
                StringComparer.Ordinal);

            foreach (var mods in overrides.Values)
            {
                foreach (string mod in mods)
                {
                    if (!neighbors.TryGetValue(mod, out var set))
                    {
                        set = new HashSet<string>(StringComparer.Ordinal);
                        neighbors[mod] = set;
                    }

                    foreach (string other in mods)
                    {
                        if (!string.Equals(mod, other, StringComparison.Ordinal))
                            set.Add(other);
                    }
                }
            }

            // Work from the existing order.
            var remaining = new HashSet<string>(
                modnames,
                StringComparer.Ordinal);

            var result = new List<string>(modnames.Count);

            while (remaining.Count > 0)
            {
                // Preserve the earliest remaining mod as the anchor.
                string current = modnames.First(m => remaining.Contains(m));

                remaining.Remove(current);
                result.Add(current);

                // Pull its file-conflicting mods next, preserving their
                // original order.
                if (neighbors.TryGetValue(current, out var related))
                {
                    foreach (string mod in modnames)
                    {
                        if (remaining.Contains(mod) && related.Contains(mod))
                        {
                            remaining.Remove(mod);
                            result.Add(mod);
                        }
                    }
                }
            }
            modnames.Clear();
            modnames.AddRange(result);
        }
        //may not be needed for kenshi
        /*
        public void ApplyDependencyAwareSort(List<string> modnames)
        {
            // SCORE TABLES
            Dictionary<string, int> creatorScore = new();
            Dictionary<string, int> conflictScore = new();
            Dictionary<string, int> dependencyScore = new();

            // GLOBAL INDEXES
            Dictionary<string, HashSet<string>> newOwners = new();
            Dictionary<string, HashSet<string>> oldOwners = new();

            // STEP 1: LOAD ALL MOD DATA
            foreach (var mod in modnames)
            {
                var re = ReverseEngineerRepository.Instance.GetReverseEngineer(mod);

                foreach (var id in re!.GetStringIdsNewRecords())
                {
                    if (!newOwners.TryGetValue(id, out var set))
                        newOwners[id] = set = new HashSet<string>();

                    set.Add(mod);
                }

                foreach (var id in re.GetStringIdsOldRecords())
                {
                    if (!oldOwners.TryGetValue(id, out var set))
                        oldOwners[id] = set = new HashSet<string>();
                }
            }

            // init scores
            foreach (var m in modnames)
            {
                creatorScore[m] = 0;
                conflictScore[m] = 0;
                dependencyScore[m] = 0;
            }

            // STEP 2: SORT 1 (CREATOR SCORE)
            foreach (var m in modnames)
            {
                var re = ReverseEngineerRepository.Instance.GetReverseEngineer(m);

                creatorScore[m] = re!.GetStringIdsNewRecords().Count
                                - re.GetStringIdsOldRecords().Count;
            }

            // STEP 3 + 4: SORT 2 + SORT 3
            var allIds = new HashSet<string>(newOwners.Keys);
            allIds.UnionWith(oldOwners.Keys);

            foreach (var id in allIds)
            {
                newOwners.TryGetValue(id, out var newMods);
                oldOwners.TryGetValue(id, out var oldMods);

                newMods ??= new HashSet<string>();
                oldMods ??= new HashSet<string>();

                // SORT 2: NEW vs NEW
                if (newMods.Count > 1)
                {
                    foreach (var m in newMods)
                        conflictScore[m] += 3;
                }

                // SORT 2: NEW vs OLD
                if (newMods.Count > 0 && oldMods.Count > 0)
                {
                    foreach (var m in newMods)
                        conflictScore[m] += 2;

                    foreach (var m in oldMods)
                        conflictScore[m] += 2;
                }

                // SORT 2: OLD vs OLD
                if (oldMods.Count > 1)
                {
                    foreach (var m in oldMods)
                        conflictScore[m] += 1;
                }

                // SORT 3: dependency inference
                // OLD implies "expects provider"
                foreach (var m in oldMods)
                {
                    foreach (var provider in newMods)
                    {
                        if (m != provider)
                            dependencyScore[m] += 1;
                    }
                }
            }

            //FINAL SORT
            modnames.Sort((a, b) =>
            {
                int da = dependencyScore[a];
                int db = dependencyScore[b];

                if (da != db) return da.CompareTo(db); // dependency first

                int ca = conflictScore[a];
                int cb = conflictScore[b];

                if (ca != cb) return ca.CompareTo(cb); // conflict second

                return creatorScore[b].CompareTo(creatorScore[a]); // creator last
            });
        }*/
        public void ApplyDirectDependencySort(List<string> modnames)
        {
            var baseGameMods = new HashSet<string>(ModRepository.Instance.BaseGameMods);
            var mods = modnames
                .Where(mod => !baseGameMods.Contains(mod))
                .ToList();

            var modSet = new HashSet<string>(mods);

            // dependency -> dependents
            var dependents = mods.ToDictionary(mod => mod, _ => new List<string>());

            // Number of dependencies that still need to be placed before each mod.
            var dependencyCount = mods.ToDictionary(mod => mod, _ => 0);

            // Build dependency graph.
            foreach (var mod in mods)
            {
                var re = ReverseEngineerRepository.Instance.GetReverseEngineer(mod);
                if (re == null)
                    continue;

                foreach (var dependency in re.getDependencies() ?? Enumerable.Empty<string>())
                {
                    if (string.IsNullOrEmpty(dependency) ||
                        baseGameMods.Contains(dependency) ||
                        !modSet.Contains(dependency))
                    {
                        continue;
                    }

                    dependents[dependency].Add(mod);
                    dependencyCount[mod]++;
                }
            }

            var remaining = new HashSet<string>(mods);
            var sorted = new List<string>();

            // Always choose the earliest available mod in the original order.
            while (remaining.Count > 0)
            {
                string? next = null;

                foreach (var mod in mods)
                {
                    if (!remaining.Contains(mod))
                        continue;

                    if (dependencyCount[mod] == 0)
                    {
                        next = mod;
                        break;
                    }
                }

                // Cycle: preserve the original order for everything remaining.
                if (next == null)
                {
                    sorted.AddRange(mods.Where(remaining.Contains));
                    break;
                }

                sorted.Add(next);
                remaining.Remove(next);

                foreach (var dependent in dependents[next])
                    dependencyCount[dependent]--;
            }

            modnames.Clear();
            modnames.AddRange(sorted);
        }
        /*public void ApplyDirectDependencySort(List<string> modnames)
        {
            HashSet<string> baseGameMods = new(ModRepository.Instance.BaseGameMods);

            // Keep only mods we actually care about (not base game)
            HashSet<string> modSet = new(modnames.Where(m => !baseGameMods.Contains(m)));

            // adjacency list: dependency -> dependents
            Dictionary<string, List<string>> graph = new();

            // in-degree: how many dependencies each mod has
            Dictionary<string, int> inDegree = new();

            // initialize
            foreach (var mod in modSet)
            {
                graph[mod] = new List<string>();
                inDegree[mod] = 0;
            }

            // build graph
            foreach (var mod in modSet)
            {
                var re = ReverseEngineerRepository.Instance.GetReverseEngineer(mod);
                if (re == null) continue;

                var deps = re.getDependencies();
                if (deps == null) continue;

                foreach (var dep in deps)
                {
                    if (string.IsNullOrEmpty(dep)) continue;
                    if (baseGameMods.Contains(dep)) continue;
                    if (!modSet.Contains(dep)) continue;

                    // dep -> mod
                    graph[dep].Add(mod);
                    inDegree[mod]++;
                }
            }

            // Kahn's algorithm queue
            Queue<string> queue = new();

            foreach (var mod in modSet)
            {
                if (inDegree[mod] == 0)
                    queue.Enqueue(mod);
            }

            List<string> sorted = new();

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                sorted.Add(current);

                foreach (var next in graph[current])
                {
                    inDegree[next]--;

                    if (inDegree[next] == 0)
                        queue.Enqueue(next);
                }
            }

            // If cycle exists, append remaining mods (no crash fallback)
            foreach (var mod in modSet)
            {
                if (!sorted.Contains(mod))
                    sorted.Add(mod);
            }
            Dictionary<string, int> index = new();

            for (int i = 0; i < sorted.Count; i++)
                index[sorted[i]] = i;


           
            // overwrite input list
            modnames.Clear();
            modnames.AddRange(sorted);
        }*/
    }
}
