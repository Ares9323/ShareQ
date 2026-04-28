using System.Net.Http;
using System.Net.Http.Headers;
using ShareQ.PluginContracts;

namespace ShareQ.Uploaders.Litterbox;

/// <summary>
/// Temporary uploads to https://litterbox.catbox.moe — free, no API key, files auto-expire after
/// the configured duration (1h, 12h, 24h, 72h). Same API shape as Catbox but with a "time" field.
/// </summary>
public sealed class LitterboxUploader : IUploader
{
    public const string UploaderId = "litterbox";
    private const string Endpoint = "https://litterbox.catbox.moe/resources/internals/api.php";

    private readonly IHttpClientFactory _httpFactory;

    public LitterboxUploader(IHttpClientFactory httpFactory)
    {
        _httpFactory = httpFactory;
    }

    public string Id => UploaderId;
    public string DisplayName => "Litterbox (temporary)";

    public async Task<UploadResult> UploadAsync(UploadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Bytes.Length == 0) return UploadResult.Failure("empty payload");

        using var http = _httpFactory.CreateClient(nameof(LitterboxUploader));
        http.DefaultRequestVersion = System.Net.HttpVersion.Version11;
        http.DefaultVersionPolicy = System.Net.Http.HttpVersionPolicy.RequestVersionExact;
        if (http.DefaultRequestHeaders.UserAgent.Count == 0)
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ShareQ/1.0");

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("fileupload"), "reqtype");
        form.Add(new StringContent("24h"), "time");

        var fileContent = new ByteArrayContent(request.Bytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(request.ContentType);
        form.Add(fileContent, "fileToUpload", request.FileName);

        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = form,
            Version = System.Net.HttpVersion.Version11,
            VersionPolicy = System.Net.Http.HttpVersionPolicy.RequestVersionExact,
        };

        try
        {
            using var response = await http.SendAsync(message, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return UploadResult.Failure($"HTTP {(int)response.StatusCode}: {body.Trim()}");
            var url = body.Trim();
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return UploadResult.Failure(url);
            return UploadResult.Success(url);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return UploadResult.Failure(ex.Message);
        }
    }
}
