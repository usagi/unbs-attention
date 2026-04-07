using Newtonsoft.Json.Linq;

namespace UnbsAttention.Services;

public sealed class BeatSaverMapClient : IBeatSaverMapClient
{
 private readonly HttpClient _httpClient;

 public BeatSaverMapClient(HttpClient httpClient)
 {
  _httpClient = httpClient;
 }

 public async Task<string?> GetDescriptionByBsrIdAsync(string bsrId, CancellationToken cancellationToken)
 {
  if (string.IsNullOrWhiteSpace(bsrId))
  {
   return null;
  }

  var requestUri = "https://api.beatsaver.com/maps/id/" + Uri.EscapeDataString(bsrId.Trim());
  return await GetDescriptionFromEndpointAsync(requestUri, cancellationToken).ConfigureAwait(false);
 }

 public async Task<string?> GetDescriptionByHashAsync(string hash, CancellationToken cancellationToken)
 {
  if (string.IsNullOrWhiteSpace(hash))
  {
   return null;
  }

  var requestUri = "https://api.beatsaver.com/maps/hash/" + Uri.EscapeDataString(hash.Trim());
  return await GetDescriptionFromEndpointAsync(requestUri, cancellationToken).ConfigureAwait(false);
 }

 private async Task<string?> GetDescriptionFromEndpointAsync(string requestUri, CancellationToken cancellationToken)
 {
  using var response = await _httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
  if (!response.IsSuccessStatusCode)
  {
   return null;
  }

  var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
  var parsed = JObject.Parse(json);
  var description = parsed["description"]?.ToString();
  return string.IsNullOrWhiteSpace(description) ? null : description;
 }
}
