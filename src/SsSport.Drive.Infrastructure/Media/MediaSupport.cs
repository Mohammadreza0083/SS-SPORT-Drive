using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Options;
using SsSport.Drive.Application.Abstractions.Ingest;

namespace SsSport.Drive.Infrastructure.Media;

/// <summary>The <c>Media</c> configuration section: the tools and folders of the ingest workers.</summary>
public sealed record MediaOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Media";

    /// <summary>Gets the ffmpeg executable (<c>Media__FfmpegPath</c>); the default finds it on the PATH.</summary>
    public string FfmpegPath { get; init; } = "ffmpeg";

    /// <summary>Gets the ffprobe executable (<c>Media__FfprobePath</c>); the default finds it on the PATH.</summary>
    public string FfprobePath { get; init; } = "ffprobe";

    /// <summary>Gets the folder work folders are made in (<c>Media__WorkPath</c>); empty uses the system temp folder.</summary>
    public string WorkPath { get; init; } = string.Empty;

    /// <summary>Gets how many media jobs (ffmpeg, PDF, images) run at once (<c>Media__Workers</c>), 1 or 2.</summary>
    public int Workers { get; init; } = 1;

    /// <summary>Gets the path of the person-segmentation ONNX model (<c>Media__CutoutModelPath</c>); empty turns background removal off.</summary>
    public string CutoutModelPath { get; init; } = string.Empty;
}

/// <summary><see cref="IWorkspace"/> over the file system: one unique folder per call, deleted when disposed.</summary>
/// <param name="options">The media settings.</param>
public sealed class TempWorkspace(IOptions<MediaOptions> options) : IWorkspace
{
    /// <summary>Creates a unique, empty folder.</summary>
    /// <returns>The folder.</returns>
    public IWorkFolder Create()
    {
        var root = options.Value.WorkPath.Length > 0 ? options.Value.WorkPath : Path.GetTempPath();
        var path = Path.Combine(root, "ssd-ingest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return new WorkFolder(path);
    }

    /// <summary>A folder that deletes itself.</summary>
    /// <param name="path">The folder.</param>
    private sealed class WorkFolder(string path) : IWorkFolder
    {
        /// <inheritdoc/>
        public string Path { get; } = path;

        /// <summary>Deletes the folder and everything in it; a folder that is already gone is fine.</summary>
        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
                // Already gone.
            }
        }
    }
}

/// <summary>Reads the brand images and the font that are embedded in this assembly.</summary>
internal static class MediaAssets
{
    /// <summary>The folder the font is written to once, so ImageMagick can load it by path.</summary>
    private static readonly Lazy<string> _fontPath = new(WriteFont);

    /// <summary>Gets the path of the Barlow Condensed SemiBold font file.</summary>
    public static string FontPath => _fontPath.Value;

    /// <summary>Reads an embedded file.</summary>
    /// <param name="name">The file name, e.g. <c>logo_horizontal_white.png</c>.</param>
    /// <returns>The bytes.</returns>
    public static byte[] Read(string name)
    {
        using var stream = Open(name);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>Opens an embedded file.</summary>
    /// <param name="name">The file name.</param>
    /// <returns>The stream.</returns>
    private static Stream Open(string name) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream($"SsSport.Drive.Infrastructure.Media.Assets.{name}")
        ?? throw new InvalidOperationException($"The embedded file {name} is missing.");

    /// <summary>Writes the font to the temp folder.</summary>
    /// <returns>The path.</returns>
    private static string WriteFont()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ssd-fonts");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "BarlowCondensed-SemiBold.ttf");
        var bytes = Read("BarlowCondensed-SemiBold.ttf");
        if (!File.Exists(path) || new FileInfo(path).Length != bytes.Length)
        {
            File.WriteAllBytes(path, bytes);
        }

        return path;
    }
}

/// <summary>Runs a command-line tool with an argument list (never a shell string) and a time limit.</summary>
internal static class ToolRunner
{
    /// <summary>How many lines of the tool's error output are kept.</summary>
    private const int ErrorLines = 20;

    /// <summary>Runs a tool to the end.</summary>
    /// <param name="tool">The executable.</param>
    /// <param name="arguments">The arguments, each passed whole.</param>
    /// <param name="timeout">How long the tool may run before it is killed.</param>
    /// <param name="cancellationToken">Cancels the run; the tool is killed.</param>
    /// <returns>The exit code, what it wrote to the standard output, and the last line it wrote to the error output.</returns>
    /// <exception cref="InvalidOperationException">The tool is not installed.</exception>
    /// <exception cref="TimeoutException">The tool ran longer than <paramref name="timeout"/>.</exception>
    public static async Task<(int ExitCode, string Output, string LastError)> RunAsync(
        string tool,
        IEnumerable<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(tool) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException($"{tool} could not be started.");
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new InvalidOperationException($"{tool} is not installed or not on the PATH.", exception);
        }

        using (process)
        {
            var errors = new Queue<string>();
            process.ErrorDataReceived += (_, line) =>
            {
                if (!string.IsNullOrWhiteSpace(line.Data))
                {
                    lock (errors)
                    {
                        errors.Enqueue(line.Data);
                        if (errors.Count > ErrorLines)
                        {
                            errors.Dequeue();
                        }
                    }
                }
            };
            process.BeginErrorReadLine();
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(limit.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                throw new TimeoutException($"{tool} ran for more than {timeout.TotalMinutes:0} minutes.");
            }

            var text = await output;
            lock (errors)
            {
                return (process.ExitCode, text, errors.LastOrDefault() ?? string.Empty);
            }
        }
    }
}
