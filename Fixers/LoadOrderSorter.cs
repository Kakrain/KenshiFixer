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
        //private const int KENSHIFIXER_BRIDGE = 0;
        private const int KENSHIFIXER_END = -2;
        private const int PATCH = -3;
        private const int INVALID = -1000;
        private string _kenshiFixerModName;
        //private string _kenshiFixerBridgeModName;
        private Dictionary<string, int> counts=new Dictionary<string, int>();
        public LoadOrderSorter(string kenshiFixerModName)//, string kenshiFixerBridgeModName)
        {
            _kenshiFixerModName = kenshiFixerModName;
            //_kenshiFixerBridgeModName = kenshiFixerBridgeModName;
        }
        private List<string> GetSortableMods(List<string> modnames)
        {
            return modnames
                .Where(m => counts.TryGetValue(m, out int c) && c >= 1)
                .ToList();
        }
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
        }
        /*public void ApplyDirectDependencySort(List<string> modnames)
        {
            // STEP 1: BUILD DEP GRAPH
            Dictionary<string, List<string>> deps = new();
            Dictionary<string, List<string>> reverse = new();
            Dictionary<string, int> inDegree = new();

            foreach (var mod in modnames)
            {
                var re = ReverseEngineerRepository.Instance.GetReverseEngineer(mod);

                var dependencies = re!.getDependencies() ?? new List<string>();

                deps[mod] = dependencies;

                if (!inDegree.ContainsKey(mod))
                    inDegree[mod] = 0;

                foreach (var dep in dependencies)
                {
                    // mod depends on dep => edge dep → mod

                    if (!reverse.ContainsKey(dep))
                        reverse[dep] = new List<string>();

                    reverse[dep].Add(mod);

                    if (!inDegree.ContainsKey(mod))
                        inDegree[mod] = 0;

                    inDegree[mod]++;
                }
            }

            // STEP 2: INITIAL QUEUE (NO DEPENDENCIES FIRST)
            Queue<string> queue = new();

            foreach (var mod in modnames)
            {
                if (!inDegree.ContainsKey(mod) || inDegree[mod] == 0)
                    queue.Enqueue(mod);
            }

            // STEP 3: TOPOLOGICAL SORT
            List<string> result = new();

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                result.Add(current);

                if (!reverse.TryGetValue(current, out var dependents))
                    continue;

                foreach (var dep in dependents)
                {
                    inDegree[dep]--;

                    if (inDegree[dep] == 0)
                        queue.Enqueue(dep);
                }
            }

            // STEP 4: CYCLE HANDLING (fallback)
            foreach (var mod in modnames)
            {
                if (!result.Contains(mod))
                    result.Add(mod);
            }

            // STEP 5: APPLY ORDER BACK
            modnames.Clear();
            modnames.AddRange(result);
        }*/

        /*public void ApplyDirectDependencySort(List<string> modnames)//funciona bien.
        {
            // -----------------------------
            // 1. FILTER BASE GAME MODS
            // -----------------------------
            HashSet<string> baseMods = new(ModRepository.Instance.BaseGameMods);

            List<string> mods = modnames;

            // -----------------------------
            // 2. BUILD DEPENDENCY GRAPH
            // -----------------------------
            Dictionary<string, HashSet<string>> dependsOn = new();
            Dictionary<string, List<string>> dependents = new();
            Dictionary<string, int> inDegree = new();

            foreach (var mod in mods)
            {
                var re = ReverseEngineerRepository.Instance.GetReverseEngineer(mod);

                dependsOn[mod] = new HashSet<string>();
                dependents[mod] = new List<string>();
                inDegree[mod] = 0;

                var deps = re?.getDependencies();

                if (deps != null)
                {
                    foreach (var dep in deps)
                    {
                        if (string.IsNullOrWhiteSpace(dep))
                            continue;

                        // ignore base game mods
                        if (baseMods.Contains(dep))
                            continue;

                        // ignore unknown mods (not in current list)
                        if (!mods.Contains(dep))
                            continue;

                        dependsOn[mod].Add(dep);
                    }
                }
            }

            // -----------------------------
            // 3. BUILD REVERSE EDGES + IN-DEGREE
            // -----------------------------
            foreach (var mod in mods)
            {
                foreach (var dep in dependsOn[mod])
                {
                    dependents[dep].Add(mod);
                    inDegree[mod]++;
                }
            }

            // -----------------------------
            // 4. TOPOLOGICAL SORT (KAHN)
            // -----------------------------
            Queue<string> queue = new();

            foreach (var mod in mods)
            {
                if (inDegree[mod] == 0)
                    queue.Enqueue(mod);
            }

            List<string> sorted = new();

            while (queue.Count > 0)
            {
                var mod = queue.Dequeue();
                sorted.Add(mod);

                foreach (var child in dependents[mod])
                {
                    inDegree[child]--;

                    if (inDegree[child] == 0)
                        queue.Enqueue(child);
                }
            }

            // -----------------------------
            // 5. CYCLE FALLBACK (IMPORTANT)
            // -----------------------------
            if (sorted.Count != mods.Count)
            {
                var remaining = mods
                    .Where(m => !sorted.Contains(m))
                    .ToList();

                // append unresolved mods at end (no crash, no infinite loop)
                sorted.AddRange(remaining);
            }

            // -----------------------------
            // 6. APPLY RESULT BACK
            // -----------------------------
            modnames.Clear();
            modnames.AddRange(sorted);
        }*/
        /*public void ApplyDirectDependencySort(List<string> modnames)
        {
            HashSet<string> modSet = new(modnames);
            HashSet<string> baseGameMods = new(ModRepository.Instance.BaseGameMods);

            // Build dependency graph (A depends on B => edge A -> B)
            Dictionary<string, HashSet<string>> dependsOn = new();

            // Initialize all mods
            foreach (var mod in modnames)
            {
                dependsOn[mod] = new HashSet<string>();

                var re = ReverseEngineerRepository.Instance.GetReverseEngineer(mod);
                var deps = re?.getDependencies();

                if (deps == null) continue;

                foreach (var dep in deps)
                {
                    if (string.IsNullOrWhiteSpace(dep))
                        continue;

                    // ignore base game mods
                    if (baseGameMods.Contains(dep))
                        continue;

                    // ignore dependencies not present in mod list
                    if (!modSet.Contains(dep))
                        continue;

                    dependsOn[mod].Add(dep);
                }
            }

            // -----------------------------
            // Kahn's algorithm (topological sort)
            // -----------------------------
            Dictionary<string, int> inDegree = new();

            foreach (var mod in modnames)
                inDegree[mod] = 0;

            foreach (var mod in modnames)
            {
                foreach (var dep in dependsOn[mod])
                {
                    inDegree[dep]++;
                }
            }

            Queue<string> queue = new();

            foreach (var mod in modnames)
            {
                if (inDegree[mod] == 0)
                    queue.Enqueue(mod);
            }

            List<string> result = new();

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                result.Add(current);

                foreach (var dep in dependsOn[current])
                {
                    inDegree[dep]--;

                    if (inDegree[dep] == 0)
                        queue.Enqueue(dep);
                }
            }

            // If cycle exists, append remaining nodes (fallback)
            foreach (var mod in modnames)
            {
                if (!result.Contains(mod))
                    result.Add(mod);
            }

            // overwrite input list
            modnames.Clear();
            modnames.AddRange(result);
        }*/
        public void ApplyDirectDependencySort(List<string> modnames)
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
                /*var ready = modSet.Where(m => inDegree[m] == 0).OrderBy(m => conflictScore[m]).ThenBy(m => modnames.IndexOf(m)).ToList();

                foreach (var m in ready)
                    queue.Enqueue(m);*/
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

            // Repair pass (single sweep, no oscillation)(not necesary for now)
            /*bool changed;

            do
            {
                changed = false;

                for (int i = 0; i < sorted.Count; i++)
                {
                    var mod = sorted[i];

                    var re = ReverseEngineerRepository.Instance.GetReverseEngineer(mod);
                    var deps = re?.getDependencies();

                    if (deps == null) continue;

                    foreach (var dep in deps)
                    {
                        if (!index.ContainsKey(dep))
                            continue;
                        if (ModRepository.Instance.BaseGameMods.Contains(dep))
                            continue;
                        int modIndex = index[mod];
                        int depIndex = index[dep];

                        // VIOLATION: dependency comes AFTER dependent
                        if (depIndex > modIndex)
                        {
                            // move dependency before mod (minimal correction)
                            sorted.RemoveAt(depIndex);

                            int insertIndex = index[mod];

                            sorted.Insert(insertIndex, dep);

                            // rebuild index after modification
                            for (int j = 0; j < sorted.Count; j++)
                                index[sorted[j]] = j;

                            changed = true;
                            break;
                        }
                    }

                    if (changed)
                        break;
                }

            } while (changed);*/


            // overwrite input list
            modnames.Clear();
            modnames.AddRange(sorted);
        }
        public void ApplyMinimumConflictSort(List<string> modnames)
        {
            Dictionary<string, HashSet<string>> newOwners = new();
            Dictionary<string, HashSet<string>> oldOwners = new();
            Dictionary<string, int> score = new();

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
                    set.Add(mod);
                }
            }

            HashSet<string> allIds = new();
            allIds.UnionWith(newOwners.Keys);
            allIds.UnionWith(oldOwners.Keys);

            foreach (var id in allIds)
            {
                newOwners.TryGetValue(id, out var newMods);
                oldOwners.TryGetValue(id, out var oldMods);

                newMods ??= new HashSet<string>();
                oldMods ??= new HashSet<string>();

                if (newMods.Count > 1)
                    foreach (var m in newMods)
                        AddScore(score, m, 3);

                if (newMods.Count > 0 && oldMods.Count > 0)
                {
                    foreach (var m in newMods)
                        AddScore(score, m, 2);

                    foreach (var m in oldMods)
                        AddScore(score, m, 2);
                }

                if (oldMods.Count > 1)
                    foreach (var m in oldMods)
                        AddScore(score, m, 1);
            }
            modnames.Sort((a, b) =>score.GetValueOrDefault(a).CompareTo(score.GetValueOrDefault(b)));
            //modnames.Sort((a, b) => counts[b].CompareTo(counts[a]));
            //var sorted = modnames
            //  .OrderBy(m => score.ContainsKey(m) ? score[m] : 0)
            //  .ToList();
        }

        void AddScore(Dictionary<string, int> score, string mod, int value)
        {
            if (!score.ContainsKey(mod))
                score[mod] = 0;

            score[mod] += value;
        }
        public void ApplySortToCreators(List<string> modnames,Action<List<string>> sortingStrategy)
        {
            var sortable = GetSortableMods(modnames);

            if (sortable.Count <= 1)
                return;

            sortingStrategy(sortable);

            int i = 0;

            for (int j = 0; j < modnames.Count; j++)
            {
                if (counts.TryGetValue(modnames[j], out int c) && c >= 1)
                {
                    modnames[j] = sortable[i++];
                }
            }
        }

        public void ApplyNewestSort(List<string> modnames)
        {
            counts = modnames.ToDictionary(m => m, m => {

                if (m == _kenshiFixerModName+".mod")
                    return KENSHIFIXER_END;
                //if(m == _kenshiFixerBridgeModName + ".mod")
                 //return KENSHIFIXER_BRIDGE;
                ModRepository.Instance.Mods.TryGetValue(m, out var mod);
                if(mod == null)
                {
                    CoreUtils.Print($"Mod {m} not found in repository, treating as invalid for load order sorting.");
                    return INVALID;
                }
                if (CoreUtils.isModAPatch(mod))
                    return PATCH;
                ReverseEngineer? re = ReverseEngineerRepository.Instance.GetReverseEngineer(m);
                int count= re?.GetStringIdsNewRecords().Count() ?? 0;
                if(count == 0)
                {
                    return -1;
                }
                return count;
            });

            modnames.Sort((a, b) => counts[b].CompareTo(counts[a]));
        }
    }
}
