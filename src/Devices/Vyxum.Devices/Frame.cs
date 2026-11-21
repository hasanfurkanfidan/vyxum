using System.Text;

namespace Vyxum.Devices;

/// <summary>One frame read by a <see cref="FrameReader"/>, without its framing bytes.</summary>
/// <param name="Payload">The frame content without STX and terminators.</param>
/// <param name="Trailer">Bytes after the terminator that belong to the frame (STX mode checksum); empty otherwise.</param>
public sealed record Frame(byte[] Payload, byte[] Trailer)
{
    /// <summary>The payload as text. Each byte maps to one character (Latin-1), so no byte is lost.</summary>
    public string Text => Encoding.Latin1.GetString(Payload);
}
