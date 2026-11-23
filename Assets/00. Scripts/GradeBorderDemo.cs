using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 도움말 창의 예시 아이템(Item 컴포넌트가 없는 그림)에 등급별 테두리 색을 입힌다.
/// 색 값은 Item과 같은 기본값을 쓰며, 최고(5)등급은 실제 아이템처럼 번갈아 반짝인다.
/// </summary>
public class GradeBorderDemo : MonoBehaviour
{
    [SerializeField, Range(1, 5)] private int grade = 1;

    private readonly System.Collections.Generic.List<Image> borders = new();
    private float phase;

    private void Awake()
    {
        phase = Random.value * Mathf.PI * 2f;
        CacheBorders();
    }

    private void OnEnable()
    {
        if (borders.Count == 0) CacheBorders();
        Apply();
    }

    private void Update()
    {
        if (grade >= 5) Apply();
    }

    private void CacheBorders()
    {
        borders.Clear();
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child.name != "Block" && child.name != "Pivot") continue;
            Image image = child.GetComponent<Image>();
            if (image != null) borders.Add(image);
        }
    }

    private void Apply()
    {
        if (borders.Count == 0) return;

        Color color = Item.EvaluateBorderColor(
            Item.DefaultGradeBorderColors, Item.DefaultTopGradeBorderShimmerColors,
            Item.DefaultTopGradeBorderShimmerStep, grade, 5, Time.unscaledTime, phase);
        for (int i = 0; i < borders.Count; i++)
            borders[i].color = color;
    }
}
