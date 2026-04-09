using System.Globalization;
using System.Text.RegularExpressions;
using UnbsAttention.Models;

namespace UnbsAttention.Services;

internal sealed class AttentionMatcherIndex
{
 private static readonly RegexOptions CompiledRegexOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

 public static AttentionMatcherIndex Empty { get; } = new(
  new List<AttentionPayload>(),
  new Dictionary<ulong, int[]>(),
  hasInfoRules: false,
  new AhoCorasickMatcher(Array.Empty<string>()),
  Array.Empty<int[]>(),
  new AhoCorasickMatcher(Array.Empty<string>()),
  Array.Empty<int[]>(),
  Array.Empty<RegexRule>(),
  Array.Empty<RegexRule>(),
  new HashSet<AttentionCategory>(),
  new HashSet<AttentionCategory>());

 private readonly IReadOnlyList<AttentionPayload> _payloads;
 private readonly Dictionary<ulong, int[]> _bsrIndex;
 private readonly bool _hasInfoRules;
 private readonly AhoCorasickMatcher _infoIncludesMatcher;
 private readonly int[][] _infoIncludesPayloadsByPattern;
 private readonly AhoCorasickMatcher _descIncludesMatcher;
 private readonly int[][] _descIncludesPayloadsByPattern;
 private readonly RegexRule[] _infoRegexRules;
 private readonly RegexRule[] _descRegexRules;
 private readonly HashSet<AttentionCategory> _categoriesWithDescriptionRules;
 private readonly HashSet<AttentionCategory> _categoriesWithBsrRules;

 private AttentionMatcherIndex(
  IReadOnlyList<AttentionPayload> payloads,
  Dictionary<ulong, int[]> bsrIndex,
  bool hasInfoRules,
  AhoCorasickMatcher infoIncludesMatcher,
  int[][] infoIncludesPayloadsByPattern,
  AhoCorasickMatcher descIncludesMatcher,
  int[][] descIncludesPayloadsByPattern,
  RegexRule[] infoRegexRules,
  RegexRule[] descRegexRules,
  HashSet<AttentionCategory> categoriesWithDescriptionRules,
  HashSet<AttentionCategory> categoriesWithBsrRules)
 {
  _payloads = payloads;
  _bsrIndex = bsrIndex;
  _hasInfoRules = hasInfoRules;
  _infoIncludesMatcher = infoIncludesMatcher;
  _infoIncludesPayloadsByPattern = infoIncludesPayloadsByPattern;
  _descIncludesMatcher = descIncludesMatcher;
  _descIncludesPayloadsByPattern = descIncludesPayloadsByPattern;
  _infoRegexRules = infoRegexRules;
  _descRegexRules = descRegexRules;
  _categoriesWithDescriptionRules = categoriesWithDescriptionRules;
  _categoriesWithBsrRules = categoriesWithBsrRules;
 }

 public static AttentionMatcherIndex Build(AttentionDatabase database)
 {
  if (database is null || database.Entries.Count == 0)
  {
   return Empty;
  }

  var payloadIds = new Dictionary<AttentionPayloadKey, int>();
  var payloads = new List<AttentionPayload>();

  var bsrPayloads = new Dictionary<ulong, HashSet<int>>();
  var infoIncludesPayloads = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
  var descIncludesPayloads = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
  var infoRegexRules = new List<RegexRule>();
  var descRegexRules = new List<RegexRule>();
  var categoriesWithDescriptionRules = new HashSet<AttentionCategory>();
  var categoriesWithBsrRules = new HashSet<AttentionCategory>();

  foreach (var entry in database.Entries)
  {
   if (entry.Target is null)
   {
    continue;
   }

   var payloadId = GetOrCreatePayloadId(payloadIds, payloads, entry);
   var target = entry.Target;

   var hasAnyValidBsr = false;
   if (target.Bsr is not null)
   {
    foreach (var bsrPattern in target.Bsr)
    {
     if (!TryParseBsrHex(bsrPattern, out var bsrValue))
     {
      continue;
     }

     hasAnyValidBsr = true;
     AddPayloadReference(bsrPayloads, bsrValue, payloadId);
    }
   }

   if (hasAnyValidBsr)
   {
    categoriesWithBsrRules.Add(entry.Category);
   }

   if (target.InfoIncludes is not null)
   {
    foreach (var infoInclude in target.InfoIncludes)
    {
     var normalized = NormalizeIncludesPattern(infoInclude);
     if (normalized.Length == 0)
     {
      continue;
     }

     AddPayloadReference(infoIncludesPayloads, normalized, payloadId);
    }
   }

   if (!string.IsNullOrWhiteSpace(target.InfoRegex))
   {
    infoRegexRules.Add(new RegexRule
    {
     PayloadId = payloadId,
     Regex = CompileRegexOrNull(target.InfoRegex),
    });
   }

   var hasDescriptionRule = false;

   if (target.DescIncludes is not null)
   {
    foreach (var descInclude in target.DescIncludes)
    {
     var normalized = NormalizeIncludesPattern(descInclude);
     if (normalized.Length == 0)
     {
      continue;
     }

     hasDescriptionRule = true;
     AddPayloadReference(descIncludesPayloads, normalized, payloadId);
    }
   }

   if (!string.IsNullOrWhiteSpace(target.DescRegex))
   {
    var regex = CompileRegexOrNull(target.DescRegex);
    descRegexRules.Add(new RegexRule
    {
     PayloadId = payloadId,
     Regex = regex,
    });

    hasDescriptionRule = hasDescriptionRule || regex is not null;
   }

   if (hasDescriptionRule)
   {
    categoriesWithDescriptionRules.Add(entry.Category);
   }
  }

  var finalizedBsr = bsrPayloads.ToDictionary(
   x => x.Key,
   x => x.Value.OrderBy(id => id).ToArray());

  var infoIncludesMatcher = BuildIncludesMatcher(infoIncludesPayloads, out var infoIncludesByPattern);
  var descIncludesMatcher = BuildIncludesMatcher(descIncludesPayloads, out var descIncludesByPattern);

  return new AttentionMatcherIndex(
   payloads,
   finalizedBsr,
    hasInfoRules: infoIncludesByPattern.Length > 0 || infoRegexRules.Count > 0,
   infoIncludesMatcher,
   infoIncludesByPattern,
   descIncludesMatcher,
   descIncludesByPattern,
   infoRegexRules.ToArray(),
   descRegexRules.ToArray(),
   categoriesWithDescriptionRules,
   categoriesWithBsrRules);
 }

 public bool HasDescriptionRules(ISet<AttentionCategory>? excludedCategories)
 {
  return HasAnyEnabledCategory(_categoriesWithDescriptionRules, excludedCategories);
 }

 public bool HasBsrRules(ISet<AttentionCategory>? excludedCategories)
 {
  return HasAnyEnabledCategory(_categoriesWithBsrRules, excludedCategories);
 }

 public IReadOnlyList<AttentionPayload> FindMatches(AttentionLookupContext context, ISet<AttentionCategory>? excludedCategories)
 {
  if (_payloads.Count == 0)
  {
   return Array.Empty<AttentionPayload>();
  }

  var matchedPayloadIds = new HashSet<int>();

  if (TryParseBsrHex(context.BsrId, out var bsrValue)
   && _bsrIndex.TryGetValue(bsrValue, out var bsrPayloadIds))
  {
   AddPayloadReferences(matchedPayloadIds, bsrPayloadIds);
  }

  if (_hasInfoRules)
  {
   var infoSearchText = context.BuildInfoSearchText();
   CollectIncludesMatches(_infoIncludesMatcher, _infoIncludesPayloadsByPattern, infoSearchText, matchedPayloadIds);
   CollectRegexMatches(_infoRegexRules, infoSearchText, matchedPayloadIds);
  }

  var description = context.BeatSaverDescription;
  if (!string.IsNullOrWhiteSpace(description))
  {
   CollectIncludesMatches(_descIncludesMatcher, _descIncludesPayloadsByPattern, description, matchedPayloadIds);
   CollectRegexMatches(_descRegexRules, description, matchedPayloadIds);
  }

  if (matchedPayloadIds.Count == 0)
  {
   return Array.Empty<AttentionPayload>();
  }

  var excludedSet = excludedCategories;
  var result = new List<AttentionPayload>(matchedPayloadIds.Count);
  foreach (var payloadId in matchedPayloadIds)
  {
   var payload = _payloads[payloadId];
   if (excludedSet is not null && excludedSet.Contains(payload.Category))
   {
    continue;
   }

   result.Add(payload);
  }

  if (result.Count == 0)
  {
   return Array.Empty<AttentionPayload>();
  }

  result.Sort(static (left, right) =>
  {
   var categoryComparison = left.Category.CompareTo(right.Category);
   if (categoryComparison != 0)
   {
    return categoryComparison;
   }

   return left.Order.CompareTo(right.Order);
  });

  return result;
 }

 public static bool TryParseBsrHex(string? value, out ulong parsed)
 {
  parsed = 0;
  if (string.IsNullOrWhiteSpace(value))
  {
   return false;
  }

  var normalized = value!.Trim();
  if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
  {
   normalized = normalized.Substring(2);
  }

  if (normalized.Length == 0)
  {
   return false;
  }

  return ulong.TryParse(normalized, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out parsed);
 }

 private static AhoCorasickMatcher BuildIncludesMatcher(
  Dictionary<string, HashSet<int>> payloadsByPattern,
  out int[][] payloadsByPatternIndex)
 {
  if (payloadsByPattern.Count == 0)
  {
   payloadsByPatternIndex = Array.Empty<int[]>();
   return new AhoCorasickMatcher(Array.Empty<string>());
  }

  var orderedPatterns = payloadsByPattern.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray();
  payloadsByPatternIndex = new int[orderedPatterns.Length][];

  for (var i = 0; i < orderedPatterns.Length; i++)
  {
   payloadsByPatternIndex[i] = payloadsByPattern[orderedPatterns[i]].OrderBy(x => x).ToArray();
  }

  return new AhoCorasickMatcher(orderedPatterns);
 }

 private static Regex? CompileRegexOrNull(string? pattern)
 {
  if (string.IsNullOrWhiteSpace(pattern))
  {
   return null;
  }

  try
  {
   return new Regex(pattern, CompiledRegexOptions);
  }
  catch (ArgumentException)
  {
   return null;
  }
 }

 private static int GetOrCreatePayloadId(
  Dictionary<AttentionPayloadKey, int> payloadIds,
  ICollection<AttentionPayload> payloads,
  AttentionEntry entry)
 {
  var key = AttentionPayloadKey.FromEntry(entry);
  if (payloadIds.TryGetValue(key, out var existing))
  {
   return existing;
  }

  var nextId = payloads.Count;
  payloadIds[key] = nextId;

  payloads.Add(new AttentionPayload(entry.Category, (entry.Reason ?? string.Empty).Trim(), nextId));

  return nextId;
 }

 private static void AddPayloadReference<TKey>(Dictionary<TKey, HashSet<int>> index, TKey key, int payloadId)
  where TKey : notnull
 {
  if (!index.TryGetValue(key, out var payloads))
  {
   payloads = new HashSet<int>();
   index[key] = payloads;
  }

  payloads.Add(payloadId);
 }

 private static string NormalizeIncludesPattern(string? pattern)
 {
  return string.IsNullOrWhiteSpace(pattern)
   ? string.Empty
  : pattern!.Trim().ToUpperInvariant();
 }

 private static void CollectIncludesMatches(
  AhoCorasickMatcher matcher,
  IReadOnlyList<int[]> payloadsByPatternIndex,
  string? input,
  HashSet<int> sink)
 {
  if (string.IsNullOrWhiteSpace(input) || matcher.IsEmpty || payloadsByPatternIndex.Count == 0)
  {
   return;
  }

  matcher.CollectMatches(input, payloadsByPatternIndex, sink);
 }

 private static void CollectRegexMatches(IReadOnlyList<RegexRule> rules, string? input, HashSet<int> sink)
 {
  if (string.IsNullOrWhiteSpace(input) || rules.Count == 0)
  {
   return;
  }

  foreach (var rule in rules)
  {
   if (rule.Regex is null)
   {
    continue;
   }

   if (rule.Regex.IsMatch(input))
   {
    sink.Add(rule.PayloadId);
   }
  }
 }

 private static void AddPayloadReferences(HashSet<int> sink, IReadOnlyList<int> payloadIds)
 {
  for (var i = 0; i < payloadIds.Count; i++)
  {
   sink.Add(payloadIds[i]);
  }
 }

 private static bool HasAnyEnabledCategory(
  IReadOnlyCollection<AttentionCategory> categories,
  ISet<AttentionCategory>? excludedCategories)
 {
  if (categories.Count == 0)
  {
   return false;
  }

  if (excludedCategories is null || excludedCategories.Count == 0)
  {
   return true;
  }

  foreach (var category in categories)
  {
   if (!excludedCategories.Contains(category))
   {
    return true;
   }
  }

  return false;
 }

 private sealed class RegexRule
 {
  public int PayloadId { get; set; }

  public Regex? Regex { get; set; }
 }
}

internal readonly struct AttentionPayload
{
 public AttentionPayload(AttentionCategory category, string reason, int order)
 {
  Category = category;
  Reason = reason;
  Order = order;
 }

 public AttentionCategory Category { get; }

 public string Reason { get; }

 public int Order { get; }
}

internal readonly struct AttentionPayloadKey : IEquatable<AttentionPayloadKey>
{
 private static readonly StringComparer ReasonComparer = StringComparer.OrdinalIgnoreCase;

 public AttentionCategory Category { get; }

 public string NormalizedReason { get; }

 private AttentionPayloadKey(AttentionCategory category, string normalizedReason)
 {
  Category = category;
  NormalizedReason = normalizedReason;
 }

 public static AttentionPayloadKey FromEntry(AttentionEntry entry)
 {
  return new AttentionPayloadKey(entry.Category, NormalizeReason(entry.Reason));
 }

 public bool Equals(AttentionPayloadKey other)
 {
  return Category == other.Category
   && ReasonComparer.Equals(NormalizedReason, other.NormalizedReason);
 }

 public override bool Equals(object? obj)
 {
  return obj is AttentionPayloadKey other && Equals(other);
 }

 public override int GetHashCode()
 {
  unchecked
  {
   return ((int)Category * 397) ^ ReasonComparer.GetHashCode(NormalizedReason);
  }
 }

 private static string NormalizeReason(string? reason)
 {
  return string.IsNullOrWhiteSpace(reason)
   ? string.Empty
   : reason!.Trim();
 }
}