using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnbsAttention.Models;

namespace UnbsAttention.Services;

public sealed class DiscordChannelAttachmentSyncProvider : IAttentionSyncProvider
{
 private static readonly Regex FileNamePattern = new(
     @"unbs-attention-[^\""""\\/]+\.json",
     RegexOptions.IgnoreCase | RegexOptions.Compiled);

 private readonly HttpClient _httpClient;
 private readonly string _channelUrl;

 public DiscordChannelAttachmentSyncProvider(HttpClient httpClient, string channelUrl)
 {
  _httpClient = httpClient;
  _channelUrl = channelUrl;
 }

 public string Name => "discord-channel-fallback";

 public async Task<AttentionDatabase?> PullLatestAsync(CancellationToken cancellationToken)
 {
  if (string.IsNullOrWhiteSpace(_channelUrl))
  {
   return null;
  }

  var html = await _httpClient.GetStringAsync(_channelUrl).ConfigureAwait(false);

  var fileNameMatch = FileNamePattern.Match(html);
  if (!fileNameMatch.Success)
  {
   return null;
  }

  // Discord の HTML 直解析は不安定なので、ここは意図的にフォールバック試作止まり。
  return null;
 }

 public Task PushAsync(AttentionDatabase database, CancellationToken cancellationToken)
 {
  var _ = JsonConvert.SerializeObject(database);
  return Task.CompletedTask;
 }
}
