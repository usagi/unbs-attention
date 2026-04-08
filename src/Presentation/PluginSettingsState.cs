using UnbsAttention.Models;

namespace UnbsAttention.Presentation;

public sealed class PluginSettingsState
{
 public bool Enabled { get; set; } = true;

 public string AttentionDisplayColorHex { get; set; } = "#FFFF00FF";

 public string PlayButtonAttentionColorHex { get; set; } = "#FFFF00FF";

 public string PlayButtonAttentionTextColorHex { get; set; } = "#880000FF";

 public Dictionary<AttentionCategory, bool> AttentionCategories { get; set; } = new();

 public Dictionary<AttentionCategory, string> CategoryPrefixes { get; set; } = new();

 public IReadOnlyList<SpreadsheetSourceItem> Sources { get; set; } = Array.Empty<SpreadsheetSourceItem>();

 public SubscriptionRefreshReport LastRefreshReport { get; set; } = new();

 public int AttentionPositionOffsetX { get; set; }

 public int AttentionPositionOffsetY { get; set; }

 public string PlayButtonConfirmColorHex { get; set; } = "#FF5933FF";

 public string PlayButtonConfirmText { get; set; } = "本当？";

 public int PlayButtonConfirmDurationSeconds { get; set; } = 6;
}
