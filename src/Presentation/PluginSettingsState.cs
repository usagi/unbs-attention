using UnbsAttention.Models;

namespace UnbsAttention.Presentation;

public sealed class PluginSettingsState
{
 public bool Enabled { get; set; } = true;

 public Dictionary<AttentionCategory, bool> AttentionCategories { get; set; } = new();

 public Dictionary<AttentionCategory, string> CategoryPrefixes { get; set; } = new();

 public IReadOnlyList<SpreadsheetSourceItem> Sources { get; set; } = Array.Empty<SpreadsheetSourceItem>();

 public SubscriptionRefreshReport LastRefreshReport { get; set; } = new();

 public int AttentionPositionOffsetX { get; set; }

 public int AttentionPositionOffsetY { get; set; }
}
