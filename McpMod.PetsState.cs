using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Monsters;
using WuppyMonster = MegaCrit.Sts2.Core.Models.Monsters.Wuppy;
using WuppyRelic = MegaCrit.Sts2.Core.Models.Relics.Wuppy;

namespace STS2_MCP;

public static partial class McpMod
{
    private static void AttachPetsState(Dictionary<string, object?> state, Player owner, string context)
    {
        var manager = CombatManager.Instance;
        if (!manager.IsInProgress) return;

        try
        {
            var relics = owner.Relics.OfType<WuppyRelic>().ToList();
            var combatState = owner.PlayerCombatState;
            if (combatState == null)
            {
                if (relics.Count == 0) return;
                throw new InvalidOperationException("PlayerCombatState is unavailable");
            }

            var wuppies = combatState.Pets.Where(pet => pet.Monster is WuppyMonster).ToList();
            if (relics.Count > 0 || wuppies.Count > 0)
                ValidateWuppyPets(owner, manager.DebugOnlyGetState(), relics, wuppies);

            var pets = new List<Dictionary<string, object?>>();
            Creature? osty = combatState.GetPet<Osty>();
            if (osty != null)
            {
                pets.Add(new Dictionary<string, object?>
                {
                    ["id"] = osty.Monster?.Id.Entry ?? "OSTY",
                    ["name"] = SafeGetText(() => osty.Monster?.Title) ?? "Otsy",
                    ["alive"] = osty.IsAlive,
                    ["hp"] = osty.CurrentHp,
                    ["max_hp"] = osty.MaxHp,
                    ["block"] = osty.Block,
                    ["status"] = BuildPowersState(osty)
                });
            }

            foreach (var wuppy in wuppies)
                pets.Add(BuildWuppyPetState(wuppy, $"{context}.{CombatPetsKey}[{pets.Count}]"));

            if (pets.Count > 0 || relics.Count > 0)
                state[CombatPetsKey] = pets;
        }
        catch (Exception ex)
        {
            state[CombatPetsKey] = null;
            RecordInternalStateFailure(state, CombatPetsKey, ex, context);
        }
    }

    private static void ValidateWuppyPets(Player owner, ICombatState? combat, List<WuppyRelic> relics, List<Creature> wuppies)
    {
        if (combat == null || !ReferenceEquals(owner.Creature.CombatState, combat))
            throw new InvalidOperationException("Owner is not attached to the current combat");

        foreach (var pet in wuppies)
        {
            if (!ReferenceEquals(pet.PetOwner, owner))
                throw new InvalidOperationException("Wuppy in PlayerCombatState.Pets belongs to a different owner");
            if (!ReferenceEquals(pet.CombatState, combat) || !combat.Allies.Contains(pet))
                throw new InvalidOperationException("Wuppy in PlayerCombatState.Pets is not an ally in the current combat");
        }

        // CombatState.IterateHookListeners skips melted relics. Their references can outlive their effects.
        var activeRelic = relics.SingleOrDefault(relic => !relic.IsMelted);
        if (activeRelic == null) return;
        var referencedPet = activeRelic.CombatWuppy;
        if (wuppies.Count == 0)
        {
            // Relics.Wuppy.SummonPet assigns CombatWuppy before PlayerCmd.AddPet registers it.
            if (referencedPet != null && ReferenceEquals(referencedPet.CombatState, combat) && combat.ContainsCreature(referencedPet))
                throw new InvalidOperationException("Relic's current CombatWuppy is missing from PlayerCombatState.Pets");
        }
        else if (wuppies.Count != 1 || !ReferenceEquals(wuppies[0], referencedPet))
        {
            throw new InvalidOperationException("PlayerCombatState.Pets disagrees with the relic's CombatWuppy");
        }
    }

    private static Dictionary<string, object?> BuildWuppyPetState(Creature pet, string context)
    {
        var row = new Dictionary<string, object?>();
        ReadInternalStateValue(row, "id", () => pet.Monster?.Id.Entry is { Length: > 0 } id
            ? id : throw new InvalidOperationException("Wuppy has no monster ID"), context);
        ReadInternalStateValue(row, "name", () => StripRichTextTags(pet.Name), context);
        ReadInternalStateValue(row, "combat_id", () => pet.CombatId
            ?? throw new InvalidOperationException("Wuppy has no CombatId"), context);
        ReadInternalStateValue(row, "hp", () => pet.CurrentHp, context);
        ReadInternalStateValue(row, "max_hp", () => pet.MaxHp, context);
        ReadInternalStateValue(row, "alive", () => pet.IsAlive, context);
        ReadInternalStateValue(row, "block", () => pet.Block, context);
        ReadInternalStateValue(row, "status", () => BuildPowersState(pet), context);
        ReadInternalStateValue(row, PetPowerIdsKey, () => pet.Powers.Select(power => power.Id.Entry is { Length: > 0 } id
            ? id : throw new InvalidOperationException("Wuppy power has no ID")).ToList(), context);
        return row;
    }
}
