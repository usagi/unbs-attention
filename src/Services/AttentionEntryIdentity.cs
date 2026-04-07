using System.Text;
using UnbsAttention.Models;

namespace UnbsAttention.Services;

public static class AttentionEntryIdentity
{
 public static string BuildKey(AttentionEntry entry)
 {
  if (!string.IsNullOrWhiteSpace(entry.LevelId))
  {
   return "level:" + entry.LevelId.Trim().ToLowerInvariant();
  }

  var target = entry.Target;
  if (target is null)
  {
   return "fallback:" + entry.Category + ":" + Normalize(entry.Reason);
  }

  var builder = new StringBuilder("target:");
  builder.Append("bsr=").Append(NormalizeList(target.Bsr)).Append('|');
  builder.Append("ii=").Append(NormalizeList(target.InfoIncludes)).Append('|');
  builder.Append("ir=").Append(Normalize(target.InfoRegex)).Append('|');
  builder.Append("di=").Append(NormalizeList(target.DescIncludes)).Append('|');
  builder.Append("dr=").Append(Normalize(target.DescRegex));

  var key = builder.ToString();
  if (key == "target:bsr=|ii=|ir=|di=|dr=")
  {
   return "fallback:" + entry.Category + ":" + Normalize(entry.Reason);
  }

  return key;
 }

 private static string Normalize(string? value)
 {
  return string.IsNullOrWhiteSpace(value)
   ? string.Empty
   : value!.Trim().ToLowerInvariant();
 }

 private static string NormalizeList(IEnumerable<string>? values)
 {
  if (values is null)
  {
   return string.Empty;
  }

  var normalized = values
   .Where(v => !string.IsNullOrWhiteSpace(v))
   .Select(v => v.Trim().ToLowerInvariant())
   .Distinct(StringComparer.Ordinal)
   .OrderBy(v => v, StringComparer.Ordinal)
   .ToArray();

  return string.Join(",", normalized);
 }
}
