using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 무기 아이콘 주변에서 4갈래 별(✦)이 반짝이며 나타났다 사라지는 효과.
/// Item이 Awake에서 자동으로 붙인다(프리팹 수정 불필요). 색은 흰색에 등급 색을 살짝 섞는다.
/// </summary>
[DisallowMultipleComponent]
public class ItemSparkle : MonoBehaviour
{
    private const int MaxSparkles = 6;

    private class Spark
    {
        public RectTransform rect;
        public Image image;
        public float age;
        public float life;
        public float size;
        public float spin;
        public float peakAlpha;
        public bool active;
    }

    private static Sprite starSprite;

    private Item item;
    private RectTransform container;
    private RectTransform source;
    private readonly List<RectTransform> cells = new();
    private readonly List<Spark> sparks = new();
    private float spawnTimer;

    private void Awake()
    {
        item = GetComponent<Item>();
        EnsureContainer();
        CacheCells();
    }

    private void OnEnable()
    {
        spawnTimer = Random.value * 0.4f;
    }

    private void OnDisable()
    {
        for (int i = 0; i < sparks.Count; i++)
        {
            sparks[i].active = false;
            if (sparks[i].image != null) sparks[i].image.enabled = false;
        }
    }

    private void Update()
    {
        if (item == null || container == null) return;

        float dt = Time.unscaledDeltaTime;
        for (int i = 0; i < sparks.Count; i++)
            if (sparks[i].active) UpdateSpark(sparks[i], dt);

        if (!item.SparkleEnabled || item.Grade < item.SparkleMinGrade) return;

        // 등급이 높을수록 조금 더 자주 반짝인다.
        float rate = 2.2f + item.Grade * 0.5f;
        spawnTimer -= dt;
        if (spawnTimer > 0f) return;
        spawnTimer = 1f / rate * Random.Range(0.6f, 1.4f);
        SpawnSpark();
    }

    private void EnsureContainer()
    {
        if (container != null) return;

        var go = new GameObject("Sparkle", typeof(RectTransform));
        container = (RectTransform)go.transform;
        container.SetParent(transform, false);
        container.anchorMin = container.anchorMax = new Vector2(0.5f, 0.5f);
        container.sizeDelta = Vector2.zero;
        container.localScale = Vector3.one;
        container.SetAsLastSibling(); // 무기 아이콘 위에 그린다.

        source = transform.Find("Sprite") as RectTransform;
    }

    // 아이템이 실제로 차지하는 칸(Block/Pivot). ㄱ, ㄴ자처럼 비어 있는 칸에는 반짝이지 않도록 쓴다.
    private void CacheCells()
    {
        cells.Clear();
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child.name != "Block" && child.name != "Pivot") continue;
            if (child is RectTransform rt) cells.Add(rt);
        }
    }

    // pos(컨테이너 좌표)가 아이템의 어느 한 칸 안쪽(가장자리에서 margin만큼 안쪽)에 있는지 검사한다.
    private bool IsInsideOccupiedCell(Vector2 pos, float margin)
    {
        if (cells.Count == 0) return true;

        Vector3 world = container.TransformPoint(pos);
        for (int i = 0; i < cells.Count; i++)
        {
            RectTransform cell = cells[i];
            if (cell == null) continue;

            Vector2 local = cell.InverseTransformPoint(world);
            Rect r = cell.rect;
            if (local.x >= r.xMin + margin && local.x <= r.xMax - margin &&
                local.y >= r.yMin + margin && local.y <= r.yMax - margin)
                return true;
        }

        return false;
    }

    private bool TryGetIconArea(out Vector2 center, out Vector2 halfSize)
    {
        RectTransform src = source != null ? source : transform.Find("Pivot") as RectTransform;
        if (src == null)
        {
            center = Vector2.zero;
            halfSize = new Vector2(40f, 40f);
            return false;
        }

        Vector3 worldCenter = src.TransformPoint(src.rect.center);
        center = container.InverseTransformPoint(worldCenter);

        // 회전한 아이템도 대응하도록 로컬 축 기준 크기를 컨테이너 좌표로 환산한다.
        Vector3 worldRight = src.TransformPoint(src.rect.center + new Vector2(src.rect.width * 0.5f, 0f));
        Vector3 worldUp = src.TransformPoint(src.rect.center + new Vector2(0f, src.rect.height * 0.5f));
        Vector2 right = (Vector2)container.InverseTransformPoint(worldRight) - center;
        Vector2 up = (Vector2)container.InverseTransformPoint(worldUp) - center;
        halfSize = new Vector2(Mathf.Max(right.magnitude, 20f), Mathf.Max(up.magnitude, 20f));
        return true;
    }

    private Spark GetFreeSpark()
    {
        for (int i = 0; i < sparks.Count; i++)
            if (!sparks[i].active) return sparks[i];

        if (sparks.Count >= MaxSparkles) return null;

        var go = new GameObject("Spark", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rect = (RectTransform)go.transform;
        rect.SetParent(container, false);

        Image image = go.GetComponent<Image>();
        image.sprite = GetStarSprite();
        image.raycastTarget = false;
        image.enabled = false;

        var spark = new Spark { rect = rect, image = image };
        sparks.Add(spark);
        return spark;
    }

    private void SpawnSpark()
    {
        Spark s = GetFreeSpark();
        if (s == null) return;

        TryGetIconArea(out Vector2 center, out Vector2 half);

        // 아이콘 둘레에 흩어지게 하되, 아이템이 차지한 칸 안쪽 지점만 사용한다(빈 칸은 건너뜀).
        float size = Random.Range(18f, 34f);
        float margin = size * 0.4f;
        bool found = false;
        Vector2 pos = center;
        for (int attempt = 0; attempt < 14; attempt++)
        {
            float angle = Random.value * Mathf.PI * 2f;
            float radius = Random.Range(0.3f, 1.15f);
            Vector2 candidate = center + new Vector2(Mathf.Cos(angle) * half.x, Mathf.Sin(angle) * half.y) * radius;
            if (!IsInsideOccupiedCell(candidate, margin)) continue;

            pos = candidate;
            found = true;
            break;
        }

        if (!found) return;

        s.age = 0f;
        s.life = Random.Range(0.7f, 1.2f);
        s.size = size;
        s.spin = Random.Range(-40f, 40f);
        s.peakAlpha = Random.Range(0.85f, 1f);
        s.active = true;

        Color c = Color.Lerp(item.GradeColor, Color.white, 0.75f);
        c.a = 0f;
        s.image.color = c;
        s.image.enabled = true;
        s.rect.sizeDelta = new Vector2(s.size, s.size);
        s.rect.anchoredPosition = pos;
        s.rect.localScale = Vector3.zero;
        s.rect.localEulerAngles = new Vector3(0f, 0f, Random.Range(-20f, 20f));
    }

    private void UpdateSpark(Spark s, float dt)
    {
        s.age += dt;
        float t = s.age / s.life;
        if (t >= 1f)
        {
            s.active = false;
            s.image.enabled = false;
            return;
        }

        // 0 → 1 → 0 로 커졌다 작아지는 깜빡임 (끝이 뾰족한 별이 팡 하고 켜졌다 꺼지는 느낌)
        float pulse = Mathf.Sin(t * Mathf.PI);
        float scale = pulse * pulse * (3f - 2f * pulse);
        s.rect.localScale = Vector3.one * Mathf.Lerp(0f, 1f, scale);
        s.rect.Rotate(0f, 0f, s.spin * dt);

        Color c = s.image.color;
        c.a = s.peakAlpha * Mathf.Clamp01(pulse * 1.6f);
        s.image.color = c;
    }

    // 4갈래 별(✦) 모양을 런타임에 한 번만 만든다.
    private static Sprite GetStarSprite()
    {
        if (starSprite != null) return starSprite;

        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = "SparkleStar"
        };

        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = Mathf.Abs((x + 0.5f) / size * 2f - 1f);
                float v = Mathf.Abs((y + 0.5f) / size * 2f - 1f);

                // 4갈래 별: sqrt(|x|) + sqrt(|y|) <= 1 (안쪽으로 오목한 별 모양)
                float f = Mathf.Sqrt(u) + Mathf.Sqrt(v);
                float edge = Mathf.Clamp01((1f - f) / 0.18f);
                // 중심은 더 환하게
                float core = Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v) * 2.2f);
                float a = Mathf.Clamp01(edge * 0.85f + core * 0.5f * edge);

                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        starSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        starSprite.name = "SparkleStar";
        return starSprite;
    }
}
