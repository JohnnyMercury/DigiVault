namespace DigiVault.Web.Middleware;

public class MinioProxyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<MinioProxyMiddleware> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _minioEndpoint;

    public MinioProxyMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        ILogger<MinioProxyMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        _httpClient = new HttpClient();

        var endpoint = configuration["MinIO:Endpoint"] ?? "localhost:9000";
        var useSSL = bool.Parse(configuration["MinIO:UseSSL"] ?? "false");
        _minioEndpoint = $"{(useSSL ? "https" : "http")}://{endpoint}";
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/minio"))
        {
            await ProxyToMinioAsync(context);
        }
        else
        {
            await _next(context);
        }
    }

    private async Task ProxyToMinioAsync(HttpContext context)
    {
        try
        {
            var path = context.Request.Path.Value![6..]; // Remove "/minio"
            var minioUrl = $"{_minioEndpoint}{path}";

            var response = await _httpClient.GetAsync(minioUrl);

            if (response.IsSuccessStatusCode)
            {
                context.Response.StatusCode = (int)response.StatusCode;
                context.Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
                context.Response.Headers.Append("Cache-Control", "public, max-age=31536000");

                if (response.Headers.ETag != null)
                    context.Response.Headers.Append("ETag", response.Headers.ETag.Tag);

                await response.Content.CopyToAsync(context.Response.Body);
            }
            else
            {
                context.Response.StatusCode = 404;
                await context.Response.WriteAsync("Image not found");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error proxying MinIO request: {Path}", context.Request.Path);
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync("Internal server error");
        }
    }
}

public static class MinioProxyMiddlewareExtensions
{
    public static IApplicationBuilder UseMinioProxy(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<MinioProxyMiddleware>();
    }
}
