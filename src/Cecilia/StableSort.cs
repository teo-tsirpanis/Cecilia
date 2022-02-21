//
// Author:
//   Jb Evain (jbevain@gmail.com)
//
// Copyright (c) 2008 - 2015 Jb Evain
// Copyright (c) 2008 - 2011 Novell, Inc.
//
// Licensed under the MIT/X11 license.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Cecilia
{
    internal static class StableSort
    {
        public static void Sort<T>(Span<T> elements, IComparer<T> comparer)
        {
            var buffer = new T[elements.Length];
            elements.CopyTo(buffer);
            TopDownSplitMerge(buffer, elements, comparer);
        }

        private static void TopDownSplitMerge<T>(Span<T> a, Span<T> b, IComparer<T> comparer)
        {
            Debug.Assert(a.Length == b.Length);
            if (a.Length < 2)
                return;

            int middle = a.Length / 2;
            TopDownSplitMerge(b.Slice(0, middle), a.Slice(0, middle), comparer);
            TopDownSplitMerge(b.Slice(middle), a.Slice(middle), comparer);
            TopDownMerge(a, b, middle, comparer);
        }

        private static void TopDownMerge<T>(ReadOnlySpan<T> a, Span<T> b, int middle, IComparer<T> comparer)
        {
            Debug.Assert(a.Length == b.Length);
            for (int i = 0, j = middle, k = 0; k < a.Length; k++)
            {
                if (i < middle && (j >= a.Length || comparer.Compare(a[i], a[j]) <= 0))
                {
                    b[k] = a[i++];
                }
                else
                {
                    b[k] = a[j++];
                }
            }
        }
    }
}
