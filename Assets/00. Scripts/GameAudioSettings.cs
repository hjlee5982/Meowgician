using System;
using UnityEngine;

/// <summary>
/// 전역 사운드 설정. PlayerPrefs에 저장되며 BGM/SFX 볼륨(0~1)을 관리한다.
/// </summary>
public static class GameAudioSettings
{
    private const string BgmLevelKey = "Audio.BgmLevel";
    private const string SfxLevelKey = "Audio.SfxLevel";
    // 이전 켜짐/꺼짐 토글 키(마이그레이션용).
    private const string LegacyBgmEnabledKey = "Audio.BgmEnabled";
    private const string LegacySfxEnabledKey = "Audio.SfxEnabled";

    private const float MuteThreshold = 0.001f;

    private static bool loaded;
    private static float bgmLevel = 1f;
    private static float sfxLevel = 1f;

    public static event Action Changed;

    /// <summary>BGM 채널 볼륨(0~1).</summary>
    public static float BgmVolume
    {
        get
        {
            EnsureLoaded();
            return bgmLevel;
        }
        set => SetBgmVolume(value);
    }

    /// <summary>SFX 채널 볼륨(0~1).</summary>
    public static float SfxVolume
    {
        get
        {
            EnsureLoaded();
            return sfxLevel;
        }
        set => SetSfxVolume(value);
    }

    /// <summary>BGM 볼륨이 0이 아니면 true.</summary>
    public static bool BgmEnabled => BgmVolume > MuteThreshold;

    /// <summary>SFX 볼륨이 0이 아니면 true.</summary>
    public static bool SfxEnabled => SfxVolume > MuteThreshold;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        EnsureLoaded();
        Apply();
    }

    public static void SetBgmVolume(float volume)
    {
        EnsureLoaded();
        volume = Mathf.Clamp01(volume);
        if (Mathf.Approximately(bgmLevel, volume))
            return;

        bgmLevel = volume;
        PlayerPrefs.SetFloat(BgmLevelKey, volume);
        PlayerPrefs.Save();
        Apply();
        Changed?.Invoke();
    }

    public static void SetSfxVolume(float volume)
    {
        EnsureLoaded();
        volume = Mathf.Clamp01(volume);
        if (Mathf.Approximately(sfxLevel, volume))
            return;

        sfxLevel = volume;
        PlayerPrefs.SetFloat(SfxLevelKey, volume);
        PlayerPrefs.Save();
        Apply();
        Changed?.Invoke();
    }

    public static void Apply()
    {
        EnsureLoaded();
        AudioListener.volume = 1f;
    }

    private static void EnsureLoaded()
    {
        if (loaded)
            return;

        bgmLevel = LoadLevel(BgmLevelKey, LegacyBgmEnabledKey);
        sfxLevel = LoadLevel(SfxLevelKey, LegacySfxEnabledKey);
        loaded = true;
    }

    private static float LoadLevel(string levelKey, string legacyEnabledKey)
    {
        if (PlayerPrefs.HasKey(levelKey))
            return Mathf.Clamp01(PlayerPrefs.GetFloat(levelKey, 1f));

        // 이전 토글 설정이 꺼짐이었다면 0, 아니면 최대.
        if (PlayerPrefs.HasKey(legacyEnabledKey))
            return PlayerPrefs.GetInt(legacyEnabledKey, 1) != 0 ? 1f : 0f;

        return 1f;
    }
}
