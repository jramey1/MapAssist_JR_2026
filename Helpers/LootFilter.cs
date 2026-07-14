using MapAssist.Settings;
using MapAssist.Types;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MapAssist.Helpers
{
    public static class LootFilter
    {
        /// <summary>
        /// Existing MapAssist-compatible entry point.
        /// </summary>
        public static (bool, ItemFilter) Filter(
            UnitItem item,
            int areaLevel,
            int playerLevel)
        {
            ItemFilter matchedRule;
            string matchReason;
            var matched = Matches(
                item,
                areaLevel,
                playerLevel,
                out matchedRule,
                out matchReason);

            return (matched, matchedRule);
        }

        /// <summary>
        /// Detailed entry point for MAExport and diagnostics. It evaluates the normal
        /// MapAssist YAML rules first, then the supplemental ROTW rules.
        /// </summary>
        public static bool Matches(
            UnitItem item,
            int areaLevel,
            int playerLevel,
            out ItemFilter matchedRule,
            out string matchReason)
        {
            matchedRule = null;
            matchReason = string.Empty;

            if (item == null)
            {
                return false;
            }

            // Skip low-quality items. The supplemental ROTW filter intentionally does
            // not promote cracked/crude/damaged bases.
            var lowQuality =
                (item.ItemData.ItemFlags & ItemFlags.IFLAG_LOWQUALITY) ==
                ItemFlags.IFLAG_LOWQUALITY;

            if (lowQuality)
            {
                matchReason = "Rejected: low-quality item";
                return false;
            }

            // Populate a list of filter rules by combining rules from Any and the item
            // base name. This preserves MapAssist's original behavior.
            var matches = LootLogConfiguration.Filters
                .Where(f => f.Key == Item.Any || (uint)f.Key == item.TxtFileNo)
                .ToList();

            // A null rule means the item/base entry itself is sufficient.
            if (matches.Any(kv => kv.Value == null))
            {
                var result = !item.IsAnyPlayerHolding;
                if (result)
                {
                    matchReason = "Configured loot-filter item/base entry";
                }

                return result;
            }

            // Scan the configured YAML rules first.
            foreach (var rule in matches.SelectMany(kv => kv.Value))
            {
                // Skip generic unidentified rules for identified items on ground or
                // in inventory.
                if (item.IsIdentified &&
                    (item.IsDropped || item.IsAnyPlayerHolding) &&
                    rule.TargetsUnidItem())
                {
                    continue;
                }

                if (item.IsInStore && !rule.CheckVendor)
                {
                    continue;
                }

                var requirementsFunctions = new Dictionary<string, Func<bool>>()
                {
                    ["Qualities"] = () => rule.Qualities.Contains(item.ItemData.ItemQuality),
                    ["Sockets"] = () => rule.Sockets.Contains(Items.GetItemStat(item, Stats.Stat.NumSockets)),
                    ["Ethereal"] = () => item.IsEthereal == rule.Ethereal,
                    ["MinAreaLevel"] = () => areaLevel >= rule.MinAreaLevel,
                    ["MaxAreaLevel"] = () => areaLevel <= rule.MaxAreaLevel,
                    ["MinPlayerLevel"] = () => playerLevel >= rule.MinPlayerLevel,
                    ["MaxPlayerLevel"] = () => playerLevel <= rule.MaxPlayerLevel,
                    ["MinQualityLevel"] = () => Items.GetQualityLevel(item) >= rule.MinQualityLevel,
                    ["MaxQualityLevel"] = () => Items.GetQualityLevel(item) <= rule.MaxQualityLevel,
                    ["AllAttributes"] = () => Items.GetItemStatAllAttributes(item) >= rule.AllAttributes,
                    ["AllResist"] = () => Items.GetItemStatResists(item, false) >= rule.AllResist,
                    ["SumResist"] = () => Items.GetItemStatResists(item, true) >= rule.SumResist,
                    ["ClassSkills"] = () =>
                    {
                        if (rule.ClassSkills.Count() == 0) return true;
                        return rule.ClassSkills.All(subrule =>
                            Items.GetItemStatAddClassSkills(item, subrule.Key).Item2 >=
                            subrule.Value);
                    },
                    ["SkillTrees"] = () =>
                    {
                        if (rule.SkillTrees.Count() == 0) return true;
                        return rule.SkillTrees.All(subrule =>
                            Items.GetItemStatAddSkillTreeSkills(item, subrule.Key).Item2 >=
                            subrule.Value);
                    },
                    ["Skills"] = () =>
                    {
                        if (rule.Skills.Count() == 0) return true;
                        return rule.Skills.All(subrule =>
                            Items.GetItemStatAddSingleSkills(item, subrule.Key).Item2 >=
                            subrule.Value);
                    },
                    ["SkillCharges"] = () =>
                    {
                        if (rule.SkillCharges.Count() == 0) return true;
                        return rule.SkillCharges.All(subrule =>
                            Items.GetItemStatAddSkillCharges(item, subrule.Key).Item1 >=
                            subrule.Value);
                    },
                };

                foreach (var statAndShift in Stats.StatShifts)
                {
                    Stats.Stat capturedStat = statAndShift.Key;
                    requirementsFunctions.Add(
                        capturedStat.ToString(),
                        () => Items.GetItemStatShifted(item, capturedStat) >=
                              (int)rule[capturedStat]);
                }

                var requirementMet = true;
                foreach (var property in rule.GetType().GetProperties())
                {
                    if (property.PropertyType == typeof(object))
                    {
                        continue;
                    }

                    var propertyValue = property.GetValue(rule, null);
                    if (propertyValue == null)
                    {
                        continue;
                    }

                    Func<bool> requirementFunc;
                    if (requirementsFunctions.TryGetValue(
                        property.Name,
                        out requirementFunc))
                    {
                        requirementMet &= requirementFunc();
                    }
                    else
                    {
                        Stats.Stat stat;
                        if (Enum.TryParse(property.Name, out stat))
                        {
                            requirementMet &= Stats.NegativeValueStats.Contains(stat)
                                ? (int)propertyValue < 0 &&
                                  Items.GetItemStat(item, stat) <= (int)propertyValue
                                : Items.GetItemStat(item, stat) >= (int)propertyValue;
                        }
                    }

                    if (!requirementMet)
                    {
                        break;
                    }
                }

                if (!requirementMet)
                {
                    continue;
                }

                matchedRule = rule;
                matchReason = "Configured MapAssist loot-filter rule";
                return true;
            }

            // The original MapAssist enums predate ROTW. Raw stat layers and current
            // localization data are therefore evaluated by this supplemental filter.
            string rotwReason;
            if (RotwLootFilter.IsGoodItem(item, out rotwReason))
            {
                matchedRule = null;
                matchReason = rotwReason;
                return true;
            }

            return false;
        }
    }
}
