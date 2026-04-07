namespace UnbsAttention.Models;

public sealed class SpreadsheetSourceItem
{
 public int Index { get; set; }

 public string RawSource { get; set; } = string.Empty;

 public string OpenUrl { get; set; } = string.Empty;

 public string CsvExportUrl { get; set; } = string.Empty;

 public bool IsValid { get; set; }
}
