using System.Reflection;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;

// Boundary doubles only: no game DLL, Godot runtime, signals or UI rendering.
namespace MegaCrit.Sts2.Core.Models
{
    public class CardModel(string id)
    {
        public string Id = id;
        public string Title => Id;
        public bool ExhaustOnNextPlay;
        public bool IsUpgradable = true;
        public int CurrentUpgradeLevel;
        public int MaxUpgradeLevel = 1;
    }
}

namespace MegaCrit.Sts2.Core.CardSelection
{
    public struct CardSelectorPrefs
    {
        public int MinSelect;
        public int MaxSelect;
    }
}

namespace MegaCrit.Sts2.Core.Combat
{
    public class CombatManager
    {
        public static CombatManager? Instance;
        public bool IsInProgress;
        public object? State;
        public object? DebugOnlyGetState() => State;
    }
}

namespace MegaCrit.Sts2.Core.Nodes.Cards
{
    public class TestNode
    {
        public bool Live = true;
        public bool Visible = true;
        public bool InTree = true;
        public bool IsInsideTree() => InTree;
    }

    public class NCardGrid(object? cards) : TestNode
    {
        // object allows future-version type mismatch to be tested too.
        private object? _cards = cards;
        public void SetCards(object? cards) => _cards = cards;
        public List<(CardModel Card, bool Visible)> Holders = [];
    }
}

namespace MegaCrit.Sts2.Core.Nodes.Screens.Overlays
{
    public class NOverlayStack
    {
        public static NOverlayStack? Instance = new();
        public object? Top;
        public object? Peek() => Top;
    }
}

namespace MegaCrit.Sts2.Core.Nodes.Screens.CardSelection
{
    public abstract class NCardGridSelectionScreen(NCardGrid? grid) : TestNode
    {
        protected NCardGrid? _grid = grid;
        // Combat pile: this remains empty while the grid has all candidates.
        protected IReadOnlyList<CardModel> _cards = Array.Empty<CardModel>();
        protected abstract void OnCardClicked(CardModel card);
    }

    public class TestSelectScreen(NCardGrid? grid) : NCardGridSelectionScreen(grid)
    {
        private HashSet<CardModel>? _selectedCards = [];
        private MegaCrit.Sts2.Core.CardSelection.CardSelectorPrefs _prefs = new() { MinSelect = 1, MaxSelect = 2 };
        public bool Preview;
        public bool PreviewAtLimit;
        public bool ReplaceSelection;
        public bool CloseImmediately;
        public bool ThrowOnClick;
        public bool ThrowOnReadAfterClose;
        public int Calls;
        public CardModel? LastCard;
        public void SetSelection(HashSet<CardModel>? selection) => _selectedCards = selection;
        public void SetPrefs(int min, int max) => _prefs = new() { MinSelect = min, MaxSelect = max };
        protected override void OnCardClicked(CardModel card)
        {
            Calls++;
            LastCard = card;
            if (ThrowOnClick) throw new InvalidOperationException("click failure");
            if (_selectedCards != null)
            {
                if (ReplaceSelection) _selectedCards = new(_selectedCards);
                if (!_selectedCards.Remove(card) && _selectedCards.Count < _prefs.MaxSelect) _selectedCards.Add(card);
                if (PreviewAtLimit && _selectedCards.Count == _prefs.MaxSelect) Preview = true;
            }
            if (CloseImmediately)
            {
                Live = false;
                NOverlayStack.Instance!.Top = null;
            }
        }
    }

    // No declared override here: resolution must walk to the concrete parent.
    public class NDeckUpgradeSelectScreen(NCardGrid? grid) : TestSelectScreen(grid);

    public abstract class WrongSignatureScreen(NCardGrid? grid) : NCardGridSelectionScreen(grid)
    {
        protected void OnCardClicked(object card) => throw new Exception("wrong overload");
    }
}

namespace STS2_MCP
{
    public static partial class McpMod
    {
        private static readonly List<string> Warnings = [];
        private static readonly Dictionary<CardModel, string> Uids = [];
        private static int RefreshCalls;
        private static int ClosedReads;
        private static void Warn(string field, string message) => Warnings.Add($"{field}: {message}");
        private static bool IsLiveNode(TestNode? node) => node?.Live == true;
        private static bool IsNodeVisible(TestNode? node) => node is { Live: true, Visible: true, InTree: true };
        private static object? FindVisibleCardSelectPreview(NCardGridSelectionScreen screen)
        {
            AssertReadable(screen);
            return screen is TestSelectScreen { Preview: true } ? screen : null;
        }
        private static object? GetInstanceFieldValue(object instance, string name)
        {
            if (instance is NCardGridSelectionScreen screen) AssertReadable(screen);
            for (Type? type = instance.GetType(); type != null; type = type.BaseType)
                if (type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) is { } field)
                    return field.GetValue(instance);
            return null;
        }
        private static void AssertReadable(NCardGridSelectionScreen screen)
        {
            if (screen is TestSelectScreen { Live: false, ThrowOnReadAfterClose: true })
            {
                ClosedReads++;
                throw new Exception("read closed screen");
            }
        }
        private static Dictionary<string, object?> Error(string message) => new() { ["status"] = "error", ["message"] = message };
        private static string? SafeGetText(Func<string?> read) => read();
        private static bool? SafeGetBool(Func<bool> read) => read();
        private static void RefreshCardUidRegistry(object state) => RefreshCalls++;
        private static string GetStableCardUid(CardModel card)
        {
            if (!Uids.TryGetValue(card, out var uid)) Uids[card] = uid = $"c{Uids.Count}";
            return uid;
        }
        private static Dictionary<string, object?> BuildCardInfo(CardModel card) => new()
        {
            ["id"] = card.Id, ["name"] = card.Title, ["cost"] = "1", ["type"] = "Attack",
            ["rarity"] = "Common", ["description"] = "Test card"
        };
        private static Dictionary<string, object?> BuildCardEnergyCostState(CardModel card) => new()
        {
            ["base"] = 1, ["with_local_modifiers"] = 1, ["with_all_modifiers"] = 1,
            ["local_modifiers"] = new List<Dictionary<string, object?>>()
        };
        // Existing card serialization / upgrade preview construction is a boundary;
        // these tests exercise when the linked row builder attaches it, not game upgrades.
        private static void AttachUpgradePreview(Dictionary<string, object?> row, CardModel card)
        {
            if (card.IsUpgradable && card.CurrentUpgradeLevel < card.MaxUpgradeLevel)
                row["upgrade_preview"] = new Dictionary<string, object?> { ["cost"] = "1", ["description"] = "Upgraded" };
        }
    }
}
