using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

public class Barrier : MonoBehaviour
{
    [Serializable]
    public struct HpSpriteStage
    {
        [Range(0, 100)] public int hpPercent;
        public Sprite sprite;
    }

    private static readonly Color BufferGaugeColor = new Color(78f / 255f, 221f / 255f, 252f / 255f, 1f);

    [SerializeField] private int maxHp = 100;
    [SerializeField] private int contactDamage = 10;
    private int configuredMaxHp;
    [SerializeField] private Slider hpGauge;
    [SerializeField] private Image bufferGaugeFillImage;
    [SerializeField] private float hpGaugeTweenDuration = 0.25f;
    [SerializeField] private Color hpGaugeFullColor = new Color(0.82f, 0.95f, 0.48f, 1f);
    [SerializeField] private Color hpGaugeEmptyColor = new Color(1f, 0.42f, 0.42f, 1f);
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Sprite fullHpSprite;
    [SerializeField] private HpSpriteStage[] damagedSprites;

    private int coreHp;
    private int bufferHp;
    private int coreMaxHp;
    private int bufferMaxHp;
    private Tween hpGaugeTween;
    private Tween bufferGaugeTween;
    private Image hpGaugeFillImage;

    public int Hp => coreHp + bufferHp;
    public int MaxHp => maxHp;

    public event Action HpDepleted;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>개발 빌드 전용: 켜면 피해를 받지 않는다(성능 측정 등).</summary>
    public static bool DebugInvincible;

    /// <summary>개발 빌드 전용: 영구강화(방어막) 레벨이 바뀐 뒤 최대 체력을 다시 계산하고 체력을 가득 채운다.</summary>
    public void DebugReapplyReinforce()
    {
        // Awake 전(방어막이 아직 한 번도 활성화되지 않음)에는 Awake가 영구강화 보너스를 직접 적용한다.
        // 이때 건드리면 기준 체력(configuredMaxHp)이 0이라 최대 체력이 1로 덮어써진다.
        if (configuredMaxHp <= 0) return;

        ApplyTitleBarrierBonus();
        InitializeHpPools();
        RefreshVisuals(animateGauge: false);
    }
#endif

    private void Awake()
    {
        if (hpGauge == null)
            hpGauge = GetComponentInChildren<Slider>(true);
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();
        if (fullHpSprite == null && spriteRenderer != null)
            fullHpSprite = spriteRenderer.sprite;

        EnsureHpGaugeColors();
        ResolveHpGaugeFillImage();
        EnsureBufferGaugeFill();
        configuredMaxHp = Mathf.Max(1, maxHp);
        ApplyTitleBarrierBonus();
        InitializeHpPools();
        RefreshVisuals(animateGauge: false);
    }

    private void ApplyTitleBarrierBonus()
    {
        float multiplier = 1f + TitleEnhanceProgress.CurrentBarrierPercent / 100f;
        maxHp = Mathf.Max(configuredMaxHp, Mathf.RoundToInt(configuredMaxHp * multiplier));
    }

    private void InitializeHpPools()
    {
        // 기본 체력(초록)은 그대로, 영구강화로 늘어난 만큼이 추가 체력(파랑). 피해는 파랑부터 깎인다.
        coreMaxHp = configuredMaxHp;
        bufferMaxHp = Mathf.Max(0, maxHp - configuredMaxHp);
        bufferHp = bufferMaxHp;
        coreHp = coreMaxHp;
    }

    private void OnDestroy()
    {
        KillHpGaugeTween();
    }

    public void ApplyContactHit()
    {
        TakeDamage(contactDamage);
    }

    public void ApplyContactHit(int damage)
    {
        TakeDamage(damage > 0 ? damage : contactDamage);
    }

    public void TakeDamage(int amount)
    {
        if (amount <= 0 || Hp <= 0) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (DebugInvincible) return;
#endif

        int remaining = amount;
        if (bufferHp > 0)
        {
            int absorbed = Mathf.Min(bufferHp, remaining);
            bufferHp -= absorbed;
            remaining -= absorbed;
        }

        if (remaining > 0)
            coreHp = Mathf.Max(0, coreHp - remaining);

        RefreshVisuals(animateGauge: true);

        if (Hp <= 0)
            HpDepleted?.Invoke();
    }

    /// <summary>잃은 체력의 절반을 회복한다(홀수면 올림). 보호막(버퍼) 쪽을 먼저 채운다.</summary>
    public void RestoreHalfOfLostHp()
    {
        int lost = (coreMaxHp + bufferMaxHp) - (coreHp + bufferHp);
        if (lost <= 0) return;

        int heal = Mathf.CeilToInt(lost * 0.5f);

        int bufferHeal = Mathf.Min(heal, bufferMaxHp - bufferHp);
        bufferHp += bufferHeal;
        heal -= bufferHeal;

        coreHp = Mathf.Min(coreMaxHp, coreHp + heal);
        RefreshVisuals(animateGauge: true);
    }

    private void RefreshVisuals(bool animateGauge)
    {
        RefreshGauge(animateGauge);
        RefreshSprite();
    }

    private void RefreshGauge(bool animate)
    {
        if (hpGauge == null) return;

        ResolveHpGaugeFillImage();
        EnsureBufferGaugeFill();
        hpGauge.minValue = 0f;
        hpGauge.maxValue = Mathf.Max(1, coreMaxHp);
        KillHpGaugeTween();

        // 초록 = 기본 체력(막대 전체 = 1), 파랑 = 영구강화로 얻은 추가 체력으로 초록 위에 왼쪽부터 겹친다.
        // 파랑 길이 = 남은 추가 체력 / 기본 체력 (강화 1단계 최대 0.2, 3단계 0.6, 5단계 1.0).
        // 파랑이 먼저 깎이고(오른쪽 끝부터), 다 없어진 뒤에야 아래의 초록이 줄어든다.
        float displayHp = coreHp;
        float bufferRatio = coreMaxHp > 0 ? Mathf.Clamp01(bufferHp / (float)coreMaxHp) : 0f;
        bool showBuffer = bufferMaxHp > 0;

        if (bufferGaugeFillImage != null)
        {
            bufferGaugeFillImage.color = BufferGaugeColor;
            bufferGaugeFillImage.enabled = showBuffer && bufferRatio > 0.0001f;
        }

        if (!animate || !hpGauge.gameObject.activeInHierarchy)
        {
            hpGauge.value = displayHp;
            ApplyHpGaugeColor(GetCoreRatio(coreHp));
            SetBufferFillRatio(bufferRatio);
            return;
        }

        float startBuffer = GetBufferFillRatio();
        hpGaugeTween = hpGauge
            .DOValue(displayHp, hpGaugeTweenDuration)
            .SetEase(Ease.OutQuad)
            .SetUpdate(true)
            .OnUpdate(() => ApplyHpGaugeColor(GetCoreRatio(hpGauge.value)));

        if (showBuffer && bufferGaugeFillImage != null)
        {
            bufferGaugeTween = DOTween
                .To(() => startBuffer, SetBufferFillRatio, bufferRatio, hpGaugeTweenDuration)
                .SetEase(Ease.OutQuad)
                .SetUpdate(true);
        }
        else
        {
            SetBufferFillRatio(0f);
        }
    }

    /// <summary>파랑 바의 현재 길이 비율(전체 막대 길이 대비).</summary>
    private float GetBufferFillRatio()
    {
        if (bufferGaugeFillImage == null)
            return 0f;

        RectTransform rt = bufferGaugeFillImage.rectTransform;
        return Mathf.Clamp01(rt.anchorMax.x - rt.anchorMin.x);
    }

    private void SetBufferFillRatio(float ratio)
    {
        if (bufferGaugeFillImage == null)
            return;

        float clamped = Mathf.Clamp01(ratio);
        // 파랑은 막대 전체 길이를 기준으로 왼쪽부터 채워지고 오른쪽 끝부터 줄어든다.
        RectTransform rt = bufferGaugeFillImage.rectTransform;
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(clamped, 1f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        bufferGaugeFillImage.color = BufferGaugeColor;
        bufferGaugeFillImage.enabled = bufferMaxHp > 0 && clamped > 0.0001f;
    }

    private void ResolveHpGaugeFillImage()
    {
        if (hpGaugeFillImage != null) return;
        if (hpGauge == null || hpGauge.fillRect == null) return;

        hpGaugeFillImage = hpGauge.fillRect.GetComponent<Image>();
    }

    private void EnsureBufferGaugeFill()
    {
        if (hpGauge == null || hpGauge.fillRect == null)
            return;

        Transform fillArea = hpGauge.fillRect.parent;
        if (fillArea == null) return;

        if (bufferGaugeFillImage == null)
        {
            Transform existing = fillArea.Find("BufferFill");
            GameObject bufferGo;
            if (existing != null)
            {
                bufferGo = existing.gameObject;
            }
            else
            {
                bufferGo = new GameObject("BufferFill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                bufferGo.transform.SetParent(fillArea, false);
            }

            bufferGo.transform.SetAsLastSibling();
            bufferGaugeFillImage = bufferGo.GetComponent<Image>();
        }

        RectTransform rt = bufferGaugeFillImage.rectTransform;
        rt.localScale = Vector3.one;
        rt.pivot = new Vector2(0f, 0.5f);

        Image source = hpGauge.fillRect.GetComponent<Image>();
        if (source != null && source.sprite != null)
            bufferGaugeFillImage.sprite = source.sprite;

        bufferGaugeFillImage.type = Image.Type.Sliced;
        bufferGaugeFillImage.pixelsPerUnitMultiplier = 1.5f;
        bufferGaugeFillImage.color = BufferGaugeColor;
        bufferGaugeFillImage.raycastTarget = false;
    }

    private void EnsureHpGaugeColors()
    {
        // 기존 씬 직렬화로 Color 기본값이 (0,0,0,0)이 된 경우 복구
        if (hpGaugeFullColor.maxColorComponent <= 0.001f)
            hpGaugeFullColor = new Color(0.82f, 0.95f, 0.48f, 1f);
        if (hpGaugeEmptyColor.maxColorComponent <= 0.001f)
            hpGaugeEmptyColor = new Color(1f, 0.42f, 0.42f, 1f);
    }

    private float GetCoreRatio(float currentCoreHp)
    {
        return coreMaxHp <= 0 ? 0f : Mathf.Clamp01(currentCoreHp / coreMaxHp);
    }

    private void ApplyHpGaugeColor(float ratio)
    {
        if (hpGaugeFillImage == null) return;
        hpGaugeFillImage.color = EvaluateHpGaugeColor(ratio);
    }

    private Color EvaluateHpGaugeColor(float ratio)
    {
        float t = 1f - Mathf.Clamp01(ratio);

        Color.RGBToHSV(hpGaugeFullColor, out float fullH, out float fullS, out float fullV);
        Color.RGBToHSV(hpGaugeEmptyColor, out float emptyH, out float emptyS, out float emptyV);

        float hue = Mathf.Lerp(fullH, emptyH, t);
        float saturation = Mathf.Lerp(fullS, emptyS, t);
        float value = Mathf.Lerp(fullV, emptyV, t);

        Color color = Color.HSVToRGB(hue, saturation, value);
        color.a = Mathf.Lerp(hpGaugeFullColor.a, hpGaugeEmptyColor.a, t);
        return color;
    }

    private void KillHpGaugeTween()
    {
        if (hpGaugeTween != null)
        {
            hpGaugeTween.Kill();
            hpGaugeTween = null;
        }

        if (bufferGaugeTween != null)
        {
            bufferGaugeTween.Kill();
            bufferGaugeTween = null;
        }
    }

    private void RefreshSprite()
    {
        if (spriteRenderer == null) return;

        spriteRenderer.sprite = ResolveSpriteForCurrentHp();
    }

    private Sprite ResolveSpriteForCurrentHp()
    {
        float hpPercent = maxHp > 0 ? (Hp * 100f) / maxHp : 0f;
        Sprite selected = fullHpSprite;
        int matchedPercent = int.MaxValue;

        if (damagedSprites == null) return selected;

        for (int i = 0; i < damagedSprites.Length; i++)
        {
            HpSpriteStage stage = damagedSprites[i];
            if (stage.sprite == null) continue;
            if (hpPercent > stage.hpPercent) continue;
            if (stage.hpPercent >= matchedPercent) continue;

            matchedPercent = stage.hpPercent;
            selected = stage.sprite;
        }

        return selected;
    }
}
