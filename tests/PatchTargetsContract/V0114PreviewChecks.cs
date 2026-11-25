using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.ValueProps;
using Wuppy = MegaCrit.Sts2.Core.Models.Monsters.Wuppy;

static class V0114PreviewChecks
{
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

    internal static void Run(Type mod, Action<bool, string> check, Player player, Player ally, CombatState combat, List<RelicModel> relics)
    {
        object? Call(string name, params object?[] args) => mod.GetMethod(name, PrivateStatic)!.Invoke(null, args);
        static Dictionary<string, object?> Row(object? value) => (Dictionary<string, object?>)value!;
        static Dictionary<string, object?> Input(Dictionary<string, object?> row) => Row(row["damage_input"]);
        static Dictionary<string, string> Reasons(Dictionary<string, object?> row) =>
            (Dictionary<string, string>)row["unavailable_reasons"]!;
        var jsonOptions = (JsonSerializerOptions)mod.GetField("_jsonOptions", PrivateStatic)!.GetValue(null)!;

        player.ResetCombatState();
        ally.ResetCombatState();
        var strike = ModelDb.Card<StrikeIronclad>().ToMutable();
        combat.AddCard(strike, player);
        player.PlayerCombatState!.Hand.AddInternal(strike, silent: true);
        var enemy = new Creature(ModelDb.Monster<Nibbit>().ToMutable(), CombatSide.Enemy, null)
        {
            CombatState = combat,
            CombatId = 20
        };
        combat.AddCreature(enemy);
        enemy.SetMaxHpInternal(20);
        enemy.SetCurrentHpInternal(20);
        enemy.GainBlockInternal(3);
        var second = new Creature(ModelDb.Monster<Nibbit>().ToMutable(), CombatSide.Enemy, null)
        {
            CombatState = combat,
            CombatId = 22
        };
        combat.AddCreature(second);
        second.SetMaxHpInternal(20);
        second.SetCurrentHpInternal(20);
        second.GainBlockInternal(12);
        var hardToKill = ModelDb.Power<HardToKillPower>().ToMutable();
        hardToKill.ApplyInternal(enemy, 1, silent: true);

        var manager = CombatManager.Instance;
        var turnField = typeof(CombatManager).GetField("_turnState", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var previousTurn = turnField.GetValue(manager);
        var turn = Activator.CreateInstance(turnField.FieldType, combat)!;
        turn.GetType().GetProperty("IsInProgress")!.SetValue(turn, true);
        turnField.SetValue(manager, turn);
        try
        {
            var attempt = Call("CaptureCombatAttempt", combat, false)!;
            Dictionary<string, object?> Log() => Row(Call("BuildCombatLogState", attempt, null));
            List<Dictionary<string, object?>> Previews(CardModel card) =>
                (List<Dictionary<string, object?>>)Call("BuildCardTargetPreviews", card)!;
            Dictionary<string, object?> Preview(CardModel card) => Previews(card).Single(row => Equals(row["combat_id"], 20u));
            void CheckInput(Dictionary<string, object?> row, string name, decimal baseValue, decimal beforeBlock,
                uint? modifierDealer, ValueProp props = ValueProp.Move)
            {
                var input = Input(row);
                check(Equals(input["var_name"], name) && Equals(input["base_for_target"], baseValue)
                    && Equals(input["before_block"], beforeBlock) && Equals(input["props"], (int)props)
                    && Equals(input["modifier_dealer_combat_id"], modifierDealer)
                    && Equals(input["preview_dealer_combat_id"], player.Creature.CombatId)
                    && !input.ContainsKey("unavailable_reasons"), $"observed inputs for {name} vs {row["combat_id"]}");
            }
            var before = Log()["next_seq"];
            var preview = Preview(strike);
            check(Equals(preview["damage"], 4) && Equals(Row(preview["values"])["Damage"], 4)
                && Equals(Row(preview["damage_cap"])["per_hit_max"], 1) && !Row(preview["damage_cap"]).ContainsKey("effective_damage"),
                "real Strike preview includes 3 Block and 1 HP loss after HardToKill");
            CheckInput(preview, "Damage", 6m, 6m, player.Creature.CombatId);
            check(enemy.Block == 3 && enemy.CurrentHp == 20 && Equals(Log()["next_seq"], before),
                "targeted dry run leaves the enemy and the combat log unchanged");

            try
            {
                CreatureCmd.GetDamageResultsPreview(null!, combat, enemy, 6, ValueProp.Move, player.Creature, strike);
                throw new Exception("the preview with no run state should fail");
            }
            catch (NullReferenceException) { }
            var hpLoss = Hook.ModifyHpLost(player.RunState, combat, enemy, 3, ValueProp.Move,
                player.Creature, strike, HpLossHookPhase.BeforeOsty, out _);
            var after = Log();
            var rows = (IEnumerable<Dictionary<string, object?>>)after["rows"]!;
            check(hpLoss == 1 && !Equals(after["next_seq"], before) && Equals(rows.Last()["hook"], "ModifyHpLost"),
                "a real HP-loss correction is logged after a throwing preview");

            enemy.RemovePowerInternal(hardToKill);
            var slippery = ModelDb.Power<SlipperyPower>().ToMutable();
            slippery.ApplyInternal(enemy, 1, silent: true);
            var savedHand = player.PlayerCombatState!.Hand.Cards.ToArray();
            var beforeSlippery = Log()["next_seq"];
            const decimal preciseDamage = 6.123456789012345678901234567m;
            strike.DynamicVars.Damage.BaseValue = preciseDamage;
            strike.DynamicVars.Damage.Props = ValueProp.Move | ValueProp.SkipHurtAnim;
            for (int i = 0; i < 2; i++)
            {
                var rowsByTarget = Previews(strike).ToDictionary(row => (uint)row["combat_id"]!);
                CheckInput(rowsByTarget[20u], "Damage", preciseDamage, preciseDamage, player.Creature.CombatId,
                    strike.DynamicVars.Damage.Props);
                CheckInput(rowsByTarget[22u], "Damage", preciseDamage, preciseDamage, player.Creature.CombatId,
                    strike.DynamicVars.Damage.Props);
                check(rowsByTarget.Count == 2 && Equals(rowsByTarget[20u]["damage"], 4)
                    && Equals(rowsByTarget[22u]["damage"], 6), "Slippery and full Block retain the existing displays");
                using var json = JsonDocument.Parse(JsonSerializer.Serialize(rowsByTarget[20u], jsonOptions));
                var input = json.RootElement.GetProperty("damage_input");
                check(input.GetProperty("base_for_target").GetDecimal() == preciseDamage
                    && input.GetProperty("before_block").GetDecimal() == preciseDamage,
                    "serialized JSON number retains decimal precision");
                check(enemy.CurrentHp == 20 && enemy.Block == 3 && slippery.Amount == 1
                    && second.CurrentHp == 20 && second.Block == 12
                    && player.PlayerCombatState.Hand.Cards.SequenceEqual(savedHand)
                    && Equals(Log()["next_seq"], beforeSlippery),
                    "each read preserves HP, Block, Slippery stacks, hand order and log sequence");
            }
            strike.DynamicVars.Damage.BaseValue = 6m;
            strike.DynamicVars.Damage.Props = ValueProp.Move;
            enemy.RemovePowerInternal(slippery);

            var shell = ModelDb.Power<HardenedShellPower>().ToMutable();
            shell.ApplyInternal(enemy, 2, silent: true);
            var slam = ModelDb.Card<BodySlam>().ToMutable();
            combat.AddCard(slam, player);
            player.PlayerCombatState!.Hand.AddInternal(slam, silent: true);
            player.Creature.GainBlockInternal(10);
            var slamPreview = Preview(slam);
            check(Equals(slamPreview["damage"], 5) && shell.DisplayAmount == 2,
                "CalculatedDamage includes 3 Block and the 2 HP HardenedShell allowance without using it");
            CheckInput(slamPreview, "CalculatedDamage", 10m, 10m, player.Creature.CombatId);
            player.Creature.LoseBlockInternal(10);
            player.PlayerCombatState.Hand.RemoveInternal(slam, silent: true);
            combat.RemoveCard(slam);
            enemy.RemovePowerInternal(shell);

            var intangible = ModelDb.Power<IntangiblePower>().ToMutable();
            intangible.ApplyInternal(enemy, 1, silent: true);
            var capped = Preview(strike);
            CheckInput(capped, "Damage", 6m, 1m, player.Creature.CombatId);
            check(Equals(capped["damage"], 1) && intangible.Amount == 1, "Intangible caps damage before Block");
            enemy.RemovePowerInternal(intangible);

            var bully = ModelDb.Card<Bully>().ToMutable();
            var mixedVars = new DynamicVarSet(bully.DynamicVars.Values.Concat([new DamageVar(37m, ValueProp.Move), new HitCountVar(2)]));
            SetTestVars(bully, mixedVars);
            mixedVars.ExtraDamage.BaseValue = 2.125m;
            var vulnerable = ModelDb.Power<VulnerablePower>().ToMutable();
            vulnerable.ApplyInternal(enemy, 1, silent: true);
            combat.AddCard(bully, player);
            player.PlayerCombatState.Hand.AddInternal(bully, silent: true);
            var mixed = Previews(bully).ToDictionary(row => (uint)row["combat_id"]!);
            CheckInput(mixed[20u], "CalculatedDamage", 6.125m, 9.1875m, player.Creature.CombatId);
            CheckInput(mixed[22u], "CalculatedDamage", 4m, 4m, player.Creature.CombatId);
            check(Equals(mixed[20u]["damage"], 9) && Equals(Row(mixed[20u]["values"])["CalculatedDamage"], 9)
                && !Equals(Row(mixed[20u]["values"])["Damage"], 9) && Equals(mixed[20u]["hits"], 2)
                && Equals(mixed[20u]["total_damage"], 18) && Equals(mixed[22u]["damage"], 4),
                "target-dependent calculation takes precedence over the other Damage var and preserves hit totals");
            enemy.RemovePowerInternal(vulnerable);
            mixedVars.CalculatedDamage.FromOsty();
            CheckInput(Preview(bully), "CalculatedDamage", 4m, 4m, null);
            var osty = new Creature(ModelDb.Monster<Osty>().ToMutable(), CombatSide.Player, null)
            {
                CombatState = combat,
                CombatId = 23
            };
            player.PlayerCombatState.AddPetInternal(osty);
            combat.AddCreature(osty);
            CheckInput(Preview(bully), "CalculatedDamage", 4m, 4m, 23u);
            osty.CombatId = null;
            var missingDealer = Input(Preview(bully));
            check(missingDealer["modifier_dealer_combat_id"] == null
                && Equals(missingDealer["preview_dealer_combat_id"], player.Creature.CombatId)
                && Reasons(missingDealer).ContainsKey("modifier_dealer_combat_id"),
                "missing combat ID is distinct from the game's null dealer");
            combat.RemoveCreature(osty);
            ((List<Creature>)player.PlayerCombatState.Pets).Remove(osty);
            player.PlayerCombatState.Hand.RemoveInternal(bully, silent: true);
            check(Previews(bully).All(row => !row.ContainsKey("damage_input")), "only cards in the hand expose the inputs");
            combat.RemoveCard(bully);

            // The nested preview uses the exact same card, variable, and target. It completes both calls,
            // then the outer calculation throws before its first call. Its inputs must still be unknown.
            var recursive = ModelDb.Card<BodySlam>().ToMutable();
            bool nested = false;
            var recursiveDamage = new CalculatedDamageVar(ValueProp.Move).WithMultiplier((card, target) =>
            {
                if (!ReferenceEquals(target, enemy)) return 2m;
                if (nested) return 30m;
                nested = true;
                try { card.UpdateDynamicVarPreview(CardPreviewMode.Normal, target, card.DynamicVars); }
                finally { nested = false; }
                throw new InvalidOperationException("outer calculation did not produce an input");
            });
            SetTestVars(recursive, new DynamicVarSet([new CalculationBaseVar(0m), new ExtraDamageVar(1m), recursiveDamage]));
            combat.AddCard(recursive, player);
            player.PlayerCombatState.Hand.AddInternal(recursive, silent: true);
            var recursiveRows = Previews(recursive).ToDictionary(row => (uint)row["combat_id"]!);
            var outerInput = Input(recursiveRows[20u]);
            check(outerInput["base_for_target"] == null && outerInput["before_block"] == null
                && Reasons(outerInput).ContainsKey("base_for_target") && Reasons(outerInput).ContainsKey("before_block"),
                "a nested preview cannot fill in the outer variable's missing observations");
            CheckInput(recursiveRows[22u], "CalculatedDamage", 2m, 2m, player.Creature.CombatId);
            player.PlayerCombatState.Hand.RemoveInternal(recursive, silent: true);
            combat.RemoveCard(recursive);

            var boot = ModelDb.Relic<TheBoot>().ToMutable();
            boot.Owner = player;
            relics.Add(boot);
            enemy.SetCurrentHpInternal(1);
            var beforeBoot = Log()["next_seq"];
            var overkill = Preview(strike);
            CheckInput(overkill, "Damage", 6m, 6m, player.Creature.CombatId);
            check(Equals(overkill["damage"], 8), "The Boot raises HP loss to 5, including overkill, on top of 3 Block");
            relics.Remove(boot);
            check(enemy.CurrentHp == 1 && enemy.Block == 3 && Equals(Log()["next_seq"], beforeBoot),
                "the overkill preview leaves HP, Block, and the log unchanged");
            enemy.SetCurrentHpInternal(20);

            var throwPatch = new Harmony("test.damage-input");
            var hook = AccessTools.DeclaredMethod(typeof(Hook), nameof(Hook.ModifyDamage));
            throwPatch.Patch(hook, postfix: new HarmonyMethod(typeof(V0114PreviewChecks), nameof(FlipDamageProps)));
            try
            {
                var mismatch = Input(Preview(strike));
                check(Equals(mismatch["base_for_target"], 6m) && mismatch["before_block"] == null
                    && mismatch["props"] == null && mismatch["preview_dealer_combat_id"] == null
                    && Reasons(mismatch).ContainsKey("props"), "mismatched calls retain only the verified modifier input");
            }
            finally
            {
                throwPatch.Unpatch(hook, HarmonyPatchType.Postfix, throwPatch.Id);
                strike.DynamicVars.Damage.Props = ValueProp.Move;
            }
            throwPatch.Patch(hook, prefix: new HarmonyMethod(typeof(V0114PreviewChecks), nameof(FailDamageModifier)));
            try
            {
                var interrupted = Previews(strike).ToDictionary(row => (uint)row["combat_id"]!);
                var partial = Input(interrupted[20u]);
                check(Equals(partial["base_for_target"], 6m) && partial["before_block"] == null
                    && Reasons(partial).ContainsKey("before_block")
                    && !interrupted[20u].ContainsKey("damage") && interrupted[20u]["values"] == null,
                    "a throw between stages retains the row and only the observed input");
                CheckInput(interrupted[22u], "Damage", 6m, 6m, player.Creature.CombatId);
            }
            finally { throwPatch.Unpatch(hook, HarmonyPatchType.Prefix, throwPatch.Id); }
            var beforeReal = Log()["next_seq"];
            for (int i = 0; i < 2; i++)
                CheckInput(Preview(strike), "Damage", 6m, 6m, player.Creature.CombatId);
            check(Equals(Log()["next_seq"], beforeReal), "reads after an interrupted preview still leave the log unchanged");
            var previousTestMode = TestMode.IsOn;
            try
            {
                TestMode.IsOn = true;
                CreatureCmd.Damage(new ThrowingPlayerChoiceContext(), enemy, 6m, ValueProp.Move | ValueProp.SkipHurtAnim,
                    player.Creature, strike, null).GetAwaiter().GetResult();
            }
            finally { TestMode.IsOn = previousTestMode; }
            var hitRows = (IEnumerable<Dictionary<string, object?>>)Row(Call("BuildCombatLogState", attempt, beforeReal))["rows"]!;
            check(enemy.CurrentHp == 17 && enemy.Block == 0 && hitRows.Count(row => Equals(row["kind"], "damage_received")) == 1,
                "a real CreatureCmd.Damage immediately after the reads consumes Block and logs one damage_received");

            var wuppy = new Creature(ModelDb.Monster<Wuppy>().ToMutable(), CombatSide.Player, null)
            {
                CombatState = combat,
                CombatId = 21
            };
            wuppy.SetMaxHpInternal(18);
            wuppy.SetCurrentHpInternal(11);
            Call("CombatLogCreatureAdding", wuppy);
            var added = ((IEnumerable<Dictionary<string, object?>>)Log()["rows"]!).Last();
            check(Equals(added["action"], "added") && Equals(added["hp"], 11) && Equals(added["max_hp"], 18),
                "the Wuppy added row carries the values already set internally");

            wuppy.PetOwner = player;
            combat.AddCreature(wuppy);
            var previousHp = enemy.CurrentHp;
            var previousMove = enemy.Monster!.IsPerformingMove;
            var previousAttack = player.Creature.IsAttacking;
            var moveProperty = typeof(MonsterModel).GetProperty(nameof(MonsterModel.IsPerformingMove))!;
            try
            {
                enemy.SetCurrentHpInternal(0);
                moveProperty.SetValue(enemy.Monster, true);
                player.Creature.IsAttacking = true;
                var beforeActivity = Log()["next_seq"];
                Call("RefreshEntityIdRegistry", combat);
                var battle = new Dictionary<string, object?>();
                Call("AttachCreatureActivity", battle, combat);
                var activity = (List<Dictionary<string, object?>>)battle["creature_activity"]!;
                var self = activity.Single(row => Equals(row["combat_id"], player.Creature.CombatId));
                var pet = activity.Single(row => Equals(row["combat_id"], 21u));
                var dying = activity.Single(row => Equals(row["combat_id"], 20u));
                check(Equals(self["player_id"], player.NetId.ToString()) && Equals(self["is_attacking"], true)
                    && !self.ContainsKey("is_performing_move"), "installed DLL: player attack flag and Monster applicability");
                check(Equals(pet["kind"], "pet") && Equals(pet["owner_player_id"], player.NetId.ToString())
                    && Equals(pet["is_attacking"], false) && Equals(pet["is_performing_move"], false),
                    "installed DLL: pet ownership and idle flags");
                check(enemy.IsDead && Equals(dying["is_performing_move"], true)
                    && Equals(dying["entity_id"], Call("GetStableEntityId", enemy)),
                    "installed DLL: active move from a dead enemy still in CombatState.Creatures");
                check(Equals(Log()["next_seq"], beforeActivity) && wuppy.CurrentHp == 11 && enemy.CurrentHp == 0,
                    "reading the installed DLL's activity flags leaves HP and combat log unchanged");
            }
            finally
            {
                player.Creature.IsAttacking = previousAttack;
                moveProperty.SetValue(enemy.Monster, previousMove);
                enemy.SetCurrentHpInternal(previousHp);
                combat.RemoveCreature(wuppy);
            }
        }
        finally
        {
            turnField.SetValue(manager, previousTurn);
            player.PlayerCombatState!.Hand.RemoveInternal(strike, silent: true);
            combat.RemoveCard(strike);
            combat.RemoveCreature(enemy);
            combat.RemoveCreature(second);
        }

        var baseCard = ModelDb.Card<StrikeIronclad>();
        var ready = new Dictionary<string, object?>();
        Call("AttachUpgradePreview", ready, baseCard);
        check(ready["upgrade_preview"] is Dictionary<string, object?> && baseCard.CurrentUpgradeLevel == 0,
            "a normal reward or selection preview upgrades a separate card");

        var patch = new Harmony("test.upgrade-preview");
        var target = AccessTools.DeclaredMethod(typeof(CardModel), nameof(CardModel.UpgradeInternal), [typeof(bool)])!;
        patch.Patch(target, prefix: new HarmonyMethod(typeof(V0114PreviewChecks), nameof(FailUpgrade)));
        try
        {
            var row = new Dictionary<string, object?> { ["id"] = baseCard.Id.Entry };
            Call("AttachUpgradePreview", row, baseCard);
            check(row["upgrade_preview"] == null && Equals(row["id"], "STRIKE_IRONCLAD")
                && ((Dictionary<string, string>)row["unavailable_reasons"]!)["upgrade_preview"]
                    .Contains("upgrade failure supplied by the contract", StringComparison.Ordinal),
                "a failed silent reward or selection preview retains the base card row and reason");
            var candidate = Activator.CreateInstance(mod.GetNestedType("WikiCandidate", BindingFlags.NonPublic)!,
                ["card", baseCard.Id.Entry, "Strike", "Strike", baseCard, null, true]);
            var wiki = Row(Call("BuildWikiCardResult", candidate, 1d));
            check(wiki["base"] is Dictionary<string, object?> && Row(wiki["upgraded"])["preview_available"] is false,
                "a failed wiki upgrade retains the base variant");
        }
        finally { patch.Unpatch(target, HarmonyPatchType.Prefix, patch.Id); }
    }

    private static void FailUpgrade(bool silent)
    {
        if (!silent) throw new Exception("preview attempted to notify CardModel.Upgraded");
        throw new InvalidOperationException("upgrade failure supplied by the contract");
    }

    private static void SetTestVars(CardModel card, DynamicVarSet vars)
    {
        typeof(CardModel).GetField("_dynamicVars", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(card, vars);
        vars.InitializeWithOwner(card);
    }

    private static void FlipDamageProps(Creature? target, CardModel? cardSource)
    {
        if (target?.CombatId == 20u && cardSource is StrikeIronclad)
            cardSource.DynamicVars.Damage.Props |= ValueProp.SkipHurtAnim;
    }

    private static void FailDamageModifier(Creature? target, CardPreviewMode previewMode)
    {
        if (target?.CombatId == 20u && previewMode == CardPreviewMode.Normal)
            throw new InvalidOperationException("damage modifier failure supplied by the contract");
    }
}
