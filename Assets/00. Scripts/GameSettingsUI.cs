using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 인게임 설정 패널. 전투 중에만 버튼을 표시하고, 열리면 오버레이 일시정지한다.
/// </summary>
public class GameSettingsUI : MonoBehaviour
{
    [Header("Open")]
    [SerializeField] private GameObject openButtonRoot;
    [SerializeField] private Button openButton;

    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Button closeButton;

    [Header("Sliders")]
    [SerializeField] private Slider bgmSlider;
    [SerializeField] private Slider sfxSlider;

    public static GameSettingsUI Instance { get; private set; }

    private bool isHudVisible;
    private bool isOpen;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        ResolveReferences();

        if (openButton != null)
        {
            openButton.onClick.RemoveListener(Open);
            openButton.onClick.AddListener(Open);
        }

        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(Close);
            closeButton.onClick.AddListener(Close);
        }

        SetupSlider(bgmSlider, OnBgmSliderChanged);
        SetupSlider(sfxSlider, OnSfxSliderChanged);

        if (panelRoot != null)
            panelRoot.SetActive(false);

        RefreshTogglesFromSettings();
        SetVisible(false);
    }

    private void OnEnable()
    {
        GameAudioSettings.Changed += RefreshTogglesFromSettings;
    }

    private void OnDisable()
    {
        GameAudioSettings.Changed -= RefreshTogglesFromSettings;
        if (isOpen)
            Close();
    }

    private void OnDestroy()
    {
        if (openButton != null)
            openButton.onClick.RemoveListener(Open);
        if (closeButton != null)
            closeButton.onClick.RemoveListener(Close);
        if (bgmSlider != null)
            bgmSlider.onValueChanged.RemoveListener(OnBgmSliderChanged);
        if (sfxSlider != null)
            sfxSlider.onValueChanged.RemoveListener(OnSfxSliderChanged);

        if (Instance == this)
        {
            if (isOpen && GameSpeedController.Instance != null)
                GameSpeedController.Instance.SetOverlayPaused(false);

            Instance = null;
        }
    }

    public void SetVisible(bool active)
    {
        isHudVisible = active;
        ResolveReferences();

        if (!active)
            Close();
        else
            ApplyButtonVisibility();
    }

    public void Open()
    {
        if (panelRoot == null)
            return;

        RefreshTogglesFromSettings();
        panelRoot.SetActive(true);
        panelRoot.transform.SetAsLastSibling();
        isOpen = true;
        ApplyButtonVisibility();

        if (GameSpeedController.Instance != null)
            GameSpeedController.Instance.SetOverlayPaused(true);
    }

    public void Close()
    {
        if (panelRoot != null)
            panelRoot.SetActive(false);

        isOpen = false;
        ApplyButtonVisibility();

        if (GameSpeedController.Instance != null)
            GameSpeedController.Instance.SetOverlayPaused(false);
    }

    private void ApplyButtonVisibility()
    {
        bool showButton = isHudVisible && !isOpen;
        if (openButtonRoot != null)
            openButtonRoot.SetActive(showButton);
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

    private void ResolveReferences()
    {
        Transform layout = null;
        GameObject layoutObject = GameObject.Find("UILayout");
        if (layoutObject != null)
            layout = layoutObject.transform;

        if (openButtonRoot == null && layout != null)
        {
            Transform found = layout.Find("Setting");
            if (found != null)
                openButtonRoot = found.gameObject;
        }

        if (panelRoot == null && layout != null)
        {
            Transform found = layout.Find("SettingsPanel");
            if (found != null)
                panelRoot = found.gameObject;
        }

        if (openButtonRoot != null && openButton == null)
            openButton = openButtonRoot.GetComponent<Button>();

        if (closeButton == null && panelRoot != null)
        {
            Transform found = panelRoot.transform.Find("Panel/CloseButton");
            if (found != null)
                closeButton = found.GetComponent<Button>();
        }

        if (bgmSlider == null && panelRoot != null)
        {
            Transform found = panelRoot.transform.Find("Panel/배경음Slider");
            if (found != null)
                bgmSlider = found.GetComponent<Slider>();
        }

        if (sfxSlider == null && panelRoot != null)
        {
            Transform found = panelRoot.transform.Find("Panel/효과음Slider");
            if (found != null)
                sfxSlider = found.GetComponent<Slider>();
        }
    }
}
