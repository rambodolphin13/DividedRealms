using DOL.Database;

namespace DOL.GS
{
    /// <summary>Which engines a piece of siege ammunition fits.</summary>
    public enum eSiegeAmmoKind
    {
        None,
        Stone, // catapult and trebuchet: shots, boulders, lava stones, ice balls, demon seals
        Bolt,  // ballista and palintone
    }

    /// <summary>One shot's numbers: damage and blast size as multipliers of the engine's plain shot.</summary>
    public readonly record struct SiegeShot(double Damage, double Area, eDamageType DamageType, string AmmoName)
    {
        public bool IsAmmo => AmmoName != null;

        public static SiegeShot Plain(eDamageType engineType) => new(1.0, 1.0, engineType, null);
    }

    /// <summary>
    /// Crafted siege ammunition (crafting pass 2026-11-17, owner design). The numbers live in the item row so they can be
    /// tuned with a world edit: DPS_AF = damage % of the plain shot, SPD_ABS = blast area % (catapults), Type_Damage =
    /// damage type. Only generic items with a siege ammo model and DPS_AF set count as ammo.
    /// </summary>
    public static class SiegeAmmo
    {
        public const int StoneModel = 1681;
        public const int BoltModel = 1682;
        public const int IceBallModel = 2621;

        public static eSiegeAmmoKind KindOf(DbItemTemplate template)
        {
            if (template == null || template.DPS_AF <= 0 || template.Object_Type != (int)eObjectType.GenericItem)
                return eSiegeAmmoKind.None;

            return template.Model switch
            {
                BoltModel => eSiegeAmmoKind.Bolt,
                StoneModel or IceBallModel => eSiegeAmmoKind.Stone,
                _ => eSiegeAmmoKind.None
            };
        }

        public static SiegeShot ShotOf(DbItemTemplate template, eDamageType engineType)
        {
            if (KindOf(template) == eSiegeAmmoKind.None)
                return SiegeShot.Plain(engineType);

            double area = template.SPD_ABS > 0 ? template.SPD_ABS / 100.0 : 1.0;
            eDamageType type = template.Type_Damage > 0 ? (eDamageType)template.Type_Damage : engineType;
            return new SiegeShot(template.DPS_AF / 100.0, area, type, template.Name);
        }

        /// <summary>Fortified engines: +25% damage over the field version (owner design 2026-11-17).</summary>
        public const double FortifiedDamage = 1.25;

        /// <summary>Fortified engines: +50% health (15,000 instead of 10,000).</summary>
        public const int FieldHealth = 10000;
        public const int FortifiedHealth = 15000;

        public static int Health(bool fortified) => fortified ? FortifiedHealth : FieldHealth;

        /// <summary>
        /// Damage one target takes from a shot. Ammunition carries a damage type, so the target's resist to it applies
        /// (live Grab Bag 2020-02-28); the plain shot keeps today's numbers.
        /// </summary>
        public static int Damage(int baseAmount, SiegeShot shot, bool fortified, int targetResistPercent)
        {
            double damage = baseAmount * shot.Damage * (fortified ? FortifiedDamage : 1.0);

            if (shot.IsAmmo)
                damage *= 1.0 - System.Math.Clamp(targetResistPercent, -100, 100) / 100.0;

            return System.Math.Max(0, (int)damage);
        }
    }
}
