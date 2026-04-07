using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace UnbsAttention.Models;

[JsonConverter(typeof(StringEnumConverter))]
public enum AttentionCategory
{
 Mute,
 NotForStreaming,
 NotForVideo,
 StageGimmick,
 Phobia,
 Jumpscare,
 Heavy,
 Other,
}
