using System.Runtime.CompilerServices;
using DOL.GS;
using DOL.GS.Spells;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// A player's cast is queued without a target (the target is read at cast time), so spell handlers that look at their
/// selectedTarget before calling base saw null and refused every player cast: Cure Root, Power Transfer, Shadow Step,
/// Ally Stealth and more (class test 2026-11-12). CheckBeginCast is now given the player's current target.
/// </summary>
[TestFixture]
public sealed class UT_SelectedTargetAtCastTime
{
    private static T Blank<T>() where T : class => (T) RuntimeHelpers.GetUninitializedObject(typeof(T));

    [Test]
    public void AQueuedTargetIsKept()
    {
        var caster = Blank<GameNPC>();
        var target = Blank<GameNPC>();
        Assert.That(SpellHandler.SelectedTargetAtCastTime(caster, target), Is.SameAs(target), "bots and pets pass their target");
    }

    [Test]
    public void ANonPlayerWithoutATargetStaysWithoutOne()
    {
        Assert.That(SpellHandler.SelectedTargetAtCastTime(Blank<GameNPC>(), null), Is.Null);
        Assert.That(SpellHandler.SelectedTargetAtCastTime(null, null), Is.Null);
    }

    [Test]
    public void APlayerCastSeesThePlayersCurrentTarget()
    {
        var player = Blank<GamePlayer>();
        var ally = Blank<GameNPC>();
        player.TargetObject = ally;
        Assert.That(SpellHandler.SelectedTargetAtCastTime(player, null), Is.SameAs(ally),
            "this was null, so Cure Root answered 'You cannot use this on yourself' and Power Transfer nothing at all");
    }

    [Test]
    public void APlayerWithNoTargetStillPassesNone()
    {
        var player = Blank<GamePlayer>();
        Assert.That(SpellHandler.SelectedTargetAtCastTime(player, null), Is.Null);
    }
}
