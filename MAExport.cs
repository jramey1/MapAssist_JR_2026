using MapAssist.Helpers;
using MapAssist.Settings;
using MapAssist.Types;
using NLog;
using NLog.Config;
using System;
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
        private bool _areaChanged;
        private bool _initialized;
        private int _suspendUpdateCount;

        private MAExport()
        {
        }

        public GameData CurrentGameData
        {
            get { return _gameData; }
        }

        public AreaData CurrentAreaData
        {
            get { return _areaData; }
        }

        public bool AreaChangedOnLastUpdate
        {
            get { return _areaChanged; }
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
            ++_suspendUpdateCount;
        }
        public void resumeUpdate()
        {
            if (_suspendUpdateCount == 0) { return; }
            --_suspendUpdateCount;
        }
        public GameData Update()
        {
            if (_suspendUpdateCount > 0)
            {
                return _gameData;
            }
            lock (_updateLock)
            {
                if (!_initialized)
                {
                    InitializeCore();
                }

                return UpdateCore();
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
            // Only the unit hash-table offset is currently known. Passing true keeps
            // GameMemory from rejecting unavailable menu/game/roster-related data.
            var result = _gameDataReader.Get();

            _gameData = result.Item1;
            _areaData = result.Item2;
            _areaChanged = result.Item3;

            return _gameData;
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
    }
}
