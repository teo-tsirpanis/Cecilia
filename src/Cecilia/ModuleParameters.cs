// This file is part of Cecilia.
// Licensed under the MIT License.

#nullable enable

namespace Cecilia
{
    public sealed class ModuleParameters
    {
        public ModuleKind Kind { get; set; } = ModuleKind.Dll;

        public TargetRuntime Runtime { get; set; } = TargetRuntime.Net_4_0;

        public uint? Timestamp { get; set; }

        public TargetArchitecture Architecture { get; set; } = TargetArchitecture.I386;

        public IAssemblyResolver? AssemblyResolver { get; set; }

        public IMetadataResolver? MetadataResolver { get; set; }

        public IMetadataImporterProvider? MetadataImporterProvider { get; set; }

        public IReflectionImporterProvider? ReflectionImporterProvider { get; set; }
    }
}
