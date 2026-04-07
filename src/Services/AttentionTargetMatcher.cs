using System.Text.RegularExpressions;
using UnbsAttention.Models;

namespace UnbsAttention.Services;

public static class AttentionTargetMatcher
{
 public static bool IsMatch(AttentionEntry entry, AttentionLookupContext context)
 {
  if (!string.IsNullOrWhiteSpace(entry.LevelId)
      && !string.IsNullOrWhiteSpace(context.LevelId)
      && string.Equals(entry.LevelId, context.LevelId, StringComparison.OrdinalIgnoreCase))
  {
   return true;
  }

  var target = entry.Target;
  if (target is null)
  {
   return false;
  }

  var hasAnyRule = HasAnyRule(target);
  if (!hasAnyRule)
  {
   return false;
  }

  return MatchBsr(target.Bsr, context.BsrId)
      || MatchIncludes(target.InfoIncludes, context.BuildInfoSearchText())
      || MatchRegex(target.InfoRegex, context.BuildInfoSearchText())
      || MatchIncludes(target.DescIncludes, context.BeatSaverDescription)
      || MatchRegex(target.DescRegex, context.BeatSaverDescription);
 }

 private static bool HasAnyRule(AttentionTarget target)
 {
  return HasAnyText(target.Bsr)
      || HasAnyText(target.InfoIncludes)
      || !string.IsNullOrWhiteSpace(target.InfoRegex)
      || HasAnyText(target.DescIncludes)
      || !string.IsNullOrWhiteSpace(target.DescRegex);
 }

 private static bool HasAnyText(IEnumerable<string>? values)
 {
  return values is not null && values.Any(v => !string.IsNullOrWhiteSpace(v));
 }

 private static bool MatchBsr(IEnumerable<string>? patterns, string? bsrId)
 {
  if (string.IsNullOrWhiteSpace(bsrId) || patterns is null)
  {
   return false;
  }

  var normalizedBsrId = bsrId!.Trim();
  foreach (var pattern in patterns)
  {
   if (string.IsNullOrWhiteSpace(pattern))
   {
    continue;
   }

   if (string.Equals(pattern.Trim(), normalizedBsrId, StringComparison.OrdinalIgnoreCase))
   {
    return true;
   }
  }

  return false;
 }

 private static bool MatchIncludes(IEnumerable<string>? patterns, string? haystack)
 {
  if (string.IsNullOrWhiteSpace(haystack) || patterns is null)
  {
   return false;
  }

  var source = haystack!;

  foreach (var pattern in patterns)
  {
   if (string.IsNullOrWhiteSpace(pattern))
   {
    continue;
   }

   var needle = pattern.Trim();
   if (needle.Length == 0)
   {
    continue;
   }

   if (source.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
   {
    return true;
   }
  }

  return false;
 }

 private static bool MatchRegex(string? pattern, string? input)
 {
  if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(input))
  {
   return false;
  }

  try
  {
   return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
  }
  catch (ArgumentException)
  {
   return false;
  }
 }
}
