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
using System.IO.Compression;
using System.Text;
using RVA = System.UInt32;

namespace Cecilia
{
    abstract class ModuleReader
    {
        readonly protected ModuleDefinition module;

        protected ModuleReader(Image image, ReadingMode mode)
        {
            this.module = new ModuleDefinition(image);
            this.module.ReadingMode = mode;
        }

        protected abstract void ReadModule();
        public abstract void ReadSymbols(ModuleDefinition module);

        protected void ReadModuleManifest(MetadataReader reader)
        {
            reader.Populate(module);

            ReadAssembly(reader);
        }

        void ReadAssembly(MetadataReader reader)
        {
            var name = reader.ReadAssemblyNameDefinition();
            if (name == null)
            {
                module.kind = ModuleKind.NetModule;
                return;
            }

            var assembly = new AssemblyDefinition();
            assembly.Name = name;

            module.assembly = assembly;
            assembly.main_module = module;
        }

        public static ModuleDefinition CreateModule(Image image, ReaderParameters parameters)
        {
            var reader = CreateModuleReader(image, parameters.ReadingMode);
            var module = reader.module;

            if (parameters.assembly_resolver != null)
                module.assembly_resolver = Disposable.NotOwned(parameters.assembly_resolver);

            if (parameters.metadata_resolver != null)
                module.metadata_resolver = parameters.metadata_resolver;

            if (parameters.metadata_importer_provider != null)
                module.metadata_importer = parameters.metadata_importer_provider.GetMetadataImporter(module);

            if (parameters.reflection_importer_provider != null)
                module.reflection_importer = parameters.reflection_importer_provider.GetReflectionImporter(module);

            GetMetadataKind(module, parameters);

            reader.ReadModule();

            ReadSymbols(module, parameters);

            reader.ReadSymbols(module);

            if (parameters.ReadingMode == ReadingMode.Immediate)
                module.MetadataSystem.Clear();

            return module;
        }

        static void ReadSymbols(ModuleDefinition module, ReaderParameters parameters)
        {
            var symbol_reader_provider = parameters.SymbolReaderProvider;

            if (symbol_reader_provider == null && parameters.ReadSymbols)
                symbol_reader_provider = new DefaultSymbolReaderProvider();

            if (symbol_reader_provider != null)
            {
                module.SymbolReaderProvider = symbol_reader_provider;

                var reader = parameters.SymbolStream != null
                    ? symbol_reader_provider.GetSymbolReader(module, parameters.SymbolStream)
                    : symbol_reader_provider.GetSymbolReader(module, module.FileName);

                if (reader != null)
                {
                    try
                    {
                        module.ReadSymbols(reader, parameters.ThrowIfSymbolsAreNotMatching);
                    }
                    catch (Exception)
                    {
                        reader.Dispose();
                        throw;
                    }
                }
            }

            if (module.Image.HasDebugTables())
                module.ReadSymbols(new PortablePdbReader(module.Image, module));
        }

        static void GetMetadataKind(ModuleDefinition module, ReaderParameters parameters)
        {
            if (!parameters.ApplyWindowsRuntimeProjections)
            {
                module.MetadataKind = MetadataKind.Ecma335;
                return;
            }

            var runtime_version = module.RuntimeVersion;

            if (!runtime_version.Contains("WindowsRuntime"))
                module.MetadataKind = MetadataKind.Ecma335;
            else if (runtime_version.Contains("CLR"))
                module.MetadataKind = MetadataKind.ManagedWindowsMetadata;
            else
                module.MetadataKind = MetadataKind.WindowsMetadata;
        }

        static ModuleReader CreateModuleReader(Image image, ReadingMode mode)
        {
            switch (mode)
            {
                case ReadingMode.Immediate:
                    return new ImmediateModuleReader(image);
                case ReadingMode.Deferred:
                    return new DeferredModuleReader(image);
                default:
                    throw new ArgumentException();
            }
        }
    }

    sealed class ImmediateModuleReader : ModuleReader
    {
        bool resolve_attributes;

        public ImmediateModuleReader(Image image)
            : base(image, ReadingMode.Immediate)
        {
        }

        protected override void ReadModule()
        {
            this.module.Read(this.module, (module, reader) =>
            {
                ReadModuleManifest(reader);
                ReadModule(module, resolve_attributes: true);
            });
        }

        public void ReadModule(ModuleDefinition module, bool resolve_attributes)
        {
            this.resolve_attributes = resolve_attributes;

            if (module.HasAssemblyReferences)
                _ = module.AssemblyReferences;
            if (module.HasResources)
                _ = module.Resources;
            if (module.HasModuleReferences)
                _ = module.ModuleReferences;
            if (module.HasTypes)
                ReadTypes(module.Types);
            if (module.HasExportedTypes)
                _ = module.ExportedTypes;

            ReadCustomAttributes(module);

            var assembly = module.Assembly;
            if (module.kind == ModuleKind.NetModule || assembly == null)
                return;

            ReadCustomAttributes(assembly);
            ReadSecurityDeclarations(assembly);
        }

        void ReadTypes(Collection<TypeDefinition> types)
        {
            for (int i = 0; i < types.Count; i++)
                ReadType(types[i]);
        }

        void ReadType(TypeDefinition type)
        {
            ReadGenericParameters(type);

            if (type.HasInterfaces)
                ReadInterfaces(type);

            if (type.HasNestedTypes)
                ReadTypes(type.NestedTypes);

            if (type.HasLayoutInfo)
                _ = type.ClassSize;

            if (type.HasFields)
                ReadFields(type);

            if (type.HasMethods)
                ReadMethods(type);

            if (type.HasProperties)
                ReadProperties(type);

            if (type.HasEvents)
                ReadEvents(type);

            ReadSecurityDeclarations(type);
            ReadCustomAttributes(type);
        }

        void ReadInterfaces(TypeDefinition type)
        {
            var interfaces = type.Interfaces;

            for (int i = 0; i < interfaces.Count; i++)
                ReadCustomAttributes(interfaces[i]);
        }

        void ReadGenericParameters(IGenericParameterProvider provider)
        {
            if (!provider.HasGenericParameters)
                return;

            var parameters = provider.GenericParameters;

            for (int i = 0; i < parameters.Count; i++)
            {
                var parameter = parameters[i];

                if (parameter.HasConstraints)
                    ReadGenericParameterConstraints(parameter);

                ReadCustomAttributes(parameter);
            }
        }

        void ReadGenericParameterConstraints(GenericParameter parameter)
        {
            var constraints = parameter.Constraints;

            for (int i = 0; i < constraints.Count; i++)
                ReadCustomAttributes(constraints[i]);
        }

        void ReadSecurityDeclarations(ISecurityDeclarationProvider provider)
        {
            if (!provider.HasSecurityDeclarations)
                return;

            var security_declarations = provider.SecurityDeclarations;

            if (!resolve_attributes)
                return;

            for (int i = 0; i < security_declarations.Count; i++)
            {
                var security_declaration = security_declarations[i];

                _ = security_declaration.SecurityAttributes;
            }
        }

        void ReadCustomAttributes(ICustomAttributeProvider provider)
        {
            if (!provider.HasCustomAttributes)
                return;

            var custom_attributes = provider.CustomAttributes;

            if (!resolve_attributes)
                return;

            for (int i = 0; i < custom_attributes.Count; i++)
            {
                var custom_attribute = custom_attributes[i];

                _ = custom_attribute.ConstructorArguments;
            }
        }

        void ReadFields(TypeDefinition type)
        {
            var fields = type.Fields;

            for (int i = 0; i < fields.Count; i++)
            {
                var field = fields[i];

                if (field.HasConstant)
                    _ = field.Constant;

                if (field.HasLayoutInfo)
                    _ = field.Offset;

                if (field.RVA > 0)
                    _ = field.InitialValue;

                if (field.HasMarshalInfo)
                    _ = field.MarshalInfo;

                ReadCustomAttributes(field);
            }
        }

        void ReadMethods(TypeDefinition type)
        {
            var methods = type.Methods;

            for (int i = 0; i < methods.Count; i++)
            {
                var method = methods[i];

                ReadGenericParameters(method);

                if (method.HasParameters)
                    ReadParameters(method);

                if (method.HasOverrides)
                    _ = method.Overrides;

                if (method.IsPInvokeImpl)
                    _ = method.PInvokeInfo;

                ReadSecurityDeclarations(method);
                ReadCustomAttributes(method);

                var return_type = method.MethodReturnType;
                if (return_type.HasConstant)
                    _ = return_type.Constant;

                if (return_type.HasMarshalInfo)
                    _ = return_type.MarshalInfo;

                ReadCustomAttributes(return_type);
            }
        }

        void ReadParameters(MethodDefinition method)
        {
            var parameters = method.Parameters;

            for (int i = 0; i < parameters.Count; i++)
            {
                var parameter = parameters[i];

                if (parameter.HasConstant)
                    _ = parameter.Constant;

                if (parameter.HasMarshalInfo)
                    _ = parameter.MarshalInfo;

                ReadCustomAttributes(parameter);
            }
        }

        void ReadProperties(TypeDefinition type)
        {
            var properties = type.Properties;

            for (int i = 0; i < properties.Count; i++)
            {
                var property = properties[i];

                _ = property.GetMethod;

                if (property.HasConstant)
                    _ = property.Constant;

                ReadCustomAttributes(property);
            }
        }

        void ReadEvents(TypeDefinition type)
        {
            var events = type.Events;

            for (int i = 0; i < events.Count; i++)
            {
                var @event = events[i];

                _ = @event.AddMethod;

                ReadCustomAttributes(@event);
            }
        }

        public override void ReadSymbols(ModuleDefinition module)
        {
            if (module.symbol_reader == null)
                return;

            ReadTypesSymbols(module.Types, module.symbol_reader);
        }

        void ReadTypesSymbols(Collection<TypeDefinition> types, ISymbolReader symbol_reader)
        {
            for (int i = 0; i < types.Count; i++)
            {
                var type = types[i];

                if (type.HasNestedTypes)
                    ReadTypesSymbols(type.NestedTypes, symbol_reader);

                if (type.HasMethods)
                    ReadMethodsSymbols(type, symbol_reader);
            }
        }

        void ReadMethodsSymbols(TypeDefinition type, ISymbolReader symbol_reader)
        {
            var methods = type.Methods;
            for (int i = 0; i < methods.Count; i++)
            {
                var method = methods[i];

                if (method.HasBody && method.token.RID != 0 && method.debug_info == null)
                    method.debug_info = symbol_reader.Read(method);
            }
        }
    }

    sealed class DeferredModuleReader : ModuleReader
    {
        public DeferredModuleReader(Image image)
            : base(image, ReadingMode.Deferred)
        {
        }

        protected override void ReadModule()
        {
            this.module.Read(this.module, (_, reader) => ReadModuleManifest(reader));
        }

        public override void ReadSymbols(ModuleDefinition module)
        {
        }
    }

    sealed class SignatureReader : ByteBuffer
    {
        readonly MetadataReader reader;
        readonly internal uint start, sig_length;

        TypeSystem TypeSystem
        {
            get { return reader.module.TypeSystem; }
        }

        public SignatureReader(uint blob, MetadataReader reader)
            : base(reader.image.BlobHeap.data)
        {
            this.reader = reader;
            this.position = (int)blob;
            this.sig_length = ReadCompressedUInt32();
            this.start = (uint)this.position;
        }

        MetadataToken ReadTypeTokenSignature()
        {
            return CodedIndex.TypeDefOrRef.GetMetadataToken(ReadCompressedUInt32());
        }

        GenericParameter GetGenericParameter(GenericParameterType type, uint var)
        {
            var context = reader.context;
            int index = (int)var;

            if (context == null)
                return GetUnboundGenericParameter(type, index);

            IGenericParameterProvider provider;

            switch (type)
            {
                case GenericParameterType.Type:
                    provider = context.Type;
                    break;
                case GenericParameterType.Method:
                    provider = context.Method;
                    break;
                default:
                    throw new NotSupportedException();
            }

            if (!context.IsDefinition)
                CheckGenericContext(provider, index);

            if (index >= provider.GenericParameters.Count)
                return GetUnboundGenericParameter(type, index);

            return provider.GenericParameters[index];
        }

        GenericParameter GetUnboundGenericParameter(GenericParameterType type, int index)
        {
            return new GenericParameter(index, type, reader.module);
        }

        static void CheckGenericContext(IGenericParameterProvider owner, int index)
        {
            var owner_parameters = owner.GenericParameters;

            for (int i = owner_parameters.Count; i <= index; i++)
                owner_parameters.Add(new GenericParameter(owner));
        }

        public void ReadGenericInstanceSignature(IGenericParameterProvider provider, IGenericInstance instance, uint arity)
        {
            if (!provider.IsDefinition)
                CheckGenericContext(provider, (int)arity - 1);

            var instance_arguments = instance.GenericArguments;

            for (int i = 0; i < arity; i++)
                instance_arguments.Add(ReadTypeSignature());
        }

        ArrayType ReadArrayTypeSignature()
        {
            var array = new ArrayType(ReadTypeSignature());

            var rank = ReadCompressedUInt32();

            var sizes = new uint[ReadCompressedUInt32()];
            for (int i = 0; i < sizes.Length; i++)
                sizes[i] = ReadCompressedUInt32();

            var low_bounds = new int[ReadCompressedUInt32()];
            for (int i = 0; i < low_bounds.Length; i++)
                low_bounds[i] = ReadCompressedInt32();

            array.Dimensions.Clear();

            for (int i = 0; i < rank; i++)
            {
                int? lower = null, upper = null;

                if (i < low_bounds.Length)
                    lower = low_bounds[i];

                if (i < sizes.Length)
                    upper = lower + (int)sizes[i] - 1;

                array.Dimensions.Add(new ArrayDimension(lower, upper));
            }

            return array;
        }

        TypeReference GetTypeDefOrRef(MetadataToken token)
        {
            return reader.GetTypeDefOrRef(token);
        }

        public TypeReference ReadTypeSignature()
        {
            return ReadTypeSignature((ElementType)ReadByte());
        }

        public TypeReference ReadTypeToken()
        {
            return GetTypeDefOrRef(ReadTypeTokenSignature());
        }

        TypeReference ReadTypeSignature(ElementType etype)
        {
            switch (etype)
            {
                case ElementType.ValueType:
                    {
                        var value_type = GetTypeDefOrRef(ReadTypeTokenSignature());
                        value_type.KnownValueType();
                        return value_type;
                    }
                case ElementType.Class:
                    return GetTypeDefOrRef(ReadTypeTokenSignature());
                case ElementType.Ptr:
                    return new PointerType(ReadTypeSignature());
                case ElementType.FnPtr:
                    {
                        var fptr = new FunctionPointerType();
                        ReadMethodSignature(fptr);
                        return fptr;
                    }
                case ElementType.ByRef:
                    return new ByReferenceType(ReadTypeSignature());
                case ElementType.Pinned:
                    return new PinnedType(ReadTypeSignature());
                case ElementType.SzArray:
                    return new ArrayType(ReadTypeSignature());
                case ElementType.Array:
                    return ReadArrayTypeSignature();
                case ElementType.CModOpt:
                    return new OptionalModifierType(
                        GetTypeDefOrRef(ReadTypeTokenSignature()), ReadTypeSignature());
                case ElementType.CModReqD:
                    return new RequiredModifierType(
                        GetTypeDefOrRef(ReadTypeTokenSignature()), ReadTypeSignature());
                case ElementType.Sentinel:
                    return new SentinelType(ReadTypeSignature());
                case ElementType.Var:
                    return GetGenericParameter(GenericParameterType.Type, ReadCompressedUInt32());
                case ElementType.MVar:
                    return GetGenericParameter(GenericParameterType.Method, ReadCompressedUInt32());
                case ElementType.GenericInst:
                    {
                        var is_value_type = ReadByte() == (byte)ElementType.ValueType;
                        var element_type = GetTypeDefOrRef(ReadTypeTokenSignature());

                        var arity = ReadCompressedUInt32();
                        var generic_instance = new GenericInstanceType(element_type, (int)arity);

                        ReadGenericInstanceSignature(element_type, generic_instance, arity);

                        if (is_value_type)
                        {
                            generic_instance.KnownValueType();
                            element_type.GetElementType().KnownValueType();
                        }

                        return generic_instance;
                    }
                case ElementType.Object: return TypeSystem.Object;
                case ElementType.Void: return TypeSystem.Void;
                case ElementType.TypedByRef: return TypeSystem.TypedReference;
                case ElementType.I: return TypeSystem.IntPtr;
                case ElementType.U: return TypeSystem.UIntPtr;
                default: return GetPrimitiveType(etype);
            }
        }

        public void ReadMethodSignature(IMethodSignature method)
        {
            var calling_convention = ReadByte();

            const byte has_this = 0x20;
            const byte explicit_this = 0x40;

            if ((calling_convention & has_this) != 0)
            {
                method.HasThis = true;
                calling_convention = (byte)(calling_convention & ~has_this);
            }

            if ((calling_convention & explicit_this) != 0)
            {
                method.ExplicitThis = true;
                calling_convention = (byte)(calling_convention & ~explicit_this);
            }

            method.CallingConvention = (MethodCallingConvention)calling_convention;

            var generic_context = method as MethodReference;
            if (generic_context != null && !generic_context.DeclaringType.IsArray)
                reader.context = generic_context;

            if ((calling_convention & 0x10) != 0)
            {
                var arity = ReadCompressedUInt32();

                if (generic_context != null && !generic_context.IsDefinition)
                    CheckGenericContext(generic_context, (int)arity -1);
            }

            var param_count = ReadCompressedUInt32();

            method.MethodReturnType.ReturnType = ReadTypeSignature();

            if (param_count == 0)
                return;

            Collection<ParameterDefinition> parameters;

            var method_ref = method as MethodReference;
            if (method_ref != null)
                parameters = method_ref.parameters = new ParameterDefinitionCollection(method, (int)param_count);
            else
                parameters = method.Parameters;

            for (int i = 0; i < param_count; i++)
                parameters.Add(new ParameterDefinition(ReadTypeSignature()));
        }

        public object ReadConstantSignature(ElementType type)
        {
            return ReadPrimitiveValue(type);
        }

        public void ReadCustomAttributeConstructorArguments(CustomAttribute attribute, Collection<ParameterDefinition> parameters)
        {
            var count = parameters.Count;
            if (count == 0)
                return;

            attribute.arguments = new Collection<CustomAttributeArgument>(count);

            for (int i = 0; i < count; i++)
                attribute.arguments.Add(
                    ReadCustomAttributeFixedArgument(parameters[i].ParameterType));
        }

        CustomAttributeArgument ReadCustomAttributeFixedArgument(TypeReference type)
        {
            if (type.IsArray)
                return ReadCustomAttributeFixedArrayArgument((ArrayType)type);

            return ReadCustomAttributeElement(type);
        }

        public void ReadCustomAttributeNamedArguments(ushort count, ref Collection<CustomAttributeNamedArgument> fields, ref Collection<CustomAttributeNamedArgument> properties)
        {
            for (int i = 0; i < count; i++)
            {
                if (!CanReadMore())
                    return;
                ReadCustomAttributeNamedArgument(ref fields, ref properties);
            }
        }

        void ReadCustomAttributeNamedArgument(ref Collection<CustomAttributeNamedArgument> fields, ref Collection<CustomAttributeNamedArgument> properties)
        {
            var kind = ReadByte();
            var type = ReadCustomAttributeFieldOrPropType();
            var name = ReadUTF8String();

            Collection<CustomAttributeNamedArgument> container;
            switch (kind)
            {
                case 0x53:
                    container = GetCustomAttributeNamedArgumentCollection(ref fields);
                    break;
                case 0x54:
                    container = GetCustomAttributeNamedArgumentCollection(ref properties);
                    break;
                default:
                    throw new NotSupportedException();
            }

            container.Add(new CustomAttributeNamedArgument(name, ReadCustomAttributeFixedArgument(type)));
        }

        static Collection<CustomAttributeNamedArgument> GetCustomAttributeNamedArgumentCollection(ref Collection<CustomAttributeNamedArgument> collection)
        {
            if (collection != null)
                return collection;

            return collection = new Collection<CustomAttributeNamedArgument>();
        }

        CustomAttributeArgument ReadCustomAttributeFixedArrayArgument(ArrayType type)
        {
            var length = ReadUInt32();

            if (length == 0xffffffff)
                return new CustomAttributeArgument(type, null);

            if (length == 0)
                return new CustomAttributeArgument(type, Array.Empty<CustomAttributeArgument>());

            var arguments = new CustomAttributeArgument[length];
            var element_type = type.ElementType;

            for (int i = 0; i < length; i++)
                arguments[i] = ReadCustomAttributeElement(element_type);

            return new CustomAttributeArgument(type, arguments);
        }

        CustomAttributeArgument ReadCustomAttributeElement(TypeReference type)
        {
            if (type.IsArray)
                return ReadCustomAttributeFixedArrayArgument((ArrayType)type);

            return new CustomAttributeArgument(
                type,
                type.etype == ElementType.Object
                    ? ReadCustomAttributeElement(ReadCustomAttributeFieldOrPropType())
                    : ReadCustomAttributeElementValue(type));
        }

        object ReadCustomAttributeElementValue(TypeReference type)
        {
            var etype = type.etype;

            switch (etype)
            {
                case ElementType.String:
                    return ReadUTF8String();
                case ElementType.None:
                    if (type.IsTypeOf("System", "Type"))
                        return ReadTypeReference();

                    return ReadCustomAttributeEnum(type);
                default:
                    return ReadPrimitiveValue(etype);
            }
        }

        object ReadPrimitiveValue(ElementType type)
        {
            switch (type)
            {
                case ElementType.Boolean:
                    return ReadByte() == 1;
                case ElementType.I1:
                    return (sbyte)ReadByte();
                case ElementType.U1:
                    return ReadByte();
                case ElementType.Char:
                    return (char)ReadUInt16();
                case ElementType.I2:
                    return ReadInt16();
                case ElementType.U2:
                    return ReadUInt16();
                case ElementType.I4:
                    return ReadInt32();
                case ElementType.U4:
                    return ReadUInt32();
                case ElementType.I8:
                    return ReadInt64();
                case ElementType.U8:
                    return ReadUInt64();
                case ElementType.R4:
                    return ReadSingle();
                case ElementType.R8:
                    return ReadDouble();
                default:
                    throw new NotImplementedException(type.ToString());
            }
        }

        TypeReference GetPrimitiveType(ElementType etype)
        {
            switch (etype)
            {
                case ElementType.Boolean:
                    return TypeSystem.Boolean;
                case ElementType.Char:
                    return TypeSystem.Char;
                case ElementType.I1:
                    return TypeSystem.SByte;
                case ElementType.U1:
                    return TypeSystem.Byte;
                case ElementType.I2:
                    return TypeSystem.Int16;
                case ElementType.U2:
                    return TypeSystem.UInt16;
                case ElementType.I4:
                    return TypeSystem.Int32;
                case ElementType.U4:
                    return TypeSystem.UInt32;
                case ElementType.I8:
                    return TypeSystem.Int64;
                case ElementType.U8:
                    return TypeSystem.UInt64;
                case ElementType.R4:
                    return TypeSystem.Single;
                case ElementType.R8:
                    return TypeSystem.Double;
                case ElementType.String:
                    return TypeSystem.String;
                default:
                    throw new NotImplementedException(etype.ToString());
            }
        }

        TypeReference ReadCustomAttributeFieldOrPropType()
        {
            var etype = (ElementType)ReadByte();

            switch (etype)
            {
                case ElementType.Boxed:
                    return TypeSystem.Object;
                case ElementType.SzArray:
                    return new ArrayType(ReadCustomAttributeFieldOrPropType());
                case ElementType.Enum:
                    return ReadTypeReference();
                case ElementType.Type:
                    return TypeSystem.LookupType("System", "Type");
                default:
                    return GetPrimitiveType(etype);
            }
        }

        public TypeReference ReadTypeReference()
        {
            return TypeParser.ParseType(reader.module, ReadUTF8String());
        }

        object ReadCustomAttributeEnum(TypeReference enum_type)
        {
            var type = enum_type.CheckedResolve();
            if (!type.IsEnum)
                throw new ArgumentException();

            return ReadCustomAttributeElementValue(type.GetEnumUnderlyingType());
        }

        public SecurityAttribute ReadSecurityAttribute()
        {
            var attribute = new SecurityAttribute(ReadTypeReference());

            ReadCompressedUInt32();

            ReadCustomAttributeNamedArguments(
                (ushort)ReadCompressedUInt32(),
                ref attribute.fields,
                ref attribute.properties);

            return attribute;
        }

        public MarshalInfo ReadMarshalInfo()
        {
            var native = ReadNativeType();
            switch (native)
            {
                case NativeType.Array:
                    {
                        var array = new ArrayMarshalInfo();
                        if (CanReadMore())
                            array.element_type = ReadNativeType();
                        if (CanReadMore())
                            array.size_parameter_index = (int)ReadCompressedUInt32();
                        if (CanReadMore())
                            array.size = (int)ReadCompressedUInt32();
                        if (CanReadMore())
                            array.size_parameter_multiplier = (int)ReadCompressedUInt32();
                        return array;
                    }
                case NativeType.SafeArray:
                    {
                        var array = new SafeArrayMarshalInfo();
                        if (CanReadMore())
                            array.element_type = ReadVariantType();
                        return array;
                    }
                case NativeType.FixedArray:
                    {
                        var array = new FixedArrayMarshalInfo();
                        if (CanReadMore())
                            array.size = (int)ReadCompressedUInt32();
                        if (CanReadMore())
                            array.element_type = ReadNativeType();
                        return array;
                    }
                case NativeType.FixedSysString:
                    {
                        var sys_string = new FixedSysStringMarshalInfo();
                        if (CanReadMore())
                            sys_string.size = (int)ReadCompressedUInt32();
                        return sys_string;
                    }
                case NativeType.CustomMarshaler:
                    {
                        var marshaler = new CustomMarshalInfo();
                        var guid_value = ReadUTF8String();
                        marshaler.guid = !string.IsNullOrEmpty(guid_value) ? new Guid(guid_value) : Guid.Empty;
                        marshaler.unmanaged_type = ReadUTF8String();
                        marshaler.managed_type = ReadTypeReference();
                        marshaler.cookie = ReadUTF8String();
                        return marshaler;
                    }
                default:
                    return new MarshalInfo(native);
            }
        }

        NativeType ReadNativeType()
        {
            return (NativeType)ReadByte();
        }

        VariantType ReadVariantType()
        {
            return (VariantType)ReadByte();
        }

        string ReadUTF8String()
        {
            if (buffer[position] == 0xff)
            {
                position++;
                return null;
            }

            var length = (int)ReadCompressedUInt32();
            if (length == 0)
                return string.Empty;

            if (position + length > buffer.Length)
                return string.Empty;

            var @string = Encoding.UTF8.GetString(buffer, position, length);

            position += length;
            return @string;
        }

        public string ReadDocumentName()
        {
            var separator = (char)buffer[position];
            position++;

            var builder = new StringBuilder();
            for (int i = 0; CanReadMore(); i++)
            {
                if (i > 0 && separator != 0)
                    builder.Append(separator);

                uint part = ReadCompressedUInt32();
                if (part != 0)
                    builder.Append(reader.ReadUTF8StringBlob(part));
            }

            return builder.ToString();
        }

        public Collection<SequencePoint> ReadSequencePoints(Document document)
        {
            ReadCompressedUInt32(); // local_sig_token

            if (document == null)
                document = reader.GetDocument(ReadCompressedUInt32());

            var offset = 0;
            var start_line = 0;
            var start_column = 0;
            var first_non_hidden = true;

            //there's about 5 compressed int32's per sequenec points.  we don't know exactly how many
            //but let's take a conservative guess so we dont end up reallocating the sequence_points collection
            //as it grows.
            var bytes_remaining_for_sequencepoints = sig_length - (position - start);
            var estimated_sequencepoint_amount = (int)bytes_remaining_for_sequencepoints / 5;
            var sequence_points = new Collection<SequencePoint>(estimated_sequencepoint_amount);

            for (var i = 0; CanReadMore(); i++)
            {
                var delta_il = (int)ReadCompressedUInt32();
                if (i > 0 && delta_il == 0)
                {
                    document = reader.GetDocument(ReadCompressedUInt32());
                    continue;
                }

                offset += delta_il;

                var delta_lines = (int)ReadCompressedUInt32();
                var delta_columns = delta_lines == 0
                    ? (int)ReadCompressedUInt32()
                    : ReadCompressedInt32();

                if (delta_lines == 0 && delta_columns == 0)
                {
                    sequence_points.Add(new SequencePoint(offset, document)
                    {
                        StartLine = 0xfeefee,
                        EndLine = 0xfeefee,
                        StartColumn = 0,
                        EndColumn = 0,
                    });
                    continue;
                }

                if (first_non_hidden)
                {
                    start_line = (int)ReadCompressedUInt32();
                    start_column = (int)ReadCompressedUInt32();
                }
                else
                {
                    start_line += ReadCompressedInt32();
                    start_column += ReadCompressedInt32();
                }

                sequence_points.Add(new SequencePoint(offset, document)
                {
                    StartLine = start_line,
                    StartColumn = start_column,
                    EndLine = start_line + delta_lines,
                    EndColumn = start_column + delta_columns,
                });
                first_non_hidden = false;
            }

            return sequence_points;
        }

        public bool CanReadMore()
        {
            return (position - start) < sig_length;
        }
    }
}
