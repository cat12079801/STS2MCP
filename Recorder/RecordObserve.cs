using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace STS2_MCP.Recorder;

/// <summary>
/// Read-only views of the game for the record. Nothing here calls into game logic: only
/// property getters that return stored values (checked against the decompiled source of
/// v0.111.0 - RunRngSet.GetRng / PlayerRngSet.GetRng index a dictionary,
/// NetCombatCardDb.TryGetCardId is a dictionary lookup, MonsterModel.NextMove is an auto
/// property).
///
/// The field names follow the native worker's observation (tools/native/worker/Engine.cs
/// Observe in cat12079801/sts2), so an M3 replay can compare the two directly.
/// </summary>
internal static class RecordObserve
{
    private static readonly FieldInfo? RngCounter =
        typeof(MegaCrit.Sts2.Core.Random.Rng).GetField("_counter", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>round / turn / side / phase at this moment, or null outside combat.</summary>
    internal static JsonObject? At()
    {
        var combat = CombatManager.Instance.DebugOnlyGetState();
        if (combat == null)
            return null;
        var pcs = LocalPlayer()?.PlayerCombatState;
        return new JsonObject
        {
            ["round"] = combat.RoundNumber,
            ["turn"] = pcs?.TurnNumber,
            ["side"] = combat.CurrentSide.ToString(),
            ["phase"] = pcs?.Phase.ToString(),
            ["in_progress"] = CombatManager.Instance.IsInProgress,
        };
    }

    internal static Player? LocalPlayer()
    {
        var run = RunManager.Instance.DebugOnlyGetState();
        return run == null ? null : LocalContext.GetMe(run);
    }

    /// <summary>
    /// (public, hidden). public is what a human player can see; hidden is what they cannot
    /// (RNG state, the enemies' internal move ids, the draw pile's order).
    /// </summary>
    internal static (JsonObject pub, JsonObject hidden)? Observe(Func<ulong?, int?> slotOf)
    {
        var player = LocalPlayer();
        var run = RunManager.Instance.DebugOnlyGetState();
        if (player == null || run == null)
            return null;
        var state = CombatManager.Instance.DebugOnlyGetState();
        var pcs = player.PlayerCombatState;
        var pub = new JsonObject
        {
            ["in_combat"] = CombatManager.Instance.IsInProgress,
            ["round"] = state?.RoundNumber,
            ["side"] = state?.CurrentSide.ToString(),
            ["phase"] = pcs?.Phase.ToString(),
            ["turn"] = pcs?.TurnNumber,
            ["player"] = new JsonObject
            {
                ["hp"] = player.Creature.CurrentHp,
                ["max_hp"] = player.Creature.MaxHp,
                ["block"] = player.Creature.Block,
                ["energy"] = pcs?.Energy,
                ["max_energy"] = pcs?.MaxEnergy,
                ["gold"] = player.Gold,
                ["powers"] = Powers(player.Creature),
                ["relics"] = new JsonArray(player.Relics.Select(r => (JsonNode)JsonValue.Create(r.Id.Entry)!).ToArray()),
                ["potions"] = new JsonArray(player.Potions.Select(p => (JsonNode?)JsonValue.Create(p?.Id.Entry)).ToArray()),
            },
            ["enemies"] = new JsonArray((state?.Enemies ?? []).Select(Enemy).ToArray()),
        };
        var hidden = new JsonObject
        {
            ["rng"] = Rngs(run),
            ["enemy_moves"] = new JsonArray((state?.Enemies ?? []).Select(e => (JsonNode)new JsonObject
            {
                ["combat_id"] = e.CombatId,
                ["move"] = e.Monster?.NextMove.Id,
            }).ToArray()),
        };
        if (pcs != null)
        {
            pub["piles"] = new JsonObject
            {
                ["hand"] = Pile(pcs.Hand.Cards),
                // The draw pile's contents (kinds and counts) are visible; its order is not.
                ["draw_count"] = pcs.DrawPile.Cards.Count,
                ["draw_contents"] = new JsonArray(pcs.DrawPile.Cards
                    .Select(c => (c.Id.Entry, c.CurrentUpgradeLevel))
                    .GroupBy(k => k).OrderBy(g => g.Key.Entry, StringComparer.Ordinal).ThenBy(g => g.Key.CurrentUpgradeLevel)
                    .Select(g => (JsonNode)new JsonObject { ["card"] = g.Key.Entry, ["upgrade"] = g.Key.CurrentUpgradeLevel, ["count"] = g.Count() })
                    .ToArray()),
                ["discard"] = Pile(pcs.DiscardPile.Cards),
                ["exhaust"] = Pile(pcs.ExhaustPile.Cards),
            };
            hidden["draw_order"] = Pile(pcs.DrawPile.Cards);
        }
        return (pub, hidden);
    }

    private static JsonObject Rngs(RunState run)
    {
        var runRng = new JsonObject { ["seed"] = run.Rng.Seed.ToString() };
        if (RngCounter != null)
        {
            foreach (var type in Enum.GetValues<MegaCrit.Sts2.Core.Entities.Rngs.RunRngType>())
                runRng[type.ToString()] = (int)RngCounter.GetValue(run.Rng.GetRng(type))!;
        }
        var players = new JsonArray();
        foreach (var p in run.Players)
        {
            var pr = new JsonObject { ["seed"] = p.PlayerRng.Seed.ToString() };
            if (RngCounter != null)
            {
                foreach (var type in Enum.GetValues<MegaCrit.Sts2.Core.Entities.Rngs.PlayerRngType>())
                    pr[type.ToString()] = (int)RngCounter.GetValue(p.PlayerRng.GetRng(type))!;
            }
            players.Add(pr);
        }
        return new JsonObject { ["run"] = runRng, ["players"] = players };
    }

    private static JsonArray Pile(IReadOnlyList<CardModel> cards) =>
        new(cards.Select(c => (JsonNode)new JsonObject
        {
            ["card"] = c.Id.Entry,
            ["upgrade"] = c.CurrentUpgradeLevel,
            ["combat_card_id"] = NetCombatCardDb.Instance.TryGetCardId(c, out var id) ? id : null,
        }).ToArray());

    private static JsonArray Powers(Creature creature) =>
        new(creature.Powers.Select(p => (JsonNode)new JsonObject { ["power"] = p.Id.Entry, ["amount"] = p.Amount }).ToArray());

    private static JsonNode Enemy(Creature c) => new JsonObject
    {
        ["combat_id"] = c.CombatId,
        ["monster"] = c.Monster?.Id.Entry,
        ["hp"] = c.CurrentHp,
        ["max_hp"] = c.MaxHp,
        ["block"] = c.Block,
        ["alive"] = c.IsAlive,
        ["powers"] = Powers(c),
        ["intents"] = new JsonArray((c.Monster?.NextMove.Intents ?? []).Select(i => (JsonNode)JsonValue.Create(i.IntentType.ToString())!).ToArray()),
    };

    /// <summary>
    /// The action the way docs/native-ipc.md "行動" writes it (play_card / end_turn), read off the
    /// INetAction the game itself put in its replay - never re-derived from the live action.
    /// Other kinds come out as {"type": "&lt;net action class&gt;", ...its public fields}.
    /// </summary>
    internal static JsonObject DescribeNetAction(INetAction? net, Func<ulong?, int?> slotOf)
    {
        switch (net)
        {
            case null:
                return new JsonObject { ["type"] = null };
            case NetPlayCardAction play:
                return new JsonObject
                {
                    ["type"] = "play_card",
                    ["combat_card_id"] = play.card.CombatCardIndex,
                    ["card"] = play.modelId.Entry,
                    ["target"] = play.targetId,
                };
            case NetEndPlayerTurnAction end:
                return new JsonObject { ["type"] = "end_turn", ["turn"] = end.turnNumber };
            default:
                var obj = new JsonObject { ["type"] = net.GetType().Name };
                foreach (var (name, value) in PublicMembers(net, slotOf))
                    obj[name] = value;
                return obj;
        }
    }

    internal static JsonObject DescribeChoiceResult(NetPlayerChoiceResult? result, Func<ulong?, int?> slotOf)
    {
        if (result is not { } r)
            return new JsonObject { ["type"] = null };
        var obj = new JsonObject { ["type"] = r.type.ToString() };
        foreach (var (name, value) in PublicMembers(r, slotOf))
        {
            if (name != "type" && value != null)
                obj[name] = value;
        }
        return obj;
    }

    private static IEnumerable<(string, JsonNode?)> PublicMembers(object o, Func<ulong?, int?> slotOf)
    {
        var type = o.GetType();
        foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            yield return (JsonName(f.Name), ToJson(f.Name, f.GetValue(o), 0, slotOf));
        foreach (var p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (p.GetIndexParameters().Length != 0 || p.GetMethod == null)
                continue;
            yield return (JsonName(p.Name), ToJson(p.Name, p.GetValue(o), 0, slotOf));
        }
    }

    /// <summary>A field that holds a platform player id (NetId). Written as the player's slot.</summary>
    private static bool IsPlayerIdField(string name) =>
        name.EndsWith("playerId", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith("PlayerId", StringComparison.Ordinal)
        || name.EndsWith("Owner", StringComparison.Ordinal)
        || name.EndsWith("ownerId", StringComparison.OrdinalIgnoreCase);

    private static string JsonName(string name) => IsPlayerIdField(name) ? name + "_slot" : name;

    /// <summary>
    /// Plain values only. Player ids are platform (Steam) ids: they come out as the player's slot
    /// in the run. IdAnonymizer is not used here - it draws from Rng.Chaotic on each new id.
    /// </summary>
    private static JsonNode? ToJson(string name, object? value, int depth, Func<ulong?, int?> slotOf)
    {
        if (value == null)
            return null;
        if (depth > 4)
            return JsonValue.Create(value.ToString());
        switch (value)
        {
            case string s: return JsonValue.Create(s);
            case bool b: return JsonValue.Create(b);
            case ulong u when IsPlayerIdField(name): return JsonValue.Create(slotOf(u));
            case ulong u: return JsonValue.Create(u);
            case long l: return JsonValue.Create(l);
            case uint ui: return JsonValue.Create(ui);
            case int i: return JsonValue.Create(i);
            case ushort us: return JsonValue.Create(us);
            case short sh: return JsonValue.Create(sh);
            case byte by: return JsonValue.Create(by);
            case float fl: return JsonValue.Create(fl);
            case double d: return JsonValue.Create(d);
            case Enum e: return JsonValue.Create(e.ToString());
            case ModelId id: return JsonValue.Create(id.Entry);
            case AbstractModel m: return JsonValue.Create(m.Id.Entry);
            case NetCombatCard c: return JsonValue.Create(c.CombatCardIndex);
            case IEnumerable seq:
                var arr = new JsonArray();
                foreach (var item in seq)
                    arr.Add(ToJson(name, item, depth + 1, slotOf));
                return arr;
        }
        var t = value.GetType();
        if (t.IsValueType && !t.IsPrimitive)
        {
            var obj = new JsonObject();
            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public))
                obj[JsonName(f.Name)] = ToJson(f.Name, f.GetValue(value), depth + 1, slotOf);
            foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (p.GetIndexParameters().Length == 0 && p.GetMethod != null)
                    obj[JsonName(p.Name)] = ToJson(p.Name, p.GetValue(value), depth + 1, slotOf);
            }
            return obj;
        }
        // Class instances (SerializableCard in a MutableCard choice, ...) are not walked: their
        // members are not all public-information and their getters are not all plain reads.
        return JsonValue.Create(value.GetType().Name);
    }

    internal static JsonObject DescribeAction(GameAction action, uint? key)
    {
        var obj = new JsonObject
        {
            ["action_id"] = action.Id,
            ["action_key"] = key,
            ["class"] = action.GetType().Name,
            ["game_action_type"] = action.ActionType.ToString(),
        };
        if (action is GenericHookGameAction hook)
            obj["hook_id"] = hook.HookId;
        return obj;
    }
}
