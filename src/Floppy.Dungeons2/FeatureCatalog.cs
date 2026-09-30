#nullable enable
using System;
using System.Collections.Generic;

namespace Floppy.Dungeons2;

internal enum Adjustment { Multiply, Divide, Add, PercentAdd, PercentReduce }

internal sealed record Feature(string Id, string Category, string Label, string Target, Adjustment Mode,
    float Min, float Max, float Step, float Limit, string Description = "")
{
    internal float Neutral => Mode is Adjustment.Multiply or Adjustment.Divide ? 1 : 0;
    internal float Value(float original, float setting) => Math.Clamp(Mode switch
    {
        Adjustment.Multiply => original * setting,
        Adjustment.Divide => original / setting,
        Adjustment.PercentAdd => original + setting / 100,
        Adjustment.PercentReduce => original * (1 - setting / 100),
        _ => original + setting
    }, 0, Limit);
}

/// <summary>Explicitly supported local fields, with bounded controls and neutral defaults.</summary>
internal static class FeatureCatalog
{
    private static Feature Factor(string id, string category, string label, string target, float max = 10,
        string description = "") => new(id, category, label + " ×", target, Adjustment.Multiply, 1, max, .25f, 1_000_000, description);
    private static Feature Faster(string id, string category, string label, string target, float max = 10) =>
        new(id, category, label + " ×", target, Adjustment.Divide, 1, max, .25f, 1_000_000);
    private static Feature Chance(string id, string category, string label, string target) =>
        new(id, category, label + " +%", target, Adjustment.PercentAdd, 0, 100, 5, 1);
    private static Feature Extra(string id, string category, string label, string target, float max, float step = 1,
        float limit = 1_000_000) => new(id, category, label + " +", target, Adjustment.Add, 0, max, step, limit);

    internal static readonly IReadOnlyList<Feature> All = new Feature[]
    {
        Factor("health.maximum", "Überleben", "Maximales Leben", "ATR_Health.HealthMax", 5),
        Factor("health.healing", "Überleben", "Erhaltene Heilung", "ATR_Health.ReceiveHealingMultiplier", 5),
        new("health.regeneration", "Überleben", "Passive Heilung +%", "ATR_Health.PassiveHealingPercentage", Adjustment.PercentAdd, 0, 10, .5f, .25f),
        new("health.leech.melee", "Überleben", "Lebensraub im Nahkampf +%", "ATR_Health.LifeLeechMeleePercentage", Adjustment.PercentAdd, 0, 50, 5, 1),
        new("health.leech.ranged", "Überleben", "Lebensraub im Fernkampf +%", "ATR_Health.LifeLeechRangedPercentage", Adjustment.PercentAdd, 0, 50, 5, 1),
        Factor("potion.healing", "Überleben", "Heiltrankwirkung", "ATR_Health.HealthPotionHealingMultiplier", 5),
        Faster("potion.cooldown", "Überleben", "Heiltrankaufladung", "ATR_Health.HealthPotionCooldownMultiplier", 20),
        new("defense.damage", "Verteidigung", "Eingehenden Schaden senken %", "ATR_Resistance.BaseDamageResistanceMultiplier", Adjustment.PercentReduce, 0, 95, 5, 100),
        new("defense.melee", "Verteidigung", "Nahkampfschaden senken %", "ATR_Resistance.MeleeDamageResistanceMultiplier", Adjustment.PercentReduce, 0, 95, 5, 100),
        new("defense.ranged", "Verteidigung", "Fernkampfschaden senken %", "ATR_Resistance.RangedDamageResistanceMultiplier", Adjustment.PercentReduce, 0, 95, 5, 100),
        new("defense.elemental", "Verteidigung", "Elementarschaden senken %", "ATR_Resistance.ElementalDamageResistanceMultiplier", Adjustment.PercentReduce, 0, 95, 5, 100),
        Factor("defense.armor", "Verteidigung", "Rüstungswirkung", "ATR_Resistance.ArmorMultiplier", 5),
        Chance("defense.deflect", "Verteidigung", "Geschosse abwehren", "ATR_Resistance.ProjectileDeflectChance"),
        Faster("defense.knockback", "Verteidigung", "Rückstoßschutz", "ATR_Resistance.KnockbackResistanceMultiplier", 10),
        Factor("damage.all", "Schaden", "Gesamtschaden", "ATR_Damage.BaseDamageMultiplier"),
        Factor("damage.melee", "Schaden", "Nahkampfschaden", "ATR_Damage.MeleeDamageMultiplier"),
        Factor("damage.ranged", "Schaden", "Fernkampfschaden", "ATR_Damage.RangedDamageMultiplier"),
        Factor("damage.artifact", "Schaden", "Artefaktschaden", "ATR_Damage.ArtifactDamageMultiplier"),
        Factor("damage.elemental", "Schaden", "Elementarschaden", "ATR_Damage.ElementalDamageMultiplier"),
        Factor("damage.fire", "Schaden", "Feuerschaden", "ATR_Damage.FireDamageMultiplier", 5),
        Factor("damage.ice", "Schaden", "Eisschaden", "ATR_Damage.IceDamageMultiplier", 5),
        Factor("damage.lightning", "Schaden", "Blitzschaden", "ATR_Damage.LightningDamageMultiplier", 5),
        Factor("damage.poison", "Schaden", "Giftschaden", "ATR_Damage.PoisonDamageMultiplier", 5),
        Factor("damage.soul", "Schaden", "Seelenschaden", "ATR_Damage.SoulDamageMultiplier", 5),
        Chance("damage.critical.chance", "Schaden", "Kritische Trefferchance", "ATR_Damage.CriticalHitChance"),
        Factor("damage.critical.power", "Schaden", "Kritischer Schaden", "ATR_Damage.CriticalHitMultiplier", 5),
        Factor("damage.stagger", "Schaden", "Taumelwirkung", "ATR_Damage.StaggerMultiplier", 3),
        Factor("combat.melee.speed", "Kampf", "Nahkampftempo", "ATR_MeleeAttack.MeleeAttackSpeed", 5),
        Factor("combat.melee.range", "Kampf", "Nahkampfreichweite", "ATR_MeleeAttack.MeleeAttackRange", 3),
        Factor("combat.ranged.speed", "Kampf", "Fernkampftempo", "ATR_RangedAttack.RangedAttackSpeed", 5),
        Factor("combat.projectile.speed", "Kampf", "Geschosstempo", "ATR_RangedAttack.RangedVelocityMultiplier", 3),
        Chance("combat.multishot.chance", "Kampf", "Mehrfachschusschance", "ATR_RangedAttack.MultiShotChance"),
        Extra("combat.multishot.count", "Kampf", "Mehrfachschuss-Geschosse", "ATR_RangedAttack.MultiShotAdditionalProjectiles", 8, 1, 16),
        Extra("combat.penetration", "Kampf", "Zusätzliche Durchschläge", "ATR_RangedAttack.AdditionalPenetrationTargets", 8, 1, 16),
        Extra("combat.ricochet", "Kampf", "Zusätzliche Abpraller", "ATR_RangedAttack.AdditionalRicochetTargets", 8, 1, 16),
        Faster("combat.ammo.recharge", "Kampf", "Munitionsnachladen", "ATR_RangedAttack.RangedAttackRechargeTime"),
        Faster("combat.ammo.delay", "Kampf", "Nachladeverzögerung verkürzen", "ATR_RangedAttack.RangedAttackRechargeDelay"),
        Factor("movement.speed", "Bewegung", "Lauftempo", "Movement.MaxWalkSpeed", 3),
        Factor("movement.jump", "Bewegung", "Sprungkraft", "Movement.JumpZVelocity", 2),
        new("movement.gravity", "Bewegung", "Schwerkraft ×", "Movement.GravityScale", Adjustment.Multiply, .2f, 2, .1f, 10),
        Extra("movement.air", "Bewegung", "Luftsteuerung", "Movement.AirControl", .8f, .05f, 1),
        Faster("movement.roll.cooldown", "Bewegung", "Rollenaufladung", "ATR_Movement.RollCooldown", 10),
        Factor("movement.interaction", "Bewegung", "Interaktionsreichweite", "ATR_Movement.InteractionRange", 3),
        Faster("artifact.cooldown", "Artefakte & Seelen", "Artefaktaufladung", "ATR_Artifact.ArtifactCooldownMultiplier", 20),
        Factor("soul.gathering", "Artefakte & Seelen", "Seelenausbeute", "ATR_Soul.SoulGathering", 5),
        Factor("soul.regeneration", "Artefakte & Seelen", "Seelenregeneration", "ATR_Soul.SoulsPassivePlayerRegenerationAmountPerPeriod", 5),
        Factor("shadow.duration", "Artefakte & Seelen", "Schattenformdauer", "ATR_ShadowForm.ShadowformDurationModifier", 5, "Verstärkt eine vorhandene Schattenform."),
        Factor("shadow.movement", "Artefakte & Seelen", "Schattenformtempo", "ATR_ShadowForm.ShadowformMovementModifier", 3, "Verstärkt eine vorhandene Schattenform."),
        Factor("progress.xp", "Beute & Fortschritt", "Erfahrungsausbeute", "ATR_XP.XPYieldMultiplier", 10, "Gilt für neu verdiente Erfahrung. Erhöht nicht direkt dein Level."),
        Chance("loot.rarity", "Beute & Fortschritt", "Seltene Beute", "ATR_Loot.RarityBonusChance"),
        Chance("loot.drop", "Beute & Fortschritt", "Fundchance", "ATR_Loot.DropChanceIncrease"),
        Chance("loot.duplicate", "Beute & Fortschritt", "Doppelte Beute", "ATR_Loot.DropDuplicationChance"),
        new("loot.emeralds", "Beute & Fortschritt", "Smaragdausbeute +%", "ATR_Currency.EmeraldIncreasePercentage", Adjustment.PercentAdd, 0, 300, 25, 10),
        Factor("companion.damage", "Begleiter & Effekte", "Begleiterschaden", "ATR_Companion.CompanionDamageMultiplier", 5),
        Faster("companion.resistance", "Begleiter & Effekte", "Begleiterschutz", "ATR_Companion.CompanionResistanceMultiplier", 5),
        Faster("companion.cooldown", "Begleiter & Effekte", "Begleiteraufladung", "ATR_Companion.CompanionCooldownMultiplier", 10),
        Factor("status.positive", "Begleiter & Effekte", "Positive Effektdauer", "ATR_Status.ReceivePositiveStatusDurationMultiplier", 5),
        Faster("status.negative", "Begleiter & Effekte", "Negative Effekte verkürzen", "ATR_Status.ReceiveNegativeStatusDurationMultiplier", 10)
    };

    internal sealed record Resource(string Id, string Category, string Label, string Current, string Maximum);
    internal static readonly IReadOnlyList<Resource> Resources = new Resource[]
    {
        new("potion", "Überleben", "Heiltränke", "ATR_Health.HealthPotionCharges", "ATR_Health.HealthPotionChargesMax"),
        new("roll", "Bewegung", "Rollen", "ATR_Movement.RollCharges", "ATR_Movement.RollChargesMax"),
        new("soul", "Artefakte & Seelen", "Seelen", "ATR_Soul.Souls", "ATR_Soul.SoulsMax")
    };
}
