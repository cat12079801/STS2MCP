// ゲームの enum の写し（メンバーと順序は sts2-a671e9ef のデコンパイルと同じ）。
// 試験はゲームの DLL を参照しないので、本番の判定関数が読む型だけをここで定義する。

namespace MegaCrit.Sts2.Core.Combat
{
    public enum CombatSide
    {
        None,
        Player,
        Enemy
    }

    public enum PlayerTurnPhase
    {
        None,
        Start,
        AutoPrePlay,
        Play,
        AutoPostPlay,
        End
    }
}

namespace MegaCrit.Sts2.Core.Entities.Multiplayer
{
    public enum ActionSynchronizerCombatState
    {
        NotInCombat,
        PreCombatSetup,
        PlayPhase,
        EndTurnPhaseOne,
        NotPlayPhase
    }
}
