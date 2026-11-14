using System;
using DOL.Database;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
    /// <summary>
    /// Mithril store patterns (live 1.123 "Patterning"), with the owner's looser rule (2026-11-14): any armor takes the look of
    /// another armor for the same slot, any weapon or shield takes the look of another for the same hand.
    /// Drag a Blank Pattern onto an item to record its look, then onto the item to change. Masks go on helms. A Pattern Eraser
    /// puts the item's own look back. Stats never change.
    /// </summary>
    public static class Transmog
    {
        public const string BlankPatternPrefix = "mithril_pattern_blank";
        public const string MaskPrefix = "mithril_mask_";
        public const string EraserId = "mithril_pattern_eraser";

        public static bool IsTransmogTool(DbInventoryItem item)
        {
            string id = item?.Id_nb;
            return id != null && (id.StartsWith(BlankPatternPrefix, StringComparison.OrdinalIgnoreCase)
                || id.StartsWith(MaskPrefix, StringComparison.OrdinalIgnoreCase)
                || id.Equals(EraserId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The look family: armor slot or weapon hand (Item_Type), or 0 when the item can't be patterned.</summary>
        public static int Family(DbInventoryItem item)
        {
            if (item == null || IsTransmogTool(item))
                return 0;

            return (eInventorySlot) item.Item_Type switch
            {
                eInventorySlot.HeadArmor or eInventorySlot.HandsArmor or eInventorySlot.FeetArmor or eInventorySlot.TorsoArmor
                    or eInventorySlot.LegsArmor or eInventorySlot.ArmsArmor or eInventorySlot.Cloak
                    or eInventorySlot.RightHandWeapon or eInventorySlot.LeftHandWeapon or eInventorySlot.TwoHandWeapon
                    or eInventorySlot.DistanceWeapon => item.Item_Type,
                _ => 0,
            };
        }

        public enum Result { NotATool, Recorded, Applied, Erased, Refused }

        /// <summary>Uses <paramref name="tool"/> on <paramref name="target"/>. The caller removes one tool when it was used up.</summary>
        public static Result Use(DbInventoryItem tool, DbInventoryItem target, out string message)
        {
            message = null;

            if (!IsTransmogTool(tool))
                return Result.NotATool;

            string id = tool.Id_nb;
            int family = Family(target);

            if (family == 0)
            {
                message = "Patterns only work on armor, cloaks, weapons and shields.";
                return Result.Refused;
            }

            if (id.Equals(EraserId, StringComparison.OrdinalIgnoreCase))
            {
                if (target.LookModel <= 0)
                {
                    message = $"Your {target.Name} already has its own look.";
                    return Result.Refused;
                }

                target.LookModel = 0;
                message = $"Your {target.Name} has its own look again.";
                return Result.Erased;
            }

            if (id.StartsWith(MaskPrefix, StringComparison.OrdinalIgnoreCase))
            {
                if (family != (int) eInventorySlot.HeadArmor)
                {
                    message = "Masks only go on helms.";
                    return Result.Refused;
                }

                target.LookModel = tool.Template.Model;
                message = $"Your {target.Name} now looks like the {tool.Name}.";
                return Result.Applied;
            }

            // Blank pattern: record first, apply second.
            if (tool.LookModel <= 0)
            {
                if (tool.Count > 1)
                {
                    message = "Split one pattern off the stack first.";
                    return Result.Refused;
                }

                tool.LookModel = target.Model;
                tool.LookSlot = family;
                message = $"The pattern now holds the look of your {target.Name}. Put it on another item for the same slot.";
                return Result.Recorded;
            }

            if (tool.LookSlot != family)
            {
                message = $"This pattern holds a look for the {SlotName(tool.LookSlot)} and can't go on your {target.Name}.";
                return Result.Refused;
            }

            target.LookModel = tool.LookModel;
            message = $"Your {target.Name} takes the pattern's look.";
            return Result.Applied;
        }

        public static string SlotName(int itemType)
        {
            return (eInventorySlot) itemType switch
            {
                eInventorySlot.HeadArmor => "head",
                eInventorySlot.HandsArmor => "hands",
                eInventorySlot.FeetArmor => "feet",
                eInventorySlot.TorsoArmor => "chest",
                eInventorySlot.LegsArmor => "legs",
                eInventorySlot.ArmsArmor => "arms",
                eInventorySlot.Cloak => "cloak",
                eInventorySlot.RightHandWeapon => "main hand",
                eInventorySlot.LeftHandWeapon => "off hand",
                eInventorySlot.TwoHandWeapon => "two-handed weapon",
                eInventorySlot.DistanceWeapon => "ranged weapon",
                _ => "another slot",
            };
        }
    }
}
