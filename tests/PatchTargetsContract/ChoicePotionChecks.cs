using System.Reflection;
using System.Text.Json;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;

static class ChoicePotionChecks
{
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static void Run(Type mod, Action<bool, string> check, Player owner, Player ally, CombatState combat)
    {
        var read = mod.GetMethod("BuildChoicePotionContext", PrivateStatic)!;
        Dictionary<string, object?>? Read(Player player, bool skipCombatEffects = false, CombatState? state = null)
            => (Dictionary<string, object?>?)read.Invoke(null, [player, state ?? combat, skipCombatEffects]);
        var stages = Audit(mod, check);
        var belt = Belt(owner);
        var allyBelt = Belt(ally);
        var relics = (List<RelicModel>)typeof(Player).GetField("_relics", PrivateInstance)!.GetValue(owner)!;
        owner.ResetCombatState();
        ally.ResetCombatState();

        check(Read(owner) == null, "Brew and relevant relics without a choice bottle leave the context absent");
        var attack = Hold(ModelDb.Potion<AttackPotion>(), owner);
        belt[0] = attack;
        belt[2] = Hold(ModelDb.Potion<AttackPotion>(), owner);
        allyBelt[2] = Hold(ModelDb.Potion<SkillPotion>(), ally);
        var attach = mod.GetMethod("AttachChoicePotionContext", PrivateStatic)!;
        foreach (var screen in new[] { "map", "card_select", "monster" })
        {
            var state = new Dictionary<string, object?>();
            attach.Invoke(null, [state, owner, screen]);
            check(state.Count == 0, $"{screen} with no settled live combat input does not attach the context");
        }
        var trinketsOnly = Read(owner)!;
        var slots = Rows(trinketsOnly["slots"]);
        check(trinketsOnly["known"] is true && Equals(trinketsOnly["owner_player_id"], "17")
            && Equals(trinketsOnly["version"], 1)
            && Equals(trinketsOnly["game_assembly_sha256"], "a671e9efb86a7b80405133105665867b053c787670d475b4347cf5ccc15cb5fb")
            && slots.Select(row => (int)row["slot"]!).SequenceEqual(new[] { 0, 2 })
            && slots.All(row => Equals(row["potion_id"], "ATTACK_POTION") && row["pick_known"] is true && row["skip_known"] is true),
            "same-ID choices are separate, retain physical slot numbers and identify their owner");
        check(Rows(slots[0]["notifications"]).Select(row => ((string)row["notification"]!,
                Equals(row["scope"], "run"), Equals(row["pick_only"], true))).SequenceEqual(stages),
            "notification sequence, run/combat scopes and pick-only stages match the audit");
        var before = Rows(Stage(slots[0], "before_potion_used")["listeners"]).Single();
        var after = Rows(Stage(slots[0], "after_potion_used")["listeners"]).Single();
        check(Equals(before["model_id"], "SURROUNDED_POWER") && Equals(before["owner_combat_id"], 1u)
            && Equals(after["model_id"], "REPTILE_TRINKET") && Equals(after["owner_index"], 0)
            && Equals(after["owner_player_id"], "17") && after["supported"] is true,
            "own potion-use handlers resolve to their public power and relic instances");
        int runCount = owner.RunState.IterateHookListeners(combat).Count() - 1;
        int combatCount = combat.IterateHookListeners().Count() - 1;
        check(slots.All(row => Equals(Stage(row, "after_potion_used")["listener_count"], runCount)
            && Equals(Stage(row, "after_card_entered_combat")["listener_count"], combatCount)),
            "each variant removes one exact bottle and keeps same-ID, other-ID and other-owner bottles");
        check(Read(ally) == null, "other owner's ReptileTrinket and SurroundedPower do not trigger");

        var closed = Read(owner, skipCombatEffects: true)!;
        var closedSlot = Rows(closed["slots"])[0];
        check(closed["should_skip_combat_effect"] is true && Equals(Stage(closedSlot, "after_card_entered_combat")["listener_count"], 0)
            && Equals(Stage(closedSlot, "after_potion_used")["listener_count"], runCount),
            "ShouldSkipCombatEffect suppresses combat-scope stages only");

        belt[1] = null;
        belt[2] = null;
        var lastBottle = Rows(Read(owner)!["slots"]).Single();
        check(Rows(Stage(lastBottle, "after_potion_used")["listeners"]).Select(row => (string)row["model_id"]!)
            .SequenceEqual(new[] { "REPTILE_TRINKET", "BELT_BUCKLE" }),
            "last bottle exposes BeltBuckle in game enumeration order");

        relics.Clear();
        // Owner identity is tied to the creature, not a NetId string taken from a potion.
        belt[0] = Hold(ModelDb.Potion<AttackPotion>(), ally);
        CheckUnknown(Read(owner), "owner or instance mismatch", check);
        belt[0] = attack;
        CheckUnknown(Read(owner, state: new CombatState(runState: owner.RunState)), "owner is not in this combat", check);
        owner.Creature.RemovePowerInternal(owner.Creature.Powers.Single());

        var draw = owner.PlayerCombatState!.DrawPile;
        var stomp = ModelDb.Card<Stomp>().ToMutable();
        combat.AddCard(stomp, owner);
        draw.AddInternal(stomp, silent: true);
        check(Read(owner) == null, "an existing Stomp has a candidate-self override and does not intervene");
        var first = AddRocketPunch();
        var second = AddRocketPunch();
        check(Read(owner) == null, "RocketPunch reacts only to Status; Attack Potion cannot select one");
        var ringing = ModelDb.Power<RingingPower>().ToMutable();
        ringing.ApplyInternal(owner.Creature, 1, silent: true);
        var stateOrder = draw.Cards.ToArray();
        var rng = owner.RunState.Rng.ToSerializable().Rngs.ToArray();
        var playerRng = owner.PlayerRng.ToSerializable().Rngs.ToArray();
        var history = CombatManager.Instance.History.Entries.ToArray();
        var read1 = Read(owner)!;
        var read2 = Read(owner)!;
        var hiddenSlot = Rows(read1["slots"]).Single();
        var entered = Rows(Stage(hiddenSlot, "after_card_entered_combat")["listeners"]).Single();
        check(read1["known"] is false && hiddenSlot["pick_known"] is false && hiddenSlot["skip_known"] is true
            && ((IEnumerable<string>)hiddenSlot["unavailable_reasons"]!).Any(reason => reason.Contains("AfterCardEnteredCombat: RINGING_POWER"))
            && Equals(entered["model_id"], "RINGING_POWER") && entered["supported"] is false,
            "unsupported pick-only rows preserve their IDs without blocking skip");

        // No fixed-DLL Draw listener can affect these four direct picks. Exercise the shared
        // serializer in its pre-use mode with a draw listener that has a real override.
        var readListeners = mod.GetMethod("BuildHookListenerRows", PrivateStatic)!;
        var generatedHook = (ValueTuple<string, string, Type[], bool>)
            mod.GetProperty("CardGeneratedForCombatHook", PrivateStatic)!.GetValue(null)!;
        List<Dictionary<string, object?>> DrawRows() => Rows(readListeners.Invoke(null,
            [combat.IterateHookListeners().ToList(), generatedHook, new List<string>(), null, false]));
        var generated = DrawRows();
        check(generated.Count == 2 && generated.All(row => row["order"] == null && Equals(row["order_group"], "draw_pile")
                && Equals(row["order_group_owner_player_id"], "17"))
            && generated.Select(row => (string)row["uid"]!).SequenceEqual(generated.Select(row => (string)row["uid"]!).Order(StringComparer.Ordinal)),
            "pre-use listener serialization hides all draw positions");
        check(JsonSerializer.Serialize(read1) == JsonSerializer.Serialize(read2) && draw.Cards.SequenceEqual(stateOrder)
            && owner.RunState.Rng.ToSerializable().Rngs.SequenceEqual(rng)
            && owner.PlayerRng.ToSerializable().Rngs.SequenceEqual(playerRng)
            && CombatManager.Instance.History.Entries.SequenceEqual(history)
            && ReferenceEquals(belt[0], attack) && !attack.IsQueued,
            "two reads leave both seeded RNG sets, history, draw order and the belt intact");
        draw.RemoveInternal(first, silent: true);
        draw.RemoveInternal(second, silent: true);
        draw.AddInternal(second, silent: true);
        draw.AddInternal(first, silent: true);
        check(JsonSerializer.Serialize(read1) == JsonSerializer.Serialize(Read(owner))
            && JsonSerializer.Serialize(generated) == JsonSerializer.Serialize(DrawRows()),
            "permuting hidden draw order leaves both the exposed context and draw listener rows unchanged");

        bool called = false;
        ModHelper.SubscribeForCombatStateHooks("test.choice-potion", _ => { called = true; return []; });
        CheckUnknown(Read(owner), "ModHelper", check);
        check(!called, "an external hook subscriber is recognized before it can run");

        CardModel AddRocketPunch()
        {
            var card = ModelDb.Card<RocketPunch>().ToMutable();
            combat.AddCard(card, owner);
            draw.AddInternal(card, silent: true);
            return card;
        }
    }

    private static (string, bool, bool)[] Audit(Type mod, Action<bool, string> check)
    {
        var expected = new (string Stage, string Method, bool Run, bool Pick, string[] Types)[]
        {
            ("before_potion_used", "BeforePotionUsed", true, false, ["SurroundedPower"]),
            ("after_card_entered_combat", "AfterCardEnteredCombat", false, true,
                ["BansheesCry", "Flatten", "GalvanicPower", "GhostSeed", "HexPower", "Hexed", "Midnight", "Pinpoint",
                 "RingingPower", "SmoggyPower", "Stomp", "SwordSagePower", "TangledPower", "VitalSparkPower"]),
            ("after_card_changed_piles", "AfterCardChangedPiles", true, true,
                ["BingBong", "BookOfFiveRings", "DarkstonePeriapt", "Hoarder", "LuckyFysh", "MockRecordCardChangedPilesPower",
                 "MockResetCombatOnShufflePower", "SovereignBlade", "SpoilsMap"]),
            ("after_card_changed_piles_late", "AfterCardChangedPilesLate", true, true, ["SoulFysh"]),
            ("after_card_generated_for_combat", "AfterCardGeneratedForCombat", false, true,
                ["Aeonglass", "AllIsWellPower", "ArsenalPower", "Fertilizer", "PillarOfCreationPower", "Regalite",
                 "RocketPunch", "SmokestackPower", "SoulboundPower", "TrashToTreasurePower"]),
            ("after_potion_used", "AfterPotionUsed", true, false, ["BeltBuckle", "ReptileTrinket"]),
            ("after_hand_emptied", "AfterHandEmptied", false, false, ["UnceasingTop"]),
        };
        var hooks = ((string Stage, string Method, Type[] Parameters, bool Run, bool Pick)[])
            mod.GetField("ChoicePotionHooks", PrivateStatic)!.GetValue(null)!;
        check(hooks.Select(h => (h.Stage, h.Method, h.Run, h.Pick)).SequenceEqual(expected.Select(h => (h.Stage, h.Method, h.Run, h.Pick))),
            "the audited methods and scopes match the production stages");
        for (int i = 0; i < hooks.Length; i++)
        {
            var hook = hooks[i];
            var baseMethod = typeof(AbstractModel).GetMethod(hook.Method, hook.Parameters)!;
            var actual = typeof(AbstractModel).Assembly.GetTypes()
                .Where(t => t.IsSubclassOf(typeof(AbstractModel)))
                .Select(t => t.GetMethod(hook.Method, hook.Parameters))
                .Where(m => m != null && m.DeclaringType != typeof(AbstractModel) && m.GetBaseDefinition() == baseMethod)
                .Select(m => m!.DeclaringType!.Name).ToHashSet(StringComparer.Ordinal);
            check(actual.SetEquals(expected[i].Types),
                $"{hook.Method} override audit: {string.Join(", ", actual.Order(StringComparer.Ordinal))}");
        }
        return expected.Select(h => (h.Stage, h.Run, h.Pick)).ToArray();
    }

    private static void CheckUnknown(Dictionary<string, object?>? context, string reason, Action<bool, string> check)
        => check(context != null && context["known"] is false && context["slots"] == null
            && ((IEnumerable<string>)context["unavailable_reasons"]!).Any(message => message.Contains(reason, StringComparison.Ordinal)),
            "unobservable context includes a reason and no partial slots: " + reason);

    private static List<PotionModel?> Belt(Player player)
        => (List<PotionModel?>)typeof(Player).GetField("_potionSlots", PrivateInstance)!.GetValue(player)!;

    private static PotionModel Hold(PotionModel canonical, Player owner)
    {
        var potion = canonical.ToMutable();
        potion.Owner = owner;
        return potion;
    }

    private static List<Dictionary<string, object?>> Rows(object? value) => (List<Dictionary<string, object?>>)value!;

    private static Dictionary<string, object?> Stage(Dictionary<string, object?> slot, string notification)
        => Rows(slot["notifications"]).Single(row => Equals(row["notification"], notification));
}
