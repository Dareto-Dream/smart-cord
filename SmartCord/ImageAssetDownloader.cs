using System.IO.Compression;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SkiaSharp;
using Svg.Skia;

namespace SmartCord;

public sealed record DownloadResult(int DownloadedCount);

public static class ImageAssetDownloader
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<DownloadResult> DownloadAsync(
        SmartCordSettings settings,
        string baseDirectory,
        CancellationToken cancellationToken)
    {
        var iconsPath = Path.Combine(baseDirectory, "icons");
        var projectsPath = Path.Combine(iconsPath, "projects");
        Directory.CreateDirectory(iconsPath);
        Directory.CreateDirectory(projectsPath);

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SmartCord/1.0");

        var count = 0;
        count += await DownloadVsCodeIconAsync(httpClient, iconsPath, cancellationToken);

        var rustSvgPath = Path.Combine(iconsPath, "rust.svg");
        await DownloadFileAsync(
            httpClient,
            "https://upload.wikimedia.org/wikipedia/commons/d/d5/Rust_programming_language_black_logo.svg",
            rustSvgPath,
            cancellationToken);
        ConvertSvgToPng(rustSvgPath, Path.Combine(iconsPath, "rust.png"));
        File.Delete(rustSvgPath);
        count++;

        count += await DownloadFileAsync(
            httpClient,
            "https://github.githubassets.com/images/modules/logos_page/GitHub-Mark.png",
            Path.Combine(iconsPath, "github.png"),
            cancellationToken);

        var terminalSvgPath = Path.Combine(iconsPath, "terminal.svg");
        await File.WriteAllTextAsync(
            terminalSvgPath,
            TerminalSvg,
            Encoding.UTF8,
            cancellationToken);
        ConvertSvgToPng(terminalSvgPath, Path.Combine(iconsPath, "terminal.png"));
        File.Delete(terminalSvgPath);
        count++;

        await WriteAssetNotesAsync(iconsPath, cancellationToken);
        await WriteAssetKeysAsync(iconsPath, cancellationToken);
        count += await DownloadProjectThumbnailsAsync(httpClient, settings, projectsPath, cancellationToken);

        return new DownloadResult(count);
    }

    public static string NormalizeAssetKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "asset";
        }

        var builder = new StringBuilder();
        var lastWasDash = false;

        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                lastWasDash = false;
            }
            else if (!lastWasDash)
            {
                builder.Append('-');
                lastWasDash = true;
            }
        }

        var key = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(key) ? "asset" : key;
    }

    private static async Task<int> DownloadVsCodeIconAsync(
        HttpClient httpClient,
        string iconsPath,
        CancellationToken cancellationToken)
    {
        var zipPath = Path.Combine(iconsPath, "vscode-icons.zip");
        await DownloadFileAsync(
            httpClient,
            "https://code.visualstudio.com/assets/branding/visual-studio-code-icons.zip",
            zipPath,
            cancellationToken);

        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.Entries.FirstOrDefault(entry =>
            entry.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) &&
            entry.FullName.Contains("stable", StringComparison.OrdinalIgnoreCase));
        entry ??= archive.Entries.FirstOrDefault(entry => entry.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            return 1;
        }

        var destination = Path.Combine(iconsPath, "vscode.png");
        entry.ExtractToFile(destination, overwrite: true);
        return 2;
    }

    private static async Task<int> DownloadProjectThumbnailsAsync(
        HttpClient httpClient,
        SmartCordSettings settings,
        string projectsPath,
        CancellationToken cancellationToken)
    {
        List<ProjectItem> projects;
        try
        {
            await using var stream = await httpClient.GetStreamAsync(settings.Api.ProjectsUrl, cancellationToken);
            projects = await ProjectJson.ReadProjectsAsync(stream, SerializerOptions, cancellationToken);
        }
        catch
        {
            return 0;
        }

        var count = 0;
        foreach (var project in projects)
        {
            if (!Uri.TryCreate(project.ThumbnailUrl, UriKind.Absolute, out var thumbnailUri))
            {
                continue;
            }

            var extension = Path.GetExtension(thumbnailUri.AbsolutePath);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".png";
            }

            var fileName = $"project-{NormalizeAssetKey(project.Title)}{extension.ToLowerInvariant()}";
            try
            {
                using var perFileTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                perFileTimeout.CancelAfter(TimeSpan.FromSeconds(15));
                count += await DownloadFileAsync(httpClient, thumbnailUri.ToString(), Path.Combine(projectsPath, fileName), perFileTimeout.Token);
            }
            catch
            {
                // Some project thumbnails are optional; one slow or missing image should not block the usable asset set.
            }
        }

        return count;
    }

    private static async Task<int> DownloadFileAsync(
        HttpClient httpClient,
        string url,
        string destination,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = File.Create(destination);
        await source.CopyToAsync(target, cancellationToken);
        return 1;
    }

    private static async Task WriteAssetNotesAsync(string iconsPath, CancellationToken cancellationToken)
    {
        var notes = """
        SmartCord Discord asset notes
        ============================

        Upload these files manually to the Discord Developer Portal Rich Presence assets page.
        Use lowercase asset keys matching the filenames without extensions.

        Core keys:
        - vscode
        - rust
        - terminal
        - github

        Project thumbnails live in icons/projects and should use keys like project-spectralis.

        Source references:
        - VS Code icon guidelines: https://code.visualstudio.com/brand
        - Rust logo source: https://commons.wikimedia.org/wiki/File:Rust_Logo.svg
        - GitHub logo guidelines: https://brand.github.com/foundations/logo
        - Discord branding: https://discord.com/branding
        - Discord Rich Presence docs: https://docs.discord.com/developers/platform/rich-presence
        """;

        await File.WriteAllTextAsync(Path.Combine(iconsPath, "README.txt"), notes, Encoding.UTF8, cancellationToken);
    }

    private static async Task WriteAssetKeysAsync(string iconsPath, CancellationToken cancellationToken)
    {
        IEnumerable<string> projectKeys = Directory.Exists(Path.Combine(iconsPath, "projects"))
            ? Directory.GetFiles(Path.Combine(iconsPath, "projects"))
                .Select(path => Path.GetFileNameWithoutExtension(path))
                .Order(StringComparer.OrdinalIgnoreCase)
            : Enumerable.Empty<string>();

        var keys = new List<string>
        {
            "vscode",
            "rust",
            "terminal",
            "github"
        };
        keys.AddRange(projectKeys);

        await File.WriteAllLinesAsync(Path.Combine(iconsPath, "asset-keys.txt"), keys.Distinct(), cancellationToken);
    }

    private static void ConvertSvgToPng(string svgPath, string pngPath, int size = 512)
    {
        var svg = new SKSvg();
        var picture = svg.Load(svgPath) ?? throw new InvalidOperationException($"Could not load SVG: {svgPath}");
        var bounds = picture.CullRect;

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new InvalidOperationException($"SVG has invalid bounds: {svgPath}");
        }

        using var bitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        var scale = Math.Min(size / bounds.Width, size / bounds.Height) * 0.9f;
        var x = (size - bounds.Width * scale) / 2f - bounds.Left * scale;
        var y = (size - bounds.Height * scale) / 2f - bounds.Top * scale;

        canvas.Translate(x, y);
        canvas.Scale(scale);
        canvas.DrawPicture(picture);
        canvas.Flush();

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var output = File.Create(pngPath);
        data.SaveTo(output);
    }

    private const string TerminalSvg = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512">
          <rect width="512" height="512" rx="96" fill="#252526"/>
          <path d="M112 164l112 92-112 92" fill="none" stroke="#f3f3f3" stroke-width="38" stroke-linecap="round" stroke-linejoin="round"/>
          <path d="M248 352h152" fill="none" stroke="#00a2ed" stroke-width="38" stroke-linecap="round"/>
        </svg>
        """;
}
