using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace STS2_MCP;

public static partial class McpMod
{
    private static int Checks;
    private static void Check(bool condition, string message)
    {
        Checks++;
        if (!condition) throw new Exception(message);
    }

    private static Dictionary<string, object?> Row(string section, object? model, string context = "test")
    {
        var row = new Dictionary<string, object?> { ["amount"] = 1 };
        AttachAllowlistedState(row, section, model, context);
        return row;
    }
    private static Dictionary<string, object?> Internal(Dictionary<string, object?> row)
        => (Dictionary<string, object?>)row["internal_state"]!;
    private static Dictionary<string, string>? Reasons(Dictionary<string, object?> values)
        => values.GetValueOrDefault("unavailable_reasons") as Dictionary<string, string>;
    private static bool Seq<T>(object? value, params T[] expected)
        => value is IEnumerable<T> items && items.SequenceEqual(expected);
    private static string Source(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Sources", name));

    // 生成物そのものの検査: section・キー・型名・備考の形と、隠す情報を指さないこと。
    private static void RunGeneratedTableContract()
    {
        var sections = new HashSet<string> { "card", "relic", "power", "player", "battle", "run" };
        Check(StateAllowlist.Length == 35, "許可リストは 35 項目");
        Check(!StateAllowlist.Any(e => e.Key == "wongo_points"), "ラン中に読まれない wongo_points は出さない（cosmetic）");
        foreach (var e in StateAllowlist)
        {
            Check(sections.Contains(e.Section), $"{e.Key}: section {e.Section}");
            Check(e.Note.Length > 0, $"{e.Key}: 分類表の備考が生成物に残る");
            Check(e.DeclaringType.StartsWith(e.ModelType, StringComparison.Ordinal), $"{e.Key}: 宣言型はモデルの型か、その入れ子");
            Check(ResolveAllowlistType(e.ModelType) != null && ResolveAllowlistType(e.DeclaringType) != null,
                $"{e.Key}: 試験の stub が {e.DeclaringType} を持つ");
            foreach (string forbidden in new[] { "Rng", "RelicGrabBag", "PlayerOdds", "_piles", "_runPiles", "SetupForPlayer",
                         "_ancientCard", "_serializableAncientCard", "_starterRelic", "_upgradedRelic" })
                Check(!e.Member.Contains(forbidden, StringComparison.Ordinal) && !e.DeclaringType.Contains(forbidden, StringComparison.Ordinal),
                    $"{e.Key}: 隠す情報（{forbidden}）を指さない");
        }
        // 同じ section・キーは、互いに継承関係の無い具象クラスどうしだけ（行に 2 つ当たらない）。
        foreach (var group in StateAllowlist.GroupBy(e => (e.Section, e.Key)).Where(g => g.Count() > 1))
            foreach (var a in group)
                foreach (var b in group)
                    if (!ReferenceEquals(a, b))
                        Check(!ResolveAllowlistType(a.ModelType)!.IsAssignableFrom(ResolveAllowlistType(b.ModelType)),
                            $"{group.Key}: {a.ModelType} と {b.ModelType} が同じ行に当たらない");
        Check(ResolveAllowlistType("MegaCrit.Sts2.Core.Models.Relics.NoSuchRelic") == null, "無い型は null");
    }

    private static void RunCardContract()
    {
        BeginStateWarnings();
        var player = new Player { PlayerCombatState = new PlayerCombatState() };
        var plain = new CardModel("STRIKE_IRONCLAD") { Owner = player };
        var values = Internal(Row("card", plain));
        Check(values.Count == 9, "基底の 9 項目だけ（サブクラスの項目は付かない）");
        Check(values["clone_of_uid"] == null && Equals(values["current_play_index"], 0)
            && values["current_target_combat_id"] == null && values["deck_version_id"] == null && values["floor_added_to_deck"] == null
            && Equals(values["is_dupe"], false) && Seq<string>(values["keywords_added"]) && Seq<string>(values["keywords_removed"])
            && values["transform_into_after_play_id"] == null && Reasons(values) == null,
            "既定値は実値（null は参照が無い・未設定、理由なし）");

        // 複製元: 山にあるカードは同じ読み取りの山の行と同じ uid を振る。山に無いカードには振らない。
        var original = new CardModel("BASH") { Owner = player };
        player.PlayerCombatState!.Hand.Add(original);
        var clone = new CardModel("BASH") { Owner = player };
        clone.Set(cloneOf: original, playIndex: 2, target: new Creature { CombatId = 3 }, floor: 7, dupe: true,
            transform: new CardModel("SADNESS"));
        clone.Added.UnionWith(new[] { CardKeyword.Retain, CardKeyword.Exhaust });
        clone.Removed.Add(CardKeyword.Ethereal);
        values = Internal(Row("card", clone));
        Check(Equals(values["clone_of_uid"], GetStableCardUid(original)), "複製元は山の行と同じ uid");
        Check(Equals(values["current_play_index"], 2) && Equals(values["current_target_combat_id"], 3u)
            && Equals(values["floor_added_to_deck"], 7) && Equals(values["is_dupe"], true)
            && Equals(values["transform_into_after_play_id"], "SADNESS"), "値を読む（対象は combat_id、変化先はモデルの id）");
        Check(Seq(values["keywords_added"], "Exhaust", "Retain") && Seq(values["keywords_removed"], "Ethereal"),
            "キーワードは列挙の値の順");

        var stray = new CardModel("ANGER");
        var orphan = new CardModel("ANGER") { Owner = player };
        orphan.Set(cloneOf: stray, target: new Creature());
        values = Internal(Row("card", orphan, "hand"));
        Check(values["clone_of_uid"] == null && Reasons(values)!["clone_of_uid"].Contains("戦闘の山に無い"),
            "山に無い複製元は uid を振らず null と理由");
        Check(!TryGetRegisteredCardUid(stray, out _), "山に無いカードに uid を振っていない");
        Check(values["current_target_combat_id"] == null && Reasons(values)!.ContainsKey("current_target_combat_id"),
            "combat_id の無い対象は null と理由");
        Check(Equals(values["is_dupe"], false), "ほかの項目は残る");
        Check(_stateWarnings.Any(w => w.StartsWith("hand.internal_state.clone_of_uid:")), "warnings にも出る");

        var rampage = new Rampage { Owner = player };
        rampage.SetExtra(10m);
        values = Internal(Row("card", rampage));
        Check(values.Count == 10 && Equals(values["extra_damage"], 10m), "Rampage は基底の 9 項目と extra_damage");
        Check(Equals(Internal(Row("card", new Thrash()))["extra_damage"], 0m)
            && Equals(Internal(Row("card", new Maul()))["extra_damage"], 0m)
            && Equals(Internal(Row("card", new TheBall()))["extra_damage"], 0m), "累計の 0 も実値");
        Check(Equals(Internal(Row("card", new SpoilsMap()))["spoils_act_index"], -1), "SpoilsMap の幕（未設定の -1 も実値）");
        var regret = new Regret();
        regret.SetCardsInHand(6);
        values = Internal(Row("card", regret));
        Check(values.Count == 10 && Equals(values["cards_in_hand_at_turn_end"], 6), "後悔のターン終了時の手札の枚数（解決中の印）");

        RunDeckVersionContract(player);
    }

    // CardModel._deckVersion（sts2#1133）: Player.PopulateCombatState が戦闘の札に元のデッキの札を入れ、
    // CardModel.AfterCloned（複製・生成）が null にする。値は参照先のモデルの id で、参照先に uid を振らない。
    private static void RunDeckVersionContract(Player player)
    {
        BeginStateWarnings();
        var deckRupture = new CardModel("RUPTURE");
        player.Deck.Cards.Add(deckRupture);
        var fromDeck = new CardModel("RUPTURE") { Owner = player, DeckVersion = deckRupture };
        var generated = new CardModel("RUPTURE") { Owner = player };
        player.PlayerCombatState!.Hand.Add(fromDeck);
        player.PlayerCombatState!.Hand.Add(generated);
        var deckRow = Internal(Row("card", fromDeck, "discard_pile"));
        var generatedRow = Internal(Row("card", generated, "discard_pile"));
        Check(Equals(deckRow["deck_version_id"], "RUPTURE") && Reasons(deckRow) == null,
            "デッキから写した札は元の札のモデル id");
        Check(generatedRow.ContainsKey("deck_version_id") && generatedRow["deck_version_id"] == null && Reasons(generatedRow) == null,
            "同じ id の生成札は参照なしの null（理由なし）");
        Check(!TryGetRegisteredCardUid(deckRupture, out _), "参照先のデッキの札に戦闘の uid を振らない");
        Check(Equals(Internal(Row("card", new CardModel("BASH") { DeckVersion = new CardModel("BASH") }))["deck_version_id"], "BASH"),
            "戦闘の山の外の札でも参照先の id を読む（uid を使わない）");

        // 参照先の id が読めない: null と理由・warnings、ほかの項目は残る。
        var broken = new CardModel("ANGER") { Owner = player, DeckVersion = new CardModel(null!) };
        var brokenRow = Internal(Row("card", broken, "draw_pile"));
        Check(brokenRow["deck_version_id"] == null && Reasons(brokenRow)!["deck_version_id"].Contains("Id.Entry"),
            "読めない参照は null と理由（参照なしと区別する）");
        Check(Equals(brokenRow["is_dupe"], false) && Reasons(brokenRow)!.Count == 1, "ほかの項目は残る");
        Check(_stateWarnings.Count(w => w.StartsWith("draw_pile.internal_state.deck_version_id:")) == 1, "warnings にも出る");

        // markdown: デッキ由来の全札に値があるので、読めたときは出さない（Never）。読めないときは理由を出す。
        string shown = FormatAllowlistedState(Row("card", fromDeck), "card");
        Check(!shown.Contains("deck version", StringComparison.Ordinal), "読めた deck_version_id は markdown に出さない");
        Check(FormatAllowlistedState(Row("card", broken), "card").Contains("deck version id null (", StringComparison.Ordinal),
            "読めない deck_version_id は理由を出す");
        Check(StateAllowlist.Single(e => e.Key == "deck_version_id") is { Section: "card", Member: "_deckVersion",
            Converter: StateValueConverter.CardModelId, Markdown: StateMarkdown.Never, Kind: StateMemberKind.Field },
            "CardModel._deckVersion を CardModelId で読む");
        player.PlayerCombatState!.Hand.Remove(fromDeck);
        player.PlayerCombatState!.Hand.Remove(generated);
        player.Deck.Cards.Remove(deckRupture);
    }

    private static void RunPowerContract()
    {
        BeginStateWarnings();
        var strength = new StrengthPower();
        strength.SetAmountOnTurnStart(0);
        var values = Internal(Row("power", strength));
        Check(values.Count == 3 && Equals(values["amount_on_turn_start"], 0), "全パワーに amount_on_turn_start");
        Check(values.ContainsKey("applier_combat_id") && values["applier_combat_id"] == null
            && values.ContainsKey("target_combat_id") && values["target_combat_id"] == null && Reasons(values) == null,
            "付与元・対象が無い（null・理由なし）も実値で、全パワーの行に付く");

        // 付与元（PowerModel._applier）と対象（PowerModel._target）は派生によらず全パワーの行に出す。
        // 縮小・締め付けなどは付与元の死で外れ（ShrinkPower.AfterDeath・ConstrictPower.AfterDeath）、
        // 泥棒・砂地獄などは対象のプレイヤーに効く（ThieveryPower.Steal・SandpitPower）。
        var shrink = new StrengthPower();
        shrink.SetApplier(new Creature { CombatId = 0 });
        shrink.SetTarget(new Creature { CombatId = 2 });
        values = Internal(Row("power", shrink));
        Check(Equals(values["applier_combat_id"], 0u) && Equals(values["target_combat_id"], 2u), "combat_id（0 も実在する id）");
        var thief = new ThieveryPower();
        thief.SetTarget(new Creature { CombatId = 1 });
        values = Internal(Row("power", thief));
        Check(Equals(values["target_combat_id"], 1u) && values["applier_combat_id"] == null, "派生の行にも基底の項目が付く");
        var idless = new StrengthPower();
        idless.SetApplier(new Creature());
        values = Internal(Row("power", idless, "status.STRENGTH_POWER"));
        Check(values["applier_combat_id"] == null && Reasons(values)!["applier_combat_id"].Contains("CombatId")
            && _stateWarnings.Any(w => w.StartsWith("status.STRENGTH_POWER.internal_state.applier_combat_id:"))
            && Equals(values["amount_on_turn_start"], 0), "CombatId の無い付与元は null・理由・warnings（ほかの id で埋めない）");

        // 呪術: engine（tools/turnplan/hexpower.py）が読む行の直下の applier_combat_id・applier_readable は、
        // 許可リストの値を写す（AttachHexApplierCompat → CopyAllowlistedReference。読み直さない）。
        BeginStateWarnings();
        var hex = new HexPower();
        hex.SetApplier(new Creature { CombatId = 3 });
        var hexRow = Row("power", hex, "status.HEX_POWER");
        CopyAllowlistedReference(hexRow, "applier_combat_id", "applier_readable", "status.HEX_POWER");
        Check(Equals(hexRow["applier_combat_id"], 3u) && hexRow["applier_readable"] is true && !hexRow.ContainsKey("unavailable_reasons")
            && Equals(Internal(hexRow)["applier_combat_id"], 3u), "呪術の行の直下にも同じ値");
        hex.SetApplier(null);
        hexRow = Row("power", hex, "status.HEX_POWER");
        CopyAllowlistedReference(hexRow, "applier_combat_id", "applier_readable", "status.HEX_POWER");
        Check(hexRow["applier_combat_id"] == null && hexRow["applier_readable"] is true && !hexRow.ContainsKey("unavailable_reasons")
            && _stateWarnings.Count == 0, "付与元なしは readable=true と null（理由なし）");
        hex.SetApplier(new Creature());
        hexRow = Row("power", hex, "status.HEX_POWER");
        CopyAllowlistedReference(hexRow, "applier_combat_id", "applier_readable", "status.HEX_POWER");
        var rowReasons = (Dictionary<string, string>)hexRow["unavailable_reasons"]!;
        Check(hexRow["applier_combat_id"] == null && hexRow["applier_readable"] is false
            && rowReasons["applier_combat_id"] == Reasons(Internal(hexRow))!["applier_combat_id"]
            && _stateWarnings.Count(w => w.Contains("applier_combat_id")) == 1,
            "読めなかった付与元は readable=false・null・同じ理由（warnings は許可リストの 1 件だけ）");

        var intercept = new InterceptPower();
        intercept.SetData(intercept.NewData(new Creature { CombatId = 5 }, new Creature { CombatId = 2 }));
        values = Internal(Row("power", intercept));
        Check(Seq(values["covered_creature_ids"], 2u, 5u), "かばっている味方は combat_id の昇順");

        intercept.SetData(null);
        values = Internal(Row("power", intercept, "status.INTERCEPT_POWER"));
        Check(values["covered_creature_ids"] == null && Reasons(values)!.ContainsKey("covered_creature_ids")
            && Equals(values["amount_on_turn_start"], 0), "Data が無ければ null と理由、ほかの項目は残る");
        intercept.SetData(new object());
        values = Internal(Row("power", intercept));
        Check(values["covered_creature_ids"] == null && Reasons(values)!["covered_creature_ids"].Contains("Expected"),
            "Data の型が違えば読まない");
    }

    private static void RunRelicContract()
    {
        BeginStateWarnings();
        var anchor = new Anchor();
        anchor.SetBase(RelicStatus.Active, wax: true, floor: 4);
        var values = Internal(Row("relic", anchor));
        Check(values.Count == 3 && Equals(values["status"], "Active") && Equals(values["is_wax"], true)
            && Equals(values["floor_added_to_deck"], 4), "基底の 3 項目だけ");

        var player = new Player { PlayerCombatState = new PlayerCombatState() };
        var played = new CardModel("BODY_SLAM") { Owner = player };
        player.PlayerCombatState!.Hand.Add(played);
        var legion = new PaelsLegion();
        legion.Set(new CardPlay { Card = played });
        values = Internal(Row("relic", legion));
        Check(Equals(values["affected_card_uid"], GetStableCardUid(played)), "CardPlay はカードの uid にする");
        legion.Set(null);
        Check(Internal(Row("relic", legion))["affected_card_uid"] == null, "解決の途中でなければ null（理由なし）");

        var tooth = new PaelsTooth();
        Check(Internal(Row("relic", tooth))["stored_cards"] is List<Dictionary<string, object?>> { Count: 0 },
            "空の歯は保管札 0 件");
        tooth.Cards.Add(new SerializableCard { Id = new ModelId("STRIKE_IRONCLAD"), CurrentUpgradeLevel = 1 });
        tooth.Cards.Add(new SerializableCard { Id = new ModelId("BASH"), Enchantment = new SerializableEnchantment { Id = new ModelId("GLAM") } });
        tooth.Cards.Add(new SerializableCard { Id = new ModelId("STRIKE_IRONCLAD"), CurrentUpgradeLevel = 1 });
        for (int i = 0; i < 2; i++)
        {
            var stored = (List<Dictionary<string, object?>>)Internal(Row("relic", tooth))["stored_cards"]!;
            Check(stored.Count == 3 && Equals(stored[0]["id"], "BASH") && Equals(stored[0]["enchantment_id"], "GLAM")
                && Equals(stored[1]["upgrade_level"], 1) && stored[1].SequenceEqual(stored[2]),
                "預けたカードを整列し、同一札の多重度を保つ");
        }
        Check(tooth.Cards.Count == 3 && tooth.Cards[0].Id?.Entry == "STRIKE_IRONCLAD", "読み取りで保管列を変更しない");
        tooth.SetUnreadable();
        values = Internal(Row("relic", tooth, "player.relics.PAELS_TOOTH"));
        Check(values["stored_cards"] == null && Reasons(values)!.ContainsKey("stored_cards")
            && _stateWarnings.Any(w => w.StartsWith("player.relics.PAELS_TOOTH.internal_state.stored_cards:")),
            "異常な null の保管列は null・理由・警告");

        var lamp = new UnsettlingLamp();
        values = Internal(Row("relic", lamp));
        Check(values["triggering_card_uid"] == null && values["doubled_power_ids"] is List<Dictionary<string, object?>> { Count: 0 },
            "未作成のリストは空");
        var weak = new StrengthPower { Owner = new Creature { CombatId = 9 } };
        var other = new InterceptPower { Owner = new Creature { CombatId = 1 } };
        lamp.Set(played, [weak, other]);
        var doubled = (List<Dictionary<string, object?>>)Internal(Row("relic", lamp))["doubled_power_ids"]!;
        Check(doubled.Count == 2 && Equals(doubled[0]["id"], "INTERCEPT_POWER") && Equals(doubled[1]["owner_combat_id"], 9u),
            "2 倍にしたパワーは id と持ち主の combat_id");

        var nib = new PenNib();
        nib.Set(new CardModel("STRIKE_IRONCLAD"));
        values = Internal(Row("relic", nib, "player.relics.PEN_NIB"));
        Check(values["attack_to_double_uid"] == null && Reasons(values)!.ContainsKey("attack_to_double_uid"),
            "持ち主の山に無いカードは null と理由");
        Check(Internal(Row("relic", new GoldenCompass())).ContainsKey("golden_path_act")
            && Internal(Row("relic", new MusicBox())).ContainsKey("card_being_played_uid")
            && Internal(Row("relic", new Vambrace())).ContainsKey("triggering_card_uid"), "各レリックの項目");
        Check(Internal(Row("relic", new Byrdpip())).Count == 3, "許可リストに無いレリックは基底の項目だけ");
    }

    private static void RunModifierContract()
    {
        BeginStateWarnings();
        var run = new MegaCrit.Sts2.Core.Runs.RunState { TotalFloor = 5 };
        var owner = new Player();
        run.Players.Add(owner);
        Check(!BuildRunState(run).ContainsKey("internal_state"), "歯がなければ run の項目を省く");
        owner.Relics.Add(new PaelsTooth { IsMelted = true });
        Check(Seq<string>(Internal(BuildRunState(run))["modifier_ids"]), "歯があり修飾子がなければ空列");
        var ordered = new ModifierModel[] { new("HOARDER"), new("OTHER"), new("HOARDER") };
        run.Modifiers = ordered;
        for (int i = 0; i < 2; i++)
            Check(Seq(Internal(BuildRunState(run))["modifier_ids"], "HOARDER", "OTHER", "HOARDER"),
                "修飾子は適用順と重複を保持する");
        Check(ReferenceEquals(run.Modifiers, ordered) && ordered[0].Id.Entry == "HOARDER", "読み取りで修飾子の実体を変更しない");
        var manager = MegaCrit.Sts2.Core.Runs.RunManager.Instance;
        manager.State = run;
        Check(Seq(Internal((Dictionary<string, object?>)WithRelevantFtueRunState(new())["run"]!)["modifier_ids"],
            "HOARDER", "OTHER", "HOARDER"), "FTUE の早期 return にもランの修飾子を付ける");
        manager.State = null;
        Check(!WithRelevantFtueRunState(new()).ContainsKey("run"), "ランのない FTUE に run を作らない");
        run.Modifiers = null;
        var values = Internal(BuildRunState(run));
        Check(values["modifier_ids"] == null && Reasons(values)!.ContainsKey("modifier_ids")
            && _stateWarnings.Any(w => w.StartsWith("run.internal_state.modifier_ids:")), "修飾子の欠測は null・理由・警告");
    }

    private static void RunPlayerBattleContract()
    {
        BeginStateWarnings();
        var extra = new ExtraPlayerFields { CardShopRemovalsUsed = 2, WongoPoints = 7 };
        var values = Internal(Row("player", extra));
        Check(values.Count == 1 && Equals(values["card_shop_removals_used"], 2), "自動実装プロパティを backing field から読む（wongo_points は出さない）");
        var computed = new StateAllowlistEntry("player", "MegaCrit.Sts2.Core.Entities.Players.ExtraPlayerFields",
            "MegaCrit.Sts2.Core.Entities.Players.ExtraPlayerFields", "Computed", StateMemberKind.AutoProperty, "computed",
            StateValueConverter.Int, StateMarkdown.NonDefault, "test");
        var guard = new Dictionary<string, object?>();
        ReadInternalStateValue(guard, "computed", () => ReadAllowlistedValue(computed, extra), "guard");
        Check(guard["computed"] == null && Reasons(guard)!["computed"].Contains("<Computed>k__BackingField"),
            "backing field の無いプロパティは getter を呼ばず null と理由");

        var combined = new Dictionary<string, object?> { ["internal_state"] = guard };
        AttachAllowlistedState(combined, "player", extra, "player");
        values = Internal(combined);
        Check(Equals(values["card_shop_removals_used"], 2) && values.ContainsKey("computed") &&
            Reasons(values)!.ContainsKey("computed"), "許可リストの追記で既存の値と理由を保持する");

        var combat = new CombatState();
        combat.SetAllies(new Creature { CombatId = 4 }, new Creature { CombatId = 1 });
        values = Internal(Row("battle", combat));
        Check(Seq(values["ally_combat_ids"], 1u, 4u) && Seq<uint>(values["escaped_creature_ids"]), "味方と逃げた敵の combat_id");
        Check(!Row("battle", new NullCombatState()).ContainsKey("internal_state"), "当てはまる項目が無ければ internal_state を付けない");
        Check(!Row("player", null).ContainsKey("internal_state"), "モデルが無ければ付けない");
    }

    private static void RunPlayerCombatIdContract()
    {
        const string key = "combat_id";
        var manager = CombatManager.Instance;
        var combat = new CombatState();
        var own = new Creature { CombatState = combat, CombatId = 0 };
        var player = new Player(own) { PlayerCombatState = new() };
        own.Player = player;
        player.ExtraFields.CardShopRemovalsUsed = 2;
        var pet = new Creature { CombatState = combat, CombatId = 2 };
        var teammate = new Creature { CombatState = combat, CombatId = 9 };
        var other = new Player(teammate);
        teammate.Player = other;
        manager.State = combat;
        manager.IsInProgress = true;

        Dictionary<string, object?> Snapshot(Player? local = null)
        {
            BeginStateWarnings();
            var row = new Dictionary<string, object?>();
            AttachPlayerAllowlistedState(row, local ?? player);
            return row;
        }

        void CheckFailure(string reason, string message, Player? local = null)
        {
            var values = Internal(Snapshot(local));
            Check(values.ContainsKey(key) && values[key] == null &&
                Reasons(values)?.GetValueOrDefault(key)?.Contains(reason) == true &&
                _stateWarnings.Any(w => w.StartsWith("player.internal_state.combat_id:")), message);
        }

        combat.SetAllies(own);
        var values = Internal(Snapshot());
        Check(!values.ContainsKey(key) && Equals(values["card_shop_removals_used"], 2) &&
            Reasons(values) == null && _stateWarnings.Count == 0, "味方が本人 1 体なら combat_id は省略");

        combat.SetAllies(own, pet);
        var row = Snapshot();
        values = Internal(row);
        Check(Equals(values[key], 0u) && Equals(values["card_shop_removals_used"], 2) &&
            Reasons(values) == null && _stateWarnings.Count == 0, "ペットと同居する本人の ID 0 と除去回数の両方を出す");
        Check(FormatAllowlistedState(row, "player").Contains("combat id 0"), "Markdown でも ID 0 を表示する");

        own.CombatId = 8;
        combat.SetAllies(pet, own);
        Check(Equals(Internal(Snapshot())[key], 8u), "先頭のペットより大きい本人 ID を出す");
        combat.SetAllies(teammate, own);
        Check(Equals(Internal(Snapshot())[key], 8u), "ペットがいない協力戦でもローカル本人の ID を出す");
        combat.SetAllies(teammate, pet);
        pet.CombatId = own.CombatId;
        CheckFailure("Allies", "同じ ID の別個体がいても、本人が味方にいなければ欠測");
        pet.CombatId = 2;

        combat.SetAllies(own, pet);
        own.Player = other;
        CheckFailure("所有者", "味方にいる個体でも所有者が別プレイヤーなら欠測");
        own.Player = player;
        own.CombatState = new CombatState();
        CheckFailure("CombatState", "味方一覧に見えても別戦闘の参照なら欠測");
        own.CombatState = combat;
        CheckFailure("Player.Creature", "本人の参照がなければ欠測", new Player());

        own.CombatId = null;
        CheckFailure("CombatId", "本人の ID 未割当は理由付きの null");
        Check(Equals(Internal(Snapshot())["card_shop_removals_used"], 2), "ID の欠測時にも除去回数を保持");
        own.CombatId = uint.MaxValue;
        pet.CombatId = null;
        Check(Equals(Internal(Snapshot())[key], uint.MaxValue), "味方の数は ID の割当によらず、本人 ID は uint");

        player.ExtraFields = null!;
        values = Internal(Snapshot());
        Check(Equals(values[key], uint.MaxValue) && values["card_shop_removals_used"] == null &&
            Reasons(values)?.ContainsKey("card_shop_removals_used") == true &&
            _stateWarnings.Any(w => w.StartsWith("player.internal_state.card_shop_removals_used:")),
            "除去回数が欠測でも本人 ID を保持");
        manager.State = null;
        CheckFailure("現在の CombatState", "進行中に現在の味方数を確認できなければ欠測");
        Check(Reasons(Internal(Snapshot()))?.Count == 2, "両方の欠測理由を同じ辞書に保持");

        player.ExtraFields = new() { CardShopRemovalsUsed = 2 };
        manager.State = combat;
        manager.IsInProgress = false;
        values = Internal(Snapshot());
        Check(!values.ContainsKey(key) && Equals(values["card_shop_removals_used"], 2) &&
            Reasons(values) == null && _stateWarnings.Count == 0, "戦闘外では残存する戦闘状態・本人 ID を公開しない");
        manager.State = null;
    }

    private static void RunReaderGuardContract()
    {
        BeginStateWarnings();
        var card = new CardModel("STRIKE_IRONCLAD");
        var values = new Dictionary<string, object?>();
        var wrongType = new StateAllowlistEntry("card", "MegaCrit.Sts2.Core.Models.CardModel", "MegaCrit.Sts2.Core.Models.CardModel",
            "_isDupe", StateMemberKind.Field, "is_dupe", StateValueConverter.Int, StateMarkdown.NonDefault, "test");
        ReadInternalStateValue(values, "is_dupe", () => ReadAllowlistedValue(wrongType, card), "guard");
        Check(values["is_dupe"] == null && Reasons(values)!["is_dupe"].Contains("Boolean"), "宣言の型が変換と違えば読まない");
        var missing = wrongType with { Member = "_renamed", Converter = StateValueConverter.Bool };
        ReadInternalStateValue(values, "renamed", () => ReadAllowlistedValue(missing, card), "guard");
        Check(values["renamed"] == null && Reasons(values)!["renamed"].Contains("MissingFieldException"), "無いフィールドは null と理由");
        var inherited = wrongType with { DeclaringType = "MegaCrit.Sts2.Core.Models.Cards.Rampage", Member = "_isDupe", Converter = StateValueConverter.Bool };
        ReadInternalStateValue(values, "inherited", () => ReadAllowlistedValue(inherited, new Rampage()), "guard");
        Check(values["inherited"] == null, "宣言した型でしか探さない（DeclaredOnly）");
    }

    private static void RunMarkdownContract()
    {
        BeginStateWarnings();
        var player = new Player { PlayerCombatState = new PlayerCombatState() };
        var plain = Row("card", new CardModel("STRIKE_IRONCLAD") { Owner = player });
        Check(FormatAllowlistedState(plain, "card") == "", "既定値だけなら何も出さない");
        var rampage = new Rampage { Owner = player };
        rampage.SetExtra(5m);
        rampage.Set(floor: 3, dupe: true);
        Check(FormatAllowlistedState(Row("card", rampage), "card") == " (internal: is dupe yes, extra damage 5)",
            "既定値以外だけ（floor_added_to_deck は出さない）");
        var orphan = new CardModel("ANGER") { Owner = player };
        orphan.Set(cloneOf: new CardModel("ANGER"));
        Check(FormatAllowlistedState(Row("card", orphan), "card").Contains("clone of uid null ("), "読めない値は理由を出す");
        Check(FormatAllowlistedParts(Row("card", orphan), "card").EndsWith("（uid を新たに振らない）)", StringComparison.Ordinal),
            "カードの詳細の行は理由の括弧を削らない");

        var power = new StrengthPower();
        power.SetAmountOnTurnStart(1);
        Check(FormatAllowlistedState(Row("power", power), "power") == "", "amount と同じなら出さない");
        power.SetAmountOnTurnStart(0);
        Check(FormatAllowlistedState(Row("power", power), "power") == " (internal: amount on turn start 0)", "amount と違えば 0 でも出す");
        power.SetApplier(new Creature { CombatId = 4 });
        power.SetTarget(new Creature { CombatId = 1 });
        Check(FormatAllowlistedState(Row("power", power), "power") == " (internal: amount on turn start 0)",
            "付与元・対象は JSON だけ（ほぼ全行に付くので markdown に出さない）");
        power.SetApplier(new Creature());
        Check(FormatAllowlistedState(Row("power", power), "power").Contains("applier combat id null ("),
            "読めなかった付与元は markdown にも理由を出す");

        var relic = new Anchor();
        relic.SetBase(RelicStatus.Normal, wax: false, floor: 9);
        Check(FormatAllowlistedState(Row("relic", relic), "relic") == "", "Normal・false・階は出さない");
        relic.SetBase(RelicStatus.Active, wax: true, floor: 9);
        Check(FormatAllowlistedState(Row("relic", relic), "relic") == " (internal: is wax yes, status Active)", "Active と蝋は出す");
        var compass = new GoldenCompass();
        compass.SetAct(0);
        Check(FormatAllowlistedState(Row("relic", compass), "relic") == " (internal: golden path act 0)", "第 1 幕（0）も出す");
        compass.SetAct(-1);
        Check(FormatAllowlistedState(Row("relic", compass), "relic") == " (internal: golden path act -1)", "未設定の -1 も出す");
        Check(FormatAllowlistedState(Row("card", new SpoilsMap { Owner = player }), "card") == " (internal: spoils act index -1)",
            "宝の地図の幕は常に出す");
        var combat = new CombatState();
        combat.SetAllies(new Creature { CombatId = 1 });
        Check(FormatAllowlistedState(Row("battle", combat), "battle") == " (internal: ally combat ids [1])", "リストは角括弧");
    }

    // 本番の組み立てが各行に internal_state を付けているか（ソースの検査。ゲーム無しでは組み立てを動かせない）。
    private static void RunWiringContract()
    {
        string builder = Source("McpMod.StateBuilder.cs");
        string multiplayer = Source("McpMod.MultiplayerState.cs");
        foreach (var (source, local) in new[] { (builder, "_player"), (multiplayer, "localPlayer") })
        {
            int common = source.IndexOf("result[\"run\"] = BuildRunState(runState);", StringComparison.Ordinal);
            Check(new[] { "rewards", "card_select", "map" }.All(screen =>
            {
                int branch = source.IndexOf($"result[\"state_type\"] = \"{screen}\";", StringComparison.Ordinal);
                return branch >= 0 && branch < common;
            }),
                "報酬・選択・通常のラン画面は共通の run ビルダーを通る");
            int selection = source.IndexOf("if (ShouldAttachSelectionBattle(", StringComparison.Ordinal);
            int player = source.IndexOf($"result[\"player\"] = BuildPlayerState({local}, screen);", StringComparison.Ordinal);
            Check(selection >= 0 && selection < common && common < player,
                "通常の戦闘と戦闘中の選択は、画面分岐の後で同じローカル player ビルダーを通る");
        }
        foreach (string call in new[]
                 {
                     "AttachAllowlistedState(battle, \"battle\", combatState, \"battle\");",
                     "AttachAllowlistedState(relicState, \"relic\", relic, $\"player.relics.{relic.Id.Entry}\");",
                     "AttachAllowlistedState(cardInfo, \"card\", card, \"deck\");",
                     "AttachAllowlistedState(state, \"card\", card, \"hand\");",
                     "AttachAllowlistedState(state, \"card\", card, pile.ToString().ToLowerInvariant() + \"_pile\");"
                 })
            Check(builder.Contains(call), "組み立てが呼ぶ: " + call);
        int playerBuilder = builder.IndexOf("private static Dictionary<string, object?> BuildPlayerState(", StringComparison.Ordinal);
        int playerBuilderEnd = builder.IndexOf("return state;", playerBuilder, StringComparison.Ordinal);
        Check(builder[playerBuilder..playerBuilderEnd].Contains("AttachPlayerAllowlistedState(state, player);"),
            "共通のローカル player ビルダーで、渡された Player に許可リストの項目を付ける");
        Check(builder.Split("AttachAllowlistedState(entry, \"power\", power, $\"status.{entry[\"id\"]}\");").Length == 3,
            "パワーの行の 2 経路（通常・説明を読めないとき）とも");
        Check(System.Text.RegularExpressions.Regex.Matches(builder,
                @"AttachAllowlistedState\(entry, ""power"", power, \$""status\.\{entry\[""id""\]\}""\);\s*AttachHexApplierCompat\(entry, power\);").Count == 2,
            "呪術の互換の写しは、2 経路とも許可リストの後");
        string power = Source("McpMod.PowerInternalState.cs");
        Check(!power.Contains("case HexPower") && !power.Contains("power.Applier"), "パワーの個別の読み取りは付与元を読まない（許可リストの 1 回だけ）");
        string formatting = Source("McpMod.Formatting.cs");
        Check(formatting.Split("FormatAllowlistedState(r, \"relic\")").Length == 3
            && formatting.Split("FormatAllowlistedState(p, \"power\")").Length == 4
            && formatting.Contains("FormatAllowlistedState(battle, \"battle\")")
            && formatting.Split("\"player\")}").Length == 3, "markdown のレリック・パワー・戦闘・プレイヤーの行");
        Check(Source("McpMod.CardDetailsFormatting.cs").Contains("sb.AppendLine(\"  - internal: \" + internalState);")
            && Source("McpMod.CardDetailsFormatting.cs").Contains("FormatAllowlistedParts(card, \"card\")"), "カードの markdown");
    }

    // 生成物が分類表から生成し直した内容と一致するか（PR #108 のレビュー P3-6）。tools/state_inventory.py --check-allowlist を
    // そのまま回す。Python は STS2_PYTHON（無ければ PATH の python3）。標準ライブラリだけで 3.9 以降なら動く。
    private static void RunGeneratedIsCurrentContract()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "tools", "state_inventory.py")))
            root = root.Parent;
        Check(root != null, "リポジトリのルート（tools/state_inventory.py）が見つかる");
        string python = Environment.GetEnvironmentVariable("STS2_PYTHON") is { Length: > 0 } configured ? configured : "python3";
        var start = new System.Diagnostics.ProcessStartInfo(python)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = root!.FullName
        };
        foreach (string arg in new[] { "-B", "tools/state_inventory.py", "--mod", ".", "--check-allowlist" })
            start.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        Check(process.ExitCode == 0, "McpMod.StateAllowlist.Generated.cs が分類表と一致する（--write-allowlist で作り直す）: " + output.Trim());
    }

    public static int Main()
    {
        RunGeneratedIsCurrentContract();
        RunGeneratedTableContract();
        RunCardContract();
        RunPowerContract();
        RunRelicContract();
        RunModifierContract();
        RunPlayerBattleContract();
        RunPlayerCombatIdContract();
        RunReaderGuardContract();
        RunMarkdownContract();
        RunWiringContract();
        Console.WriteLine($"StateAllowlistContract: {Checks} checks passed");
        return 0;
    }
}
