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
using System.Text;
using BlobIndex = System.UInt32;
using CodedRID = System.UInt32;
using GuidIndex = System.UInt32;
using RID = System.UInt32;
using RVA = System.UInt32;
using StringIndex = System.UInt32;

namespace Cecilia
{
    sealed class SignatureWriter : ByteBuffer
    {
        readonly MetadataBuilder metadata;

        public SignatureWriter(MetadataBuilder metadata)
            : base(6)
        {
            this.metadata = metadata;
        }

        public void WriteElementType(ElementType element_type)
        {
            WriteByte((byte)element_type);
        }

        public void WriteUTF8String(string @string)
        {
            if (@string == null)
            {
                WriteByte(0xff);
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(@string);
            WriteCompressedUInt32((uint)bytes.Length);
            WriteBytes(bytes);
        }

        public void WriteMethodSignature(IMethodSignature method)
        {
            byte calling_convention = (byte)method.CallingConvention;
            if (method.HasThis)
                calling_convention |= 0x20;
            if (method.ExplicitThis)
                calling_convention |= 0x40;

            var generic_provider = method as IGenericParameterProvider;
            var generic_arity = generic_provider != null && generic_provider.HasGenericParameters
                ? generic_provider.GenericParameters.Count
                : 0;

            if (generic_arity > 0)
                calling_convention |= 0x10;

            var param_count = method.HasParameters ? method.Parameters.Count : 0;

            WriteByte(calling_convention);

            if (generic_arity > 0)
                WriteCompressedUInt32((uint)generic_arity);

            WriteCompressedUInt32((uint)param_count);
            WriteTypeSignature(method.ReturnType);

            if (param_count == 0)
                return;

            var parameters = method.Parameters;

            for (int i = 0; i < param_count; i++)
                WriteTypeSignature(parameters[i].ParameterType);
        }

        uint MakeTypeDefOrRefCodedRID(TypeReference type)
        {
            return CodedIndex.TypeDefOrRef.CompressMetadataToken(metadata.LookupToken(type));
        }

        public void WriteTypeToken(TypeReference type)
        {
            WriteCompressedUInt32(MakeTypeDefOrRefCodedRID(type));
        }

        public void WriteTypeSignature(TypeReference type)
        {
            Mixin.CheckNotNull(type);

            var etype = type.etype;

            switch (etype)
            {
                case ElementType.MVar:
                case ElementType.Var:
                    {
                        var generic_parameter = (GenericParameter)type;

                        WriteElementType(etype);
                        var position = generic_parameter.Position;
                        if (position == -1)
                            throw new NotSupportedException();

                        WriteCompressedUInt32((uint)position);
                        break;
                    }

                case ElementType.GenericInst:
                    {
                        var generic_instance = (GenericInstanceType)type;
                        WriteElementType(ElementType.GenericInst);
                        WriteElementType(generic_instance.IsValueType ? ElementType.ValueType : ElementType.Class);
                        WriteCompressedUInt32(MakeTypeDefOrRefCodedRID(generic_instance.ElementType));

                        WriteGenericInstanceSignature(generic_instance);
                        break;
                    }

                case ElementType.Ptr:
                case ElementType.ByRef:
                case ElementType.Pinned:
                case ElementType.Sentinel:
                    {
                        var type_spec = (TypeSpecification)type;
                        WriteElementType(etype);
                        WriteTypeSignature(type_spec.ElementType);
                        break;
                    }

                case ElementType.FnPtr:
                    {
                        var fptr = (FunctionPointerType)type;
                        WriteElementType(ElementType.FnPtr);
                        WriteMethodSignature(fptr);
                        break;
                    }

                case ElementType.CModOpt:
                case ElementType.CModReqD:
                    {
                        var modifier = (IModifierType)type;
                        WriteModifierSignature(etype, modifier);
                        break;
                    }

                case ElementType.Array:
                    {
                        var array = (ArrayType)type;
                        if (!array.IsVector)
                        {
                            WriteArrayTypeSignature(array);
                            break;
                        }

                        WriteElementType(ElementType.SzArray);
                        WriteTypeSignature(array.ElementType);
                        break;
                    }

                case ElementType.None:
                    {
                        WriteElementType(type.IsValueType ? ElementType.ValueType : ElementType.Class);
                        WriteCompressedUInt32(MakeTypeDefOrRefCodedRID(type));
                        break;
                    }

                default:
                    if (!TryWriteElementType(type))
                        throw new NotSupportedException();

                    break;

            }
        }

        void WriteArrayTypeSignature(ArrayType array)
        {
            WriteElementType(ElementType.Array);
            WriteTypeSignature(array.ElementType);

            var dimensions = array.Dimensions;
            var rank = dimensions.Count;

            WriteCompressedUInt32((uint)rank);

            var sized = 0;
            var lbounds = 0;

            for (int i = 0; i < rank; i++)
            {
                var dimension = dimensions[i];

                if (dimension.UpperBound.HasValue)
                {
                    sized++;
                    lbounds++;
                }
                else if (dimension.LowerBound.HasValue)
                    lbounds++;
            }

            var sizes = new int[sized];
            var low_bounds = new int[lbounds];

            for (int i = 0; i < lbounds; i++)
            {
                var dimension = dimensions[i];
                low_bounds[i] = dimension.LowerBound.GetValueOrDefault();
                if (dimension.UpperBound.HasValue)
                    sizes[i] = dimension.UpperBound.Value - low_bounds[i] + 1;
            }

            WriteCompressedUInt32((uint)sized);
            for (int i = 0; i < sized; i++)
                WriteCompressedUInt32((uint)sizes[i]);

            WriteCompressedUInt32((uint)lbounds);
            for (int i = 0; i < lbounds; i++)
                WriteCompressedInt32(low_bounds[i]);
        }

        public void WriteGenericInstanceSignature(IGenericInstance instance)
        {
            var generic_arguments = instance.GenericArguments;
            var arity = generic_arguments.Count;

            WriteCompressedUInt32((uint)arity);
            for (int i = 0; i < arity; i++)
                WriteTypeSignature(generic_arguments[i]);
        }

        void WriteModifierSignature(ElementType element_type, IModifierType type)
        {
            WriteElementType(element_type);
            WriteCompressedUInt32(MakeTypeDefOrRefCodedRID(type.ModifierType));
            WriteTypeSignature(type.ElementType);
        }

        bool TryWriteElementType(TypeReference type)
        {
            var element = type.etype;

            if (element == ElementType.None)
                return false;

            WriteElementType(element);
            return true;
        }

        public void WriteConstantString(string value)
        {
            if (value != null)
                WriteBytes(Encoding.Unicode.GetBytes(value));
            else
                WriteByte(0xff);
        }

        public void WriteConstantPrimitive(object value)
        {
            WritePrimitiveValue(value);
        }

        public void WriteCustomAttributeConstructorArguments(CustomAttribute attribute)
        {
            if (!attribute.HasConstructorArguments)
                return;

            var arguments = attribute.ConstructorArguments;
            var parameters = attribute.Constructor.Parameters;

            if (parameters.Count != arguments.Count)
                throw new InvalidOperationException();

            for (int i = 0; i < arguments.Count; i++)
                WriteCustomAttributeFixedArgument(parameters[i].ParameterType, arguments[i]);
        }

        void WriteCustomAttributeFixedArgument(TypeReference type, CustomAttributeArgument argument)
        {
            if (type.IsArray)
            {
                WriteCustomAttributeFixedArrayArgument((ArrayType)type, argument);
                return;
            }

            WriteCustomAttributeElement(type, argument);
        }

        void WriteCustomAttributeFixedArrayArgument(ArrayType type, CustomAttributeArgument argument)
        {
            var values = argument.Value as CustomAttributeArgument[];

            if (values == null)
            {
                WriteUInt32(0xffffffff);
                return;
            }

            WriteInt32(values.Length);

            if (values.Length == 0)
                return;

            var element_type = type.ElementType;

            for (int i = 0; i < values.Length; i++)
                WriteCustomAttributeElement(element_type, values[i]);
        }

        void WriteCustomAttributeElement(TypeReference type, CustomAttributeArgument argument)
        {
            if (type.IsArray)
            {
                WriteCustomAttributeFixedArrayArgument((ArrayType)type, argument);
                return;
            }

            if (type.etype == ElementType.Object)
            {
                argument = (CustomAttributeArgument)argument.Value;
                type = argument.Type;

                WriteCustomAttributeFieldOrPropType(type);
                WriteCustomAttributeElement(type, argument);
                return;
            }

            WriteCustomAttributeValue(type, argument.Value);
        }

        void WriteCustomAttributeValue(TypeReference type, object value)
        {
            var etype = type.etype;

            switch (etype)
            {
                case ElementType.String:
                    var @string = (string)value;
                    if (@string == null)
                        WriteByte(0xff);
                    else
                        WriteUTF8String(@string);
                    break;
                case ElementType.None:
                    if (type.IsTypeOf("System", "Type"))
                        WriteCustomAttributeTypeValue((TypeReference)value);
                    else
                        WriteCustomAttributeEnumValue(type, value);
                    break;
                default:
                    WritePrimitiveValue(value);
                    break;
            }
        }

        private void WriteCustomAttributeTypeValue(TypeReference value)
        {
            var typeDefinition = value as TypeDefinition;

            if (typeDefinition != null)
            {
                TypeDefinition outermostDeclaringType = typeDefinition;
                while (outermostDeclaringType.DeclaringType != null)
                    outermostDeclaringType = outermostDeclaringType.DeclaringType;

                // In CLR .winmd files, custom attribute arguments reference unmangled type names (rather than <CLR>Name)
                if (WindowsRuntimeProjections.IsClrImplementationType(outermostDeclaringType))
                {
                    WindowsRuntimeProjections.Project(outermostDeclaringType);
                    WriteTypeReference(value);
                    WindowsRuntimeProjections.RemoveProjection(outermostDeclaringType);
                    return;
                }
            }

            WriteTypeReference(value);
        }

        void WritePrimitiveValue(object value)
        {
            Mixin.CheckNotNull(value);

            switch (Type.GetTypeCode(value.GetType()))
            {
                case TypeCode.Boolean:
                    WriteByte((byte)(((bool)value) ? 1 : 0));
                    break;
                case TypeCode.Byte:
                    WriteByte((byte)value);
                    break;
                case TypeCode.SByte:
                    WriteSByte((sbyte)value);
                    break;
                case TypeCode.Int16:
                    WriteInt16((short)value);
                    break;
                case TypeCode.UInt16:
                    WriteUInt16((ushort)value);
                    break;
                case TypeCode.Char:
                    WriteInt16((short)(char)value);
                    break;
                case TypeCode.Int32:
                    WriteInt32((int)value);
                    break;
                case TypeCode.UInt32:
                    WriteUInt32((uint)value);
                    break;
                case TypeCode.Single:
                    WriteSingle((float)value);
                    break;
                case TypeCode.Int64:
                    WriteInt64((long)value);
                    break;
                case TypeCode.UInt64:
                    WriteUInt64((ulong)value);
                    break;
                case TypeCode.Double:
                    WriteDouble((double)value);
                    break;
                default:
                    throw new NotSupportedException(value.GetType().FullName);
            }
        }

        void WriteCustomAttributeEnumValue(TypeReference enum_type, object value)
        {
            var type = enum_type.CheckedResolve();
            if (!type.IsEnum)
                throw new ArgumentException();

            WriteCustomAttributeValue(type.GetEnumUnderlyingType(), value);
        }

        void WriteCustomAttributeFieldOrPropType(TypeReference type)
        {
            if (type.IsArray)
            {
                var array = (ArrayType)type;
                WriteElementType(ElementType.SzArray);
                WriteCustomAttributeFieldOrPropType(array.ElementType);
                return;
            }

            var etype = type.etype;

            switch (etype)
            {
                case ElementType.Object:
                    WriteElementType(ElementType.Boxed);
                    return;
                case ElementType.None:
                    if (type.IsTypeOf("System", "Type"))
                        WriteElementType(ElementType.Type);
                    else
                    {
                        WriteElementType(ElementType.Enum);
                        WriteTypeReference(type);
                    }
                    return;
                default:
                    WriteElementType(etype);
                    return;
            }
        }

        public void WriteCustomAttributeNamedArguments(CustomAttribute attribute)
        {
            var count = GetNamedArgumentCount(attribute);

            WriteUInt16((ushort)count);

            if (count == 0)
                return;

            WriteICustomAttributeNamedArguments(attribute);
        }

        static int GetNamedArgumentCount(ICustomAttribute attribute)
        {
            int count = 0;

            if (attribute.HasFields)
                count += attribute.Fields.Count;

            if (attribute.HasProperties)
                count += attribute.Properties.Count;

            return count;
        }

        void WriteICustomAttributeNamedArguments(ICustomAttribute attribute)
        {
            if (attribute.HasFields)
                WriteCustomAttributeNamedArguments(0x53, attribute.Fields);

            if (attribute.HasProperties)
                WriteCustomAttributeNamedArguments(0x54, attribute.Properties);
        }

        void WriteCustomAttributeNamedArguments(byte kind, Collection<CustomAttributeNamedArgument> named_arguments)
        {
            for (int i = 0; i < named_arguments.Count; i++)
                WriteCustomAttributeNamedArgument(kind, named_arguments[i]);
        }

        void WriteCustomAttributeNamedArgument(byte kind, CustomAttributeNamedArgument named_argument)
        {
            var argument = named_argument.Argument;

            WriteByte(kind);
            WriteCustomAttributeFieldOrPropType(argument.Type);
            WriteUTF8String(named_argument.Name);
            WriteCustomAttributeFixedArgument(argument.Type, argument);
        }

        void WriteSecurityAttribute(SecurityAttribute attribute)
        {
            WriteTypeReference(attribute.AttributeType);

            var count = GetNamedArgumentCount(attribute);

            if (count == 0)
            {
                WriteCompressedUInt32(1); // length
                WriteCompressedUInt32(0); // count
                return;
            }

            var buffer = new SignatureWriter(metadata);
            buffer.WriteCompressedUInt32((uint)count);
            buffer.WriteICustomAttributeNamedArguments(attribute);

            WriteCompressedUInt32((uint)buffer.length);
            WriteBytes(buffer);
        }

        public void WriteSecurityDeclaration(SecurityDeclaration declaration)
        {
            WriteByte((byte)'.');

            var attributes = declaration.security_attributes;
            if (attributes == null)
                throw new NotSupportedException();

            WriteCompressedUInt32((uint)attributes.Count);

            for (int i = 0; i < attributes.Count; i++)
                WriteSecurityAttribute(attributes[i]);
        }

        public void WriteXmlSecurityDeclaration(SecurityDeclaration declaration)
        {
            var xml = GetXmlSecurityDeclaration(declaration);
            if (xml == null)
                throw new NotSupportedException();

            WriteBytes(Encoding.Unicode.GetBytes(xml));
        }

        static string GetXmlSecurityDeclaration(SecurityDeclaration declaration)
        {
            if (declaration.security_attributes == null || declaration.security_attributes.Count != 1)
                return null;

            var attribute = declaration.security_attributes[0];

            if (!attribute.AttributeType.IsTypeOf("System.Security.Permissions", "PermissionSetAttribute"))
                return null;

            if (attribute.properties == null || attribute.properties.Count != 1)
                return null;

            var property = attribute.properties[0];
            if (property.Name != "XML")
                return null;

            return (string)property.Argument.Value;
        }

        void WriteTypeReference(TypeReference type)
        {
            WriteUTF8String(TypeParser.ToParseable(type, top_level: false));
        }

        public void WriteMarshalInfo(MarshalInfo marshal_info)
        {
            WriteNativeType(marshal_info.native);

            switch (marshal_info.native)
            {
                case NativeType.Array:
                    {
                        var array = (ArrayMarshalInfo)marshal_info;
                        if (array.element_type != NativeType.None)
                            WriteNativeType(array.element_type);
                        if (array.size_parameter_index > -1)
                            WriteCompressedUInt32((uint)array.size_parameter_index);
                        if (array.size > -1)
                            WriteCompressedUInt32((uint)array.size);
                        if (array.size_parameter_multiplier > -1)
                            WriteCompressedUInt32((uint)array.size_parameter_multiplier);
                        return;
                    }
                case NativeType.SafeArray:
                    {
                        var array = (SafeArrayMarshalInfo)marshal_info;
                        if (array.element_type != VariantType.None)
                            WriteVariantType(array.element_type);
                        return;
                    }
                case NativeType.FixedArray:
                    {
                        var array = (FixedArrayMarshalInfo)marshal_info;
                        if (array.size > -1)
                            WriteCompressedUInt32((uint)array.size);
                        if (array.element_type != NativeType.None)
                            WriteNativeType(array.element_type);
                        return;
                    }
                case NativeType.FixedSysString:
                    var sys_string = (FixedSysStringMarshalInfo)marshal_info;
                    if (sys_string.size > -1)
                        WriteCompressedUInt32((uint)sys_string.size);
                    return;
                case NativeType.CustomMarshaler:
                    var marshaler = (CustomMarshalInfo)marshal_info;
                    WriteUTF8String(marshaler.guid != Guid.Empty ? marshaler.guid.ToString() : string.Empty);
                    WriteUTF8String(marshaler.unmanaged_type);
                    WriteTypeReference(marshaler.managed_type);
                    WriteUTF8String(marshaler.cookie);
                    return;
            }
        }

        void WriteNativeType(NativeType native)
        {
            WriteByte((byte)native);
        }

        void WriteVariantType(VariantType variant)
        {
            WriteByte((byte)variant);
        }

        public void WriteSequencePoints(MethodDebugInformation info)
        {
            var start_line = -1;
            var start_column = -1;

            WriteCompressedUInt32(info.local_var_token.RID);

            Document previous_document;
            if (!info.TryGetUniqueDocument(out previous_document))
                previous_document = null;

            for (int i = 0; i < info.SequencePoints.Count; i++)
            {
                var sequence_point = info.SequencePoints[i];

                var document = sequence_point.Document;
                if (previous_document != document)
                {
                    var document_token = metadata.GetDocumentToken(document);

                    if (previous_document != null)
                        WriteCompressedUInt32(0);

                    WriteCompressedUInt32(document_token.RID);
                    previous_document = document;
                }

                if (i > 0)
                    WriteCompressedUInt32((uint)(sequence_point.Offset - info.SequencePoints[i - 1].Offset));
                else
                    WriteCompressedUInt32((uint)sequence_point.Offset);

                if (sequence_point.IsHidden)
                {
                    WriteInt16(0);
                    continue;
                }

                var delta_lines = sequence_point.EndLine - sequence_point.StartLine;
                var delta_columns = sequence_point.EndColumn - sequence_point.StartColumn;

                WriteCompressedUInt32((uint)delta_lines);

                if (delta_lines == 0)
                    WriteCompressedUInt32((uint)delta_columns);
                else
                    WriteCompressedInt32(delta_columns);

                if (start_line < 0)
                {
                    WriteCompressedUInt32((uint)sequence_point.StartLine);
                    WriteCompressedUInt32((uint)sequence_point.StartColumn);
                }
                else
                {
                    WriteCompressedInt32(sequence_point.StartLine - start_line);
                    WriteCompressedInt32(sequence_point.StartColumn - start_column);
                }

                start_line = sequence_point.StartLine;
                start_column = sequence_point.StartColumn;
            }
        }
    }
}
