#if UNBS_BSIPA
using System.Reflection;
using UnbsAttention.Models;
using UnbsAttention.Services;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace UnbsAttention.Presentation;

[DefaultExecutionOrder(10000)]
public sealed class BsipaAttentionDisplayBridge : MonoBehaviour
{
 private static readonly BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
 private static readonly Color DefaultAttentionDisplayColor = new(1f, 0.84f, 0.25f, 1f);
 private static readonly Color DefaultPlayButtonAttentionColor = new(1f, 0.86f, 0.18f, 1f);
 private static readonly Color DefaultPlayButtonAttentionTextColor = new(0f, 0f, 0f, 1f);
 private static readonly Color DefaultPlayButtonConfirmColor = new(1f, 0.35f, 0.2f, 1f);
 private static readonly Color DefaultPlayButtonOutlineColor = new(0f, 0f, 0f, 1f);
 private const float VisualMaintenanceIntervalSeconds = 0.15f;
 private const float TintMaintenanceIntervalSeconds = 0.1f;
 private const float TintDriftBoostSeconds = 0.35f;

 private sealed class EventSubscription
 {
  public object Target { get; set; } = null!;

  public EventInfo? EventInfo { get; set; }

  public FieldInfo? DelegateField { get; set; }

  public Delegate Handler { get; set; } = null!;
 }

 private sealed class PlayPointerEventRelay : MonoBehaviour,
  IPointerEnterHandler,
  IPointerExitHandler,
  ISelectHandler,
  IDeselectHandler
 {
  public Action? OnStateChanged;
  public Action<bool>? OnHoverChanged;

  public void OnPointerEnter(PointerEventData eventData)
  {
   OnHoverChanged?.Invoke(true);
   OnStateChanged?.Invoke();
  }

  public void OnPointerExit(PointerEventData eventData)
  {
   OnHoverChanged?.Invoke(false);
   OnStateChanged?.Invoke();
  }

  public void OnSelect(BaseEventData eventData)
  {
   OnStateChanged?.Invoke();
  }

  public void OnDeselect(BaseEventData eventData)
  {
   OnStateChanged?.Invoke();
  }
 }

 private AttentionPluginRuntime? _runtime;
 private CancellationTokenSource? _cts;
 private readonly List<EventSubscription> _menuControllerSubscriptions = new();
 private EventSubscription? _gameTransitionSubscription;
 private bool _sceneHooksInstalled;
 private bool _songSelectionActive;
 private bool _playPointerHovering;
 private int _pendingAttachRequest;
 private int _pendingRefreshRequest;
 private int _pendingTintRefreshRequest;
 private float _nextVisualMaintenanceAt = float.MinValue;
 private float _nextTintMaintenanceAt = float.MinValue;
 private float _tintDriftBoostUntil = float.MinValue;
 private bool _refreshInFlight;
 private string? _displayText;
 private string _lastAppliedDisplayText = string.Empty;
 private Color _lastAppliedDisplayColor = Color.clear;
 private bool _hasAppliedDisplayStyle;
 private string _lastLookupSnapshotKey = string.Empty;
 private MonoBehaviour? _cachedDetailController;
 private GameObject? _curvedTextObject;
 private Component? _curvedTextComponent;
 private Transform? _currentSurfaceRoot;
 private Transform? _currentPlayAnchor;
 private Transform? _currentPracticeAnchor;
 private Transform? _currentActionAnchor;
 private Type? _curvedTextType;
 private Component? _playTintTarget;
 private Component? _playOutlineTintTarget;
 private Component? _playLabelTintTarget;
 private Component? _playSelectableTarget;
 private ColorMemberSnapshot? _playTintOriginalSnapshot;
 private ColorMemberSnapshot? _playOutlineOriginalSnapshot;
 private ColorMemberSnapshot? _playLabelOriginalSnapshot;
 private string? _playLabelOriginalText;
 private bool _playLabelOriginalTextCaptured;
 private bool _playOriginalSelectableColorsCaptured;
 private object? _playOriginalSelectableColors;
 private Component? _playClickInterceptTarget;
 private Component? _playPointerRelayTarget;
 private PlayPointerEventRelay? _playPointerEventRelay;
 private object? _playOriginalOnClickEvent;
 private object? _playInterceptOnClickEvent;
 private bool _playInterceptInstalled;
 private bool _playConfirmModalVisible;
 private bool _playBypassOnce;
 private bool _playInterceptDisabledDueToError;
 private float _playConfirmArmedUntil;
 private Transform? _lastTintTargetRoot;
 private bool _hasAppliedTintState;
 private bool _lastAppliedAttentionState;
 private bool _lastAppliedConfirmState;
 private bool _lastConfirmLabelActive;
 private int _lastConfirmLabelSeconds = -1;
 private string _lastPlayAnchorPath = string.Empty;
 private float _lastTintTraceAt = float.MinValue;
 private int _tintTraceCount;
 private long _lastDataRevision = -1;
 private string _cachedAttentionDisplayColorHex = string.Empty;
 private Color _cachedAttentionDisplayColor = DefaultAttentionDisplayColor;
 private string _cachedPlayButtonAttentionColorHex = string.Empty;
 private Color _cachedPlayButtonAttentionColor = DefaultPlayButtonAttentionColor;
 private string _cachedPlayButtonAttentionTextColorHex = string.Empty;
 private Color _cachedPlayButtonAttentionTextColor = DefaultPlayButtonAttentionTextColor;
 private string _cachedPlayButtonConfirmColorHex = string.Empty;
 private Color _cachedPlayButtonConfirmColor = DefaultPlayButtonConfirmColor;

 private void OnEnable()
 {
  Application.onBeforeRender -= OnBeforeRender;
  Application.onBeforeRender += OnBeforeRender;
 }

 private void OnDisable()
 {
  Application.onBeforeRender -= OnBeforeRender;
 }

 public void Bind(AttentionPluginRuntime runtime)
 {
  if (_runtime is not null)
  {
   _runtime.DataRevisionChanged -= OnRuntimeDataRevisionChanged;
  }

  _runtime = runtime;
  _runtime.DataRevisionChanged += OnRuntimeDataRevisionChanged;
  _cts ??= new CancellationTokenSource();

  InstallSceneHooksIfNeeded();
  TryInstallGameTransitionHook();
  RebindMenuControllerEvents();
 }

 private void Update()
 {
  if (_runtime is null || _cts is null)
  {
   return;
  }

  var now = Time.unscaledTime;
  if (!_songSelectionActive)
  {
   Interlocked.Exchange(ref _pendingAttachRequest, 0);
   Interlocked.Exchange(ref _pendingRefreshRequest, 0);
    Interlocked.Exchange(ref _pendingTintRefreshRequest, 0);
   return;
  }

  if (_playConfirmModalVisible && now > _playConfirmArmedUntil)
  {
   _playConfirmModalVisible = false;
    RequestTintRefresh();
  }

  if (Interlocked.Exchange(ref _pendingAttachRequest, 0) == 1)
  {
   EnsureCurvedTextAttached();
  }

  var runVisualMaintenance = now >= _nextVisualMaintenanceAt;
  if (runVisualMaintenance)
  {
   _nextVisualMaintenanceAt = now + VisualMaintenanceIntervalSeconds;
  }

  var runTintMaintenance = now >= _nextTintMaintenanceAt;
  if (runTintMaintenance)
  {
   _nextTintMaintenanceAt = now + TintMaintenanceIntervalSeconds;
  }

  var forceTintRefresh = Interlocked.Exchange(ref _pendingTintRefreshRequest, 0) == 1;
  var boostedDriftCheck = _playPointerHovering || now <= _tintDriftBoostUntil;
  ApplyDisplayToCurvedText(runVisualMaintenance, runTintMaintenance || forceTintRefresh || boostedDriftCheck);

  if (_refreshInFlight)
  {
   return;
  }

  if (Interlocked.Exchange(ref _pendingRefreshRequest, 0) == 1)
  {
   _ = RefreshAsync(_cts.Token);
  }
 }

 private void LateUpdate()
 {
  if (_runtime is null || _cts is null || !_songSelectionActive)
  {
   return;
  }

  var attentionActive = !string.IsNullOrWhiteSpace(_displayText);
  if (!attentionActive)
  {
   return;
  }

  UpdatePlayButtonTint(attentionActive, true, forceApply: true);
 }

 private void OnBeforeRender()
 {
  if (_runtime is null || _cts is null || !_songSelectionActive)
  {
   return;
  }

  var attentionActive = !string.IsNullOrWhiteSpace(_displayText);
  if (!attentionActive)
  {
   return;
  }

  UpdatePlayButtonTint(attentionActive, true, forceApply: true);
 }

 private void InstallSceneHooksIfNeeded()
 {
  if (_sceneHooksInstalled)
  {
   return;
  }

  SceneManager.activeSceneChanged += OnActiveSceneChanged;
  _sceneHooksInstalled = true;

  OnActiveSceneChanged(default, SceneManager.GetActiveScene());
 }

 private void OnActiveSceneChanged(Scene previousScene, Scene nextScene)
 {
  if (IsMenuScene(nextScene.name))
  {
   TryInstallGameTransitionHook();
   RebindMenuControllerEvents();
   return;
  }

  ClearMenuControllerEventSubscriptions();
  ClearGameTransitionHook();
  ClearPlayPointerEventRelay();
  _cachedDetailController = null;

  if (_songSelectionActive)
  {
   _songSelectionActive = false;
   OnSongSelectionDeactivated();
  }
 }

 private static bool IsMenuScene(string sceneName)
 {
  return sceneName.IndexOf("menu", StringComparison.OrdinalIgnoreCase) >= 0;
 }

 private void TryInstallGameTransitionHook()
 {
  if (_gameTransitionSubscription is not null)
  {
   return;
  }

  var manager = Resources.FindObjectsOfTypeAll<MonoBehaviour>()
   .FirstOrDefault(x => x is not null && string.Equals(x.GetType().Name, "GameScenesManager", StringComparison.Ordinal));
  if (manager is null)
  {
   return;
  }

  var handlerMethod = GetType().GetMethod(nameof(OnGameTransitionDidFinish), InstanceFlags);
  if (handlerMethod is null)
  {
   return;
  }

  if (!TryBindEvent(manager, "transitionDidFinishEvent", handlerMethod, out var subscription) || subscription is null)
  {
   return;
  }

  _gameTransitionSubscription = subscription;
 }

 private void OnGameTransitionDidFinish(object sceneTransitionType, object transitionSetupData, object diContainer)
 {
  RebindMenuControllerEvents();
 }

 private void RebindMenuControllerEvents()
 {
  ClearMenuControllerEventSubscriptions();

    _cachedDetailController = FindStandardLevelDetailViewController(requireUsable: false);
  if (_cachedDetailController is null)
  {
   return;
  }

  var boundAny = false;
  boundAny |= TryBindMenuControllerEvent(_cachedDetailController, "didActivateEvent", nameof(OnDetailDidActivate));
  boundAny |= TryBindMenuControllerEvent(_cachedDetailController, "didDeactivateEvent", nameof(OnDetailDidDeactivate));
  boundAny |= TryBindMenuControllerEvent(_cachedDetailController, "didChangeDifficultyBeatmapEvent", nameof(OnMenuSelectionChangedOneArg));

  var levelCollectionController = FindComponentByTypeName("LevelCollectionViewController");
  if (levelCollectionController is not null)
  {
   boundAny |= TryBindMenuControllerEvent(levelCollectionController, "didSelectLevelEvent", nameof(OnMenuSelectionChangedTwoArgs));
  }

  var characteristicController = FindComponentByTypeName("BeatmapCharacteristicSegmentedControlController");
  if (characteristicController is not null)
  {
   boundAny |= TryBindMenuControllerEvent(characteristicController, "didSelectBeatmapCharacteristicEvent", nameof(OnMenuSelectionChangedTwoArgs));
  }

    if (!boundAny)
  {
   return;
  }

    if (!IsUsableDetailController(_cachedDetailController))
    {
     return;
    }

  if (!_songSelectionActive)
  {
   _songSelectionActive = true;
   OnSongSelectionActivated();
   return;
  }

  RequestAttachAndRefresh();
 }

 private static Component? FindComponentByTypeName(string typeName)
 {
  Component? fallback = null;

  foreach (var component in Resources.FindObjectsOfTypeAll<Component>())
  {
   if (component is null)
   {
    continue;
   }

   if (!string.Equals(component.GetType().Name, typeName, StringComparison.Ordinal))
   {
    continue;
   }

  if (component is Behaviour behaviour && behaviour.isActiveAndEnabled)
   {
    return component;
   }

   fallback ??= component;
  }

  return fallback;
 }

 private bool TryBindMenuControllerEvent(object target, string eventName, string handlerMethodName)
 {
  var method = GetType().GetMethod(handlerMethodName, InstanceFlags);
  if (method is null)
  {
   return false;
  }

  if (!TryBindEvent(target, eventName, method, out var subscription) || subscription is null)
  {
   return false;
  }

  _menuControllerSubscriptions.Add(subscription);
  return true;
 }

 private static FieldInfo? FindDelegateField(Type type, string eventName)
 {
  for (var cursor = type; cursor is not null; cursor = cursor.BaseType)
  {
   var field = cursor.GetField(eventName, InstanceFlags)
    ?? cursor.GetField("m_" + eventName, InstanceFlags)
    ?? cursor.GetField("_" + eventName, InstanceFlags);
   if (field is not null && typeof(Delegate).IsAssignableFrom(field.FieldType))
   {
    return field;
   }
  }

  return null;
 }

 private bool TryBindEvent(object target, string eventName, MethodInfo handlerMethod, out EventSubscription? subscription)
 {
  subscription = null;
  var type = target.GetType();

  var eventInfo = type.GetEvent(eventName, InstanceFlags);
  if (eventInfo?.EventHandlerType is not null)
  {
   var handler = Delegate.CreateDelegate(eventInfo.EventHandlerType, this, handlerMethod, false);
   if (handler is not null)
   {
    eventInfo.AddEventHandler(target, handler);
    subscription = new EventSubscription
    {
     Target = target,
     EventInfo = eventInfo,
     Handler = handler,
    };

    return true;
   }
  }

  var delegateField = FindDelegateField(type, eventName);
  if (delegateField is null)
  {
   return false;
  }

  var delegateType = delegateField.FieldType;
  if (!typeof(Delegate).IsAssignableFrom(delegateType))
  {
   return false;
  }

  var fieldHandler = Delegate.CreateDelegate(delegateType, this, handlerMethod, false);
  if (fieldHandler is null)
  {
   return false;
  }

  var current = delegateField.GetValue(target) as Delegate;
  delegateField.SetValue(target, Delegate.Combine(current, fieldHandler));

  subscription = new EventSubscription
  {
   Target = target,
   DelegateField = delegateField,
   Handler = fieldHandler,
  };

  return true;
 }

 private static void UnbindEvent(EventSubscription subscription)
 {
  try
  {
   if (subscription.EventInfo is not null)
   {
    subscription.EventInfo.RemoveEventHandler(subscription.Target, subscription.Handler);
    return;
   }

   if (subscription.DelegateField is null)
   {
    return;
   }

   var current = subscription.DelegateField.GetValue(subscription.Target) as Delegate;
   subscription.DelegateField.SetValue(subscription.Target, Delegate.Remove(current, subscription.Handler));
  }
  catch
  {
  }
 }

 private void ClearMenuControllerEventSubscriptions()
 {
  foreach (var subscription in _menuControllerSubscriptions)
  {
   UnbindEvent(subscription);
  }

  _menuControllerSubscriptions.Clear();
 }

 private void ClearGameTransitionHook()
 {
  if (_gameTransitionSubscription is null)
  {
   return;
  }

  UnbindEvent(_gameTransitionSubscription);
  _gameTransitionSubscription = null;
 }

 private void OnDetailDidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
 {
  if (!_songSelectionActive)
  {
   _songSelectionActive = true;
   OnSongSelectionActivated();
   return;
  }

  RequestAttachAndRefresh();
 }

 private void OnDetailDidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)
 {
  if (!_songSelectionActive)
  {
   return;
  }

  _songSelectionActive = false;
  OnSongSelectionDeactivated();
 }

 private void OnMenuSelectionChangedOneArg(object arg)
 {
  if (!_songSelectionActive)
  {
   return;
  }

  RequestAttachAndRefresh();
 }

 private void OnMenuSelectionChangedTwoArgs(object arg0, object arg1)
 {
  if (!_songSelectionActive)
  {
   return;
  }

  RequestAttachAndRefresh();
 }

 private void OnRuntimeDataRevisionChanged(long revision)
 {
  if (!_songSelectionActive)
  {
   return;
  }

  Interlocked.Exchange(ref _pendingRefreshRequest, 1);
 }

 private void RequestAttachAndRefresh()
 {
  Interlocked.Exchange(ref _pendingAttachRequest, 1);
  Interlocked.Exchange(ref _pendingRefreshRequest, 1);
  Interlocked.Exchange(ref _pendingTintRefreshRequest, 1);
  _nextVisualMaintenanceAt = float.MinValue;
  _nextTintMaintenanceAt = float.MinValue;
 }

 private void RequestTintRefresh()
 {
  Interlocked.Exchange(ref _pendingTintRefreshRequest, 1);
  _nextTintMaintenanceAt = float.MinValue;
  _tintDriftBoostUntil = Mathf.Max(_tintDriftBoostUntil, Time.unscaledTime + TintDriftBoostSeconds);
 }

   private void OnSongSelectionActivated()
   {
      _displayText = null;
      _lastAppliedDisplayText = string.Empty;
      _hasAppliedDisplayStyle = false;
    _lastLookupSnapshotKey = string.Empty;
    _lastDataRevision = -1;
    _nextVisualMaintenanceAt = float.MinValue;
    _nextTintMaintenanceAt = float.MinValue;
    _tintDriftBoostUntil = float.MinValue;
    _playPointerHovering = false;
    RequestAttachAndRefresh();
    LogInfo("[UNBS] Song selection activated.");
   }

   private void OnSongSelectionDeactivated()
   {
    _displayText = null;
    _lastAppliedDisplayText = string.Empty;
    _hasAppliedDisplayStyle = false;
    _lastLookupSnapshotKey = string.Empty;
    _lastDataRevision = -1;
    _nextVisualMaintenanceAt = float.MinValue;
    _nextTintMaintenanceAt = float.MinValue;
    _tintDriftBoostUntil = float.MinValue;
    _playPointerHovering = false;
    Interlocked.Exchange(ref _pendingAttachRequest, 0);
    Interlocked.Exchange(ref _pendingRefreshRequest, 0);
    Interlocked.Exchange(ref _pendingTintRefreshRequest, 0);
    _cachedDetailController = null;

    if (_curvedTextObject is not null)
    {
     _curvedTextObject.SetActive(false);
    }

    _currentSurfaceRoot = null;
    _currentPlayAnchor = null;
    _currentPracticeAnchor = null;
    _currentActionAnchor = null;
    _lastTintTargetRoot = null;
    ClearPlayPointerEventRelay();
    RestorePlayButtonVisualState();
    ResetPlayIntercept();
    LogInfo("[UNBS] Song selection deactivated.");
   }

 private void OnGUI()
 {
  // 曲面テキストへ載せられない時だけ 2D 描画をフォールバックとして使う。
   if (_curvedTextObject is not null && _curvedTextObject.activeSelf)
    {
     return;
    }

  if (string.IsNullOrWhiteSpace(_displayText))
  {
   return;
  }

  var width = 900f;
  var x = (Screen.width - width) * 0.5f;
  var y = Screen.height * 0.78f;

  var style = new GUIStyle(GUI.skin.label)
  {
   alignment = TextAnchor.MiddleCenter,
   fontSize = 22,
    normal = { textColor = GetAttentionDisplayColor() },
   richText = false,
  };

  GUI.Label(new Rect(x, y, width, 28f), _displayText, style);
 }

 private void EnsureCurvedTextAttached()
 {
  var detailController = ResolveDetailController();
  if (detailController is null)
  {
    if (_curvedTextObject is not null)
   {
      _curvedTextObject.SetActive(false);
   }

     _displayText = null;
    _lastAppliedDisplayText = string.Empty;
    _hasAppliedDisplayStyle = false;
    _currentSurfaceRoot = null;
    _currentPlayAnchor = null;
    _currentPracticeAnchor = null;
    _currentActionAnchor = null;
    _lastTintTargetRoot = null;
    ResetPlayIntercept();
    UpdatePlayButtonTint(false, true);
   return;
  }

  var playAnchor = FindAnchorByNames(
   detailController,
   "_playButton",
   "playButton",
   "_practiceButton",
   "practiceButton",
   "_actionButton",
   "actionButton")
   ?? FindChildByNames(detailController.transform,
    "ActionButton",
    "actionButton",
    "PlayButton",
    "playButton")
   ?? detailController.transform;
  var practiceAnchor = FindAnchorByNames(detailController, "_practiceButton", "practiceButton")
   ?? FindChildByNames(detailController.transform, "PracticeButton", "practiceButton");
  var actionAnchor = FindAnchorByNames(detailController, "_actionButton", "actionButton")
   ?? FindChildByNames(detailController.transform, "ActionButton", "actionButton");
   var surfaceRoot = playAnchor.parent ?? detailController.transform;
  var template = FindCurvedTextTemplate(detailController.transform, playAnchor);
   if (template is null)
  {
    if (_curvedTextObject is not null)
    {
      _curvedTextObject.SetActive(false);
    }

      _displayText = null;
    _lastAppliedDisplayText = string.Empty;
    _hasAppliedDisplayStyle = false;
    _currentSurfaceRoot = null;
    _currentPlayAnchor = null;
    _currentPracticeAnchor = null;
    _currentActionAnchor = null;
      _lastTintTargetRoot = null;
      ResetPlayIntercept();
      UpdatePlayButtonTint(false, true);
    return;
  }

   if (_curvedTextObject is null || _currentSurfaceRoot != surfaceRoot || _curvedTextType != template.GetType())
  {
    CreateCurvedTextFromTemplate(template, surfaceRoot);
    _currentSurfaceRoot = surfaceRoot;
  }

   _currentPlayAnchor = playAnchor;
  _currentPracticeAnchor = practiceAnchor;
  _currentActionAnchor = actionAnchor;
  TracePlayAnchorIfChanged(playAnchor);
   PositionCurvedTextUnderPlayArea();
 }

 private MonoBehaviour? ResolveDetailController()
 {
  if (_cachedDetailController is not null && IsUsableDetailController(_cachedDetailController))
  {
   return _cachedDetailController;
  }

  _cachedDetailController = FindStandardLevelDetailViewController(requireUsable: true);
  return _cachedDetailController;
 }

private void ApplyDisplayToCurvedText(bool runVisualMaintenance, bool runTintMaintenance)
 {
  if (_curvedTextObject is null)
  {
   return;
  }

  var hasText = !string.IsNullOrWhiteSpace(_displayText);
  _curvedTextObject.SetActive(hasText && _currentSurfaceRoot is not null);

  if (hasText && _curvedTextComponent is not null)
  {
   var nextText = _displayText!;
    var textChanged = false;
   if (!string.Equals(_lastAppliedDisplayText, nextText, StringComparison.Ordinal))
   {
    SetTextOnComponent(_curvedTextComponent, nextText);
    _lastAppliedDisplayText = nextText;
     textChanged = true;
   }

   var displayColor = GetAttentionDisplayColor();
    var styleChanged = false;
   if (!_hasAppliedDisplayStyle || _lastAppliedDisplayColor != displayColor)
   {
    TrySetColorOnComponent(_curvedTextComponent, displayColor);
    TrySetLeftAlignment(_curvedTextComponent);
    _lastAppliedDisplayColor = displayColor;
    _hasAppliedDisplayStyle = true;
     styleChanged = true;
   }

    if (runVisualMaintenance || textChanged || styleChanged)
    {
     PositionCurvedTextUnderPlayArea();
    }
  }
  else if (!hasText)
  {
   _lastAppliedDisplayText = string.Empty;
  }

  if (!_playInterceptDisabledDueToError)
  {
   try
   {
    EnsurePlayClickIntercept(hasText);
   }
   catch (Exception ex)
   {
    _playInterceptDisabledDueToError = true;
    _playConfirmModalVisible = false;
    RestorePlayClickIntercept();
      LogError("[UNBS] Play intercept disabled due to runtime error: " + ex.GetType().Name + " " + ex.Message);
   }
  }

  UpdatePlayButtonTint(hasText, runTintMaintenance);
 }

 private void EnsurePlayClickIntercept(bool attentionActive)
 {
  var target = ResolvePlayClickTarget();
  if (!ReferenceEquals(target, _playClickInterceptTarget))
  {
   RestorePlayClickIntercept();
  }

  if (target is null)
  {
   return;
  }

  if (_playInterceptInstalled)
  {
   if (!attentionActive)
   {
    _playConfirmModalVisible = false;
   }

   return;
  }

  if (!TryInstallPlayClickIntercept(target))
  {
   return;
  }

  _playClickInterceptTarget = target;
  _playInterceptInstalled = true;
 }

 private Component? ResolvePlayClickTarget()
 {
  if (_playSelectableTarget is not null && HasOnClickEvent(_playSelectableTarget))
  {
   return _playSelectableTarget;
  }

  if (_currentPlayAnchor is null)
  {
   return null;
  }

  foreach (var component in _currentPlayAnchor.GetComponentsInChildren<Component>(true))
  {
   if (component is not null && HasOnClickEvent(component))
   {
    return component;
   }
  }

  return null;
 }

 private bool TryInstallPlayClickIntercept(Component target)
 {
  if (!TryGetOnClickEvent(target, out var originalEvent) || originalEvent is null)
  {
   return false;
  }

  object? interceptEvent;
  try
  {
   interceptEvent = Activator.CreateInstance(originalEvent.GetType());
  }
  catch
  {
   return false;
  }

  if (interceptEvent is null)
  {
   return false;
  }

  var addListener = interceptEvent.GetType()
   .GetMethods(InstanceFlags)
   .FirstOrDefault(x =>
    string.Equals(x.Name, "AddListener", StringComparison.Ordinal)
    && x.GetParameters().Length == 1
    && typeof(Delegate).IsAssignableFrom(x.GetParameters()[0].ParameterType));
  var listenerType = addListener?.GetParameters().FirstOrDefault()?.ParameterType;
  var handler = GetType().GetMethod(nameof(OnInterceptedPlayClicked), InstanceFlags);
  if (addListener is null || listenerType is null || handler is null)
  {
   return false;
  }

  var del = Delegate.CreateDelegate(listenerType, this, handler, false);
  if (del is null)
  {
   return false;
  }

  addListener.Invoke(interceptEvent, new object[] { del });
  if (!TrySetOnClickEvent(target, interceptEvent))
  {
   return false;
  }

  _playOriginalOnClickEvent = originalEvent;
  _playInterceptOnClickEvent = interceptEvent;
  LogInfo("[UNBS] Play click intercept installed on " + DescribeComponent(target));
  return true;
 }

 private void OnInterceptedPlayClicked()
 {
  if (_playBypassOnce)
  {
   InvokeOriginalPlay();
   return;
  }

  var attentionActive = !string.IsNullOrWhiteSpace(_displayText);
  if (!attentionActive)
  {
   InvokeOriginalPlay();
   return;
  }

  if (_playConfirmModalVisible && Time.unscaledTime <= _playConfirmArmedUntil)
  {
    _playBypassOnce = true;
    var started = InvokeOriginalPlayWithTemporaryRestore();
    _playBypassOnce = false;
    _playConfirmModalVisible = !started;
    RequestTintRefresh();
    if (started)
    {
     LogInfo("[UNBS] Play confirmation accepted via second press.");
    }
    else
    {
     LogWarning("[UNBS] Play confirmation second press did not start song. Keeping confirm state.");
    }
   return;
  }

  _playConfirmModalVisible = true;
    var durationSeconds = GetPlayButtonConfirmDurationSeconds();
    _playConfirmArmedUntil = Time.unscaledTime + durationSeconds;
    RequestTintRefresh();
      LogInfo("[UNBS] Play confirmation armed. Press Play again within " + durationSeconds + " seconds to start.");
 }

 private bool InvokeOriginalPlayWithTemporaryRestore()
 {
  if (_playClickInterceptTarget is null || _playOriginalOnClickEvent is null)
  {
   InvokeOriginalPlay();
   return true;
  }

  var restored = TrySetOnClickEvent(_playClickInterceptTarget, _playOriginalOnClickEvent);
  if (!restored)
  {
   InvokeOriginalPlay();
   return true;
  }

  var invoked = TryInvokeOnClickFromComponent(_playClickInterceptTarget)
   || TryInvokeEventObject(_playOriginalOnClickEvent);

  if (_playInterceptOnClickEvent is not null)
  {
   TrySetOnClickEvent(_playClickInterceptTarget, _playInterceptOnClickEvent);
  }

  return invoked;
 }

 private void InvokeOriginalPlay()
 {
  if (_playOriginalOnClickEvent is null)
  {
   return;
  }

  TryInvokeEventObject(_playOriginalOnClickEvent);
 }

 private static bool TryInvokeOnClickFromComponent(Component component)
 {
  if (!TryGetOnClickEvent(component, out var evt) || evt is null)
  {
   return false;
  }

  return TryInvokeEventObject(evt);
 }

 private static bool TryInvokeEventObject(object evt)
 {
  var invoke = evt.GetType()
   .GetMethods(InstanceFlags)
   .FirstOrDefault(x => string.Equals(x.Name, "Invoke", StringComparison.Ordinal) && x.GetParameters().Length == 0);
  if (invoke is null)
  {
   return false;
  }

  invoke.Invoke(evt, null);
  return true;
 }

 private void RestorePlayClickIntercept()
 {
  if (_playClickInterceptTarget is not null && _playOriginalOnClickEvent is not null)
  {
   TrySetOnClickEvent(_playClickInterceptTarget, _playOriginalOnClickEvent);
  }

  _playClickInterceptTarget = null;
  _playOriginalOnClickEvent = null;
  _playInterceptOnClickEvent = null;
  _playInterceptInstalled = false;
  _playBypassOnce = false;
    _playConfirmArmedUntil = 0f;
 }

 private void ResetPlayIntercept()
 {
  _playConfirmModalVisible = false;
  _playInterceptDisabledDueToError = false;
  RestorePlayClickIntercept();
    RequestTintRefresh();
    InvalidatePlayButtonTintState();
 }

 private static bool HasOnClickEvent(Component component)
 {
  return TryGetOnClickEvent(component, out _);
 }

 private static bool TryGetOnClickEvent(Component component, out object? evt)
 {
  evt = null;
  var prop = component.GetType().GetProperty("onClick", InstanceFlags);
  if (prop?.CanRead != true)
  {
   return false;
  }

  evt = prop.GetValue(component);
  return evt is not null;
 }

 private static bool TrySetOnClickEvent(Component component, object evt)
{
  var type = component.GetType();

  var prop = type.GetProperty("onClick", InstanceFlags);
  if (prop?.CanWrite == true && prop.PropertyType.IsAssignableFrom(evt.GetType()))
  {
   prop.SetValue(component, evt);
   return true;
  }

  FieldInfo? field = null;
  var cursor = type;
  while (cursor is not null && field is null)
  {
   field = cursor.GetField("m_OnClick", InstanceFlags)
    ?? cursor.GetField("_onClick", InstanceFlags)
    ?? cursor.GetField("onClick", InstanceFlags);
   cursor = cursor.BaseType;
  }

  if (field is null || !field.FieldType.IsAssignableFrom(evt.GetType()))
  {
   return false;
  }

  field.SetValue(component, evt);
  return true;
 }

 private void CreateCurvedTextFromTemplate(Component template, Transform parent)
 {
   if (_curvedTextObject is not null)
   {
    Destroy(_curvedTextObject);
    _curvedTextObject = null;
    _curvedTextComponent = null;
    _curvedTextType = null;
   }

   _curvedTextType = template.GetType();
   _curvedTextObject = Instantiate(template.gameObject, parent, false);
   _curvedTextObject.name = "UnbsAttention.CurvedText";
   _curvedTextComponent = _curvedTextObject.GetComponent(_curvedTextType);
  var disabledGraphics = DisableNonTextGraphics(_curvedTextObject, _curvedTextComponent);
   if (_curvedTextComponent is not null)
   {
    SetTextOnComponent(_curvedTextComponent, string.Empty);
   }

   _curvedTextObject.SetActive(false);
   _lastAppliedDisplayText = string.Empty;
   _hasAppliedDisplayStyle = false;
   InvalidatePlayButtonTintState();

   if (IsDebugLoggingEnabled())
   {
    var templateText = TryGetTextFromComponent(template, out var sourceText)
     ? sourceText
     : "<unreadable>";
    LogInfo("[UNBS] Curved text template selected: " + DescribeComponent(template) + " text='" + templateText + "'");
      if (disabledGraphics > 0)
      {
       LogInfo("[UNBS] Disabled non-text graphics on curved text clone: " + disabledGraphics);
      }
   }
 }

   private static int DisableNonTextGraphics(GameObject root, Component? textComponent)
   {
    var disabled = 0;
    foreach (var component in root.GetComponentsInChildren<Component>(true))
    {
     if (component is null || ReferenceEquals(component, textComponent))
     {
      continue;
     }

     var typeName = component.GetType().Name;
     var isTextLike = typeName.IndexOf("Text", StringComparison.OrdinalIgnoreCase) >= 0
      || typeName.IndexOf("TMP", StringComparison.OrdinalIgnoreCase) >= 0;
     if (isTextLike)
     {
      continue;
     }

     var isGraphicLike = typeName.IndexOf("Image", StringComparison.OrdinalIgnoreCase) >= 0
      || typeName.IndexOf("Graphic", StringComparison.OrdinalIgnoreCase) >= 0;
     if (!isGraphicLike)
     {
      continue;
     }

     if (component is Behaviour behaviour && behaviour.enabled)
     {
      behaviour.enabled = false;
      disabled++;
      continue;
     }

     if (component is Renderer renderer && renderer.enabled)
     {
      renderer.enabled = false;
      disabled++;
     }
    }

    return disabled;
   }

 private void PositionCurvedTextUnderPlayArea()
 {
   if (_curvedTextObject is null || _currentPlayAnchor is null)
   {
    return;
   }

   var playRect = _currentPlayAnchor as RectTransform;
   var textRect = _curvedTextObject.transform as RectTransform;
   if (playRect is not null && textRect is not null)
   {
    var actionRowRect = playRect.parent as RectTransform;
    var hostParentRect = actionRowRect?.parent as RectTransform;
    var targetParent = hostParentRect ?? playRect.parent as RectTransform;

    if (actionRowRect is not null && hostParentRect is not null)
    {
     textRect.SetParent(hostParentRect, false);
     var siblingIndex = Mathf.Min(actionRowRect.GetSiblingIndex() + 1, hostParentRect.childCount - 1);
     textRect.SetSiblingIndex(siblingIndex);
     textRect.anchorMin = actionRowRect.anchorMin;
     textRect.anchorMax = actionRowRect.anchorMax;
    }
    else
    {
     textRect.SetParent(playRect.parent, false);
     textRect.anchorMin = playRect.anchorMin;
     textRect.anchorMax = playRect.anchorMax;
    }

    textRect.localRotation = Quaternion.identity;
    textRect.localScale = Vector3.one;

    var left = 0f;
    var right = 0f;
    var bottom = 0f;
    var top = 0f;
    var hasBounds = false;

    if (targetParent is not null)
    {
     AccumulateBounds(playRect, targetParent, ref hasBounds, ref left, ref right, ref bottom, ref top);

     if (_currentPracticeAnchor is RectTransform practiceRect)
     {
      AccumulateBounds(practiceRect, targetParent, ref hasBounds, ref left, ref right, ref bottom, ref top);
     }

     if (_currentActionAnchor is RectTransform actionRect)
     {
      AccumulateBounds(actionRect, targetParent, ref hasBounds, ref left, ref right, ref bottom, ref top);
     }
    }

    if (!hasBounds)
    {
     left = GetRectLeft(playRect);
     right = GetRectRight(playRect);
     bottom = GetRectBottom(playRect);
     top = GetRectTop(playRect);
     ExpandBoundsWithAnchor(_currentPracticeAnchor, playRect.parent, ref left, ref right, ref bottom);
     ExpandBoundsWithAnchor(_currentActionAnchor, playRect.parent, ref left, ref right, ref bottom);
     ExpandTopWithAnchor(_currentPracticeAnchor, playRect.parent, ref top);
     ExpandTopWithAnchor(_currentActionAnchor, playRect.parent, ref top);
    }

    var offsetX = _runtime?.GetAttentionPositionOffsetX() ?? 0;
    var offsetY = _runtime?.GetAttentionPositionOffsetY() ?? 0;
     var rowHeight = Mathf.Max(8f, top - bottom);
     var lineGap = Mathf.Max(6f, rowHeight * 0.18f);
     var textHeight = 16f;

     // Place on the next line under the last action-row element.
     textRect.pivot = new Vector2(0f, 1f);
     textRect.sizeDelta = new Vector2(Mathf.Clamp((right - left) + 18f, 140f, 420f), textHeight);
     textRect.anchoredPosition = new Vector2(left + offsetX, bottom - lineGap + offsetY);

    SetPlayTintTargets(playRect);
    return;
   }

   _curvedTextObject.transform.SetParent(_currentPlayAnchor.parent, false);
    var localOffsetX = ((_runtime?.GetAttentionPositionOffsetX() ?? 0) * 0.0015f);
    var localOffsetY = ((_runtime?.GetAttentionPositionOffsetY() ?? 0) * 0.0015f);
     _curvedTextObject.transform.localPosition = _currentPlayAnchor.localPosition + new Vector3(0.08f + localOffsetX, -0.24f + localOffsetY, 0f);
   _curvedTextObject.transform.localRotation = Quaternion.identity;
   _curvedTextObject.transform.localScale = Vector3.one;

  SetPlayTintTargets(_currentPlayAnchor);
 }

private void UpdatePlayButtonTint(bool attentionActive, bool allowDriftCheck, bool forceApply = false)
 {
  var confirmActive = attentionActive && _playConfirmModalVisible;
  var activeTint = confirmActive
   ? GetPlayButtonConfirmColor()
   : GetPlayButtonAttentionColor();
  var activeOutlineTint = DefaultPlayButtonOutlineColor;
  var activeLabelTint = GetPlayButtonAttentionTextColor();

  var stateChanged = !_hasAppliedTintState
   || _lastAppliedAttentionState != attentionActive
   || _lastAppliedConfirmState != confirmActive;

  var shouldApplyVisualTint = forceApply || stateChanged;
  if (!shouldApplyVisualTint
   && attentionActive
    && allowDriftCheck
   && IsPlayButtonTintDrifted(activeTint, activeOutlineTint, activeLabelTint))
  {
   shouldApplyVisualTint = true;
  }

  if (shouldApplyVisualTint)
  {
   var colorWriteOk = false;
   var outlineWriteOk = false;
   var labelWriteOk = false;
   var selectableWriteOk = false;

   if (_playTintTarget is not null)
   {
    colorWriteOk = ApplyComponentTint(
     _playTintTarget,
     ref _playTintOriginalSnapshot,
     attentionActive,
     activeTint);
   }

   if (_playOutlineTintTarget is not null)
   {
    outlineWriteOk = ApplyComponentTint(
     _playOutlineTintTarget,
     ref _playOutlineOriginalSnapshot,
     attentionActive,
     activeOutlineTint);
   }

   if (_playLabelTintTarget is not null)
   {
    labelWriteOk = ApplyComponentTint(
     _playLabelTintTarget,
     ref _playLabelOriginalSnapshot,
     attentionActive,
     activeLabelTint);
   }

   if (_playSelectableTarget is not null)
   {
    selectableWriteOk = TrySetSelectableTint(_playSelectableTarget, attentionActive, activeTint);
   }

   _hasAppliedTintState = true;
   _lastAppliedAttentionState = attentionActive;
   _lastAppliedConfirmState = confirmActive;

  if (IsDebugLoggingEnabled())
   {
   var now = Time.unscaledTime;
   if (stateChanged || now - _lastTintTraceAt >= 0.5f)
   {
    _lastTintTraceAt = now;
    _tintTraceCount++;
    TraceTintWrite(attentionActive, activeTint, colorWriteOk, outlineWriteOk, labelWriteOk, selectableWriteOk);
   }
   }
  }

  if (confirmActive)
  {
   UpdatePlayButtonConfirmLabel(true);
  }
  else if (_lastConfirmLabelActive)
  {
   UpdatePlayButtonConfirmLabel(false);
  }
 }

 private void InvalidatePlayButtonTintState()
 {
  _hasAppliedTintState = false;
  _lastConfirmLabelActive = false;
  _lastConfirmLabelSeconds = -1;
  _lastTintTraceAt = float.MinValue;
 }

 private bool IsPlayButtonTintDrifted(Color expectedTint, Color expectedOutline, Color expectedLabel)
 {
  if (_playTintTarget is not null
   && TryGetColorFromComponent(_playTintTarget, out var tintColor)
   && !IsApproxColor(tintColor, expectedTint))
  {
   return true;
  }

  if (_playOutlineTintTarget is not null
   && TryGetColorFromComponent(_playOutlineTintTarget, out var outlineColor)
   && !IsApproxColor(outlineColor, expectedOutline))
  {
   return true;
  }

  if (_playLabelTintTarget is not null
   && TryGetColorFromComponent(_playLabelTintTarget, out var labelColor)
   && !IsApproxColor(labelColor, expectedLabel))
  {
   return true;
  }

  return false;
 }

 private static bool IsApproxColor(Color left, Color right)
 {
  const float epsilon = 0.02f;
  return Mathf.Abs(left.r - right.r) <= epsilon
   && Mathf.Abs(left.g - right.g) <= epsilon
   && Mathf.Abs(left.b - right.b) <= epsilon
   && Mathf.Abs(left.a - right.a) <= epsilon;
 }

 private static bool ApplyComponentTint(
  Component target,
  ref ColorMemberSnapshot? originalSnapshot,
  bool attentionActive,
  Color activeTint)
 {
  originalSnapshot ??= CaptureColorMemberSnapshot(target);
  if (originalSnapshot is null)
  {
   return false;
  }

  if (attentionActive)
  {
   return TryApplyTintFromSnapshot(target, originalSnapshot, activeTint);
  }

  return RestoreColorMemberSnapshot(target, originalSnapshot);
 }

 private void SetPlayTintTargets(Transform root)
 {
  if (ReferenceEquals(_lastTintTargetRoot, root) && _playTintTarget != null)
  {
   return;
  }

  var nextTintTarget = FindTintTarget(root);
  var nextOutlineTarget = FindOutlineTintTarget(root);
    var nextLabelTarget = FindPlayLabelTintTarget(root);
  var nextSelectableTarget = FindSelectableTarget(root);

  var changed = !ReferenceEquals(_playTintTarget, nextTintTarget)
   || !ReferenceEquals(_playOutlineTintTarget, nextOutlineTarget)
     || !ReferenceEquals(_playLabelTintTarget, nextLabelTarget)
   || !ReferenceEquals(_playSelectableTarget, nextSelectableTarget);
  if (changed)
  {
   RestorePlayButtonVisualState();
   InvalidatePlayButtonTintState();
  }

  _playTintTarget = nextTintTarget;
  _playOutlineTintTarget = nextOutlineTarget;
  _playLabelTintTarget = nextLabelTarget;
  _playSelectableTarget = nextSelectableTarget;
  _lastTintTargetRoot = root;

    RebindPlayPointerEventRelay(nextSelectableTarget);
    if (changed)
    {
     RequestTintRefresh();
    }

  if (changed)
  {
   TracePlayTargets(root, nextTintTarget, nextOutlineTarget, nextLabelTarget, nextSelectableTarget);
  }
 }

   private void RebindPlayPointerEventRelay(Component? target)
   {
    if (ReferenceEquals(_playPointerRelayTarget, target) && _playPointerEventRelay is not null)
    {
     return;
    }

    ClearPlayPointerEventRelay();
    if (target is null)
    {
     return;
    }

    _playPointerRelayTarget = target;
    _playPointerEventRelay = target.GetComponent<PlayPointerEventRelay>();
    if (_playPointerEventRelay is null)
    {
     _playPointerEventRelay = target.gameObject.AddComponent<PlayPointerEventRelay>();
    }

    _playPointerHovering = false;
    _playPointerEventRelay.OnHoverChanged = OnPlayPointerHoverChanged;
    _playPointerEventRelay.OnStateChanged = OnPlayPointerStateChanged;
   }

   private void ClearPlayPointerEventRelay()
   {
    if (_playPointerEventRelay is not null)
    {
     _playPointerEventRelay.OnHoverChanged = null;
     _playPointerEventRelay.OnStateChanged = null;
     Destroy(_playPointerEventRelay);
    }

    _playPointerHovering = false;
    _playPointerEventRelay = null;
    _playPointerRelayTarget = null;
   }

     private void OnPlayPointerHoverChanged(bool isHovering)
     {
    _playPointerHovering = isHovering;
    RequestTintRefresh();
     }

   private void OnPlayPointerStateChanged()
   {
    RequestTintRefresh();
   }

 private void TracePlayAnchorIfChanged(Transform playAnchor)
 {
  if (!IsDebugLoggingEnabled())
  {
   return;
  }

  var path = BuildTransformPath(playAnchor);
  if (string.Equals(_lastPlayAnchorPath, path, StringComparison.Ordinal))
  {
   return;
  }

  _lastPlayAnchorPath = path;
  LogInfo("[UNBS] Play anchor changed: " + path + " (" + playAnchor.GetType().Name + ")");
 }

 private bool IsDebugLoggingEnabled()
 {
  return _runtime?.GetDebugLoggingEnabled() ?? false;
 }

 private void LogInfo(string message)
 {
  if (IsDebugLoggingEnabled())
  {
   Debug.Log(message);
  }
 }

 private void LogWarning(string message)
 {
  if (IsDebugLoggingEnabled())
  {
   Debug.LogWarning(message);
  }
 }

 private void LogError(string message)
 {
  if (IsDebugLoggingEnabled())
  {
   Debug.LogError(message);
  }
 }

 private static string BuildTransformPath(Transform transform)
 {
  var stack = new Stack<string>();
  var cursor = transform;
  while (cursor is not null)
  {
   stack.Push(cursor.name);
   cursor = cursor.parent;
  }

  return string.Join("/", stack);
 }

 private void TracePlayTargets(Transform root, Component? tintTarget, Component? outlineTarget, Component? labelTarget, Component? selectableTarget)
 {
  if (!IsDebugLoggingEnabled())
  {
   return;
  }

  LogInfo("[UNBS] SetPlayTintTargets root=" + BuildTransformPath(root));
  LogInfo("[UNBS] Tint target=" + DescribeComponent(tintTarget));
  LogInfo("[UNBS] Outline target=" + DescribeComponent(outlineTarget));
  LogInfo("[UNBS] Label target=" + DescribeComponent(labelTarget));
  LogInfo("[UNBS] Selectable target=" + DescribeComponent(selectableTarget));

  var candidates = root.GetComponentsInChildren<Component>(true)
   .Where(x => x is not null)
   .Where(x => HasColorMember(x) || HasSelectableColors(x))
   .ToList();

  LogInfo("[UNBS] Color candidate count=" + candidates.Count);
  foreach (var component in candidates.Take(60))
  {
   var info = DescribeComponent(component);
   var colorText = TryGetColorFromComponent(component, out var c)
    ? $" color=({c.r:F2},{c.g:F2},{c.b:F2},{c.a:F2})"
    : string.Empty;
   var selectableText = string.Empty;
   if (TryGetSelectableColors(component, out var block))
   {
    selectableText = " selectable=" + DescribeSelectableColors(block);
   }

  LogInfo("[UNBS] Candidate " + info + colorText + selectableText);
  }

  if (candidates.Count > 60)
  {
   LogInfo("[UNBS] Candidate log truncated: " + (candidates.Count - 60) + " entries omitted");
  }
 }

 private void TraceTintWrite(bool attentionActive, Color activeTint, bool colorWriteOk, bool outlineWriteOk, bool labelWriteOk, bool selectableWriteOk)
 {
  if (!IsDebugLoggingEnabled())
  {
   return;
  }

  var tintText = $"({activeTint.r:F2},{activeTint.g:F2},{activeTint.b:F2},{activeTint.a:F2})";
  var targetNow = _playTintTarget is not null && TryGetColorFromComponent(_playTintTarget, out var nowColor)
   ? $" nowColor=({nowColor.r:F2},{nowColor.g:F2},{nowColor.b:F2},{nowColor.a:F2})"
   : " nowColor=<unreadable>";

  var outlineNow = _playOutlineTintTarget is not null && TryGetColorFromComponent(_playOutlineTintTarget, out var outlineColor)
   ? $" outlineNow=({outlineColor.r:F2},{outlineColor.g:F2},{outlineColor.b:F2},{outlineColor.a:F2})"
   : " outlineNow=<unreadable>";

  var labelNow = _playLabelTintTarget is not null && TryGetColorFromComponent(_playLabelTintTarget, out var labelColor)
   ? $" labelNow=({labelColor.r:F2},{labelColor.g:F2},{labelColor.b:F2},{labelColor.a:F2})"
   : " labelNow=<unreadable>";

  var selectableNow = string.Empty;
  if (_playSelectableTarget is not null && TryGetSelectableColors(_playSelectableTarget, out var block))
  {
   selectableNow = " nowSelectable=" + DescribeSelectableColors(block);
  }

  LogInfo(
   "[UNBS] TintTrace#" + _tintTraceCount
   + " active=" + attentionActive
   + " targetTint=" + tintText
   + " colorWriteOk=" + colorWriteOk
   + " outlineWriteOk=" + outlineWriteOk
   + " labelWriteOk=" + labelWriteOk
   + " selectableWriteOk=" + selectableWriteOk
   + " tintTarget=" + DescribeComponent(_playTintTarget)
   + " outlineTarget=" + DescribeComponent(_playOutlineTintTarget)
   + " labelTarget=" + DescribeComponent(_playLabelTintTarget)
   + " selectableTarget=" + DescribeComponent(_playSelectableTarget)
   + targetNow
   + outlineNow
   + labelNow
   + selectableNow);
 }

 private static string DescribeComponent(Component? component)
 {
  if (component is null)
  {
   return "<null>";
  }

  var path = BuildTransformPath(component.transform);
  return component.GetType().FullName + " @ " + path;
 }

 private static string DescribeSelectableColors(object colors)
 {
  var type = colors.GetType();
  Color Read(string fieldName)
  {
   var field = type.GetField(fieldName, InstanceFlags);
   if (field?.FieldType != typeof(Color))
   {
    return default;
   }

   return (Color)field.GetValue(colors);
  }

  var n = Read("normalColor");
  var h = Read("highlightedColor");
  var p = Read("pressedColor");
  var s = Read("selectedColor");
  return $"N({n.r:F2},{n.g:F2},{n.b:F2},{n.a:F2}) H({h.r:F2},{h.g:F2},{h.b:F2},{h.a:F2}) P({p.r:F2},{p.g:F2},{p.b:F2},{p.a:F2}) S({s.r:F2},{s.g:F2},{s.b:F2},{s.a:F2})";
 }

 private async Task RefreshAsync(CancellationToken cancellationToken)
 {
  _refreshInFlight = true;
  try
  {
  if (!_songSelectionActive)
  {
   _displayText = null;
    _lastLookupSnapshotKey = string.Empty;
   _lastDataRevision = -1;
   return;
  }

   var level = FindCurrentBeatmapLevel();
   if (level is null)
   {
    _displayText = null;
      _lastLookupSnapshotKey = string.Empty;
    return;
   }

   var snapshot = BuildSnapshot(level);
   if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.LevelId))
   {
    _displayText = null;
     _lastLookupSnapshotKey = string.Empty;
    _lastDataRevision = -1;
    return;
   }

    var snapshotCacheKey = BuildSnapshotCacheKey(snapshot);
   var dataRevision = _runtime!.GetDataRevision();

    if (string.Equals(_lastLookupSnapshotKey, snapshotCacheKey, StringComparison.Ordinal)
      && _lastDataRevision == dataRevision)
   {
    return;
   }

   var text = await _runtime!
    .BuildAttentionTextAsync(snapshot, cancellationToken)
    .ConfigureAwait(false);

  if (!_songSelectionActive)
  {
   _displayText = null;
    _lastLookupSnapshotKey = string.Empty;
   _lastDataRevision = -1;
   return;
  }

   _displayText = text;
    _lastLookupSnapshotKey = snapshotCacheKey;
   _lastDataRevision = dataRevision;
  }
  catch
  {
   _displayText = null;
    _lastLookupSnapshotKey = string.Empty;
    _lastDataRevision = -1;
  }
  finally
  {
   _refreshInFlight = false;
  }
 }

 private object? FindCurrentBeatmapLevel()
 {
  var detailController = ResolveDetailController();
  if (detailController is null)
  {
   return null;
  }

  var type = detailController.GetType();
  var levelField = type.GetField("_beatmapLevel", InstanceFlags);
  if (levelField?.GetValue(detailController) is object levelFromField)
  {
   return levelFromField;
  }

  var levelProperty = type.GetProperty("beatmapLevel", InstanceFlags);
  if (levelProperty?.GetValue(detailController) is object levelFromProperty)
  {
   return levelFromProperty;
  }

  return null;
 }

private static MonoBehaviour? FindStandardLevelDetailViewController(bool requireUsable)
 {
    var allMonoBehaviours = Resources.FindObjectsOfTypeAll<MonoBehaviour>();
    foreach (var monoBehaviour in allMonoBehaviours)
  {
   if (monoBehaviour is null)
   {
    continue;
   }

   var type = monoBehaviour.GetType();
   if (!string.Equals(type.Name, "StandardLevelDetailViewController", StringComparison.Ordinal))
   {
    continue;
   }

   if (requireUsable && !IsUsableDetailController(monoBehaviour))
   {
    continue;
   }

     return monoBehaviour;
  }

  return null;
 }

 private static bool IsUsableDetailController(MonoBehaviour controller)
 {
  if (!controller.isActiveAndEnabled)
  {
   return false;
  }

  var go = controller.gameObject;
  if (go is null || !go.activeInHierarchy)
  {
   return false;
  }

  var type = controller.GetType();
  var hierarchyProp = type.GetProperty("isInViewControllerHierarchy", InstanceFlags);
  if (hierarchyProp?.PropertyType == typeof(bool)
   && hierarchyProp.GetValue(controller) is bool inHierarchy
   && !inHierarchy)
  {
   return false;
  }

  return true;
 }

 private static Component? FindCurvedTextTemplate(Transform root, Transform preferredAnchor)
 {
 static bool IsTextComponent(Component component)
 {
  var typeName = component.GetType().Name;
  return string.Equals(typeName, "CurvedTextMeshPro", StringComparison.Ordinal)
   || string.Equals(typeName, "TextMeshProUGUI", StringComparison.Ordinal);
 }

 static int ScoreComponent(Component component, Transform preferred)
 {
  var score = 0;
  var transform = component.transform;

  if (transform == preferred || transform.IsChildOf(preferred))
  {
   score += 240;
  }

  if (transform.parent == preferred)
  {
   score += 40;
  }

  var path = BuildTransformPath(transform);
  if (path.IndexOf("ActionButton", StringComparison.OrdinalIgnoreCase) >= 0
   || path.IndexOf("PlayButton", StringComparison.OrdinalIgnoreCase) >= 0)
  {
   score += 140;
  }

  if (path.IndexOf("PracticeButton", StringComparison.OrdinalIgnoreCase) >= 0)
  {
   score -= 180;
  }

  if (TryGetTextFromComponent(component, out var text))
  {
   var trimmed = text?.Trim() ?? string.Empty;
   if (trimmed.Length == 0)
   {
    score += 8;
   }

   if (trimmed.IndexOf("play", StringComparison.OrdinalIgnoreCase) >= 0
    || trimmed.IndexOf("プレイ", StringComparison.Ordinal) >= 0)
   {
    score += 80;
   }

   if (trimmed.IndexOf("practice", StringComparison.OrdinalIgnoreCase) >= 0
    || trimmed.IndexOf("練習", StringComparison.Ordinal) >= 0)
   {
    score -= 120;
   }
  }

  return score;
 }

 Component? best = null;
 var bestScore = int.MinValue;

 foreach (var component in root.GetComponentsInChildren<Component>(true))
 {
  if (component is null)
  {
   continue;
  }

  if (!IsTextComponent(component) || !HasTextMember(component))
  {
   continue;
  }

  var score = ScoreComponent(component, preferredAnchor);
  if (score <= bestScore)
  {
   continue;
  }

  best = component;
  bestScore = score;
 }

 return best;
 }

 private static bool HasTextMember(Component component)
 {
  var type = component.GetType();
  var prop = type.GetProperty("text", InstanceFlags);
  if (prop?.CanWrite == true && prop.PropertyType == typeof(string))
  {
  return true;
  }

  var field = type.GetField("text", InstanceFlags);
  return field?.FieldType == typeof(string);
 }

   private static Component? FindTintTarget(Transform root)
   {
    foreach (var component in root.GetComponents<Component>())
    {
     if (component is null)
     {
    continue;
     }

     var typeName = component.GetType().Name;
       if ((typeName.IndexOf("Image", StringComparison.OrdinalIgnoreCase) >= 0
         || typeName.IndexOf("Graphic", StringComparison.OrdinalIgnoreCase) >= 0)
       && HasColorMember(component))
     {
    return component;
     }
    }

    foreach (var component in root.GetComponentsInChildren<Component>(true))
    {
     if (component is null)
     {
    continue;
     }

     var typeName = component.GetType().Name;
       if ((typeName.IndexOf("Image", StringComparison.OrdinalIgnoreCase) >= 0
         || typeName.IndexOf("Graphic", StringComparison.OrdinalIgnoreCase) >= 0)
       && HasColorMember(component))
     {
    return component;
     }
    }

    return null;
   }

 private static Component? FindOutlineTintTarget(Transform root)
 {
  var preferred = root.GetComponentsInChildren<Component>(true)
   .Where(x => x is not null)
   .FirstOrDefault(x =>
   {
    var name = x.transform.name;
    return (name.IndexOf("Underline", StringComparison.OrdinalIgnoreCase) >= 0
      || name.IndexOf("Outline", StringComparison.OrdinalIgnoreCase) >= 0
      || name.IndexOf("Border", StringComparison.OrdinalIgnoreCase) >= 0)
     && HasColorMember(x);
   });
  if (preferred is not null)
  {
   return preferred;
  }

  return null;
 }

 private static Component? FindSelectableTarget(Transform root)
 {
  foreach (var component in root.GetComponents<Component>())
  {
   if (component is null)
   {
    continue;
   }

     if (HasSelectableColors(component) && HasOnClickEvent(component))
     {
      return component;
     }
    }

    foreach (var component in root.GetComponentsInChildren<Component>(true))
    {
     if (component is null)
     {
      continue;
     }

     if (HasSelectableColors(component) && HasOnClickEvent(component))
     {
      return component;
     }
    }

    foreach (var component in root.GetComponents<Component>())
    {
     if (component is null)
     {
      continue;
     }

   if (HasSelectableColors(component))
   {
    return component;
   }
  }

  foreach (var component in root.GetComponentsInChildren<Component>(true))
  {
   if (component is null)
   {
    continue;
   }

   if (HasSelectableColors(component))
   {
    return component;
   }
  }

  return null;
 }

 private static Component? FindPlayLabelTintTarget(Transform root)
 {
  var preferred = root.GetComponentsInChildren<Component>(true)
   .Where(x => x is not null)
   .FirstOrDefault(x =>
   {
    var typeName = x.GetType().Name;
    if (typeName.IndexOf("Text", StringComparison.OrdinalIgnoreCase) < 0)
    {
     return false;
    }

    return HasColorMember(x);
   });

  if (preferred is not null)
  {
   return preferred;
  }

  return null;
 }

   private static bool HasColorMember(Component component)
   {
  return TryGetColorMember(component, out _);
   }

 private static bool HasSelectableColors(Component component)
 {
  var colorsType = GetSelectableColorsType(component);
  if (colorsType is null)
  {
   return false;
  }

  return HasColorField(colorsType, "normalColor")
   && HasColorField(colorsType, "highlightedColor")
   && HasColorField(colorsType, "pressedColor")
   && HasColorField(colorsType, "selectedColor");
 }

 private static Type? GetSelectableColorsType(Component component)
 {
  var type = component.GetType();
  var property = type.GetProperty("colors", InstanceFlags);
  if (property?.CanRead == true)
  {
   return property.PropertyType;
  }

  var field = FindSelectableColorsField(type);
  return field?.FieldType;
 }

 private static FieldInfo? FindSelectableColorsField(Type type)
 {
  for (var cursor = type; cursor is not null; cursor = cursor.BaseType)
  {
   var field = cursor.GetField("m_Colors", InstanceFlags)
    ?? cursor.GetField("_colors", InstanceFlags)
    ?? cursor.GetField("colors", InstanceFlags);
   if (field is not null)
   {
    return field;
   }
  }

  return null;
 }

 private static bool HasColorField(Type type, string fieldName)
 {
  var field = type.GetField(fieldName, InstanceFlags);
  return field?.FieldType == typeof(Color);
 }

 private static Transform? FindAnchorByNames(MonoBehaviour detailController, params string[] names)
 {
  var type = detailController.GetType();
  foreach (var name in names)
  {
  var field = type.GetField(name, InstanceFlags);
  if (field?.GetValue(detailController) is Component componentFromField)
  {
   return componentFromField.transform;
  }

  var property = type.GetProperty(name, InstanceFlags);
  if (property?.GetValue(detailController) is Component componentFromProperty)
  {
   return componentFromProperty.transform;
  }
  }

  return null;
 }

 private static Transform? FindChildByNames(Transform root, params string[] names)
 {
  foreach (var transform in root.GetComponentsInChildren<Transform>(true))
  {
   if (transform is null)
   {
    continue;
   }

   foreach (var name in names)
   {
    if (string.Equals(transform.name, name, StringComparison.OrdinalIgnoreCase))
    {
     return transform;
    }
   }
  }

  return null;
 }

 private void UpdatePlayButtonConfirmLabel(bool confirmActive)
 {
  if (_playLabelTintTarget is null)
  {
   _lastConfirmLabelActive = false;
   _lastConfirmLabelSeconds = -1;
   return;
  }

  if (!_playLabelOriginalTextCaptured)
  {
   if (TryGetTextFromComponent(_playLabelTintTarget, out var original))
   {
    _playLabelOriginalText = original;
    _playLabelOriginalTextCaptured = true;
   }
  }

  if (!_playLabelOriginalTextCaptured)
  {
   return;
  }

  if (confirmActive)
  {
   var remainingSeconds = Mathf.Max(0, Mathf.CeilToInt(_playConfirmArmedUntil - Time.unscaledTime));
   if (_lastConfirmLabelActive && _lastConfirmLabelSeconds == remainingSeconds)
   {
    return;
   }

   var confirmLabel = GetPlayButtonConfirmText() + "(" + remainingSeconds + ")";
   SetTextOnComponent(_playLabelTintTarget, confirmLabel);
   _lastConfirmLabelActive = true;
   _lastConfirmLabelSeconds = remainingSeconds;
   return;
  }

  if (!_lastConfirmLabelActive)
  {
   return;
  }

  if (_playLabelOriginalText is not null)
  {
   SetTextOnComponent(_playLabelTintTarget, _playLabelOriginalText);
  }

  _lastConfirmLabelActive = false;
  _lastConfirmLabelSeconds = -1;
 }

 private static void ExpandBoundsWithAnchor(Transform? anchor, Transform? expectedParent, ref float left, ref float right, ref float bottom)
 {
  if (anchor is not RectTransform rect || expectedParent is null)
  {
  return;
  }

  if (rect.parent != expectedParent)
  {
  return;
  }

  left = Mathf.Min(left, GetRectLeft(rect));
  right = Mathf.Max(right, GetRectRight(rect));
  bottom = Mathf.Min(bottom, GetRectBottom(rect));
 }

 private static float GetRectLeft(RectTransform rect)
 {
  return rect.anchoredPosition.x - (rect.rect.width * rect.pivot.x);
 }

 private static float GetRectRight(RectTransform rect)
 {
  return rect.anchoredPosition.x + (rect.rect.width * (1f - rect.pivot.x));
 }

 private static float GetRectBottom(RectTransform rect)
 {
  return rect.anchoredPosition.y - (rect.rect.height * rect.pivot.y);
 }

 private static float GetRectTop(RectTransform rect)
 {
  return rect.anchoredPosition.y + (rect.rect.height * (1f - rect.pivot.y));
 }

 private static void ExpandTopWithAnchor(Transform? anchor, Transform? expectedParent, ref float top)
 {
  if (anchor is not RectTransform rect || expectedParent is null)
  {
   return;
  }

  if (rect.parent != expectedParent)
  {
   return;
  }

  top = Mathf.Max(top, GetRectTop(rect));
 }

 private static void AccumulateBounds(
  RectTransform rect,
  RectTransform targetParent,
  ref bool hasBounds,
  ref float left,
  ref float right,
  ref float bottom,
  ref float top)
 {
  if (!TryGetBoundsInParentSpace(rect, targetParent, out var rectLeft, out var rectRight, out var rectBottom, out var rectTop))
  {
   return;
  }

  if (!hasBounds)
  {
   left = rectLeft;
   right = rectRight;
   bottom = rectBottom;
   top = rectTop;
   hasBounds = true;
   return;
  }

  left = Mathf.Min(left, rectLeft);
  right = Mathf.Max(right, rectRight);
  bottom = Mathf.Min(bottom, rectBottom);
  top = Mathf.Max(top, rectTop);
 }

 private static bool TryGetBoundsInParentSpace(
  RectTransform rect,
  RectTransform targetParent,
  out float left,
  out float right,
  out float bottom,
  out float top)
 {
  left = 0f;
  right = 0f;
  bottom = 0f;
  top = 0f;

  if (rect is null || targetParent is null)
  {
   return false;
  }

  var corners = new Vector3[4];
  rect.GetWorldCorners(corners);

  var local0 = targetParent.InverseTransformPoint(corners[0]);
  var local1 = targetParent.InverseTransformPoint(corners[1]);
  var local2 = targetParent.InverseTransformPoint(corners[2]);
  var local3 = targetParent.InverseTransformPoint(corners[3]);

  left = Mathf.Min(local0.x, local1.x, local2.x, local3.x);
  right = Mathf.Max(local0.x, local1.x, local2.x, local3.x);
  bottom = Mathf.Min(local0.y, local1.y, local2.y, local3.y);
  top = Mathf.Max(local0.y, local1.y, local2.y, local3.y);
  return true;
 }

 private static SongSelectionSnapshot? BuildSnapshot(object beatmapLevel)
 {
  var type = beatmapLevel.GetType();

  string? GetString(params string[] names)
  {
   foreach (var name in names)
   {
    var value = TryReadStringMember(type, beatmapLevel, name);
    if (!string.IsNullOrWhiteSpace(value))
    {
     return value;
    }
   }

   return null;
  }

  var levelId = GetString("levelID", "levelId", "_levelID", "_levelId");
  if (string.IsNullOrWhiteSpace(levelId))
  {
   return null;
  }

  var bsrCandidate = GetString(
   "bsrId",
   "bsrID",
   "_bsrId",
   "_bsrID",
   "beatsaverKey",
   "beatSaverKey",
   "_beatsaverKey",
   "_beatSaverKey",
   "songKey",
   "_songKey",
   "key",
   "_key");
  var bsrId = AttentionMatcherIndex.TryParseBsrHex(bsrCandidate, out _)
   ? bsrCandidate?.Trim()
   : null;

  return new SongSelectionSnapshot
  {
   LevelId = levelId,
   SongName = GetString("songName", "_songName", "songDisplayName"),
   SongSubName = GetString("songSubName", "_songSubName"),
   SongAuthorName = GetString("songAuthorName", "_songAuthorName"),
   LevelAuthorName = GetString("levelAuthorName", "_levelAuthorName", "allMappers"),
   BsrId = bsrId,
   BeatSaverDescription = null,
  };
 }

 private static string BuildSnapshotCacheKey(SongSelectionSnapshot snapshot)
 {
  return string.Join("|", new[]
  {
   snapshot.LevelId?.Trim() ?? string.Empty,
   snapshot.BsrId?.Trim() ?? string.Empty,
   snapshot.SongName?.Trim() ?? string.Empty,
   snapshot.SongSubName?.Trim() ?? string.Empty,
   snapshot.SongAuthorName?.Trim() ?? string.Empty,
   snapshot.LevelAuthorName?.Trim() ?? string.Empty,
  });
 }

 private static string? TryReadStringMember(Type type, object instance, string memberName)
 {
  for (var cursor = type; cursor is not null; cursor = cursor.BaseType)
  {
   var field = cursor.GetField(memberName, InstanceFlags)
    ?? cursor.GetFields(InstanceFlags).FirstOrDefault(x => string.Equals(x.Name, memberName, StringComparison.OrdinalIgnoreCase));
   if (field?.FieldType == typeof(string) && field.GetValue(instance) is string fieldValue && !string.IsNullOrWhiteSpace(fieldValue))
   {
    return fieldValue;
   }

   var property = cursor.GetProperty(memberName, InstanceFlags)
    ?? cursor.GetProperties(InstanceFlags).FirstOrDefault(x => string.Equals(x.Name, memberName, StringComparison.OrdinalIgnoreCase));
   if (property?.CanRead == true
    && property.PropertyType == typeof(string)
    && property.GetIndexParameters().Length == 0
    && property.GetValue(instance) is string propertyValue
    && !string.IsNullOrWhiteSpace(propertyValue))
   {
    return propertyValue;
   }
  }

  return null;
 }

 private static void SetTextOnComponent(Component textComponent, string text)
 {
  var type = textComponent.GetType();
  var prop = type.GetProperty("text", InstanceFlags);
  if (prop?.CanWrite == true && prop.PropertyType == typeof(string))
  {
   prop.SetValue(textComponent, text);
   return;
  }

  var field = type.GetField("text", InstanceFlags);
  if (field?.FieldType == typeof(string))
  {
   field.SetValue(textComponent, text);
  }
 }

 private static bool TryGetTextFromComponent(Component textComponent, out string text)
 {
  text = string.Empty;
  var type = textComponent.GetType();
  var prop = type.GetProperty("text", InstanceFlags);
  if (prop?.CanRead == true && prop.PropertyType == typeof(string) && prop.GetValue(textComponent) is string propText)
  {
   text = propText;
   return true;
  }

  var field = type.GetField("text", InstanceFlags);
  if (field?.FieldType == typeof(string) && field.GetValue(textComponent) is string fieldText)
  {
   text = fieldText;
   return true;
  }

  return false;
 }

 private static void TrySetLeftAlignment(Component textComponent)
 {
  var type = textComponent.GetType();
  var alignmentProp = type.GetProperty("alignment", InstanceFlags);
  if (alignmentProp?.CanWrite != true)
  {
   return;
  }

  var enumType = alignmentProp.PropertyType;
  if (!enumType.IsEnum)
  {
   return;
  }

  object? value = null;
  var names = new[] { "TopLeft", "UpperLeft", "Left", "MidlineLeft", "BottomLeft" };
  foreach (var name in names)
  {
   if (Enum.GetNames(enumType).Any(x => string.Equals(x, name, StringComparison.Ordinal)))
   {
    value = Enum.Parse(enumType, name);
    break;
   }
  }

  if (value is not null)
  {
   alignmentProp.SetValue(textComponent, value);
  }
 }

 private static bool TrySetColorOnComponent(Component textComponent, Color color)
 {
  return TrySetColorMember(textComponent, color);
 }

 private static bool TryGetColorFromComponent(Component component, out Color color)
 {
  return TryGetColorMember(component, out color);
 }

 private bool TrySetSelectableTint(Component selectable, bool attentionActive, Color activeTint)
 {
  if (!_playOriginalSelectableColorsCaptured)
  {
   if (!TryGetSelectableColors(selectable, out var originalColors))
   {
    return false;
   }

   _playOriginalSelectableColors = originalColors;
   _playOriginalSelectableColorsCaptured = true;
  }

  if (_playOriginalSelectableColors is null)
  {
   return false;
  }

  var nextColors = attentionActive
   ? BuildTintedSelectableColors(_playOriginalSelectableColors, activeTint)
   : _playOriginalSelectableColors;
  return TrySetSelectableColors(selectable, nextColors);
 }

 private static bool TryGetSelectableColors(Component component, out object colors)
 {
  colors = default!;
  var type = component.GetType();
  var property = type.GetProperty("colors", InstanceFlags);
  if (property?.CanRead == true)
  {
   colors = property.GetValue(component)!;
   return colors is not null;
  }

  var field = FindSelectableColorsField(type);
  if (field is null)
  {
   return false;
  }

  colors = field.GetValue(component)!;
  return colors is not null;
 }

 private static bool TrySetSelectableColors(Component component, object colors)
 {
  var type = component.GetType();
  var property = type.GetProperty("colors", InstanceFlags);
  if (property?.CanWrite == true)
  {
   property.SetValue(component, colors);
   return true;
  }

  var field = FindSelectableColorsField(type);
  if (field is null)
  {
   return false;
  }

  field.SetValue(component, colors);
  return true;
 }

 private static object BuildTintedSelectableColors(object originalColors, Color tint)
 {
  var colorBlockType = originalColors.GetType();
  var tinted = originalColors;

  TintColorField(ref tinted, colorBlockType, "normalColor", tint);
  TintColorField(ref tinted, colorBlockType, "highlightedColor", tint);
  TintColorField(ref tinted, colorBlockType, "pressedColor", tint);
  TintColorField(ref tinted, colorBlockType, "selectedColor", tint);
  TintColorField(ref tinted, colorBlockType, "disabledColor", tint);
    SetFloatField(ref tinted, colorBlockType, "colorMultiplier", 1f);
  SetFloatField(ref tinted, colorBlockType, "fadeDuration", 0f);

  return tinted;
 }

 private static void TintColorField(ref object boxedStruct, Type type, string fieldName, Color tint)
 {
  var field = type.GetField(fieldName, InstanceFlags);
  if (field?.FieldType != typeof(Color))
  {
   return;
  }

  var original = (Color)field.GetValue(boxedStruct);
    var next = new Color(tint.r, tint.g, tint.b, tint.a);
  field.SetValue(boxedStruct, next);
 }

 private static void SetFloatField(ref object boxedStruct, Type type, string fieldName, float value)
 {
  var field = type.GetField(fieldName, InstanceFlags);
  if (field?.FieldType != typeof(float))
  {
   return;
  }

  field.SetValue(boxedStruct, value);
 }

 private static readonly string[] ColorMemberNames =
 {
  "color",
  "_color",
  "color0",
  "_color0",
  "color1",
  "_color1",
 };

   private sealed class ColorMemberSnapshot
   {
    public List<ColorMemberEntry> Entries { get; } = new();
   }

   private sealed class ColorMemberEntry
   {
    public string Name { get; set; } = string.Empty;

    public bool IsProperty { get; set; }

    public Color OriginalColor { get; set; }
   }

   private static ColorMemberSnapshot? CaptureColorMemberSnapshot(Component component)
   {
    var type = component.GetType();
    var snapshot = new ColorMemberSnapshot();

    foreach (var name in ColorMemberNames)
    {
     var prop = type.GetProperty(name, InstanceFlags);
     if (prop?.CanRead == true && prop.CanWrite && prop.PropertyType == typeof(Color))
     {
      snapshot.Entries.Add(new ColorMemberEntry
      {
       Name = name,
       IsProperty = true,
       OriginalColor = (Color)prop.GetValue(component),
      });
      continue;
     }

     var field = type.GetField(name, InstanceFlags);
     if (field?.FieldType == typeof(Color))
     {
      snapshot.Entries.Add(new ColorMemberEntry
      {
       Name = name,
       IsProperty = false,
       OriginalColor = (Color)field.GetValue(component),
      });
     }
    }

    return snapshot.Entries.Count == 0 ? null : snapshot;
   }

   private static bool TryApplyTintFromSnapshot(Component component, ColorMemberSnapshot snapshot, Color tint)
   {
    var wroteAny = false;
    foreach (var entry in snapshot.Entries)
    {
       var next = new Color(tint.r, tint.g, tint.b, tint.a);
     if (TrySetColorMemberByName(component, entry.Name, entry.IsProperty, next))
     {
      wroteAny = true;
     }
    }

    return wroteAny;
   }

   private static bool RestoreColorMemberSnapshot(Component component, ColorMemberSnapshot snapshot)
   {
    var wroteAny = false;
    foreach (var entry in snapshot.Entries)
    {
     if (TrySetColorMemberByName(component, entry.Name, entry.IsProperty, entry.OriginalColor))
     {
      wroteAny = true;
     }
    }

    return wroteAny;
   }

   private static bool TrySetColorMemberByName(Component component, string name, bool isProperty, Color color)
   {
    var type = component.GetType();
    if (isProperty)
    {
     var prop = type.GetProperty(name, InstanceFlags);
     if (prop?.CanWrite == true && prop.PropertyType == typeof(Color))
     {
      prop.SetValue(component, color);
      return true;
     }

     return false;
    }

    var field = type.GetField(name, InstanceFlags);
    if (field?.FieldType == typeof(Color))
    {
     field.SetValue(component, color);
     return true;
    }

    return false;
   }

 private static bool TryGetColorMember(Component component, out Color color)
 {
  var type = component.GetType();
  foreach (var name in ColorMemberNames)
  {
   var prop = type.GetProperty(name, InstanceFlags);
   if (prop?.CanRead == true && prop.PropertyType == typeof(Color))
   {
    color = (Color)prop.GetValue(component);
    return true;
   }

   var field = type.GetField(name, InstanceFlags);
   if (field?.FieldType == typeof(Color))
   {
    color = (Color)field.GetValue(component);
    return true;
   }
  }

  color = default;
  return false;
 }

 private static bool TrySetColorMember(Component component, Color color)
 {
  var type = component.GetType();
  var wroteAny = false;
  foreach (var name in ColorMemberNames)
  {
   var prop = type.GetProperty(name, InstanceFlags);
   if (prop?.CanWrite == true && prop.PropertyType == typeof(Color))
   {
    prop.SetValue(component, color);
    wroteAny = true;
    continue;
   }

   var field = type.GetField(name, InstanceFlags);
   if (field?.FieldType == typeof(Color))
   {
    field.SetValue(component, color);
    wroteAny = true;
   }
  }

  return wroteAny;
 }

 private Color GetAttentionDisplayColor()
 {
  return GetCachedRuntimeColor(
   _runtime?.GetAttentionDisplayColorHex(),
   DefaultAttentionDisplayColor,
   ref _cachedAttentionDisplayColorHex,
   ref _cachedAttentionDisplayColor);
 }

 private Color GetPlayButtonAttentionColor()
 {
  return GetCachedRuntimeColor(
   _runtime?.GetPlayButtonAttentionColorHex(),
   DefaultPlayButtonAttentionColor,
   ref _cachedPlayButtonAttentionColorHex,
   ref _cachedPlayButtonAttentionColor);
 }

 private Color GetPlayButtonAttentionTextColor()
 {
  return GetCachedRuntimeColor(
   _runtime?.GetPlayButtonAttentionTextColorHex(),
   DefaultPlayButtonAttentionTextColor,
   ref _cachedPlayButtonAttentionTextColorHex,
   ref _cachedPlayButtonAttentionTextColor);
 }

 private Color GetPlayButtonConfirmColor()
 {
  return GetCachedRuntimeColor(
   _runtime?.GetPlayButtonConfirmColorHex(),
   DefaultPlayButtonConfirmColor,
   ref _cachedPlayButtonConfirmColorHex,
   ref _cachedPlayButtonConfirmColor);
 }

 private string GetPlayButtonConfirmText()
 {
  var value = _runtime?.GetPlayButtonConfirmText()?.Trim();
  return string.IsNullOrWhiteSpace(value) ? "本当？" : value!;
 }

 private int GetPlayButtonConfirmDurationSeconds()
 {
  return Math.Max(0, _runtime?.GetPlayButtonConfirmDurationSeconds() ?? 6);
 }

 private Color GetCachedRuntimeColor(string? rawHex, Color fallback, ref string cachedHex, ref Color cachedColor)
 {
  var normalized = string.IsNullOrWhiteSpace(rawHex)
   ? string.Empty
   : rawHex!.Trim();
  if (string.Equals(normalized, cachedHex, StringComparison.OrdinalIgnoreCase))
  {
   return cachedColor;
  }

  cachedHex = normalized;
  cachedColor = ParseRuntimeColor(normalized, fallback);
  return cachedColor;
 }

 private static Color ParseRuntimeColor(string? rawHex, Color fallback)
 {
  var trimmed = rawHex?.Trim();
  if (!string.IsNullOrWhiteSpace(trimmed) && ColorUtility.TryParseHtmlString(trimmed, out var parsed))
  {
   return parsed;
  }

  return fallback;
 }

 private void RestorePlayButtonVisualState()
 {
  if (_playTintTarget is not null && _playTintOriginalSnapshot is not null)
  {
   RestoreColorMemberSnapshot(_playTintTarget, _playTintOriginalSnapshot);
  }

  if (_playOutlineTintTarget is not null && _playOutlineOriginalSnapshot is not null)
  {
   RestoreColorMemberSnapshot(_playOutlineTintTarget, _playOutlineOriginalSnapshot);
  }

    if (_playLabelTintTarget is not null && _playLabelOriginalSnapshot is not null)
    {
     RestoreColorMemberSnapshot(_playLabelTintTarget, _playLabelOriginalSnapshot);
    }

  if (_playLabelTintTarget is not null && _playLabelOriginalTextCaptured && _playLabelOriginalText is not null)
  {
   SetTextOnComponent(_playLabelTintTarget, _playLabelOriginalText);
  }

  if (_playSelectableTarget is not null
   && _playOriginalSelectableColorsCaptured
   && _playOriginalSelectableColors is not null)
  {
   TrySetSelectableColors(_playSelectableTarget, _playOriginalSelectableColors);
  }

  _playTintOriginalSnapshot = null;
  _playOutlineOriginalSnapshot = null;
  _playLabelOriginalSnapshot = null;
    _playLabelOriginalText = null;
    _playLabelOriginalTextCaptured = false;
  _playOriginalSelectableColorsCaptured = false;
  _playOriginalSelectableColors = null;
  InvalidatePlayButtonTintState();
 }

 private void OnDestroy()
 {
  if (_runtime is not null)
  {
   _runtime.DataRevisionChanged -= OnRuntimeDataRevisionChanged;
   _runtime = null;
  }

  if (_sceneHooksInstalled)
  {
   SceneManager.activeSceneChanged -= OnActiveSceneChanged;
   _sceneHooksInstalled = false;
  }

    Application.onBeforeRender -= OnBeforeRender;

  ClearMenuControllerEventSubscriptions();
  ClearGameTransitionHook();

  try
  {
   _cts?.Cancel();
  }
  catch
  {
  }

  _cts?.Dispose();
  _cts = null;

   if (_curvedTextObject is not null)
    {
    Destroy(_curvedTextObject);
    _curvedTextObject = null;
    _curvedTextComponent = null;
    _currentSurfaceRoot = null;
    _currentPlayAnchor = null;
    _currentPracticeAnchor = null;
    _currentActionAnchor = null;
    _cachedDetailController = null;
    _curvedTextType = null;
    }

  RestorePlayButtonVisualState();
    ResetPlayIntercept();
  _playTintTarget = null;
  _playOutlineTintTarget = null;
  _playLabelTintTarget = null;
  _playSelectableTarget = null;
  _lastTintTargetRoot = null;
  _lastAppliedDisplayText = string.Empty;
  _hasAppliedDisplayStyle = false;
 }
}
#endif
