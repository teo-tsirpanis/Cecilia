// This file is part of Cecilia.
// Licensed under the MIT License.

namespace Cecilia
{
    internal static class BitFieldExtensions
    {
        public static bool GetAttributes(this uint self, uint attributes)
        {
            return (self & attributes) != 0;
        }

        public static uint SetAttributes(this uint self, uint attributes, bool value)
        {
            if (value)
                return self | attributes;

            return self & ~attributes;
        }

        public static bool GetMaskedAttributes(this uint self, uint mask, uint attributes)
        {
            return (self & mask) == attributes;
        }

        public static uint SetMaskedAttributes(this uint self, uint mask, uint attributes, bool value)
        {
            if (value)
            {
                self &= ~mask;
                return self | attributes;
            }

            return self & ~(mask & attributes);
        }

        public static bool GetAttributes(this ushort self, ushort attributes)
        {
            return (self & attributes) != 0;
        }

        public static ushort SetAttributes(this ushort self, ushort attributes, bool value)
        {
            if (value)
                return (ushort)(self | attributes);

            return (ushort)(self & ~attributes);
        }

        public static bool GetMaskedAttributes(this ushort self, ushort mask, uint attributes)
        {
            return (self & mask) == attributes;
        }

        public static ushort SetMaskedAttributes(this ushort self, ushort mask, uint attributes, bool value)
        {
            if (value)
            {
                self = (ushort)(self & ~mask);
                return (ushort)(self | attributes);
            }

            return (ushort)(self & ~(mask & attributes));
        }
    }
}
