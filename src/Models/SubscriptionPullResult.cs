namespace UnbsAttention.Models;

public sealed class SubscriptionPullResult
{
 public AttentionDatabase? Database { get; set; }

 public SubscriptionRefreshReport Report { get; set; } = new();
}
