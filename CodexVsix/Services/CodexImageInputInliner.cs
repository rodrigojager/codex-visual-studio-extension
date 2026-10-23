using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using Newtonsoft.Json.Linq;

namespace CodexVsix.Services;

// Adapted from Billy Martin's PR #26. Only structured attachments are read;
// text mentioning a filename is never treated as permission to upload that file.
internal static class CodexImageInputInliner
{
    internal const int MaxImageBytes = 20 * 1024 * 1024;
    internal const int MaxTurnImageBytes = 40 * 1024 * 1024;
    internal const long MaxPixels = 25_000_000;

    internal static byte[] ReadBoundedFile(string path, int limit, bool allowEmpty = false)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if ((!allowEmpty && stream.Length == 0) || stream.Length > limit)
            throw new InvalidDataException("The attachment is empty or too large. Choose a smaller file.");
        var bytes = new byte[(int)stream.Length];
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = stream.Read(bytes, offset, bytes.Length - offset);
            if (read == 0) throw new InvalidDataException("The attachment changed while it was being read. Attach it again.");
            offset += read;
        }
        return bytes;
    }

    public static JObject CreateInlineImage(string path)
        => CreateImage(ReadBoundedFile(Path.GetFullPath(path), MaxImageBytes));

    public static JToken? InlineImages(string method, JToken? parameters, string workingDirectory)
    {
        if (method != "turn/start" && method != "turn/steer") return parameters;
        if (parameters is not JObject source || source["input"] is not JArray sourceInput) return parameters;
        var result = (JObject)source.DeepClone();
        var inputs = new JArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        foreach (var input in sourceInput)
        {
            if (input is not JObject item) { inputs.Add(input.DeepClone()); continue; }
            var type = item["type"]?.Value<string>();
            if (type != "localImage" && type != "image") { inputs.Add(item.DeepClone()); continue; }
            var reference = (type == "localImage" ? item["path"] : item["url"])?.Value<string>();
            if (string.IsNullOrWhiteSpace(reference)) throw new InvalidDataException("The attached image has no readable location.");
            byte[] bytes;
            if (reference!.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var comma = reference.IndexOf(',');
                if (comma < 0 || !reference.Substring(0, comma).EndsWith(";base64", StringComparison.OrdinalIgnoreCase)
                    || reference.Length - comma - 1 > ((MaxImageBytes + 2L) / 3) * 4)
                    throw new InvalidDataException("The attached image is invalid or exceeds 20 MB.");
                try { bytes = Convert.FromBase64String(reference.Substring(comma + 1)); }
                catch (FormatException ex) { throw new InvalidDataException("The attached image could not be decoded.", ex); }
            }
            else if (type == "image" && Uri.TryCreate(reference, UriKind.Absolute, out var remote)
                && (remote.Scheme == "https" || remote.Scheme == "http"))
            {
                inputs.Add(item.DeepClone()); // Remote URLs remain the runtime's responsibility.
                continue;
            }
            else bytes = ReadBoundedFile(ResolvePath(reference, workingDirectory), MaxImageBytes);

            using var hash = SHA256.Create();
            var key = Convert.ToBase64String(hash.ComputeHash(bytes)) + ":" + item["detail"]?.ToString();
            if (!seen.Add(key)) continue;
            total += bytes.Length;
            if (total > MaxTurnImageBytes) throw new InvalidDataException("The attached images exceed 40 MB in total. Send fewer or smaller images.");
            var image = CreateImage(bytes);
            if (item["detail"] is JToken detail) image["detail"] = detail.DeepClone();
            inputs.Add(image);
        }
        result["input"] = inputs;
        return result;
    }

    private static JObject CreateImage(byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length > MaxImageBytes)
            throw new InvalidDataException("The attached image is empty or exceeds 20 MB.");
        var mime = DetectMimeType(bytes);
        if (mime == "image/webp") ValidateWebP(bytes);
        else
        {
            try
            {
                using var stream = new MemoryStream(bytes, writable: false);
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
                var frame = decoder.Frames[0];
                ValidateDimensions(frame.PixelWidth, frame.PixelHeight);
                // Decode in bounded strips, rather than allocating a full uncompressed image.
                var stride = checked((frame.PixelWidth * frame.Format.BitsPerPixel + 7) / 8);
                var rows = Math.Min(frame.PixelHeight, Math.Max(1, 1024 * 1024 / Math.Max(1, stride)));
                var buffer = new byte[checked(rows * stride)];
                for (var y = 0; y < frame.PixelHeight; y += rows)
                    frame.CopyPixels(new Int32Rect(0, y, frame.PixelWidth, Math.Min(rows, frame.PixelHeight - y)), buffer, stride, 0);
                if (mime == "image/bmp")
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(frame);
                    using var png = new MemoryStream();
                    encoder.Save(png);
                    if (png.Length > MaxImageBytes) throw new InvalidDataException("The converted image exceeds 20 MB.");
                    bytes = png.ToArray();
                    mime = "image/png";
                }
            }
            catch (Exception ex) when (ex is NotSupportedException || ex is ArgumentException
                || ex is System.Runtime.InteropServices.COMException || ex is FileFormatException)
            {
                throw new InvalidDataException("The attached image is damaged or unsupported. Save it as PNG or JPEG and attach it again.", ex);
            }
        }
        return new JObject { ["type"] = "image", ["url"] = "data:" + mime + ";base64," + Convert.ToBase64String(bytes) };
    }

    private static string DetectMimeType(byte[] b)
    {
        if (b.Length >= 8 && b[0] == 137 && Encoding.ASCII.GetString(b, 1, 3) == "PNG" && b[4] == 13 && b[5] == 10 && b[6] == 26 && b[7] == 10) return "image/png";
        if (b.Length >= 3 && b[0] == 255 && b[1] == 216 && b[2] == 255) return "image/jpeg";
        if (b.Length >= 6 && (Encoding.ASCII.GetString(b, 0, 6) == "GIF87a" || Encoding.ASCII.GetString(b, 0, 6) == "GIF89a")) return "image/gif";
        if (b.Length >= 2 && b[0] == 66 && b[1] == 77) return "image/bmp";
        if (b.Length >= 12 && Encoding.ASCII.GetString(b, 0, 4) == "RIFF" && Encoding.ASCII.GetString(b, 8, 4) == "WEBP") return "image/webp";
        throw new InvalidDataException("The attached file is not a supported image. Choose PNG, JPEG, GIF, WebP or BMP.");
    }

    private static void ValidateDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0 || (long)width * height > MaxPixels)
            throw new InvalidDataException("The image dimensions are invalid or exceed 25 megapixels. Resize it before attaching.");
    }

    // WebP is decoded by the renderer/runtime. WIC has no guaranteed WebP codec on
    // supported Windows installations; validate its container and canvas bounds here.
    private static void ValidateWebP(byte[] bytes)
    {
        if ((long)BitConverter.ToUInt32(bytes, 4) + 8 != bytes.Length)
            throw new InvalidDataException("The WebP attachment is truncated.");
        var foundImage = false;
        for (long offset = 12; offset < bytes.Length;)
        {
            if (offset + 8 > bytes.Length) throw new InvalidDataException("Invalid WebP chunk.");
            var i = (int)offset;
            var size = BitConverter.ToUInt32(bytes, i + 4);
            var next = offset + 8 + size + (size % 2);
            if (next > bytes.Length) throw new InvalidDataException("Truncated WebP chunk.");
            var kind = Encoding.ASCII.GetString(bytes, i, 4);
            if (kind == "VP8X" && size >= 10)
                ValidateDimensions(1 + ReadUInt24(bytes, i + 12), 1 + ReadUInt24(bytes, i + 15));
            else if (kind == "VP8L" && size >= 5 && bytes[i + 8] == 0x2f)
            {
                var bits = BitConverter.ToUInt32(bytes, i + 9);
                ValidateDimensions((int)(bits & 0x3fff) + 1, (int)((bits >> 14) & 0x3fff) + 1);
                foundImage = true;
            }
            else if (kind == "VP8 " && size >= 10 && bytes[i + 11] == 0x9d && bytes[i + 12] == 1 && bytes[i + 13] == 0x2a)
            {
                ValidateDimensions(BitConverter.ToUInt16(bytes, i + 14) & 0x3fff, BitConverter.ToUInt16(bytes, i + 16) & 0x3fff);
                foundImage = true;
            }
            else if (kind == "ANMF") throw new InvalidDataException("Animated WebP is not supported. Save a still frame as PNG.");
            offset = next;
        }
        if (!foundImage) throw new InvalidDataException("The WebP attachment contains no readable image.");
    }
    private static int ReadUInt24(byte[] b, int i) => b[i] | b[i + 1] << 8 | b[i + 2] << 16;
    private static string ResolvePath(string reference, string directory)
    {
        var path = reference.Trim();
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile) path = uri.LocalPath;
        if (path.Length >= 3 && (path[0] == '/' || path[0] == '\\') && char.IsLetter(path[1]) && path[2] == ':') path = path.Substring(1);
        path = path.Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(directory, path));
    }
}
