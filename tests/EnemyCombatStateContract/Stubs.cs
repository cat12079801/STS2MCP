// v0.113.1 の WaterfallGiant から、MOD が読む private フィールドの名前・型だけを写す。
// move の実行・TriggerAboutToBlowState・乱数は用意しない。値は試験が直接設定する。
namespace MegaCrit.Sts2.Core.Models
{
    public class MonsterModel { }
}

namespace MegaCrit.Sts2.Core.Models.Monsters
{
    public sealed class WaterfallGiant : MonsterModel
    {
        private int _steamEruptionDamage;
        private bool _isAboutToBlow;
        public void Set(bool aboutToBlow, int damage)
        {
            _isAboutToBlow = aboutToBlow;
            _steamEruptionDamage = damage;
        }
        public (bool, int) Peek() => (_isAboutToBlow, _steamEruptionDamage);
    }

    public sealed class LouseProgenitor : MonsterModel
    {
        // 同名フィールドを持つ別の敵でも combat_state を付けない。
        private bool _isAboutToBlow = true;
        public bool Peek() => _isAboutToBlow;
    }
}
