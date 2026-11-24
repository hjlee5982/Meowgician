using UnityEditor;
using UnityEngine;

/// <summary>
/// MonsterHpTuner 인스펙터: 목록 채우기/초기화 버튼과, 비율을 바꿨을 때 스테이지별 체력이 어떻게 달라지는지 보여주는 미리보기.
/// </summary>
[CustomEditor(typeof(MonsterHpTuner))]
public class MonsterHpTunerEditor : Editor
{
    private static readonly int[] PreviewStages = { 1, 20, 40, 60, 80, 100 };

    private bool showPreview = true;
    private GUIStyle wrapStyle;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var tuner = (MonsterHpTuner)target;

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("GameData에서 몬스터 목록 채우기"))
            {
                Undo.RecordObject(tuner, "Fill monster list");
                tuner.FillFromGameData();
                EditorUtility.SetDirty(tuner);
            }

            if (GUILayout.Button("모두 100%로"))
            {
                Undo.RecordObject(tuner, "Reset monster percents");
                foreach (MonsterHpTuner.Entry entry in tuner.Monsters)
                    if (entry != null) entry.percentAtFinalStage = 100f;

                EditorUtility.SetDirty(tuner);
            }
        }

        EditorGUILayout.Space();
        showPreview = EditorGUILayout.Foldout(showPreview, "체력 미리보기 (원본 → 조정, 해당 스테이지에 나오는 몬스터만)", true);
        if (!showPreview) return;

        if (wrapStyle == null)
            wrapStyle = new GUIStyle(EditorStyles.label) { wordWrap = true, richText = true };

        if (!tuner.EnableTuning)
            EditorGUILayout.HelpBox("조절이 꺼져 있어서 원본과 같게 보입니다.", MessageType.Info);

        foreach (MonsterHpTuner.Entry entry in tuner.Monsters)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.monsterId)) continue;

            string title = string.IsNullOrWhiteSpace(entry.displayName) ? entry.monsterId : entry.displayName;
            EditorGUILayout.LabelField("<b>" + title + "</b>  (" + entry.percentAtFinalStage.ToString("0.##") + "%)", wrapStyle);
            EditorGUILayout.LabelField(BuildPreviewLine(tuner, entry), wrapStyle);
        }
    }

    private static string BuildPreviewLine(MonsterHpTuner tuner, MonsterHpTuner.Entry entry)
    {
        var parts = new System.Text.StringBuilder();
        for (int i = 0; i < PreviewStages.Length; i++)
        {
            int stage = PreviewStages[i];
            if (i > 0) parts.Append("   |   ");
            parts.Append("S").Append(stage).Append(' ');

            if (!MonsterHpTuner.TryGetBaseHp(entry.monsterId, stage, out int baseHp))
            {
                parts.Append('-');
                continue;
            }

            int tuned = tuner.TryApply(entry.monsterId, baseHp, stage, out int result) ? result : baseHp;
            parts.Append(baseHp.ToString("N0")).Append(" → ").Append(tuned.ToString("N0"));
        }

        return parts.ToString();
    }
}
