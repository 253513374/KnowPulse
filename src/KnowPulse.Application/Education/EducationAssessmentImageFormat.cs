using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Content;

namespace KnowPulse.Education;

internal static class EducationAssessmentImageFormat
{
    internal static async Task<byte[]> ReadAsync(IRemoteStreamContent file, long maximum)
    {
        if (file == null || file.ContentLength is < 1 || file.ContentLength > maximum) throw Invalid();
        using var input = file.GetStream();
        var bytes = await ReadStreamAsync(input, maximum);
        if (file.ContentLength.HasValue && bytes.LongLength != file.ContentLength.Value) throw Invalid();
        return bytes;
    }

    internal static async Task<byte[]> ReadStreamAsync(Stream input, long maximum)
    {
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await input.ReadAsync(buffer)) > 0)
        {
            if (output.Length + count > maximum) throw Invalid();
            await output.WriteAsync(buffer.AsMemory(0, count));
        }
        if (output.Length == 0) throw Invalid();
        return output.ToArray();
    }

    internal static string Detect(byte[] bytes, string? claimedType)
    {
        string? type = null;
        if (bytes.Length >= 45 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) &&
            BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8, 4)) == 13 && bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8) &&
            BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4)) > 0 && BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4)) > 0 &&
            bytes.AsSpan(bytes.Length - 12, 8).SequenceEqual(new byte[] { 0, 0, 0, 0, 73, 69, 78, 68 })) type = "image/png";
        else if (bytes.Length >= 4 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff &&
                 bytes[^2] == 0xff && bytes[^1] == 0xd9) type = "image/jpeg";
        else if (bytes.Length >= 20 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8) &&
                 BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4)) == bytes.Length - 8 &&
                 (bytes.AsSpan(12, 4).SequenceEqual("VP8 "u8) || bytes.AsSpan(12, 4).SequenceEqual("VP8L"u8) || bytes.AsSpan(12, 4).SequenceEqual("VP8X"u8))) type = "image/webp";
        // This identifies the actual allowed file signatures; it is not an image codec or OCR promise.
        if (type == null || !string.Equals(type, claimedType?.Trim(), StringComparison.OrdinalIgnoreCase)) throw Invalid();
        return type;
    }

    internal static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    internal static string FileName(string? name, string type)
    {
        var safe = (name ?? string.Empty).Replace('\\', '/');
        safe = safe[(safe.LastIndexOf('/') + 1)..];
        if (string.IsNullOrWhiteSpace(safe) || safe.Length > 256 || safe.IndexOfAny(['\r', '\n', '\0']) >= 0)
            safe = type == "image/png" ? "answer.png" : type == "image/jpeg" ? "answer.jpg" : "answer.webp";
        return safe;
    }
    private static BusinessException Invalid() => new("KnowPulse:EducationAssessment:InvalidImage");
}
