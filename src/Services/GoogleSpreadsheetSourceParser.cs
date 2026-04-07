using System.Text.RegularExpressions;

namespace UnbsAttention.Services;

public static class GoogleSpreadsheetSourceParser
{
 private static readonly Regex UrlPattern = new("spreadsheets/d/([a-zA-Z0-9-_]+)", RegexOptions.Compiled);
 private static readonly Regex IdOnlyPattern = new("^[a-zA-Z0-9-_]{20,}$", RegexOptions.Compiled);
 private static readonly Regex GidPattern = new("[?&]gid=([0-9]+)", RegexOptions.Compiled);

 public static bool TryBuildCsvExportUrl(string source, out string exportUrl)
 {
  return TryBuildCsvGvizUrl(source, out exportUrl);
 }

 public static bool TryBuildCsvGvizUrl(string source, out string csvUrl)
 {
  csvUrl = string.Empty;
  if (string.IsNullOrWhiteSpace(source))
  {
   return false;
  }

  var raw = source.Trim();
  var id = ExtractSheetId(raw);
  if (string.IsNullOrWhiteSpace(id))
  {
   return false;
  }

  var gid = ExtractGid(raw) ?? "0";
  csvUrl = $"https://docs.google.com/spreadsheets/d/{id}/gviz/tq?tqx=out:csv&gid={gid}";
  return true;
 }

 public static bool TryBuildLegacyCsvExportUrl(string source, out string exportUrl)
 {
  exportUrl = string.Empty;
  if (string.IsNullOrWhiteSpace(source))
  {
   return false;
  }

  var raw = source.Trim();
  var id = ExtractSheetId(raw);
  if (string.IsNullOrWhiteSpace(id))
  {
   return false;
  }

  var gid = ExtractGid(raw) ?? "0";
  exportUrl = $"https://docs.google.com/spreadsheets/d/{id}/export?format=csv&gid={gid}";
  return true;
 }

 public static bool TryBuildOpenUrl(string source, out string openUrl)
 {
  openUrl = string.Empty;
  if (string.IsNullOrWhiteSpace(source))
  {
   return false;
  }

  var raw = source.Trim();
  var id = ExtractSheetId(raw);
  if (string.IsNullOrWhiteSpace(id))
  {
   return false;
  }

  var gid = ExtractGid(raw) ?? "0";
  openUrl = $"https://docs.google.com/spreadsheets/d/{id}/edit#gid={gid}";
  return true;
 }

 private static string? ExtractSheetId(string value)
 {
  var match = UrlPattern.Match(value);
  if (match.Success)
  {
   return match.Groups[1].Value;
  }

  return IdOnlyPattern.IsMatch(value) ? value : null;
 }

 private static string? ExtractGid(string value)
 {
  var match = GidPattern.Match(value);
  return match.Success ? match.Groups[1].Value : null;
 }
}
