using System;
using System.Collections.Generic;

namespace ErenshorPvP
{
    internal enum PvpSpellSemanticCategory
    {
        Unknown,
        DirectHpHeal,
        HealOverTime,
        BeneficialBuff,
        SelfUtility,
        HarmfulDamage,
        OffensiveDebuff,
        CrowdControl,
        Area
    }

    internal sealed class PvpSpellSemanticSnapshot
    {
        internal PvpSpellSemanticCategory Category;
        internal bool Beneficial;
        internal bool Harmful;
        internal bool DirectHpHeal;
        internal bool HealOverTime;
        internal bool Area;
        internal bool DeclaresSelf;
        internal bool UnsafeWorldShape;
        internal string Token { get { return Category.ToString().ToLowerInvariant(); } }
    }

    // Runtime classification over current Spell data. It deliberately distinguishes "beneficial"
    // from "restores HP" so food/water/buffs never inflate healing request/effect diagnostics.
    internal static class PvpSpellSemantics
    {
        internal static PvpSpellSemanticSnapshot Inspect(Spell spell)
        {
            PvpSpellSemanticSnapshot result = new PvpSpellSemanticSnapshot { Category = PvpSpellSemanticCategory.Unknown };
            if (spell == null) return result;
            try
            {
                bool healType = spell.Type == Spell.SpellType.Heal;
                bool beneficialType = spell.Type == Spell.SpellType.Beneficial;
                bool damageType = spell.Type == Spell.SpellType.Damage;
                bool statusType = spell.Type == Spell.SpellType.StatusEffect;
                bool ae = spell.Type == Spell.SpellType.AE;
                bool pbae = spell.Type == Spell.SpellType.PBAE;
                bool cc = spell.CrowdControlSpell || spell.RootTarget || spell.StunTarget || spell.FearTarget;
                bool self = PvpSpellTargetPolicy.DeclaresSelfApplication(spell.SelfOnly, spell.ApplyToCaster, spell.InflictOnSelf);
                bool pet = spell.PetToSummon != null || spell.Type == Spell.SpellType.Pet;
                bool charm = spell.CharmTarget;
                bool area = spell.GroupEffect || ae || pbae;
                bool damageTypeIsHealing = Convert.ToInt32(spell.MyDamageType) == 0;
                bool directHeal = PvpSpellTargetPolicy.IsDirectHpHeal(healType, spell.HP, spell.TargetHealing,
                    spell.TargetDamage, damageTypeIsHealing, ae, pbae, pet, charm);
                bool hot = !directHeal && HasHealingPayloadInStatusChain(spell);
                bool damagePayload = damageType || spell.TargetDamage > 0 || spell.BleedDamagePercent > 0 || spell.Lifetap;
                bool beneficialPayload = directHeal || hot || healType || beneficialType || spell.TargetHealing > 0 ||
                    spell.CasterHealing > 0 || spell.PercentManaRestoration > 0 || spell.ApplyToCaster || spell.SelfOnly;
                // A StatusEffect is not inherently harmful: Hydrated/Nourished and class buffs are
                // StatusEffect-shaped too. Only classify it as an offensive debuff when the current
                // payload is not otherwise proven beneficial/self-applying.
                bool offensiveDebuff = statusType && !beneficialPayload && !self;
                bool harmful = damagePayload || offensiveDebuff || cc;
                bool beneficial = beneficialPayload && !damagePayload;

                result.Beneficial = beneficial;
                result.Harmful = harmful;
                result.DirectHpHeal = directHeal;
                result.HealOverTime = hot;
                result.Area = area;
                result.DeclaresSelf = self;
                result.UnsafeWorldShape = pet || charm;

                if (directHeal) result.Category = PvpSpellSemanticCategory.DirectHpHeal;
                else if (hot) result.Category = PvpSpellSemanticCategory.HealOverTime;
                else if (cc) result.Category = PvpSpellSemanticCategory.CrowdControl;
                else if (damagePayload) result.Category = PvpSpellSemanticCategory.HarmfulDamage;
                else if (offensiveDebuff) result.Category = PvpSpellSemanticCategory.OffensiveDebuff;
                else if (area) result.Category = PvpSpellSemanticCategory.Area;
                else if (self && beneficialPayload) result.Category = PvpSpellSemanticCategory.SelfUtility;
                else if (beneficialPayload) result.Category = PvpSpellSemanticCategory.BeneficialBuff;
                else if (self) result.Category = PvpSpellSemanticCategory.SelfUtility;
            }
            catch { }
            return result;
        }

        internal static bool IsSafeTemporaryCombatAbility(Spell spell)
        {
            if (spell == null) return false;
            PvpSpellSemanticSnapshot semantic = Inspect(spell);
            if (semantic.UnsafeWorldShape) return false;
            // The existing profile builder already requires SimUsable/current level/class and only
            // admits combat spell families. This second gate removes the two current proven
            // persistent/world-ownership escape shapes (pet creation and charm) without discarding
            // normal procs, buffs, heals, CC, DoTs or AoE class kit.
            return semantic.Category != PvpSpellSemanticCategory.Unknown;
        }

        private static bool HasHealingPayloadInStatusChain(Spell root)
        {
            HashSet<Spell> seen = new HashSet<Spell>();
            Spell current = root;
            for (int depth = 0; current != null && depth < 8 && seen.Add(current); depth++)
            {
                try
                {
                    if (depth > 0)
                    {
                        bool healType = current.Type == Spell.SpellType.Heal;
                        bool damageTypeIsHealing = Convert.ToInt32(current.MyDamageType) == 0;
                        if (PvpSpellTargetPolicy.IsDirectHpHeal(healType, current.HP, current.TargetHealing,
                            current.TargetDamage, damageTypeIsHealing,
                            current.Type == Spell.SpellType.AE, current.Type == Spell.SpellType.PBAE,
                            current.PetToSummon != null || current.Type == Spell.SpellType.Pet,
                            current.CharmTarget)) return true;
                    }
                    current = current.StatusEffectToApply;
                }
                catch { return false; }
            }
            return false;
        }
    }
}
