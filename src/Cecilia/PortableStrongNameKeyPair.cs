// Taken from https://github.com/mono/mono/blob/e3c2771b7004661afb04d568b6e8ae1d2f397a3c/mcs/class/Mono.Security/Mono.Security.Cryptography/CryptoConvert.cs
// and adapted for Cecilia.

//
// System.Reflection.StrongNameKeyPair.cs
//
// Authors:
//	Kevin Winchester (kwin@ns.sympatico.ca)
//	Sebastien Pouliot (sebastien@ximian.com)
//
// (C) 2002 Kevin Winchester
// Portions (C) 2002 Motus Technologies Inc. (http://www.motus.com)
// Copyright (C) 2004-2005 Novell, Inc (http://www.novell.com)
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

using Cecilia.Security.Cryptography;
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace Cecilia
{
    public class PortableStrongNameKeyPair
    {
        private static readonly byte[] s_ecmaKey = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 4, 0, 0, 0, 0, 0, 0, 0 };

        private byte[] _publicKey;

        private readonly RSA _rsa;

        public PortableStrongNameKeyPair(ReadOnlySpan<byte> keyPair)
        {
            LoadKey(keyPair, out _rsa);
        }

        public PortableStrongNameKeyPair(byte[] keyPairArray)
        {
            Mixin.CheckNotNull(keyPairArray);

            LoadKey(keyPairArray, out _rsa);
        }

        public PortableStrongNameKeyPair(FileStream keyPairFile)
        {
            Mixin.CheckNotNull(keyPairFile);

            byte[] input = new byte[keyPairFile.Length];
            keyPairFile.Read(input, 0, input.Length);
            LoadKey(input, out _rsa);
        }

#if NET
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
        public PortableStrongNameKeyPair(string keyPairContainer)
        {
            Mixin.CheckNotNull(keyPairContainer);

            var csp = new CspParameters
            {
                KeyContainerName = keyPairContainer
            };
            _rsa = new RSACryptoServiceProvider(csp);
        }

        // Warning: the returned instance must not be disposed.
        internal RSA GetRSA()
        {
            // ECMA "key" is valid but doesn't produce a RSA instance
            if (ReferenceEquals(_publicKey, s_ecmaKey))
                throw new InvalidOperationException("Cannot use the ECMA Standard Public Key for signing.");
            Debug.Assert(_rsa != null);
            return _rsa;
        }

        private void LoadKey(ReadOnlySpan<byte> key, out RSA rsa)
        {
            // check for ECMA key
            if (key.SequenceEqual(s_ecmaKey))
            {
                // We reuse the array to save an allocation.
                // If the PublicKey array is ever exposed, we must clone s_ecmaKey.
                _publicKey = s_ecmaKey;
                rsa = null;
            }
            else
            {
                rsa = CryptoConvert.FromCapiKeyBlob(key);
            }
        }

        public ReadOnlySpan<byte> PublicKeySpan
        {
            get
            {
                if (_publicKey == null)
                {
                    RSAParameters parameters = GetRSA().ExportParameters(false);
                    int keyBlobLength = CryptoConvert.GetCapiPublicKeyBlobLength(in parameters);

                    _publicKey = new byte[keyBlobLength + 12];
                    // The first 12 bytes are documented at:
                    // http://msdn.microsoft.com/library/en-us/cprefadd/html/grfungethashfromfile.asp
                    // ALG_ID - Signature
                    _publicKey[0] = 0x00;
                    _publicKey[1] = 0x24;
                    _publicKey[2] = 0x00;
                    _publicKey[3] = 0x00;
                    // ALG_ID - Hash
                    _publicKey[4] = 0x04;
                    _publicKey[5] = 0x80;
                    _publicKey[6] = 0x00;
                    _publicKey[7] = 0x00;
                    // Length of Public Key (in bytes)
                    _publicKey[8] = (byte)(keyBlobLength % 256);
                    _publicKey[9] = (byte)(keyBlobLength / 256); // just in case
                    _publicKey[10] = 0x00;
                    _publicKey[11] = 0x00;

                    CryptoConvert.WriteCapiPublicKeyBlob(in parameters, _publicKey.AsSpan(12));
                }
                return _publicKey;
            }
        }
    }
}
