using Newtonsoft.Json;
using UnbsAttention.Services;

namespace UnbsAttention.Models;

public sealed class AttentionDatabase
{
 private static readonly ISet<AttentionCategory> EmptyExcludedCategories = new HashSet<AttentionCategory>();

 [JsonProperty("version")]
 public int Version { get; set; } = 1;

 [JsonProperty("entries")]
 public List<AttentionEntry> Entries { get; set; } = new();

 public bool TryGet(string levelId, out AttentionEntry? entry)
 {
  entry = Entries.FirstOrDefault(e => string.Equals(e.LevelId, levelId, StringComparison.OrdinalIgnoreCase));
  return entry is not null;
 }

 public bool Add(AttentionEntry candidate)
 {
  if (!AttentionEntryValidator.IsValidForMatching(candidate))
  {
   return false;
  }

  Entries.Add(CloneEntry(candidate));
  return true;
 }

 public bool Upsert(AttentionEntry candidate)
 {
  if (!AttentionEntryValidator.IsValidForMatching(candidate))
  {
   return false;
  }

  var incoming = CloneEntry(candidate);

  if (string.IsNullOrWhiteSpace(incoming.LevelId))
  {
   Entries.Add(incoming);
   return true;
  }

  var levelId = incoming.LevelId.Trim();

  for (var i = 0; i < Entries.Count; i++)
  {
   var existing = Entries[i];
   if (string.IsNullOrWhiteSpace(existing.LevelId))
   {
    continue;
   }

   if (string.Equals(existing.LevelId.Trim(), levelId, StringComparison.OrdinalIgnoreCase))
   {
    Entries[i] = incoming;
    return true;
   }
  }

  Entries.Add(incoming);
  return true;
 }

 public int MergeFrom(AttentionDatabase incoming)
 {
  if (incoming is null)
  {
   return 0;
  }

  var changed = 0;
  foreach (var entry in incoming.Entries)
  {
   if (Add(entry))
   {
    changed++;
   }
  }

  return changed;
 }

 public IReadOnlyList<AttentionEntry> FindByContext(AttentionLookupContext context, IEnumerable<string>? excludedCategories)
 {
  var excludedSet = BuildExcludedCategorySet(excludedCategories);
  return FindByContext(context, excludedSet);
 }

 public IReadOnlyList<AttentionEntry> FindByContext(AttentionLookupContext context, ISet<AttentionCategory>? excludedCategories)
 {
  var excludedSet = excludedCategories ?? EmptyExcludedCategories;
  return Entries
    .Where(e => !excludedSet.Contains(e.Category)
        && AttentionTargetMatcher.IsMatch(e, context))
    .ToList();
 }

 public bool TryGetByContext(
     AttentionLookupContext context,
     IEnumerable<string>? excludedCategories,
     out AttentionEntry? entry)
 {
  var excludedSet = BuildExcludedCategorySet(excludedCategories);

  return TryGetByContext(context, excludedSet, out entry);
 }

 public bool TryGetByContext(
     AttentionLookupContext context,
     ISet<AttentionCategory>? excludedCategories,
     out AttentionEntry? entry)
 {
  var excludedSet = excludedCategories ?? EmptyExcludedCategories;

  entry = Entries.FirstOrDefault(e =>
      !excludedSet.Contains(e.Category)
      && AttentionTargetMatcher.IsMatch(e, context));

  return entry is not null;
 }

 private static HashSet<AttentionCategory> BuildExcludedCategorySet(IEnumerable<string>? categories)
 {
  var result = new HashSet<AttentionCategory>();
  if (categories is null)
  {
   return result;
  }

  foreach (var value in categories)
  {
   if (string.IsNullOrWhiteSpace(value))
   {
    continue;
   }

   if (TryParseCategory(value, out var category))
   {
    result.Add(category);
   }
  }

  return result;
 }

 private static bool TryParseCategory(string text, out AttentionCategory category)
 {
  category = AttentionCategory.Other;

  var normalized = text.Replace("-", string.Empty).Replace("_", string.Empty).Trim();
  if (string.Equals(normalized, "NotForStreaming", StringComparison.OrdinalIgnoreCase)
      || string.Equals(normalized, "NotforStreaming", StringComparison.OrdinalIgnoreCase)
      || string.Equals(normalized, "NotStreaming", StringComparison.OrdinalIgnoreCase))
  {
   category = AttentionCategory.NotForStreaming;
   return true;
  }

  if (string.Equals(normalized, "NotForVideo", StringComparison.OrdinalIgnoreCase)
      || string.Equals(normalized, "NorForVideo", StringComparison.OrdinalIgnoreCase)
      || string.Equals(normalized, "NoVideo", StringComparison.OrdinalIgnoreCase)
      || string.Equals(normalized, "VideoNG", StringComparison.OrdinalIgnoreCase))
  {
   category = AttentionCategory.NotForVideo;
   return true;
  }

  if (string.Equals(normalized, "StageGimmick", StringComparison.OrdinalIgnoreCase)
      || string.Equals(normalized, "StageTrick", StringComparison.OrdinalIgnoreCase))
  {
   category = AttentionCategory.StageGimmick;
   return true;
  }

  if (string.Equals(normalized, "Phobia", StringComparison.OrdinalIgnoreCase)
      || string.Equals(normalized, "Phobias", StringComparison.OrdinalIgnoreCase))
  {
   category = AttentionCategory.Phobia;
   return true;
  }

  return Enum.TryParse(text, true, out category);
 }

 private static AttentionEntry CloneEntry(AttentionEntry source)
 {
  return new AttentionEntry
  {
   LevelId = source.LevelId,
   Category = source.Category,
   Target = source.Target is null
    ? null
    : new AttentionTarget
    {
     Bsr = source.Target.Bsr is null ? null : new List<string>(source.Target.Bsr),
     InfoIncludes = source.Target.InfoIncludes is null ? null : new List<string>(source.Target.InfoIncludes),
     InfoRegex = source.Target.InfoRegex,
     DescIncludes = source.Target.DescIncludes is null ? null : new List<string>(source.Target.DescIncludes),
     DescRegex = source.Target.DescRegex,
    },
   Reason = source.Reason,
   UpdatedAtUtc = source.UpdatedAtUtc,
   UpdatedBy = source.UpdatedBy,
  };
 }
}
