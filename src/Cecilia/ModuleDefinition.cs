//
// Author:
//   Jb Evain (jbevain@gmail.com)
//
// Copyright (c) 2008 - 2015 Jb Evain
// Copyright (c) 2008 - 2011 Novell, Inc.
//
// Licensed under the MIT/X11 license.
//

using Cecilia.Cil;
using Cecilia.Metadata;
using Cecilia.PE;
using Mono.Collections.Generic;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SR = System.Reflection;

namespace Cecilia
{
    public enum ReadingMode
    {
        Immediate = 1,
        Deferred = 2,
    }

    public sealed class ModuleDefinition : ModuleReference, ICustomAttributeProvider, ICustomDebugInformationProvider, IDisposable
    {
        internal Image Image;
        internal MetadataSystem MetadataSystem;
        internal ReadingMode ReadingMode;
        internal ISymbolReaderProvider SymbolReaderProvider;

        internal ISymbolReader symbol_reader;
        internal Disposable<IAssemblyResolver> assembly_resolver;
        internal IMetadataResolver metadata_resolver;
        internal TypeSystem type_system;
        internal readonly MetadataReader reader;
        readonly string file_name;

        internal string runtime_version;
        internal ModuleKind kind;
        WindowsRuntimeProjections projections;
        MetadataKind metadata_kind;
        TargetRuntime runtime;
        TargetArchitecture architecture;
        ModuleAttributes attributes;
        ModuleCharacteristics characteristics;
        Guid mvid;

        internal ushort linker_version = 8;
        internal ushort subsystem_major = 4;
        internal ushort subsystem_minor = 0;
        internal uint timestamp;

        internal AssemblyDefinition assembly;
        MethodDefinition entry_point;
        bool entry_point_set;

        internal IReflectionImporter reflection_importer;
        internal IMetadataImporter metadata_importer;

        Collection<CustomAttribute> custom_attributes;
        Collection<AssemblyNameReference> references;
        Collection<ModuleReference> modules;
        Collection<Resource> resources;
        Collection<ExportedType> exported_types;
        TypeDefinitionCollection types;

        internal Collection<CustomDebugInformation> custom_infos;

        internal MetadataBuilder metadata_builder;

        public bool IsMain
        {
            get { return kind != ModuleKind.NetModule; }
        }

        public ModuleKind Kind
        {
            get { return kind; }
            set { kind = value; }
        }

        public MetadataKind MetadataKind
        {
            get { return metadata_kind; }
            set { metadata_kind = value; }
        }

        internal bool IsWindowsMetadata => metadata_kind != MetadataKind.Ecma335;

        internal WindowsRuntimeProjections Projections
        {
            get
            {
                if (projections == null)
                    Interlocked.CompareExchange(ref projections, new WindowsRuntimeProjections(this), null);

                return projections;
            }
        }

        public TargetRuntime Runtime
        {
            get { return runtime; }
            set
            {
                runtime = value;
                runtime_version = runtime.RuntimeVersionString();
            }
        }

        public string RuntimeVersion
        {
            get { return runtime_version; }
            set
            {
                runtime_version = value;
                runtime = runtime_version.ParseRuntime();
            }
        }

        public TargetArchitecture Architecture
        {
            get { return architecture; }
            set { architecture = value; }
        }

        public ModuleAttributes Attributes
        {
            get { return attributes; }
            set { attributes = value; }
        }

        public ModuleCharacteristics Characteristics
        {
            get { return characteristics; }
            set { characteristics = value; }
        }

        public string FileName
        {
            get { return file_name; }
        }

        public Guid Mvid
        {
            get { return mvid; }
            set { mvid = value; }
        }

        internal bool HasImage
        {
            get { return Image != null; }
        }

        public bool HasSymbols
        {
            get { return symbol_reader != null; }
        }

        public ISymbolReader SymbolReader
        {
            get { return symbol_reader; }
        }

        public override MetadataScopeType MetadataScopeType
        {
            get { return MetadataScopeType.ModuleDefinition; }
        }

        public AssemblyDefinition Assembly
        {
            get { return assembly; }
        }

        internal IReflectionImporter ReflectionImporter
        {
            get
            {
                if (reflection_importer == null)
                    Interlocked.CompareExchange(ref reflection_importer, new DefaultReflectionImporter(this), null);

                return reflection_importer;
            }
        }

        internal IMetadataImporter MetadataImporter
        {
            get
            {
                if (metadata_importer == null)
                    Interlocked.CompareExchange(ref metadata_importer, new DefaultMetadataImporter(this), null);

                return metadata_importer;
            }
        }

        public IAssemblyResolver AssemblyResolver
        {
            get
            {
                if (assembly_resolver.value == null)
                {
                    lock (module_lock)
                    {
                        assembly_resolver = Disposable.Owned(new DefaultAssemblyResolver() as IAssemblyResolver);
                    }
                }

                return assembly_resolver.value;
            }
        }

        public IMetadataResolver MetadataResolver
        {
            get
            {
                if (metadata_resolver == null)
                    Interlocked.CompareExchange(ref metadata_resolver, new MetadataResolver(this.AssemblyResolver), null);

                return metadata_resolver;
            }
        }

        public TypeSystem TypeSystem
        {
            get
            {
                if (type_system == null)
                    Interlocked.CompareExchange(ref type_system, TypeSystem.CreateTypeSystem(this), null);

                return type_system;
            }
        }

        public bool HasAssemblyReferences
        {
            get
            {
                if (references != null)
                    return references.Count > 0;

                return HasImage && Image.HasTable(Table.AssemblyRef);
            }
        }

        public Collection<AssemblyNameReference> AssemblyReferences
        {
            get
            {
                if (references != null)
                    return references;

                if (HasImage)
                    return Read(ref references, this, (_, reader) => reader.ReadAssemblyReferences());

                Interlocked.CompareExchange(ref references, new Collection<AssemblyNameReference>(), null);
                return references;
            }
        }

        public bool HasModuleReferences
        {
            get
            {
                if (modules != null)
                    return modules.Count > 0;

                return HasImage && Image.HasTable(Table.ModuleRef);
            }
        }

        public Collection<ModuleReference> ModuleReferences
        {
            get
            {
                if (modules != null)
                    return modules;

                if (HasImage)
                    return Read(ref modules, this, (_, reader) => reader.ReadModuleReferences());

                Interlocked.CompareExchange(ref modules, new Collection<ModuleReference>(), null);
                return modules;
            }
        }

        public bool HasResources
        {
            get
            {
                if (resources != null)
                    return resources.Count > 0;

                if (HasImage)
                    return Image.HasTable(Table.ManifestResource) || Read(this, (_, reader) => reader.HasFileResource());

                return false;
            }
        }

        public Collection<Resource> Resources
        {
            get
            {
                if (resources != null)
                    return resources;

                if (HasImage)
                    return Read(ref resources, this, (_, reader) => reader.ReadResources());

                Interlocked.CompareExchange(ref resources, new Collection<Resource>(), null);
                return resources;
            }
        }

        public bool HasCustomAttributes
        {
            get
            {
                if (custom_attributes != null)
                    return custom_attributes.Count > 0;

                return this.GetHasCustomAttributes(this);
            }
        }

        public Collection<CustomAttribute> CustomAttributes
        {
            get { return custom_attributes ?? (this.GetCustomAttributes(ref custom_attributes, this)); }
        }

        public bool HasTypes
        {
            get
            {
                if (types != null)
                    return types.Count > 0;

                return HasImage && Image.HasTable(Table.TypeDef);
            }
        }

        public Collection<TypeDefinition> Types
        {
            get
            {
                if (types != null)
                    return types;

                if (HasImage)
                    return Read(ref types, this, (_, reader) => reader.ReadTypes());

                Interlocked.CompareExchange(ref types, new TypeDefinitionCollection(this), null);
                return types;
            }
        }

        public bool HasExportedTypes
        {
            get
            {
                if (exported_types != null)
                    return exported_types.Count > 0;

                return HasImage && Image.HasTable(Table.ExportedType);
            }
        }

        public Collection<ExportedType> ExportedTypes
        {
            get
            {
                if (exported_types != null)
                    return exported_types;

                if (HasImage)
                    return Read(ref exported_types, this, (_, reader) => reader.ReadExportedTypes());

                Interlocked.CompareExchange(ref exported_types, new Collection<ExportedType>(), null);
                return exported_types;
            }
        }

        public MethodDefinition EntryPoint
        {
            get
            {
                if (entry_point_set)
                    return entry_point;

                if (HasImage)
                    Read(ref entry_point, this, (_, reader) => reader.ReadEntryPoint());
                else
                    entry_point = null;

                entry_point_set = true;
                return entry_point;
            }
            set
            {
                entry_point = value;
                entry_point_set = true;
            }
        }

        public bool HasCustomDebugInformations
        {
            get
            {
                return custom_infos != null && custom_infos.Count > 0;
            }
        }

        public Collection<CustomDebugInformation> CustomDebugInformations
        {
            get
            {
                if (custom_infos == null)
                    Interlocked.CompareExchange(ref custom_infos, new Collection<CustomDebugInformation>(), null);

                return custom_infos;
            }
        }

        internal ModuleDefinition()
        {
            this.MetadataSystem = new MetadataSystem();
            this.token = new MetadataToken(TokenType.Module, 1);
        }

        internal ModuleDefinition(Image image)
            : this()
        {
            this.Image = image;
            this.kind = image.Kind;
            this.RuntimeVersion = image.RuntimeVersion;
            this.architecture = image.Architecture;
            this.attributes = image.Attributes;
            this.characteristics = image.DllCharacteristics;
            this.linker_version = image.LinkerVersion;
            this.subsystem_major = image.SubSystemMajor;
            this.subsystem_minor = image.SubSystemMinor;
            this.file_name = image.FileName;
            this.timestamp = image.Timestamp;

            this.reader = new MetadataReader(this);
        }

        public void Dispose()
        {
            if (Image != null)
                Image.Dispose();

            if (symbol_reader != null)
                symbol_reader.Dispose();

            if (assembly_resolver.value != null)
                assembly_resolver.Dispose();
        }

        public bool HasTypeReference(string fullName)
        {
            return HasTypeReference(string.Empty, fullName);
        }

        public bool HasTypeReference(string scope, string fullName)
        {
            Mixin.CheckNotNullOrEmpty(fullName);

            if (!HasImage)
                return false;

            return GetTypeReference(scope, fullName) != null;
        }

        public bool TryGetTypeReference(string fullName, out TypeReference type)
        {
            return TryGetTypeReference(string.Empty, fullName, out type);
        }

        public bool TryGetTypeReference(string scope, string fullName, out TypeReference type)
        {
            Mixin.CheckNotNullOrEmpty(fullName);

            if (!HasImage)
            {
                type = null;
                return false;
            }

            return (type = GetTypeReference(scope, fullName)) != null;
        }

        TypeReference GetTypeReference(string scope, string fullname)
        {
            return Read(new Row<string, string>(scope, fullname), (row, reader) => reader.GetTypeReference(row.Col1, row.Col2));
        }

        public IEnumerable<TypeReference> GetTypeReferences()
        {
            if (!HasImage)
                return Array.Empty<TypeReference>();

            return Read(this, (_, reader) => reader.GetTypeReferences());
        }

        public IEnumerable<MemberReference> GetMemberReferences()
        {
            if (!HasImage)
                return Array.Empty<MemberReference>();

            return Read(this, (_, reader) => reader.GetMemberReferences());
        }

        public IEnumerable<CustomAttribute> GetCustomAttributes()
        {
            if (!HasImage)
                return Array.Empty<CustomAttribute>();

            return Read(this, (_, reader) => reader.GetCustomAttributes());
        }

        public TypeReference GetType(string fullName, bool runtimeName)
        {
            return runtimeName
                ? TypeParser.ParseType(this, fullName, typeDefinitionOnly: true)
                : GetType(fullName);
        }

        public TypeDefinition GetType(string fullName)
        {
            Mixin.CheckNotNullOrEmpty(fullName);

            var position = fullName.IndexOf('/');
            if (position > 0)
                return GetNestedType(fullName);

            return ((TypeDefinitionCollection)this.Types).GetType(fullName);
        }

        public TypeDefinition GetType(string @namespace, string name)
        {
            Mixin.CheckNotNullOrEmpty(name);

            return ((TypeDefinitionCollection)this.Types).GetType(@namespace ?? string.Empty, name);
        }

        public IEnumerable<TypeDefinition> GetTypes()
        {
            return GetTypes(Types);
        }

        static IEnumerable<TypeDefinition> GetTypes(Collection<TypeDefinition> types)
        {
            for (int i = 0; i < types.Count; i++)
            {
                var type = types[i];

                yield return type;

                if (!type.HasNestedTypes)
                    continue;

                foreach (var nested in GetTypes(type.NestedTypes))
                    yield return nested;
            }
        }

        TypeDefinition GetNestedType(string fullname)
        {
            var names = fullname.Split('/');
            var type = GetType(names[0]);

            if (type == null)
                return null;

            for (int i = 1; i < names.Length; i++)
            {
                var nested_type = type.GetNestedType(names[i]);
                if (nested_type == null)
                    return null;

                type = nested_type;
            }

            return type;
        }

        internal FieldDefinition Resolve(FieldReference field)
        {
            return MetadataResolver.Resolve(field);
        }

        internal MethodDefinition Resolve(MethodReference method)
        {
            return MetadataResolver.Resolve(method);
        }

        internal TypeDefinition Resolve(TypeReference type)
        {
            return MetadataResolver.Resolve(type);
        }

        static void CheckContext(IGenericParameterProvider context, ModuleDefinition module)
        {
            if (context == null)
                return;

            if (context.Module != module)
                throw new ArgumentException();
        }
        public TypeReference ImportReference(Type type)
        {
            return ImportReference(type, null);
        }
        public TypeReference ImportReference(Type type, IGenericParameterProvider context)
        {
            Mixin.CheckNotNull(type);
            CheckContext(context, this);

            return ReflectionImporter.ImportReference(type, context);
        }

        public FieldReference ImportReference(SR.FieldInfo field)
        {
            return ImportReference(field, null);
        }

        public FieldReference ImportReference(SR.FieldInfo field, IGenericParameterProvider context)
        {
            Mixin.CheckNotNull(field);
            CheckContext(context, this);

            return ReflectionImporter.ImportReference(field, context);
        }

        public MethodReference ImportReference(SR.MethodBase method)
        {
            return ImportReference(method, null);
        }

        public MethodReference ImportReference(SR.MethodBase method, IGenericParameterProvider context)
        {
            Mixin.CheckNotNull(method);
            CheckContext(context, this);

            return ReflectionImporter.ImportReference(method, context);
        }

        public TypeReference ImportReference(TypeReference type)
        {
            return ImportReference(type, null);
        }

        public TypeReference ImportReference(TypeReference type, IGenericParameterProvider context)
        {
            Mixin.CheckNotNull(type);

            if (type.Module == this)
                return type;

            CheckContext(context, this);

            return MetadataImporter.ImportReference(type, context);
        }

        public FieldReference ImportReference(FieldReference field)
        {
            return ImportReference(field, null);
        }

        public FieldReference ImportReference(FieldReference field, IGenericParameterProvider context)
        {
            Mixin.CheckNotNull(field);

            if (field.Module == this)
                return field;

            CheckContext(context, this);

            return MetadataImporter.ImportReference(field, context);
        }

        public MethodReference ImportReference(MethodReference method)
        {
            return ImportReference(method, null);
        }

        public MethodReference ImportReference(MethodReference method, IGenericParameterProvider context)
        {
            Mixin.CheckNotNull(method);

            if (method.Module == this)
                return method;

            CheckContext(context, this);

            return MetadataImporter.ImportReference(method, context);
        }

        public IMetadataTokenProvider LookupToken(int token)
        {
            return LookupToken(new MetadataToken((uint)token));
        }

        public IMetadataTokenProvider LookupToken(MetadataToken token)
        {
            return Read(token, (t, reader) => reader.LookupToken(t));
        }

        public void ImmediateRead()
        {
            if (!HasImage)
                return;
            ReadingMode = ReadingMode.Immediate;
            var moduleReader = new ImmediateModuleReader(Image);
            moduleReader.ReadModule(this, resolve_attributes: true);
        }

        readonly object module_lock = new object();

        internal object SyncRoot
        {
            get { return module_lock; }
        }

        internal void Read<TItem>(TItem item, Action<TItem, MetadataReader> read)
        {
            lock (module_lock)
            {
                var position = reader.position;
                var context = reader.context;

                read(item, reader);

                reader.position = position;
                reader.context = context;
            }
        }

        internal TRet Read<TItem, TRet>(TItem item, Func<TItem, MetadataReader, TRet> read)
        {
            lock (module_lock)
            {
                var position = reader.position;
                var context = reader.context;

                var ret = read(item, reader);

                reader.position = position;
                reader.context = context;

                return ret;
            }
        }

        internal TRet Read<TItem, TRet>(ref TRet variable, TItem item, Func<TItem, MetadataReader, TRet> read) where TRet : class
        {
            lock (module_lock)
            {
                if (variable != null)
                    return variable;

                var position = reader.position;
                var context = reader.context;

                var ret = read(item, reader);

                reader.position = position;
                reader.context = context;

                return variable = ret;
            }
        }

        public bool HasDebugHeader
        {
            get { return Image != null && Image.DebugHeader != null; }
        }

        public ImageDebugHeader GetDebugHeader()
        {
            return Image.DebugHeader ?? new ImageDebugHeader();
        }

        public static ModuleDefinition CreateModule(string name, ModuleKind kind)
        {
            return CreateModule(name, new ModuleParameters { Kind = kind });
        }

        public static ModuleDefinition CreateModule(string name, ModuleParameters parameters)
        {
            Mixin.CheckNotNullOrEmpty(name);
            Mixin.CheckNotNull(parameters);

            var module = new ModuleDefinition
            {
                Name = name,
                kind = parameters.Kind,
                timestamp = parameters.Timestamp ?? Mixin.GetTimestamp(),
                Runtime = parameters.Runtime,
                architecture = parameters.Architecture,
                mvid = Guid.NewGuid(),
                Attributes = ModuleAttributes.ILOnly,
                Characteristics = (ModuleCharacteristics)0x8540,
            };

            if (parameters.AssemblyResolver != null)
                module.assembly_resolver = Disposable.NotOwned(parameters.AssemblyResolver);

            if (parameters.MetadataResolver != null)
                module.metadata_resolver = parameters.MetadataResolver;

            if (parameters.MetadataImporterProvider != null)
                module.metadata_importer = parameters.MetadataImporterProvider.GetMetadataImporter(module);

            if (parameters.ReflectionImporterProvider != null)
                module.reflection_importer = parameters.ReflectionImporterProvider.GetReflectionImporter(module);

            if (parameters.Kind != ModuleKind.NetModule)
            {
                var assembly = new AssemblyDefinition();
                module.assembly = assembly;
                module.assembly.Name = CreateAssemblyName(name);
                assembly.main_module = module;
            }

            module.Types.Add(new TypeDefinition(string.Empty, "<Module>", TypeAttributes.NotPublic));

            return module;
        }

        static AssemblyNameDefinition CreateAssemblyName(string name)
        {
            if (name.EndsWith(".dll") || name.EndsWith(".exe"))
                name = name.Substring(0, name.Length - 4);

            return new AssemblyNameDefinition(name, Mixin.ZeroVersion);
        }

        public void ReadSymbols()
        {
            if (string.IsNullOrEmpty(file_name))
                throw new InvalidOperationException();

            var provider = new DefaultSymbolReaderProvider(throwIfNoSymbol: true);
            ReadSymbols(provider.GetSymbolReader(this, file_name), throwIfSymbolsAreNotMaching: true);
        }

        public void ReadSymbols(ISymbolReader reader)
        {
            ReadSymbols(reader, throwIfSymbolsAreNotMaching: true);
        }

        public void ReadSymbols(ISymbolReader reader, bool throwIfSymbolsAreNotMaching)
        {
            Mixin.CheckNotNull(reader);

            symbol_reader = reader;

            if (!symbol_reader.ProcessDebugHeader(GetDebugHeader()))
            {
                symbol_reader = null;

                if (throwIfSymbolsAreNotMaching)
                    throw new SymbolsNotMatchingException("Symbols were found but are not matching the assembly");

                return;
            }

            if (HasImage && ReadingMode == ReadingMode.Immediate)
            {
                var immediate_reader = new ImmediateModuleReader(Image);
                immediate_reader.ReadSymbols(this);
            }
        }

        public static ModuleDefinition ReadModule(string fileName)
        {
            return ReadModule(fileName, new ReaderParameters(ReadingMode.Deferred));
        }

        public static ModuleDefinition ReadModule(string fileName, ReaderParameters parameters)
        {
            var stream = GetFileStream(fileName, FileMode.Open, parameters.ReadWrite ? FileAccess.ReadWrite : FileAccess.Read, FileShare.Read);

            if (parameters.InMemory)
            {
                var memory = new MemoryStream(stream.CanSeek ? (int)stream.Length : 0);
                using (stream)
                    stream.CopyTo(memory);

                memory.Position = 0;
                stream = memory;
            }

            try
            {
                return ReadModule(Disposable.Owned(stream), fileName, parameters);
            }
            catch (Exception)
            {
                stream.Dispose();
                throw;
            }
        }

        static Stream GetFileStream(string fileName, FileMode mode, FileAccess access, FileShare share)
        {
            Mixin.CheckNotNullOrEmpty(fileName);

            return new FileStream(fileName, mode, access, share);
        }

        public static ModuleDefinition ReadModule(Stream stream)
        {
            return ReadModule(stream, new ReaderParameters(ReadingMode.Deferred));
        }

        public static ModuleDefinition ReadModule(Stream stream, ReaderParameters parameters)
        {
            Mixin.CheckNotNull(stream);
            Mixin.CheckReadSeek(stream);

            return ReadModule(Disposable.NotOwned(stream), stream.GetFileName(), parameters);
        }

        static ModuleDefinition ReadModule(Disposable<Stream> stream, string fileName, ReaderParameters parameters)
        {
            Mixin.CheckNotNull(parameters);

            return ModuleReader.CreateModule(
                ImageReader.ReadImage(stream, fileName),
                parameters);
        }

        public void Write(string fileName)
        {
            Write(fileName, new WriterParameters());
        }

        public void Write(string fileName, WriterParameters parameters)
        {
            Mixin.CheckNotNull(parameters);
            var file = GetFileStream(fileName, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
            ModuleWriter.WriteModule(this, Disposable.Owned(file), parameters);
        }

        public void Write()
        {
            Write(new WriterParameters());
        }

        public void Write(WriterParameters parameters)
        {
            if (!HasImage)
                throw new InvalidOperationException();

            Write(Image.Stream.value, parameters);
        }

        public void Write(Stream stream)
        {
            Write(stream, new WriterParameters());
        }

        public void Write(Stream stream, WriterParameters parameters)
        {
            Mixin.CheckNotNull(stream);
            Mixin.CheckWriteSeek(stream);
            Mixin.CheckNotNull(parameters);

            ModuleWriter.WriteModule(this, Disposable.NotOwned(stream), parameters);
        }
    }

    static partial class Mixin
    {
        public static bool HasImage(this ModuleDefinition self)
        {
            return self != null && self.HasImage;
        }

        public static string GetFileName(this Stream self)
        {
            var file_stream = self as FileStream;
            if (file_stream == null)
                return string.Empty;

            return Path.GetFullPath(file_stream.Name);
        }
    }
}
