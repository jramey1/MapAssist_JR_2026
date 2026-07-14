using MapAssist.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MapAssist
{
    [Flags]
    public enum GameDataOffset
    {
        None = 0x00,
        UnitHashTable = 0x01,
        ExpansionCheck = 0x02,
        GameName = 0x04,
        MenuData = 0x08,
        RosterData = 0x10,
        LastHoverData = 0x20,
        InteractedNpc = 0x40,
        Pets = 0x80,
        All = 0x7FFFFFFF
    }
    public class global
    {
        public const bool RunMapOverlay = false;
        public const bool IsMAExportEnabled = true;
        public const Difficulty MAExportDifficulty = Difficulty.Normal;
        public static GameDataOffset OffsetsToPopulate = IsMAExportEnabled ? GameDataOffset.UnitHashTable | GameDataOffset.LastHoverData : GameDataOffset.All;
    }
}
