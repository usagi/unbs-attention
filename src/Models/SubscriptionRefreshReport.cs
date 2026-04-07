namespace UnbsAttention.Models;

public sealed class SubscriptionRefreshReport
{
 public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

 public DateTime FinishedAtUtc { get; set; } = DateTime.UtcNow;

 public int TotalSources { get; set; }

 public int SucceededSources { get; set; }

 public int FailedSources { get; set; }

 public int ImportedRows { get; set; }

 public List<SubscriptionSourceRefreshResult> Sources { get; set; } = new();
}
