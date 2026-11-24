using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Relics;

namespace STS2_MCP;

public static partial class McpMod
{
    private static readonly JsonSerializerOptions WireOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static void Main()
    {
        CardModel[] cards =
        [
            new("STRIKE_IRONCLAD", CardType.Attack) { IsBasic = true, CanonicalTags = [CardTag.Strike], UpgradeLevel = 1 },
            new("STRIKE_IRONCLAD", CardType.Attack) { IsBasic = true, CanonicalTags = [CardTag.Strike], Eternal = true,
                Attached = new EnchantmentModel("MOMENTUM") },
            new("DEFEND_IRONCLAD", CardType.Skill) { IsBasic = true, CanonicalTags = [CardTag.Defend], Eternal = true },
            new("BASH", CardType.Attack) { IsBasic = true },
            new("QUEST", CardType.Quest),
            new("UNPLAYABLE_SKILL", CardType.Skill) { Unplayable = true }
        ];
        var player = new Player(new CardPile(cards));
        var playerState = PublishDeck(cards);
        var relics = new (RelicModel Relic, string[] ExtraKeys)[]
        {
            (new BeautifulBracelet(), ["can_enchant"]),
            (new ElectricShrymp(), ["can_enchant"]),
            (new NeowsTalisman(), ["tags"]),
            (new LeafyPoultice(), ["tags", "is_transformable"]),
            (new Astrolabe(), ["is_transformable"]),
            (new PandorasBox(), ["is_basic_strike_or_defend", "is_removable"])
        };

        foreach (var (relic, extraKeys) in relics)
        {
            var (option, snapshot, markdown) = Snapshot(relic, player, playerState);
            string name = relic.GetType().Name;
            var targets = option.GetProperty("deck_targets");
            var rows = targets.GetProperty("rows");
            var deck = snapshot.GetProperty("player").GetProperty("deck");
            Check(targets.GetProperty("known").GetBoolean() && !targets.GetProperty("unavailable_reasons").EnumerateObject().Any(),
                $"{name}: successful observation");
            Check(rows.GetArrayLength() == deck.GetArrayLength(), $"{name}: full deck");
            var keys = new[] { "deck_index", "card_id", "enchantment_id" }.Concat(extraKeys).ToHashSet();
            for (int i = 0; i < cards.Length; i++)
            {
                Check(keys.SetEquals(rows[i].EnumerateObject().Select(property => property.Name)), $"{name}: row keys at {i}");
                Check(rows[i].GetProperty("deck_index").GetInt32() == deck[i].GetProperty("index").GetInt32()
                    && rows[i].GetProperty("card_id").GetString() == deck[i].GetProperty("id").GetString(), $"{name}: deck join at {i}");
            }
            Check(rows[0].GetProperty("enchantment_id").ValueKind == JsonValueKind.Null
                && rows[1].GetProperty("enchantment_id").GetString() == "MOMENTUM", $"{name}: null and attached IDs survive JSON");
            Check(deck[0].GetProperty("upgrade_level").GetInt32() == 1
                && deck[1].GetProperty("upgrade_level").GetInt32() == 0, $"{name}: duplicate IDs have distinct positions and upgrades");
            Check(markdown.Contains($"Deck targets: {cards.Length} deck cards read"), $"{name}: Markdown shows observed count");
            Check(snapshot.GetRawText() == Snapshot(relic, player, playerState).Snapshot.GetRawText(), $"{name}: repeated read is stable");

            var emptyPlayer = new Player(new CardPile(Array.Empty<CardModel>()));
            ModelDb.FailLookup = true; // An empty deck needs no enchantment model lookup.
            var (empty, _, emptyMarkdown) = Snapshot(relic, emptyPlayer, PublishDeck([]));
            ModelDb.FailLookup = false;
            var emptyTargets = empty.GetProperty("deck_targets");
            Check(emptyTargets.GetProperty("known").GetBoolean() && emptyTargets.GetProperty("rows").GetArrayLength() == 0
                && !emptyTargets.GetProperty("unavailable_reasons").EnumerateObject().Any(), $"{name}: empty deck is known");
            Check(emptyMarkdown.Contains("Deck targets: 0 deck cards read"), $"{name}: Markdown distinguishes empty from failure");

            var (hidden, _, hiddenMarkdown) = Snapshot(relic, null, null, isPresentedAncient: false);
            Check(!hidden.TryGetProperty("deck_targets", out _) && hiddenMarkdown.Length == 0, $"{name}: not presented as an Ancient option");
        }

        JsonElement RowsFor(RelicModel relic)
            => Snapshot(relic, player, playerState).Option.GetProperty("deck_targets").GetProperty("rows");
        CheckBools(RowsFor(new BeautifulBracelet()), "can_enchant", [true, false, true, true, false, false]);
        CheckBools(RowsFor(new ElectricShrymp()), "can_enchant", [false, false, true, false, false, false]);
        Check(ModelDb.Enchantment<Swift>().Calls == cards.Length * 3
            && ModelDb.Enchantment<Imbued>().Calls == cards.Length * 3, "uses the respective canonical predicate once per row");
        foreach (var relic in new RelicModel[] { new NeowsTalisman(), new LeafyPoultice() })
        {
            var rows = RowsFor(relic);
            Check(rows[0].GetProperty("tags")[0].GetString() == "Strike" && rows[1].GetProperty("tags")[0].GetString() == "Strike"
                && rows[2].GetProperty("tags")[0].GetString() == "Defend" && rows[3].GetProperty("tags").GetArrayLength() == 0,
                "uses enum names, including duplicate strikes and a Basic non-strike");
        }
        CheckBools(RowsFor(new LeafyPoultice()), "is_transformable", [true, false, false, true, true, true]);
        CheckBools(RowsFor(new Astrolabe()), "is_transformable", [true, false, false, true, true, true]);
        CheckBools(RowsFor(new PandorasBox()), "is_basic_strike_or_defend", [true, true, true, false, false, false]);
        CheckBools(RowsFor(new PandorasBox()), "is_removable", [true, false, false, true, true, true]);

        var (other, _, otherMarkdown) = Snapshot(new OtherRelic(), null, null);
        Check(!other.TryGetProperty("deck_targets", out _) && otherMarkdown.Length == 0, "other Ancient relic is outside the contract");
        CheckFailure(Snapshot(new PandorasBox(), player, null), "player.deck");
        CheckFailure(Snapshot(new PandorasBox(), new Player(null), playerState), "player.deck");
        CheckFailure(Snapshot(new PandorasBox(), player, PublishDeck(cards[..^1])), "player.deck");
        var shuffled = cards.ToArray();
        (shuffled[2], shuffled[3]) = (shuffled[3], shuffled[2]);
        CheckFailure(Snapshot(new PandorasBox(), player, PublishDeck(shuffled)), "rows[2].card_id");
        cards[1].FailEnchantmentRead = true;
        CheckFailure(Snapshot(new BeautifulBracelet(), player, playerState), "rows[1].enchantment_id");
        cards[1].FailEnchantmentRead = false;
        cards[1].FailTagsRead = true;
        CheckFailure(Snapshot(new LeafyPoultice(), player, playerState), "rows[1].tags");
        cards[1].FailTagsRead = false;
        ModelDb.FailLookup = true;
        CheckFailure(Snapshot(new ElectricShrymp(), player, playerState), "rows[0].can_enchant");
        ModelDb.FailLookup = false;
        Check(RowsFor(new BeautifulBracelet()).GetArrayLength() == cards.Length, "next read recovers independently");

        CheckWiring();
        Console.WriteLine("Ancient deck targets contract checks passed.");
    }

    private static Dictionary<string, object?> PublishDeck(IReadOnlyList<CardModel> cards) => new()
    {
        ["deck"] = cards.Select((card, index) => new Dictionary<string, object?>
        {
            ["index"] = index, ["id"] = card.Id.Entry, ["upgrade_level"] = card.UpgradeLevel
        }).ToList()
    };

    private static (JsonElement Option, JsonElement Snapshot, string Markdown) Snapshot(RelicModel relic,
        Player? player, Dictionary<string, object?>? playerState, bool isPresentedAncient = true)
    {
        BeginStateWarnings();
        var option = new Dictionary<string, object?> { ["index"] = 2 };
        AttachAncientDeckTargets(option, isPresentedAncient, relic, player, playerState);
        var state = new Dictionary<string, object?> { ["option"] = option, ["player"] = playerState };
        EndStateWarnings(state);
        var markdown = new StringBuilder();
        FormatAncientDeckTargetsMarkdown(markdown, option);
        var snapshot = JsonSerializer.SerializeToElement(state, WireOptions);
        return (snapshot.GetProperty("option"), snapshot, markdown.ToString());
    }

    private static void CheckFailure((JsonElement Option, JsonElement Snapshot, string Markdown) result, string path)
    {
        var targets = result.Option.GetProperty("deck_targets");
        Check(!targets.GetProperty("known").GetBoolean() && targets.GetProperty("rows").GetArrayLength() == 0,
            $"{path}: a failure never publishes partial rows");
        Check(targets.GetProperty("unavailable_reasons").TryGetProperty(path, out var reason)
            && reason.GetString()!.StartsWith("InvalidOperationException"), $"{path}: reports the failing field");
        Check(result.Snapshot.GetProperty("warnings")[0].GetString()!.StartsWith($"event.options[2].deck_targets.{path}:"),
            $"{path}: warning identifies option and field");
        Check(result.Markdown.Contains($"Deck targets: unavailable ({path})"), $"{path}: Markdown reports the failure");
    }

    private static void CheckBools(JsonElement rows, string key, bool[] expected)
        => Check(rows.EnumerateArray().Select(row => row.GetProperty(key).GetBoolean()).SequenceEqual(expected), key);

    private static void CheckWiring()
    {
        string Source(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Sources", $"McpMod.{name}.cs"));
        string state = Source("StateBuilder");
        string multiplayer = Source("MultiplayerState");
        string markdown = Source("Formatting");
        Check(state.Contains("FindAll<NEventOptionButton>(uiRoom)") && state.Contains("var opt = button.Option;")
            && state.Contains("if (opt.Relic is { } relic)")
            && state.Contains("AttachAncientDeckTargets(optData, isAncient && !inDialogue && !opt.WasChosen &&")
            && state.Contains("IsNodeVisible(button), relic, localPlayer, playerState);"), "event reader follows only presented UI relics");
        const string publishedPlayer = "result.GetValueOrDefault(\"player\") as Dictionary<string, object?>);";
        Check(state.Contains("result[\"event\"] = BuildEventState(visibleEventRoom, _player,") && state.Contains(publishedPlayer)
            && multiplayer.Contains("result[\"event\"] = BuildMultiplayerEventState(visibleEventRoom, runState, localPlayer,")
            && multiplayer.Contains(publishedPlayer) && multiplayer.Contains("var state = BuildEventState(eventRoom, localPlayer, playerState);"),
            "both event builders pass the published local player with the event");
        Check(markdown.Contains("FormatAncientDeckTargetsMarkdown(sb, opt);"), "event Markdown formats the option's observation");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
