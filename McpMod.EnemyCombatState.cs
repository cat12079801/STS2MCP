using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Runs;

namespace STS2_MCP;

public static partial class McpMod
{
    // 許可リスト。v0.114.0（sts2-ae309f85）の宣言型・型を検査した private フィールドと、
    // 副作用の無い getter（下記）だけを読む。move の実行・初期化・乱数・履歴からの再構成はしない。
    // 対象外の敵は null（combat_state を省略）。
    // 載せるのは、敵の行動の分岐・効果量に効く現在値のうち、ほかの state（HP・status・move_state・
    // battle.enemy_move_context）に出ないもの（sts2#1018 の抽出器の依存の対応表）。表示・効果音だけに使う
    // フィールド（LouseProgenitor._curled など）と、初期地点の選択だけに使うフィールド
    // （move_state.initial_state_id で読める Chomper._screamFirst など）は載せない。
    // 台詞だけを分ける印（GremlinMerc._hasSpoken・KinPriest._speechUsed）は、行動の本体の条件分岐が読むので載せる
    // （sts2#1065。生成表の条件を未観測のまま残さない）。
    private static Dictionary<string, object?>? BuildEnemyCombatState(MonsterModel? monster, string entityId)
    {
        var state = new Dictionary<string, object?>();
        string context = $"battle.enemies.{entityId}.combat_state";
        void Read(string key, Func<object?> read) => ReadInternalStateValue(state, key, read, context);
        void Field<T>(string key, MonsterModel model, Type declaring, string field)
            => Read(key, () => ReadDeclaredStateField<T>(model, declaring, field));
        switch (monster)
        {
            case WaterfallGiant giant:
                // TriggerAboutToBlowState（SteamEruptionPower.AfterDeath から）で true。false も実値。
                Field<bool>("is_about_to_blow", giant, typeof(WaterfallGiant), "_isAboutToBlow");
                // AboutToBlowMove が SteamEruptionPower の量を写し、ExplodeMove の攻撃に使う。写す前は 0 の実値。
                Field<int>("steam_eruption_damage", giant, typeof(WaterfallGiant), "_steamEruptionDamage");
                // AfterAddedToRoom が BasePressureGunDamage を入れ、PressureGunMove が攻撃の後に PressureGunIncrease（5）を足す。
                Read("current_pressure_gun_damage", () => RequireNonNegative(
                    ReadDeclaredStateField<int>(giant, typeof(WaterfallGiant), "_currentPressureGunDamage"), "_currentPressureGunDamage"));
                // 以下は getter の現在値（AscensionHelper.GetValueIfAscension / IsDread → RunManager の読み取りだけ）。
                // RunManager.IsInProgress が false だと getter は Ascension・Dread を「無し」として返すので、そのときは読まない。
                Read("steam_boost_amount", () => ReadGiantGetter(giant, "SteamBoostAmount"));
                Read("pressurize_amount", () => ReadGiantGetter(giant, "PressurizeAmount"));
                Read("stomp_damage", () => ReadGiantGetter(giant, "StompDamage"));
                Read("ram_damage", () => ReadGiantGetter(giant, "RamDamage"));
                Read("pressure_up_damage", () => ReadGiantGetter(giant, "PressureUpDamage"));
                // SiphonMove はこれにプレイヤー人数を掛けて回復する。ここは掛ける前の 1 人分。
                Read("siphon_heal", () => ReadGiantGetter(giant, "SiphonHeal"));
                break;
            case Aeonglass aeonglass:
                // IncreasingIntensityMove の筋力は IncreasingIntensityBaseStrength + AdditionalStrength で、実行のたびに +1。
                Field<int>("additional_strength", aeonglass, typeof(Aeonglass), "_additionalStrength");
                // IncreasingIntensityMove のたびに +1。AfterCardGeneratedForCombat が生成した Wither をこの回数だけ FakeUpgrade する。
                Field<int>("wither_upgrade_count", aeonglass, typeof(Aeonglass), "_witherUpgradeCount");
                break;
            case Axebot axebot:
                // public getter StockAmount（_stockOverrideAmount ?? 2）。BootUpMove の筋力は BootUpStrGain × (2 - StockAmount)。
                Read("stock_amount", () => ReadDeclaredStateProperty<int>(axebot, typeof(Axebot), "StockAmount"));
                break;
            case BowlbugRock rock:
                // POST_HEADBUTT の条件分岐と、HeadbuttMove の攻撃の後のスタン（DizzyMove が false に戻す）。
                Field<bool>("is_off_balance", rock, typeof(BowlbugRock), "_isOffBalance");
                break;
            case Fabricator fabricator:
                AttachFabricatorLastSpawned(state, fabricator, context);
                break;
            case FrogKnight knight:
                // 条件分岐 HasBeetleCharged || HP >= MaxHp / 2。BeetleChargeMove が true にする。
                Field<bool>("has_beetle_charged", knight, typeof(FrogKnight), "_hasBeetleCharged");
                break;
            case GremlinMerc merc:
                // GimmeMove の条件分岐（!HasSpoken && GameMode != Blitz / == Blitz）。真の側は true にして台詞（TalkCmd）と
                // 待ちだけを行い、攻撃・Steal・行動の遷移は変えない。初回の GIMME_MOVE の後に true。
                Field<bool>("has_spoken", merc, typeof(GremlinMerc), "_hasSpoken");
                break;
            case KinPriest priest:
                // RitualMove の条件分岐（!SpeechUsed）。真の側は true にして台詞（TalkCmd）だけを行い、筋力の付与は変えない。
                Field<bool>("speech_used", priest, typeof(KinPriest), "_speechUsed");
                break;
            case KnowledgeDemon demon:
                // 条件分岐（< 3 なら CURSE_OF_KNOWLEDGE）と、ChooseCurse の呪いの組・DisintegrationDamageValues の添字。
                Field<int>("curse_of_knowledge_counter", demon, typeof(KnowledgeDemon), "_curseOfKnowledgeCounter");
                break;
            case LivingFog fog:
                // BloatMove が GasBomb を足す回数（空きスロットがある限り）。
                Field<int>("bloat_amount", fog, typeof(LivingFog), "_bloatAmount");
                break;
            case Nibbit nibbit:
                // 初期編成で入る値（INIT_MOVE の条件分岐が IsAlone / IsFront を読む）。生存数から遡って決めない。
                Field<bool>("is_alone", nibbit, typeof(Nibbit), "_isAlone");
                Field<bool>("is_front", nibbit, typeof(Nibbit), "_isFront");
                break;
            case Queen queen:
                // 条件分岐（BURN_BRIGHT_FOR_ME か ENRAGED 側か）。TorchHeadAmalgam の AfterDeath で true。
                Field<bool>("has_amalgam_died", queen, typeof(Queen), "_hasAmalgamDied");
                break;
            case TestSubject subject:
                // 条件分岐 Respawns < 2 と RespawnMove の段階。
                Field<int>("respawns", subject, typeof(TestSubject), "_respawns");
                // MULTI_CLAW の回数は BaseMultiClawCount + ExtraMultiClawCount で、MultiClawMove のたびに +1。
                Field<int>("extra_multi_claw_count", subject, typeof(TestSubject), "_extraMultiClawCount");
                break;
            case Toadpole toadpole:
                // 初期編成で入る値（INIT_MOVE の条件分岐が IsFront を読む）。
                Field<bool>("is_front", toadpole, typeof(Toadpole), "_isFront");
                break;
            case TwoTailedRat rat:
                // CanSummon（> 0 なら召喚不可）。SCRATCH・DISEASE_BITE・SCREECH のたびに -1。負の値も実値。
                Field<int>("turns_until_summonable", rat, typeof(TwoTailedRat), "_turnsUntilSummonable");
                // CanSummon（>= 3 なら召喚不可）。CallForBackup が全ラットに最大値 + 1 を入れる。
                Field<int>("call_for_backup_count", rat, typeof(TwoTailedRat), "_callForBackupCount");
                break;
            default:
                return null;
        }
        return state;
    }

    // Fabricator.SpawnBot は options から _lastSpawned と同じモデルを除いて抽選し、選んだモデルを _lastSpawned に入れる。
    // 宣言フィールド（MonsterModel?）の読取に成功して null なら「まだ召喚なし」（last_spawned_readable=true・id null）。
    // 読取失敗は last_spawned_readable=false・null・理由。同種の別個体や召喚履歴から補わない。
    private static void AttachFabricatorLastSpawned(Dictionary<string, object?> state, Fabricator fabricator, string context)
    {
        state["last_spawned_readable"] = false;
        ReadInternalStateValue(state, "last_spawned_id", () =>
        {
            var field = typeof(Fabricator).GetField("_lastSpawned",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)
                ?? throw new MissingFieldException(nameof(Fabricator), "_lastSpawned");
            if (field.FieldType != typeof(MonsterModel))
                throw new InvalidOperationException("Expected MonsterModel field Fabricator._lastSpawned");
            string? id = field.GetValue(fabricator) is MonsterModel last
                ? last.Id.Entry ?? throw new InvalidOperationException("Fabricator._lastSpawned.Id.Entry が null です")
                : null;
            state["last_spawned_readable"] = true;
            return id;
        }, context);
    }

    private static int ReadGiantGetter(WaterfallGiant giant, string name)
    {
        if (!RunManager.Instance.IsInProgress)
            throw new InvalidOperationException($"RunManager is not in progress; WaterfallGiant.{name} would ignore ascension and game mode");
        return RequireNonNegative(ReadDeclaredStateProperty<int>(giant, typeof(WaterfallGiant), name), name);
    }

    private static int RequireNonNegative(int value, string name)
        => value >= 0 ? value : throw new InvalidOperationException($"Expected non-negative WaterfallGiant.{name}, got {value}");

    private static string FormatEnemyCombatState(Dictionary<string, object?> enemy)
    {
        if (enemy.GetValueOrDefault("combat_state") is not Dictionary<string, object?> state || state.Count == 0)
            return "";
        var reasons = state.GetValueOrDefault("unavailable_reasons") as Dictionary<string, string>;
        string Value(string key, object? value)
        {
            if (value is bool flag) return flag ? "yes" : "no";
            if (value != null) return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "?";
            if (reasons?.GetValueOrDefault(key) is { } reason) return $"null ({reason})";
            // 読取に成功した null（ファブリケーターのまだ召喚なし）。
            return key == "last_spawned_id" && state.GetValueOrDefault("last_spawned_readable") is true ? "none" : "null (unavailable)";
        }
        return "**Combat state:** " + string.Join(" | ", state
            .Where(entry => entry.Key is not ("unavailable_reasons" or "last_spawned_readable"))
            .Select(entry => $"{entry.Key.Replace('_', ' ')}: {Value(entry.Key, entry.Value)}"));
    }
}
