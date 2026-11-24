using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class Item : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public enum ItemCategory
    {
        Fire,
        Grimoire,
        Ice,
        Water,
        Wind,
        Rock,
        Buff
    }

    [SerializeField] private float rotateDuration = 0.15f;
    [SerializeField] private float cellSize = 100f;
    [Tooltip("터치로 드래그할 때 아이템을 손가락 위쪽으로 띄우는 거리(화면 높이 대비 비율). 0이면 사용 안 함.")]
    [SerializeField, Range(0f, 0.3f)] private float touchDragLiftRatio = 0.1f;
    [SerializeField] private ItemCategory category = ItemCategory.Fire;
    [SerializeField] private int grade = 1;
    [SerializeField] private Color[] gradeColors =
    {
        new Color(0.564f, 1f, 0.561f, 1f),
        new Color(0.65f, 0.82f, 1f, 1f),
        new Color(0.85f, 0.7f, 1f, 1f),
        new Color(1f, 0.85f, 0.6f, 1f),
        new Color(1f, 0.7f, 0.75f, 1f)
    };
    [SerializeField] private Projectile projectilePrefab;
    [SerializeField] private Projectile additionalProjectilePrefab;
    [Header("Outer Gap")]
    [Tooltip("아이템의 바깥쪽 가장자리를 이만큼(px) 안쪽으로 줄여서, 가방에서 맞닿은 아이템 사이에 틈(이 값의 2배)이 생기게 한다. 0이면 사용 안 함.")]
    [SerializeField, Min(0f)] private float outerGap = 3f;

    [Header("Border Colors")]
    [Tooltip("등급별 테두리 색 (1등급부터 순서대로). 최고 등급은 아래 반짝임 색을 대신 쓴다.")]
    [SerializeField] private Color32[] gradeBorderColors = (Color32[])DefaultGradeBorderColors.Clone();
    [Tooltip("최고(5)등급 테두리가 번갈아 반짝이는 색들")]
    [SerializeField] private Color32[] topGradeBorderShimmerColors = (Color32[])DefaultTopGradeBorderShimmerColors.Clone();
    [Tooltip("반짝임 색 하나에서 다음 색으로 넘어가는 데 걸리는 시간(초)")]
    [SerializeField, Min(0.05f)] private float topGradeBorderShimmerStep = DefaultTopGradeBorderShimmerStep;

    [Header("Sparkle")]
    [Tooltip("무기 아이콘 주변에서 4갈래 별이 반짝이는 효과 사용 여부")]
    [SerializeField] private bool sparkleEnabled = true;
    [Tooltip("이 등급 이상일 때만 반짝인다. (기본 4 = 4~5단계만, 1로 바꾸면 모든 단계)")]
    [SerializeField, Range(1, 5)] private int sparkleMinGrade = 4;

    [Header("Grade Glow Pulse")]
    [Tooltip("아이템 배경색이 천천히 밝아졌다 원래대로 돌아오기를 반복하는 효과 사용 여부")]
    [SerializeField] private bool gradePulseEnabled = true;
    [Tooltip("이 등급 이상일 때만 깜빡인다. (기본 4 = 4~5단계만, 1로 바꾸면 모든 단계)")]
    [SerializeField, Range(1, 5)] private int gradePulseMinGrade = 4;
    [Tooltip("한 번 밝아졌다 돌아오는 데 걸리는 시간(초)")]
    [SerializeField, Min(0.2f)] private float gradePulsePeriod = 2.6f;
    [Tooltip("가장 밝을 때 흰색 쪽으로 섞는 비율")]
    [SerializeField, Range(0f, 1f)] private float gradePulseBrightness = 0.55f;
    [SerializeField] private Sprite icon;
    [SerializeField] private Color iconColor = Color.white;

    private RectTransform rectTransform;
    private RectTransform parentRect;
    private RectTransform pivotRect;
    private InventoryManager inventoryManager;
    private bool isDragging;
    private Vector2 pointerFollowPoint;
    private Vector2 dragGrabOffsetLocal;
    private Vector2 lastScreenPosition;
    private Camera lastPressCamera;
    private Vector2 dragScreenLift;
    private Tween rotateTween;
    private float angleZ;
    private float targetAngleZ;
    private readonly List<Vector2Int> localShapeCells = new();
    private readonly List<int> occupiedSlots = new();
    private readonly List<int> slotsAtDragStart = new();
    private readonly List<Image> gradeImages = new();
    private readonly List<Image> borderImages = new();
    private bool outerGapApplied;
    private float pulsePhase;
    private bool pulseApplied;

    private Transform dragStartParent;
    private Vector2 dragStartAnchoredPosition;
    private Quaternion dragStartLocalRotation;
    private Vector3 dragStartLocalScale;
    private float dragStartAngleZ;
    private float dragStartTargetAngleZ;

    // 테두리 색 기본값(1등급부터). 도움말 창의 예시 아이템(GradeBorderDemo)도 같은 값을 쓴다.
    public static readonly Color32[] DefaultGradeBorderColors =
    {
        new Color32(169, 138, 253, 255), // 1등급
        new Color32(199, 252, 129, 255), // 2등급
        new Color32(132, 233, 254, 255), // 3등급
        new Color32(255, 255, 255, 255), // 4등급
        new Color32(255, 255, 255, 255)  // 5등급(반짝임 색을 쓰므로 예비값)
    };

    public static readonly Color32[] DefaultTopGradeBorderShimmerColors =
    {
        new Color32(105, 213, 234, 255),
        new Color32(248, 234, 49, 255),
        new Color32(255, 255, 255, 255)
    };

    public const float DefaultTopGradeBorderShimmerStep = 0.225f;

    /// <summary>등급에 맞는 테두리 색. 최고 등급이면 shimmer 색들을 시간에 따라 번갈아 보여 준다.</summary>
    public static Color EvaluateBorderColor(
        Color32[] gradeColors, Color32[] shimmerColors, float shimmerStep,
        int grade, int maxGrade, float time, float phase)
    {
        if (shimmerColors != null && shimmerColors.Length > 0 && grade >= maxGrade)
        {
            int n = shimmerColors.Length;
            float x = time / Mathf.Max(0.05f, shimmerStep) + phase / (Mathf.PI * 2f);
            int index = Mathf.FloorToInt(x);
            float t = x - index;
            t = t * t * (3f - 2f * t);
            Color a = shimmerColors[((index % n) + n) % n];
            Color b = shimmerColors[(((index + 1) % n) + n) % n];
            return Color.Lerp(a, b, t);
        }

        if (gradeColors == null || gradeColors.Length == 0)
            return Color.black;

        return gradeColors[Mathf.Clamp(grade - 1, 0, gradeColors.Length - 1)];
    }

    public enum BuffOptionType
    {
        None,
        ExtraFireInterval,
        ExtraDamage,
        Critical,
        ExtraAddRange
    }

    private const string BuffPotionKey = "Item_Buff_Potion";
    private const string BuffScrollKey = "Item_Buff_Scroll";
    private const string BuffQuillPenKey = "Item_Buff_QuillPen";
    private const string BuffCrystalBallKey = "Item_Buff_CrystalBall";

    private string dataId;
    private string displayName;
    private float baseRange;
    private float baseDamage;
    private float baseFireInterval;
    private float addRange = 1f;
    private float addDamage = 1f;
    private float addFireInterval = 1f;
    private bool hasCombatStats;
    private BuffOptionType buffOptionType;
    private float buffOptionPercent;

    public Projectile ProjectilePrefab => projectilePrefab;
    public Projectile AdditionalProjectilePrefab => additionalProjectilePrefab;
    public bool SparkleEnabled => sparkleEnabled;
    public int SparkleMinGrade => sparkleMinGrade;
    public Sprite Icon => icon;
    public Color IconColor => iconColor;
    public int Grade => grade;
    public Color GradeColor => GetGradeColor();
    public int MaxGrade => gradeColors != null && gradeColors.Length > 0 ? gradeColors.Length : 1;
    public string ShopBanKey => GetTypeKey();
    public string DataId => dataId;
    public string DisplayName => string.IsNullOrEmpty(displayName) ? GetTypeKey() : displayName;
    public float Range => hasCombatStats ? GetScaledRange() : GetFallbackRange();
    public int Damage => hasCombatStats ? GetScaledDamage() : GetFallbackDamage();
    public float FireInterval => hasCombatStats ? GetScaledFireInterval() : GetFallbackFireInterval();
    public ItemCategory Category => category;
    public bool IsBuff => category == ItemCategory.Buff;
    public bool IsWeapon => !IsBuff;
    public BuffOptionType BuffOption => buffOptionType;
    public float BuffContributionPercent =>
        buffOptionType == BuffOptionType.None ? 0f : grade * buffOptionPercent;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        parentRect = transform.parent as RectTransform;
        pivotRect = transform.Find("Pivot") as RectTransform;
        inventoryManager = GetComponentInParent<InventoryManager>();

        angleZ = Mathf.Round(rectTransform.localEulerAngles.z / 90f) * 90f;
        targetAngleZ = angleZ;
        rectTransform.localEulerAngles = new Vector3(0f, 0f, angleZ);

        CacheShapeCells();
        ApplyOuterGap();
        CacheGradeImages();
        ApplyGradeColor();
        ApplyGameData();

        pulsePhase = UnityEngine.Random.value * Mathf.PI * 2f;

        if (sparkleEnabled && GetComponent<ItemSparkle>() == null)
            gameObject.AddComponent<ItemSparkle>();
    }

    private void OnDestroy()
    {
        rotateTween?.Kill();
        if (isDragging && inventoryManager != null)
            inventoryManager.NotifyItemDragEnded(this);
    }

    private void UpdateGradePulse()
    {
        bool eligible = gradePulseEnabled && grade >= gradePulseMinGrade && gradeImages.Count > 0;
        if (!eligible)
        {
            if (pulseApplied)
            {
                pulseApplied = false;
                ApplyGradeColor();
            }

            return;
        }

        // 0 → 1 → 0 을 부드럽게 반복(코사인). 원래 색에서 흰색 쪽으로 섞어 밝아진다.
        float wave = 0.5f - 0.5f * Mathf.Cos(Time.unscaledTime * (Mathf.PI * 2f / gradePulsePeriod) + pulsePhase);
        Color color = Color.Lerp(GetGradeColor(), Color.white, wave * gradePulseBrightness);
        for (int i = 0; i < gradeImages.Count; i++)
            gradeImages[i].color = color;
        pulseApplied = true;
    }

    private void Update()
    {
        UpdateGradePulse();
        if (IsTopGrade) ApplyBorderColor();

        if (inventoryManager != null && inventoryManager.DisableRotation) return;
        if (!isDragging) return;
        if (Keyboard.current == null || !Keyboard.current.rKey.wasPressedThisFrame) return;

        TryRotate(clockwise: true);
    }

    public bool CanMergeWith(Item other)
    {
        if (other == null || other == this) return false;
        if (grade != other.grade) return false;
        if (grade >= MaxGrade) return false;
        return IsSameType(other);
    }

    public bool IsSameType(Item other)
    {
        if (other == null) return false;
        return GetTypeKey() == other.GetTypeKey();
    }

    public void SetGrade(int newGrade)
    {
        grade = Mathf.Clamp(newGrade, 1, MaxGrade);
        ApplyGradeColor();
    }

    public void UpgradeGrade()
    {
        SetGrade(grade + 1);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        GameSfx.PlayPick();
        isDragging = true;
        dragScreenLift = IsTouchPointer(eventData)
            ? new Vector2(0f, Screen.height * touchDragLiftRatio)
            : Vector2.zero;
        if (inventoryManager != null)
            inventoryManager.NotifyItemDragBegan(this);

        dragStartParent = transform.parent;
        dragStartAnchoredPosition = rectTransform.anchoredPosition;
        dragStartLocalRotation = rectTransform.localRotation;
        dragStartLocalScale = rectTransform.localScale;
        dragStartAngleZ = angleZ;
        dragStartTargetAngleZ = targetAngleZ;

        slotsAtDragStart.Clear();
        slotsAtDragStart.AddRange(occupiedSlots);
        if (slotsAtDragStart.Count > 0)
        {
            inventoryManager.ReleaseSlots(slotsAtDragStart);
            occupiedSlots.Clear();
        }

        if (inventoryManager.IsUnderShop(dragStartParent))
        {
            float scale = inventoryManager.BagCellSize / cellSize;
            rectTransform.localScale = Vector3.one * scale;
        }

        rectTransform.SetParent(inventoryManager.DragLayer, true);
        parentRect = inventoryManager.DragLayer;
        transform.SetAsLastSibling();
        BeginPointerFollow(eventData);
        UpdatePreview(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        MoveToPointer(eventData);
        UpdatePreview(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        isDragging = false;

        rotateTween?.Kill();
        angleZ = targetAngleZ;
        SetAngleZ(angleZ);

        // 터치일 때는 화면에 보이는(손가락보다 위로 띄운) 위치를 기준으로 놓는다.
        Vector2 dropPosition = eventData.position + dragScreenLift;
        if (inventoryManager.TryMergeItems(this, dropPosition, eventData.pressEventCamera))
        {
            inventoryManager.ClearPreview();
            inventoryManager.NotifyItemDragEnded(this);
            return;
        }

        bool placed;
        if (inventoryManager.IsClearPrepareMode)
        {
            placed =
                inventoryManager.TryPlaceItemOnClear(this, dropPosition, eventData.pressEventCamera, dragStartParent) ||
                inventoryManager.TryPlaceItemOnShop(this, dropPosition, eventData.pressEventCamera);
        }
        else
        {
            placed =
                inventoryManager.TryPlaceItem(this, dropPosition, eventData.pressEventCamera) ||
                inventoryManager.TryPlaceItemOnShop(this, dropPosition, eventData.pressEventCamera) ||
                inventoryManager.TryPlaceItemOnClear(this, dropPosition, eventData.pressEventCamera, dragStartParent);
        }

        inventoryManager.ClearPreview();

        // 합성은 위에서 따로 처리(합성음)하므로, 놓았을 때와 제자리로 되돌아갈 때만 내려놓는 소리를 낸다.
        GameSfx.PlayDrop();

        if (!placed)
        {
            RestoreDragStartState();
            if (slotsAtDragStart.Count > 0)
            {
                inventoryManager.OccupySlots(slotsAtDragStart);
                occupiedSlots.Clear();
                occupiedSlots.AddRange(slotsAtDragStart);
            }
        }

        inventoryManager.NotifyItemDragEnded(this);
    }

    public void PlaceOnBag(Transform itemsParent, RectTransform pivotSlot, float bagCellSize)
    {
        float scale = bagCellSize / cellSize;
        rectTransform.SetParent(itemsParent, false);
        parentRect = itemsParent as RectTransform;
        rectTransform.localScale = Vector3.one * scale;
        rectTransform.localRotation = Quaternion.Euler(0f, 0f, angleZ);

        Vector3 delta = pivotSlot.position - pivotRect.position;
        rectTransform.position += delta;
    }

    public void PlaceOnShop(RectTransform shopSlot)
    {
        occupiedSlots.Clear();
        rectTransform.SetParent(shopSlot, false);
        parentRect = shopSlot;
        rectTransform.localScale = Vector3.one;
        rectTransform.localRotation = Quaternion.Euler(0f, 0f, angleZ);
        rectTransform.anchoredPosition = Vector2.zero;
    }

    public void PlaceOnClear(RectTransform clearSlot)
    {
        occupiedSlots.Clear();
        rectTransform.SetParent(clearSlot, false);
        parentRect = clearSlot;
        rectTransform.localScale = Vector3.one;
        rectTransform.localRotation = Quaternion.Euler(0f, 0f, angleZ);
        rectTransform.anchoredPosition = Vector2.zero;
    }

    public void SetOccupiedSlots(List<int> indices)
    {
        occupiedSlots.Clear();
        occupiedSlots.AddRange(indices);
    }

    /// <summary>이 아이템이 가방에서 차지하고 있는 슬롯 번호들.</summary>
    public IReadOnlyList<int> OccupiedSlots => occupiedSlots;

    public void GetShapeOffsets(List<Vector2Int> results)
    {
        results.Clear();

        int steps = Mathf.RoundToInt(-angleZ / 90f);
        steps = ((steps % 4) + 4) % 4;

        for (int i = 0; i < localShapeCells.Count; i++)
        {
            Vector2Int cell = localShapeCells[i];
            for (int step = 0; step < steps; step++)
            {
                cell = new Vector2Int(cell.y, -cell.x);
            }

            results.Add(new Vector2Int(cell.x, -cell.y));
        }
    }

    private string GetTypeKey()
    {
        string objectName = name;
        const string cloneSuffix = "(Clone)";
        int cloneIndex = objectName.IndexOf(cloneSuffix);
        if (cloneIndex >= 0)
        {
            objectName = objectName.Substring(0, cloneIndex).TrimEnd();
        }

        return objectName;
    }

    private void ApplyGameData()
    {
        if (!GameDataRepository.TryGetItemByPrefabKey(GetTypeKey(), out ItemData itemData))
            return;

        dataId = itemData.Id;
        displayName = itemData.Name;
        ApplyBuffOptionData(itemData);

        if (!itemData.HasCombatStats) return;

        hasCombatStats = true;
        baseRange = itemData.Range;
        baseDamage = itemData.Damage;
        baseFireInterval = itemData.FireInterval;
        addRange = itemData.AddRange > 0f ? itemData.AddRange : 1f;
        addDamage = itemData.AddDamage > 0f ? itemData.AddDamage : 1f;
        addFireInterval = itemData.AddFireInterval > 0f ? itemData.AddFireInterval : 1f;
    }

    private void ApplyBuffOptionData(ItemData itemData)
    {
        buffOptionType = BuffOptionType.None;
        buffOptionPercent = 0f;

        string key = GetTypeKey();
        if (key == BuffPotionKey)
        {
            buffOptionType = BuffOptionType.ExtraFireInterval;
            buffOptionPercent = ResolveBuffPercent(itemData.ExtraFireInterval, itemData.Range);
        }
        else if (key == BuffScrollKey)
        {
            buffOptionType = BuffOptionType.ExtraDamage;
            buffOptionPercent = ResolveBuffPercent(itemData.ExtraDamage, itemData.Range);
        }
        else if (key == BuffQuillPenKey)
        {
            buffOptionType = BuffOptionType.Critical;
            buffOptionPercent = ResolveBuffPercent(itemData.Critical, itemData.Range);
        }
        else if (key == BuffCrystalBallKey)
        {
            buffOptionType = BuffOptionType.ExtraAddRange;
            buffOptionPercent = ResolveBuffPercent(itemData.ExtraAddRange, itemData.Range);
        }
    }

    private static float ResolveBuffPercent(float dedicatedValue, float rangeFallback)
    {
        if (dedicatedValue > 0f) return dedicatedValue;
        return rangeFallback > 0f ? rangeFallback : 0f;
    }

    private float GetScaledRange()
    {
        return baseRange * GetGradeMultiplier(addRange);
    }

    private int GetScaledDamage()
    {
        return Mathf.Max(0, Mathf.RoundToInt(baseDamage * GetGradeMultiplier(addDamage)));
    }

    private float GetScaledFireInterval()
    {
        float divisor = GetGradeMultiplier(addFireInterval);
        return divisor > 0f ? baseFireInterval / divisor : baseFireInterval;
    }

    private float GetGradeMultiplier(float perGradeFactor)
    {
        int steps = Mathf.Max(0, grade - 1);
        if (steps <= 0 || perGradeFactor <= 0f) return 1f;
        return Mathf.Pow(perGradeFactor, steps);
    }

    private float GetFallbackRange()
    {
        return projectilePrefab != null ? projectilePrefab.Range : 0f;
    }

    private int GetFallbackDamage()
    {
        return projectilePrefab != null ? projectilePrefab.Damage : 0;
    }

    private float GetFallbackFireInterval()
    {
        return projectilePrefab != null ? projectilePrefab.FireInterval : 1f;
    }

    private void RestoreDragStartState()
    {
        rectTransform.SetParent(dragStartParent, false);
        parentRect = dragStartParent as RectTransform;
        rectTransform.anchoredPosition = dragStartAnchoredPosition;
        rectTransform.localRotation = dragStartLocalRotation;
        rectTransform.localScale = dragStartLocalScale;
        angleZ = dragStartAngleZ;
        targetAngleZ = dragStartTargetAngleZ;
    }

    private void CacheShapeCells()
    {
        localShapeCells.Clear();
        Vector2 pivotPos = pivotRect.anchoredPosition;

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child.name != "Block" && child.name != "Pivot") continue;

            Vector2 offset = ((RectTransform)child).anchoredPosition - pivotPos;
            localShapeCells.Add(new Vector2Int(
                Mathf.RoundToInt(offset.x / cellSize),
                Mathf.RoundToInt(offset.y / cellSize)));
        }
    }

    /// <summary>
    /// 아이템 모양의 "바깥쪽" 변(옆에 같은 아이템의 칸이 없는 변)을 outerGap만큼 안쪽으로 줄인다.
    /// 칸 테두리와 등급 색 채움을 함께 줄이므로 테두리 두께는 그대로이고, 같은 아이템 안쪽 이음매는 건드리지 않는다.
    /// 칸 위치를 읽는 CacheShapeCells 이후에 한 번만 호출한다.
    /// </summary>
    private void ApplyOuterGap()
    {
        if (outerGapApplied || outerGap <= 0f) return;
        outerGapApplied = true;

        var cells = new List<RectTransform>();
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child.name != "Block" && child.name != "Pivot") continue;
            if (child is RectTransform rt) cells.Add(rt);
        }

        // 먼저 모든 칸의 "이웃 여부"를 원래 위치로 판단한 뒤에 줄인다.
        float tolerance = cellSize * 0.1f;
        var originals = new Vector2[cells.Count];
        for (int i = 0; i < cells.Count; i++)
            originals[i] = cells[i].anchoredPosition;

        Vector2[] dirs = { Vector2.left, Vector2.right, Vector2.up, Vector2.down };
        for (int i = 0; i < cells.Count; i++)
        {
            for (int d = 0; d < dirs.Length; d++)
            {
                Vector2 neighbor = originals[i] + dirs[d] * cellSize;
                bool hasNeighbor = false;
                for (int j = 0; j < cells.Count && !hasNeighbor; j++)
                {
                    if (j != i && (originals[j] - neighbor).sqrMagnitude < tolerance * tolerance)
                        hasNeighbor = true;
                }

                if (!hasNeighbor)
                    ShrinkCellSide(cells[i], dirs[d]);
            }
        }
    }

    private void ShrinkCellSide(RectTransform cell, Vector2 outward)
    {
        bool horizontal = Mathf.Abs(outward.x) > 0.5f;

        Vector2 size = cell.sizeDelta;
        Vector2 pos = cell.anchoredPosition;
        if (horizontal)
        {
            size.x -= outerGap;
            pos.x -= outward.x * outerGap * 0.5f;
        }
        else
        {
            size.y -= outerGap;
            pos.y -= outward.y * outerGap * 0.5f;
        }

        cell.sizeDelta = size;
        cell.anchoredPosition = pos;

        // 채움(자식 Image)도 같은 쪽 끝을 같은 만큼 줄인다. (중심 위치는 부모를 따라 움직인다)
        if (cell.Find("Image") is RectTransform fill)
        {
            Vector2 fillSize = fill.sizeDelta;
            if (horizontal) fillSize.x -= outerGap;
            else fillSize.y -= outerGap;
            fill.sizeDelta = fillSize;
        }
    }

    private void CacheGradeImages()
    {
        gradeImages.Clear();
        borderImages.Clear();

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child.name != "Block" && child.name != "Pivot") continue;

            // 칸 자체의 Image(검정)가 테두리, 자식 "Image"가 등급 색 채움이다.
            Image borderImage = child.GetComponent<Image>();
            if (borderImage != null) borderImages.Add(borderImage);

            Transform imageTransform = child.Find("Image");
            if (imageTransform == null) continue;

            Image image = imageTransform.GetComponent<Image>();
            if (image != null) gradeImages.Add(image);
        }
    }

    private void ApplyGradeColor()
    {
        Color color = GetGradeColor();
        for (int i = 0; i < gradeImages.Count; i++)
        {
            gradeImages[i].color = color;
        }

        ApplyBorderColor();
    }

    private bool IsTopGrade =>
        topGradeBorderShimmerColors != null && topGradeBorderShimmerColors.Length > 0 && grade >= MaxGrade;

    private Color GetBorderColor()
    {
        return EvaluateBorderColor(
            gradeBorderColors, topGradeBorderShimmerColors, topGradeBorderShimmerStep,
            grade, MaxGrade, Time.unscaledTime, pulsePhase);
    }

    private void ApplyBorderColor()
    {
        if (borderImages.Count == 0) return;

        Color color = GetBorderColor();
        for (int i = 0; i < borderImages.Count; i++)
            borderImages[i].color = color;
    }

    private Color GetGradeColor()
    {
        if (gradeColors == null || gradeColors.Length == 0)
        {
            return Color.white;
        }

        int index = Mathf.Clamp(grade - 1, 0, gradeColors.Length - 1);
        return gradeColors[index];
    }

    private void BeginPointerFollow(PointerEventData eventData)
    {
        bool snapToPivot = inventoryManager == null || inventoryManager.SnapDragToPivot;
        if (snapToPivot || pivotRect == null)
        {
            dragGrabOffsetLocal = pivotRect != null ? pivotRect.anchoredPosition : Vector2.zero;
        }
        else if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                     rectTransform,
                     eventData.position,
                     eventData.pressEventCamera,
                     out dragGrabOffsetLocal))
        {
            dragGrabOffsetLocal = pivotRect.anchoredPosition;
        }

        MoveToPointer(eventData);
    }

    private void MoveToPointer(PointerEventData eventData)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentRect,
                eventData.position + dragScreenLift,
                eventData.pressEventCamera,
                out Vector2 localPoint))
        {
            return;
        }

        pointerFollowPoint = localPoint;
        ApplyPointerFollow();
    }

    public bool TryRotate(bool clockwise)
    {
        if (inventoryManager != null && inventoryManager.DisableRotation) return false;
        if (!isDragging) return false;

        targetAngleZ += clockwise ? -90f : 90f;

        rotateTween?.Kill();
        rotateTween = DOTween.To(() => angleZ, SetAngleZ, targetAngleZ, rotateDuration)
            .SetEase(Ease.OutCubic)
            .OnComplete(() =>
            {
                angleZ = targetAngleZ;
                SetAngleZ(angleZ);
            });
        return true;
    }

    private void SetAngleZ(float z)
    {
        angleZ = z;
        rectTransform.localEulerAngles = new Vector3(0f, 0f, z);
        ApplyPointerFollow();

        if (isDragging)
        {
            inventoryManager.UpdateItemPreview(this, lastScreenPosition, lastPressCamera);
        }
    }

    private void ApplyPointerFollow()
    {
        Vector2 grabOffset = GetGrabOffsetInParentSpace();
        grabOffset.x *= rectTransform.localScale.x;
        grabOffset.y *= rectTransform.localScale.y;
        rectTransform.anchoredPosition = pointerFollowPoint - grabOffset;
    }

    private Vector2 GetGrabOffsetInParentSpace()
    {
        return rectTransform.localRotation * (Vector3)dragGrabOffsetLocal;
    }

    public Vector2 GetPivotScreenPosition(Camera eventCamera)
    {
        Transform pivot = pivotRect != null ? pivotRect : rectTransform;
        return RectTransformUtility.WorldToScreenPoint(eventCamera, pivot.position);
    }

    private static bool IsTouchPointer(PointerEventData eventData)
    {
        if (eventData is UnityEngine.InputSystem.UI.ExtendedPointerEventData extended)
            return extended.pointerType == UnityEngine.InputSystem.UI.UIPointerType.Touch;

        return eventData.pointerId >= 0;
    }

    private void UpdatePreview(PointerEventData eventData)
    {
        lastScreenPosition = eventData.position + dragScreenLift;
        lastPressCamera = eventData.pressEventCamera;
        inventoryManager.UpdateItemPreview(this, lastScreenPosition, lastPressCamera);
    }
}
