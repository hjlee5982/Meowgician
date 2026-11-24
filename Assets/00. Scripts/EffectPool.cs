using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// SpriteFrameEffect 전용 간단한 풀. 사망/충돌 이펙트의 Instantiate/Destroy(GC, 스파이크)를 줄인다.
/// 프리팹에 SpriteFrameEffect가 없으면 일반 Instantiate로 동작한다.
/// </summary>
public static class EffectPool
{
    private static readonly Dictionary<GameObject, Stack<SpriteFrameEffect>> pools =
        new Dictionary<GameObject, Stack<SpriteFrameEffect>>();

    public static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (prefab == null) return null;

        if (prefab.GetComponent<SpriteFrameEffect>() == null)
            return Object.Instantiate(prefab, position, rotation);

        if (!pools.TryGetValue(prefab, out Stack<SpriteFrameEffect> stack))
        {
            stack = new Stack<SpriteFrameEffect>(16);
            pools[prefab] = stack;
        }

        SpriteFrameEffect effect = null;
        while (stack.Count > 0 && effect == null)
            effect = stack.Pop();

        if (effect == null)
        {
            GameObject created = Object.Instantiate(prefab, position, rotation);
            effect = created.GetComponent<SpriteFrameEffect>();
            effect.SetPool(stack);
            return created;
        }

        Transform t = effect.transform;
        t.SetPositionAndRotation(position, rotation);
        t.localScale = prefab.transform.localScale;
        effect.gameObject.SetActive(true);
        effect.ResetPlayback();
        return effect.gameObject;
    }

    /// <summary>씬 전환 시 파괴된 인스턴스 참조를 비운다.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        pools.Clear();
    }
}
