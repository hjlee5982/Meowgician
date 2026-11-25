using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 몬스터별 체력 곡선을 "마지막 스테이지 체력 비율(%)" 하나로 조절하는 밸런싱 스크립트. 씬(하이어라키)에 하나만 둔다.
///
/// 배율 = (비율 / 100) ^ ((스테이지 - 1) / (마지막 스테이지 - 1))
///  - 1스테이지는 항상 원래 체력이다.
///  - 마지막 스테이지(기본 100)는 원래 체력 × 비율이다. 100이면 지금과 같고, 50이면 절반이 된다.
///  - 그 사이는 로그 척도에서 직선으로 이어진다. 그래서 체력이 지수적으로 늘어나는 곡선의
///    기울기(스테이지당 증가율)가 함께 낮아진다(또는 비율이 100보다 크면 가팔라진다).
///    예) 50%: 50스테이지는 약 71%, 75스테이지는 약 84% 체력.
///
/// 시트의 스테이지별 체력(또는 몬스터 기본 체력)에 곱해서 몬스터가 생성될 때 적용한다(Spawner).
/// 모든 몬스터가 100이면 아무것도 바뀌지 않는다.
///
/// 곡선 모드: 몬스터별로 "스테이지 → 체력" 점(그래프)을 직접 찍을 수 있다(인스펙터의 그래프에서 점을 끌어 조절).
/// 켜면 시트 값 대신 곡선의 체력을 쓰고 비율은 무시한다. 점 사이는 지수 곡선(로그 척도의 직선)으로 이어서,
/// 체력이 지수적으로 늘어나는 구조에 자연스럽게 맞는다. 첫 점 이전과 마지막 점 이후는 그 점의 체력을 유지한다.
/// </summary>
public class MonsterHpTuner : MonoBehaviour
{
    /// <summary>체력 곡선의 한 점: 이 스테이지의 체력.</summary>
    [Serializable]
    public struct HpPoint
    {
        public int stage;
        public float hp;

        public HpPoint(int stage, float hp)
        {
            this.stage = stage;
            this.hp = hp;
        }
    }

    [Serializable]
    public class Entry
    {
        [Tooltip("GameData 몬스터 시트의 ID")]
        public string monsterId;

        [Tooltip("표시용 이름(동작에는 영향 없음)")]
        public string displayName;

        [Tooltip("마지막 스테이지 체력의 비율(%). 100 = 지금과 같음, 50 = 절반(곡선 기울기도 완만해짐), 200 = 두 배.")]
        [Min(0.01f)]
        public float percentAtFinalStage = 100f;

        [Tooltip("켜면 시트 값 대신 아래 곡선(스테이지 → 체력)의 값을 쓴다. 이때 비율은 무시한다.")]
        public bool useHpCurve;

        [Tooltip("체력 곡선의 점들(스테이지 오름차순). 인스펙터의 그래프에서 끌어서 조절한다.")]
        public List<HpPoint> hpCurve = new();
    }

    [Tooltip("끄면 모든 조절을 무시하고 시트 값 그대로 쓴다.")]
    [SerializeField] private bool enableTuning = true;

    [Tooltip("비율이 그대로 적용되는 마지막 스테이지(게임의 마지막 스테이지 = 100). 1스테이지는 항상 원래 체력이다.")]
    [SerializeField, Min(2)] private int finalStage = 100;

    [SerializeField] private List<Entry> monsters = new();

    public static MonsterHpTuner Instance { get; private set; }

    public bool EnableTuning => enableTuning;
    public int FinalStage => Mathf.Max(2, finalStage);
    public List<Entry> Monsters => monsters;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>해당 몬스터의 스테이지별 체력 배율. 조절이 꺼져 있거나 목록에 없으면 1.</summary>
    public float GetMultiplier(string monsterId, int stage)
    {
        if (!enableTuning || string.IsNullOrWhiteSpace(monsterId)) return 1f;

        Entry entry = FindEntry(monsterId);
        if (entry == null) return 1f;

        float percent = Mathf.Max(0.01f, entry.percentAtFinalStage) / 100f;
        if (Mathf.Approximately(percent, 1f)) return 1f;

        float progress = Mathf.Clamp01((stage - 1) / (float)(FinalStage - 1));
        return Mathf.Pow(percent, progress);
    }

    /// <summary>
    /// 기준 체력에 배율을 곱한다. 배율이 1이면(조절 없음) false를 돌려주고 체력은 건드리지 않는다.
    /// </summary>
    public bool TryApply(string monsterId, int baseHp, int stage, out int tunedHp)
    {
        tunedHp = baseHp;

        // 곡선 모드: 시트 값과 상관없이 곡선의 체력을 쓴다.
        if (enableTuning && !string.IsNullOrWhiteSpace(monsterId))
        {
            Entry curveEntry = FindEntry(monsterId);
            if (curveEntry != null && curveEntry.useHpCurve && curveEntry.hpCurve != null && curveEntry.hpCurve.Count > 0)
            {
                tunedHp = EvaluateCurve(curveEntry.hpCurve, stage);
                return true;
            }
        }

        if (baseHp <= 0) return false;

        float multiplier = GetMultiplier(monsterId, stage);
        if (Mathf.Approximately(multiplier, 1f)) return false;

        double scaled = Math.Round(baseHp * (double)multiplier);
        tunedHp = (int)Math.Min(int.MaxValue, Math.Max(1d, scaled));
        return true;
    }

    /// <summary>
    /// 점(스테이지 오름차순)들로 이루어진 체력 곡선의 해당 스테이지 값. 점 사이는 지수 보간(로그 척도의 직선),
    /// 첫 점 이전과 마지막 점 이후는 그 점의 체력을 유지한다. 점이 없으면 0.
    /// </summary>
    public static int EvaluateCurve(List<HpPoint> points, int stage)
    {
        if (points == null || points.Count == 0) return 0;

        HpPoint first = points[0];
        HpPoint last = points[points.Count - 1];
        double value;

        if (stage <= first.stage || points.Count == 1)
        {
            value = first.hp;
        }
        else if (stage >= last.stage)
        {
            value = last.hp;
        }
        else
        {
            value = last.hp;
            for (int i = 0; i < points.Count - 1; i++)
            {
                HpPoint a = points[i];
                HpPoint b = points[i + 1];
                if (stage < a.stage || stage > b.stage) continue;

                double span = Math.Max(1, b.stage - a.stage);
                double t = (stage - a.stage) / span;
                double lnA = Math.Log(Math.Max(1d, a.hp));
                double lnB = Math.Log(Math.Max(1d, b.hp));
                value = Math.Exp(lnA + (lnB - lnA) * t);
                break;
            }
        }

        return (int)Math.Min(int.MaxValue, Math.Max(1d, Math.Round(value)));
    }

    /// <summary>점들을 스테이지 오름차순으로 정렬하고, 같은 스테이지의 중복을 없애고, 체력을 1 이상으로 맞춘다.</summary>
    public static void NormalizeCurve(List<HpPoint> points, int maxStage)
    {
        if (points == null) return;

        for (int i = 0; i < points.Count; i++)
        {
            HpPoint p = points[i];
            p.stage = Mathf.Clamp(p.stage, 1, Mathf.Max(1, maxStage));
            p.hp = Mathf.Max(1f, p.hp);
            points[i] = p;
        }

        points.Sort((a, b) => a.stage.CompareTo(b.stage));
        for (int i = points.Count - 1; i > 0; i--)
        {
            if (points[i].stage == points[i - 1].stage)
                points.RemoveAt(i);
        }
    }

    private Entry FindEntry(string monsterId)
    {
        for (int i = 0; i < monsters.Count; i++)
        {
            Entry entry = monsters[i];
            if (entry != null && string.Equals(entry.monsterId, monsterId, StringComparison.OrdinalIgnoreCase))
                return entry;
        }

        return null;
    }

    /// <summary>
    /// 스테이지 시트에서 이 몬스터가 해당 스테이지에 나올 때의 (조절 전) 체력. 나오지 않으면 false.
    /// 인스펙터 미리보기용이다.
    /// </summary>
    public static bool TryGetBaseHp(string monsterId, int stage, out int hp)
    {
        hp = 0;
        if (!GameDataRepository.TryGetStageByOrder(stage - 1, out StageData stageData)) return false;

        List<StageSpawnEntry> entries = GameDataRepository.GetStageSpawnEntries(stageData.Id);
        for (int i = 0; i < entries.Count; i++)
        {
            StageSpawnEntry entry = entries[i];
            if (!string.Equals(entry.MonsterId, monsterId, StringComparison.OrdinalIgnoreCase)) continue;

            hp = entry.Hp;
            if (hp <= 0 && GameDataRepository.TryGetMonsterById(monsterId, out MonsterData data))
                hp = data.Hp;

            return hp > 0;
        }

        return false;
    }

    /// <summary>GameData의 몬스터 목록으로 비어 있는 항목을 채운다(이미 있는 항목의 비율은 그대로 둔다).</summary>
    [ContextMenu("몬스터 목록 채우기")]
    public void FillFromGameData()
    {
        var found = new List<MonsterData>();
        foreach (KeyValuePair<string, MonsterData> pair in GameDataRepository.Monsters)
        {
            MonsterData data = pair.Value;
            if (data != null && !string.IsNullOrWhiteSpace(data.Id) && !found.Exists(m => m.Id == data.Id))
                found.Add(data);
        }

        found.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

        for (int i = 0; i < found.Count; i++)
        {
            MonsterData data = found[i];
            Entry existing = FindEntry(data.Id);
            if (existing != null)
            {
                existing.displayName = data.Name;
                continue;
            }

            monsters.Add(new Entry { monsterId = data.Id, displayName = data.Name, percentAtFinalStage = 100f });
        }
    }

    private void Reset()
    {
        FillFromGameData();
    }

    private void OnValidate()
    {
        if (finalStage < 2) finalStage = 2;
        for (int i = 0; i < monsters.Count; i++)
        {
            if (monsters[i] == null) continue;

            if (monsters[i].percentAtFinalStage < 0.01f)
                monsters[i].percentAtFinalStage = 0.01f;

            if (monsters[i].hpCurve == null)
                monsters[i].hpCurve = new List<HpPoint>();
            NormalizeCurve(monsters[i].hpCurve, FinalStage);
        }
    }
}

