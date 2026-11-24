using UnityEngine;

/// <summary>
/// 물리 레이어 이름과 자주 쓰는 검사 필터.
/// 몬스터 / 플레이어 투사체 / 몬스터 투사체 / 방어막을 레이어로 나눠 두고,
/// 충돌 매트릭스에서 필요 없는 쌍(몬스터-몬스터, 투사체-투사체 등)을 꺼서 물리 비용과 불필요한 콜백을 줄인다.
/// </summary>
public static class GameLayers
{
    public const string Monster = "Monster";
    public const string PlayerProjectile = "PlayerProjectile";
    public const string MonsterProjectile = "MonsterProjectile";
    public const string Barrier = "Barrier";

    private static bool filterReady;
    private static ContactFilter2D monsterFilter;

    /// <summary>투사체가 "몬스터만" 찾는 Physics2D.OverlapCollider용 필터. 레이어가 없으면 기존처럼 전부 검사한다.</summary>
    public static ContactFilter2D MonsterOverlapFilter
    {
        get
        {
            if (!filterReady)
            {
                int mask = LayerMask.GetMask(Monster);
                if (mask == 0)
                {
                    monsterFilter = ContactFilter2D.noFilter;
                }
                else
                {
                    monsterFilter = new ContactFilter2D();
                    monsterFilter.useTriggers = true;
                    monsterFilter.SetLayerMask(mask);
                }

                filterReady = true;
            }

            return monsterFilter;
        }
    }
}
