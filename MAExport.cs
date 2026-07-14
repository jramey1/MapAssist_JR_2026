using MapAssist.Helpers;
using MapAssist.Settings;
using MonsterTypeFlags = MapAssist.Structs.MonsterTypeFlags;
using MapAssist.Types;
using NLog;
using NLog.Config;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace MapAssist
{
    public class MAExport
    {
        #region singleton

        private static volatile MAExport _instance;
        private static readonly object _sync = new object();

        public static MAExport instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_sync)
                    {
                        if (_instance == null)
                        {
                            _instance = new MAExport();
                        }
                    }
                }

                return _instance;
            }
        }

        #endregion

        private readonly object _updateLock = new object();

        private GameDataReader _gameDataReader;
        private GameData _gameData;
        private AreaData _areaData;
        private UnitAny[] _unitList = new UnitAny[0];
        private bool _areaChanged;
        private bool _initialized;
        private int _suspendUpdateCount;

        private MAExport()
        {
        }

        public GameData CurrentGameData
        {
            get
            {
                lock (_updateLock)
                {
                    return _gameData;
                }
            }
        }

        public AreaData CurrentAreaData
        {
            get
            {
                lock (_updateLock)
                {
                    return _areaData;
                }
            }
        }

        public bool AreaChangedOnLastUpdate
        {
            get
            {
                lock (_updateLock)
                {
                    return _areaChanged;
                }
            }
        }

        /// <summary>
        /// Returns a shallow copy of the latest flat MapAssist unit list.
        /// The array is copied so callers cannot modify MAExport's collection.
        /// </summary>
        public UnitAny[] CurrentUnitList
        {
            get
            {
                lock (_updateLock)
                {
                    return CopyUnitListNoLock();
                }
            }
        }

        public UnitAny[] GetCurrentUnitListSnapshot()
        {
            lock (_updateLock)
            {
                return CopyUnitListNoLock();
            }
        }

        public void initialize()
        {
            lock (_updateLock)
            {
                if (_initialized)
                {
                    return;
                }

                InitializeCore();
                UpdateCore();
            }
        }

        public void suspendUpdate()
        {
            lock (_updateLock)
            {
                ++_suspendUpdateCount;
            }
        }

        public void resumeUpdate()
        {
            lock (_updateLock)
            {
                if (_suspendUpdateCount == 0)
                {
                    return;
                }

                --_suspendUpdateCount;
            }
        }

        public GameData Update()
        {
            lock (_updateLock)
            {
                if (_suspendUpdateCount > 0)
                {
                    return _gameData;
                }

                if (!_initialized)
                {
                    InitializeCore();
                }

                return UpdateCore();
            }
        }

        /// <summary>
        /// Updates MapAssist and returns the flat UnitAny list from that update.
        /// </summary>
        public UnitAny[] UpdateUnitList()
        {
            GameData ignored;
            return UpdateUnitList(out ignored);
        }

        /// <summary>
        /// Updates MapAssist and returns both GameData and a copied flat UnitAny list
        /// while holding the same update lock. This prevents the two values from coming
        /// from different completed updates.
        /// </summary>
        public UnitAny[] UpdateUnitList(out GameData gameData)
        {
            lock (_updateLock)
            {
                if (_suspendUpdateCount == 0)
                {
                    if (!_initialized)
                    {
                        InitializeCore();
                    }

                    UpdateCore();

                    // The normal GameData.Monsters collection intentionally excludes
                    // NPC.Dummies entries. Direct-unit-list rendering needs those ambient
                    // units so it can distinguish non-interactable NPCs from enemies.
                    _unitList = BuildUnitList(_gameData, true);
                }

                gameData = _gameData;
                return CopyUnitListNoLock();
            }
        }

        public IEnumerable<UnitItem> getItemsInInventory()
        {
            lock (_updateLock)
            {
                if (_gameData == null || _gameData.AllItems == null)
                {
                    yield break;
                }
                foreach (UnitItem item in _gameData.AllItems)
                {
                    if (item.IsInInventoryOrCube)
                    {
                        yield return item;
                    }                    
                }
            }
        }
        public IEnumerable<UnitItem> getItemsInStash(int stashTabIndex)
        {
            lock (_updateLock)
            {
                if (_gameData == null || _gameData.AllItems == null)
                {
                    yield break;
                }
                foreach (UnitItem item in _gameData.AllItems)
                {                    
                    if ((int)item.StashTab == stashTabIndex)
                    {
                        yield return item;
                    }
                }
            }
        }
        public IEnumerable<UnitItem> getItemsInBelt()
        {
            lock (_updateLock)
            {
                if (_gameData == null || _gameData.AllItems == null)
                {
                    yield break;
                }
                foreach (UnitItem item in _gameData.AllItems)
                {
                    if (item.ItemMode == ItemMode.INBELT)
                    {
                        yield return item;
                    }
                }
            }
        }
        /// <summary>
        /// Returns a snapshot of the current hostile monsters. Town NPCs,
        /// mercenaries, and summons are excluded.
        /// </summary>
        public IEnumerable<UnitMonster> getEnemies()
        {
            lock (_updateLock)
            {
                return GetEnemiesNoLock(MonsterTypeFlags.None);
            }
        }

        /// <summary>
        /// Returns a snapshot of the current hostile monsters matching any of the
        /// requested MonsterTypeFlags. Passing None returns all enemies.
        ///
        /// Other matches normal enemies that have no special monster classification.
        /// The raw MonsterData flags are used so Possessed, Ghostly, and Multishot
        /// can also be queried.
        /// </summary>
        public IEnumerable<UnitMonster> getEnemies(MonsterTypeFlags flags)
        {
            lock (_updateLock)
            {
                return GetEnemiesNoLock(flags);
            }
        }

        /// <summary>
        /// Gets the hostile monster nearest to the current player.
        /// </summary>
        public bool getClosestEnemy(out UnitMonster unitMonster)
        {
            lock (_updateLock)
            {
                return TryGetClosestEnemyNoLock(
                    MonsterTypeFlags.None,
                    out unitMonster);
            }
        }

        /// <summary>
        /// Gets the nearest hostile monster matching any of the requested flags.
        /// Passing None searches all enemies.
        /// </summary>
        public bool getClosestEnemy(
            MonsterTypeFlags flags,
            out UnitMonster unitMonster)
        {
            lock (_updateLock)
            {
                return TryGetClosestEnemyNoLock(flags, out unitMonster);
            }
        }

        /// <summary>
        /// Gets the nearest currently loaded interactable town NPC with the requested
        /// Npc identifier. Interactable NPCs are represented by UnitMonster in
        /// MapAssist.
        /// </summary>
        public bool getInteractableNPC(Npc npc, out UnitMonster unitNpc)
        {
            lock (_updateLock)
            {
                unitNpc = null;

                if (_gameData == null ||
                    _gameData.Monsters == null ||
                    !NpcExtensions.IsTownsfolk(npc))
                {
                    return false;
                }

                UnitPlayer player = _gameData.PlayerUnit;
                var closestDistance = double.MaxValue;

                foreach (UnitMonster monster in _gameData.Monsters)
                {
                    if (monster == null || monster.Npc != npc)
                    {
                        continue;
                    }

                    // Keep this check even though the requested enum value was checked
                    // above. It protects against future changes to the townsfolk list.
                    if (!NpcExtensions.IsTownsfolk(monster.Npc))
                    {
                        continue;
                    }

                    if (player == null)
                    {
                        unitNpc = monster;
                        return true;
                    }

                    double distance;
                    if (!TryGetDistance(monster, player, out distance) ||
                        distance >= closestDistance)
                    {
                        continue;
                    }

                    closestDistance = distance;
                    unitNpc = monster;
                }

                return unitNpc != null;
            }
        }

        /// <summary>
        /// Returns a snapshot of the current valid items whose mapped item mode is
        /// Ground. Ground items are UnitItem instances, not UnitObject instances.
        /// </summary>
        public IEnumerable<UnitItem> getItemsOnGround()
        {
            lock (_updateLock)
            {
                if (_gameData == null || _gameData.AllItems == null)
                {
                    return new UnitItem[0];
                }

                var result = new List<UnitItem>();

                foreach (UnitItem item in _gameData.AllItems)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    try
                    {
                        if (item.IsValidItem && item.IsDropped)
                        {
                            result.Add(item);
                        }
                    }
                    catch
                    {
                        // A unit can disappear between completed MapAssist frames.
                    }
                }

                return result.ToArray();
            }
        }


        /// <summary>
        /// Checks one item against the complete MapAssist loot filter, including the
        /// supplemental ROTW checks added in RotwLootFilter.
        ///
        /// This method uses the latest completed MAExport snapshot; it does not trigger
        /// another game-memory update.
        /// </summary>
        public bool itemMatchesLootFilter(UnitItem item)
        {
            ItemFilter ignoredRule;
            string ignoredReason;
            return itemMatchesLootFilter(
                item,
                out ignoredRule,
                out ignoredReason);
        }

        /// <summary>
        /// Checks one item against the complete loot filter and returns the configured
        /// ItemFilter rule when the match came from LootLogConfiguration. A ROTW
        /// supplemental match has a null matchedRule.
        /// </summary>
        public bool itemMatchesLootFilter(
            UnitItem item,
            out ItemFilter matchedRule)
        {
            string ignoredReason;
            return itemMatchesLootFilter(
                item,
                out matchedRule,
                out ignoredReason);
        }

        /// <summary>
        /// Checks one item against the configured MapAssist rules and the supplemental
        /// ROTW rules. matchReason identifies which path accepted the item.
        /// </summary>
        public bool itemMatchesLootFilter(
            UnitItem item,
            out ItemFilter matchedRule,
            out string matchReason)
        {
            lock (_updateLock)
            {
                return ItemMatchesLootFilterNoLock(
                    item,
                    out matchedRule,
                    out matchReason);
            }
        }

        /// <summary>
        /// Checks only the supplemental ROTW rules. This is useful for debugging or
        /// displaying why an item was retained even though no YAML ItemFilter matched.
        /// </summary>
        public bool isGoodRotwItem(UnitItem item, out string reason)
        {
            lock (_updateLock)
            {
                return RotwLootFilter.IsGoodItem(item, out reason);
            }
        }

        /// <summary>
        /// Returns a snapshot containing only current ground items accepted by either
        /// the configured MapAssist loot filter or the supplemental ROTW filter.
        /// </summary>
        public IEnumerable<UnitItem> getItemsOnGroundMatchingLootFilter()
        {
            lock (_updateLock)
            {
                if (_gameData == null || _gameData.AllItems == null)
                {
                    return new UnitItem[0];
                }

                var result = new List<UnitItem>();

                foreach (UnitItem item in _gameData.AllItems)
                {
                    if (!IsValidGroundItem(item))
                    {
                        continue;
                    }

                    ItemFilter matchedRule;
                    string matchReason;
                    if (ItemMatchesLootFilterNoLock(
                        item,
                        out matchedRule,
                        out matchReason))
                    {
                        result.Add(item);
                    }
                }

                return result.ToArray();
            }
        }

        /// <summary>
        /// Returns only current ground items accepted by the supplemental ROTW rules.
        /// </summary>
        public IEnumerable<UnitItem> getGoodRotwItemsOnGround()
        {
            lock (_updateLock)
            {
                if (_gameData == null || _gameData.AllItems == null)
                {
                    return new UnitItem[0];
                }

                var result = new List<UnitItem>();

                foreach (UnitItem item in _gameData.AllItems)
                {
                    if (!IsValidGroundItem(item))
                    {
                        continue;
                    }

                    string reason;
                    if (RotwLootFilter.IsGoodItem(item, out reason))
                    {
                        result.Add(item);
                    }
                }

                return result.ToArray();
            }
        }

        /// <summary>
        /// Gets the currently loaded stash object. The stash is represented by
        /// UnitObject and identified by UnitObject.IsStash.
        /// </summary>
        public bool getStash(out UnitObject unitStash)
        {
            lock (_updateLock)
            {
                unitStash = null;

                if (_gameData == null || _gameData.Objects == null)
                {
                    return false;
                }

                foreach (UnitObject unitObject in _gameData.Objects)
                {
                    if (unitObject == null)
                    {
                        continue;
                    }

                    try
                    {
                        if (unitObject.IsStash)
                        {
                            unitStash = unitObject;
                            return true;
                        }
                    }
                    catch
                    {
                        // Skip a unit that became invalid between completed frames.
                    }
                }

                return false;
            }
        }
        /// <summary>
        /// Gets the currently loaded stash object. The stash is represented by
        /// UnitObject and identified by UnitObject.IsStash.
        /// </summary>
        public bool getPortal(out UnitObject unitPortal)
        {
            lock (_updateLock)
            {
                unitPortal = null;

                if (_gameData == null || _gameData.Objects == null)
                {
                    return false;
                }

                foreach (UnitObject unitObject in _gameData.Objects)
                {
                    if (unitObject == null)
                    {
                        continue;
                    }

                    try
                    {
                        if (unitObject.IsPortal)
                        {
                            unitPortal = unitObject;
                            return true;
                        }
                    }
                    catch
                    {
                        // Skip a unit that became invalid between completed frames.
                    }
                }

                return false;
            }
        }

        private bool ItemMatchesLootFilterNoLock(
            UnitItem item,
            out ItemFilter matchedRule,
            out string matchReason)
        {
            matchedRule = null;
            matchReason = string.Empty;

            if (item == null || _gameData == null)
            {
                return false;
            }

            var areaLevel = 0;
            var playerLevel = 0;

            try
            {
                // This matches the supplied GameMemory.cs, which intentionally uses
                // Hell area levels for the mod's loot-filter evaluation.
                areaLevel = _gameData.Area.Level(Difficulty.Hell);
            }
            catch
            {
                // Area level requirements will naturally fail if no area is available.
            }

            try
            {
                if (_gameData.PlayerUnit != null)
                {
                    playerLevel = _gameData.PlayerUnit.Level;
                }
            }
            catch
            {
            }

            try
            {
                return LootFilter.Matches(
                    item,
                    areaLevel,
                    playerLevel,
                    out matchedRule,
                    out matchReason);
            }
            catch
            {
                matchedRule = null;
                matchReason = string.Empty;
                return false;
            }
        }

        private static bool IsValidGroundItem(UnitItem item)
        {
            if (item == null)
            {
                return false;
            }

            try
            {
                return item.IsValidItem && item.IsDropped;
            }
            catch
            {
                return false;
            }
        }

        private void InitializeCore()
        {
            LoadLoggingConfiguration();
            LoadMainConfiguration();
            LoadLootLogConfiguration();

            Process[] d2rProcesses = Process.GetProcessesByName("D2R");
            if (d2rProcesses.Length == 0)
            {
                throw new InvalidOperationException("D2R is not running.");
            }

            GameManager.SetActiveWindow(d2rProcesses[0].MainWindowHandle);
            _gameDataReader = new GameDataReader();
            _initialized = true;
        }

        private GameData UpdateCore()
        {
            var result = _gameDataReader.Get();

            _gameData = result.Item1;
            _areaData = result.Item2;
            _areaChanged = result.Item3;
            _unitList = BuildUnitList(_gameData, false);

            return _gameData;
        }

        /// <summary>
        /// Builds one flat, de-duplicated list from GameData's typed collections.
        /// </summary>
        private static UnitAny[] BuildUnitList(
            GameData gameData,
            bool includeAdditionalRawMonsterUnits)
        {
            if (gameData == null)
            {
                return new UnitAny[0];
            }

            var result = new List<UnitAny>();
            var addedUnits = new HashSet<UnitKey>();

            AddUnit(result, addedUnits, gameData.PlayerUnit);

            if (gameData.Players != null)
            {
                AddUnits(result, addedUnits, gameData.Players.Values);
            }

            AddUnits(result, addedUnits, gameData.Corpses);
            AddUnits(result, addedUnits, gameData.Mercs);
            AddUnits(result, addedUnits, gameData.Summons);
            AddUnits(result, addedUnits, gameData.Monsters);
            AddUnits(result, addedUnits, gameData.Objects);
            AddUnits(result, addedUnits, gameData.Missiles);
            AddUnits(result, addedUnits, gameData.AllItems);

            if (includeAdditionalRawMonsterUnits)
            {
                AddAdditionalRawMonsterUnits(result, addedUnits);
            }

            return result.ToArray();
        }

        /// <summary>
        /// Reads the monster hash table and appends units filtered out of
        /// GameData.Monsters, especially NPC.Dummies ambient actors. Units already
        /// present through GameData remain authoritative and are de-duplicated.
        /// </summary>
        private static void AddAdditionalRawMonsterUnits(
            List<UnitAny> result,
            HashSet<UnitKey> addedUnits)
        {
            var unitHashTable = GameManager.UnitHashTable(
                128 * 8 * (int)UnitType.Monster);

            foreach (IntPtr firstUnitPointer in unitHashTable.UnitTable)
            {
                IntPtr unitPointer = firstUnitPointer;
                var visitedPointers = new HashSet<IntPtr>();

                while (unitPointer != IntPtr.Zero && visitedPointers.Add(unitPointer))
                {
                    var unit = new UnitMonster(unitPointer);
                    if (!unit.IsValidUnit)
                    {
                        break;
                    }

                    IntPtr nextUnitPointer = unit.Struct.pListNext;

                    try
                    {
                        UnitMonster updatedUnit = unit.Update();
                        if (updatedUnit != null)
                        {
                            AddUnit(result, addedUnits, updatedUnit);
                        }
                    }
                    catch
                    {
                        // A linked-list unit may disappear during the extra export pass.
                        // Skip that record and continue with the pointer captured above.
                    }

                    unitPointer = nextUnitPointer;
                }
            }
        }

        private static void AddUnits<T>(
            List<UnitAny> result,
            HashSet<UnitKey> addedUnits,
            IEnumerable<T> units)
            where T : UnitAny
        {
            if (units == null)
            {
                return;
            }

            foreach (T unit in units)
            {
                AddUnit(result, addedUnits, unit);
            }
        }

        private static void AddUnit(
            List<UnitAny> result,
            HashSet<UnitKey> addedUnits,
            UnitAny unit)
        {
            if (unit == null ||
                unit.PtrUnit == IntPtr.Zero ||
                unit.UnitId == uint.MaxValue)
            {
                return;
            }

            var key = new UnitKey(unit.UnitType, unit.UnitId);
            if (!addedUnits.Add(key))
            {
                return;
            }

            result.Add(unit);
        }

        private UnitMonster[] GetEnemiesNoLock(MonsterTypeFlags flags)
        {
            if (_gameData == null || _gameData.Monsters == null)
            {
                return new UnitMonster[0];
            }

            var result = new List<UnitMonster>();

            foreach (UnitMonster monster in _gameData.Monsters)
            {
                if (!IsEnemy(monster) ||
                    !MatchesMonsterTypeFlags(monster, flags))
                {
                    continue;
                }

                result.Add(monster);
            }

            return result.ToArray();
        }

        private bool TryGetClosestEnemyNoLock(
            MonsterTypeFlags flags,
            out UnitMonster unitMonster)
        {
            unitMonster = null;

            if (_gameData == null || _gameData.PlayerUnit == null)
            {
                return false;
            }

            UnitPlayer player = _gameData.PlayerUnit;
            UnitMonster[] enemies = GetEnemiesNoLock(flags);
            var closestDistance = double.MaxValue;

            foreach (UnitMonster enemy in enemies)
            {
                double distance;
                if (!TryGetDistance(enemy, player, out distance) ||
                    distance >= closestDistance)
                {
                    continue;
                }

                closestDistance = distance;
                unitMonster = enemy;
            }

            return unitMonster != null;
        }

        private static bool IsEnemy(UnitMonster monster)
        {
            if (monster == null ||
                monster.PtrUnit == IntPtr.Zero ||
                monster.UnitId == uint.MaxValue ||
                monster.UnitType != UnitType.Monster)
            {
                return false;
            }

            try
            {
                if (monster.IsMerc || monster.IsSummon)
                {
                    return false;
                }

                if (NpcExtensions.IsTownsfolk(monster.Npc))
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// MonsterTypeFlags is a flags enum. A monster is included when it matches
        /// any requested flag. Other is treated specially because UnitMonster
        /// synthesizes Other when no special classification is present.
        /// </summary>
        private static bool MatchesMonsterTypeFlags(
            UnitMonster monster,
            MonsterTypeFlags flags)
        {
            if (flags == MonsterTypeFlags.None)
            {
                return true;
            }

            try
            {
                if ((flags & MonsterTypeFlags.Other) == MonsterTypeFlags.Other &&
                    monster.MonsterType == MonsterTypeFlags.Other)
                {
                    return true;
                }

                if ((flags & MonsterTypeFlags.SuperUnique) == MonsterTypeFlags.SuperUnique &&
                    monster.IsSuperUnique)
                {
                    return true;
                }

                MonsterTypeFlags directlyStoredFlags =
                    MonsterTypeFlags.Champion |
                    MonsterTypeFlags.Unique |
                    MonsterTypeFlags.Minion |
                    MonsterTypeFlags.Possessed |
                    MonsterTypeFlags.Ghostly |
                    MonsterTypeFlags.Multishot;

                MonsterTypeFlags requestedStoredFlags = flags & directlyStoredFlags;
                return requestedStoredFlags != MonsterTypeFlags.None &&
                       (monster.MonsterData.MonsterType & requestedStoredFlags) !=
                       MonsterTypeFlags.None;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetDistance(
            UnitAny first,
            UnitAny second,
            out double distance)
        {
            distance = double.MaxValue;

            if (first == null || second == null)
            {
                return false;
            }

            try
            {
                distance = first.DistanceTo(second);
                return !double.IsNaN(distance) &&
                       !double.IsInfinity(distance);
            }
            catch
            {
                return false;
            }
        }

        private UnitAny[] CopyUnitListNoLock()
        {
            var copy = new UnitAny[_unitList.Length];
            Array.Copy(_unitList, copy, _unitList.Length);
            return copy;
        }

        private static void LoadMainConfiguration()
        {
            MapAssistConfiguration.Load();
            MapAssistConfiguration.Loaded.RenderingConfiguration.InitialSize =
                MapAssistConfiguration.Loaded.RenderingConfiguration.Size;
        }

        private static void LoadLootLogConfiguration()
        {
            LootLogConfiguration.Load();
        }

        private static void LoadLoggingConfiguration()
        {
            ConfigurationItemFactory.Default.LayoutRenderers.RegisterDefinition(
                "InvariantCulture",
                typeof(InvariantCultureLayoutRendererWrapper));

            var config = new LoggingConfiguration();

            var logfile = new NLog.Targets.FileTarget("logfile")
            {
                FileName = "logs\\log.txt",
                CreateDirs = true,
                ArchiveNumbering = NLog.Targets.ArchiveNumberingMode.DateAndSequence,
                ArchiveOldFileOnStartup = true,
                MaxArchiveFiles = 5,
                Encoding = System.Text.Encoding.UTF8,
                Layout = NLog.Layouts.Layout.FromString(
                    "${longdate}|${level:uppercase=true}|${logger}|${InvariantCulture:${message:withexception=true}}")
            };

            var logconsole = new NLog.Targets.ConsoleTarget("logconsole");

            config.AddRule(LogLevel.Debug, LogLevel.Fatal, logconsole);
            config.AddRule(LogLevel.Info, LogLevel.Fatal, logfile);

            LogManager.Configuration = config;
        }

        private struct UnitKey : IEquatable<UnitKey>
        {
            public UnitKey(UnitType unitType, uint unitId)
            {
                UnitType = unitType;
                UnitId = unitId;
            }

            private UnitType UnitType;
            private uint UnitId;

            public bool Equals(UnitKey other)
            {
                return UnitType == other.UnitType && UnitId == other.UnitId;
            }

            public override bool Equals(object obj)
            {
                return obj is UnitKey && Equals((UnitKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((int)UnitType * 397) ^ (int)UnitId;
                }
            }
        }
    }
}
