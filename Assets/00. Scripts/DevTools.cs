#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 개발 빌드 / 에디터 전용 디버그 패널. 릴리스 빌드에는 포함되지 않는다.
///
/// - 화면 왼쪽 위의 FPS / 프레임 시간 / 몬스터·투사체 수 표시
/// - "DEV" 버튼으로 밸런싱 툴 열기(DevBalancePanel): 스테이지 이동 / 영구강화 / 특성 / 장비 / 도구 탭
///   (도구 탭: 무기 채우기, 방어막 무적, 자동 진행, 성능 원인 분리 스위치)
/// 성능 측정을 위해 후반부(70단계 이상)로 빨리 가서 오래 돌려 볼 때 쓴다.
/// UI는 코드로 만들어서 씬을 수정하지 않는다. 글자는 영어(기본 폰트)만 쓴다.
/// </summary>
public class DevTools : MonoBehaviour
{
    private GameManager gm;
    private Canvas canvas;
    private GameObject panel; // 도구 탭의 내용 영역(밸런싱 패널 안)
    private DevBalancePanel balance;
    private Text statsText;
    private Text stageLabel;
    private Text invincibleLabel;
    private Text autoLabel;
    private Text infoLabel;

    private bool autoplay;
    private float autoTimer;

    // 프레임 통계 (1초마다 갱신)
    private float statTimer;
    private int statFrames;
    private float statMaxFrame;
    private int statGcCount;
    private int lastGcCollections;
    private string statsCache = "";

    // 구간별 CPU 시간(ProfilerRecorder) + 프레임 타이밍(CPU/GPU)
    private sealed class MarkerSample
    {
        public string name;
        public ProfilerRecorder recorder;
        public double sumMs;
    }
    private readonly List<MarkerSample> markers = new List<MarkerSample>();
    private Text hotText;
    private readonly FrameTiming[] frameTimings = new FrameTiming[1];
    private readonly StringBuilder hotBuilder = new StringBuilder(512);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (!Debug.isDebugBuild) return;
        SceneManager.sceneLoaded += OnSceneLoaded;
        TryCreate(SceneManager.GetActiveScene());
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        TryCreate(scene);
    }

    private static void TryCreate(Scene scene)
    {
        if (scene.name != "GameScene") return;
        if (FindFirstObjectByType<DevTools>() != null) return;
        new GameObject("DevTools").AddComponent<DevTools>();
    }

    private void Awake()
    {
        BuildUi();
        lastGcCollections = GC.CollectionCount(0);
        StartMarkerRecorders();
    }

    private void StartMarkerRecorders()
    {
        var handles = new List<ProfilerRecorderHandle>(4096);
        ProfilerRecorderHandle.GetAvailable(handles);
        var seen = new HashSet<string>();
        for (int i = 0; i < handles.Count; i++)
        {
            ProfilerRecorderDescription d = ProfilerRecorderHandle.GetDescription(handles[i]);
            if (d.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds) continue;
            if (!IsInterestingCategory(d.Category)) continue;
            if (!seen.Add(d.Category.Name + "/" + d.Name)) continue;

            ProfilerRecorder r = ProfilerRecorder.StartNew(d.Category, d.Name);
            if (!r.Valid) continue;
            markers.Add(new MarkerSample { name = d.Name, recorder = r });
        }
    }

    private static bool IsInterestingCategory(ProfilerCategory c)
    {
        return c == ProfilerCategory.Scripts || c == ProfilerCategory.Physics || c == ProfilerCategory.Animation
            || c == ProfilerCategory.Render || c == ProfilerCategory.Gui || c == ProfilerCategory.Internal
            || c == ProfilerCategory.Audio || c == ProfilerCategory.Particles || c == ProfilerCategory.Lighting;
    }

    private void SampleMarkers()
    {
        for (int i = 0; i < markers.Count; i++)
        {
            MarkerSample m = markers[i];
            if (m.recorder.Valid) m.sumMs += m.recorder.LastValue / 1000000.0;
        }
    }

    private static bool IsNoiseMarker(string n)
    {
        // 대기/총합 항목은 원인을 가리므로 숨긴다.
        return n.Contains("Idle") || n.Contains("Wait") || n.Contains("Semaphore") || n.Contains("Present")
            || n.Contains("Frame Time") || n.Contains("Total") || n == "Main Thread" || n == "PlayerLoop"
            || n.StartsWith("Gfx.") || n.StartsWith("GfxDevice") || n.Contains("Render Thread");
    }

    private string BuildHotText(int frames)
    {
        // 평균 ms 상위 8개 (하위 구간이 상위 구간에 포함되므로 겹쳐서 보인다)
        markers.Sort((a, b) => b.sumMs.CompareTo(a.sumMs));
        hotBuilder.Length = 0;

        FrameTimingManager.CaptureFrameTimings();
        uint n = FrameTimingManager.GetLatestTimings(1, frameTimings);
        if (n > 0)
        {
            hotBuilder.AppendFormat("cpu main {0:F1}  render {1:F1}  gpu {2:F1} ms\n",
                frameTimings[0].cpuMainThreadFrameTime, frameTimings[0].cpuRenderThreadFrameTime, frameTimings[0].gpuFrameTime);
        }

        int shown = 0;
        for (int i = 0; i < markers.Count && shown < 14; i++)
        {
            double avg = markers[i].sumMs / Math.Max(1, frames);
            if (avg < 0.3) break;
            if (IsNoiseMarker(markers[i].name)) continue;
            hotBuilder.AppendFormat("{0:F1} {1}\n", avg, markers[i].name);
            shown++;
        }

        for (int i = 0; i < markers.Count; i++) markers[i].sumMs = 0;

        // 우리 코드 구간 측정 (프레임당 ms / 프레임당 호출 수)
        string probes = "";
        var pb = new StringBuilder(256);
        pb.Append("[code ms/frame (calls/frame)]\n");
        for (int id = 0; id < (int)PerfProbe.Id.Count; id++)
        {
            PerfProbe.Drain(id, out double ms, out int calls);
            pb.AppendFormat("{0:F2} ({1:F0}) {2}\n", ms / Math.Max(1, frames), calls / (double)Math.Max(1, frames), (PerfProbe.Id)id);
        }
        probes = pb.ToString();
        return probes + hotBuilder.ToString();
    }

    private void OnDestroy()
    {
        for (int i = 0; i < markers.Count; i++) markers[i].recorder.Dispose();
        markers.Clear();
        Barrier.DebugInvincible = false;

        // URP 에셋은 에디터에서 값이 남으므로 원래 값으로 되돌린다.
        var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
        if (urp != null && defaultsCaptured)
        {
            urp.supportsHDR = defaultHdr;
            urp.renderScale = defaultRenderScale;
        }
    }

    private int fixedStepsThisSecond;

    private void FixedUpdate()
    {
        fixedStepsThisSecond++;
    }

    private void Update()
    {
        if (gm == null) gm = GameManager.Instance;

        UpdateStats();
        TickPerfToggles();
        if (autoplay) RunAutoplay();
        if (stageLabel != null && gm != null)
            stageLabel.text = "Stage " + gm.DebugStage + (gm.DebugInCombat ? "  (combat)" : "  (prepare)");
    }

    // ---------------------------------------------------------------- 통계
    private void UpdateStats()
    {
        float dt = Time.unscaledDeltaTime;
        statFrames++;
        SampleMarkers();
        statTimer += dt;
        if (dt > statMaxFrame) statMaxFrame = dt;

        if (statTimer < 1f) return;

        int gc = GC.CollectionCount(0);
        statGcCount = gc - lastGcCollections;
        lastGcCollections = gc;

        float fps = statFrames / statTimer;
        int monsters = FindObjectsByType<Monster>(FindObjectsSortMode.None).Length;
        int projectiles = FindObjectsByType<Projectile>(FindObjectsSortMode.None).Length
                          + FindObjectsByType<MonsterProjectile>(FindObjectsSortMode.None).Length;
        float memMb = GC.GetTotalMemory(false) / (1024f * 1024f);

        float stepsPerFrame = fixedStepsThisSecond / (float)Mathf.Max(1, statFrames);
        fixedStepsThisSecond = 0;
        statsCache = string.Format("{0:F0} fps  avg {1:F1} ms  max {2:F1} ms\nmon {3}  proj {4}  gc {5}/s  heap {6:F0} MB\nspeed x{7:F0}  physics steps/frame {8:F1}",
            fps, 1000f / Mathf.Max(0.001f, fps), statMaxFrame * 1000f, monsters, projectiles, statGcCount, memMb, Time.timeScale, stepsPerFrame);
        if (statsText != null) statsText.text = statsCache;
        if (hotText != null) hotText.text = BuildHotText(statFrames);

        statTimer = 0f;
        statFrames = 0;
        statMaxFrame = 0f;
    }

    // ---------------------------------------------------------------- 자동 진행
    private void RunAutoplay()
    {
        if (gm == null) return;

        autoTimer += Time.unscaledDeltaTime;
        if (autoTimer < 0.6f) return;
        autoTimer = 0f;

        if (gm.DebugIsGameOver) return;
        if (gm.DebugEnhanceOpen)
        {
            gm.DebugPickEnhance(0);
            return;
        }

        gm.DebugClickNext();
    }

    // ---------------------------------------------------------------- 동작
    private void FillBag(int grade)
    {
        if (gm == null) return;
        if (gm.DebugInCombat)
        {
            SetInfo("Fill bag only works in the prepare phase.");
            return;
        }

        int placed = gm.DebugInventory.DebugFillBagWithWeapons(grade);
        SetInfo("Placed " + placed + " grade-" + grade + " weapons.");
    }

    private void ToggleInvincible()
    {
        Barrier.DebugInvincible = !Barrier.DebugInvincible;
        if (invincibleLabel != null) invincibleLabel.text = "Barrier invincible: " + (Barrier.DebugInvincible ? "ON" : "OFF");
    }

    private void ToggleAutoplay()
    {
        autoplay = !autoplay;
        if (autoLabel != null) autoLabel.text = "Autoplay: " + (autoplay ? "ON" : "OFF");
    }

    private void SetInfo(string message)
    {
        if (infoLabel != null) infoLabel.text = message;
    }

    // ---------------------------------------------------------------- UI 만들기
    private void BuildUi()
    {
        var canvasGo = new GameObject("DevToolsCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        // 이벤트 시스템이 없으면 만든다(보통 씬에 이미 있다).
        if (FindFirstObjectByType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));

        RectTransform root = (RectTransform)canvasGo.transform;

        // 항상 보이는 통계 텍스트 (왼쪽 위, 노치를 피해 아래로 내림)
        statsText = CreateText(root, "Stats", 26, TextAnchor.UpperLeft, new Color(1f, 1f, 0.4f, 1f));
        var statsRt = statsText.rectTransform;
        statsRt.anchorMin = statsRt.anchorMax = new Vector2(0f, 1f);
        statsRt.pivot = new Vector2(0f, 1f);
        statsRt.anchoredPosition = new Vector2(190f, -150f);
        statsRt.sizeDelta = new Vector2(760f, 130f);
        statsText.raycastTarget = false;
        statsText.text = "...";
        AddOutline(statsText);

        hotText = CreateText(root, "Hot", 20, TextAnchor.UpperLeft, new Color(0.7f, 1f, 1f, 1f));
        var hotRt = hotText.rectTransform;
        hotRt.anchorMin = hotRt.anchorMax = new Vector2(0f, 1f);
        hotRt.pivot = new Vector2(0f, 1f);
        hotRt.anchoredPosition = new Vector2(190f, -290f);
        hotRt.sizeDelta = new Vector2(700f, 520f);
        hotText.raycastTarget = false;
        hotText.text = "";
        AddOutline(hotText);

        // DEV 토글 버튼
        Button toggle = CreateButton(root, "DEV", new Vector2(150f, 80f), new Color(0.1f, 0.1f, 0.1f, 0.75f), 30);
        var toggleRt = (RectTransform)toggle.transform;
        toggleRt.anchorMin = toggleRt.anchorMax = new Vector2(0f, 1f);
        toggleRt.pivot = new Vector2(0f, 1f);
        toggleRt.anchoredPosition = new Vector2(24f, -150f);
        // 밸런싱 툴 패널(탭: 스테이지 / 영구강화 / 특성 / 장비 / 도구). 도구 탭에는 아래의 기존 기능을 만든다.
        balance = DevBalancePanel.Create(root);
        toggle.onClick.AddListener(() => balance.gameObject.SetActive(!balance.gameObject.activeSelf));
        panel = balance.ToolsContent.gameObject;

        stageLabel = AddLabel(panel.transform, "Stage -", 30, new Color(0.7f, 1f, 0.7f, 1f), 44f);

        AddRowButton("Fill bag: grade 5 weapons", () => FillBag(5), new Color(0.45f, 0.3f, 0.6f, 1f));
        AddRowButton("Fill bag: grade 3 weapons", () => FillBag(3), new Color(0.35f, 0.3f, 0.5f, 1f));

        Button inv = AddRowButton("Barrier invincible: OFF", ToggleInvincible, new Color(0.6f, 0.3f, 0.3f, 1f));
        invincibleLabel = inv.GetComponentInChildren<Text>();
        Button auto = AddRowButton("Autoplay: OFF", ToggleAutoplay, new Color(0.3f, 0.5f, 0.3f, 1f));
        autoLabel = auto.GetComponentInChildren<Text>();

        AddLabel(panel.transform, "Perf bisect (turn OFF one at a time)", 22, new Color(1f, 0.8f, 0.4f, 1f), 30f);
        AddPerfToggle("Item UI fx", () => perfItemFx, v => perfItemFx = v);
        AddPerfToggle("Sprite shadows", () => perfShadows, v => perfShadows = v);
        AddPerfToggle("Monster animators", () => perfAnimators, v => perfAnimators = v);
        AddPerfToggle("Player firing", () => perfFiring, v => perfFiring = v);
        AddPerfToggle("Proj shadows", () => perfProjShadows, v => perfProjShadows = v);
        AddPerfToggle("Proj sprites", () => perfProjSprites, v => perfProjSprites = v);
        AddPerfToggle("Proj colliders", () => perfProjColliders, v => perfProjColliders = v);
        AddPerfToggle("HDR", () => perfHdr, v => perfHdr = v);
        AddPerfToggle("Render scale 1.0 (OFF=0.5)", () => perfFullRes, v => perfFullRes = v);

        infoLabel = AddLabel(panel.transform, "", 22, new Color(1f, 0.85f, 0.5f, 1f), 56f);
    }

    // ---------------------------------------------------------------- 성능 원인 분리 스위치
    private bool perfItemFx = true;
    private bool perfShadows = true;
    private bool perfAnimators = true;
    private bool perfFiring = true;
    private bool perfProjShadows = true;
    private bool perfProjSprites = true;
    private bool perfProjColliders = true;
    private bool perfHdr = true;
    private bool perfFullRes = true;
    private float perfTimer;
    private bool defaultsCaptured;
    private bool defaultHdr;
    private float defaultRenderScale = 1f;

    private void AddPerfToggle(string name, Func<bool> get, Action<bool> set)
    {
        Text label = null;
        Button b = AddRowButton(name + ": ON", () =>
        {
            set(!get());
            label.text = name + ": " + (get() ? "ON" : "OFF");
            ApplyPerfToggles(true);
        }, new Color(0.3f, 0.4f, 0.55f, 1f));
        label = b.GetComponentInChildren<Text>();
    }

    private void TickPerfToggles()
    {
        // 새로 생성되는 몬스터/아이템에도 적용되도록 주기적으로 다시 적용한다.
        perfTimer += Time.unscaledDeltaTime;
        if (perfTimer < 0.5f) return;
        perfTimer = 0f;
        if (!perfItemFx || !perfShadows || !perfAnimators || !perfFiring || !perfProjShadows || !perfProjSprites || !perfProjColliders)
            ApplyPerfToggles(false);
    }

    private void ApplyPerfToggles(bool restore)
    {
        var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
        if (urp != null)
        {
            if (!defaultsCaptured)
            {
                defaultsCaptured = true;
                defaultHdr = urp.supportsHDR;
                defaultRenderScale = urp.renderScale;
            }
            urp.supportsHDR = perfHdr && defaultHdr;
            urp.renderScale = perfFullRes ? defaultRenderScale : 0.5f;
        }

        if (!perfItemFx || restore)
        {
            foreach (Item item in FindObjectsByType<Item>(FindObjectsSortMode.None))
                item.enabled = perfItemFx;
            foreach (ItemSparkle sparkle in FindObjectsByType<ItemSparkle>(FindObjectsSortMode.None))
                sparkle.enabled = perfItemFx;
        }

        if (!perfShadows || restore)
        {
            foreach (SpriteShadow shadow in FindObjectsByType<SpriteShadow>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                shadow.enabled = perfShadows;
                Transform child = shadow.transform.Find("Shadow");
                if (child != null) child.gameObject.SetActive(perfShadows);
            }
        }

        if (!perfAnimators || restore)
        {
            var monsters = Monster.Active;
            for (int i = 0; i < monsters.Count; i++)
            {
                Animator[] anims = monsters[i].GetComponentsInChildren<Animator>();
                for (int a = 0; a < anims.Length; a++)
                    anims[a].enabled = perfAnimators;
            }
        }

        if (!perfProjShadows || !perfProjSprites || !perfProjColliders || restore)
        {
            foreach (Projectile proj in FindObjectsByType<Projectile>(FindObjectsSortMode.None))
            {
                if (!perfProjShadows || restore)
                {
                    foreach (SpriteShadow sh in proj.GetComponentsInChildren<SpriteShadow>(true))
                    {
                        sh.enabled = perfProjShadows;
                        Transform child = sh.transform.Find("Shadow");
                        if (child != null) child.gameObject.SetActive(perfProjShadows);
                    }
                }

                if (!perfProjSprites || restore)
                {
                    foreach (SpriteRenderer sr in proj.GetComponentsInChildren<SpriteRenderer>(true))
                    {
                        if (sr.gameObject.name == "Shadow") continue;
                        sr.enabled = perfProjSprites;
                    }
                }

                if (!perfProjColliders || restore)
                {
                    foreach (Collider2D col in proj.GetComponentsInChildren<Collider2D>(true))
                        col.enabled = perfProjColliders;
                }
            }
        }

        if (!perfFiring || restore)
        {
            PlayerShooter shooter = FindFirstObjectByType<PlayerShooter>();
            if (shooter != null) shooter.enabled = perfFiring;
        }
    }

    private Button AddRowButton(string label, UnityEngine.Events.UnityAction onClick, Color color)
    {
        Button b = CreateButton(panel.transform, label, new Vector2(0f, 84f), color, 28);
        var le = b.gameObject.AddComponent<LayoutElement>();
        le.minHeight = 84f;
        le.preferredHeight = 84f;
        b.onClick.AddListener(onClick);
        return b;
    }

    private Text AddLabel(Transform parent, string text, int size, Color color, float height)
    {
        Text t = CreateText(parent, "Label", size, TextAnchor.MiddleLeft, color);
        t.text = text;
        t.raycastTarget = false;
        var le = t.gameObject.AddComponent<LayoutElement>();
        le.minHeight = height;
        le.preferredHeight = height;
        return t;
    }

    private static Font cachedFont;

    private static Font GetFont()
    {
        if (cachedFont == null)
            cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return cachedFont;
    }

    private static Text CreateText(Transform parent, string name, int size, TextAnchor anchor, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font = GetFont();
        t.fontSize = size;
        t.alignment = anchor;
        t.color = color;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    private static void AddOutline(Text t)
    {
        var o = t.gameObject.AddComponent<Outline>();
        o.effectColor = new Color(0f, 0f, 0f, 0.9f);
        o.effectDistance = new Vector2(2f, -2f);
    }

    private static Button CreateButton(Transform parent, string label, Vector2 size, Color color, int fontSize)
    {
        var go = new GameObject("Button_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = size;
        var img = go.GetComponent<Image>();
        img.color = color;
        var b = go.GetComponent<Button>();
        b.targetGraphic = img;

        Text t = CreateText(go.transform, "Text", fontSize, TextAnchor.MiddleCenter, Color.white);
        t.text = label;
        t.raycastTarget = false;
        var trt = t.rectTransform;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;
        return b;
    }
}
#endif
