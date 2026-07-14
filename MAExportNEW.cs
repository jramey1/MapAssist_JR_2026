using MapAssist.Helpers;
using MapAssist.Settings;
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
                }

                gameData = _gameData;
                return CopyUnitListNoLock();
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
            _unitList = BuildUnitList(_gameData);

            return _gameData;
        }

        /// <summary>
        /// Builds one flat, de-duplicated list from GameData's typed collections.
        /// </summary>
        private static UnitAny[] BuildUnitList(GameData gameData)
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

            return result.ToArray();
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
