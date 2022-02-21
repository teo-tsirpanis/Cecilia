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
using System;

namespace Cecilia
{
    static partial class Mixin
    {
        public static bool IsNullOrEmpty<T>(this T[] self)
        {
            return self == null || self.Length == 0;
        }

        public static bool IsNullOrEmpty<T>(this Collection<T> self)
        {
            return self == null || self.size == 0;
        }

        public static T[] Add<T>(this T[] self, T item)
        {
            if (self == null)
            {
                return new[] { item };
            }

            Array.Resize(ref self, self.Length + 1);
            self[self.Length - 1] = item;
            return self;
        }
    }
}
