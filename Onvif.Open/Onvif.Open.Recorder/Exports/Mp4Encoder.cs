// Sarthak Killedar
// 2026-11-09

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Onvif.Open.Recorder.Exports
{
    /// <summary>What a watermark says: header lines, and a running clock in the site's time.</summary>
    /// <param name="Lines">Fixed text, one line each (organization, camera, who exported it, ...).</param>
    /// <param name="Start">The wall-clock time of the first frame.</param>
    /// <param name="Offset">The site's offset from UTC at <paramref name="Start"/>.</param>
    /// <param name="Zone">The time zone's short name shown after the clock, e.g. "CET".</param>
    public sealed record Watermark(IReadOnlyList<string> Lines, DateTimeOffset Start, TimeSpan Offset, string Zone);

    /// <summary>Turns a recorded MPEG-TS clip into a watermarked MP4.</summary>
    public interface IMp4Encoder
    {
        /// <summary>Throws <see cref="InvalidOperationException"/> with the reason when it fails.</summary>
        Task EncodeAsync(string input, string output, Watermark watermark, CancellationToken cancellationToken);
    }

    /// <summary>
    /// <see cref="IMp4Encoder"/> with ffmpeg ("Export:Ffmpeg", default "ffmpeg" on the path): H.264 with the watermark
    /// drawn into every frame (top left: the lines, then the site time of each frame), AAC audio, moov atom first so it
    /// plays while downloading.
    /// </summary>
    public sealed class FfmpegMp4Encoder : IMp4Encoder
    {
        /// <param name="fontFile">The watermark's font ("Export:FontFile"); null: a common one found on this machine, else fontconfig's default.</param>
        public FfmpegMp4Encoder(string ffmpeg = null, string fontFile = null)
        {
            _ffmpeg = string.IsNullOrWhiteSpace(ffmpeg) ? "ffmpeg" : ffmpeg;
            _fontFile = string.IsNullOrWhiteSpace(fontFile) ? FindFont() : fontFile;
        }

        /// <summary>Fonts tried when none is configured (ffmpeg builds without fontconfig need one).</summary>
        public static readonly string[] CommonFonts =
        {
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/TTF/DejaVuSans.ttf",
            "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
            "/usr/share/fonts/truetype/freefont/FreeSans.ttf",
            "/System/Library/Fonts/Supplemental/Arial.ttf",
            "/Library/Fonts/Arial.ttf",
            @"C:\Windows\Fonts\arial.ttf"
        };

        private readonly string _ffmpeg;
        private readonly string _fontFile;

        private static string FindFont() => CommonFonts.FirstOrDefault(File.Exists);

        public async Task EncodeAsync(string input, string output, Watermark watermark, CancellationToken cancellationToken)
        {
            // The text goes in a file: no escaping of the filter arguments, and drawtext expands the clock in it.
            var textFile = output + ".txt";
            await File.WriteAllTextAsync(textFile, Text(watermark), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);

            // The font is copied next to the text: its path then needs no escaping in the filter either.
            string font = null;
            if (_fontFile != null && File.Exists(_fontFile))
            {
                font = output + ".font" + Path.GetExtension(_fontFile);
                File.Copy(_fontFile, font, overwrite: true);
            }

            // Run in the output's folder and name the text file without a path: nothing to escape in the filter.
            var start = new ProcessStartInfo(_ffmpeg)
            {
                WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(output)),
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };

            foreach (var argument in new[]
            {
                "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
                "-i", input,
                "-vf", $"setpts=PTS-STARTPTS,drawtext={(font == null ? string.Empty : $"fontfile={Path.GetFileName(font)}:")}textfile={Path.GetFileName(textFile)}:expansion=normal:x=12:y=12:fontsize=h/32:fontcolor=white:box=1:boxcolor=black@0.55:boxborderw=6:line_spacing=4",
                "-c:v", "libx264", "-preset", "veryfast", "-crf", "23", "-pix_fmt", "yuv420p",
                "-c:a", "aac", "-b:a", "96k",
                "-movflags", "+faststart",
                "-f", "mp4", output
            })
            {
                start.ArgumentList.Add(argument);
            }

            try
            {
                using (var process = Process.Start(start) ?? throw new InvalidOperationException("ffmpeg did not start."))
                {
                    var errors = process.StandardError.ReadToEndAsync(cancellationToken);
                    _ = process.StandardOutput.ReadToEndAsync(cancellationToken);
                    try
                    {
                        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        process.Kill(entireProcessTree: true);
                        throw;
                    }

                    if (process.ExitCode != 0)
                    {
                        var message = (await errors.ConfigureAwait(false)).Trim();
                        throw new InvalidOperationException("The video could not be encoded: " + (message.Length > 500 ? message.Substring(message.Length - 500) : message));
                    }
                }
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                throw new InvalidOperationException($"ffmpeg is needed for MP4 exports and was not found ({_ffmpeg}): {ex.Message}", ex);
            }
            finally
            {
                File.Delete(textFile);
                if (font != null)
                {
                    File.Delete(font);
                }
            }
        }

        /// <summary>The watermark's text; "%{pts:gmtime:N}" is the frame's time, shifted to the site's offset.</summary>
        public static string Text(Watermark watermark)
        {
            var epoch = (watermark.Start + watermark.Offset).ToUnixTimeSeconds();
            var lines = watermark.Lines.Select(Clean).Where(l => l.Length > 0).ToList();
            lines.Add($"%{{pts:gmtime:{epoch}}} {Clean(watermark.Zone)}".TrimEnd());
            return string.Join("\n", lines);
        }

        /// <summary>No line breaks or drawtext expansions from names people typed ("%" becomes a look-alike).</summary>
        private static string Clean(string text) =>
            new string((text ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()).Replace('%', '\uFF05').Trim();
    }
}
