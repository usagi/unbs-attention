using System.Diagnostics;
using UnbsAttention.Models;

namespace UnbsAttention.Presentation;

public sealed class PluginSettingsController
{
 private readonly AttentionPluginRuntime _runtime;
 private readonly List<IPluginSettingsView> _views = new();
 private int _refreshInFlight;

 public PluginSettingsController(AttentionPluginRuntime runtime, IPluginSettingsView view)
 {
  _runtime = runtime;
  _views.Add(view);
 }

 public void AddView(IPluginSettingsView view)
 {
  if (_views.Contains(view))
  {
   return;
  }

  _views.Add(view);
 }

 public void RenderState()
 {
  RenderAll();
 }

 public bool SetCategoryEnabled(AttentionCategory category, bool enabled)
 {
  var changed = _runtime.SetCategoryEnabled(category, enabled);
  if (changed)
  {
   RenderAll();
  }

  return changed;
 }

 public bool SetEnabled(bool enabled)
 {
  var changed = _runtime.SetEnabled(enabled);
  if (changed)
  {
   RenderAll();
  }

  return changed;
 }

 public bool SetAttentionDisplayColorHex(string value)
 {
  var changed = _runtime.SetAttentionDisplayColorHex(value);
  if (changed)
  {
   RenderAll();
  }

  return changed;
 }

 public bool SetPlayButtonAttentionColorHex(string value)
 {
  var changed = _runtime.SetPlayButtonAttentionColorHex(value);
  if (changed)
  {
   RenderAll();
  }

  return changed;
 }

 public bool SetPlayButtonAttentionTextColorHex(string value)
 {
  var changed = _runtime.SetPlayButtonAttentionTextColorHex(value);
  if (changed)
  {
   RenderAll();
  }

  return changed;
 }

 public bool SetCategoryPrefix(AttentionCategory category, string prefix)
 {
  var changed = _runtime.SetCategoryPrefix(category, prefix);
  if (changed)
  {
   RenderAll();
  }

  return changed;
 }

 public bool AdjustAttentionPositionOffset(int deltaX, int deltaY)
 {
  var changed = _runtime.AdjustAttentionPositionOffset(deltaX, deltaY);
  if (changed)
  {
   RenderAll();
  }

  return changed;
 }

 public bool SetPlayButtonConfirmColorHex(string value)
 {
  var changed = _runtime.SetPlayButtonConfirmColorHex(value);
  if (changed)
  {
   RenderAll();
  }

  return changed;
 }

 public bool SetPlayButtonConfirmText(string value)
 {
  var changed = _runtime.SetPlayButtonConfirmText(value);
  if (changed)
  {
   RenderAll();
  }

  return changed;
 }

 public bool SetPlayButtonConfirmDurationSeconds(int value)
 {
  var changed = _runtime.SetPlayButtonConfirmDurationSeconds(value);
  if (changed)
  {
   RenderAll();
  }

  return changed;
 }

 public bool AddSource(string source)
 {
  var ok = _runtime.AddSpreadsheetSource(source);
  ShowMessageAll(ok ? "Source added." : "Failed to add source.");
  RenderAll();
  return ok;
 }

 public bool RemoveSourceAt(int index)
 {
  var ok = _runtime.RemoveSpreadsheetSourceAt(index);
  ShowMessageAll(ok ? "Source removed." : "Failed to remove source.");
  RenderAll();
  return ok;
 }

 public bool OpenSourceAt(int index)
 {
  if (!_runtime.TryGetOpenableSpreadsheetSourceUrl(index, out var url))
  {
   ShowMessageAll("Source URL is invalid.");
   return false;
  }

  try
  {
   Process.Start(new ProcessStartInfo
   {
    FileName = url,
    UseShellExecute = true,
   });
   return true;
  }
  catch
  {
   ShowMessageAll("Could not open browser.");
   return false;
  }
 }

 public async Task<int> RefreshNowAsync(CancellationToken cancellationToken)
 {
  if (Interlocked.CompareExchange(ref _refreshInFlight, 1, 0) != 0)
  {
   ShowMessageAll("更新中です。完了まで待ってください。");
   return 0;
  }

  try
  {
   var changed = await _runtime.PullAndMergeAsync(cancellationToken);
   RenderAll();
   ShowMessageAll(changed > 0 ? "Refreshed." : "No changes.");
   return changed;
  }
  finally
  {
   Interlocked.Exchange(ref _refreshInFlight, 0);
  }
 }

 private void RenderAll()
 {
  var state = BuildState();
  foreach (var view in _views)
  {
   view.Render(state);
  }
 }

 private void ShowMessageAll(string message)
 {
  foreach (var view in _views)
  {
   view.ShowMessage(message);
  }
 }

 private PluginSettingsState BuildState()
 {
  var enabled = _runtime.GetEnabledCategories();
  var map = Enum
   .GetValues(typeof(AttentionCategory))
   .Cast<AttentionCategory>()
   .ToDictionary(
    category => category,
    category => enabled.Any(x => string.Equals(x, category.ToString(), StringComparison.OrdinalIgnoreCase)));

  var prefixMap = Enum
   .GetValues(typeof(AttentionCategory))
   .Cast<AttentionCategory>()
   .ToDictionary(category => category, category => _runtime.GetCategoryPrefix(category));

  return new PluginSettingsState
  {
   Enabled = _runtime.GetEnabled(),
   AttentionDisplayColorHex = _runtime.GetAttentionDisplayColorHex(),
   PlayButtonAttentionColorHex = _runtime.GetPlayButtonAttentionColorHex(),
   PlayButtonAttentionTextColorHex = _runtime.GetPlayButtonAttentionTextColorHex(),
   AttentionCategories = map,
   CategoryPrefixes = prefixMap,
   Sources = _runtime.GetSpreadsheetSourceItems(),
   LastRefreshReport = _runtime.GetLastRefreshReport(),
   AttentionPositionOffsetX = _runtime.GetAttentionPositionOffsetX(),
   AttentionPositionOffsetY = _runtime.GetAttentionPositionOffsetY(),
   PlayButtonConfirmColorHex = _runtime.GetPlayButtonConfirmColorHex(),
   PlayButtonConfirmText = _runtime.GetPlayButtonConfirmText(),
   PlayButtonConfirmDurationSeconds = _runtime.GetPlayButtonConfirmDurationSeconds(),
  };
 }
}
