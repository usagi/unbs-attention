namespace UnbsAttention.Services;

public interface ITwitchLiveChecker
{
 Task<bool> IsLiveAsync(
     string broadcasterId,
     string clientId,
     string appAccessToken,
     CancellationToken cancellationToken);
}
