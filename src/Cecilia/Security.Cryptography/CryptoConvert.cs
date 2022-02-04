//
// CryptoConvert.cs - Crypto Convertion Routines
//
// Author:
//	Sebastien Pouliot  <sebastien@ximian.com>
//
// (C) 2003 Motus Technologies Inc. (http://www.motus.com)
// Copyright (C) 2004-2006 Novell Inc. (http://www.novell.com)
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Cecilia.Security.Cryptography
{
    internal static class CryptoConvert
    {
#if NETSTANDARD
        static private Span<T> TrimStart<T>(this Span<T> span, T element) where T: IEquatable<T>
        {
            for (int i = 0; i < span.Length; i++)
            {
                if (!span[i].Equals(element))
                {
                    return span.Slice(i);
                }
            }
            return default;
        }
#endif

        static RSA FromCapiPrivateKeyBlob(ReadOnlySpan<byte> blob)
        {
            var rsap = new RSAParameters();
            try
            {
                // We used to read the reserved bytes at 2 and 3.
                // Reserved values must be ignored when reading them.
                // A different value should not cause an error.
                if ((blob[0] != 0x07) || // PRIVATEKEYBLOB (0x07)
                    (blob[1] != 0x02) || // Version (0x02)
                    (BinaryPrimitives.ReadUInt32LittleEndian(blob.Slice(8)) != 0x32415352)) // DWORD magic = RSA2
                    throw new CryptographicException("Invalid blob header");

                // ALGID (CALG_RSA_SIGN, CALG_RSA_KEYX, ...)
                // int algId = BinaryPrimitives.ReadInt32LittleEndian(blob.Slice(4));

                // DWORD bitlen
                int bitLen = BinaryPrimitives.ReadInt32LittleEndian(blob.Slice(12));

                // DWORD public exponent
                Span<byte> exp = stackalloc byte[4];
                blob.Slice(16, 4).CopyTo(exp);
                exp.Reverse();
                rsap.Exponent = exp.TrimStart<byte>(0).ToArray();

                int pos = 20;
                // BYTE modulus[rsapubkey.bitlen/8];
                int byteLen = bitLen / 8;
                rsap.Modulus = new byte[byteLen];
                blob.Slice(pos, byteLen).CopyTo(rsap.Modulus);
                Array.Reverse(rsap.Modulus);
                pos += byteLen;

                // BYTE prime1[rsapubkey.bitlen/16];
                int byteHalfLen = byteLen / 2;
                rsap.P = blob.Slice(pos, byteHalfLen).ToArray();
                Array.Reverse(rsap.P);
                pos += byteHalfLen;

                // BYTE prime2[rsapubkey.bitlen/16];
                rsap.Q = blob.Slice(pos, byteHalfLen).ToArray();
                Array.Reverse(rsap.Q);
                pos += byteHalfLen;

                // BYTE exponent1[rsapubkey.bitlen/16];
                rsap.DP = blob.Slice(pos, byteHalfLen).ToArray();
                Array.Reverse(rsap.DP);
                pos += byteHalfLen;

                // BYTE exponent2[rsapubkey.bitlen/16];
                rsap.DQ = blob.Slice(pos, byteHalfLen).ToArray();
                Array.Reverse(rsap.DQ);
                pos += byteHalfLen;

                // BYTE coefficient[rsapubkey.bitlen/16];
                rsap.InverseQ = blob.Slice(pos, byteHalfLen).ToArray();
                Array.Reverse(rsap.InverseQ);
                pos += byteHalfLen;

                // ok, this is hackish but CryptoAPI support it so...
                // note: only works because CRT is used by default
                // http://bugzilla.ximian.com/show_bug.cgi?id=57941
                // TL;DR: the blob might not include the private D
                // exponent, but it is not needed since we give it
                // the Chinese Remainder Theorem parameters.
                rsap.D = new byte[byteLen]; // must be allocated
                if (pos + byteLen <= blob.Length)
                {
                    // BYTE privateExponent[rsapubkey.bitlen/8];
                    blob.Slice(pos, byteLen).CopyTo(rsap.D);
                    Array.Reverse(rsap.D);
                }
            }
            catch (Exception e)
            {
                throw new CryptographicException("Invalid blob.", e);
            }

            RSA rsa = RSA.Create();
            rsa.ImportParameters(rsap);
            return rsa;
        }

        static RSA FromCapiPublicKeyBlob(ReadOnlySpan<byte> blob)
        {
            try
            {
                if ((blob[0] != 0x06) || // PUBLICKEYBLOB (0x06)
                    (blob[1] != 0x02) || // Version (0x02)
                    (BinaryPrimitives.ReadUInt32LittleEndian(blob.Slice(8)) != 0x31415352)) // DWORD magic = RSA1
                    throw new CryptographicException("Invalid blob header");

                // ALGID (CALG_RSA_SIGN, CALG_RSA_KEYX, ...)
                // int algId = BinaryPrimitives.ReadInt32LittleEndian(blob.Slice(4));

                // DWORD bitlen
                int bitLen = BinaryPrimitives.ReadInt32LittleEndian(blob.Slice(12));

                // DWORD public exponent
                var rsap = new RSAParameters
                {
                    Exponent = new byte[3] { blob[18], blob[17], blob[16] }
                };

                // BYTE modulus[rsapubkey.bitlen/8];
                int byteLen = bitLen / 8;
                rsap.Modulus = blob.Slice(20, byteLen).ToArray();
                Array.Reverse(rsap.Modulus);

                RSA rsa = RSA.Create();
                rsa.ImportParameters(rsap);
                return rsa;
            }
            catch (Exception e)
            {
                throw new CryptographicException("Invalid blob.", e);
            }
        }

        static public RSA FromCapiKeyBlob(ReadOnlySpan<byte> blob)
        {
            if (blob.IsEmpty)
                throw new ArgumentException("blob is too small.", nameof(blob));

            switch (blob[0])
            {
                case 0x00:
                    // this could be a public key inside an header
                    // like "sn -e" would produce
                    if (blob[12] == 0x06)
                    {
                        return FromCapiPublicKeyBlob(blob.Slice(12));
                    }
                    break;
                case 0x06:
                    return FromCapiPublicKeyBlob(blob);
                case 0x07:
                    return FromCapiPrivateKeyBlob(blob);
            }
            throw new CryptographicException("Unknown blob format.");
        }

        static public byte[] ToCapiPublicKeyBlob(RSA rsa)
        {
            RSAParameters p = rsa.ExportParameters(false);
            int keyLength = p.Modulus.Length; // in bytes
            var blob = new byte[20 + keyLength];

            blob[0] = 0x06; // Type - PUBLICKEYBLOB (0x06)
            blob[1] = 0x02; // Version - Always CUR_BLOB_VERSION (0x02)
                            // [2], [3]		// RESERVED - Always 0
            blob[5] = 0x24; // ALGID - Always 00 24 00 00 (for CALG_RSA_SIGN)
            blob[8] = 0x52; // Magic - RSA1 (ASCII in hex)
            blob[9] = 0x53;
            blob[10] = 0x41;
            blob[11] = 0x31;

            BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(12), keyLength * 8);

            // public exponent (DWORD)
            var exp = new Span<byte>(blob, 16, 4);
            p.Exponent.AsSpan().CopyTo(exp);
            exp.Reverse();
            // modulus
            var mod = blob.AsSpan().Slice(20);
            p.Modulus.AsSpan().CopyTo(mod);
            mod.Reverse();
            return blob;
        }
    }
}
