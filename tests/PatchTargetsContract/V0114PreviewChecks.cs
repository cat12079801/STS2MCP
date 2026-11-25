using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.ValueProps;
using Wuppy = MegaCrit.Sts2.Core.Models.Monsters.Wuppy;

static class V0114PreviewChecks
{
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

    internal static void Run(Type mod, Action<bool, string> check, Player player, CombatState combat, List<RelicModel> relics)
    {
        object? Call(string name, params object?[] args) => mod.GetMethod(name, PrivateStatic)!.Invoke(null, args);
        static Dictionary<string, object?> Row(object? value) => (Dictionary<string, object?>)value!;

        player.ResetCombatState();
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
            Dictionary<string, object?> Preview(CardModel card) =>
                ((List<Dictionary<string, object?>>)Call("BuildCardTargetPreviews", card)!)
                    .Single(row => Equals(row["combat_id"], 20u));
            var before = Log()["next_seq"];
            var preview = Preview(strike);
            check(Equals(preview["damage"], 4) && Equals(Row(preview["values"])["Damage"], 4)
                && Equals(Row(preview["damage_cap"])["per_hit_max"], 1) && !Row(preview["damage_cap"]).ContainsKey("effective_damage"),
                "real Strike preview includes 3 Block and 1 HP loss after HardToKill");
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
            check(Equals(Preview(strike)["damage"], 4) && slippery.Amount == 1,
                "Slippery is reflected in one hit without consuming the stack");
            enemy.RemovePowerInternal(slippery);

            var shell = ModelDb.Power<HardenedShellPower>().ToMutable();
            shell.ApplyInternal(enemy, 2, silent: true);
            var slam = ModelDb.Card<BodySlam>().ToMutable();
            combat.AddCard(slam, player);
            player.PlayerCombatState!.Hand.AddInternal(slam, silent: true);
            player.Creature.GainBlockInternal(10);
            check(Equals(Preview(slam)["damage"], 5), "CalculatedDamage includes 3 Block and the 2 HP HardenedShell allowance");
            player.Creature.LoseBlockInternal(10);
            player.PlayerCombatState.Hand.RemoveInternal(slam, silent: true);
            combat.RemoveCard(slam);
            enemy.RemovePowerInternal(shell);

            var boot = ModelDb.Relic<TheBoot>().ToMutable();
            boot.Owner = player;
            relics.Add(boot);
            enemy.SetCurrentHpInternal(1);
            var beforeBoot = Log()["next_seq"];
            check(Equals(Preview(strike)["damage"], 8), "The Boot raises HP loss to 5, including overkill, on top of 3 Block");
            relics.Remove(boot);
            check(enemy.CurrentHp == 1 && enemy.Block == 3 && Equals(Log()["next_seq"], beforeBoot),
                "the overkill preview leaves HP, Block, and the log unchanged");

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
        }
        finally
        {
            turnField.SetValue(manager, previousTurn);
            player.PlayerCombatState!.Hand.RemoveInternal(strike, silent: true);
            combat.RemoveCard(strike);
            combat.RemoveCreature(enemy);
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
}
