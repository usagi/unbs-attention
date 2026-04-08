namespace UnbsAttention.Services;

public interface IBeatSaverMapClient
{
 Task<string?> GetDescriptionByBsrIdAsync(string bsrId, CancellationToken cancellationToken);

 Task<string?> GetDescriptionByHashAsync(string hash, CancellationToken cancellationToken);

 Task<BeatSaverMapDetails?> GetMapDetailsByHashAsync(string hash, CancellationToken cancellationToken);
}
