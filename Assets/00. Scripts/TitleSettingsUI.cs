using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 타이틀 씬 설정 패널. 배경음/효과음 볼륨을 슬라이더로 조절한다.
/// </summary>
public class TitleSettingsUI : MonoBehaviour
{
    [Header("Open")]
    [SerializeField] private Button openButton;

    [Header("Main UI")]
    [Tooltip("설정 패널이 열릴 때 숨길 타이틀 화면 UI.")]
    [SerializeField] private GameObject[] mainUiRoots;

    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Button closeButton;

    [Header("Sliders")]
    [SerializeField] private Slider bgmSlider;
    [SerializeField] private Slider sfxSlider;

    private void Awake()
    {
        if (openButton != null)
            openButton.onClick.AddListener(Open);
        if (closeButton != null)
            closeButton.onClick.AddListener(Close);

        SetupSlider(bgmSlider, OnBgmSliderChanged);
        SetupSlider(sfxSlider, OnSfxSliderChanged);

        if (panelRoot != null)
            panelRoot.SetActive(false);

        RefreshTogglesFromSettings();
    }

    private void OnEnable()
    {
        GameAudioSettings.Changed += RefreshTogglesFromSettings;
    }

    private void OnDisable()
    {
        GameAudioSettings.Changed -= RefreshTogglesFromSettings;
        if (panelRoot != null && panelRoot.activeSelf)
            Close();
    }

    public void Open()
    {
        RefreshTogglesFromSettings();
        SetMainUiVisible(false);
        if (panelRoot != null)
            panelRoot.SetActive(true);
    }

    public void Close()
    {
        if (panelRoot != null)
            panelRoot.SetActive(false);
        SetMainUiVisible(true);
    }

    private static void SetupSlider(Slider slider, UnityEngine.Events.UnityAction<float> onChanged)
    {
        if (slider == null)
            return;

        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
        slider.onValueChanged.RemoveListener(onChanged);
        slider.onValueChanged.AddListener(onChanged);
    }

    private void OnBgmSliderChanged(float value)
    {
        GameAudioSettings.SetBgmVolume(value);
    }

    private void OnSfxSliderChanged(float value)
    {
        GameAudioSettings.SetSfxVolume(value);
    }

    private void RefreshTogglesFromSettings()
    {
        if (bgmSlider != null)
            bgmSlider.SetValueWithoutNotify(GameAudioSettings.BgmVolume);
        if (sfxSlider != null)
            sfxSlider.SetValueWithoutNotify(GameAudioSettings.SfxVolume);
    }

    private void SetMainUiVisible(bool visible)
    {
        if (mainUiRoots == null)
            return;

        for (int i = 0; i < mainUiRoots.Length; i++)
        {
            if (mainUiRoots[i] != null)
                mainUiRoots[i].SetActive(visible);
        }
    }
}
