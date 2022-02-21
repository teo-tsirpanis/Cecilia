//
// Author:
//   Jb Evain (jbevain@gmail.com)
//
// Copyright (c) 2008 - 2015 Jb Evain
// Copyright (c) 2008 - 2011 Novell, Inc.
//
// Licensed under the MIT/X11 license.
//

using Cecilia.Metadata;
using Mono.Collections.Generic;
using System;
using System.Threading;

namespace Cecilia
{

    public enum MetadataType : byte
    {
        Void = ElementType.Void,
        Boolean = ElementType.Boolean,
        Char = ElementType.Char,
        SByte = ElementType.I1,
        Byte = ElementType.U1,
        Int16 = ElementType.I2,
        UInt16 = ElementType.U2,
        Int32 = ElementType.I4,
        UInt32 = ElementType.U4,
        Int64 = ElementType.I8,
        UInt64 = ElementType.U8,
        Single = ElementType.R4,
        Double = ElementType.R8,
        String = ElementType.String,
        Pointer = ElementType.Ptr,
        ByReference = ElementType.ByRef,
        ValueType = ElementType.ValueType,
        Class = ElementType.Class,
        Var = ElementType.Var,
        Array = ElementType.Array,
        GenericInstance = ElementType.GenericInst,
        TypedByReference = ElementType.TypedByRef,
        IntPtr = ElementType.I,
        UIntPtr = ElementType.U,
        FunctionPointer = ElementType.FnPtr,
        Object = ElementType.Object,
        MVar = ElementType.MVar,
        RequiredModifier = ElementType.CModReqD,
        OptionalModifier = ElementType.CModOpt,
        Sentinel = ElementType.Sentinel,
        Pinned = ElementType.Pinned,
    }

    public class TypeReference : MemberReference, IGenericParameterProvider, IGenericContext
    {

        string @namespace;
        bool value_type;
        internal IMetadataScope scope;
        internal ModuleDefinition module;

        internal ElementType etype = ElementType.None;

        string fullname;

        protected Collection<GenericParameter> generic_parameters;

        public override string Name
        {
            get { return base.Name; }
            set
            {
                if (IsWindowsRuntimeProjection && value != base.Name)
                    throw new InvalidOperationException("Projected type reference name can't be changed.");
                base.Name = value;
                ClearFullName();
            }
        }

        public virtual string Namespace
        {
            get { return @namespace; }
            set
            {
                if (IsWindowsRuntimeProjection && value != @namespace)
                    throw new InvalidOperationException("Projected type reference namespace can't be changed.");
                @namespace = value;
                ClearFullName();
            }
        }

        public virtual bool IsValueType
        {
            get { return value_type; }
            set { value_type = value; }
        }

        public override ModuleDefinition Module
        {
            get
            {
                if (module != null)
                    return module;

                var declaring_type = this.DeclaringType;
                if (declaring_type != null)
                    return declaring_type.Module;

                return null;
            }
        }

        internal TypeReferenceProjection WindowsRuntimeProjection
        {
            get { return (TypeReferenceProjection)projection; }
            set { projection = value; }
        }

        IGenericParameterProvider IGenericContext.Type
        {
            get { return this; }
        }

        IGenericParameterProvider IGenericContext.Method
        {
            get { return null; }
        }

        GenericParameterType IGenericParameterProvider.GenericParameterType
        {
            get { return GenericParameterType.Type; }
        }

        public virtual bool HasGenericParameters
        {
            get { return !generic_parameters.IsNullOrEmpty(); }
        }

        public virtual Collection<GenericParameter> GenericParameters
        {
            get
            {
                if (generic_parameters == null)
                    Interlocked.CompareExchange(ref generic_parameters, new GenericParameterCollection(this), null);

                return generic_parameters;
            }
        }

        public virtual IMetadataScope Scope
        {
            get
            {
                var declaring_type = this.DeclaringType;
                if (declaring_type != null)
                    return declaring_type.Scope;

                return scope;
            }
            set
            {
                var declaring_type = this.DeclaringType;
                if (declaring_type != null)
                {
                    if (IsWindowsRuntimeProjection && value != declaring_type.Scope)
                        throw new InvalidOperationException("Projected type scope can't be changed.");
                    declaring_type.Scope = value;
                    return;
                }

                if (IsWindowsRuntimeProjection && value != scope)
                    throw new InvalidOperationException("Projected type scope can't be changed.");
                scope = value;
            }
        }

        public bool IsNested
        {
            get { return this.DeclaringType != null; }
        }

        public override TypeReference DeclaringType
        {
            get { return base.DeclaringType; }
            set
            {
                if (IsWindowsRuntimeProjection && value != base.DeclaringType)
                    throw new InvalidOperationException("Projected type declaring type can't be changed.");
                base.DeclaringType = value;
                ClearFullName();
            }
        }

        public override string FullName
        {
            get
            {
                if (fullname != null)
                    return fullname;

                var new_fullname = this.TypeFullName;

                if (IsNested)
                    new_fullname = DeclaringType.FullName + "/" + new_fullname;
                Interlocked.CompareExchange(ref fullname, new_fullname, null);
                return fullname;
            }
        }

        public virtual bool IsByReference
        {
            get { return false; }
        }

        public virtual bool IsPointer
        {
            get { return false; }
        }

        public virtual bool IsSentinel
        {
            get { return false; }
        }

        public virtual bool IsArray
        {
            get { return false; }
        }

        public virtual bool IsGenericParameter
        {
            get { return false; }
        }

        public virtual bool IsGenericInstance
        {
            get { return false; }
        }

        public virtual bool IsRequiredModifier
        {
            get { return false; }
        }

        public virtual bool IsOptionalModifier
        {
            get { return false; }
        }

        public virtual bool IsPinned
        {
            get { return false; }
        }

        public virtual bool IsFunctionPointer
        {
            get { return false; }
        }

        public virtual bool IsPrimitive
        {
            get { return etype.IsPrimitive(); }
        }

        public virtual MetadataType MetadataType
        {
            get
            {
                switch (etype)
                {
                    case ElementType.None:
                        return IsValueType ? MetadataType.ValueType : MetadataType.Class;
                    default:
                        return (MetadataType)etype;
                }
            }
        }

        protected TypeReference(string @namespace, string name)
            : base(name)
        {
            this.@namespace = @namespace ?? string.Empty;
            this.token = new MetadataToken(TokenType.TypeRef, 0);
        }

        public TypeReference(string @namespace, string name, ModuleDefinition module, IMetadataScope scope)
            : this(@namespace, name)
        {
            this.module = module;
            this.scope = scope;
        }

        public TypeReference(string @namespace, string name, ModuleDefinition module, IMetadataScope scope, bool valueType) :
            this(@namespace, name, module, scope)
        {
            value_type = valueType;
        }

        protected virtual void ClearFullName()
        {
            this.fullname = null;
        }

        public virtual TypeReference GetElementType()
        {
            return this;
        }

        protected override IMemberDefinition ResolveDefinition()
        {
            return this.Resolve();
        }

        public new virtual TypeDefinition Resolve()
        {
            var module = this.Module;
            if (module == null)
                throw new NotSupportedException();

            return module.Resolve(this);
        }

        internal string TypeFullName =>
            string.IsNullOrEmpty(Namespace)
                ? Name
                : Namespace + '.' + Name;

        internal bool IsTypeOf(string @namespace, string name) => Name == name && Namespace == @namespace;

        internal bool IsTypeSpecification => etype is
            ElementType.Array
            or ElementType.ByRef
            or ElementType.CModOpt
            or ElementType.CModReqD
            or ElementType.FnPtr
            or ElementType.GenericInst
            or ElementType.MVar
            or ElementType.Pinned
            or ElementType.SzArray
            or ElementType.Sentinel
            or ElementType.Var;

        internal TypeDefinition CheckedResolve()
        {
            var type = Resolve();
            if (type == null)
                throw new ResolutionException(this);

            return type;
        }
    }
}
