using System;

namespace UnbsAttention.Config;

public enum AttentionDisplayMode
{
 IconOnly = 0,
 IconWithReason = 1,
}

public class PluginConfig
{
 public bool Enabled { get; set; } = true;

 // true のときだけ診断ログを出力する。
 public bool Debug { get; set; } = false;

 public AttentionDisplayMode DisplayMode { get; set; } = AttentionDisplayMode.IconWithReason;

 // 表示対象カテゴリ（大文字小文字は区別しない）。
 // ON(true) のカテゴリだけ注意表示に出す。
 public List<string> AttentionCategories { get; set; } = new()
 {
  "Mute",
  "NotForStreaming",
  "NotForVideo",
  "StageGimmick",
  "Phobia",
  "Jumpscare",
  "Heavy",
  "Other",
 };

 public Dictionary<string, string> CategoryPrefixes { get; set; } = new(StringComparer.OrdinalIgnoreCase)
 {
  ["Mute"] = "🔇[Mute]",
  ["NotForStreaming"] = "🚫📡[Not for Streaming]",
  ["NotForVideo"] = "🚫🎥[Not for Video]",
  ["StageGimmick"] = "🎭[Stage Gimmick]",
  ["Phobia"] = "😨[Phobia]",
  ["Jumpscare"] = "😱[Jumpscare]",
  ["Heavy"] = "⚠[Heavy]",
  ["Other"] = "🏷[Other]",
 };

 public int AttentionPositionOffsetX { get; set; } = 0;

 public int AttentionPositionOffsetY { get; set; } = 0;

 // Attention表示テキスト色（#RRGGBB / #RRGGBBAA）。
 public string AttentionDisplayColorHex { get; set; } = "#FFFF00FF";

 // アテンション時のPlayボタン背景色（#RRGGBB / #RRGGBBAA）。
 public string PlayButtonAttentionColorHex { get; set; } = "#FFFF00FF";

 // アテンション時のPlayボタン文字色（#RRGGBB / #RRGGBBAA）。
 public string PlayButtonAttentionTextColorHex { get; set; } = "#880000FF";

 // アテンション確認中(2回目待ち)のPlayボタン背景色（#RRGGBB / #RRGGBBAA）。
 public string PlayButtonConfirmColorHex { get; set; } = "#FF5933FF";

 // アテンション確認中にPlayラベルを置き換える文言。
 public string PlayButtonConfirmText { get; set; } = "本当？";

 // アテンション確認の有効時間（秒）。0以上の整数。
 public int PlayButtonConfirmDurationSeconds { get; set; } = 6;

 public bool EnableOnlyWhenTwitchStreamerLive { get; set; } = false;

 public string TwitchBroadcasterId { get; set; } = string.Empty;

 public string TwitchClientId { get; set; } = string.Empty;

 public string TwitchAppAccessToken { get; set; } = string.Empty;

 public bool TwitchCheckFailOpen { get; set; } = true;

 public int TwitchCheckIntervalSeconds { get; set; } = 90;

 public string AttentionJsonPath { get; set; } = "UserData/unbs-attention.json";

 public string SyncStrategy { get; set; } = "google-sheets";

 // 各要素は Google Spreadsheet の完全URL またはシートID。
 public List<string> SpreadsheetSources { get; set; } = new()
 {
  "https://docs.google.com/spreadsheets/d/14Wxm_M7sZh_kCSaLCuf5TWphaXaBiBqqQGb5g3Zgg3g/edit?usp=sharing",
 };

 public bool AutoRefreshOnInit { get; set; } = true;

 public int AutoRefreshDelaySeconds { get; set; } = 8;

 // BeatSaver description のインメモリキャッシュTTL（秒）。0以下でキャッシュ無効。
 public int BeatSaverDescriptionCacheTtlSeconds { get; set; } = 300;

 public string DiscordChannelUrl { get; set; } = string.Empty;
}
