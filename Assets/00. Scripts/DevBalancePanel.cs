#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 개발 빌드 / 에디터 전용 밸런싱 툴 패널(DevTools의 DEV 버튼으로 연다). 릴리스 빌드에는 포함되지 않는다.
///
/// 탭: 스테이지(1~100 입력 후 이동) / 영구강화(레벨 -+) / 특성(레벨업 강화 옵션 스택 -+) /
///     장비(원하는 등급으로 가방에 넣고 빼기) / 도구(DevTools가 채우는 기존 성능·자동 진행 기능)
/// </summary>
internal sealed class DevBalancePanel : MonoBehaviour
{
    private enum Tab { Stage, Permanent, Traits, Items, Tools }

    private static readonly string[] TabLabels = { "스테이지", "영구강화", "특성", "장비", "도구" };

    private const int MinStage = 1;
    private const int MaxStage = 100;
    private const int MaxGradeSelect = 5;

    // 영구강화 6종과 값 단위.
    private static readonly string[] PermIds =
    {
        TitleEnhanceProgress.EfficiencyId,
        TitleEnhanceProgress.MagicId,
        TitleEnhanceProgress.ProductivityId,
        TitleEnhanceProgress.DiscardId,
        TitleEnhanceProgress.BarrierId,
        TitleEnhanceProgress.ChoiceCountId
    };

    private static readonly string[] PermUnits = { "%", "%", "%", "회", "%", "개" };

    private readonly ScrollRect[] scrolls = new ScrollRect[5];
    private readonly RectTransform[] contents = new RectTransform[5];
    private readonly Button[] tabButtons = new Button[5];
    private Tab current = Tab.Stage;
    private TextMeshProUGUI info;
    private GameManager gm;
    private float timer;

    // 스테이지 탭
    private TextMeshProUGUI stageStatus;
    private InputField stageInput;

    // 영구강화 탭
    private readonly TextMeshProUGUI[] permValueLabels = new TextMeshProUGUI[PermIds.Length];

    // 특성 탭
    private TextMeshProUGUI traitSummary;
    private readonly List<string> traitIds = new();
    private readonly List<TextMeshProUGUI> traitCountLabels = new();

    // 장비 탭
    private int selectedGrade = 1;
    private TextMeshProUGUI gradeLabel;
    private RectTransform prefabListRoot;
    private RectTransform bagListRoot;
    private bool prefabRowsBuilt;
    private int bagSignature = int.MinValue;

    /// <summary>"도구" 탭의 내용 영역. DevTools가 기존 성능/자동 진행 UI를 여기에 만든다.</summary>
    public RectTransform ToolsContent => contents[(int)Tab.Tools];

    // ---------------------------------------------------------------- 만들기
    public static DevBalancePanel Create(Transform canvasRoot)
    {
        var go = new GameObject("BalancePanel", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(canvasRoot, false);

        // 폭은 화면에 맞춰 늘이고 줄인다(세로로 긴 폰은 캔버스 폭이 1080보다 좁다). 높이만 고정.
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -250f);
        rt.sizeDelta = new Vector2(-24f, 1180f);
        // 뒤의 게임 UI와 성능 통계가 비치면 읽기 어려워서 완전히 불투명하게 한다.
        go.GetComponent<Image>().color = new Color(0.07f, 0.08f, 0.12f, 1f);

        var panel = go.AddComponent<DevBalancePanel>();
        panel.Build();
        go.SetActive(false);
        return panel;
    }

    private void Build()
    {
        DevUi.ResolveFont();

        // 탭 바
        var tabBar = new GameObject("TabBar", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        tabBar.transform.SetParent(transform, false);
        var tabRt = (RectTransform)tabBar.transform;
        tabRt.anchorMin = new Vector2(0f, 1f);
        tabRt.anchorMax = new Vector2(1f, 1f);
        tabRt.pivot = new Vector2(0.5f, 1f);
        tabRt.anchoredPosition = new Vector2(0f, -8f);
        tabRt.sizeDelta = new Vector2(-16f, 84f);

        var h = tabBar.GetComponent<HorizontalLayoutGroup>();
        h.spacing = 6f;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = true;
        h.childForceExpandHeight = true;

        for (int i = 0; i < TabLabels.Length; i++)
        {
            Tab tab = (Tab)i;
            tabButtons[i] = DevUi.Button(tabBar.transform, TabLabels[i], DevUi.Neutral, 30, () => Select(tab), -1f, 84f);
        }

        // 아래 정보 줄
        info = DevUi.Text(transform, "", 24, DevUi.Warn);
        var infoRt = info.rectTransform;
        infoRt.anchorMin = new Vector2(0f, 0f);
        infoRt.anchorMax = new Vector2(1f, 0f);
        infoRt.pivot = new Vector2(0.5f, 0f);
        infoRt.anchoredPosition = new Vector2(0f, 6f);
        infoRt.sizeDelta = new Vector2(-24f, 62f);

        // 탭마다 스크롤 영역 하나
        for (int i = 0; i < scrolls.Length; i++)
        {
            scrolls[i] = DevUi.Scroll(transform, out contents[i]);
            var srt = (RectTransform)scrolls[i].transform;
            srt.offsetMin = new Vector2(8f, 74f);
            srt.offsetMax = new Vector2(-8f, -100f);
        }

        BuildStage(contents[(int)Tab.Stage]);
        BuildPermanent(contents[(int)Tab.Permanent]);
        BuildTraits(contents[(int)Tab.Traits]);
        BuildItems(contents[(int)Tab.Items]);

        Select(Tab.Stage);
    }

    private void OnEnable()
    {
        if (scrolls[0] != null) RefreshCurrent();
    }

    private void Update()
    {
        timer += Time.unscaledDeltaTime;
        if (timer < 0.25f) return;
        timer = 0f;

        switch (current)
        {
            case Tab.Stage: UpdateStageStatus(); break;
            case Tab.Traits: RefreshTraits(); break;
            case Tab.Items: RefreshItems(false); break;
        }
    }

    private void Select(Tab tab)
    {
        current = tab;
        for (int i = 0; i < scrolls.Length; i++)
        {
            scrolls[i].gameObject.SetActive(i == (int)tab);
            var img = tabButtons[i].GetComponent<Image>();
            if (img != null) img.color = i == (int)tab ? DevUi.Accent : DevUi.Neutral;
        }

        RefreshCurrent();
    }

    private void RefreshCurrent()
    {
        switch (current)
        {
            case Tab.Stage: UpdateStageStatus(); break;
            case Tab.Permanent: RefreshPermanent(); break;
            case Tab.Traits: RefreshTraits(); break;
            case Tab.Items: RefreshItems(true); break;
        }
    }

    private bool EnsureGm()
    {
        if (gm == null) gm = GameManager.Instance;
        if (gm != null) return true;

        Info("GameManager를 찾을 수 없어요(게임 씬에서만 동작해요).");
        return false;
    }

    private void Info(string message)
    {
        if (info != null) info.text = message;
    }

    // ---------------------------------------------------------------- 스테이지 탭
    private void BuildStage(RectTransform c)
    {
        stageStatus = DevUi.Text(c, "-", 30, DevUi.Good);
        DevUi.Layout(stageStatus, height: 52f);

        DevUi.Header(c, "스테이지 이동 (1~100)");
        RectTransform row = DevUi.Row(c, 96);
        DevUi.Button(row, "-10", DevUi.Neutral, 30, () => AdjustStage(-10), 120f, 84f);
        DevUi.Button(row, "-1", DevUi.Neutral, 30, () => AdjustStage(-1), 100f, 84f);
        stageInput = DevUi.IntInput(row, "80", 260f, 84f, 3);
        DevUi.Button(row, "+1", DevUi.Neutral, 30, () => AdjustStage(1), 100f, 84f);
        DevUi.Button(row, "+10", DevUi.Neutral, 30, () => AdjustStage(10), 120f, 84f);

        DevUi.Button(c, "이 스테이지로 이동", DevUi.Accent, 34, GoToInputStage, -1f, 100f);

        DevUi.Header(c, "바로가기");
        PresetRow(c, 1, 10, 25, 50, 60);
        PresetRow(c, 70, 80, 90, 95, 100);

        DevUi.Header(c, "골드");
        RectTransform gold = DevUi.Row(c, 84);
        DevUi.Button(gold, "+1,000", DevUi.Plus, 28, () => AddGold(1000), -1f, 76f);
        DevUi.Button(gold, "+10,000", DevUi.Plus, 28, () => AddGold(10000), -1f, 76f);
        DevUi.Button(gold, "+100,000", DevUi.Plus, 28, () => AddGold(100000), -1f, 76f);

        DevUi.Note(c, "이동하면 준비 단계로 가요. 가방 확장 단계는 건너뛰고 골드는 최소 3,000으로 맞춰요. 전투 중이어도 몬스터를 정리하고 이동해요.", 90f);
    }

    private void PresetRow(RectTransform c, params int[] stages)
    {
        RectTransform row = DevUi.Row(c, 84);
        for (int i = 0; i < stages.Length; i++)
        {
            int stage = stages[i];
            DevUi.Button(row, stage.ToString(), DevUi.Neutral, 30, () =>
            {
                stageInput.text = stage.ToString();
                JumpTo(stage);
            }, -1f, 76f);
        }
    }

    private int ReadStage()
    {
        if (!int.TryParse(stageInput.text, out int value))
            value = gm != null ? gm.DebugStage : MinStage;

        return Mathf.Clamp(value, MinStage, MaxStage);
    }

    private void AdjustStage(int delta)
    {
        stageInput.text = Mathf.Clamp(ReadStage() + delta, MinStage, MaxStage).ToString();
    }

    private void GoToInputStage()
    {
        int stage = ReadStage();
        stageInput.text = stage.ToString();
        JumpTo(stage);
    }

    private void JumpTo(int stage)
    {
        if (!EnsureGm()) return;

        gm.DebugJumpToStage(stage);
        Info("스테이지 " + gm.DebugStage + "(으)로 이동했어요. 준비 단계예요.");
        UpdateStageStatus();
    }

    private void AddGold(int amount)
    {
        if (!EnsureGm()) return;

        gm.DebugAddGold(amount);
        Info("골드 +" + amount.ToString("N0"));
        UpdateStageStatus();
    }

    private void UpdateStageStatus()
    {
        if (gm == null) gm = GameManager.Instance;
        if (stageStatus == null) return;

        stageStatus.text = gm == null
            ? "게임 씬이 아니에요"
            : "현재 스테이지 " + gm.DebugStage + (gm.DebugInCombat ? " (전투 중)" : " (준비 단계)") + "   골드 " + gm.Gold.ToString("N0");
    }

    // ---------------------------------------------------------------- 영구강화 탭
    private void BuildPermanent(RectTransform c)
    {
        DevUi.Note(c, "타이틀의 영구강화 레벨을 직접 바꿔요. 이 기기의 저장 데이터(PlayerPrefs)에 바로 저장되니, 끝나면 \"전체 0레벨\"로 되돌려 두세요.", 90f);

        for (int i = 0; i < PermIds.Length; i++)
        {
            int index = i;
            RectTransform row = DevUi.Row(c, 96, true);

            TextMeshProUGUI name = DevUi.Text(row, PermName(PermIds[i]), 28, Color.white);
            DevUi.Layout(name, flexibleWidth: 1f);

            permValueLabels[i] = DevUi.Text(row, "", 26, DevUi.Good, TextAlignmentOptions.MidlineRight);
            DevUi.Layout(permValueLabels[i], width: 330f);

            DevUi.Button(row, "-", DevUi.Minus, 40, () => ChangePermanent(index, -1), 90f, 80f);
            DevUi.Button(row, "+", DevUi.Plus, 40, () => ChangePermanent(index, 1), 90f, 80f);
        }

        RectTransform all = DevUi.Row(c, 90);
        DevUi.Button(all, "전체 0레벨", DevUi.Minus, 30, () => SetAllPermanent(false), -1f, 80f);
        DevUi.Button(all, "전체 MAX", DevUi.Plus, 30, () => SetAllPermanent(true), -1f, 80f);

        DevUi.Note(c, "방어막은 최대 체력을 다시 계산하고 체력을 가득 채워요. 버리기 횟수는 바로 늘거나 줄어요. 효율, 마법력, 생산성, 선택지 수는 다음 사용부터 적용돼요.", 100f);
    }

    private static string PermName(string id)
    {
        if (GameDataRepository.TryGetReinforce(id, out ReinforceData data) && !string.IsNullOrWhiteSpace(data.Name))
        {
            if (GameDataRepository.TryGetLocalization(data.Name, out string localized))
                return localized;

            return data.Name;
        }

        return id;
    }

    private void RefreshPermanent()
    {
        for (int i = 0; i < PermIds.Length; i++)
        {
            if (permValueLabels[i] == null) continue;

            string id = PermIds[i];
            int level = TitleEnhanceProgress.GetLevel(id);
            int max = TitleEnhanceProgress.GetMaxLevel(id);
            int value = TitleEnhanceProgress.GetValueAtLevel(id, level);
            string sign = PermUnits[i] == "%" ? "+" : string.Empty;
            permValueLabels[i].text = "Lv " + level + "/" + max + "   (" + sign + value + PermUnits[i] + ")";
        }
    }

    private void ChangePermanent(int index, int delta)
    {
        string id = PermIds[index];
        int old = TitleEnhanceProgress.GetLevel(id);
        int next = Mathf.Clamp(old + delta, 0, TitleEnhanceProgress.GetMaxLevel(id));
        if (next == old) return;

        ApplyPermanent(() => TitleEnhanceProgress.SetLevel(id, next));
        Info(PermName(id) + " Lv " + old + " → " + next);
    }

    private void SetAllPermanent(bool max)
    {
        ApplyPermanent(() =>
        {
            for (int i = 0; i < PermIds.Length; i++)
                TitleEnhanceProgress.SetLevel(PermIds[i], max ? TitleEnhanceProgress.GetMaxLevel(PermIds[i]) : 0);
        });
        Info(max ? "영구강화를 전부 MAX로 올렸어요." : "영구강화를 전부 0레벨로 내렸어요.");
    }

    private void ApplyPermanent(Action change)
    {
        int discardBefore = TitleEnhanceProgress.CurrentDiscardCount;
        change();
        int discardAfter = TitleEnhanceProgress.CurrentDiscardCount;

        if (gm == null) gm = GameManager.Instance;
        if (gm != null) gm.DebugOnPermanentEnhanceChanged(discardAfter - discardBefore);

        RefreshPermanent();
    }

    // ---------------------------------------------------------------- 특성 탭
    private void BuildTraits(RectTransform c)
    {
        traitSummary = DevUi.Text(c, "", 26, DevUi.Good);
        DevUi.Layout(traitSummary, height: 84f);

        DevUi.Button(c, "쌓은 특성 모두 해제", DevUi.Minus, 30, ClearTraits, -1f, 80f);
        DevUi.Note(c, "레벨업 때 고르는 강화 옵션을 원하는 만큼 쌓거나 내려요. 실제 플레이에서 고른 옵션도 같이 반영돼요.", 70f);

        IReadOnlyList<EnhanceOptionData> options = GameDataRepository.AllEnhanceOptions;
        for (int i = 0; i < options.Count; i++)
        {
            EnhanceOptionData option = options[i];
            string id = option.Id;
            RectTransform row = DevUi.Row(c, 112, true);

            TextMeshProUGUI desc = DevUi.Text(row, EnhancePanelFiller.DescribeOption(option), 24, Color.white);
            DevUi.Layout(desc, flexibleWidth: 1f);

            TextMeshProUGUI count = DevUi.Text(row, "x0", 30, DevUi.Good, TextAlignmentOptions.Center);
            DevUi.Layout(count, width: 90f);

            DevUi.Button(row, "-", DevUi.Minus, 40, () => ChangeTrait(id, -1), 90f, 80f);
            DevUi.Button(row, "+", DevUi.Plus, 40, () => ChangeTrait(id, 1), 90f, 80f);

            traitIds.Add(id);
            traitCountLabels.Add(count);
        }

        if (options.Count == 0)
            DevUi.Note(c, "강화 옵션 데이터가 없어요(GameData를 불러오지 못했어요).", 64f);
    }

    private void ChangeTrait(string optionId, int delta)
    {
        if (!EnsureGm()) return;

        gm.DebugChangeEnhanceOption(optionId, delta);
        RefreshTraits();
    }

    private void ClearTraits()
    {
        if (!EnsureGm()) return;

        gm.DebugClearEnhanceOptions();
        RefreshTraits();
        Info("쌓은 특성을 모두 해제했어요.");
    }

    private void RefreshTraits()
    {
        if (gm == null) gm = GameManager.Instance;

        for (int i = 0; i < traitIds.Count; i++)
        {
            int count = gm != null ? gm.DebugGetEnhanceCount(traitIds[i]) : 0;
            traitCountLabels[i].text = "x" + count;
            traitCountLabels[i].color = count > 0 ? DevUi.Good : new Color(1f, 1f, 1f, 0.5f);
        }

        if (traitSummary == null) return;
        if (gm == null)
        {
            traitSummary.text = string.Empty;
            return;
        }

        EnhanceBonusState b = gm.EnhanceBonuses;
        traitSummary.text =
            "공격력 전체 +" + b.AttackUpAll.ToString("0.##") + "%   공속 전체 +" + b.AttackSpeedUpAll.ToString("0.##") + "%\n" +
            "속성 공격력  물 " + b.AttackUpWater.ToString("0.#") + "  바위 " + b.AttackUpRock.ToString("0.#") +
            "  얼음 " + b.AttackUpIce.ToString("0.#") + "  바람 " + b.AttackUpWind.ToString("0.#") +
            "  불 " + b.AttackUpFire.ToString("0.#") + "  암 " + b.AttackUpDark.ToString("0.#");
    }

    // ---------------------------------------------------------------- 장비 탭
    private void BuildItems(RectTransform c)
    {
        DevUi.Header(c, "넣을 장비 등급");
        RectTransform gradeRow = DevUi.Row(c, 96);
        DevUi.Button(gradeRow, "-", DevUi.Minus, 40, () => ChangeSelectedGrade(-1), 110f, 84f);
        gradeLabel = DevUi.Text(gradeRow, "", 34, Color.white, TextAlignmentOptions.Center);
        DevUi.Layout(gradeLabel, flexibleWidth: 1f);
        DevUi.Button(gradeRow, "+", DevUi.Plus, 40, () => ChangeSelectedGrade(1), 110f, 84f);
        UpdateGradeLabel();

        RectTransform bulk = DevUi.Row(c, 90);
        DevUi.Button(bulk, "무기 채우기", DevUi.Accent, 28, FillBag, -1f, 80f);
        DevUi.Button(bulk, "가방 비우기", DevUi.Minus, 28, ClearBag, -1f, 80f);

        DevUi.Header(c, "가방 안 장비 (등급 변경 / 빼기)");
        bagListRoot = NewList(c);

        DevUi.Header(c, "장비 추가 (눌러서 가방에 넣기)");
        prefabListRoot = NewList(c);

        DevUi.Note(c, "준비 단계에서만 바꿀 수 있어요. 가방에 자리가 없으면 들어가지 않아요.", 64f);
    }

    private static RectTransform NewList(Transform parent)
    {
        var go = new GameObject("List", typeof(RectTransform), typeof(VerticalLayoutGroup));
        go.transform.SetParent(parent, false);

        var v = go.GetComponent<VerticalLayoutGroup>();
        v.spacing = 6f;
        v.childControlWidth = true;
        v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        return (RectTransform)go.transform;
    }

    private void ChangeSelectedGrade(int delta)
    {
        selectedGrade = Mathf.Clamp(selectedGrade + delta, 1, MaxGradeSelect);
        UpdateGradeLabel();
    }

    private void UpdateGradeLabel()
    {
        if (gradeLabel != null) gradeLabel.text = "등급 " + selectedGrade;
    }

    private bool CanEditBag()
    {
        if (!EnsureGm()) return false;
        if (gm.DebugInventory == null) return false;

        if (gm.DebugInCombat)
        {
            Info("전투 중에는 장비를 바꿀 수 없어요. 준비 단계에서 해 주세요.");
            return false;
        }

        return true;
    }

    private void FillBag()
    {
        if (!CanEditBag()) return;

        int placed = gm.DebugInventory.DebugFillBagWithWeapons(selectedGrade);
        Info("등급 " + selectedGrade + " 무기 " + placed + "개를 넣었어요.");
        RefreshItems(true);
    }

    private void ClearBag()
    {
        if (!CanEditBag()) return;

        int removed = gm.DebugInventory.DebugClearBag();
        Info("가방에서 " + removed + "개를 뺐어요.");
        RefreshItems(true);
    }

    // 프리팹은 인스턴스와 달리 게임 데이터가 적용되기 전이라 DisplayName이 내부 이름이다. 데이터 시트에서 이름을 찾는다.
    private static string ItemName(Item prefab)
    {
        if (GameDataRepository.TryGetItemByPrefabKey(prefab.ShopBanKey, out ItemData data) &&
            !string.IsNullOrWhiteSpace(data.Name))
        {
            return data.Name;
        }

        return prefab.DisplayName;
    }

    private void AddItem(Item prefab)
    {
        if (!CanEditBag()) return;

        bool ok = gm.DebugInventory.DebugAddItem(prefab, selectedGrade);
        string name = ItemName(prefab);
        Info(ok
            ? name + " 등급 " + selectedGrade + "을(를) 넣었어요."
            : name + ": 가방에 자리가 없어요.");
        RefreshItems(true);
    }

    private void RemoveItem(Item item)
    {
        if (!CanEditBag() || item == null) return;

        string name = item.DisplayName;
        gm.DebugInventory.DebugRemoveItem(item);
        Info(name + "을(를) 뺐어요.");
        RefreshItems(true);
    }

    private void ChangeItemGrade(Item item, int delta)
    {
        if (!CanEditBag() || item == null) return;

        item.SetGrade(item.Grade + delta);
        RefreshItems(true);
    }

    private void EnsurePrefabRows()
    {
        if (prefabRowsBuilt) return;
        if (gm == null) gm = GameManager.Instance;
        if (gm == null || gm.DebugInventory == null) return;

        prefabRowsBuilt = true;
        var prefabs = new List<Item>();
        gm.DebugInventory.DebugGetItemPrefabs(prefabs);

        for (int i = 0; i < prefabs.Count; i++)
        {
            Item prefab = prefabs[i];
            RectTransform row = DevUi.Row(prefabListRoot, 88, true);

            TextMeshProUGUI name = DevUi.Text(row, ItemName(prefab) + "  (" + prefab.Category + ")", 26, Color.white);
            DevUi.Layout(name, flexibleWidth: 1f);
            DevUi.Button(row, "추가", DevUi.Plus, 28, () => AddItem(prefab), 150f, 76f);
        }
    }

    private int ComputeBagSignature(IReadOnlyList<Item> items)
    {
        int hash = items.Count;
        for (int i = 0; i < items.Count; i++)
        {
            hash = hash * 31 + items[i].GetInstanceID();
            hash = hash * 31 + items[i].Grade;
        }

        return hash;
    }

    private void RefreshItems(bool force)
    {
        if (gm == null) gm = GameManager.Instance;
        if (gm == null || gm.DebugInventory == null || bagListRoot == null) return;

        EnsurePrefabRows();

        IReadOnlyList<Item> items = gm.DebugInventory.GetBagItems();
        int signature = ComputeBagSignature(items);
        if (!force && signature == bagSignature) return;

        bagSignature = signature;
        DevUi.ClearChildren(bagListRoot);

        if (items.Count == 0)
        {
            TextMeshProUGUI empty = DevUi.Text(bagListRoot, "가방이 비어 있어요.", 24, new Color(1f, 1f, 1f, 0.5f));
            DevUi.Layout(empty, height: 56f);
            return;
        }

        for (int i = 0; i < items.Count; i++)
        {
            Item item = items[i];
            RectTransform row = DevUi.Row(bagListRoot, 88, true);

            TextMeshProUGUI label = DevUi.Text(row, item.DisplayName + "  G" + item.Grade, 26, Color.white);
            DevUi.Layout(label, flexibleWidth: 1f);

            DevUi.Button(row, "G-", DevUi.Minus, 26, () => ChangeItemGrade(item, -1), 90f, 76f);
            DevUi.Button(row, "G+", DevUi.Plus, 26, () => ChangeItemGrade(item, 1), 90f, 76f);
            DevUi.Button(row, "빼기", DevUi.Minus, 26, () => RemoveItem(item), 130f, 76f);
        }
    }
}
#endif
