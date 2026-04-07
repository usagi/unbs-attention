using Newtonsoft.Json;

namespace UnbsAttention.Models;

public sealed class AttentionTarget
{
 [JsonProperty("bsr")]
 public List<string>? Bsr { get; set; }

 [JsonProperty("info_includes")]
 public List<string>? InfoIncludes { get; set; }

 [JsonProperty("info_regex")]
 public string? InfoRegex { get; set; }

 [JsonProperty("desc_includes")]
 public List<string>? DescIncludes { get; set; }

 [JsonProperty("desc_regex")]
 public string? DescRegex { get; set; }
}
