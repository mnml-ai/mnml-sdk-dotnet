using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Mnml
{
    /// <summary>A render from a source image, or from a prompt alone (<c>mode = text-to-render</c>).</summary>
    public sealed class RenderRequest
    {
        /// <summary>What to render. Up to 4,000 characters. Required.</summary>
        [JsonPropertyName("prompt")] public string Prompt { get; set; } = "";

        /// <summary>An engine id or name (<c>v4.6-ultra</c>, <c>v4.6</c>). <see cref="MnmlClient.Engines"/> lists them.</summary>
        [JsonPropertyName("engine")] public string? Engine { get; set; }

        /// <summary>One of <see cref="Modes"/>.</summary>
        [JsonPropertyName("mode")] public string? Mode { get; set; }

        /// <summary>The source image.</summary>
        [JsonPropertyName("image")] public ImageInput? Image { get; set; }

        /// <summary>The same as <see cref="Image"/>, under its first name.</summary>
        [Obsolete("Use Image.")]
        [JsonPropertyName("image_url")] public string? ImageUrl { get; set; }

        /// <summary>A finished job of yours, whose output is the source.</summary>
        [JsonPropertyName("job_id")] public string? JobId { get; set; }

        /// <summary>Up to 15 reference images; how many an engine takes is its <c>MaxReferences</c>.</summary>
        [JsonPropertyName("references")] public List<Reference>? References { get; set; }

        /// <summary>Studio's settings for this engine and mode, name to value. Omit for Auto.</summary>
        [JsonPropertyName("settings")] public Dictionary<string, string>? Settings { get; set; }

        /// <summary>One of <see cref="AspectRatios"/>.</summary>
        [JsonPropertyName("aspect_ratio")] public string? AspectRatio { get; set; }

        /// <summary>1–4 variations of this brief, each its own job and charge.</summary>
        [JsonPropertyName("count")] public int? Count { get; set; }

        /// <summary>A seed, to repeat a result.</summary>
        [JsonPropertyName("seed")] public long? Seed { get; set; }

        /// <summary>A public https:// URL to send this job's settle event to, signed like every webhook.</summary>
        [JsonPropertyName("webhook_url")] public string? WebhookUrl { get; set; }
    }

    /// <summary>A prompt edit or an erase, over the whole image or a region.</summary>
    public sealed class EditRequest
    {
        /// <summary><c>edit</c> (the default) changes what the prompt says; <c>erase</c> removes what the region covers.</summary>
        [JsonPropertyName("kind")] public string? Kind { get; set; }

        /// <summary>What to change.</summary>
        [JsonPropertyName("prompt")] public string? Prompt { get; set; }

        /// <summary>The image to edit.</summary>
        [JsonPropertyName("image")] public ImageInput? Image { get; set; }

        /// <summary>The same as <see cref="Image"/>, under its first name.</summary>
        [Obsolete("Use Image.")]
        [JsonPropertyName("image_url")] public string? ImageUrl { get; set; }

        /// <summary>A finished job of yours, whose output is edited.</summary>
        [JsonPropertyName("job_id")] public string? JobId { get; set; }

        /// <summary>An engine id or name.</summary>
        [JsonPropertyName("engine")] public string? Engine { get; set; }

        /// <summary>One of <see cref="Modes"/>.</summary>
        [JsonPropertyName("mode")] public string? Mode { get; set; }

        /// <summary>Reference images.</summary>
        [JsonPropertyName("references")] public List<Reference>? References { get; set; }

        /// <summary>The area to change; the whole image when omitted.</summary>
        [JsonPropertyName("region")] public Region? Region { get; set; }

        /// <summary>A PNG the size of the image, white where to change, black where to keep.</summary>
        [JsonPropertyName("mask")] public ImageInput? Mask { get; set; }

        /// <summary>A public https:// URL to send this job's settle event to.</summary>
        [JsonPropertyName("webhook_url")] public string? WebhookUrl { get; set; }
    }

    /// <summary>An upscale, enhance, background removal or outpaint.</summary>
    public sealed class EnhancementRequest
    {
        /// <summary>One of <see cref="EnhancementKinds"/>. Required.</summary>
        [JsonPropertyName("kind")] public string Kind { get; set; } = "";

        /// <summary>The image to enhance.</summary>
        [JsonPropertyName("image")] public ImageInput? Image { get; set; }

        /// <summary>The same as <see cref="Image"/>, under its first name.</summary>
        [Obsolete("Use Image.")]
        [JsonPropertyName("image_url")] public string? ImageUrl { get; set; }

        /// <summary>A finished job of yours, whose output is enhanced.</summary>
        [JsonPropertyName("job_id")] public string? JobId { get; set; }

        /// <summary>0–100, for <c>enhance</c>.</summary>
        [JsonPropertyName("creativity")] public int? Creativity { get; set; }

        /// <summary>What to add, for <c>enhance</c> and <c>outpaint</c>.</summary>
        [JsonPropertyName("prompt")] public string? Prompt { get; set; }

        /// <summary>The frame to extend to, for <c>outpaint</c>: one of <see cref="AspectRatios"/> but <c>auto</c>.</summary>
        [JsonPropertyName("aspect_ratio")] public string? AspectRatio { get; set; }

        /// <summary>A public https:// URL to send this job's settle event to.</summary>
        [JsonPropertyName("webhook_url")] public string? WebhookUrl { get; set; }
    }

    /// <summary>A video from a still image.</summary>
    public sealed class VideoRequest
    {
        /// <summary>The first frame.</summary>
        [JsonPropertyName("image")] public ImageInput? Image { get; set; }

        /// <summary>The same as <see cref="Image"/>, under its first name.</summary>
        [Obsolete("Use Image.")]
        [JsonPropertyName("image_url")] public string? ImageUrl { get; set; }

        /// <summary>A finished job of yours, whose output is the first frame.</summary>
        [JsonPropertyName("job_id")] public string? JobId { get; set; }

        /// <summary>A video model id or name. Default <c>v2.0-flash</c>.</summary>
        [JsonPropertyName("model")] public string? Model { get; set; }

        /// <summary>10 or 15 on the v2.0 models (default 10); v1.1 has one fixed length.</summary>
        [JsonPropertyName("duration_seconds")] public int? DurationSeconds { get; set; }

        /// <summary><c>static</c> (the default), <c>auto</c>, or one or two of <see cref="CameraMoves"/> joined by a comma.</summary>
        [JsonPropertyName("camera_movement")] public string? CameraMovement { get; set; }

        /// <summary><c>subtle</c> (the default), <c>balanced</c>, <c>dynamic</c> or <c>auto</c>.</summary>
        [JsonPropertyName("motion")] public string? Motion { get; set; }

        /// <summary>What should happen in the clip, in plain words.</summary>
        [JsonPropertyName("prompt")] public string? Prompt { get; set; }

        /// <summary>A last frame to move towards.</summary>
        [JsonPropertyName("end_frame")] public Source? EndFrame { get; set; }

        /// <summary>v1.1 only: the Cinematic variant, which needs <see cref="EndFrame"/>.</summary>
        [JsonPropertyName("cinematic")] public bool? Cinematic { get; set; }

        /// <summary>A public https:// URL to send this job's settle event to.</summary>
        [JsonPropertyName("webhook_url")] public string? WebhookUrl { get; set; }
    }

    /// <summary>The render modes.</summary>
    public static class Modes
    {
#pragma warning disable CS1591 // Each constant is its own value.
        public const string Exterior = "exterior";
        public const string Interior = "interior";
        public const string Masterplan = "masterplan";
        public const string Plan = "plan";
        public const string Landscape = "landscape";
        public const string Product = "product";
        public const string TextToRender = "text-to-render";
#pragma warning restore CS1591
    }

    /// <summary>The aspect ratios.</summary>
    public static class AspectRatios
    {
#pragma warning disable CS1591
        public const string Auto = "auto";
        public const string Square = "1:1";
        public const string Landscape3x2 = "3:2";
        public const string Landscape4x3 = "4:3";
        public const string Landscape5x4 = "5:4";
        public const string Widescreen16x9 = "16:9";
        public const string Ultrawide21x9 = "21:9";
        public const string Portrait2x3 = "2:3";
        public const string Portrait3x4 = "3:4";
        public const string Portrait4x5 = "4:5";
        public const string Portrait9x16 = "9:16";
#pragma warning restore CS1591
    }

    /// <summary>What a reference image lends the render.</summary>
    public static class ReferenceModes
    {
#pragma warning disable CS1591
        public const string Auto = "auto";
        public const string Style = "style";
        public const string Material = "material";
        public const string Atmosphere = "atmosphere";
        public const string Color = "color";
        public const string Geometry = "geometry";
#pragma warning restore CS1591
    }

    /// <summary>The enhancement kinds.</summary>
    public static class EnhancementKinds
    {
#pragma warning disable CS1591
        public const string Upscale = "upscale";
        public const string Enhance = "enhance";
        public const string BackgroundRemove = "bg-remove";
        public const string Outpaint = "outpaint";
#pragma warning restore CS1591
    }

    /// <summary>The camera moves a video can make.</summary>
    public static class CameraMoves
    {
#pragma warning disable CS1591
        public const string Static = "static";
        public const string Auto = "auto";
        public const string DollyIn = "dolly-in";
        public const string DollyOut = "dolly-out";
        public const string TruckLeft = "truck-left";
        public const string TruckRight = "truck-right";
        public const string PedestalUp = "pedestal-up";
        public const string PedestalDown = "pedestal-down";
        public const string PanLeft = "pan-left";
        public const string PanRight = "pan-right";
        public const string TiltUp = "tilt-up";
        public const string TiltDown = "tilt-down";
        public const string OrbitLeft = "orbit-left";
        public const string OrbitRight = "orbit-right";
        public const string ZoomIn = "zoom-in";
        public const string ZoomOut = "zoom-out";
#pragma warning restore CS1591
    }
}
