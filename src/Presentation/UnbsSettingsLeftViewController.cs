#if UNBS_BSIPA
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using System.Threading;
using UnbsAttention.Models;

namespace UnbsAttention.Presentation;

public sealed class UnbsSettingsLeftViewController : BSMLResourceViewController, IPluginSettingsView
{
 private static readonly string[] StatePropertyNames =
 {
  nameof(Enabled),
  nameof(ShowMute),
  nameof(ShowNotForStreaming),
  nameof(ShowNotForVideo),
  nameof(ShowStageGimmick),
  nameof(ShowPhobia),
  nameof(ShowJumpscare),
  nameof(ShowHeavy),
  nameof(ShowOther),
  nameof(PrefixMute),
  nameof(PrefixNotForStreaming),
  nameof(PrefixNotForVideo),
  nameof(PrefixStageGimmick),
  nameof(PrefixPhobia),
  nameof(PrefixJumpscare),
  nameof(PrefixHeavy),
  nameof(PrefixOther),
  nameof(PositionOffsetX),
  nameof(PositionOffsetY),
    nameof(AttentionDisplayColorHex),
    nameof(PlayButtonAttentionColorHex),
    nameof(PlayButtonAttentionTextColorHex),
  nameof(PlayConfirmColorHex),
  nameof(PlayConfirmText),
  nameof(PlayConfirmDurationSecondsText),
 };

 private enum LeftTab
 {
  General = 0,
  Attention = 1,
  Prefix = 2,
  Position = 3,
 }

 private PluginSettingsController? _controller;
 private PluginSettingsState _state = new();
 private bool _suppressCallbacks;
 private LeftTab _activeTab = LeftTab.General;

 public override string ResourceName => "UnbsAttention.UI.Settings.Left.bsml";

 [UIValue("LastMessage")]
 public string LastMessage { get; private set; } = string.Empty;

 [UIValue("ReportText")]
 public string ReportText { get; private set; } = "更新結果: success=0 fail=0 rows=0";

 [UIValue("GeneralTabActive")]
 public bool GeneralTabActive => _activeTab == LeftTab.General;

 [UIValue("AttentionTabActive")]
 public bool AttentionTabActive => _activeTab == LeftTab.Attention;

 [UIValue("PrefixTabActive")]
 public bool PrefixTabActive => _activeTab == LeftTab.Prefix;

 [UIValue("PositionTabActive")]
 public bool PositionTabActive => _activeTab == LeftTab.Position;

 [UIValue("PositionOffsetX")]
 public string PositionOffsetX => _state.AttentionPositionOffsetX.ToString();

 [UIValue("PositionOffsetY")]
 public string PositionOffsetY => _state.AttentionPositionOffsetY.ToString();

 [UIValue("AttentionDisplayColorHex")]
 public string AttentionDisplayColorHex
 {
  get => _state.AttentionDisplayColorHex;
  set => SetAttentionDisplayColorHex(value);
 }

 [UIValue("PlayButtonAttentionColorHex")]
 public string PlayButtonAttentionColorHex
 {
  get => _state.PlayButtonAttentionColorHex;
  set => SetPlayButtonAttentionColorHex(value);
 }

 [UIValue("PlayButtonAttentionTextColorHex")]
 public string PlayButtonAttentionTextColorHex
 {
  get => _state.PlayButtonAttentionTextColorHex;
  set => SetPlayButtonAttentionTextColorHex(value);
 }

 [UIValue("PlayConfirmColorHex")]
 public string PlayConfirmColorHex
 {
  get => _state.PlayButtonConfirmColorHex;
  set => SetPlayConfirmColorHex(value);
 }

 [UIValue("PlayConfirmText")]
 public string PlayConfirmText
 {
  get => _state.PlayButtonConfirmText;
  set => SetPlayConfirmText(value);
 }

 [UIValue("PlayConfirmDurationSecondsText")]
 public string PlayConfirmDurationSecondsText
 {
  get => _state.PlayButtonConfirmDurationSeconds.ToString();
  set => SetPlayConfirmDurationSecondsText(value);
 }

 [UIValue("Enabled")]
 public bool Enabled
 {
  get => _state.Enabled;
  set => SetPluginEnabled(value);
 }

 [UIValue("ShowMute")]
 public bool ShowMute
 {
  get => GetEnabled(AttentionCategory.Mute);
  set => SetCategoryEnabled(AttentionCategory.Mute, value);
 }

 [UIValue("ShowNotForStreaming")]
 public bool ShowNotForStreaming
 {
  get => GetEnabled(AttentionCategory.NotForStreaming);
  set => SetCategoryEnabled(AttentionCategory.NotForStreaming, value);
 }

 [UIValue("ShowNotForVideo")]
 public bool ShowNotForVideo
 {
  get => GetEnabled(AttentionCategory.NotForVideo);
  set => SetCategoryEnabled(AttentionCategory.NotForVideo, value);
 }

 [UIValue("ShowStageGimmick")]
 public bool ShowStageGimmick
 {
  get => GetEnabled(AttentionCategory.StageGimmick);
  set => SetCategoryEnabled(AttentionCategory.StageGimmick, value);
 }

 [UIValue("ShowPhobia")]
 public bool ShowPhobia
 {
  get => GetEnabled(AttentionCategory.Phobia);
  set => SetCategoryEnabled(AttentionCategory.Phobia, value);
 }

 [UIValue("ShowJumpscare")]
 public bool ShowJumpscare
 {
  get => GetEnabled(AttentionCategory.Jumpscare);
  set => SetCategoryEnabled(AttentionCategory.Jumpscare, value);
 }

 [UIValue("ShowHeavy")]
 public bool ShowHeavy
 {
  get => GetEnabled(AttentionCategory.Heavy);
  set => SetCategoryEnabled(AttentionCategory.Heavy, value);
 }

 [UIValue("ShowOther")]
 public bool ShowOther
 {
  get => GetEnabled(AttentionCategory.Other);
  set => SetCategoryEnabled(AttentionCategory.Other, value);
 }

 [UIValue("PrefixMute")]
 public string PrefixMute
 {
  get => GetPrefix(AttentionCategory.Mute);
  set => SetPrefix(AttentionCategory.Mute, value);
 }

 [UIValue("PrefixNotForStreaming")]
 public string PrefixNotForStreaming
 {
  get => GetPrefix(AttentionCategory.NotForStreaming);
  set => SetPrefix(AttentionCategory.NotForStreaming, value);
 }

 [UIValue("PrefixNotForVideo")]
 public string PrefixNotForVideo
 {
  get => GetPrefix(AttentionCategory.NotForVideo);
  set => SetPrefix(AttentionCategory.NotForVideo, value);
 }

 [UIValue("PrefixStageGimmick")]
 public string PrefixStageGimmick
 {
  get => GetPrefix(AttentionCategory.StageGimmick);
  set => SetPrefix(AttentionCategory.StageGimmick, value);
 }

 [UIValue("PrefixPhobia")]
 public string PrefixPhobia
 {
  get => GetPrefix(AttentionCategory.Phobia);
  set => SetPrefix(AttentionCategory.Phobia, value);
 }

 [UIValue("PrefixJumpscare")]
 public string PrefixJumpscare
 {
  get => GetPrefix(AttentionCategory.Jumpscare);
  set => SetPrefix(AttentionCategory.Jumpscare, value);
 }

 [UIValue("PrefixHeavy")]
 public string PrefixHeavy
 {
  get => GetPrefix(AttentionCategory.Heavy);
  set => SetPrefix(AttentionCategory.Heavy, value);
 }

 [UIValue("PrefixOther")]
 public string PrefixOther
 {
  get => GetPrefix(AttentionCategory.Other);
  set => SetPrefix(AttentionCategory.Other, value);
 }

 public void Bind(PluginSettingsController controller)
 {
  _controller = controller;
 }

 public void Render(PluginSettingsState state)
 {
  _suppressCallbacks = true;
  try
  {
   _state = state;
   var report = state.LastRefreshReport;
   ReportText = $"更新結果: success={report.SucceededSources} fail={report.FailedSources} rows={report.ImportedRows}";
    NotifyPropertyChanged(nameof(ReportText));

    foreach (var propertyName in StatePropertyNames)
    {
     NotifyPropertyChanged(propertyName);
    }

    NotifyTabStateChanged();
  }
  finally
  {
   _suppressCallbacks = false;
  }
 }

 public void ShowMessage(string message)
 {
  LastMessage = message;
  NotifyPropertyChanged(nameof(LastMessage));
 }

 [UIAction("ShowGeneralTab")]
 public void ShowGeneralTab()
 {
  SetActiveTab(LeftTab.General);
 }

 [UIAction("ShowAttentionTab")]
 public void ShowAttentionTab()
 {
  SetActiveTab(LeftTab.Attention);
 }

 [UIAction("ShowPrefixTab")]
 public void ShowPrefixTab()
 {
  SetActiveTab(LeftTab.Prefix);
 }

 [UIAction("ShowPositionTab")]
 public void ShowPositionTab()
 {
  SetActiveTab(LeftTab.Position);
 }

 [UIAction("OffsetXMinus")]
 public void OffsetXMinus()
 {
  AdjustOffset(-1, 0);
 }

 [UIAction("OffsetXPlus")]
 public void OffsetXPlus()
 {
  AdjustOffset(1, 0);
 }

 [UIAction("OffsetYMinus")]
 public void OffsetYMinus()
 {
  AdjustOffset(0, -1);
 }

 [UIAction("OffsetYPlus")]
 public void OffsetYPlus()
 {
  AdjustOffset(0, 1);
 }

 [UIAction("RefreshNow")]
 public async void RefreshNow()
 {
  if (_controller is null)
  {
   LastMessage = "コントローラー初期化待ちです。";
   return;
  }

  try
  {
   var changed = await _controller.RefreshNowAsync(CancellationToken.None);
   LastMessage = changed > 0 ? "更新しました。" : "変更はありません。";
   NotifyPropertyChanged(nameof(LastMessage));
  }
  catch
  {
   LastMessage = "更新に失敗しました。";
   NotifyPropertyChanged(nameof(LastMessage));
  }
 }

 private bool GetEnabled(AttentionCategory category)
 {
  return !_state.AttentionCategories.TryGetValue(category, out var enabled) || enabled;
 }

 private void SetCategoryEnabled(AttentionCategory category, bool enabled)
 {
  if (_suppressCallbacks || _controller is null)
  {
   return;
  }

  _controller.SetCategoryEnabled(category, enabled);
 }

 private void SetPluginEnabled(bool enabled)
 {
  if (_suppressCallbacks || _controller is null)
  {
   return;
  }

  _controller.SetEnabled(enabled);
 }

 private string GetPrefix(AttentionCategory category)
 {
  return _state.CategoryPrefixes.TryGetValue(category, out var prefix)
   ? prefix
   : AttentionLineFormatter.GetDefaultPrefix(category);
 }

 private void SetPrefix(AttentionCategory category, string prefix)
 {
  if (_suppressCallbacks || _controller is null)
  {
   return;
  }

  _controller.SetCategoryPrefix(category, prefix);
 }

 private void AdjustOffset(int deltaX, int deltaY)
 {
  if (_controller is null)
  {
   LastMessage = "コントローラー初期化待ちです。";
   NotifyPropertyChanged(nameof(LastMessage));
   return;
  }

  var changed = _controller.AdjustAttentionPositionOffset(deltaX, deltaY);
  if (!changed)
  {
   return;
  }
 }

 private void SetPlayConfirmColorHex(string value)
 {
  if (_suppressCallbacks || _controller is null)
  {
   return;
  }

  _controller.SetPlayButtonConfirmColorHex(value);
 }

   private void SetAttentionDisplayColorHex(string value)
   {
    if (_suppressCallbacks || _controller is null)
    {
     return;
    }

    _controller.SetAttentionDisplayColorHex(value);
   }

   private void SetPlayButtonAttentionColorHex(string value)
   {
    if (_suppressCallbacks || _controller is null)
    {
     return;
    }

    _controller.SetPlayButtonAttentionColorHex(value);
   }

   private void SetPlayButtonAttentionTextColorHex(string value)
   {
    if (_suppressCallbacks || _controller is null)
    {
     return;
    }

    _controller.SetPlayButtonAttentionTextColorHex(value);
   }

 private void SetPlayConfirmText(string value)
 {
  if (_suppressCallbacks || _controller is null)
  {
   return;
  }

  _controller.SetPlayButtonConfirmText(value);
 }

 private void SetPlayConfirmDurationSecondsText(string value)
 {
  if (_suppressCallbacks || _controller is null)
  {
   return;
  }

  var trimmed = value?.Trim();
  if (!int.TryParse(trimmed, out var seconds) || seconds < 0)
  {
   LastMessage = "Confirm countdown seconds must be an integer >= 0.";
   NotifyPropertyChanged(nameof(LastMessage));
   _controller.RenderState();
   return;
  }

  _controller.SetPlayButtonConfirmDurationSeconds(seconds);
 }

 private void SetActiveTab(LeftTab tab)
 {
  if (_activeTab == tab)
  {
   return;
  }

  _activeTab = tab;
  NotifyTabStateChanged();
 }

 private void NotifyTabStateChanged()
 {
  NotifyPropertyChanged(nameof(GeneralTabActive));
  NotifyPropertyChanged(nameof(AttentionTabActive));
  NotifyPropertyChanged(nameof(PrefixTabActive));
  NotifyPropertyChanged(nameof(PositionTabActive));
 }

}
#endif
