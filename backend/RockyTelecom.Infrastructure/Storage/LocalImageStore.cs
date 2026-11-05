using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Infrastructure.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace RockyTelecom.Infrastructure.Storage;

/// <summary>
/// Local disk image store (D-14). Every upload is re-encoded to WebP at three widths so the
/// storefront only ever serves optimised files (Lighthouse budget, DESIGN §7).
/// </summary>
internal sealed class LocalImageStore : IImageStore
{
    private static readonly string[] Sizes = { "thumb", "medium", "large" };

    private readonly StorageOptions _options;
    private readonly ILogger<LocalImageStore> _logger;
    private readonly string _root;
    private readonly string _publicBase;

    public LocalImageStore(
        IOptions<StorageOptions> options,
        IHostEnvironment environment,
        ILogger<LocalImageStore> logger)
    {
        _options = options.Value;
        _logger = logger;
        _root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, _options.RootPath));
        _publicBase = "/" + _options.PublicBasePath.Trim('/', '\\');
    }

    public async Task<StoredImage> SaveAsync(Stream content, string fileName, CancellationToken cancellationToken = default)
    {
        var safeBase = SanitiseBaseName(fileName);
        var folder = Path.Combine(DateTime.UtcNow.ToString("yyyy"), DateTime.UtcNow.ToString("MM"));

        Directory.CreateDirectory(Path.Combine(_root, folder));

        Image image;
        try
        {
            image = await Image.LoadAsync(content, cancellationToken);
        }
        catch (SixLabors.ImageSharp.ImageFormatException)
        {
            // A damaged or fake image is the uploader's problem, not a server fault.
            throw new RockyTelecom.Domain.Common.DomainValidationException(
                "That file is not a readable image. Export it again as JPEG, PNG or WebP.");
        }

        using var disposable = image;

        var stored = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var size in Sizes)
        {
            var width = size switch
            {
                "thumb" => _options.ThumbWidth,
                "medium" => _options.MediumWidth,
                _ => _options.LargeWidth
            };

            // Never upscale: a 600px source must not become a 1600px file.
            var target = Math.Min(width, image.Width);

            using var copy = image.Clone(clone => clone.Resize(target, 0));

            var relative = Path.Combine(folder, $"{safeBase}-{size}.webp").Replace('\\', '/');
            var absolute = Path.Combine(_root, relative);

            await copy.SaveAsync(absolute, new WebpEncoder { Quality = 82 }, cancellationToken);
            stored[size] = relative;
        }

        _logger.LogInformation("Stored image {FileName} as {Thumb}", fileName, stored["thumb"]);

        return new StoredImage(stored["large"], stored["thumb"], stored["medium"], stored["large"]);
    }

    public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var absolute = ResolveWithinRoot(relativePath);
        if (absolute is null)
        {
            _logger.LogWarning("Refused to delete an out-of-root path {Path}", relativePath);
            return Task.CompletedTask;
        }

        if (File.Exists(absolute))
        {
            File.Delete(absolute);
        }

        return Task.CompletedTask;
    }

    public string GetPublicUrl(string relativePath) =>
        string.IsNullOrWhiteSpace(relativePath)
            ? string.Empty
            : $"{_publicBase}/{relativePath.TrimStart('/').Replace('\\', '/')}";

    /// <summary>Resolves a stored path and refuses anything that escapes the upload root.</summary>
    private string? ResolveWithinRoot(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        var combined = Path.GetFullPath(Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        return combined.StartsWith(_root, StringComparison.OrdinalIgnoreCase) ? combined : null;
    }

    private static string SanitiseBaseName(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var cleaned = new string(stem
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray())
            .Trim('-');

        while (cleaned.Contains("--", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("--", "-", StringComparison.Ordinal);
        }

        if (string.IsNullOrEmpty(cleaned))
        {
            cleaned = "image";
        }

        return $"{cleaned}-{Guid.NewGuid():N}"[..Math.Min(cleaned.Length + 9, 60)];
    }
}
