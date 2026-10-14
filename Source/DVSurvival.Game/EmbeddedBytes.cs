using System;
using System.IO;

namespace DVSurvival.Mod
{
    internal static class EmbeddedBytes
    {
        internal static byte[] Read(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            var bytes = new byte[checked((int)stream.Length)];
            int offset = 0;
            while (offset < bytes.Length)
            {
                int count = stream.Read(bytes, offset, bytes.Length - offset);
                if (count == 0) throw new EndOfStreamException();
                offset += count;
            }
            return bytes;
        }
    }
}
