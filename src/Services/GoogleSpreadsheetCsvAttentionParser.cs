using System.Text.RegularExpressions;
using UnbsAttention.Models;

namespace UnbsAttention.Services;

public static class GoogleSpreadsheetCsvAttentionParser
{
 public static AttentionDatabase Parse(string csv)
 {
  var database = new AttentionDatabase();
  var rows = CsvReader.Parse(csv);
  if (rows.Count == 0)
  {
   return database;
  }

  var header = rows[0]
   .Select((name, index) => new { Name = NormalizeHeader(name), Index = index })
   .Where(x => !string.IsNullOrWhiteSpace(x.Name))
   .ToDictionary(x => x.Name, x => x.Index, StringComparer.OrdinalIgnoreCase);

  for (var i = 1; i < rows.Count; i++)
  {
   var row = rows[i];
   var entry = ParseRow(header, row);
   if (entry is null)
   {
    continue;
   }

   database.Add(entry);
  }

  return database;
 }

 private static AttentionEntry? ParseRow(Dictionary<string, int> header, IReadOnlyList<string> row)
 {
  var reason = Get(header, row, "reason");
  var categoryRaw = Get(header, row, "category");

  var category = ParseCategory(categoryRaw);
  var entry = new AttentionEntry
  {
   LevelId = string.Empty,
   Category = category,
   Reason = reason ?? string.Empty,
   UpdatedBy = "sheet",
   UpdatedAtUtc = DateTime.UtcNow,
   Target = new AttentionTarget
   {
    Bsr = SplitList(Get(header, row, "bsr"), splitOnWhitespace: true),
    InfoIncludes = SplitList(Get(header, row, "info_includes"), splitOnWhitespace: false),
    InfoRegex = EmptyToNull(Get(header, row, "info_regex")),
    DescIncludes = SplitList(Get(header, row, "desc_includes"), splitOnWhitespace: false),
    DescRegex = EmptyToNull(Get(header, row, "desc_regex")),
   },
  };

  return AttentionEntryValidator.IsValidForMatching(entry) ? entry : null;
 }

 private static string NormalizeHeader(string value)
 {
  return (value ?? string.Empty).Trim().ToLowerInvariant();
 }

 private static string? Get(Dictionary<string, int> header, IReadOnlyList<string> row, string name)
 {
  if (!header.TryGetValue(name, out var index))
  {
   return null;
  }

  if (index < 0 || index >= row.Count)
  {
   return null;
  }

  var value = row[index].Trim();
  return value.Length == 0 ? null : value;
 }

 private static List<string>? SplitList(string? value, bool splitOnWhitespace = false)
 {
  if (string.IsNullOrWhiteSpace(value))
  {
   return null;
  }

  var raw = splitOnWhitespace
   ? Regex.Split(value!, @"[;,|\s]+", RegexOptions.CultureInvariant)
   : value!
    .Split(new[] { ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries);

  var values = raw
   .Select(v => v.Trim())
   .Where(v => v.Length > 0)
   .Distinct(StringComparer.OrdinalIgnoreCase)
   .ToList();

  return values.Count == 0 ? null : values;
 }

 private static string? EmptyToNull(string? value)
 {
  return string.IsNullOrWhiteSpace(value) ? null : value;
 }

 private static AttentionCategory ParseCategory(string? text)
 {
  if (string.IsNullOrWhiteSpace(text))
  {
   return AttentionCategory.Other;
  }

  var normalized = text!.Replace("-", string.Empty).Replace("_", string.Empty).Trim();
  if (string.Equals(normalized, "NotForStreaming", StringComparison.OrdinalIgnoreCase)
      || string.Equals(normalized, "NotStreaming", StringComparison.OrdinalIgnoreCase))
  {
   return AttentionCategory.NotForStreaming;
  }

  if (string.Equals(normalized, "NotForVideo", StringComparison.OrdinalIgnoreCase)
    || string.Equals(normalized, "NorForVideo", StringComparison.OrdinalIgnoreCase)
    || string.Equals(normalized, "NoVideo", StringComparison.OrdinalIgnoreCase)
    || string.Equals(normalized, "VideoNG", StringComparison.OrdinalIgnoreCase))
  {
   return AttentionCategory.NotForVideo;
  }

  if (string.Equals(normalized, "StageGimmick", StringComparison.OrdinalIgnoreCase)
    || string.Equals(normalized, "StageTrick", StringComparison.OrdinalIgnoreCase))
  {
   return AttentionCategory.StageGimmick;
  }

  if (string.Equals(normalized, "Phobia", StringComparison.OrdinalIgnoreCase)
    || string.Equals(normalized, "Phobias", StringComparison.OrdinalIgnoreCase))
  {
   return AttentionCategory.Phobia;
  }

  return Enum.TryParse(text, true, out AttentionCategory category)
   ? category
   : AttentionCategory.Other;
 }
}
