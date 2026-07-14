using PlayerClass = MapAssist.Structs.PlayerClass;
using MapAssist.Types;
using System;
using System.Collections.Generic;

namespace MapAssist.Helpers
{
    /// <summary>
    /// Supplemental checks for valuable Diablo II: Reign of the Warlock items that
    /// are not represented by the original MapAssist PlayerClass, SkillTree, Skill,
    /// or Item enums.
    ///
    /// This class deliberately supplements the normal YAML loot filter rather than
    /// replacing it. The rules are conservative and concentrate on Warlock-specific
    /// skill items and the ROTW market targets identified by the supplied reference.
    /// </summary>
    public static class RotwLootFilter
    {
        private static readonly HashSet<ushort> ClassicSkillTreeIds =
            BuildClassicSkillTreeIds();

        private static readonly string[] ExplicitRotwNames =
        {
            "hysteria",
            "obsession",
            "mania",
            "warlock hellfire torch",
            "warlock torch"
        };

        private static readonly string[] WarlockBaseNameFragments =
        {
            "warlock",
            "grimoire",
            "focus"
        };

        /// <summary>
        /// Returns true when the item matches the supplemental ROTW rules.
        /// </summary>
        public static bool IsGoodItem(UnitItem item)
        {
            string ignored;
            return IsGoodItem(item, out ignored);
        }

        /// <summary>
        /// Returns true when the item matches the supplemental ROTW rules and
        /// supplies a human-readable reason for diagnostics or UI display.
        /// </summary>
        public static bool IsGoodItem(UnitItem item, out string reason)
        {
            reason = string.Empty;

            if (item == null)
            {
                return false;
            }

            try
            {
                if (!item.IsValidItem)
                {
                    return false;
                }

                string combinedName = GetCombinedName(item);
                int sockets = SafeGetItemStat(item, Stats.Stat.NumSockets);
                int fasterCastRate = SafeGetItemStat(item, Stats.Stat.FasterCastRate);
                int fasterHitRecovery = SafeGetItemStat(item, Stats.Stat.FasterHitRecovery);
                int allResist = SafeGetAllResist(item);
                int maxLife = SafeGetShiftedItemStat(item, Stats.Stat.MaxLife);

                // The ROTW trader sheet calls out these named runewords/items.
                if (ContainsAny(combinedName, ExplicitRotwNames))
                {
                    reason = "ROTW named target: " + SafeDisplayName(item);
                    return true;
                }

                // High runes remain universally valuable and are safe to preserve in
                // a mod-aware filter even when the YAML filter predates ROTW.
                if (item.Item >= Item.VexRune && item.Item <= Item.ZodRune)
                {
                    reason = "High rune: " + item.Item;
                    return true;
                }

                int modClassSkillLayer;
                int modClassSkills = GetHighestModClassSkillBonus(
                    item,
                    out modClassSkillLayer);

                int modSkillTreeLayer;
                int modSkillTreeSkills = GetHighestModSkillTreeBonus(
                    item,
                    out modSkillTreeLayer);

                int modSingleSkillLayer;
                int modSingleSkillBonus = GetHighestUnknownSingleSkillBonus(
                    item,
                    out modSingleSkillLayer);

                int modChargedSkillId;
                if (HasUnknownChargedSkill(item, out modChargedSkillId))
                {
                    reason = "ROTW/non-classic charged skill " + modChargedSkillId;
                    return true;
                }

                // A Grand Charm with a non-classic skill-tree layer is a Warlock
                // skiller. Life and FHR variants are especially valuable, but even a
                // plain Warlock skiller is a market target in the supplied sheet.
                if (item.Item == Item.GrandCharm && modSkillTreeSkills >= 1)
                {
                    reason = "Warlock skiller (tree layer " + modSkillTreeLayer + ")";

                    if (fasterHitRecovery >= 12)
                    {
                        reason += " + " + fasterHitRecovery + "% FHR";
                    }
                    else if (maxLife >= 30)
                    {
                        reason += " + " + maxLife + " life";
                    }

                    return true;
                }

                // Hellfire Torches use +3 class skills. A Unique Large Charm with a
                // class layer beyond Assassin is therefore a strong Warlock Torch
                // discriminator without requiring MapAssist's enums to know Warlock.
                if (item.Item == Item.LargeCharm &&
                    item.ItemData.ItemQuality == ItemQuality.UNIQUE &&
                    modClassSkills >= 3)
                {
                    reason = "Warlock Hellfire Torch (class layer " +
                             modClassSkillLayer + ")";
                    return true;
                }

                // ROTW 2/10 and 2/20 caster jewelry/circlets. The supplied value sheet
                // highlights 2/20, while 2/10 is still useful enough to surface.
                if (IsAmuletOrCirclet(item.Item) &&
                    modClassSkills >= 2 &&
                    fasterCastRate >= 10)
                {
                    reason = "+" + modClassSkills + " Warlock skills / " +
                             fasterCastRate + "% FCR";
                    return true;
                }

                // Magic +3 Warlock tree/class items are the ROTW equivalents of
                // classic skill-tab shopping targets.
                if (item.ItemData.ItemQuality == ItemQuality.MAGIC &&
                    (modClassSkills >= 3 || modSkillTreeSkills >= 3))
                {
                    reason = modClassSkills >= 3
                        ? "+" + modClassSkills + " Warlock class skills"
                        : "+" + modSkillTreeSkills +
                          " Warlock skill tree (layer " + modSkillTreeLayer + ")";
                    return true;
                }

                // Warlock class bases may be unknown to the old Item enum but can
                // still be identified through localization names and raw stat layers.
                bool looksLikeWarlockBase = ContainsAny(
                    combinedName,
                    WarlockBaseNameFragments);

                if (looksLikeWarlockBase)
                {
                    if (combinedName.IndexOf("grimoire", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        sockets >= 3)
                    {
                        reason = "3-socket Warlock grimoire/offhand";
                        return true;
                    }

                    if (combinedName.IndexOf("robe", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        sockets >= 3)
                    {
                        reason = "3-socket Warlock robe base";
                        return true;
                    }

                    if (combinedName.IndexOf("focus", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        sockets >= 2 && sockets <= 4)
                    {
                        reason = sockets + "-socket Warlock focus base";
                        return true;
                    }

                    if (modClassSkills >= 2 ||
                        modSkillTreeSkills >= 2 ||
                        modSingleSkillBonus >= 3)
                    {
                        reason = "Warlock class base with useful skills";
                        return true;
                    }
                }

                // ROTW-specific base targets from the supplied trader sheet.
                if (item.Item == Item.AkaranTarge &&
                    sockets == 4 &&
                    allResist >= 40)
                {
                    reason = "4-socket Akaran Targe with " + allResist + " all resist";
                    return true;
                }

                if (item.Item == Item.ArchonStaff &&
                    item.IsEthereal &&
                    sockets == 5)
                {
                    reason = "Ethereal 5-socket Archon Staff";
                    return true;
                }

                // A non-classic single-skill layer on an otherwise recognizable
                // Warlock item is useful even when the localization name is incomplete.
                if (modSingleSkillBonus >= 3 &&
                    (modClassSkills > 0 || modSkillTreeSkills > 0))
                {
                    reason = "+" + modSingleSkillBonus +
                             " non-classic skill (layer " + modSingleSkillLayer + ")";
                    return true;
                }
            }
            catch
            {
                // A unit may disappear while the current frame is being consumed.
                // A supplemental filter must never break the normal MapAssist update.
            }

            reason = string.Empty;
            return false;
        }

        private static bool IsAmuletOrCirclet(Item item)
        {
            switch (item)
            {
                case Item.Amulet:
                case Item.Circlet:
                case Item.Coronet:
                case Item.Tiara:
                case Item.Diadem:
                    return true;

                default:
                    return false;
            }
        }

        private static int GetHighestModClassSkillBonus(
            UnitItem item,
            out int layer)
        {
            layer = -1;
            int highest = 0;
            Dictionary<ushort, int> values;

            if (item.StatLayers == null ||
                !item.StatLayers.TryGetValue(Stats.Stat.AddClassSkills, out values) ||
                values == null)
            {
                return 0;
            }

            foreach (KeyValuePair<ushort, int> value in values)
            {
                // Classic MapAssist knows class IDs 0 through 6. ROTW's Warlock is
                // represented by a new class layer beyond Assassin.
                if (value.Key <= (ushort)PlayerClass.Assassin ||
                    value.Key == ushort.MaxValue ||
                    value.Value <= highest)
                {
                    continue;
                }

                highest = value.Value;
                layer = value.Key;
            }

            return highest;
        }

        private static int GetHighestModSkillTreeBonus(
            UnitItem item,
            out int layer)
        {
            layer = -1;
            int highest = 0;
            Dictionary<ushort, int> values;

            if (item.StatLayers == null ||
                !item.StatLayers.TryGetValue(Stats.Stat.AddSkillTab, out values) ||
                values == null)
            {
                return 0;
            }

            foreach (KeyValuePair<ushort, int> value in values)
            {
                if (ClassicSkillTreeIds.Contains(value.Key) ||
                    value.Key == ushort.MaxValue ||
                    value.Value <= highest)
                {
                    continue;
                }

                highest = value.Value;
                layer = value.Key;
            }

            return highest;
        }

        private static int GetHighestUnknownSingleSkillBonus(
            UnitItem item,
            out int layer)
        {
            layer = -1;
            int highest = 0;
            Dictionary<ushort, int> values;

            if (item.StatLayers == null ||
                !item.StatLayers.TryGetValue(Stats.Stat.SingleSkill, out values) ||
                values == null)
            {
                return 0;
            }

            foreach (KeyValuePair<ushort, int> value in values)
            {
                if (value.Key <= short.MaxValue &&
                    Enum.IsDefined(typeof(Skill), (short)value.Key))
                {
                    continue;
                }

                if (value.Value <= highest)
                {
                    continue;
                }

                highest = value.Value;
                layer = value.Key;
            }

            return highest;
        }

        private static bool HasUnknownChargedSkill(
            UnitItem item,
            out int skillId)
        {
            skillId = -1;
            Dictionary<ushort, int> values;

            if (item.StatLayers == null ||
                !item.StatLayers.TryGetValue(Stats.Stat.ItemChargedSkill, out values) ||
                values == null)
            {
                return false;
            }

            foreach (KeyValuePair<ushort, int> value in values)
            {
                int currentSkillId = value.Key >> 6;
                if (currentSkillId <= short.MaxValue &&
                    Enum.IsDefined(typeof(Skill), (short)currentSkillId))
                {
                    continue;
                }

                skillId = currentSkillId;
                return true;
            }

            return false;
        }

        private static int SafeGetItemStat(UnitItem item, Stats.Stat stat)
        {
            try
            {
                return Items.GetItemStat(item, stat);
            }
            catch
            {
                return 0;
            }
        }

        private static int SafeGetShiftedItemStat(UnitItem item, Stats.Stat stat)
        {
            try
            {
                return Items.GetItemStatShifted(item, stat);
            }
            catch
            {
                return 0;
            }
        }

        private static int SafeGetAllResist(UnitItem item)
        {
            try
            {
                return Items.GetItemStatResists(item, false);
            }
            catch
            {
                return 0;
            }
        }

        private static string GetCombinedName(UnitItem item)
        {
            var names = new List<string>();

            AddName(names, SafeDisplayName(item));

            try
            {
                AddName(names, item.ItemBaseName);
            }
            catch
            {
            }

            try
            {
                AddName(names, item.Item.ToString());
            }
            catch
            {
            }

            try
            {
                if (item.IsRuneWord && item.Prefixes != null && item.Prefixes.Length > 0)
                {
                    AddName(names, Items.GetRunewordFromId(item.Prefixes[0]));
                }
            }
            catch
            {
            }

            return string.Join(" ", names.ToArray()).ToLowerInvariant();
        }

        private static string SafeDisplayName(UnitItem item)
        {
            try
            {
                string name = Items.ItemFullName(item);
                if (!string.IsNullOrWhiteSpace(name) &&
                    !string.Equals(name, "ItemNotFound", StringComparison.OrdinalIgnoreCase))
                {
                    return name;
                }
            }
            catch
            {
            }

            try
            {
                return item.ItemBaseName ?? item.Item.ToString();
            }
            catch
            {
                return "ROTW item";
            }
        }

        private static void AddName(List<string> names, string name)
        {
            if (string.IsNullOrWhiteSpace(name) ||
                string.Equals(name, "ItemNotFound", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            names.Add(name);
        }

        private static bool ContainsAny(string value, string[] candidates)
        {
            if (string.IsNullOrEmpty(value) || candidates == null)
            {
                return false;
            }

            for (int index = 0; index < candidates.Length; index++)
            {
                if (value.IndexOf(candidates[index], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static HashSet<ushort> BuildClassicSkillTreeIds()
        {
            var result = new HashSet<ushort>();
            Array values = Enum.GetValues(typeof(SkillTree));

            foreach (object value in values)
            {
                SkillTree skillTree = (SkillTree)value;
                if (skillTree == SkillTree.Any)
                {
                    continue;
                }

                result.Add((ushort)skillTree);
            }

            return result;
        }
    }
}
