using UnbsAttention.Config;

namespace UnbsAttention.Services;

public sealed class AttentionVisibilityPolicy
{
 private readonly PluginConfig _config;
 private readonly ITwitchLiveChecker _twitchLiveChecker;

 private DateTime _lastCheckedAtUtc = DateTime.MinValue;
 private bool _lastLiveState = true;

 public AttentionVisibilityPolicy(PluginConfig config, ITwitchLiveChecker twitchLiveChecker)
 {
  _config = config;
  _twitchLiveChecker = twitchLiveChecker;
 }

 public async Task<bool> ShouldShowAsync(CancellationToken cancellationToken)
 {
  if (!_config.EnableOnlyWhenTwitchStreamerLive)
  {
   return true;
  }

  var interval = Math.Max(10, _config.TwitchCheckIntervalSeconds);
  if ((DateTime.UtcNow - _lastCheckedAtUtc).TotalSeconds < interval)
  {
   return _lastLiveState;
  }

  try
  {
   _lastLiveState = await _twitchLiveChecker.IsLiveAsync(
       _config.TwitchBroadcasterId,
       _config.TwitchClientId,
       _config.TwitchAppAccessToken,
       cancellationToken).ConfigureAwait(false);
  }
  catch
  {
   _lastLiveState = _config.TwitchCheckFailOpen;
  }

  _lastCheckedAtUtc = DateTime.UtcNow;
  return _lastLiveState;
 }
}
