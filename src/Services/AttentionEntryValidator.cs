using System.Text.RegularExpressions;
using UnbsAttention.Models;

namespace UnbsAttention.Services;

public static class AttentionEntryValidator
{
 public static bool IsValidForMatching(AttentionEntry entry)
 {
  return ValidateEntry(entry).Count == 0;
 }

 public static List<string> ValidateEntry(AttentionEntry entry)
 {
  var issues = new List<string>();

  var hasTarget = HasTargetRule(entry.Target);
  if (!hasTarget)
  {
   issues.Add("entry must have at least one target rule");
  }

  if (!string.IsNullOrWhiteSpace(entry.Target?.InfoRegex) && !IsValidRegex(entry.Target!.InfoRegex!))
  {
   issues.Add("target.info_regex is invalid");
  }

  if (!string.IsNullOrWhiteSpace(entry.Target?.DescRegex) && !IsValidRegex(entry.Target!.DescRegex!))
  {
   issues.Add("target.desc_regex is invalid");
  }

  return issues;
 }

 public static IReadOnlyList<AttentionValidationIssue> ValidateDatabase(AttentionDatabase database)
 {
  var result = new List<AttentionValidationIssue>();
  for (var i = 0; i < database.Entries.Count; i++)
  {
   var entry = database.Entries[i];
   var issues = ValidateEntry(entry);
   foreach (var issue in issues)
   {
    result.Add(new AttentionValidationIssue
    {
     EntryIndex = i,
     Code = "invalid_entry",
     Message = issue,
    });
   }
  }

  return result;
 }

 private static bool HasTargetRule(AttentionTarget? target)
 {
  if (target is null)
  {
   return false;
  }

  return HasAny(target.Bsr)
   || HasAny(target.InfoIncludes)
   || !string.IsNullOrWhiteSpace(target.InfoRegex)
   || HasAny(target.DescIncludes)
   || !string.IsNullOrWhiteSpace(target.DescRegex);
 }

 private static bool HasAny(IEnumerable<string>? values)
 {
  return values is not null && values.Any(v => !string.IsNullOrWhiteSpace(v));
 }

 private static bool IsValidRegex(string pattern)
 {
  try
  {
   _ = new Regex(pattern, RegexOptions.CultureInvariant);
   return true;
  }
  catch (ArgumentException)
  {
   return false;
  }
 }
}
