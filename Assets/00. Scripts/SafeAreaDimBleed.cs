using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 세이프 에어리어 안쪽에 놓인 "화면을 어둡게 덮는 Image"가 세이프 에어리어 바깥(노치, 하단 바 영역)까지 어둡게 덮도록 한다.
///
/// 이 컴포넌트가 붙은 오브젝트는 레이아웃(자식 배치)을 그대로 두고, 자식에 전체 화면 크기의 어두운 Image("DimBleed")를
/// 맨 뒤에 만들어서 화면 끝까지 덮는다. 원래 Image는 색만 투명하게 바꿔 클릭을 막는 역할(Raycast)은 유지한다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
public class SafeAreaDimBleed : MonoBehaviour
{
    private const string BleedName = "DimBleed";

    [SerializeField, HideInInspector] private Color dimColor = new Color(0f, 0f, 0f, 0f);
    [SerializeField, HideInInspector] private bool captured;

    private Image source;
    private RectTransform self;
    private RectTransform bleedRect;
    private Canvas rootCanvas;
    private readonly Vector3[] corners = new Vector3[4];

    private void Awake()
    {
        Setup();
    }

    private void OnEnable()
    {
        Setup();
        Apply();
    }

    private void LateUpdate()
    {
        Apply();
    }

    private void Setup()
    {
        if (self == null)
            self = (RectTransform)transform;
        if (source == null)
            source = GetComponent<Image>();
        if (source == null)
            return;

        if (!captured)
        {
            dimColor = source.color;
            captured = true;
        }

        if (bleedRect == null)
        {
            Transform existing = transform.Find(BleedName);
            if (existing != null)
            {
                bleedRect = (RectTransform)existing;
            }
            else
            {
                var go = new GameObject(BleedName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                bleedRect = (RectTransform)go.transform;
                bleedRect.SetParent(transform, false);
            }

            bleedRect.SetAsFirstSibling();
        }

        Image bleedImage = bleedRect.GetComponent<Image>();
        bleedImage.sprite = source.sprite;
        bleedImage.type = source.type;
        bleedImage.color = dimColor;
        bleedImage.raycastTarget = false;

        // 원래 Image는 보이지 않게 하되 클릭 차단(Raycast)은 유지한다.
        Color c = source.color;
        c.a = 0f;
        source.color = c;
    }

    private void Apply()
    {
        if (bleedRect == null || self == null)
            return;

        if (rootCanvas == null)
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            rootCanvas = canvas != null ? canvas.rootCanvas : null;
        }

        if (rootCanvas == null)
            return;

        RectTransform canvasRect = (RectTransform)rootCanvas.transform;
        canvasRect.GetWorldCorners(corners);
        Vector2 min = self.InverseTransformPoint(corners[0]);
        Vector2 max = self.InverseTransformPoint(corners[2]);
        Rect r = self.rect;

        Vector2 offsetMin = min - r.min;
        Vector2 offsetMax = max - r.max;

        bleedRect.anchorMin = Vector2.zero;
        bleedRect.anchorMax = Vector2.one;
        if ((bleedRect.offsetMin - offsetMin).sqrMagnitude > 0.0001f)
            bleedRect.offsetMin = offsetMin;
        if ((bleedRect.offsetMax - offsetMax).sqrMagnitude > 0.0001f)
            bleedRect.offsetMax = offsetMax;
    }
}
