using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Entities.Creatures;

await STS2_MCP.McpMod.RunCompatContract();

namespace STS2_MCP
{
    public static partial class McpMod
    {
        private const string Version = "test-mod";
        private const string BuildCommit = "test-mod-commit";
        private const int StateSchemaVersion = 4;
        private static int Checks;
        private static void Check(bool condition, string message)
        {
            Checks++;
            if (!condition) throw new Exception(message);
        }
        private static string? SafeGetText(Func<string?> read) => read();
        private static void Warn(string context, Exception ex) => throw new Exception(context, ex);
        private static Dictionary<string, object?> Error(string message)
            => new() { ["status"] = "error", ["message"] = message };

        public static async Task RunCompatContract()
        {
            static Dictionary<string, object?> Preview(int damage, int? hits, string key = "HitCount", Power? power = null,
                bool repeat = false, string? cardId = null, bool vulnerable = false, bool lostHp = false)
            {
                var values = new Dictionary<string, object?> { ["Damage"] = damage };
                if (hits != null) values[key] = hits.Value;
                if (repeat) values["Repeat"] = 1;
                var target = new Creature();
                if (power != null) target.Powers.Add(power);
                var preview = new Dictionary<string, object?>();
                AddCardPreviewValues(preview, values, target, cardId, vulnerable, lostHp);
                return preview;
            }
            static int Effective(Dictionary<string, object?> preview)
                => (int)((Dictionary<string, object?>)preview["damage_cap"]!)["effective_damage"]!;

            // Values from v0.113 CanonicalVars and targeted previews, not a running game.
            foreach (var (damage, hits, key, total, label) in new[]
            {
                (5, 2, "HitCount", 10, "TwinStrike"),
                (3, 3, "HitCount", 9, "SwordBoomerang"),
                (3, 4, "HitCount", 12, "SwordBoomerang+"),
                (5, 4, "CalculatedHits", 20, "TearAsunder with Repeat=1"),
                (5, 3, "CalculatedHits", 15, "Flechettes"),
                (5, 0, "CalculatedHits", 0, "Flechettes with no skills"),
                (5, 1, "CalculatedHits", 5, "one computed hit")
            })
            {
                var preview = Preview(damage, hits, key, repeat: key == "CalculatedHits");
                Check(Equals(preview["hits"], hits) && Equals(preview["total_damage"], total), label);
                Check(!preview.ContainsKey("damage_cap"), "uncapped target");
                Check(Effective(Preview(damage, hits, key, new IntangiblePower(), key == "CalculatedHits")) == hits,
                    label + " intangible");
            }
            foreach (int repeat in new[] { 1, 3 })
            {
                var preview = Preview(19, repeat, "Repeat", new SlipperyPower(1));
                Check(!preview.ContainsKey("hits") && !preview.ContainsKey("total_damage"), "Repeat alone is not a hit count");
                Check(Effective(preview) == 1, "Repeat alone does not inflate capped damage");
            }
            foreach (string cardId in new[] { "DISMANTLE", "SPITE" })
            foreach (bool condition in new[] { false, true })
            foreach (int previewHits in new[] { 2, 3, 4 }) // base, Thumbscrew / Spite+, Spite+ with Thumbscrew
            {
                bool vulnerable = cardId == "DISMANTLE" && condition;
                bool lostHp = cardId == "SPITE" && condition;
                int expectedHits = condition ? previewHits : 1;
                var values = new Dictionary<string, object?> { ["HitCount"] = previewHits };
                Check(ResolveCardPreviewHitCount(values, cardId, vulnerable, lostHp) == expectedHits,
                    $"{cardId}: pure conditional hit count, preview={previewHits}, condition={condition}");
                int damage = cardId == "DISMANTLE" ? 8 : 5;
                var preview = Preview(damage, previewHits, cardId: cardId, vulnerable: vulnerable, lostHp: lostHp);
                Check(Equals(preview["hits"], expectedHits) && Equals(preview["total_damage"], damage * expectedHits),
                    $"{cardId}: corrected count drives total damage");
                Check(Effective(Preview(damage, previewHits, power: new SlipperyPower(1), cardId: cardId,
                    vulnerable: vulnerable, lostHp: lostHp)) == 1 + damage * (expectedHits - 1),
                    $"{cardId}: corrected count drives Slippery cap");
                Check(Effective(Preview(damage, previewHits, power: new IntangiblePower(), cardId: cardId,
                    vulnerable: vulnerable, lostHp: lostHp)) == expectedHits,
                    $"{cardId}: corrected count drives Intangible cap");
            }
            Check(Equals(Preview(8, 2, cardId: "DISMANTLE", lostHp: true)["hits"], 1), "Dismantle ignores owner HP loss");
            Check(Equals(Preview(5, 2, cardId: "SPITE", vulnerable: true)["hits"], 1), "Spite ignores target Vulnerable");
            Check(ResolveCardPreviewHitCount(new() { ["Repeat"] = 3 }, "DISMANTLE", false, false) == null,
                "conditional correction does not invent a missing attack count");
            Check(Effective(Preview(5, 4, "CalculatedHits", new SlipperyPower(2), true)) == 12, "first two hits capped");
            Check(Effective(Preview(5, 4, "CalculatedHits", new HardToKillPower(3), true)) == 12, "all four hits capped");
            Check(Effective(Preview(5, null, power: new IntangiblePower())) == 1, "no hit var defaults to one for caps");
            Check(!Preview(5, null).ContainsKey("hits"), "single hit without count keeps old shape");
            var both = new Dictionary<string, object?> { ["Damage"] = 5, ["HitCount"] = 2, ["CalculatedHits"] = 4, ["Repeat"] = 1 };
            var result = new Dictionary<string, object?>();
            AddCardPreviewValues(result, both, new Creature());
            Check(Equals(result["hits"], 2) && Equals(result["total_damage"], 10), "HitCount wins all three");
            var shell = (Dictionary<string, object?>)Preview(5, 4, power: new HardenedShellPower(7))["damage_cap"]!;
            Check(Equals(shell["per_turn_max"], 7) && !shell.ContainsKey("effective_damage"), "per-turn cap unchanged");

            ReleaseInfoManager.Instance.ReleaseInfo = new("v0.113.0", "16ac0fe3");
            foreach (var type in new[] { "unknown", "menu", "shop", "monster" })
            {
                var state = NewStateResult(type);
                Check(Equals(state["game_version"], "v0.113.0") && Equals(state["game_commit"], "16ac0fe3"), "release stamp " + type);
                Check(Equals(state["mod_version"], Version) && Equals(state["mod_commit"], BuildCommit)
                    && Equals(state["schema_version"], 4), "independent mod/schema stamp");
            }
            ReleaseInfoManager.Instance.ReleaseInfo = null;
            Check(NewStateResult()["game_version"] == null && NewStateResult()["game_commit"] == null, "missing release info");

            foreach (bool failInstance in new[] { true, false })
            {
                ReleaseInfoManager.FailInstance = failInstance;
                ReleaseInfoManager.FailReleaseInfo = !failInstance;
                var state = NewStateResult("menu");
                Check(state["game_version"] == null && state["game_commit"] == null,
                    "release exception leaves nullable metadata");
                Check(Equals(state["state_type"], "menu") && Equals(state["mod_version"], Version)
                    && Equals(state["schema_version"], 4), "release exception preserves state response");
                ReleaseInfoManager.FailInstance = ReleaseInfoManager.FailReleaseInfo = false;
            }
            ReleaseInfoManager.Instance.ReleaseInfo = new("v0.113.0", "16ac0fe3");
            Check(Equals(NewStateResult()["game_version"], "v0.113.0"), "release metadata recovers after failure");

            var success = BuildShopPurchaseResult(Task.FromResult(true), 75);
            Check(Equals(success["purchase_success"], true) && Equals(success["purchase_completed"], true), "purchase completed");
            Check(Equals(BuildShopPurchaseResult(Task.FromResult(false), 75)["status"], "error"), "purchase refused");
            Check(Equals(BuildShopPurchaseResult(Task.FromException<bool>(new InvalidOperationException("bad id")), 75)["status"], "error"), "purchase fault");
            Check(Equals(BuildShopPurchaseResult(Task.FromCanceled<bool>(new CancellationToken(true)), 75)["status"], "error"), "purchase cancelled");
            var selection = new TaskCompletionSource<bool>();
            var pending = BuildShopPurchaseResult(selection.Task, 75);
            Check(Equals(pending["status"], "ok") && Equals(pending["purchase_completed"], false)
                && !pending.ContainsKey("purchase_success") && !selection.Task.IsCompleted, "selection remains interactive");
            selection.SetResult(true);
            await ObserveShopPurchase(Task.FromResult(false));
            Check(Godot.GD.Warnings.Count == 1, "eventual failure observed");
            await ObserveShopPurchase(Task.FromException<bool>(new InvalidOperationException("async failure")));
            Check(Godot.GD.Errors.Count == 1, "eventual exception observed");

            var sources = Path.Combine(AppContext.BaseDirectory, "Sources");
            var stateSource = File.ReadAllText(Path.Combine(sources, "McpMod.StateBuilder.cs"));
            var actionSource = File.ReadAllText(Path.Combine(sources, "McpMod.Actions.cs"));
            Check(stateSource.Contains("AddCardPreviewValues(preview, values, enemy, card.Id.Entry,"), "production previews use linked calculation");
            Check(stateSource.Contains("RunManager.Instance.MerchantSynchronizer.GetLocalInventory()")
                && actionSource.Contains("RunManager.Instance.MerchantSynchronizer.PurchaseLocalEntry(entry)"), "new merchant API wired");
            Check(stateSource.Contains("var result = NewStateResult();") &&
                File.ReadAllText(Path.Combine(sources, "McpMod.MultiplayerState.cs")).Contains("var result = NewStateResult();"), "both state builders stamped");
            Check(File.ReadAllText(Path.Combine(sources, "McpMod.cs")).Contains("public const int StateSchemaVersion = 4;"), "published schema remains 4");
            Console.WriteLine($"v0.113 compatibility contract: {Checks} checks passed");
        }
    }
}
