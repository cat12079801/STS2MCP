// v0.113.1 の WaterfallGiant から、MOD が読む private フィールド・getter の名前・型だけを写す。
// move の実行・TriggerAboutToBlowState・乱数・Ascension の判定は用意しない。値は試験が直接設定する。
namespace MegaCrit.Sts2.Core.Models
{
    public class MonsterModel { }
}

namespace MegaCrit.Sts2.Core.Runs
{
    // 本物は RunManager.Instance.IsInProgress（State != null）。試験は値だけを切り替える。
    public sealed class RunManager
    {
        public static RunManager Instance { get; } = new();
        public bool IsInProgress { get; set; } = true;
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

    public sealed class LouseProgenitor : MonsterModel
    {
        // 同名フィールドを持つ別の敵でも combat_state を付けない。
        private bool _isAboutToBlow = true;
        public bool Peek() => _isAboutToBlow;
    }
}
