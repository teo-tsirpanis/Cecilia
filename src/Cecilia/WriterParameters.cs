// This file is part of Cecilia.
// Licensed under the MIT License.

#nullable enable

using Cecilia.Cil;
using System.IO;

namespace Cecilia
{
    public sealed class WriterParameters
    {
        public uint? Timestamp { get; set; }

        public Stream? SymbolStream { get; set; }

        public ISymbolWriterProvider? SymbolWriterProvider { get; set; }

        public bool WriteSymbols { get; set; }

        public bool HasStrongNameKey => StrongNameKeyPair != null;

        public byte[] StrongNameKeyBlob
        {
            set => StrongNameKeyPair = new(value);
        }

#if NET
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
        public string StrongNameKeyContainer
        {
            set => StrongNameKeyPair = new(value);
        }

        public PortableStrongNameKeyPair? StrongNameKeyPair { get; set; }

        public bool DeterministicMvid { get; set; }
    }
}
