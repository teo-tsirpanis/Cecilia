//
// Author:
//   Jb Evain (jbevain@gmail.com)
//
// Copyright (c) 2008 - 2015 Jb Evain
// Copyright (c) 2008 - 2011 Novell, Inc.
//
// Licensed under the MIT/X11 license.
//

using Cecilia.PE;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;

namespace Cecilia.Security.Cryptography
{
    // Most of this code has been adapted
    // from Jeroen Frijters' fantastic work
    // in IKVM.Reflection.Emit. Thanks!
    internal static class CryptoService
    {
        public static byte[] GetPublicKey(WriterParameters parameters)
        {
            RSAParameters rsaParams;
            using (RSA rsa = parameters.StrongNameKeyPair.CreateRSA())
                rsaParams = rsa.ExportParameters(false);
            var cspBlobLength = CryptoConvert.GetCapiPublicKeyBlobLength(in rsaParams);
            var publicKey = new byte[12 + cspBlobLength];
            CryptoConvert.WriteCapiPublicKeyBlob(in rsaParams, publicKey.AsSpan(12));
            // The first 12 bytes are documented at:
            // http://msdn.microsoft.com/library/en-us/cprefadd/html/grfungethashfromfile.asp
            // ALG_ID - Signature
            publicKey[1] = 36;
            // ALG_ID - Hash
            publicKey[4] = 4;
            publicKey[5] = 128;
            // Length of Public Key (in bytes)
            BinaryPrimitives.WriteInt32LittleEndian(publicKey.AsSpan(8), cspBlobLength);
            return publicKey;
        }

        public static void StrongName(Stream stream, ImageWriter writer, WriterParameters parameters)
        {
            var strong_name = CreateStrongName(parameters, HashStream(stream, writer, out int strong_name_pointer));
            PatchStrongName(stream, strong_name_pointer, strong_name);
        }

        static void PatchStrongName(Stream stream, int strong_name_pointer, byte[] strong_name)
        {
            stream.Seek(strong_name_pointer, SeekOrigin.Begin);
            stream.Write(strong_name, 0, strong_name.Length);
        }

        static byte[] CreateStrongName(WriterParameters parameters, byte[] hash)
        {
            using var rsa = parameters.StrongNameKeyPair.CreateRSA();
            byte[] signature = rsa.SignHash(hash, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
            Array.Reverse(signature);

            return signature;
        }

        static byte[] HashStream(Stream stream, ImageWriter writer, out int strong_name_pointer)
        {
            const int buffer_size = 8192;

            var text = writer.text;
            var header_size = (int)writer.GetHeaderSize();
            var text_section_pointer = (int)text.PointerToRawData;
            var strong_name_directory = writer.GetStrongNameSignatureDirectory();

            if (strong_name_directory.Size == 0)
                throw new InvalidOperationException();

            strong_name_pointer = (int)(text_section_pointer
                + (strong_name_directory.VirtualAddress - text.VirtualAddress));
            var strong_name_length = (int)strong_name_directory.Size;

            using var sha1 = SHA1.Create();
            var buffer = new byte[buffer_size];
            using (var crypto_stream = new CryptoStream(Stream.Null, sha1, CryptoStreamMode.Write))
            {
                stream.Seek(0, SeekOrigin.Begin);
                CopyStreamChunk(stream, crypto_stream, buffer, header_size);

                stream.Seek(text_section_pointer, SeekOrigin.Begin);
                CopyStreamChunk(stream, crypto_stream, buffer, (int)strong_name_pointer - text_section_pointer);

                stream.Seek(strong_name_length, SeekOrigin.Current);
                CopyStreamChunk(stream, crypto_stream, buffer, (int)(stream.Length - (strong_name_pointer + strong_name_length)));
            }

            return sha1.Hash;
        }

        public static void CopyStreamChunk(Stream stream, Stream dest_stream, byte[] buffer, int length)
        {
            while (length > 0)
            {
                int read = stream.Read(buffer, 0, Math.Min(buffer.Length, length));
                dest_stream.Write(buffer, 0, read);
                length -= read;
            }
        }

        public static byte[] ComputeHash(string file)
        {
            // It used to return an empty array if the file did not exist,
            // but in Cecilia the check was removed.

            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            return ComputeHash(stream);
        }

        public static byte[] ComputeHash(Stream stream)
        {
            const int buffer_size = 8192;

            using var sha1 = SHA1.Create();
            var buffer = new byte[buffer_size];

            using (var crypto_stream = new CryptoStream(Stream.Null, sha1, CryptoStreamMode.Write))
                CopyStreamChunk(stream, crypto_stream, buffer, (int)stream.Length);

            return sha1.Hash;
        }

        public static Guid ComputeGuid(ReadOnlySpan<byte> hash)
        {
            // From corefx/src/System.Reflection.Metadata/src/System/Reflection/Metadata/BlobContentId.cs
            Span<byte> guid = stackalloc byte[16];

            hash.Slice(0, 16).CopyTo(guid);

            // modify the guid data so it decodes to the form of a "random" guid ala rfc4122
            guid[7] = (byte)((guid[7] & 0x0f) | (4 << 4));
            guid[8] = (byte)((guid[8] & 0x3f) | (2 << 6));

#if NETSTANDARD2_0
            return new Guid(guid.ToArray());
#else
            return new Guid(guid);
#endif
        }
    }
}
