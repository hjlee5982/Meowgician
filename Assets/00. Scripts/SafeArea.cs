using UnityEngine;

/// <summary>
/// 이 RectTransform을 화면의 세이프 에어리어(노치, 제스처 바 등을 제외한 영역)에 맞춘다.
/// 시작할 때 한 번만이 아니라 해상도·방향·세이프 에어리어가 바뀔 때마다 다시 적용한다
/// (에디터 Game 뷰 크기 변경, 웹 브라우저 창 크기 변경, 휴대전화 회전 등).
/// </summary>
public class SafeArea : MonoBehaviour
{
    private RectTransform rectTransform;
    private Rect lastSafeArea;
    private Vector2Int lastScreenSize;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        Apply();
    }

    private void OnEnable()
    {
        Apply();
    }

    private void Update()
    {
        if (Screen.safeArea != lastSafeArea ||
            Screen.width != lastScreenSize.x ||
            Screen.height != lastScreenSize.y)
        {
            Apply();
        }
    }

    private void Apply()
    {
        if (rectTransform == null)
            rectTransform = GetComponent<RectTransform>();
        if (rectTransform == null || Screen.width <= 0 || Screen.height <= 0)
            return;

        Rect safe = Screen.safeArea;
        lastSafeArea = safe;
        lastScreenSize = new Vector2Int(Screen.width, Screen.height);

        Vector2 min = safe.min;
        Vector2 max = safe.max;

        min.x /= Screen.width;
        min.y /= Screen.height;
        max.x /= Screen.width;
        max.y /= Screen.height;

        rectTransform.anchorMin = min;
        rectTransform.anchorMax = max;
    }
}
