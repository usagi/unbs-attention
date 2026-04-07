using System.Net.Http.Headers;
using Newtonsoft.Json.Linq;

namespace UnbsAttention.Services;

public sealed class TwitchHelixLiveChecker : ITwitchLiveChecker
{
 private readonly HttpClient _httpClient;

 public TwitchHelixLiveChecker(HttpClient httpClient)
 {
  _httpClient = httpClient;
 }

 public async Task<bool> IsLiveAsync(
     string broadcasterId,
     string clientId,
     string appAccessToken,
     CancellationToken cancellationToken)
 {
  if (string.IsNullOrWhiteSpace(broadcasterId)
      || string.IsNullOrWhiteSpace(clientId)
      || string.IsNullOrWhiteSpace(appAccessToken))
  {
   return false;
  }

  using var request = new HttpRequestMessage(
      HttpMethod.Get,
      $"https://api.twitch.tv/helix/streams?user_id={Uri.EscapeDataString(broadcasterId)}");

  request.Headers.Add("Client-Id", clientId);
  request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", appAccessToken);

  using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
  response.EnsureSuccessStatusCode();

  var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
  var json = JObject.Parse(payload);
  var data = json["data"] as JArray;
  return data is { Count: > 0 };
 }
}
