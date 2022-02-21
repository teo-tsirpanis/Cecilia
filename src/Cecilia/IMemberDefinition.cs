//
// Author:
//   Jb Evain (jbevain@gmail.com)
//
// Copyright (c) 2008 - 2015 Jb Evain
// Copyright (c) 2008 - 2011 Novell, Inc.
//
// Licensed under the MIT/X11 license.
//

namespace Cecilia
{
    public interface IMemberDefinition : ICustomAttributeProvider
    {
        string Name { get; set; }
        string FullName { get; }

        bool IsSpecialName { get; set; }
        bool IsRuntimeSpecialName { get; set; }

        TypeDefinition DeclaringType { get; set; }
    }
}
