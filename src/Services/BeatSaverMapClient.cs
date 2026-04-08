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
  var details = await GetMapDetailsFromEndpointAsync(requestUri, cancellationToken).ConfigureAwait(false);
  return details?.Description;
 }

 public async Task<string?> GetDescriptionByHashAsync(string hash, CancellationToken cancellationToken)
 {
  var details = await GetMapDetailsByHashAsync(hash, cancellationToken).ConfigureAwait(false);
  return details?.Description;
 }

 public async Task<BeatSaverMapDetails?> GetMapDetailsByHashAsync(string hash, CancellationToken cancellationToken)
 {
  if (string.IsNullOrWhiteSpace(hash))
  {
   return null;
  }

  var requestUri = "https://api.beatsaver.com/maps/hash/" + Uri.EscapeDataString(hash.Trim());
  return await GetMapDetailsFromEndpointAsync(requestUri, cancellationToken).ConfigureAwait(false);
 }

 private async Task<BeatSaverMapDetails?> GetMapDetailsFromEndpointAsync(string requestUri, CancellationToken cancellationToken)
 {
  using var response = await _httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
  if (!response.IsSuccessStatusCode)
  {
   return null;
  }

  var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
  var parsed = JObject.Parse(json);

  var bsrId = parsed["id"]?.ToString();
  var description = parsed["description"]?.ToString();

  return new BeatSaverMapDetails
  {
   BsrId = string.IsNullOrWhiteSpace(bsrId) ? null : bsrId,
   Description = string.IsNullOrWhiteSpace(description) ? null : description,
  };
 }
}
