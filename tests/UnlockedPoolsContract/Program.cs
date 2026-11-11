using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

STS2_MCP.McpMod.RunContractTests();

namespace STS2_MCP
{
    public static partial class McpMod
    {
        private static readonly List<string> Warnings = new();
        private static void Warn(string context, string message) => Warnings.Add($"{context}: {message}");

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        public static void RunContractTests()
        {
            var owner = new Player();
            owner.RunState.CardMultiplayerConstraint = CardMultiplayerConstraint.MultiplayerOnly;
            var calls = new int[4];
            void CheckUnlocks(MegaCrit.Sts2.Core.Unlocks.UnlockState unlocks)
                => Check(ReferenceEquals(unlocks, owner.UnlockState), "must pass owner's unlock state");
            void ConfigureSuccess()
            {
                ModelDb.Colorless.Read = (unlocks, constraint) =>
                {
                    calls[0]++;
                    CheckUnlocks(unlocks);
                    Check(constraint == owner.RunState.CardMultiplayerConstraint, "colorless constraint");
                    return [new(new("Z_CARD")), new(new("A_CARD"))];
                };
                owner.Character.CardPool.Read = (unlocks, constraint) =>
                {
                    calls[1]++;
                    CheckUnlocks(unlocks);
                    Check(constraint == owner.RunState.CardMultiplayerConstraint, "character constraint");
                    return [new(new("OWNER_CARD"))];
                };
                owner.Character.PotionPool.Read = unlocks =>
                {
                    calls[2]++;
                    CheckUnlocks(unlocks);
                    return []; // An empty unlocked pool must stay distinct from failure.
                };
                ModelDb.Shared.Read = unlocks =>
                {
                    calls[3]++;
                    CheckUnlocks(unlocks);
                    return [new(new("SHARED_POTION"))];
                };
            }

            ConfigureSuccess();
            ICombatState combat = new CombatState();
            var pools = BuildUnlockedPools(owner, combat);
            Check(((List<string>)pools["colorless_cards"]!).SequenceEqual(["A_CARD", "Z_CARD"]), "sorted ID.Entry");
            Check(((List<string>)pools["character_cards"]!).Single() == "OWNER_CARD", "owner's cards");
            var potions = (Dictionary<string, object?>)pools["potions"]!;
            Check(((List<string>)potions["character"]!).Count == 0, "successful empty pool");
            Check(((List<string>)potions["shared"]!).Single() == "SHARED_POTION", "shared provenance");
            Check(Warnings.Count == 0, "normal reads must not warn");
            Check(ReferenceEquals(pools, BuildUnlockedPools(owner, combat)), "same combat must reuse cache");
            Check(calls.All(count => count == 1), "must enumerate each source only once");

            owner.RunState.CardMultiplayerConstraint = CardMultiplayerConstraint.SingleplayerOnly;
            combat = new CombatState();
            BuildUnlockedPools(owner, combat);
            Check(calls.All(count => count == 2), "new combat must refresh all pools with solo constraint");

            owner = new Player();
            ConfigureSuccess();
            BuildUnlockedPools(owner, combat);
            Check(calls.All(count => count == 3), "different owner in same combat must refresh");

            static IEnumerable<CardModel> FailDuringEnumeration()
            {
                yield return new(new("PARTIAL_MUST_NOT_ESCAPE"));
                throw new InvalidOperationException("lazy pool failed");
            }
            int failedCalls = 0;
            ModelDb.Colorless.Read = (_, _) => { failedCalls++; return FailDuringEnumeration(); };
            ModelDb.Shared.Read = _ => throw new InvalidOperationException("shared pool failed");
            combat = new CombatState();
            pools = BuildUnlockedPools(owner, combat);
            potions = (Dictionary<string, object?>)pools["potions"]!;
            Check(pools["colorless_cards"] == null && potions["shared"] == null, "failed pools must be null");
            Check(pools["character_cards"] is List<string> && potions["character"] is List<string>, "other pools survive");
            Check(Warnings.Count == 2 && Warnings.Any(w => w.StartsWith("player.unlocked_pools.colorless_cards:"))
                && Warnings.Any(w => w.StartsWith("player.unlocked_pools.potions.shared:")), "precise failure paths");
            Warnings.Clear(); // Equivalent to the next state's BeginStateWarnings.
            BuildUnlockedPools(owner, combat);
            Check(failedCalls == 1 && Warnings.Count == 2, "cached failures must warn again without rereading");

            ConfigureSuccess();
            owner.Character.CardPool.Read = (_, _) => throw new InvalidOperationException("owner cards failed");
            owner.Character.PotionPool.Read = _ => throw new InvalidOperationException("owner potions failed");
            Warnings.Clear();
            pools = BuildUnlockedPools(owner, new CombatState());
            potions = (Dictionary<string, object?>)pools["potions"]!;
            Check(pools["character_cards"] == null && potions["character"] == null, "owner failures must be isolated");
            Check(pools["colorless_cards"] is List<string> && potions["shared"] is List<string>, "other sources recover next combat");
            Check(Warnings.Count == 2 && Warnings.Any(w => w.StartsWith("player.unlocked_pools.character_cards:"))
                && Warnings.Any(w => w.StartsWith("player.unlocked_pools.potions.character:")), "owner failure paths");

            ConfigureSuccess();
            Warnings.Clear();
            BuildUnlockedPools(owner, new CombatState());
            Check(Warnings.Count == 0, "recovered combat must drop stale warnings");
            Console.WriteLine("Unlocked pools contract passed (arguments, IDs, cache lifecycle, isolated failures, warning replay).");
        }
    }
}
