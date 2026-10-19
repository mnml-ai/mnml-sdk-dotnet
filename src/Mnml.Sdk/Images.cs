using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mnml
{
    /// <summary>
    /// An image, sent in the same call that uses it, up to 15 MB: a public <c>https://</c> link,
    /// a <c>data:image/…;base64,</c> URI, base64 text, or the bytes themselves. Bytes are sent as
    /// base64; the API checks the image again, so the type the SDK reads off the first bytes is
    /// only a label.
    /// </summary>
    [JsonConverter(typeof(ImageInputConverter))]
    public sealed class ImageInput
    {
        /// <summary>The image as the API receives it: a link, a data URI or base64.</summary>
        public string Value { get; }

        private ImageInput(string value)
        {
            if (string.IsNullOrEmpty(value)) throw new ArgumentException("An image cannot be empty.", nameof(value));
            Value = value;
        }

        /// <summary>A public https:// link, a data URI or base64 text, sent as it is.</summary>
        public static ImageInput FromString(string value) => new ImageInput(value);

        /// <summary>The image's bytes (JPEG, PNG or WebP).</summary>
        public static ImageInput FromBytes(byte[] bytes)
        {
            if (bytes is null || bytes.Length == 0) throw new ArgumentException("An image cannot be empty.", nameof(bytes));
            return new ImageInput($"data:{Sniff(bytes)};base64,{Convert.ToBase64String(bytes)}");
        }

        /// <summary>The image in a file on disk.</summary>
        public static ImageInput FromFile(string path) => FromBytes(File.ReadAllBytes(path));

        /// <summary>The image in a stream, read to its end.</summary>
        public static ImageInput FromStream(Stream stream)
        {
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return FromBytes(copy.ToArray());
        }

        /// <summary>A link, a data URI or base64 text.</summary>
        public static implicit operator ImageInput(string value) => FromString(value);

        /// <summary>The image's bytes.</summary>
        public static implicit operator ImageInput(byte[] bytes) => FromBytes(bytes);

        /// <inheritdoc />
        public override string ToString() => Value.Length > 64 ? Value.Substring(0, 64) + "…" : Value;

        /// <summary>The image type the bytes declare by their first bytes.</summary>
        internal static string Sniff(byte[] b)
        {
            if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "image/jpeg";
            if (b.Length >= 4 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "image/png";
            if (b.Length >= 12 && b[0] == (byte)'R' && b[1] == (byte)'I' && b[2] == (byte)'F' && b[3] == (byte)'F'
                && b[8] == (byte)'W' && b[9] == (byte)'E' && b[10] == (byte)'B' && b[11] == (byte)'P') return "image/webp";
            return "application/octet-stream";
        }
    }

    internal sealed class ImageInputConverter : JsonConverter<ImageInput>
    {
        public override ImageInput Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            ImageInput.FromString(reader.GetString() ?? throw new JsonException("An image must be a string."));

        public override void Write(Utf8JsonWriter writer, ImageInput value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Value);
    }

    /// <summary>Where an image comes from: the image itself, or a finished job of yours.</summary>
    public class Source
    {
        /// <summary>The image itself.</summary>
        [JsonPropertyName("image")] public ImageInput? Image { get; set; }

        /// <summary>A finished job of yours: work on its output.</summary>
        [JsonPropertyName("job_id")] public string? JobId { get; set; }

        /// <summary>The image itself.</summary>
        public static Source FromImage(ImageInput image) => new Source { Image = image };

        /// <summary>A finished job's output.</summary>
        public static Source FromJob(string jobId) => new Source { JobId = jobId };
    }

    /// <summary>A reference image and what to take from it.</summary>
    public sealed class Reference : Source
    {
        /// <summary>What to take from the reference: one of <see cref="ReferenceModes"/>. Default <c>auto</c>.</summary>
        [JsonPropertyName("mode")] public string? Mode { get; set; }

        /// <summary>A reference image, taken in <paramref name="mode"/> (default <c>auto</c>).</summary>
        public static Reference Of(ImageInput image, string? mode = null) => new Reference { Image = image, Mode = mode };

        /// <summary>A finished job's output as the reference.</summary>
        public static Reference OfJob(string jobId, string? mode = null) => new Reference { JobId = jobId, Mode = mode };
    }

    /// <summary>An area of the image, in fractions (0–1) from its top-left corner.</summary>
    public sealed class Region
    {
        /// <summary>A rectangle.</summary>
        [JsonPropertyName("box")] public RegionBox? Box { get; set; }

        /// <summary>An outline of 3 to 64 [x, y] points.</summary>
        [JsonPropertyName("polygon")] public double[][]? Polygon { get; set; }

        /// <summary>A rectangle from its top-left corner, its width and its height.</summary>
        public static Region Rectangle(double x, double y, double width, double height) =>
            new Region { Box = new RegionBox { X = x, Y = y, Width = width, Height = height } };

        /// <summary>An outline, as [x, y] points.</summary>
        public static Region Outline(params (double X, double Y)[] points)
        {
            var polygon = new double[points.Length][];
            for (var i = 0; i < points.Length; i++) polygon[i] = new[] { points[i].X, points[i].Y };
            return new Region { Polygon = polygon };
        }
    }

    /// <summary>A rectangle, in fractions (0–1) of the image.</summary>
    public sealed class RegionBox
    {
        /// <summary>From the left edge.</summary>
        [JsonPropertyName("x")] public double X { get; set; }

        /// <summary>From the top edge.</summary>
        [JsonPropertyName("y")] public double Y { get; set; }

        /// <summary>Its width.</summary>
        [JsonPropertyName("width")] public double Width { get; set; }

        /// <summary>Its height.</summary>
        [JsonPropertyName("height")] public double Height { get; set; }
    }
}
