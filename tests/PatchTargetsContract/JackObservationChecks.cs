using System.Collections;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Relics;

static class JackObservationChecks
{
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Key = "jack_observation_context";
    private static bool _settled;
    private static int _endingHookCalls;

    internal static void Run(Type mod, Action<bool, string> check, Player player, Player ally, CombatState combat)
    {
        player.ResetCombatState();
        ally.ResetCombatState();
        var hand = player.PlayerCombatState!.Hand;
        var draw = player.PlayerCombatState.DrawPile;
        var attach = mod.GetMethod("AttachJackObservationContext", PrivateStatic)!;
        var scope = mod.GetMethod("BuildJackListenerScope", PrivateStatic)!;
        var manager = CombatManager.Instance;
        var turnField = typeof(CombatManager).GetField("_turnState", PrivateInstance)!;
        var previousTurn = turnField.GetValue(manager);
        var turnType = turnField.FieldType;
        var turn = Activator.CreateInstance(turnType, combat)!;
        turnType.GetProperty("IsInProgress")!.SetValue(turn, true);
        turnType.GetProperty("IsStarting")!.SetValue(turn, false);
        turnField.SetValue(manager, turn);

        // Supply the UI verdict in this headless process. The reader still checks the real
        // enemy/ending guard and captures the attempt via the actual CombatManager.
        var harmony = new Harmony("test.jack-observation");
        var settled = mod.GetMethod("IsSettledPotionInput", PrivateStatic)!;
        var endingHook = typeof(Hook).GetMethod(nameof(Hook.ShouldStopCombatFromEnding))!;
        harmony.Patch(settled, prefix: new HarmonyMethod(typeof(JackObservationChecks), nameof(SettledInput)));
        harmony.Patch(endingHook, prefix: new HarmonyMethod(typeof(JackObservationChecks), nameof(EndingHook)));
        var enemy = new Creature(ModelDb.Monster<Nibbit>().ToMutable(), CombatSide.Enemy, null)
        {
            CombatState = combat,
            CombatId = 3
        };
        combat.AddCreature(enemy);

        var jack = ModelDb.Card<JackOfAllTrades>().ToMutable();
        combat.AddCard(jack, player);
        var deckCard = ModelDb.Card<Stomp>().ToMutable();
        deckCard.EnchantInternal(ModelDb.Enchantment<Sharp>().ToMutable(), 1);
        player.RunState.AddCard(deckCard, player);
        player.Deck.AddInternal(deckCard, silent: true);
        AddDraw(ModelDb.Card<DefendIronclad>());
        AddDraw(ModelDb.Card<DefendIronclad>());
        AddDraw(ModelDb.Card<RocketPunch>());
        try
        {
            _settled = true;
            hand.AddInternal(jack, silent: true);
            check(Read() == null, "a normal Jack in the hand leaves the context absent");
            jack.UpgradeInternal();
            jack.FinalizeUpgradeInternal();
            hand.RemoveInternal(jack, silent: true);
            draw.AddInternal(jack, silent: true);
            check(Read() == null, "an upgraded Jack in the draw pile leaves the context absent");
            draw.RemoveInternal(jack, silent: true);
            hand.AddInternal(jack, silent: true);
            check(Read("card_select") == null && Read("map") == null, "selection and noncombat states omit the context");
            _settled = false;
            check(Read() == null, "an unsettled combat omits the context");
            _settled = true;
            turnType.GetProperty("IsInProgress")!.SetValue(turn, false);
            check(Read() == null, "ShouldSkipCombatEffect omits the context");
            turnType.GetProperty("IsInProgress")!.SetValue(turn, true);

            var history = manager.History.Entries.ToArray();
            var playerRng = player.PlayerRng.ToSerializable().Rngs.ToArray();
            var runRng = player.RunState.Rng.ToSerializable().Rngs.ToArray();
            var cards = (ICollection)typeof(CombatState).GetField("_allCards", PrivateInstance)!.GetValue(combat)!;
            int cardCount = cards.Count;
            object? nextCreatureId = typeof(CombatState).GetField("_nextCreatureId", PrivateInstance)!.GetValue(combat);
            var relics = player.Relics.Select(relic => (relic, relic.DisplayAmount)).ToArray();
            int generatedCount = jack.DynamicVars.Cards.IntValue;
            var drawOrder = draw.Cards.ToArray();
            var state = Read()!;
            var encoded = JsonSerializer.Serialize(state);
            var attempt = mod.GetMethod("CaptureCombatAttempt", PrivateStatic)!.Invoke(null, [combat, true])!;
            var attemptId = attempt.GetType().GetProperty("Id", PrivateInstance)!.GetValue(attempt);
            check(state["known"] is true && Equals(state["version"], 1) && Equals(state["owner_player_id"], "17")
                && Equals(state["attempt_id"], attemptId) && state["should_skip_combat_effect"] is false
                && Equals(state["game_assembly_sha256"], "ae309f852fb69204a59c04b7b5af63acc839829c65c1349777621ce616e65310")
                && !((IEnumerable<string>)state["unavailable_reasons"]!).Any(),
                "the matching live attempt and its DLL identify a known context");

            var combatRows = Rows(Row(state["combat"])["models"]);
            var runRows = Rows(Row(state["run"])["models"]);
            check(combatRows.All(row => row.Keys.SequenceEqual(new[] { "category", "id", "concrete_type", "count" }))
                && combatRows.SequenceEqual(combatRows.OrderBy(row => (string)row["category"]!, StringComparer.Ordinal)
                    .ThenBy(row => (string)row["id"]!, StringComparer.Ordinal)
                    .ThenBy(row => (string)row["concrete_type"]!, StringComparer.Ordinal)),
                "model rows retain only the ordinal-sorted type, ID and multiplicity");
            check(Count(combatRows, "card", "DEFEND_IRONCLAD", typeof(DefendIronclad)) == 2
                && Count(runRows, "card", "DEFEND_IRONCLAD", typeof(DefendIronclad)) == 2,
                "duplicate combat cards are counted in each scope");
            check(Count(combatRows, "card", "STOMP", typeof(Stomp)) == 0
                && Count(runRows, "card", "STOMP", typeof(Stomp)) == 1
                && Count(combatRows, "enchantment", "SHARP", typeof(Sharp)) == 0
                && Count(runRows, "enchantment", "SHARP", typeof(Sharp)) == 1,
                "the Deck-only card and its enchantment are in the run scope");
            check(Count(combatRows, "relic", "REPTILE_TRINKET", typeof(ReptileTrinket)) == 1
                && Count(runRows, "relic", "REPTILE_TRINKET", typeof(ReptileTrinket)) == 1
                && combatRows.Where(row => Equals(row["category"], "potion")).Sum(row => (int)row["count"]!) == 1
                && runRows.Where(row => Equals(row["category"], "potion")).Sum(row => (int)row["count"]!) == 1,
                "neither scope counts melted relics or empty potion slots");
            check(Equals(Row(state["combat"])["listener_count"], combatRows.Sum(row => (int)row["count"]!))
                && Equals(Row(state["run"])["listener_count"], runRows.Sum(row => (int)row["count"]!))
                && Count(runRows, "monster", "NIBBIT", typeof(Nibbit)) == 1
                && (int)Row(state["run"])["listener_count"]! == (int)Row(state["combat"])["listener_count"]! + 2,
                "run includes all combat models as well as the two Deck listeners");

            check(encoded == JsonSerializer.Serialize(Read()) && draw.Cards.SequenceEqual(drawOrder),
                "repeated reads are stable and preserve the original draw order");
            ((List<CardModel>)draw.Cards).Reverse();
            check(encoded == JsonSerializer.Serialize(Read()), "permuting the draw pile leaves both scopes unchanged");
            check(player.PlayerRng.ToSerializable().Rngs.SequenceEqual(playerRng)
                && player.RunState.Rng.ToSerializable().Rngs.SequenceEqual(runRng)
                && manager.History.Entries.SequenceEqual(history)
                && cards.Count == cardCount && Equals(nextCreatureId, typeof(CombatState).GetField("_nextCreatureId", PrivateInstance)!.GetValue(combat))
                && player.Relics.Select(relic => (relic, relic.DisplayAmount)).SequenceEqual(relics)
                && jack.DynamicVars.Cards.IntValue == generatedCount,
                "snapshot reads leave RNGs, combat history, card population and counters unchanged");

            var emptyScope = Row(scope.Invoke(null, [Array.Empty<AbstractModel>()]));
            check(Equals(emptyScope["listener_count"], 0) && Rows(emptyScope["models"]).Count == 0,
                "a known empty listener scope has count zero and empty models");
            var rawDeck = (List<CardModel>)player.Deck.Cards;
            // A canonical card has no readable mutable owner. Only the run iterator sees this malformed row.
            rawDeck.Add(ModelDb.Card<Stomp>());
            try { CheckUnknown("run_listener_read_failed"); }
            finally { rawDeck.RemoveAt(rawDeck.Count - 1); }

            bool subscriberCalled = false;
            var runSubscribers = SubscriberList("_runHookSubscribers");
            ModHelper.SubscribeForRunStateHooks("test.jack-run", _ => { subscriberCalled = true; return []; });
            try { CheckUnknown("external_hook_subscribers"); }
            finally { runSubscribers.RemoveAt(runSubscribers.Count - 1); }
            var combatSubscribers = SubscriberList("_combatHookSubscribers");
            ModHelper.SubscribeForCombatStateHooks("test.jack-combat", _ => { subscriberCalled = true; return []; });
            try { CheckUnknown("external_hook_subscribers"); }
            finally { combatSubscribers.RemoveAt(combatSubscribers.Count - 1); }
            check(!subscriberCalled, "both subscriber guards run before either iterator calls external code");

            var externalPower = (ExternalEnemyPower)new ExternalEnemyPower().ToMutable();
            externalPower.ApplyInternal(enemy, 1, silent: true);
            try
            {
                CheckUnknown("external_listener_model");
                check(!externalPower.PrimaryGuardRead, "the primary-enemy guard does not call an external power getter");
            }
            finally { enemy.RemovePowerInternal(externalPower); }

            combat.RemoveCreature(enemy);
            _endingHookCalls = 0;
            CheckUnknown("no_living_primary_enemy");
            check(_endingHookCalls == 0, "the primary-enemy check does not enter the ending hook");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            turnField.SetValue(manager, previousTurn);
            if (combat.Enemies.Contains(enemy)) combat.RemoveCreature(enemy);
            player.Deck.RemoveInternal(deckCard, silent: true);
            player.RunState.RemoveCard(deckCard);
        }

        void AddDraw(CardModel canonical)
        {
            var card = canonical.ToMutable();
            combat.AddCard(card, player);
            draw.AddInternal(card, silent: true);
        }
        Dictionary<string, object?>? Read(string screen = "monster")
        {
            var state = new Dictionary<string, object?>();
            attach.Invoke(null, [state, player, screen]);
            return (Dictionary<string, object?>?)state.GetValueOrDefault(Key);
        }
        void CheckUnknown(string reason)
        {
            var state = Read();
            check(state != null && state["known"] is false && state["combat"] == null && state["run"] == null
                && ((IEnumerable<string>)state["unavailable_reasons"]!).SequenceEqual(new[] { reason }),
                "a failed observation omits both scopes and exposes the reason: " + reason);
        }
    }

    private static bool SettledInput(bool checkCombatEnding, ref bool __result)
    {
        if (checkCombatEnding) throw new InvalidOperationException("the early guard must not check combat ending");
        __result = _settled;
        return false;
    }

    private static bool EndingHook(ref bool __result)
    {
        _endingHookCalls++;
        __result = false;
        return false;
    }

    private static IList SubscriberList(string field) => (IList)typeof(ModHelper).GetField(field, PrivateStatic)!.GetValue(null)!;
    private static Dictionary<string, object?> Row(object? value) => (Dictionary<string, object?>)value!;
    private static List<Dictionary<string, object?>> Rows(object? value) => (List<Dictionary<string, object?>>)value!;
    private static int Count(IEnumerable<Dictionary<string, object?>> rows, string category, string id, Type type)
        => rows.Where(row => Equals(row["category"], category) && Equals(row["id"], id)
            && Equals(row["concrete_type"], type.FullName)).Sum(row => (int)row["count"]!);

    private sealed class ExternalEnemyPower : PowerModel
    {
        internal bool PrimaryGuardRead { get; private set; }
        public override PowerType Type => PowerType.Buff;
        public override PowerStackType StackType => PowerStackType.Single;
        public override bool OwnerIsSecondaryEnemy
        {
            get
            {
                PrimaryGuardRead = true;
                return false;
            }
        }
    }
}
