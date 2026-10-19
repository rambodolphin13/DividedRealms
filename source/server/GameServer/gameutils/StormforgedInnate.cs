using System;

namespace DOL.GS
{
    /// <summary>Intrinsic class bonuses, separate from item/buff caps and saved base stats.</summary>
    public static class StormforgedInnate
    {
        public static int Bonus(GameLiving living, eProperty property)
        {
            int? classId = living switch
            {
                GamePlayer player => player.CharacterClass?.ID,
                IGamePlayer bot => bot.CharacterClass?.ID,
                _ => null
            };
            if (classId != (int)eCharacterClass.Stormforged)
                return 0;
            int level = Math.Clamp((int)living.Level, 1, 50);
            if (property >= eProperty.Stat_First && property <= eProperty.Stat_Last)
                return level * 2;
            return property switch
            {
                eProperty.MaxHealth => level * 10,
                eProperty.MaxMana => level * 2,
                _ => 0
            };
        }
    }
}
