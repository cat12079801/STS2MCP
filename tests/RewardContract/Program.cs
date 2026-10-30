using System.Text;
using System.Text.Json;
using STS2_MCP;
using STS2_MCP.Rewards;

int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    checks++;
}
var session = new RewardSession();
var screen = new object();
var run = new object();
var gold = new object();
var card = new object();
var otherCard = new object();
session.Open(screen, run);
var g = session.Register(gold, fresh: true);
var c = session.Register(card, fresh: true);
var c2 = session.Register(otherCard, fresh: true);
session.Sync(new[] { gold, card, otherCard });
var revision = session.Revision;
Check(c.SelectionState == "unopened", "fresh UI rewards are unopened");
Check(session.Resolve(c.Id, 1, revision, out var resolved) == null && ReferenceEquals(resolved, c), "ID/index agreement");
session.Begin(g);
session.Complete(g, true);
session.Sync(new[] { card, otherCard });
Check(session.Claimable[0].Id == c.Id, "gold removal preserves card identity and shifts index");
Check(session.Resolve(null, 1, revision, out _) != null, "stale revision rejected even when index still in bounds");
Check(session.Resolve(c.Id, 1, null, out _) != null, "conflicting ID/index rejected");
Check(session.Resolve("missing", 0, null, out _) != null, "unknown ID never falls back to index");
Check(session.Resolve(g.Id, null, null, out _) != null, "claimed ID rejected");
Check(session.Resolve(null, 1, null, out resolved) == null && ReferenceEquals(resolved, c2), "legacy index means CURRENT position");
Check(session.Resolve(null, null, null, out _) != null, "missing selector rejected");
Check(session.Resolve(null, -1, null, out _) != null, "negative index rejected");
Check(session.Resolve(c.Id, null, session.Revision, out _) == null, "current revision and ID accepted");
session.Begin(c);
Check(c.SelectionState == "opened" && c.Resolving, "submit is not completion");
session.AwaitChoice(c);
Check(!c.Resolving && c.SelectionState == "opened", "open choice is input boundary");
session.Choice(c, "skip", "Skip");
Check(c.Resolving && c.SelectionState == "opened", "skip submission does not complete reward");
session.Complete(c, false);
Check(c.SelectionState == "skipped" && !c.Resolving && session.Claimable.Contains(c), "skip keeps parent claimable");
Check(c2.SelectionState == "unopened", "another card reward was not skipped");
revision = session.Revision;
for (int i = 0; i < 5; i++) session.Sync(new[] { card, otherCard });
Check(session.Revision == revision && c.SelectionState == "skipped", "GET enumeration preserves skip and revision");
var item = c.Describe(true);
Check(item["reward_consumed"] is false && item["skipped"] is true, "skip serializer meaning");
var json = JsonSerializer.Serialize(item);
Check(json.Contains("reward_id") && json.Contains("skipped"), "synthetic new fields serialize");
session.Begin(c);
session.AwaitChoice(c);
session.Choice(c, "alternative", "REROLL");
session.AwaitChoice(c);
Check(c.SelectionState == "opened" && !c.Resolving && c.LastOptionId == "REROLL", "reroll remains same open parent");
session.Choice(c, "alternative", "SACRIFICE");
session.Complete(c, true);
Check(c.SelectionState == "claimed" && c.LastOptionId == "SACRIFICE" && !session.Claimable.Contains(c), "sacrifice consumes with distinct outcome");
session.Begin(c2);
session.Interrupt(c2);
Check(c2.SelectionState == "unknown" && c2.LastSelection == "interrupted", "interruption differs from skip");
var potion = session.Register(new object());
Check(potion.SelectionState == "unknown", "late registration never assumes unopened");
session.Begin(potion);
session.Complete(potion, false);
Check(potion.SelectionState == "unknown", "non-card refusal is not a skip");
var special = session.Register(new object(), fresh: true);
session.Begin(special);
session.Complete(special, true);
Check(special.SelectionState == "claimed", "direct special_card completion");
var oldId = c2.Id;
revision = session.Revision;
session.Clear();
session.Open(new object(), run);
session.Sync(new[] { otherCard });
Check(session.Resolve(oldId, 0, null, out _) != null, "old screen ID rejected, no fallback");
Check(session.Resolve(null, 0, revision, out _) != null, "old screen revision rejected");
Check(session.Claimable[0].Id != oldId, "same object on new screen gets new ID");
session.Complete(c2, true);
Check(session.Claimable[0].SelectionState == "unknown", "late callback from discarded session ignored");
session.CheckRun(new object());
Check(!session.Entries.Any(), "run change discards session");
var restart = new RewardSession();
restart.Open(screen, run);
restart.Sync(new[] { otherCard });
Check(restart.Resolve(oldId, null, null, out _) != null && restart.Claimable[0].Id != oldId, "new epoch rejects pre-restart IDs");

var legacy = new Dictionary<string, object?>
{
    ["items"] = new List<Dictionary<string, object?>> { new() { ["index"] = 0, ["type"] = "card", ["description"] = "Add card" } },
    ["can_proceed"] = true
};
Check(McpMod.RewardsMarkdown(legacy).Contains("[0] **card**: Add card"), "legacy schema3 rewards markdown");
legacy["screen_revision"] = session.Revision;
legacy["selection_history"] = new List<Dictionary<string, object?>> { item };
Check(McpMod.RewardsMarkdown(legacy).Contains(c.Id) && McpMod.RewardsMarkdown(legacy).Contains("skipped"), "new reward history markdown");
var cardState = new Dictionary<string, object?> { ["cards"] = new List<Dictionary<string, object?>>(), ["can_skip"] = true };
Check(McpMod.CardMarkdown(cardState).Contains("Can skip:** Yes"), "legacy schema3 card markdown");
cardState["parent_reward_id"] = c.Id;
cardState["selection_state"] = "opened";
Check(McpMod.CardMarkdown(cardState).Contains(c.Id), "parent ID markdown");
foreach (var path in args)
{
    using var doc = JsonDocument.Parse(File.ReadAllText(path));
    foreach (var section in new[] { "rewards", "card_reward" })
    {
        if (!doc.RootElement.TryGetProperty(section, out var data)) continue;
        var state = (Dictionary<string, object?>)ConvertJson(data)!;
        var markdown = section == "rewards" ? McpMod.RewardsMarkdown(state) : McpMod.CardMarkdown(state);
        Check(markdown.Length > 0 && !data.TryGetProperty("parent_reward_id", out _) && !data.TryGetProperty("screen_revision", out _),
            "unmodified legacy schema3 input renders: " + Path.GetFileName(path));
    }
}
object? ConvertJson(JsonElement element) => element.ValueKind switch
{
    JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => ConvertJson(p.Value)),
    JsonValueKind.Array => element.EnumerateArray().Select(e => (Dictionary<string, object?>)ConvertJson(e)!).ToList(),
    JsonValueKind.String => element.GetString(),
    JsonValueKind.Number => element.GetInt32(),
    JsonValueKind.True => true,
    JsonValueKind.False => false,
    _ => null
};
Console.WriteLine($"PASS: {checks} reward contract checks (no game dependencies)");

namespace STS2_MCP
{
    public static partial class McpMod
    {
        internal static string RewardsMarkdown(Dictionary<string, object?> state) { var sb = new StringBuilder(); FormatRewardsMarkdown(sb, state); return sb.ToString(); }
        internal static string CardMarkdown(Dictionary<string, object?> state) { var sb = new StringBuilder(); FormatCardRewardMarkdown(sb, state); return sb.ToString(); }
    }
}
