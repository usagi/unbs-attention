using UnbsAttention.Models;

namespace UnbsAttention.Services;

public interface IAttentionSyncProvider
{
 string Name { get; }

 Task<AttentionDatabase?> PullLatestAsync(CancellationToken cancellationToken);

 // 互換性のため残している。既定運用は pull のみ。
 Task PushAsync(AttentionDatabase database, CancellationToken cancellationToken);
}
