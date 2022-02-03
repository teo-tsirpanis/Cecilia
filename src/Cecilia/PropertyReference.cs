//
// Author:
//   Jb Evain (jbevain@gmail.com)
//
// Copyright (c) 2008 - 2015 Jb Evain
// Copyright (c) 2008 - 2011 Novell, Inc.
//
// Licensed under the MIT/X11 license.
//

using Mono.Collections.Generic;

namespace Cecilia
{

    public abstract class PropertyReference : MemberReference
    {

        TypeReference property_type;

        public TypeReference PropertyType
        {
            get { return property_type; }
            set { property_type = value; }
        }

        public abstract Collection<ParameterDefinition> Parameters
        {
            get;
        }

        internal PropertyReference(string name, TypeReference propertyType)
            : base(name)
        {
            Mixin.CheckNotNull(propertyType);

            property_type = propertyType;
        }

        protected override IMemberDefinition ResolveDefinition()
        {
            return this.Resolve();
        }

        public new abstract PropertyDefinition Resolve();
    }
}
