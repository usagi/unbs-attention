#if UNBS_BSIPA
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.MenuButtons;
using BeatSaberMarkupLanguage.Util;
using HMUI;
using IPA;
using IPA.Config;
using IPA.Config.Stores;
using Newtonsoft.Json;
using UnbsAttention.Config;
using UnbsAttention.Presentation;
using UnityEngine;

namespace UnbsAttention;

[Plugin(RuntimeOptions.DynamicInit)]
public sealed class BsipaPlugin
{
    private const string SettingsFilePath = "UserData/unbs-attention.settings.json";

    private readonly AttentionPluginRuntime _runtime = new();
    private PluginSettingsController? _settingsController;
    private UnbsSettingsLeftViewController? _settingsLeftViewController;
    private UnbsSettingsViewController? _settingsViewController;
    private UnbsSettingsFlowCoordinator? _settingsFlowCoordinator;
    private MainFlowCoordinator? _mainFlowCoordinator;
    private MenuButton? _menuButton;
    private bool _menuButtonRegistered;
    private IPA.Logging.Logger? _logger;
    private GameObject? _attentionDisplayObject;

    [Init]
    public void Init(IPA.Logging.Logger logger, IPA.Config.Config conf)
    {
        _logger = logger;
        PluginConfig pluginConfig;
        try
        {
            pluginConfig = conf.Generated<PluginConfig>();
        }
        catch (Exception ex)
        {
            logger.Warn("Failed to deserialize BSIPA config. Using defaults: " + ex.GetType().Name + " " + ex.Message);
            pluginConfig = new PluginConfig();
        }

        pluginConfig = TryLoadSettingsFile(pluginConfig);
        _runtime.Init(pluginConfig);
        logger.Info("unbs-attention initialized via BSIPA (Custom Flow Coordinator mode).");

        _menuButton = new MenuButton("UNBS Attention", "Open UNBS Attention settings", OnMenuButtonPressed, true);

        _attentionDisplayObject = new GameObject("UnbsAttention.AttentionDisplayBridge");
        UnityEngine.Object.DontDestroyOnLoad(_attentionDisplayObject);
        var bridge = _attentionDisplayObject.AddComponent<BsipaAttentionDisplayBridge>();
        bridge.Bind(_runtime);
    }

    [OnStart]
    public void OnStart()
    {
        MainMenuAwaiter.MainMenuInitializing += OnMainMenuInitializing;
        _logger?.Info("unbs-attention waiting for main menu initialization.");
    }

    [OnExit]
    public void OnExit()
    {
        SaveSettingsFile();

        MainMenuAwaiter.MainMenuInitializing -= OnMainMenuInitializing;
        TryUnregisterMenuButton();

        if (_attentionDisplayObject is not null)
        {
            UnityEngine.Object.Destroy(_attentionDisplayObject);
            _attentionDisplayObject = null;
        }

        _settingsFlowCoordinator = null;
        _settingsLeftViewController = null;
        _settingsViewController = null;
        _menuButton = null;
        _settingsController = null;
    }

    private void OnMainMenuInitializing()
    {
        _mainFlowCoordinator = ResolveMainFlowCoordinator();
        _logger?.Info(_mainFlowCoordinator is null
            ? "MainMenuInitializing: MainFlowCoordinator not found yet."
            : "MainMenuInitializing: MainFlowCoordinator resolved.");

        EnsureSettingsUiInitialized();

        if (_settingsFlowCoordinator is not null)
        {
            _settingsFlowCoordinator.BindMainFlow(_mainFlowCoordinator);
        }

        if (TryRegisterMenuButton())
        {
            _settingsController?.RenderState();
            _logger?.Info("unbs-attention settings flow is active.");
        }
        else
        {
            _logger?.Warn("MainMenuInitializing: failed to register UNBS Attention menu button.");
        }
    }

    private void EnsureSettingsUiInitialized()
    {
        if (_settingsLeftViewController is not null && _settingsViewController is not null && _settingsFlowCoordinator is not null && _settingsController is not null)
        {
            return;
        }

        try
        {
            _settingsLeftViewController = BeatSaberUI.CreateViewController<UnbsSettingsLeftViewController>();
            _settingsViewController = BeatSaberUI.CreateViewController<UnbsSettingsViewController>();
            _settingsController = new PluginSettingsController(_runtime, _settingsLeftViewController);
            _settingsController.AddView(_settingsViewController);

            _settingsLeftViewController.Bind(_settingsController);
            _settingsViewController.Bind(_settingsController);

            _settingsFlowCoordinator = BeatSaberUI.CreateFlowCoordinator<UnbsSettingsFlowCoordinator>();
            _settingsFlowCoordinator.Bind(_settingsLeftViewController, _settingsViewController);
            _settingsFlowCoordinator.BindMainFlow(_mainFlowCoordinator);
            _settingsFlowCoordinator.OnBackButton = SaveSettingsFile;
        }
        catch (Exception ex)
        {
            _logger?.Error("Failed to initialize UNBS settings UI: " + ex.GetType().Name + " " + ex.Message);
            _settingsController = null;
            _settingsLeftViewController = null;
            _settingsViewController = null;
            _settingsFlowCoordinator = null;
        }
    }

    private PluginConfig TryLoadSettingsFile(PluginConfig fallback)
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                return fallback;
            }

            var json = File.ReadAllText(SettingsFilePath);
            var loaded = JsonConvert.DeserializeObject<PluginConfig>(json);
            return loaded ?? fallback;
        }
        catch (Exception ex)
        {
            _logger?.Warn("Failed to parse settings file. Using defaults: " + ex.GetType().Name + " " + ex.Message);
            return fallback;
        }
    }

    private void SaveSettingsFile()
    {
        try
        {
            var snapshot = _runtime.GetConfigSnapshot();
            var directory = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonConvert.SerializeObject(snapshot, Formatting.Indented);
            File.WriteAllText(SettingsFilePath, json);
        }
        catch (Exception ex)
        {
            _logger?.Warn("Failed to save settings file: " + ex.GetType().Name + " " + ex.Message);
        }
    }

    private bool TryRegisterMenuButton()
    {
        if (_menuButtonRegistered)
        {
            return true;
        }

        if (_menuButton is null)
        {
            return false;
        }

        try
        {
            var menuButtons = MenuButtons.Instance;
            if (menuButtons is null)
            {
                return false;
            }

            menuButtons.RegisterButton(_menuButton);
            _menuButtonRegistered = true;
            _logger?.Info("Registered UNBS Attention menu button.");
            return true;
        }
        catch (Exception ex)
        {
            _logger?.Warn("Failed to register menu button: " + ex.GetType().Name + " " + ex.Message);
            return false;
        }
    }

    private void TryUnregisterMenuButton()
    {
        if (!_menuButtonRegistered || _menuButton is null)
        {
            return;
        }

        try
        {
            var menuButtons = MenuButtons.Instance;
            if (menuButtons is not null)
            {
                menuButtons.UnregisterButton(_menuButton);
            }
        }
        catch
        {
        }
        finally
        {
            _menuButtonRegistered = false;
        }
    }

    private void OnMenuButtonPressed()
    {
        try
        {
            _mainFlowCoordinator ??= ResolveMainFlowCoordinator();

            EnsureSettingsUiInitialized();

            if (_settingsFlowCoordinator is null)
            {
                _logger?.Warn("Settings flow coordinator is not ready.");
                return;
            }

            if (_mainFlowCoordinator is null)
            {
                _logger?.Warn("MainFlowCoordinator is not available.");
                return;
            }

            _settingsController?.RenderState();
            _settingsFlowCoordinator.BindMainFlow(_mainFlowCoordinator);
            _mainFlowCoordinator.PresentFlowCoordinator(_settingsFlowCoordinator, null, ViewController.AnimationDirection.Horizontal, false, false);
        }
        catch (Exception ex)
        {
            _logger?.Warn("Failed to open settings from menu button: " + ex.GetType().Name + " " + ex.Message);
        }
    }

    private static MainFlowCoordinator? ResolveMainFlowCoordinator()
    {
        return Resources.FindObjectsOfTypeAll<MainFlowCoordinator>().FirstOrDefault();
    }
}
#endif
