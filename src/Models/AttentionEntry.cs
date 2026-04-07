using Newtonsoft.Json;

namespace UnbsAttention.Models;

public sealed class AttentionEntry
{
 // 旧フォーマット互換。新規データは `target` 側を使う。
 [JsonProperty("levelId")]
 public string LevelId { get; set; } = string.Empty;

 [JsonProperty("category")]
 public AttentionCategory Category { get; set; } = AttentionCategory.Other;

 [JsonProperty("target")]
 public AttentionTarget? Target { get; set; }

 [JsonProperty("reason")]
 public string Reason { get; set; } = string.Empty;

 [JsonProperty("updatedAtUtc")]
 public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

 [JsonProperty("updatedBy")]
 public string UpdatedBy { get; set; } = string.Empty;
}
