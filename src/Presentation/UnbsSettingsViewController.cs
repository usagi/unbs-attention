#if UNBS_BSIPA
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components;
using BeatSaberMarkupLanguage.ViewControllers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using HMUI;
using UnbsAttention.Models;
using UnbsAttention.Services;
using UnityEngine;

namespace UnbsAttention.Presentation;

public sealed class UnbsSettingsViewController : BSMLResourceViewController, IPluginSettingsView
{
 private PluginSettingsController? _controller;
 private PluginSettingsState _state = new();

 public override string ResourceName => "UnbsAttention.UI.Settings.Sources.bsml";

 [UIValue("LastMessage")]
 public string LastMessage { get; private set; } = string.Empty;

 [UIValue("ReportText")]
 public string ReportText { get; private set; } = "Not refreshed yet.";

 [UIComponent("source-list")]
 private CustomListTableData? _sourceList = null;

 [UIValue("RefreshFailDetails")]
 public string RefreshFailDetails { get; private set; } = string.Empty;

 private int _selectedSourceIndex = -1;
 private readonly List<string> _sourceLabels = new();
 private static readonly Regex SheetIdPattern = new(@"/d/([a-zA-Z0-9-_]+)", RegexOptions.Compiled);

 public void Bind(PluginSettingsController controller)
 {
  _controller = controller;
 }

 [UIAction("#post-parse")]
 private void OnPostParse()
 {
  _controller?.RenderState();
 }

 public void Render(PluginSettingsState state)
 {
  _state = state;
  BuildSourceLabels(state.Sources);

  if (_sourceLabels.Count == 0)
  {
   _selectedSourceIndex = -1;
  }
  else if (_selectedSourceIndex < 0 || _selectedSourceIndex >= _sourceLabels.Count)
  {
   _selectedSourceIndex = 0;
  }
  else if (_selectedSourceIndex < _sourceLabels.Count)
  {
  }

  ReloadSourceList();

  var report = state.LastRefreshReport;
  ReportText = $"Succeeded={report.SucceededSources} Failed={report.FailedSources} Attentions={report.ImportedRows}";
  RefreshFailDetails = BuildFailDetailsText(report);

  NotifyPropertyChanged(nameof(ReportText));
  NotifyPropertyChanged(nameof(RefreshFailDetails));
 }

 public void ShowMessage(string message)
 {
  LastMessage = message;
  NotifyPropertyChanged(nameof(LastMessage));
 }

 [UIAction("source-selected")]
 public void OnSourceSelected(TableView _, int index)
 {
  if (index < 0 || index >= _state.Sources.Count)
  {
   _selectedSourceIndex = -1;
  }
  else
  {
   _selectedSourceIndex = index;
  }

 }

 [UIAction("AddSourceFromClipboard")]
 public void AddSourceFromClipboard()
 {
  if (_controller is null)
  {
   LastMessage = "コントローラー初期化待ちです。";
   return;
  }

  var fromClipboard = (GUIUtility.systemCopyBuffer ?? string.Empty).Trim();
  if (string.IsNullOrWhiteSpace(fromClipboard))
  {
   LastMessage = "クリップボードが空です。";
   return;
  }

  var ok = _controller.AddSource(fromClipboard);
  if (!ok)
  {
   LastMessage = "URL追加に失敗しました。URL形式を確認してください。";
  }
 }

 [UIAction("RemoveSelectedSource")]
 public void RemoveSelectedSource()
 {
  if (_controller is null)
  {
  LastMessage = "コントローラー初期化待ちです。";
   return;
  }

  if (_selectedSourceIndex < 0)
  {
   LastMessage = "一覧からURLを選択してください。";
   return;
  }

  _controller.RemoveSourceAt(_selectedSourceIndex);
 }

 [UIAction("OpenSelectedSource")]
 public void OpenSelectedSource()
 {
  if (_controller is null)
  {
  LastMessage = "コントローラー初期化待ちです。";
   return;
  }

  if (_selectedSourceIndex < 0)
  {
   LastMessage = "一覧からURLを選択してください。";
   return;
  }

  _controller.OpenSourceAt(_selectedSourceIndex);
 }

 [UIAction("RefreshNow")]
 public async void RefreshNow()
 {
  if (_controller is null)
  {
   LastMessage = "コントローラー初期化待ちです。";
   NotifyPropertyChanged(nameof(LastMessage));
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

 private void BuildSourceLabels(IReadOnlyList<SpreadsheetSourceItem> sources)
 {
  _sourceLabels.Clear();
  for (var i = 0; i < sources.Count; i++)
  {
   _sourceLabels.Add(BuildSourceLabel(sources[i].RawSource));
  }
 }

 private void ReloadSourceList()
 {
    var list = _sourceList;
    var table = list?.TableView;
    if (list is null || table is null)
  {
   return;
  }

  var cells = new List<CustomListTableData.CustomCellInfo>();
  for (var i = 0; i < _sourceLabels.Count; i++)
  {
   var prefix = i == _selectedSourceIndex ? "> " : "  ";
   cells.Add(new CustomListTableData.CustomCellInfo(prefix + _sourceLabels[i]));
  }

    list.Data = cells;
    table.ReloadData();

  if (_selectedSourceIndex >= 0 && _selectedSourceIndex < _sourceLabels.Count)
  {
     table.SelectCellWithIdx(_selectedSourceIndex);
  }
  else
  {
     table.ClearSelection();
  }
 }

 private static string BuildSourceLabel(string rawSource)
 {
  var source = (rawSource ?? string.Empty).Trim();
  if (GoogleSpreadsheetSourceParser.TryBuildOpenUrl(source, out var openUrl))
  {
   var m = SheetIdPattern.Match(openUrl);
   if (m.Success)
   {
    var id = m.Groups[1].Value;
    var prefixLength = Math.Min(12, id.Length);
    var idPrefix = id.Substring(0, prefixLength);
    return "ID " + idPrefix + "...";
   }
  }

  var length = Math.Min(20, source.Length);
  return length == source.Length ? source : source.Substring(0, length) + "...";
 }

 private static string BuildFailDetailsText(SubscriptionRefreshReport report)
 {
  if (report.FailedSources <= 0)
  {
    return string.Empty;
  }

  var sb = new StringBuilder();
  sb.Append("失敗詳細:");
  foreach (var src in report.Sources.Where(x => !x.IsSuccess))
  {
   sb.Append("\n- ");
   sb.Append(BuildSourceLabel(src.RawSource));
   sb.Append(": ");
   sb.Append(CompactMessage(src.Message));
  }

  return sb.ToString();
 }

 private static string CompactMessage(string? message)
 {
   var safe = message ?? string.Empty;
   if (string.IsNullOrWhiteSpace(safe))
  {
   return "unknown error";
  }

   var oneLine = safe.Replace("\r", " ").Replace("\n", " ").Trim();
  return oneLine.Length <= 90 ? oneLine : oneLine.Substring(0, 90) + "...";
 }

}
#endif
