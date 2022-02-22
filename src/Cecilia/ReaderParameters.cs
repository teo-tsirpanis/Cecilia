// This file is part of Cecilia.
// Licensed under the MIT License.

#nullable enable

using Cecilia.Cil;
using System.IO;

namespace Cecilia
{
    public sealed class ReaderParameters
    {
        public ReadingMode ReadingMode { get; set; }

        public bool InMemory { get; set; }

        public IAssemblyResolver? AssemblyResolver { get; set; }

        public IMetadataResolver? MetadataResolver { get; set; }

        public IMetadataImporterProvider? MetadataImporterProvider { get; set; }

        public IReflectionImporterProvider? ReflectionImporterProvider { get; set; }

        public Stream? SymbolStream { get; set; }

        public ISymbolReaderProvider? SymbolReaderProvider { get; set; }

        public bool ReadSymbols { get; set; }

        public bool ThrowIfSymbolsAreNotMatching { get; set; }

        public bool ReadWrite { get; set; }

        public bool ApplyWindowsRuntimeProjections { get; set; }

        public ReaderParameters() : this(ReadingMode.Deferred)
        {
        }

        public ReaderParameters(ReadingMode readingMode)
        {
            ReadingMode = readingMode;
            ThrowIfSymbolsAreNotMatching = true;
        }
    }
}
