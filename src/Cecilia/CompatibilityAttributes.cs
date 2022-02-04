// This file is part of Cecilia.
// Licensed under the MIT License.

#if !NET
namespace System.Runtime.CompilerServices
{
    internal sealed class CallerArgumentExpressionAttribute : Attribute
    {
        public CallerArgumentExpressionAttribute(string expression) { }
    }
}
#endif
