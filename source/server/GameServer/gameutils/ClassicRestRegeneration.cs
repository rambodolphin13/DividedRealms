namespace DOL.GS
{
    /// <summary>One timing/stance rule for real players and NPC-backed playerbots.
    /// Native property calculators still apply level, buffs, debuffs and server modifiers.</summary>
    public static class ClassicRestRegeneration
    {
        /// <summary>
        /// 3 s out of combat, standing or sitting (1.87: standing health and power regeneration doubled to match
        /// sitting, DR-186); in combat 14 s standing and 10 s sitting, as before.
        /// </summary>
        public static int HealthAndPowerInterval(bool sitting, bool inCombat)
        {
            if (!inCombat)
                return 3000;
            return (sitting ? 10 : 14) * 1000;
        }

        /// <summary>1.87: endurance regenerates at the standing rate while moving (DR-187); none in combat.</summary>
        public static int BaseEndurancePerTick(bool sitting, bool inCombat, bool moving)
        {
            return inCombat ? 0 : sitting && !moving ? 4 : 1;
        }
    }
}
