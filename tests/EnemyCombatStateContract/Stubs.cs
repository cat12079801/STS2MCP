// v0.113.1 の WaterfallGiant から、MOD が読む private フィールド・getter の名前・型だけを写す。
// move の実行・TriggerAboutToBlowState・乱数・Ascension の判定は用意しない。値は試験が直接設定する。
namespace MegaCrit.Sts2.Core.Models
{
    public sealed class StubModelId
    {
        public string? Entry { get; set; }
    }

    public class MonsterModel
    {
        public StubModelId Id { get; set; } = new();
    }

    // EncounterModel.Slots は virtual getter（TheKinBoss は IsDread を読む）。試験は値だけを置く。
    public class EncounterModel
    {
        public StubModelId Id { get; set; } = new();
        public List<string>? SlotValues { get; set; } = new();
        public bool ThrowSlots { get; set; }
        public IReadOnlyList<string> Slots => ThrowSlots ? throw new InvalidOperationException("Slots 読取失敗") : SlotValues!;
    }
}

namespace MegaCrit.Sts2.Core.Entities.Ascension
{
    // v0.113.1 の AscensionLevel の並び（値は本体と同じ）。
    public enum AscensionLevel
    {
        None, SwarmingElites, WearyTraveler, Poverty, TightBelt, AscendersBane, Inflation, Scarcity,
        ToughEnemies, DeadlyEnemies, DoubleBoss
    }
}

namespace MegaCrit.Sts2.Core.Entities.Creatures
{
    public class Creature
    {
        public string EntityIdValue { get; set; } = "?";
        public string? SlotValue { get; set; }
        public bool ThrowSlot { get; set; }
        public string? SlotName => ThrowSlot ? throw new InvalidOperationException("SlotName 読取失敗") : SlotValue;
    }
}

namespace MegaCrit.Sts2.Core.Runs
{
    using Entities.Ascension;

    public enum GameMode { None, Standard, Daily, Dread, Blitz }

    public interface IRunState
    {
        GameMode GameMode { get; }
    }

    public sealed class StubRunState : IRunState
    {
        public GameMode GameMode { get; set; } = GameMode.Standard;
    }

    // 本物は RunManager.Instance.IsInProgress（State != null）・HasAscension（AscensionManager.HasLevel）・
    // IsDread（State.GameMode == Dread）。どちらも IsInProgress が false なら false。試験は値だけを切り替える。
    public sealed class RunManager
    {
        public static RunManager Instance { get; } = new();
        public bool IsInProgress { get; set; } = true;
        public HashSet<AscensionLevel> Levels { get; } = new();
        public bool Dread { get; set; }
        public int AscensionReads { get; private set; }
        public bool HasAscension(AscensionLevel level)
        {
            AscensionReads++;
            return IsInProgress && Levels.Contains(level);
        }
        public bool IsDread() => IsInProgress && Dread;
    }
}

namespace MegaCrit.Sts2.Core.Combat
{
    using Entities.Creatures;
    using Models;
    using Runs;

    public interface ICombatState
    {
        IRunState RunState { get; }
        IReadOnlyList<Creature> Enemies { get; }
        IReadOnlyList<object> Players { get; }
        EncounterModel? Encounter { get; }
    }

    public sealed class StubCombatState : ICombatState
    {
        public IRunState RunState { get; set; } = new StubRunState();
        public IReadOnlyList<Creature> Enemies { get; set; } = new List<Creature>();
        public IReadOnlyList<object> Players { get; set; } = new List<object> { new() };
        public EncounterModel? EncounterValue { get; set; }
        public bool ThrowEncounter { get; set; }
        public EncounterModel? Encounter => ThrowEncounter ? throw new InvalidOperationException("Encounter 読取失敗") : EncounterValue;
    }
}

namespace MegaCrit.Sts2.Core.Models.Monsters
{
    public sealed record GiantValues(int PressureGun, int SteamBoost, int Pressurize, int Stomp, int Ram, int PressureUp, int Siphon);

    public sealed class WaterfallGiant : MonsterModel
    {
        private int _currentPressureGunDamage;
        private int _steamEruptionDamage;
        private bool _isAboutToBlow;
        // getter の戻り値。本物は AscensionHelper の判定から作るが、試験は値だけを置く。
        private GiantValues _values = new(0, 0, 0, 0, 0, 0, 0);
        public string? ThrowFrom { get; set; }
        public int GetterReads { get; private set; }

        public int SiphonHeal => Get("SiphonHeal", _values.Siphon);
        private int PressurizeAmount => Get("PressurizeAmount", _values.Pressurize);
        private int StompDamage => Get("StompDamage", _values.Stomp);
        private int RamDamage => Get("RamDamage", _values.Ram);
        private int PressureUpDamage => Get("PressureUpDamage", _values.PressureUp);
        private int SteamBoostAmount => Get("SteamBoostAmount", _values.SteamBoost);
        // 本物の CurrentPressureGunDamage は setter に AssertMutable がある。MOD はフィールドを読む。
        private int CurrentPressureGunDamage => _currentPressureGunDamage;

        private int Get(string name, int value)
        {
            GetterReads++;
            if (ThrowFrom == name) throw new InvalidOperationException("boom");
            return value;
        }

        public void Set(bool aboutToBlow, int damage)
        {
            _isAboutToBlow = aboutToBlow;
            _steamEruptionDamage = damage;
        }

        public void Set(GiantValues values)
        {
            _values = values;
            _currentPressureGunDamage = values.PressureGun;
        }

        public (bool, int) Peek() => (_isAboutToBlow, _steamEruptionDamage);
        public (int, GiantValues) PeekValues() => (_currentPressureGunDamage, _values);
    }

    // sts2#1018 の許可リスト。本体と同じ宣言型・名前・型の private フィールド（Axebot は public getter）。
    public sealed class Aeonglass : MonsterModel
    {
        private int _additionalStrength;
        private int _witherUpgradeCount;
        public void Set(int strength, int wither) { _additionalStrength = strength; _witherUpgradeCount = wither; }
    }

    public sealed class Axebot : MonsterModel
    {
        private int? _stockOverrideAmount;
        public int StockAmount => _stockOverrideAmount ?? 2;
        public void Set(int? stock) => _stockOverrideAmount = stock;
    }

    public sealed class BowlbugRock : MonsterModel
    {
        private bool _isOffBalance;
        public void Set(bool value) => _isOffBalance = value;
    }

    public sealed class Fabricator : MonsterModel
    {
        private MonsterModel? _lastSpawned;
        public void Set(MonsterModel? last) => _lastSpawned = last;
        public MonsterModel? Peek() => _lastSpawned;
    }

    public sealed class FrogKnight : MonsterModel
    {
        private bool _hasBeetleCharged;
        public void Set(bool value) => _hasBeetleCharged = value;
    }

    public sealed class KnowledgeDemon : MonsterModel
    {
        private int _curseOfKnowledgeCounter;
        public void Set(int value) => _curseOfKnowledgeCounter = value;
    }

    public sealed class LivingFog : MonsterModel
    {
        private int _bloatAmount = 1;
        public int Peek() => _bloatAmount;
    }

    public sealed class Nibbit : MonsterModel
    {
        private bool _isFront;
        private bool _isAlone;
        public void Set(bool alone, bool front) { _isAlone = alone; _isFront = front; }
    }

    public sealed class Queen : MonsterModel
    {
        private bool _hasAmalgamDied;
        public void Set(bool value) => _hasAmalgamDied = value;
    }

    public sealed class TestSubject : MonsterModel
    {
        private int _respawns;
        private int _extraMultiClawCount;
        public void Set(int respawns, int extra) { _respawns = respawns; _extraMultiClawCount = extra; }
    }

    public sealed class Toadpole : MonsterModel
    {
        private bool _isFront;
        public void Set(bool value) => _isFront = value;
    }

    public sealed class TwoTailedRat : MonsterModel
    {
        private int _turnsUntilSummonable = 2;
        private int _callForBackupCount;
        public void Set(int turns, int calls) { _turnsUntilSummonable = turns; _callForBackupCount = calls; }
    }

    public sealed class Guardbot : MonsterModel { }

    public sealed class LouseProgenitor : MonsterModel
    {
        // 同名フィールドを持つ別の敵でも combat_state を付けない。
        private bool _isAboutToBlow = true;
        public bool Peek() => _isAboutToBlow;
    }
}
