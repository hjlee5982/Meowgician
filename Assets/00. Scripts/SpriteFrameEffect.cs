using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 스프라이트 프레임을 한 번 재생하고 스스로 사라지는 간단한 이펙트(폭발 등).
/// Animator 없이 프레임 배열만으로 동작한다. EffectPool로 생성되면 파괴 대신 풀로 반환된다.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class SpriteFrameEffect : MonoBehaviour
{
    [SerializeField] private Sprite[] frames;
    [SerializeField] private float framesPerSecond = 24f;

    private SpriteRenderer spriteRenderer;
    private float age;
    private Stack<SpriteFrameEffect> pool;

    internal void SetPool(Stack<SpriteFrameEffect> owner)
    {
        pool = owner;
    }

    internal void ResetPlayback()
    {
        age = 0f;
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        if (frames != null && frames.Length > 0)
            spriteRenderer.sprite = frames[0];
    }

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (frames != null && frames.Length > 0)
            spriteRenderer.sprite = frames[0];
    }

    private void Update()
    {
        if (frames == null || frames.Length == 0)
        {
            Release();
            return;
        }

        age += Time.deltaTime;
        int index = Mathf.FloorToInt(age * framesPerSecond);
        if (index >= frames.Length)
        {
            Release();
            return;
        }

        spriteRenderer.sprite = frames[index];
    }

    private void Release()
    {
        if (pool != null)
        {
            gameObject.SetActive(false);
            pool.Push(this);
        }
        else
        {
            Destroy(gameObject);
        }
    }
}
