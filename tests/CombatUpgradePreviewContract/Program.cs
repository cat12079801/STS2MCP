using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Enchantments;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Events;

STS2_MCP.McpMod.RunCombatUpgradePreviewContract();

namespace STS2_MCP
{
    public static partial class McpMod
    {
        private static int Checks;
        private static void Check(bool condition, string label)
        {
            Checks++;
            if (!condition) throw new Exception(label);
        }

        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private static string Json(object? value) => JsonSerializer.Serialize(value, Options);

        private static Player NewPlayer() => new();

        private static T Place<T>(Player player, PileType pile, T card) where T : CardModel
        {
            card.OwnerPlayer = player;
            player.Pile(pile).Cards.Add(card);
            return card;
        }

        /// <summary>The production row for a combat card, as BuildPileCardList / BuildCardState build it
        /// (minus the StateBuilder-only display fields).</summary>
        private static Dictionary<string, object?> Row(CardModel card, PileType pile)
        {
            var row = new Dictionary<string, object?>
            {
                ["energy_cost"] = BuildCardEnergyCostState(card),
                ["star_cost_state"] = BuildCardStarCostState(card),
                // StateBuilder reads these through SafeGetText / SafeGetCardDescription.
                ["name"] = Safe(() => card.Title),
                ["cost"] = Safe(() => GetCostDisplay(card)),
                ["description"] = Safe(() => card.GetDescriptionForPile(pile))
            };
            AttachCardDetails(row, card);
            AttachCardPlayability(row, card);
            AttachCombatUpgradeState(row, card, pile);
            return row;
        }

        private static string? Safe(Func<string?> read)
        {
            try { return read(); } catch { return null; }
        }

        private static Dictionary<string, object?> Preview(Dictionary<string, object?> row)
            => (Dictionary<string, object?>)row["upgrade_preview"]!;
        private static Dictionary<string, string> Reasons(Dictionary<string, object?> obj)
            => (Dictionary<string, string>)obj["unavailable_reasons"]!;
        private static Dictionary<string, object?> Obj(Dictionary<string, object?> obj, string key)
            => (Dictionary<string, object?>)obj[key]!;
        private static List<string> Strings(Dictionary<string, object?> obj, string key) => (List<string>)obj[key]!;

        /// <summary>Everything the live card exposes, serialized: the "nothing changed" oracle.</summary>
        private static string LiveSnapshot(CardModel card)
        {
            var row = new Dictionary<string, object?>
            {
                ["energy_cost"] = BuildCardEnergyCostState(card),
                ["star_cost_state"] = BuildCardStarCostState(card),
                ["level"] = card.UpgradeLevelField,
                ["local"] = KeywordIds(card.GetKeywordsWithSources(KeywordSources.Local)),
                ["preview_type"] = card.UpgradePreviewType.ToString(),
                ["enchantment_card_is_live"] = card.Enchantment == null || ReferenceEquals(card.Enchantment.AttachedCard, card),
                ["affliction_card_is_live"] = card.Affliction == null || ReferenceEquals(card.Affliction.AttachedCard, card)
            };
            AttachCardDetails(row, card);
            return Json(row);
        }

        private static string Markdown(Action<StringBuilder> write)
        {
            var sb = new StringBuilder();
            write(sb);
            return sb.ToString();
        }

        // sts2#1019: the ready clone carries the live card's turn state; selection candidates map to a pile uid or carry their own values.
        private static void RunCardEffectInputsContract()
        {
            string[] turnKeys = { "retain_this_turn", "single_turn_retain", "sly_this_turn", "single_turn_sly",
                "base_replay_count", "extra_replay_this_turn", "enchanted_replay_count" };
            BeginStateWarnings();
            var player = NewPlayer();

            // --- ready: the clone keeps the single-turn flags, replay counts and enchantment vars ---
            var glam = new Glam();
            var replayed = Place(player, PileType.Hand, new CardModel { Name = "Strike", TagSet = { CardTag.Strike } });
            replayed.Enchant(glam, 1);
            replayed.GiveSingleTurnSly();
            replayed.GiveSingleTurnRetain();
            replayed.BaseReplayCount = 1;
            replayed.ExtraReplayThisTurn = 2;
            int notifications = 0;
            replayed.SubscribeAll(() => notifications++);
            string before = LiveSnapshot(replayed);
            var row = Row(replayed, PileType.Hand);
            var preview = Preview(row);
            Check(Equals(preview["status"], "ready") && turnKeys.All(preview.ContainsKey) && Reasons(preview).Count == 0, "ready carries every turn key");
            Check(preview["sly_this_turn"] is true && preview["single_turn_sly"] is true && preview["retain_this_turn"] is true && preview["single_turn_retain"] is true,
                "single-turn Sly / Retain survive the clone (MemberwiseClone copies the private flags)");
            Check(Equals(preview["base_replay_count"], 1) && Equals(preview["extra_replay_this_turn"], 2) && Equals(preview["enchanted_replay_count"], 4),
                "replay counts and Glam's Times on the clone");
            Check(Equals(row["enchanted_replay_count"], 4) && row["single_turn_sly"] is true, "live row has the same values");
            Check(Equals(((Dictionary<string, object?>)Obj(preview, "enchantment")["dynamic_vars"]!)["Times"], 1m)
                && Equals(((Dictionary<string, object?>)Obj(row, "enchantment")["dynamic_vars"]!)["Times"], 1m), "enchantment vars on the live row and the clone");
            Check(notifications == 0 && LiveSnapshot(replayed) == before && replayed.PeekTurnState() == (true, true, 1, 2), "reading notifies nobody and changes nothing");
            glam.StatusValue = EnchantmentStatus.Disabled;
            Check(Equals(Preview(Row(replayed, PileType.Hand))["enchanted_replay_count"], 3), "a Disabled Glam (after its first play) no longer adds Times on the clone either");
            var plainRow = Row(Place(player, PileType.Draw, new CardModel { Name = "Plain" }), PileType.Draw);
            Check(turnKeys.All(Preview(plainRow).ContainsKey) && Preview(plainRow)["sly_this_turn"] is false && Equals(Preview(plainRow)["base_replay_count"], 0),
                "zeros and false are read values in ready too");

            // --- Mad Science: the rider on the row and the clone ---
            var mad = Place(player, PileType.Discard, new MadScience());
            mad.TinkerTimeRider = TinkerTime.RiderEffect.Violence;
            var madRow = Row(mad, PileType.Discard);
            Check(Equals(madRow["tinker_time_rider"], "Violence") && Equals(Preview(madRow)["tinker_time_rider"], "Violence"), "rider on the live row and the ready clone");
            Check(Strings(Preview(madRow), "local_keyword_ids").SequenceEqual(new[] { "Innate" }) && Strings(madRow, "local_keyword_ids").Count == 0, "Mad Science+ gains Innate on the clone only");
            Check(!Preview(plainRow).ContainsKey("tinker_time_rider"), "other cards carry no rider");

            // --- a required turn read failing on the clone makes the preview unavailable ---
            foreach (string field in new[] { "sly_this_turn", "base_replay_count", "extra_replay_this_turn" })
            {
                var broken = Place(player, PileType.Draw, new CardModel { Name = "Broken", PreviewThrowOn = { field } });
                var brokenRow = Row(broken, PileType.Draw);
                var brokenPreview = Preview(brokenRow);
                Check(Equals(brokenPreview["status"], "unavailable") && Reasons(brokenPreview)[field].Contains($"{field} failed")
                    && brokenPreview.Keys.OrderBy(k => k).SequenceEqual(new[] { "from_level", "status", "unavailable_reasons" }), $"{field} failure on the clone is unavailable with its path");
                Check(brokenRow[field] != null && !Reasons(brokenRow).ContainsKey(field), $"{field}: the live row still reads it");
            }

            // --- selection candidates: pile instance or detached values ---
            UidRequests.Clear();
            var inHand = Place(player, PileType.Hand, new CardModel { Name = "InHand" });
            var handInstance = BuildSelectionCandidateInstance(inHand);
            Check(Equals(handInstance["source"], "pile") && Equals(handInstance["pile"], "Hand") && Equals(handInstance["uid"], GetStableCardUid(inHand))
                && !handInstance.ContainsKey("values") && Reasons(handInstance).Count == 0, "a pile candidate references its pile row uid");
            foreach (var pile in new[] { PileType.Draw, PileType.Discard, PileType.Exhaust, PileType.Play })
            {
                var inPile = Place(player, pile, new CardModel { Name = $"In{pile}" });
                var instance = BuildSelectionCandidateInstance(inPile);
                Check(Equals(instance["source"], "pile") && Equals(instance["pile"], pile.ToString()), $"{pile} candidate is a pile instance");
            }

            UidRequests.Clear();
            var generated = new MadScience { OwnerPlayer = player };
            generated.TinkerTimeRider = TinkerTime.RiderEffect.Sapping;
            generated.Enchant(new Glam(), 1);
            generated.GiveSingleTurnSly();
            generated.EnergyCost.Modifiers.Add(new LocalCostModifier(0, LocalCostType.Absolute, LocalCostModifierExpiration.EndOfTurn, false));
            var pileOrders = player.Piles.ToDictionary(p => p.Type, p => p.Cards.ToList());
            string generatedBefore = LiveSnapshot(generated);
            var detached = BuildSelectionCandidateInstance(generated);
            var values = Obj(detached, "values");
            Check(Equals(detached["source"], "detached") && detached["pile"] == null && !detached.ContainsKey("uid") && UidRequests.Count == 0,
                "a candidate in no pile is detached and gets no uid");
            Check(Equals(values["id"], "TEST") && Equals(values["type"], "Attack") && Equals(values["target_type"], "AnyEnemy") && Equals(values["upgrade_level"], 0)
                && Equals(values["max_upgrade_level"], 1) && Equals(Obj(values, "dynamic_vars")["ViolenceHits"], 3m), "detached values: id, shape, level, numbers");
            Check(Equals(values["tinker_time_rider"], "Sapping") && Equals(Obj(values, "enchantment")["id"], "GLAM") && Equals(values["enchanted_replay_count"], 1)
                && values["sly_this_turn"] is true && values["single_turn_sly"] is true && values["single_turn_retain"] is false, "detached values: rider, enchantment, replays, single-turn expiry");
            Check(values["keyword_ids"] is List<string> && values["local_keyword_ids"] is List<string> && values["exhaust_on_next_play"] is false, "detached values: both keyword sets and the exhaust flag");
            var detachedEnergy = Obj(values, "energy_cost");
            Check(Equals(detachedEnergy["with_local_modifiers"], 0) && detachedEnergy["with_all_modifiers"] == null && Reasons(detachedEnergy)["with_all_modifiers"] == "detached_preview_context"
                && values["star_cost_state"] is Dictionary<string, object?>, "detached energy cost: local modifiers, no global context");
            Check(!values.ContainsKey("can_play") && !values.ContainsKey("unplayable_reason") && !values.ContainsKey("vs_targets") && !values.ContainsKey("upgrade_preview"),
                "no playability, target or upgrade evaluation on a detached candidate");
            Check(Reasons(values).Count == 0 && Reasons(detached).Count == 0, "a clean detached read has no reasons");
            Check(LiveSnapshot(generated) == generatedBefore && player.Piles.All(p => p.Cards.SequenceEqual(pileOrders[p.Type])) && generated.Pile == null,
                "reading a candidate moves nothing and changes nothing");

            var deckCard = Place(player, PileType.Deck, new CardModel { Name = "DeckCard" });
            var deckInstance = BuildSelectionCandidateInstance(deckCard);
            Check(Equals(deckInstance["source"], "detached") && Equals(deckInstance["pile"], "Deck") && deckInstance["values"] is Dictionary<string, object?>,
                "a card in a non-combat pile is detached and names the pile");

            BeginStateWarnings();
            var twice = Place(player, PileType.Hand, new CardModel { Name = "Twice" });
            player.Pile(PileType.Discard).Cards.Add(twice);
            var ambiguous = BuildSelectionCandidateInstance(twice);
            Check(ambiguous["source"] == null && ambiguous["pile"] == null && Reasons(ambiguous)["source"].StartsWith("ambiguous: in 2 piles")
                && !ambiguous.ContainsKey("uid") && !ambiguous.ContainsKey("values"), "a card in two piles is not mapped to either");
            Check(_stateWarnings.Any(w => w.StartsWith("card.combat_instance.source:")), "ambiguity warns");
            player.Pile(PileType.Discard).Cards.Remove(twice);

            var ownerless = new CardModel { Name = "Ownerless", ThrowOn = { "owner" } };
            var ownerFailed = BuildSelectionCandidateInstance(ownerless);
            Check(ownerFailed["source"] == null && Reasons(ownerFailed)["source"].Contains("owner failed") && Reasons(ownerFailed).ContainsKey("pile"),
                "an unreadable owner leaves the source unknown with its reason");

            var slyBroken = new CardModel { Name = "SlyBroken", OwnerPlayer = player, ThrowOn = { "sly_this_turn" } };
            var slyBrokenValues = Obj(BuildSelectionCandidateInstance(slyBroken), "values");
            Check(slyBrokenValues.ContainsKey("sly_this_turn") && slyBrokenValues["sly_this_turn"] == null && Reasons(slyBrokenValues)["sly_this_turn"].Contains("sly_this_turn failed")
                && slyBrokenValues["single_turn_sly"] is false, "a detached value failure is null with its reason; the others stay");

            using (var json = JsonDocument.Parse(Json(new Dictionary<string, object?> { ["pile"] = handInstance, ["detached"] = detached, ["ambiguous"] = ambiguous })))
            {
                var root = json.RootElement;
                Check(root.GetProperty("pile").GetProperty("uid").GetString() == GetStableCardUid(inHand)
                    && root.GetProperty("detached").GetProperty("pile").ValueKind == JsonValueKind.Null
                    && root.GetProperty("detached").GetProperty("values").GetProperty("enchanted_replay_count").GetInt32() == 1
                    && root.GetProperty("ambiguous").GetProperty("source").ValueKind == JsonValueKind.Null, "wire: uid string, explicit nulls, numeric values");
            }
            string md = Markdown(sb =>
            {
                AppendCombatInstanceMarkdown(sb, new() { ["combat_instance"] = handInstance });
                AppendCombatInstanceMarkdown(sb, new() { ["combat_instance"] = detached });
                AppendCombatInstanceMarkdown(sb, new() { ["combat_instance"] = deckInstance });
                AppendCombatInstanceMarkdown(sb, new() { ["combat_instance"] = ambiguous });
                AppendCombatInstanceMarkdown(sb, new() { ["uid"] = "out of combat" });
            });
            Check(md.Contains($"  - instance: Hand `{GetStableCardUid(inHand)}`") && md.Contains("  - instance: detached, values in JSON")
                && md.Contains("  - instance: detached (in Deck), values in JSON") && md.Contains("  - instance: unknown (ambiguous: in 2 piles")
                && md.Split('\n').Count(line => line.StartsWith("  - instance")) == 4, "markdown: one line per combat candidate, nothing out of combat");

            // --- wiring: every in-combat selection screen, out-of-combat shapes unchanged ---
            string sources = Path.Combine(AppContext.BaseDirectory, "Sources");
            string Source(string file) => File.ReadAllText(Path.Combine(sources, file));
            string builder = Source("McpMod.StateBuilder.cs");
            string Slice(string text, string start, string end) => text[text.IndexOf(start, StringComparison.Ordinal)..text.IndexOf(end, text.IndexOf(start, StringComparison.Ordinal), StringComparison.Ordinal)];
            string handSelect = Slice(builder, "private static Dictionary<string, object?> BuildHandSelectState", "// Confirm button state");
            string reward = Slice(builder, "private static Dictionary<string, object?> BuildCardRewardState", "var alternatives");
            foreach (var (name, body) in new[] { ("hand_select", handSelect), ("card_reward", reward) })
                Check(body.Contains("var combatState = GetLiveCombatStateForSelection();") && body.Contains("if (combatState != null) RefreshCardUidRegistry(combatState);")
                    && body.Contains("if (combatState != null) cardInfo[\"combat_instance\"] = BuildSelectionCandidateInstance(card);"), $"{name}: combat_instance only during a live combat");
            string grid = Slice(Source("McpMod.CardSelection.cs"), "BuildCardSelectCardRows(", "FindCardSelectClickMethod");
            Check(grid.IndexOf("row[\"combat_instance\"] = BuildSelectionCandidateInstance(card);", StringComparison.Ordinal) > grid.IndexOf("if (combatState != null)", StringComparison.Ordinal),
                "card_select: combat_instance inside the combat branch");
            string choose = Source("McpMod.ChooseCardSelection.cs");
            Check(choose.IndexOf("row[\"combat_instance\"] = BuildSelectionCandidateInstance(card);", StringComparison.Ordinal) > choose.IndexOf("else", choose.IndexOf("if (combatState == null)", StringComparison.Ordinal), StringComparison.Ordinal),
                "choose: combat_instance on the combat rows only");
            string candidate = string.Join("\n", Source("McpMod.SelectionCandidate.cs").Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
            foreach (string forbidden in new[] { "CanPlay", "AttachCardPlayability", "BuildCardTargetPreviews", "UpdateDynamicVarPreview", "CardCmd", "CloneCard", "MutableClone", "Rng", "AddCard" })
                Check(!candidate.Contains(forbidden + "(", StringComparison.Ordinal) && !candidate.Contains(forbidden + ".", StringComparison.Ordinal), $"candidate reader does not call {forbidden}");
            Check(candidate.IndexOf("GetStableCardUid(card)", StringComparison.Ordinal) > candidate.IndexOf("state[\"source\"] = CandidateSourcePile;", StringComparison.Ordinal)
                && candidate.Split("GetStableCardUid(").Length == 2, "a uid is requested only for a pile instance");
            BeginStateWarnings();
        }

        public static void RunCombatUpgradePreviewContract()
        {
            BeginStateWarnings();
            var player = NewPlayer();

            // --- ready: a plain card in each combat pile, absolute values of the upgraded clone ---
            foreach (var pile in new[] { PileType.Hand, PileType.Draw, PileType.Discard, PileType.Exhaust, PileType.Play })
            {
                var strike = Place(player, pile, new CardModel { Name = "Strike", TagSet = { CardTag.Strike } });
                string before = LiveSnapshot(strike);
                var row = Row(strike, pile);
                var preview = Preview(row);
                Check(Equals(preview["status"], "ready") && Equals(preview["from_level"], 0), $"{pile}: ready from level 0");
                Check(Equals(Obj(preview, "dynamic_vars")["Damage"], 9m) && Equals(Obj(row, "dynamic_vars")["Damage"], 6m), $"{pile}: clone BaseValue 6 -> 9, live row keeps 6");
                Check(Equals(preview["upgrade_level"], 1) && Equals(preview["max_upgrade_level"], 1) && preview["is_upgraded"] is true && preview["is_upgradable"] is false, $"{pile}: next level and cap from the clone's getters");
                Check(Equals(preview["id"], "TEST") && Equals(preview["name"], "Strike+") && Equals(preview["type"], "Attack") && Equals(preview["target_type"], "AnyEnemy"), $"{pile}: id, name, shape");
                Check(Equals(preview["description"], $"Strike+ Damage 9 at {pile}"), $"{pile}: description at the parent's pile, no fallback");
                Check(Strings(preview, "tags").SequenceEqual(new[] { "Strike" }) && Strings(preview, "keyword_ids").Count == 0 && Strings(preview, "local_keyword_ids").Count == 0, $"{pile}: tags and empty keyword sets");
                Check(preview["affliction"] == null && preview["enchantment"] == null && preview.ContainsKey("affliction"), $"{pile}: known absent attachments are null");
                Check(Reasons(preview).Count == 0, $"{pile}: ready has no reasons");
                Check(Equals(row["upgrade_level"], 0) && Equals(row["max_upgrade_level"], 1) && Strings(row, "keyword_ids").Count == 0 && row["retain_this_turn"] is false && row["single_turn_retain"] is false, $"{pile}: parent level, cap, keywords, retain");
                Check(!preview.ContainsKey("keywords"), $"{pile}: hover tips are not duplicated into the preview");
                var energy = Obj(preview, "energy_cost");
                Check(Equals(energy["base"], 1) && Equals(energy["with_local_modifiers"], 1) && energy["with_all_modifiers"] == null
                    && Reasons(energy)["with_all_modifiers"] == "detached_preview_context", $"{pile}: detached clone has no all-modifier cost");
                Check(Obj(row, "energy_cost")["with_all_modifiers"] is 1, $"{pile}: live row still computes all modifiers");
                Check(LiveSnapshot(strike) == before && strike.Pile!.Type == pile, $"{pile}: live card unchanged");
            }

            // --- not_upgradable: at the cap, including cap 0 (Wound / Dazed / AscendersBane). No clone. ---
            var upgraded = Place(player, PileType.Discard, new CardModel { Name = "Bash" });
            upgraded.UpgradeInternal();
            upgraded.FinalizeUpgradeInternal();
            var wound = Place(player, PileType.Draw, new CardModel { Name = "Wound", CardType = CardType.Status, MaxLevel = 0, Canonical = new[] { CardKeyword.Unplayable } });
            _upgradePreviewStats = default;
            foreach (var (card, level) in new[] { ((CardModel)upgraded, 1), (wound, 0) })
            {
                var row = Row(card, card.Pile!.Type);
                var preview = Preview(row);
                Check(Equals(preview["status"], "not_upgradable") && Equals(preview["from_level"], level) && Reasons(preview).Count == 0, $"{card.Name}: not_upgradable at level {level}");
                Check(preview.Keys.OrderBy(k => k).SequenceEqual(new[] { "from_level", "status", "unavailable_reasons" }), $"{card.Name}: not_upgradable carries no clone values");
            }
            Check(_upgradePreviewStats.Clones == 0 && _upgradePreviewStats.NotUpgradable == 2, "not_upgradable makes no clone");
            Check(Strings(Row(wound, PileType.Draw), "keyword_ids").SequenceEqual(new[] { "Unplayable" }), "parent keyword ids are enum names");

            // --- multi-level: DingyDagger 7 -> 11 -> 16, the next level only ---
            var dagger = Place(player, PileType.Hand, new DingyDaggerDouble());
            var daggerPreview = Preview(Row(dagger, PileType.Hand));
            Check(Equals(Obj(daggerPreview, "dynamic_vars")["Damage"], 11m) && Equals(daggerPreview["upgrade_level"], 1) && daggerPreview["is_upgradable"] is true && Equals(daggerPreview["name"], "Dingy Dagger+1"), "DingyDagger level 0 -> 1 is 11 and still upgradable");
            dagger.UpgradeInternal();
            dagger.FinalizeUpgradeInternal();
            daggerPreview = Preview(Row(dagger, PileType.Hand));
            Check(Equals(daggerPreview["from_level"], 1) && Equals(Obj(daggerPreview, "dynamic_vars")["Damage"], 16m) && Equals(daggerPreview["upgrade_level"], 2) && Equals(daggerPreview["max_upgrade_level"], 999999999), "DingyDagger level 1 -> 2 is 16 (3 + the new level)");

            // --- isolation: enchanted + afflicted live card with every listener attached ---
            var isolated = Place(player, PileType.Exhaust, new CardModel { Name = "Hexed" });
            var enchantment = new EnchantmentModel { ModelId = new("SHARP") };
            isolated.Enchant(enchantment, 3);
            var affliction = new MegaCrit.Sts2.Core.Models.Afflictions.Hexed();
            isolated.Afflict(affliction, 1);
            int notifications = 0, enchantmentNotifications = 0, afflictionAmountNotifications = 0;
            isolated.SubscribeAll(() => notifications++);
            enchantment.StatusChanged += () => enchantmentNotifications++;
            enchantment.ExecutionFinished += _ => enchantmentNotifications++;
            affliction.AmountChanged += (_, _) => afflictionAmountNotifications++;
            player.Hex = true;
            int recalcBefore = player.RecalculateCardValuesCalls;
            string isolatedBefore = LiveSnapshot(isolated);
            var pileOrders = player.Piles.ToDictionary(p => p.Type, p => p.Cards.ToList());
            var isolatedRow = Row(isolated, PileType.Exhaust);
            var isolatedPreview = Preview(isolatedRow);
            Check(Equals(isolatedPreview["status"], "ready"), "enchanted and afflicted card previews");
            Check(notifications == 0 && enchantmentNotifications == 0 && afflictionAmountNotifications == 0, "no live card / attachment listener is notified");
            Check(player.RecalculateCardValuesCalls == recalcBefore, "no RecalculateCardValues from re-afflicting the clone");
            Check(LiveSnapshot(isolated) == isolatedBefore, "live card values, keywords, attachments unchanged");
            Check(ReferenceEquals(isolated.Enchantment, enchantment) && ReferenceEquals(enchantment.AttachedCard, isolated)
                && ReferenceEquals(isolated.Affliction, affliction) && ReferenceEquals(affliction.AttachedCard, isolated), "live attachments still belong to the live card");
            Check(player.Piles.All(p => p.Cards.SequenceEqual(pileOrders[p.Type])), "every pile keeps its cards and order");
            Check(Equals(Obj(isolatedPreview, "enchantment")["id"], "SHARP") && Equals(Obj(isolatedPreview, "enchantment")["amount"], 3)
                && Equals(Obj(isolatedPreview, "affliction")["id"], "HEXED") && Equals(Obj(isolatedPreview, "affliction")["amount"], 1), "attachments are cloned onto the preview");
            Check(Strings(isolatedPreview, "keyword_ids").SequenceEqual(new[] { "Ethereal" }) && Strings(isolatedPreview, "local_keyword_ids").Count == 0, "combat keyword (Hex: Ethereal) on the clone, not in its local set");
            Check(Strings(isolatedRow, "keyword_ids").SequenceEqual(new[] { "Ethereal" }) && Strings(isolatedRow, "local_keyword_ids").Count == 0, "live row: effective vs local keyword sets");
            // Control: the hazard the isolation exists for. A plain MutableClone of the same card
            // re-enchants while the clone still holds the live delegates.
            isolated.MutableClone();
            Check(notifications >= 2, "control: plain MutableClone notifies live EnchantmentChanged / AfflictionChanged listeners");
            notifications = 0;
            Check(Json(Row(isolated, PileType.Exhaust)) == Json(isolatedRow) && notifications == 0, "repeated reads are identical and silent");
            isolated.FireAll();
            Check(notifications == 11, "control: the counter sees every live event");
            player.Hex = false;

            // --- Apparition: the upgrade removes a local keyword on the clone only ---
            var apparition = Place(player, PileType.Draw, new ApparitionDouble());
            int keywordNotifications = 0;
            apparition.SubscribeAll(() => keywordNotifications++);
            var apparitionRow = Row(apparition, PileType.Draw);
            var apparitionPreview = Preview(apparitionRow);
            Check(Strings(apparitionPreview, "local_keyword_ids").SequenceEqual(new[] { "Exhaust" }) && Strings(apparitionRow, "local_keyword_ids").SequenceEqual(new[] { "Exhaust", "Ethereal" }), "Apparition+ loses Ethereal on the clone, the live card keeps it");
            Check(Obj(apparitionPreview, "dynamic_vars").Count == 0 && Equals(apparitionPreview["status"], "ready"), "an empty DynamicVars set is a successful read");
            Check(keywordNotifications == 0, "RemoveKeyword on the clone notifies nobody");

            // --- energy: upgrade lowers base and clips Absolute modifiers; FreeAttack is pile-only ---
            player.FreeAttack = true;
            var bodySlam = Place(player, PileType.Hand, new CostUpgradeDouble { Name = "Body Slam", CanonicalCost = 2, Addend = -2 });
            bodySlam.EnergyCost.Modifiers.Add(new LocalCostModifier(1, LocalCostType.Absolute, LocalCostModifierExpiration.WhenPlayed, false));
            int hooksOnPileless = player.EnergyHookCallsOnPilelessCards;
            string bodySlamBefore = LiveSnapshot(bodySlam);
            var bodySlamRow = Row(bodySlam, PileType.Hand);
            var bodySlamEnergy = Obj(Preview(bodySlamRow), "energy_cost");
            var bodySlamMods = (List<Dictionary<string, object?>>)bodySlamEnergy["local_modifiers"]!;
            Check(Equals(bodySlamEnergy["base"], 0) && Equals(bodySlamEnergy["base_raw"], 0) && Equals(bodySlamEnergy["canonical"], 2)
                && Equals(bodySlamMods.Single()["amount"], 0) && Equals(bodySlamEnergy["with_local_modifiers"], 0), "CardEnergyCost.UpgradeBy lowers the base and clips the Absolute modifier on the clone");
            Check(Equals(Obj(bodySlamRow, "energy_cost")["base"], 2) && Equals(((List<Dictionary<string, object?>>)Obj(bodySlamRow, "energy_cost")["local_modifiers"]!).Single()["amount"], 1)
                && Equals(Obj(bodySlamRow, "energy_cost")["with_all_modifiers"], 0), "live card keeps base 2, Absolute 1, and FreeAttack makes it 0 in hand");
            Check(bodySlamEnergy["with_all_modifiers"] == null && player.EnergyHookCallsOnPilelessCards - hooksOnPileless == 1, "machine cost never runs global hooks on the clone; only the display cost does (once)");
            Check(Equals(Preview(bodySlamRow)["cost"], "0"), "display cost is what the game's own pileless preview card shows");
            Check(LiveSnapshot(bodySlam) == bodySlamBefore, "clipping happened on cloned modifiers only");
            player.FreeAttack = false;

            // --- X cost ---
            var whirlwind = Place(player, PileType.Discard, new CardModel { Name = "Whirlwind", CostsXValue = true });
            whirlwind.EnergyCost.CapturedXValue = 3;
            var xPreview = Preview(Row(whirlwind, PileType.Discard));
            var xEnergy = Obj(xPreview, "energy_cost");
            Check(Equals(xPreview["status"], "ready") && xEnergy["costs_x"] is true && xEnergy["base"] == null && Reasons(xEnergy)["base"] == "x_cost"
                && Equals(xEnergy["captured_x_value"], 3) && Equals(xPreview["cost"], "X"), "X cost: no base is normal, X capture is copied");

            // --- star cost: UpgradeStarCostBy drops temporary costs above the new base ---
            var star = Place(player, PileType.Draw, new StarUpgradeDouble());
            star.AddTemporaryStarCost(new TemporaryCardCost(2, true, false));
            var starPreview = Preview(Row(star, PileType.Draw));
            var starState = Obj(starPreview, "star_cost_state");
            Check(Equals(starState["base"], 1) && Equals(starState["current"], 1) && ((System.Collections.ICollection)starState["temporary_costs"]!).Count == 0, "star base 2 -> 1 removes the temporary 2 on the clone");
            Check(star.TemporaryStarCosts.Count == 1 && star.BaseStarCost == 2, "live star costs unchanged");

            // --- single-turn retain is separate from the Retain keyword ---
            var retained = Place(player, PileType.Hand, new CardModel { Name = "Held" });
            retained.GiveSingleTurnRetain();
            var retainedRow = Row(retained, PileType.Hand);
            Check(retainedRow["retain_this_turn"] is true && retainedRow["single_turn_retain"] is true && !Strings(retainedRow, "keyword_ids").Contains("Retain"), "single-turn retain is not a keyword");

            // --- failures stay local: required field, display field, clone, consistency, parent ---
            BeginStateWarnings();
            var broken = Place(player, PileType.Draw, new CardModel { Name = "Broken", PreviewThrowOn = { "description" } });
            var neighbour = Place(player, PileType.Draw, new CardModel { Name = "Neighbour" });
            var brokenRow = Row(broken, PileType.Draw);
            var brokenPreview = Preview(brokenRow);
            Check(Equals(brokenPreview["status"], "unavailable") && Equals(brokenPreview["from_level"], 0) && Reasons(brokenPreview)["description"].Contains("description failed"), "required description failure is unavailable with its path");
            Check(brokenPreview.Keys.OrderBy(k => k).SequenceEqual(new[] { "from_level", "status", "unavailable_reasons" }), "unavailable carries no partial values");
            Check(Equals(Preview(Row(neighbour, PileType.Draw))["status"], "ready") && Equals(Obj(brokenRow, "dynamic_vars")["Damage"], 6m), "other cards and the live row are unaffected");
            Check(_stateWarnings.Any(w => w.StartsWith("card.upgrade_preview.description:")), "warning names the preview field");

            var varsBroken = Place(player, PileType.Draw, new CardModel { PreviewThrowOn = { "dynamic_vars" } });
            var varsPreview = Preview(Row(varsBroken, PileType.Draw));
            Check(Equals(varsPreview["status"], "unavailable") && Reasons(varsPreview)["clone"].Contains("dynamic_vars failed"), "an upgrade that throws on the clone is unavailable (clone)");
            var typeBroken = Place(player, PileType.Draw, new CardModel { PreviewThrowOn = { "type", "tags" } });
            var typePreview = Preview(Row(typeBroken, PileType.Draw));
            Check(Equals(typePreview["status"], "unavailable") && Reasons(typePreview).Keys.OrderBy(k => k).SequenceEqual(new[] { "tags", "type" }), "detail getters failing on the clone keep their own paths");

            var displayBroken = Place(player, PileType.Hand, new CardModel());
            displayBroken.EnergyCost.ThrowAllOnPreview = true;
            var displayPreview = Preview(Row(displayBroken, PileType.Hand));
            Check(Equals(displayPreview["status"], "ready") && displayPreview["cost"] == null && Reasons(displayPreview).Keys.SequenceEqual(new[] { "cost" }), "display-only failure keeps ready with its reason");

            var baseBroken = Place(player, PileType.Draw, new CardModel());
            baseBroken.EnergyCost.ThrowCanonicalOnPreview = true;
            var baseBrokenPreview = Preview(Row(baseBroken, PileType.Draw));
            Check(Equals(baseBrokenPreview["status"], "unavailable") && Reasons(baseBrokenPreview).ContainsKey("energy_cost.base_raw"), "missing energy base is unavailable, unlike X / no-cost");

            var custom = Place(player, PileType.Draw, new CustomEventDouble());
            var customPreview = Preview(Row(custom, PileType.Draw));
            Check(Equals(customPreview["status"], "unavailable") && Reasons(customPreview)["clone"].Contains("Cannot isolate event CustomEventDouble.Custom"), "an event that cannot be cleared refuses the clone");
            Check(_modelEventFields.TryGetValue(typeof(CustomEventDouble), out var refusal) && refusal.Fields == null && refusal.Error!.Contains("CustomEventDouble.Custom")
                && Equals(Preview(Row(Place(player, PileType.Draw, new CustomEventDouble()), PileType.Draw))["status"], "unavailable"), "the refusal is cached per type and reused");

            var cap = Place(player, PileType.Draw, new CapChangingDouble());
            var capPreview = Preview(Row(cap, PileType.Draw));
            Check(Equals(capPreview["status"], "unavailable") && Reasons(capPreview)["max_upgrade_level"].StartsWith("inconsistent"), "cap mismatch is unavailable");
            Check(!cap.Grown, "the inconsistent OnUpgrade ran on the clone only");

            var parentBroken = Place(player, PileType.Draw, new CardModel { ThrowOn = { "is_upgradable" } });
            var parentPreview = Preview(Row(parentBroken, PileType.Draw));
            Check(Equals(parentPreview["status"], "unavailable") && Equals(parentPreview["from_level"], 0) && Reasons(parentPreview).ContainsKey("parent.is_upgradable"), "unreadable parent flag is unavailable, not not_upgradable");
            var levelBroken = Place(player, PileType.Draw, new CardModel { ThrowOn = { "upgrade_level" } });
            var levelRow = Row(levelBroken, PileType.Draw);
            Check(Equals(Preview(levelRow)["status"], "unavailable") && Preview(levelRow)["from_level"] == null && levelRow["upgrade_level"] == null && Reasons(levelRow).ContainsKey("upgrade_level"), "unreadable level: null with reasons on the row and the preview");
            Check(Markdown(sb => AppendCardDetailsMarkdown(sb, levelRow)).Contains("  - upgrade_level null (InvalidOperationException: upgrade_level failed)"), "row markdown shows the unreadable level");

            // --- wire format and markdown ---
            using (var json = JsonDocument.Parse(Json(Row(neighbour, PileType.Draw))))
            {
                var p = json.RootElement.GetProperty("upgrade_preview");
                Check(p.GetProperty("status").GetString() == "ready" && p.GetProperty("dynamic_vars").GetProperty("Damage").GetDecimal() == 9m
                    && p.GetProperty("energy_cost").GetProperty("with_all_modifiers").ValueKind == JsonValueKind.Null, "wire keeps game variable casing, decimals and explicit nulls");
            }
            using (var json = JsonDocument.Parse(Json(Row(levelBroken, PileType.Draw))))
                Check(json.RootElement.GetProperty("upgrade_preview").GetProperty("from_level").ValueKind == JsonValueKind.Null, "unknown from_level is an explicit null");

            var handRow = Row(neighbour, PileType.Hand);
            Check(Markdown(sb => AppendUpgradePreviewLine(sb, handRow, showReady: false)) == "", "combat rows print no ready line (values are in the JSON)");
            Check(Markdown(sb => AppendUpgradePreviewLine(sb, handRow)).Contains("→ **Upgraded**: (1 energy) - Neighbour+ Damage 9 at Hand"), "a caller asking for ready lines gets the upgraded form");
            Check(Markdown(sb => AppendUpgradePreviewLine(sb, Row(upgraded, PileType.Discard))) == "", "not_upgradable prints nothing");
            Check(Markdown(sb => AppendUpgradePreviewLine(sb, brokenRow)).Contains("→ upgrade preview unavailable (description: InvalidOperationException: description failed)"), "unavailable prints its reasons");
            var legacy = new Dictionary<string, object?> { ["upgrade_preview"] = new Dictionary<string, object?> { ["cost"] = "1", ["description"] = "Deal 9 damage." } };
            Check(Markdown(sb => AppendUpgradePreviewLine(sb, legacy)).Contains("→ **Upgraded**: (1 energy) - Deal 9 damage."), "out-of-combat preview without status still prints");
            var piles = new Dictionary<string, object?>
            {
                ["draw_pile_count"] = 2, ["discard_pile_count"] = 0, ["exhaust_pile_count"] = 0, ["play_pile_count"] = 0,
                ["draw_pile"] = new List<Dictionary<string, object?>> { Row(neighbour, PileType.Draw), brokenRow }
            };
            foreach (var r in (List<Dictionary<string, object?>>)piles["draw_pile"]!) { r["star_cost"] = null; }
            string pileMd = Markdown(sb => FormatDeckPilesMarkdown(sb, piles));
            Check(!pileMd.Contains("**Upgraded**") && pileMd.Contains("upgrade preview unavailable (description"), "pile markdown prints failures only");

            // --- perf counters ---
            var measured = BuildStateMeasured(() =>
            {
                Row(neighbour, PileType.Draw);
                Row(upgraded, PileType.Discard);
                return new Dictionary<string, object?>();
            }, perf: true);
            var perf = Obj(Obj(measured, "state_build_perf"), "upgrade_preview");
            Check(Equals(perf["rows"], 2) && Equals(perf["clones"], 1) && Equals(perf["ready"], 1) && Equals(perf["not_upgradable"], 1) && Equals(perf["unavailable"], 0)
                && perf["elapsed_ms"] is double && Obj(measured, "state_build_perf")["build_ms"] is double, "perf reports rows, clones, statuses and times");
            Check(!BuildStateMeasured(() => new Dictionary<string, object?>(), perf: false).ContainsKey("state_build_perf"), "perf is opt-in");

            // --- size estimate: f35-like (26 cards, 18 upgradable) and f40-like (25 cards, 23 upgradable) ---
            foreach (var (label, total, upgradable) in new[] { ("f35 r1 s00 shape", 26, 18), ("f40 r4 s00 shape", 25, 23) })
            {
                var sizer = NewPlayer();
                int added = 0;
                for (int i = 0; i < total; i++)
                {
                    var pile = i < 5 ? PileType.Hand : i < 15 ? PileType.Draw : i < 22 ? PileType.Discard : PileType.Exhaust;
                    var card = Place(sizer, pile, new CardModel { Name = "Strike", TagSet = { CardTag.Strike }, CanonicalVarValues = new() { ["Damage"] = 6m, ["Vulnerable"] = 2m } });
                    if (i >= upgradable) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
                    var row = Row(card, pile);
                    string with = Json(row);
                    foreach (string key in new[] { "upgrade_preview", "upgrade_level", "max_upgrade_level", "local_keyword_ids", "keyword_ids", "retain_this_turn", "single_turn_retain",
                        "sly_this_turn", "single_turn_sly", "base_replay_count", "extra_replay_this_turn", "enchanted_replay_count" })
                        row.Remove(key);
                    added += Encoding.UTF8.GetByteCount(with) - Encoding.UTF8.GetByteCount(Json(row));
                }
                if (label.StartsWith("f35")) Console.WriteLine("  sample ready (pile row): " + Json(Preview(Row(Place(sizer, PileType.Draw, new CardModel { Name = "Strike", TagSet = { CardTag.Strike }, CanonicalVarValues = new() { ["Damage"] = 6m, ["Vulnerable"] = 2m } }), PileType.Draw))));
                Console.WriteLine($"  size estimate {label}: +{added} bytes for {total} rows ({upgradable} ready, {total - upgradable} not_upgradable)");
                Check(added > 0, $"{label}: size estimate computed");
            }

            RunCardEffectInputsContract();

            // --- wiring (sources) ---
            string sources = Path.Combine(AppContext.BaseDirectory, "Sources");
            string Source(string file) => File.ReadAllText(Path.Combine(sources, file));
            string builder = Source("McpMod.StateBuilder.cs");
            string Slice(string start, string end) => builder[builder.IndexOf(start, StringComparison.Ordinal)..builder.IndexOf(end, builder.IndexOf(start, StringComparison.Ordinal), StringComparison.Ordinal)];
            string hand = Slice("private static Dictionary<string, object?> BuildCardState", "private static List<Dictionary<string, object?>> BuildCardTargetPreviews");
            Check(hand.Contains("AttachCombatUpgradeState(state, card, PileType.Hand)"), "hand rows always attach the combat upgrade state");
            string pileBuilder = Slice("private static List<Dictionary<string, object?>> BuildPileCardList", "private static List<Dictionary<string, object?>> BuildEnemyList");
            int playability = pileBuilder.IndexOf("AttachCardPlayability(state, card)", StringComparison.Ordinal);
            Check(playability >= 0 && pileBuilder.IndexOf("AttachCombatUpgradeState(state, card, pile)", StringComparison.Ordinal) > playability, "pile rows attach it after the details (reasons dictionary)");
            Check(!builder.Contains("CanUpgradeHandThisTurn") && !builder.Contains("HandUpgradeCardIds"), "no conditional hand-only preview remains");
            Check(Slice("private static Dictionary<string, object?> BuildHandSelectState", "// Confirm button state").Contains("AttachUpgradePreview(cardInfo, card)")
                && Slice("private static Dictionary<string, object?> BuildCardRewardState", "var alternatives").Contains("AttachUpgradePreview(cardInfo, card)"), "hand_select and rewards keep the out-of-combat preview contract");
            Check(Source("McpMod.Helpers.cs").Contains("? CreateIsolatedMutableClone(card)"), "out-of-combat preview of a live card also uses the isolated clone");
            // Code only: the comments cite CardCmd.Upgrade / CloneCard as what is not used.
            string previewSource = string.Join("\n", Source("McpMod.CombatUpgradePreview.cs").Split('\n')
                .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
            foreach (string forbidden in new[] { "GetStableCardUid", "CardCmd", "CloneCard", "Rng", "AddCard", "RecalculateCardValues", "UpdateDynamicVarPreview(", "History" })
                Check(!previewSource.Contains(forbidden + "(", StringComparison.Ordinal) && !previewSource.Contains(forbidden + ".", StringComparison.Ordinal), $"preview code does not call {forbidden}");
            Check(previewSource.Contains("clone.UpgradeInternal();") && previewSource.Contains("clone.FinalizeUpgradeInternal();"), "the clone gets the two calls CardCmd.Upgrade makes");
            string mod = Source("McpMod.cs");
            Check(mod.Contains("public const int StateSchemaVersion = 10;"), "combat upgrade_preview changed meaning at schema 5; current schema is 10 (sts2#1037)");
            Check(mod.Contains("BuildStateMeasured(BuildGameState, perf)") && mod.Contains("BuildStateMeasured(BuildMultiplayerGameState, perf)"), "?perf=1 on both state endpoints");
            Check(Source("McpMod.MultiplayerState.cs").Contains("BuildPlayerState(localPlayer)"), "multiplayer shares the player builder");
            Check(Source("McpMod.Formatting.cs").Contains("AppendUpgradePreviewLine(sb, card, showReady: false);"), "hand markdown prints failures only");

            Console.WriteLine($"CombatUpgradePreviewContract: {Checks} checks passed");
        }
    }
}
