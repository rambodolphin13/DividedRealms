using System;

namespace DOL.GS
{
    /// <summary>
    /// Level 50 progress for account companions (DR-494): kill experience fills the Master Level bar and realm points
    /// the Champion bar, by the same rules a player character follows (MasterLevelRules.GainExperience,
    /// ChampionRules.GainRealmPoints). Champion experience needs the character to have started Champion training at
    /// the King, as for a player. Pure so it can be tested; the companion stores the result on its character.
    /// </summary>
    public static class AccountCompanionEndgame
    {
        /// <summary>The experience one Master Level needs (per level, the bar empties on each level).</summary>
        public const long MasterLevelExperience = 32000;

        public readonly record struct Progress(int Level, long Experience, int LevelsGained);

        public static Progress AddMasterExperience(int mlLevel, long mlExperience, long killExperience, long levelSpan, double rate)
        {
            if (mlLevel >= GamePlayer.ML_MAX_LEVEL)
                return new Progress(mlLevel, 0, 0);

            long gained = MasterLevelRules.ToMasterExperience(killExperience, MasterLevelExperience, levelSpan, rate);
            if (gained <= 0)
                return new Progress(mlLevel, mlExperience, 0);

            int level = mlLevel;
            long experience = mlExperience + gained;
            while (level < GamePlayer.ML_MAX_LEVEL && experience >= MasterLevelExperience)
            {
                experience -= MasterLevelExperience;
                level++;
            }

            if (level >= GamePlayer.ML_MAX_LEVEL)
                experience = 0;

            return new Progress(level, experience, level - mlLevel);
        }

        /// <summary>Champion experience is cumulative: a level comes when it reaches that level's total.</summary>
        public static Progress AddChampionExperience(bool champion, int championLevel, long championExperience, long realmPoints, double rate)
        {
            if (!champion || championLevel >= GamePlayer.CL_MAX_LEVEL)
                return new Progress(championLevel, championExperience, 0);

            long gained = ChampionRules.ToChampionExperience(realmPoints, rate);
            if (gained <= 0)
                return new Progress(championLevel, championExperience, 0);

            long cap = GamePlayer.CLXPLevel[GamePlayer.CL_MAX_LEVEL];
            long experience = Math.Min(championExperience + gained, cap);
            int level = championLevel;
            while (level < GamePlayer.CL_MAX_LEVEL && experience >= GamePlayer.CLXPLevel[level + 1])
                level++;

            return new Progress(level, experience, level - championLevel);
        }
    }
}
