using System.Reflection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.PotionPools;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Odds;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;

static class EntropicBrewChecks
{
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static void Run(Type mod, Action<bool, string> check)
    {
        object? Invoke(string method, params object?[] args) =>
            (mod.GetMethod(method, PrivateStatic) ?? throw new MissingMethodException(method)).Invoke(null, args);
        Dictionary<string, object?> Row(object? value) => (Dictionary<string, object?>)value!;
        void Reject(string method, string reason, params object?[] args)
        {
            try { Invoke(method, args); }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException error)
            {
                check(error.Message.Contains(reason, StringComparison.Ordinal), method + ": " + error.Message);
                return;
            }
            throw new Exception(method + " accepted invalid input");
        }

        var hash = ((Lazy<string>)mod.GetField("GenerationAssemblySha256", PrivateStatic)!.GetValue(null)!).Value;
        var thresholds = Row(Invoke("ReadEntropicBrewThresholds", hash));
        check(Equals(thresholds["Rare"], 0.1f) && Equals(thresholds["Uncommon"], 0.35f) && Equals(thresholds["Common"], 1f)
            && Equals(thresholds["comparison"], "first_upper_bound_inclusive")
            && Equals(thresholds["within_rarity"], "uniform_by_occurrence")
            && Equals(thresholds["between_slots"], "with_replacement"), "Brew publishes the audited float32 thresholds and selection rules");
        Reject("ReadEntropicBrewThresholds", "unverified", new string('0', 64));

        Dictionary<string, object?> Option(string id, string? rarity) => new() { ["id"] = id, ["rarity"] = rarity };
        Dictionary<string, object?> Unlocked(List<string>? character, List<string>? shared) => new()
        {
            ["potions"] = new Dictionary<string, object?> { ["character"] = character, ["shared"] = shared }
        };
        List<Dictionary<string, object?>> duplicates = [Option("A", "Rare"), Option("B", "Uncommon"),
            Option("C", "Common"), Option("C", "Common")];
        Invoke("CheckEntropicBrewOptions", Unlocked([], ["A", "B", "C", "C"]), duplicates);
        check(true, "an empty character pool and the factory's duplicate occurrences are accepted");
        Reject("CheckEntropicBrewOptions", "multiset", Unlocked([], ["A", "B", "C"]), duplicates);
        Reject("CheckEntropicBrewOptions", "unobserved", Unlocked(null, ["A", "B", "C", "C"]), duplicates);
        Reject("CheckEntropicBrewOptions", "positive probability", Unlocked([], []), new List<Dictionary<string, object?>>());
        Reject("CheckEntropicBrewOptions", "unknown id or rarity", Unlocked([], ["C"]), new List<Dictionary<string, object?>> { Option("C", null) });

        // Give the actual game factory its potion pool, without initializing the engine or loading save data.
        var game = typeof(PotionModel).Assembly;
        var potionTypes = game.GetTypes().Where(t => !t.IsAbstract && t.Namespace == typeof(FirePotion).Namespace
            && t.IsSubclassOf(typeof(PotionModel)));
        ModelDb.Init(potionTypes.Concat(new[] { typeof(IroncladPotionPool), typeof(SharedPotionPool),
            typeof(ReptileTrinket), typeof(BeltBuckle), typeof(Sozu), typeof(SurroundedPower),
            typeof(RocketPunch), typeof(Stomp), typeof(RingingPower), typeof(JackOfAllTrades),
            typeof(DefendIronclad), typeof(Sharp), typeof(Nibbit), typeof(StrikeIronclad), typeof(BodySlam),
            typeof(HardToKillPower), typeof(SlipperyPower), typeof(HardenedShellPower), typeof(TheBoot),
            typeof(MegaCrit.Sts2.Core.Models.Monsters.Wuppy) }).ToArray());
        typeof(ModelDb).GetField("_allPotionPools", PrivateStatic)!.SetValue(null,
            new PotionPoolModel[] { ModelDb.PotionPool<IroncladPotionPool>(), ModelDb.PotionPool<SharedPotionPool>() });
        typeof(ModelDb).GetField("_allEncounters", PrivateStatic)!.SetValue(null, Array.Empty<EncounterModel>());
        var ironclad = new Ironclad();
        var playerConstructor = typeof(Player).GetConstructors(PrivateInstance).Single();
        Player NewPlayer(ulong id) => (Player)playerConstructor.Invoke(
            [ironclad, id, 70, 80, 3, 99, 3, 0, new RelicGrabBag(), UnlockState.none, null, null, null, null, null]);
        var owner = NewPlayer(17);
        var ally = NewPlayer(18);
        var runConstructor = typeof(RunState).GetConstructors(PrivateInstance).Single();
        var rng = new RunRngSet("brew-contract");
        var run = (RunState)runConstructor.Invoke(new object?[] { new Player[] { owner, ally }, Array.Empty<ActModel>(),
            Array.Empty<ModifierModel>(), GameMode.Standard, false, 0, rng, new RunOddsSet(rng.UnknownMapPoint),
            new RelicGrabBag(), 0 });
        owner.RunState = run;
        ally.RunState = run;
        var combat = new CombatState(runState: run);
        owner.Creature.CombatState = combat;
        owner.Creature.CombatId = 1;
        ally.Creature.CombatState = combat;
        ally.Creature.CombatId = 2;
        combat.AddCreature(owner.Creature);
        combat.AddCreature(ally.Creature);
        var belt = (List<PotionModel?>)typeof(Player).GetField("_potionSlots", PrivateInstance)!.GetValue(owner)!;
        var brew = ModelDb.Potion<EntropicBrew>().ToMutable();
        brew.Owner = owner;
        belt[1] = brew;

        var relics = (List<RelicModel>)typeof(Player).GetField("_relics", PrivateInstance)!.GetValue(owner)!;
        var trinket = ModelDb.Relic<ReptileTrinket>().ToMutable();
        var buckle = ModelDb.Relic<BeltBuckle>().ToMutable();
        var sozu = ModelDb.Relic<Sozu>().ToMutable();
        var melted = ModelDb.Relic<ReptileTrinket>().ToMutable();
        melted.IsMelted = true;
        foreach (var relic in new[] { trinket, buckle, sozu, melted })
        {
            relic.Owner = owner;
            relics.Add(relic);
        }
        var power = ModelDb.Power<SurroundedPower>().ToMutable();
        power.ApplyInternal(owner.Creature, 1, silent: true);
        var block = ModelDb.Potion<BlockPotion>();
        var canonicalVars = block.DynamicVars;
        var canonicalEnergy = ModelDb.Potion<EnergyPotion>().DynamicVars.Energy;
        canonicalEnergy.ColorPrefix = "test-original";
        var playerRandomBefore = owner.PlayerRng.ToSerializable();
        var runRandomBefore = run.Rng.ToSerializable();
        var realOptions = PotionFactory.GetPotionOptions(owner).Select(p => p.Id.Entry).ToList();

        // Exercise the same live verdict builder with the holder facts supplied by the slot
        // projection. FruitJuice can be checked without a native scene or an enemy node.
        var juice = ModelDb.Potion<FruitJuice>().ToMutable();
        juice.Owner = owner;
        var slotType = mod.GetNestedType("FreshPotionSlot", BindingFlags.NonPublic)!;
        object Slot(bool usable, bool disabled, string? unavailable = null)
            => Activator.CreateInstance(slotType, usable, disabled, unavailable)!;
        object Verdict(object? slot)
            => Invoke("DecidePotionUsability", Invoke("BuildPotionUsabilityReads", owner, juice, 0, slot))!;
        object? Fact(object verdict, string name) => verdict.GetType().GetField(name)!.GetValue(verdict);
        var available = Verdict(Slot(true, false));
        check(Fact(available, "CanUse") is true && ((List<string>)Fact(available, "TargetIds")!).SequenceEqual(new[] { "player:17" }),
            "a fresh usable slot projects the owning player's stable target");
        check(Equals(Fact(Verdict(Slot(true, true)), "Reason"), "holder_disabled")
            && Equals(Fact(Verdict(Slot(false, false)), "Reason"), "holder_not_usable"),
            "a newly filled slot still obeys the holder's projected flags");
        check(Equals(Fact(Verdict(Slot(false, false, "holder mismatch")), "Reason"), "unobserved:holder_model")
            && Fact(Verdict(null), "CanUse") == null, "a missing holder or a held-model mismatch cannot authorize real use");

        for (int repeat = 0; repeat < 2; repeat++)
        {
            var pool = Row(Invoke("BuildEntropicBrewPool", owner, Unlocked([], realOptions), "hand_select"));
            var options = (List<Dictionary<string, object?>>)pool["options"]!;
            check(pool["known"] is true && Equals(pool["player_id"], "17") && Equals(pool["players"], 2)
                && options.Select(row => (string)row["id"]!).SequenceEqual(realOptions.Order(StringComparer.Ordinal)),
                "Brew publishes the real factory's options and owner with no Ironclad unlocks");
            check(options.Any(row => Equals(row["id"], "FAIRY_IN_A_BOTTLE") && Equals(row["usage"], "Automatic"))
                && options.Any(row => Equals(row["id"], "FRUIT_JUICE")) && options.Any(row => Equals(row["id"], "ENTROPIC_BREW")),
                "the out-of-combat pool includes eligible fairy, juice and Brew");

            var preview = Row(pool["preview"]);
            var slots = (List<Dictionary<string, object?>>)preview["slots"]!;
            check(slots.Count == 3 && Equals(slots[1]["requires"], "remove_used_potion")
                && Equals(slots[1]["current_potion_id"], "ENTROPIC_BREW") && Equals(slots[2]["requires"], "empty"),
                "the preview covers every physical slot");
            var option = options.Single(row => Equals(row["id"], "BLOCK_POTION"));
            var availability = ((List<Dictionary<string, object?>>)option["slot_verdicts"]!).Single();
            check(((List<int>)availability["slots"]!).SequenceEqual(new[] { 0, 1, 2 })
                && availability["can_use_if_generated"] == null
                && Equals(availability["can_use_if_generated_reason"], "unobserved:preview_scope"), "a pending selection leaves the conditional verdict unknown");
            check(((Dictionary<string, decimal>)option["vars"]!)["Block"] == 12m
                && option["name"] == null && RowReasons(option).ContainsKey("name"),
                "a localization failure retains the option and its rarity and vars");
            var listenerSection = Row(pool["listeners"]);
            var notifications = Row(listenerSection["notifications"]);
            var useListeners = (List<Dictionary<string, object?>>)notifications["after_potion_used"]!;
            var acquireListeners = (List<Dictionary<string, object?>>)notifications["should_procure_potion"]!;
            check(listenerSection["known"] is true && useListeners.Select(row => (string)row["model_id"]!)
                    .SequenceEqual(new[] { "REPTILE_TRINKET", "BELT_BUCKLE" })
                && Equals(useListeners[0]["owner_player_id"], "17") && Equals(useListeners[1]["owner_index"], 1)
                && Equals(acquireListeners.Single()["model_id"], "SOZU")
                && Equals(((List<Dictionary<string, object?>>)notifications["before_potion_used"]!).Single()["owner_combat_id"], 1u)
                && Equals(((List<Dictionary<string, object?>>)notifications["after_potion_procured"]!).Single()["model_id"], "BELT_BUCKLE"),
                "the four override lists keep game order, omit melted relics and identify the owning instance");
        }
        check(ReferenceEquals(block.DynamicVars, canonicalVars) && canonicalEnergy.ColorPrefix == "test-original"
            && ReferenceEquals(belt[1], brew)
            && belt[0] == null && belt[2] == null && !brew.IsQueued && ReferenceEquals(brew.Owner, owner),
            "repeated pool reads keep the canonical model and belt intact");
        check(owner.PlayerRng.ToSerializable().Rngs.SequenceEqual(playerRandomBefore.Rngs)
            && run.Rng.ToSerializable().Rngs.SequenceEqual(runRandomBefore.Rngs), "repeated pool reads leave all seeded RNG streams unchanged");

        var otherPool = Row(Invoke("BuildEntropicBrewPool", ally, Unlocked([], realOptions), "hand_select"));
        check(otherPool["known"] is true && Equals(otherPool["player_id"], "18")
            && ((List<Dictionary<string, object?>>)Row(otherPool["preview"])["slots"]!).All(slot => slot["current_potion_id"] == null),
            "switching players rebinds the pool and the belt");
        var stranger = NewPlayer(17);
        var unknownOwner = Row(Invoke("BuildEntropicBrewPool", stranger, Unlocked([], realOptions), "hand_select"));
        check(unknownOwner["known"] is false && unknownOwner["options"] == null && RowReasons(unknownOwner).ContainsKey("contract"),
            "a player absent from their run cannot publish a known pool");

        V0114PreviewChecks.Run(mod, check, owner, combat, relics);
        JackObservationChecks.Run(mod, check, owner, ally, combat);
        ChoicePotionChecks.Run(mod, check, owner, ally, combat);

        bool subscriberCalled = false;
        ModHelper.SubscribeForRunStateHooks("test.entropic-brew", _ =>
        {
            subscriberCalled = true;
            return Array.Empty<AbstractModel>();
        });
        var external = Row(Row(Invoke("BuildEntropicBrewPool", owner, Unlocked([], realOptions), "hand_select"))["listeners"]);
        check(external["known"] is false && external["notifications"] == null && !subscriberCalled
            && ((List<string>)external["unavailable_reasons"]!).Any(reason => reason.Contains("ModHelper")),
            "external listeners are unobserved without invoking the subscriber");
    }

    private static Dictionary<string, string> RowReasons(Dictionary<string, object?> row)
        => (Dictionary<string, string>)row["unavailable_reasons"]!;
}
