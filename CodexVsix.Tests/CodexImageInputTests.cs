using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexVsix.Models;
using CodexVsix.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CodexVsix.Tests;

public sealed class CodexImageInputTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "CodexImageTest-" + Guid.NewGuid());
    public CodexImageInputTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);

    private string Image(string name = "imagem ação 中文.png", BitmapEncoder? encoder = null)
    {
        var frame = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { 0, 0, 255, 255, 0, 255, 0, 255, 255, 0, 0, 255, 255, 255, 255, 255 }, 8);
        encoder ??= new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(frame));
        var path = Path.Combine(_directory, name);
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    [Fact]
    public void PickerBinaryContractPreservesRealBytesAndUnicode()
    {
        var path = Image();
        var picked = CodexOfficialWebViewBridge.ToPickedFile(path);
        var reply = CodexOfficialWebViewBridge.ReadBinaryFile(new JObject { ["path"] = picked["fsPath"] });
        Assert.Equal(File.ReadAllBytes(path), Convert.FromBase64String(reply["contentsBase64"]!.Value<string>()!));
        Assert.Equal(reply["data"], reply["contentsBase64"]);
    }

    [Theory]
    [InlineData("turn/start")]
    [InlineData("turn/steer")]
    public void NormalizesAndDeduplicatesStructuredImagesWithoutChangingTextOrInput(string method)
    {
        var path = Image();
        var inline = CodexImageInputInliner.CreateInlineImage(path);
        var text = "# Files mentioned by the user:\n\n## photo.png: " + path + "\n\n## My request for Codex:\nlook";
        var request = new JObject { ["threadId"] = "t1", ["input"] = new JArray(
            new JObject { ["type"] = "text", ["text"] = text },
            new JObject { ["type"] = "localImage", ["path"] = "/" + path.Replace('\\', '/') }, inline) };
        var before = request.ToString();
        var result = (JObject)CodexImageInputInliner.InlineImages(method, request, _directory)!;
        Assert.Equal(before, request.ToString());
        var inputs = (JArray)result["input"]!;
        Assert.Equal(2, inputs.Count);
        Assert.Equal(text, inputs[0]!["text"]!.Value<string>());
        Assert.Equal(inline["url"], inputs[1]!["url"]);
    }

    [Fact]
    public void PlainTextMentionDoesNotUploadAFile()
    {
        var text = "# Files mentioned by the user:\n\n## photo.png: " + Image();
        var request = new JObject { ["input"] = new JArray(new JObject { ["type"] = "text", ["text"] = text }) };
        var result = CodexImageInputInliner.InlineImages("turn/start", request, _directory);
        Assert.True(JToken.DeepEquals(request, result));
    }

    [Fact]
    public void LegacyImageOnlySubmissionContainsTheImageBytes()
    {
        var path = Image();
        using var service = new CodexProcessService();
        var inputs = JArray.FromObject(service.BuildUserInput("", new CodexExtensionSettings(), _directory, new[] { path }, ""));
        var image = Assert.Single(inputs.OfType<JObject>(), item => item["type"]?.Value<string>() == "image");
        Assert.Equal(CodexImageInputInliner.CreateInlineImage(path)["url"], image["url"]);
    }

    [Fact]
    public void DetectsActualFormatAndConvertsBmpForTransport()
    {
        Assert.StartsWith("data:image/jpeg;", CodexImageInputInliner.CreateInlineImage(Image("wrong.png", new JpegBitmapEncoder()))["url"]!.Value<string>());
        Assert.StartsWith("data:image/png;", CodexImageInputInliner.CreateInlineImage(Image("photo.bmp", new BmpBitmapEncoder()))["url"]!.Value<string>());
        Assert.StartsWith("data:image/gif;", CodexImageInputInliner.CreateInlineImage(Image("photo.gif", new GifBitmapEncoder()))["url"]!.Value<string>());
    }

    [Fact]
    public void RejectsCorruptEmptyMissingAndOversizedFiles()
    {
        var path = Path.Combine(_directory, "invalid.png");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        Assert.Throws<InvalidDataException>(() => CodexImageInputInliner.CreateInlineImage(path));
        File.WriteAllBytes(path, Array.Empty<byte>());
        Assert.Throws<InvalidDataException>(() => CodexImageInputInliner.CreateInlineImage(path));
        using (var stream = File.Create(path)) stream.SetLength(CodexImageInputInliner.MaxImageBytes + 1L);
        Assert.Throws<InvalidDataException>(() => CodexImageInputInliner.CreateInlineImage(path));
        File.Delete(path);
        Assert.Throws<FileNotFoundException>(() => CodexImageInputInliner.CreateInlineImage(path));
    }

    [Theory]
    [InlineData("data:image/png;base64,%%invalid%%")]
    [InlineData("data:image/png,raw")]
    [InlineData("data:image/png;base64,AQID")]
    public void RejectsInvalidInlineData(string url)
    {
        var request = new JObject { ["input"] = new JArray(new JObject { ["type"] = "image", ["url"] = url }) };
        Assert.Throws<InvalidDataException>(() => CodexImageInputInliner.InlineImages("turn/start", request, _directory));
    }
}
