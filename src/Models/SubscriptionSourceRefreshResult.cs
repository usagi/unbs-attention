namespace UnbsAttention.Models;

public sealed class SubscriptionSourceRefreshResult
{
 public string RawSource { get; set; } = string.Empty;

 public string CsvExportUrl { get; set; } = string.Empty;

 public bool IsSuccess { get; set; }

 public string Message { get; set; } = string.Empty;

 public int ImportedRows { get; set; }
}
