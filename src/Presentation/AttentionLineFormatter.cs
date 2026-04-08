using UnbsAttention.Config;
using UnbsAttention.Models;

namespace UnbsAttention.Presentation;

public static class AttentionLineFormatter
{
 private static readonly Dictionary<AttentionCategory, string> DefaultPrefixes = new()
 {
    { AttentionCategory.Mute, "🔇[Mute]" },
   { AttentionCategory.NotForStreaming, "🚫📡" },
   { AttentionCategory.NotForVideo, "🚫🎥" },
    { AttentionCategory.StageGimmick, "🎭[Stage Gimmick]" },
    { AttentionCategory.Phobia, "😨[Phobia]" },
    { AttentionCategory.Jumpscare, "😱[Jumpscare]" },
    { AttentionCategory.Heavy, "⚠️[Heavy]" },
    { AttentionCategory.Other, "🏷️[Other]" },
 };

 public static string GetDefaultPrefix(AttentionCategory category)
 {
  return DefaultPrefixes.TryGetValue(category, out var prefix)
   ? prefix
   : $"[{category}]";
 }

 public static string Format(AttentionEntry entry, AttentionDisplayMode mode, IReadOnlyDictionary<string, string>? categoryPrefixes)
 {
  return Format(entry.Category, entry.Reason, mode, categoryPrefixes);
 }

 public static string Format(
  AttentionCategory category,
  string? reason,
  AttentionDisplayMode mode,
  IReadOnlyDictionary<string, string>? categoryPrefixes)
 {
  var prefix = ResolvePrefix(category, categoryPrefixes);

  if (mode == AttentionDisplayMode.IconOnly)
  {
   return prefix;
  }

  if (string.IsNullOrWhiteSpace(reason))
  {
   return prefix;
  }

  return $"{prefix} {reason}";
 }

 private static string ResolvePrefix(AttentionCategory category, IReadOnlyDictionary<string, string>? categoryPrefixes)
 {
  if (categoryPrefixes is not null
          && categoryPrefixes.TryGetValue(category.ToString(), out var configured)
          && !string.IsNullOrWhiteSpace(configured))
  {
   return configured.Trim();
  }

  return GetDefaultPrefix(category);
 }
}
