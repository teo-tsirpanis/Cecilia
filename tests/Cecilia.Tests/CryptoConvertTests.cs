// This file is part of Cecilia.
// Licensed under the MIT License.

using Cecilia.Security.Cryptography;
using NUnit.Framework;
using System;
using System.Security.Cryptography;

namespace Cecilia.Tests
{
    [TestFixture]
    public class CryptoConvertTests
    {
        [Test]
        public void RoundtripBetweenPublicKeyBlobs()
        {
            // Try with many random RSA keys.
            for (int i = 0; i < 10; i++)
            {
                RSAParameters rsaParams;
                using (RSA rsa = RSA.Create(2048))
                    rsaParams = rsa.ExportParameters(false);

                var publicKeyBlob = new byte[CryptoConvert.GetCapiPublicKeyBlobLength(in rsaParams)];
                CryptoConvert.WriteCapiPublicKeyBlob(in rsaParams, publicKeyBlob);

                RSAParameters rsaParams2;
                using (RSA rsa2 = CryptoConvert.FromCapiKeyBlob(publicKeyBlob))
                    rsaParams2 = rsa2.ExportParameters(false);

                // The random key's exponent might be trimmed.
                Assert.AreEqual(rsaParams.Exponent.AsSpan().TrimEnd<byte>(0).ToArray(), rsaParams2.Exponent.AsSpan().TrimEnd<byte>(0).ToArray());
                Assert.AreEqual(rsaParams2.Modulus, rsaParams2.Modulus);
            }
        }
    }

#if NETFRAMEWORK
    internal static class SpanExtensions
    {
        public static Span<T> TrimEnd<T>(this Span<T> span, T element) where T : IEquatable<T>
        {
            for (int i = span.Length - 1; i >= 0; i--)
            {
                if (!span[i].Equals(element))
                {
                    return span.Slice(0, i + 1);
                }
            }
            return default;
        }
    }
#endif
}
