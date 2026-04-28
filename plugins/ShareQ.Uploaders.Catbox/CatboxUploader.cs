using System.Net.Http;
using System.Net.Http.Headers;
using ShareQ.PluginContracts;

namespace ShareQ.Uploaders.Catbox;

/// <summary>
/// Anonymous uploads to https://catbox.moe — permanent, no API key needed.
/// API: POST multipart/form-data to https://catbox.moe/user/api.php with reqtype=fileupload
/// and fileToUpload=binary; response body is the public URL as plain text.
/// </summary>
public sealed class CatboxUploader : IUploader
{
    public const string UploaderId = "catbox";
    private const string Endpoint = "https://catbox.moe/user/api.php";

    private readonly IHttpClientFactory _httpFactory;

    public CatboxUploader(IHttpClientFactory httpFactory)
    {
        _httpFactory = httpFactory;
    }

    public string Id => UploaderId;
    public string DisplayName => "Catbox.moe";

    public async Task<UploadResult> UploadAsync(UploadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Bytes.Length == 0) return UploadResult.Failure("empty payload");

        using var http = _httpFactory.CreateClient(nameof(CatboxUploader));
        // Catbox's CDN drops HTTP/2 multipart uploads mid-stream ("response ended prematurely").
        // Pin the request to HTTP/1.1 and set a UA — same headers as ShareX uses.
        http.DefaultRequestVersion = System.Net.HttpVersion.Version11;
        http.DefaultVersionPolicy = System.Net.Http.HttpVersionPolicy.RequestVersionExact;
        if (http.DefaultRequestHeaders.UserAgent.Count == 0)
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ShareQ/1.0");

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("fileupload"), "reqtype");

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
            // Catbox returns a plain URL on success or an error string starting with no scheme.
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
