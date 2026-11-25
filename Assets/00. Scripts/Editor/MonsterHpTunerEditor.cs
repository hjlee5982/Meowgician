using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// MonsterHpTuner 인스펙터. 몬스터마다 "스테이지(x) → 체력(y)" 그래프를 그려서 보여주고,
/// 곡선 모드에서는 그래프의 점을 마우스로 끌어 체력 곡선을 직접 조절한다.
///
///  - 회색 선: 시트(원본) 체력,  초록 선: 조정 결과(실제로 적용되는 값),  노란 점: 곡선 점
///  - 점 끌기: 이동 / 빈 곳 더블클릭: 점 추가 / 점 우클릭: 삭제
///  - y축은 로그 스케일(기본)과 선형을 전환할 수 있다. 체력이 1스테이지 8에서 100스테이지 수백만까지
///    지수적으로 커서, 로그 스케일이어야 초반과 후반을 같이 보고 조절할 수 있다.
/// </summary>
[CustomEditor(typeof(MonsterHpTuner))]
public class MonsterHpTunerEditor : Editor
{
    private static readonly Color BgColor = new Color(0.12f, 0.13f, 0.16f, 1f);
    private static readonly Color GridColor = new Color(1f, 1f, 1f, 0.10f);
    private static readonly Color SheetColor = new Color(0.62f, 0.64f, 0.70f, 1f);
    private static readonly Color TunedColor = new Color(0.40f, 0.90f, 0.50f, 1f);
    private static readonly Color PointColor = new Color(1f, 0.85f, 0.30f, 1f);

    private const float GraphHeight = 300f;
    private const float PointRadius = 6f;
    private const float HitRadius = 12f;
    private const double MaxHp = 2e9;

    private bool logAxis = true;
    private int bakeInterval = 10;
    private int dragPoint = -1;
    private GUIStyle smallLabel;
    private readonly HashSet<string> openEntries = new();
    private readonly Dictionary<string, double[]> sheetCache = new();

    private struct GraphMap
    {
        public Rect plot;
        public int finalStage;
        public bool log;
        public double lo;
        public double hi;

        public float X(double stage)
        {
            double t = (stage - 1) / Math.Max(1, finalStage - 1);
            return plot.x + (float)t * plot.width;
        }

        public double InvX(float x)
        {
            return 1 + (x - plot.x) / plot.width * Math.Max(1, finalStage - 1);
        }

        public float Y(double hp)
        {
            double t = log ? (Math.Log10(Math.Max(1d, hp)) - lo) / (hi - lo) : hp / hi;
            t = Math.Max(0d, Math.Min(1d, t));
            return plot.yMax - (float)t * plot.height;
        }

        public double InvY(float y)
        {
            double t = (plot.yMax - y) / plot.height;
            return log ? Math.Pow(10d, lo + t * (hi - lo)) : t * hi;
        }
    }

    private void OnEnable()
    {
        sheetCache.Clear();

        // 곡선을 쓰는 몬스터는 처음부터 펼쳐서 바로 그래프를 볼 수 있게 한다.
        // 스크립트 재컴파일 직후 낡은 인스펙터가 남아 있으면 target 캐스트가 실패할 수 있어 안전하게 처리한다.
        var tuner = target as MonsterHpTuner;
        if (tuner == null || tuner.Monsters == null)
            return;

        foreach (MonsterHpTuner.Entry entry in tuner.Monsters)
        {
            if (entry != null && entry.useHpCurve && !string.IsNullOrWhiteSpace(entry.monsterId))
                openEntries.Add(entry.monsterId);
        }
    }

    public override void OnInspectorGUI()
    {
        var tuner = target as MonsterHpTuner;
        if (tuner == null)
            return;

        if (smallLabel == null)
            smallLabel = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight, normal = { textColor = new Color(1f, 1f, 1f, 0.7f) } };

        serializedObject.Update();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("enableTuning"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("finalStage"));
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(4f);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("GameData에서 몬스터 목록 채우기"))
            {
                Undo.RecordObject(tuner, "Fill monster list");
                tuner.FillFromGameData();
                sheetCache.Clear();
                EditorUtility.SetDirty(tuner);
            }

            if (GUILayout.Button("모두 원래대로 (100%, 곡선 끔)"))
            {
                Undo.RecordObject(tuner, "Reset monster tuning");
                foreach (MonsterHpTuner.Entry entry in tuner.Monsters)
                {
                    if (entry == null) continue;
                    entry.percentAtFinalStage = 100f;
                    entry.useHpCurve = false;
                }

                EditorUtility.SetDirty(tuner);
            }
        }

        logAxis = EditorGUILayout.ToggleLeft("y축 로그 스케일 (체력이 지수적으로 커서 초반도 보이게 함)", logAxis);
        bakeInterval = EditorGUILayout.IntSlider("곡선 채우기 간격(스테이지)", bakeInterval, 1, 25);
        EditorGUILayout.Space(4f);

        foreach (MonsterHpTuner.Entry entry in tuner.Monsters)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.monsterId)) continue;
            if (entry.hpCurve == null) entry.hpCurve = new List<MonsterHpTuner.HpPoint>();
            DrawEntry(tuner, entry);
        }
    }

    // ---------------------------------------------------------------- 몬스터 한 마리
    private void DrawEntry(MonsterHpTuner tuner, MonsterHpTuner.Entry entry)
    {
        string title = string.IsNullOrWhiteSpace(entry.displayName) ? entry.monsterId : entry.displayName;
        string summary = entry.useHpCurve
            ? "곡선 " + entry.hpCurve.Count + "점"
            : entry.percentAtFinalStage.ToString("0.##") + "%";

        bool open = openEntries.Contains(entry.monsterId);
        bool nowOpen = EditorGUILayout.Foldout(open, title + "    [" + summary + "]", true);
        if (nowOpen != open)
        {
            if (nowOpen) openEntries.Add(entry.monsterId);
            else openEntries.Remove(entry.monsterId);
        }

        if (!nowOpen) return;

        EditorGUI.indentLevel++;

        EditorGUI.BeginChangeCheck();
        bool use = EditorGUILayout.ToggleLeft("곡선 사용 (그래프로 체력을 직접 조절, 비율은 무시)", entry.useHpCurve);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(tuner, "Toggle HP curve");
            entry.useHpCurve = use;
            if (use && entry.hpCurve.Count == 0)
                Bake(tuner, entry);
            EditorUtility.SetDirty(tuner);
        }

        if (entry.useHpCurve)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("시트 값으로 곡선 채우기"))
                {
                    Bake(tuner, entry);
                }

                if (GUILayout.Button("곡선 점 모두 지우기"))
                {
                    Undo.RecordObject(tuner, "Clear HP curve");
                    entry.hpCurve.Clear();
                    EditorUtility.SetDirty(tuner);
                }
            }
        }
        else
        {
            EditorGUI.BeginChangeCheck();
            float percent = EditorGUILayout.FloatField("100스테이지 체력 비율(%)", entry.percentAtFinalStage);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(tuner, "Change monster percent");
                entry.percentAtFinalStage = Mathf.Max(0.01f, percent);
                EditorUtility.SetDirty(tuner);
            }
        }

        DrawGraph(tuner, entry);

        if (entry.useHpCurve)
            DrawPointList(tuner, entry);

        EditorGUI.indentLevel--;
        EditorGUILayout.Space(6f);
    }

    // ---------------------------------------------------------------- 시트(원본) 체력
    private double[] GetSheet(string monsterId, int finalStage)
    {
        string key = monsterId + "#" + finalStage;
        if (sheetCache.TryGetValue(key, out double[] cached)) return cached;

        var sheet = new double[finalStage + 1];
        for (int s = 1; s <= finalStage; s++)
            sheet[s] = MonsterHpTuner.TryGetBaseHp(monsterId, s, out int hp) ? hp : double.NaN;

        sheetCache[key] = sheet;
        return sheet;
    }

    private static double ComputeTuned(MonsterHpTuner tuner, MonsterHpTuner.Entry entry, int stage, double sheetHp)
    {
        if (tuner.EnableTuning && entry.useHpCurve && entry.hpCurve.Count > 0)
            return MonsterHpTuner.EvaluateCurve(entry.hpCurve, stage);

        if (double.IsNaN(sheetHp)) return double.NaN;

        int baseHp = (int)Math.Min(int.MaxValue, sheetHp);
        return tuner.TryApply(entry.monsterId, baseHp, stage, out int tuned) ? tuned : baseHp;
    }

    private void Bake(MonsterHpTuner tuner, MonsterHpTuner.Entry entry)
    {
        Undo.RecordObject(tuner, "Fill HP curve from sheet");

        int finalStage = tuner.FinalStage;
        double[] sheet = GetSheet(entry.monsterId, finalStage);
        var points = new List<MonsterHpTuner.HpPoint>();
        int lastAvailable = -1;

        for (int s = 1; s <= finalStage; s++)
        {
            if (double.IsNaN(sheet[s])) continue;

            lastAvailable = s;
            if (points.Count == 0 || s % bakeInterval == 0 || s == finalStage)
                points.Add(new MonsterHpTuner.HpPoint(s, (float)sheet[s]));
        }

        if (lastAvailable > 0 && (points.Count == 0 || points[points.Count - 1].stage != lastAvailable))
            points.Add(new MonsterHpTuner.HpPoint(lastAvailable, (float)sheet[lastAvailable]));

        MonsterHpTuner.NormalizeCurve(points, finalStage);
        entry.hpCurve = points;
        EditorUtility.SetDirty(tuner);
    }

    // ---------------------------------------------------------------- 그래프
    private void DrawGraph(MonsterHpTuner tuner, MonsterHpTuner.Entry entry)
    {
        int finalStage = tuner.FinalStage;
        Rect outer = EditorGUILayout.GetControlRect(false, GraphHeight);
        outer.xMin += EditorGUI.indentLevel * 15f;
        var plot = new Rect(outer.x + 64f, outer.y + 10f, Mathf.Max(60f, outer.width - 64f - 12f), outer.height - 10f - 24f);

        double[] sheet = GetSheet(entry.monsterId, finalStage);
        var tuned = new double[finalStage + 1];
        for (int s = 1; s <= finalStage; s++)
            tuned[s] = ComputeTuned(tuner, entry, s, sheet[s]);

        // y축 범위
        double maxValue = 1d;
        double minValue = double.MaxValue;
        for (int s = 1; s <= finalStage; s++)
        {
            Accumulate(sheet[s], ref minValue, ref maxValue);
            Accumulate(tuned[s], ref minValue, ref maxValue);
        }

        foreach (MonsterHpTuner.HpPoint p in entry.hpCurve)
            Accumulate(p.hp, ref minValue, ref maxValue);

        if (minValue == double.MaxValue) minValue = 1d;

        var map = new GraphMap { plot = plot, finalStage = finalStage, log = logAxis };
        if (logAxis)
        {
            map.lo = Math.Floor(Math.Log10(Math.Max(1d, minValue)));
            map.hi = Math.Ceiling(Math.Log10(Math.Max(10d, maxValue)));
            if (map.hi <= map.lo) map.hi = map.lo + 1d;
        }
        else
        {
            map.lo = 0d;
            map.hi = NiceCeil(maxValue * 1.05d);
        }

        int controlId = GUIUtility.GetControlID(FocusType.Passive);
        if (entry.useHpCurve)
            HandleMouse(tuner, entry, map, controlId);

        if (Event.current.type == EventType.Repaint)
            PaintGraph(entry, map, sheet, tuned, outer);

        EditorGUILayout.LabelField(
            entry.useHpCurve
                ? "회색: 시트(원본)   초록: 적용되는 체력   노란 점: 끌어서 이동 · 빈 곳 더블클릭으로 추가 · 우클릭으로 삭제"
                : "회색: 시트(원본)   초록: 적용되는 체력   (점을 직접 끌려면 '곡선 사용'을 켜세요)",
            EditorStyles.miniLabel);
    }

    private static void Accumulate(double value, ref double min, ref double max)
    {
        if (double.IsNaN(value) || value < 1d) return;
        if (value < min) min = value;
        if (value > max) max = value;
    }

    private static double NiceCeil(double value)
    {
        if (value <= 0d) return 1d;

        double magnitude = Math.Pow(10d, Math.Floor(Math.Log10(value)));
        double normalized = value / magnitude;
        double nice = normalized <= 1d ? 1d : normalized <= 2d ? 2d : normalized <= 5d ? 5d : 10d;
        return nice * magnitude;
    }

    private void PaintGraph(MonsterHpTuner.Entry entry, GraphMap map, double[] sheet, double[] tuned, Rect outer)
    {
        Rect plot = map.plot;
        EditorGUI.DrawRect(plot, BgColor);

        // y 격자와 눈금
        if (map.log)
        {
            int decades = (int)(map.hi - map.lo);
            int step = Math.Max(1, (decades + 7) / 8);
            for (int k = (int)map.lo; k <= (int)map.hi; k += step)
            {
                float y = map.Y(Math.Pow(10d, k));
                DrawHLine(plot, y);
                GUI.Label(new Rect(outer.x, y - 8f, 60f, 16f), FormatHp(Math.Pow(10d, k)), smallLabel);
            }
        }
        else
        {
            for (int i = 0; i <= 5; i++)
            {
                double v = map.hi * i / 5d;
                float y = map.Y(v);
                DrawHLine(plot, y);
                GUI.Label(new Rect(outer.x, y - 8f, 60f, 16f), FormatHp(v), smallLabel);
            }
        }

        // x 격자와 눈금(스테이지)
        int xStep = map.finalStage <= 40 ? 5 : 10;
        for (int s = xStep; s <= map.finalStage; s += xStep)
        {
            float x = map.X(s);
            Handles.color = GridColor;
            Handles.DrawLine(new Vector3(x, plot.y), new Vector3(x, plot.yMax));
            GUI.Label(new Rect(x - 20f, plot.yMax + 2f, 40f, 16f), s.ToString(), new GUIStyle(smallLabel) { alignment = TextAnchor.MiddleCenter });
        }

        GUI.Label(new Rect(plot.x - 4f, plot.yMax + 2f, 30f, 16f), "1", new GUIStyle(smallLabel) { alignment = TextAnchor.MiddleLeft });

        // 선
        DrawSeries(map, sheet, SheetColor, 1.5f);
        DrawSeries(map, tuned, TunedColor, 2.5f);

        // 곡선 점
        if (entry.useHpCurve)
        {
            for (int i = 0; i < entry.hpCurve.Count; i++)
            {
                MonsterHpTuner.HpPoint p = entry.hpCurve[i];
                var pos = new Vector3(map.X(p.stage), map.Y(p.hp));
                Handles.color = Color.black;
                Handles.DrawSolidDisc(pos, Vector3.forward, PointRadius + 1.5f);
                Handles.color = i == dragPoint ? Color.white : PointColor;
                Handles.DrawSolidDisc(pos, Vector3.forward, PointRadius);

                if (i == dragPoint)
                {
                    string label = "S" + p.stage + "   " + ((long)p.hp).ToString("N0");
                    var style = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
                    float lx = Mathf.Min(pos.x + 10f, plot.xMax - 130f);
                    GUI.Label(new Rect(lx, Mathf.Max(plot.y, pos.y - 22f), 160f, 16f), label, style);
                }
            }
        }
    }

    private static void DrawHLine(Rect plot, float y)
    {
        Handles.color = GridColor;
        Handles.DrawLine(new Vector3(plot.x, y), new Vector3(plot.xMax, y));
    }

    private static void DrawSeries(GraphMap map, double[] values, Color color, float width)
    {
        Handles.color = color;
        var segment = new List<Vector3>();

        for (int s = 1; s < values.Length; s++)
        {
            if (double.IsNaN(values[s]))
            {
                Flush(segment, width);
                continue;
            }

            segment.Add(new Vector3(map.X(s), map.Y(values[s])));
        }

        Flush(segment, width);
    }

    private static void Flush(List<Vector3> segment, float width)
    {
        if (segment.Count >= 2)
            Handles.DrawAAPolyLine(width, segment.ToArray());
        segment.Clear();
    }

    private static string FormatHp(double value)
    {
        if (value >= 1e9) return (value / 1e9).ToString("0.##") + "B";
        if (value >= 1e6) return (value / 1e6).ToString("0.##") + "M";
        if (value >= 1e3) return (value / 1e3).ToString("0.##") + "K";
        return value.ToString("0.##");
    }

    // ---------------------------------------------------------------- 마우스로 점 조절
    private void HandleMouse(MonsterHpTuner tuner, MonsterHpTuner.Entry entry, GraphMap map, int controlId)
    {
        Event e = Event.current;
        EventType type = e.GetTypeForControl(controlId);
        Vector2 mouse = e.mousePosition;
        List<MonsterHpTuner.HpPoint> points = entry.hpCurve;
        Rect hitArea = new Rect(map.plot.x - HitRadius, map.plot.y - HitRadius, map.plot.width + HitRadius * 2f, map.plot.height + HitRadius * 2f);

        switch (type)
        {
            case EventType.MouseDown:
                if (!hitArea.Contains(mouse)) break;

                int hit = FindPoint(map, points, mouse);
                if (e.button == 1 && hit >= 0)
                {
                    Undo.RecordObject(tuner, "Delete HP point");
                    points.RemoveAt(hit);
                    EditorUtility.SetDirty(tuner);
                    e.Use();
                }
                else if (e.button == 0)
                {
                    if (hit >= 0)
                    {
                        Undo.IncrementCurrentGroup();
                        dragPoint = hit;
                        GUIUtility.hotControl = controlId;
                        e.Use();
                    }
                    else if (e.clickCount == 2 && map.plot.Contains(mouse))
                    {
                        AddPoint(tuner, entry, map, mouse);
                        e.Use();
                    }
                }

                break;

            case EventType.MouseDrag:
                if (GUIUtility.hotControl != controlId || dragPoint < 0 || dragPoint >= points.Count) break;

                Undo.RecordObject(tuner, "Move HP point");
                int minStage = dragPoint > 0 ? points[dragPoint - 1].stage + 1 : 1;
                int maxStage = dragPoint < points.Count - 1 ? points[dragPoint + 1].stage - 1 : tuner.FinalStage;
                int stage = Mathf.Clamp(Mathf.RoundToInt((float)map.InvX(mouse.x)), minStage, Mathf.Max(minStage, maxStage));
                double hp = Math.Max(1d, Math.Min(MaxHp, map.InvY(mouse.y)));
                points[dragPoint] = new MonsterHpTuner.HpPoint(stage, (float)Math.Round(hp));
                EditorUtility.SetDirty(tuner);
                e.Use();
                Repaint();
                break;

            case EventType.MouseUp:
                if (GUIUtility.hotControl == controlId)
                {
                    GUIUtility.hotControl = 0;
                    dragPoint = -1;
                    e.Use();
                    Repaint();
                }

                break;
        }
    }

    private static int FindPoint(GraphMap map, List<MonsterHpTuner.HpPoint> points, Vector2 mouse)
    {
        int best = -1;
        float bestDistance = HitRadius;
        for (int i = 0; i < points.Count; i++)
        {
            var pos = new Vector2(map.X(points[i].stage), map.Y(points[i].hp));
            float distance = Vector2.Distance(pos, mouse);
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    private static void AddPoint(MonsterHpTuner tuner, MonsterHpTuner.Entry entry, GraphMap map, Vector2 mouse)
    {
        int stage = Mathf.Clamp(Mathf.RoundToInt((float)map.InvX(mouse.x)), 1, tuner.FinalStage);
        foreach (MonsterHpTuner.HpPoint p in entry.hpCurve)
            if (p.stage == stage) return;

        Undo.RecordObject(tuner, "Add HP point");
        double hp = Math.Max(1d, Math.Min(MaxHp, map.InvY(mouse.y)));
        entry.hpCurve.Add(new MonsterHpTuner.HpPoint(stage, (float)Math.Round(hp)));
        MonsterHpTuner.NormalizeCurve(entry.hpCurve, tuner.FinalStage);
        EditorUtility.SetDirty(tuner);
    }

    // ---------------------------------------------------------------- 점 목록(숫자로 정확히 입력)
    private void DrawPointList(MonsterHpTuner tuner, MonsterHpTuner.Entry entry)
    {
        List<MonsterHpTuner.HpPoint> points = entry.hpCurve;
        EditorGUILayout.LabelField("곡선 점 (스테이지 / 체력) — 숫자를 쓰고 Enter", EditorStyles.boldLabel);

        int remove = -1;
        bool changed = false;
        for (int i = 0; i < points.Count; i++)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                // 라벨 없는 오버로드는 Unity가 "GUIContent가 null"이라는 오류를 내서 GUIContent.none을 명시한다.
                int stage = EditorGUILayout.DelayedIntField(GUIContent.none, points[i].stage, GUILayout.Width(70f));
                double hp = EditorGUILayout.DelayedDoubleField(GUIContent.none, points[i].hp);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(tuner, "Edit HP point");
                    points[i] = new MonsterHpTuner.HpPoint(stage, (float)Math.Max(1d, Math.Min(MaxHp, hp)));
                    changed = true;
                }

                if (GUILayout.Button("✕", GUILayout.Width(28f)))
                    remove = i;
            }
        }

        if (GUILayout.Button("+ 점 추가 (마지막 점 뒤)"))
        {
            Undo.RecordObject(tuner, "Add HP point");
            MonsterHpTuner.HpPoint last = points.Count > 0 ? points[points.Count - 1] : new MonsterHpTuner.HpPoint(1, 10f);
            points.Add(new MonsterHpTuner.HpPoint(Mathf.Min(tuner.FinalStage, last.stage + 10), last.hp));
            changed = true;
        }

        if (remove >= 0)
        {
            Undo.RecordObject(tuner, "Delete HP point");
            points.RemoveAt(remove);
            changed = true;
        }

        if (changed)
        {
            MonsterHpTuner.NormalizeCurve(points, tuner.FinalStage);
            EditorUtility.SetDirty(tuner);
        }
    }
}
