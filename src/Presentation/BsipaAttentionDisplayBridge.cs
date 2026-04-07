#if UNBS_BSIPA
using System.Reflection;
using UnbsAttention.Models;
using UnityEngine;

namespace UnbsAttention.Presentation;

public sealed class BsipaAttentionDisplayBridge : MonoBehaviour
{
 private static readonly BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
 private static readonly Color DefaultAttentionDisplayColor = new(1f, 0.84f, 0.25f, 1f);
 private static readonly Color DefaultPlayButtonAttentionColor = new(1f, 0.86f, 0.18f, 1f);
 private static readonly Color DefaultPlayButtonAttentionTextColor = new(0f, 0f, 0f, 1f);
 private static readonly Color DefaultPlayButtonOutlineColor = new(0f, 0f, 0f, 1f);

 private AttentionPluginRuntime? _runtime;
 private CancellationTokenSource? _cts;
 private float _lastRefreshAt;
 private float _lastDetailLookupAt;
 private bool _refreshInFlight;
 private string? _displayText;
 private string _lastLevelId = string.Empty;
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
 private bool _playOriginalSelectableColorsCaptured;
 private object? _playOriginalSelectableColors;
 private string _lastPlayAnchorPath = string.Empty;
 private float _lastTintTraceAt;
 private int _tintTraceCount;

 public void Bind(AttentionPluginRuntime runtime)
 {
  _runtime = runtime;
  _cts = new CancellationTokenSource();
 }

 private void Update()
 {
  if (_runtime is null || _cts is null)
  {
   return;
  }

   EnsureCurvedTextAttached();
   ApplyDisplayToCurvedText();

  if (_refreshInFlight)
  {
   return;
  }

  var now = Time.unscaledTime;
  if (now - _lastRefreshAt < 0.75f)
  {
   return;
  }

  _lastRefreshAt = now;
  _ = RefreshAsync(_cts.Token);
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
    _currentSurfaceRoot = null;
    _currentPlayAnchor = null;
    _currentPracticeAnchor = null;
    _currentActionAnchor = null;
     UpdatePlayButtonTint(false);
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
   var template = FindCurvedTextTemplate(detailController.transform);
   if (template is null)
  {
    if (_curvedTextObject is not null)
    {
      _curvedTextObject.SetActive(false);
    }

      _displayText = null;
    _currentSurfaceRoot = null;
    _currentPlayAnchor = null;
    _currentPracticeAnchor = null;
    _currentActionAnchor = null;
      UpdatePlayButtonTint(false);
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
   if (_curvedTextObject is not null)
   {
    _curvedTextObject.SetActive(true);
   }
 }

 private MonoBehaviour? ResolveDetailController()
 {
  if (_cachedDetailController is not null && IsUsableDetailController(_cachedDetailController))
  {
   return _cachedDetailController;
  }

  _cachedDetailController = null;

  var now = Time.unscaledTime;
  if (now - _lastDetailLookupAt < 0.35f)
  {
   return null;
  }

  _lastDetailLookupAt = now;
  _cachedDetailController = FindStandardLevelDetailViewController();
  return _cachedDetailController;
 }

 private void ApplyDisplayToCurvedText()
 {
   if (_curvedTextObject is null)
  {
   return;
  }

  var hasText = !string.IsNullOrWhiteSpace(_displayText);
   _curvedTextObject.SetActive(hasText && _currentSurfaceRoot is not null);

   if (hasText && _curvedTextComponent is not null)
  {
    SetTextOnComponent(_curvedTextComponent, _displayText!);
    TrySetColorOnComponent(_curvedTextComponent, GetAttentionDisplayColor());
   TrySetLeftAlignment(_curvedTextComponent);

    PositionCurvedTextUnderPlayArea();
  }

  UpdatePlayButtonTint(hasText);
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

 private void UpdatePlayButtonTint(bool attentionActive)
 {
  var activeTint = GetPlayButtonAttentionColor();
  var activeOutlineTint = DefaultPlayButtonOutlineColor;
  var activeLabelTint = GetPlayButtonAttentionTextColor();
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

  var now = Time.unscaledTime;
  if (now - _lastTintTraceAt >= 0.5f)
  {
   _lastTintTraceAt = now;
   _tintTraceCount++;
    TraceTintWrite(attentionActive, activeTint, colorWriteOk, outlineWriteOk, labelWriteOk, selectableWriteOk);
  }
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
  }

  _playTintTarget = nextTintTarget;
  _playOutlineTintTarget = nextOutlineTarget;
  _playLabelTintTarget = nextLabelTarget;
  _playSelectableTarget = nextSelectableTarget;

  if (changed)
  {
   TracePlayTargets(root, nextTintTarget, nextOutlineTarget, nextLabelTarget, nextSelectableTarget);
  }
 }

 private void TracePlayAnchorIfChanged(Transform playAnchor)
 {
  var path = BuildTransformPath(playAnchor);
  if (string.Equals(_lastPlayAnchorPath, path, StringComparison.Ordinal))
  {
   return;
  }

  _lastPlayAnchorPath = path;
  Debug.Log("[UNBS] Play anchor changed: " + path + " (" + playAnchor.GetType().Name + ")");
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
  Debug.Log("[UNBS] SetPlayTintTargets root=" + BuildTransformPath(root));
  Debug.Log("[UNBS] Tint target=" + DescribeComponent(tintTarget));
  Debug.Log("[UNBS] Outline target=" + DescribeComponent(outlineTarget));
  Debug.Log("[UNBS] Label target=" + DescribeComponent(labelTarget));
  Debug.Log("[UNBS] Selectable target=" + DescribeComponent(selectableTarget));

  var candidates = root.GetComponentsInChildren<Component>(true)
   .Where(x => x is not null)
   .Where(x => HasColorMember(x) || HasSelectableColors(x))
   .ToList();

  Debug.Log("[UNBS] Color candidate count=" + candidates.Count);
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

   Debug.Log("[UNBS] Candidate " + info + colorText + selectableText);
  }

  if (candidates.Count > 60)
  {
   Debug.Log("[UNBS] Candidate log truncated: " + (candidates.Count - 60) + " entries omitted");
  }
 }

 private void TraceTintWrite(bool attentionActive, Color activeTint, bool colorWriteOk, bool outlineWriteOk, bool labelWriteOk, bool selectableWriteOk)
 {
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

  Debug.Log(
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
   var level = FindCurrentBeatmapLevel();
   if (level is null)
   {
    _displayText = null;
    _lastLevelId = string.Empty;
    return;
   }

   var snapshot = BuildSnapshot(level);
   if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.LevelId))
   {
    _displayText = null;
    _lastLevelId = string.Empty;
    return;
   }

   if (string.Equals(_lastLevelId, snapshot.LevelId, StringComparison.OrdinalIgnoreCase)
       && !string.IsNullOrWhiteSpace(_displayText))
   {
    return;
   }

   var text = await _runtime!
    .BuildAttentionTextAsync(snapshot, cancellationToken)
    .ConfigureAwait(false);

   _displayText = text;
   _lastLevelId = snapshot.LevelId ?? string.Empty;
  }
  catch
  {
   _displayText = null;
  }
  finally
  {
   _refreshInFlight = false;
  }
 }

 private static object? FindCurrentBeatmapLevel()
 {
    var detailController = FindStandardLevelDetailViewController();
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

 private static MonoBehaviour? FindStandardLevelDetailViewController()
 {
    var allMonoBehaviours = Resources.FindObjectsOfTypeAll<MonoBehaviour>();
    foreach (var monoBehaviour in allMonoBehaviours)
  {
   if (monoBehaviour is null)
   {
    continue;
   }

   if (!IsUsableDetailController(monoBehaviour))
   {
    continue;
   }

   var type = monoBehaviour.GetType();
   if (!string.Equals(type.Name, "StandardLevelDetailViewController", StringComparison.Ordinal))
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

 private static Component? FindCurvedTextTemplate(Transform root)
 {
  var components = root.GetComponentsInChildren<Component>(true);
  foreach (var component in components)
  {
   if (component is null)
   {
   continue;
   }

   var typeName = component.GetType().Name;
   if (string.Equals(typeName, "CurvedTextMeshPro", StringComparison.Ordinal)
      || string.Equals(typeName, "TextMeshProUGUI", StringComparison.Ordinal))
   {
   return component;
   }
  }

  return null;
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
  var property = component.GetType().GetProperty("colors", InstanceFlags);
  if (property?.CanRead != true || property.CanWrite != true)
  {
   return false;
  }

  var type = property.PropertyType;
  return HasColorField(type, "normalColor")
   && HasColorField(type, "highlightedColor")
   && HasColorField(type, "pressedColor")
   && HasColorField(type, "selectedColor");
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

  string? GetString(string fieldName)
  {
   var field = type.GetField(fieldName, InstanceFlags);
   if (field?.GetValue(beatmapLevel) is string s)
   {
    return s;
   }

   var property = type.GetProperty(fieldName, InstanceFlags);
   return property?.GetValue(beatmapLevel) as string;
  }

  var levelId = GetString("levelID");
  if (string.IsNullOrWhiteSpace(levelId))
  {
   return null;
  }

  return new SongSelectionSnapshot
  {
   LevelId = levelId,
   SongName = GetString("songName"),
   SongSubName = GetString("songSubName"),
   SongAuthorName = GetString("songAuthorName"),
   LevelAuthorName = null,
   BsrId = null,
   BeatSaverDescription = null,
  };
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
  var names = new[] { "MidlineLeft", "Left", "TopLeft", "BottomLeft" };
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
  var property = component.GetType().GetProperty("colors", InstanceFlags);
  if (property?.CanRead != true)
  {
   return false;
  }

  colors = property.GetValue(component)!;
  return colors is not null;
 }

 private static bool TrySetSelectableColors(Component component, object colors)
 {
  var property = component.GetType().GetProperty("colors", InstanceFlags);
  if (property?.CanWrite != true)
  {
   return false;
  }

  property.SetValue(component, colors);
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
  return ParseRuntimeColor(_runtime?.GetAttentionDisplayColorHex(), DefaultAttentionDisplayColor);
 }

 private Color GetPlayButtonAttentionColor()
 {
  return ParseRuntimeColor(_runtime?.GetPlayButtonAttentionColorHex(), DefaultPlayButtonAttentionColor);
 }

 private Color GetPlayButtonAttentionTextColor()
 {
  return ParseRuntimeColor(_runtime?.GetPlayButtonAttentionTextColorHex(), DefaultPlayButtonAttentionTextColor);
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

  if (_playSelectableTarget is not null
   && _playOriginalSelectableColorsCaptured
   && _playOriginalSelectableColors is not null)
  {
   TrySetSelectableColors(_playSelectableTarget, _playOriginalSelectableColors);
  }

  _playTintOriginalSnapshot = null;
  _playOutlineOriginalSnapshot = null;
  _playLabelOriginalSnapshot = null;
  _playOriginalSelectableColorsCaptured = false;
  _playOriginalSelectableColors = null;
 }

 private void OnDestroy()
 {
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
  _playTintTarget = null;
  _playOutlineTintTarget = null;
  _playLabelTintTarget = null;
  _playSelectableTarget = null;
 }
}
#endif
