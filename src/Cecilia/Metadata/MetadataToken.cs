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

namespace Cecilia
{
    public struct MetadataToken : IEquatable<MetadataToken>
    {
        readonly uint token;

        public uint RID => token & 0x00ffffff;

        public TokenType TokenType => (TokenType)(token & 0xff000000);

        public static MetadataToken Zero => new MetadataToken((uint)0);

        public MetadataToken(uint token) => this.token = token;

        public MetadataToken(TokenType type)
            : this(type, 0)
        {
        }

        public MetadataToken(TokenType type, uint rid) => token = (uint)type | rid;

        public MetadataToken(TokenType type, int rid) => token = (uint)type | (uint)rid;

        public int ToInt32() => (int)token;

        public uint ToUInt32() => token;

        public override int GetHashCode() => (int)token;

        public bool Equals(MetadataToken other) => other.token == token;

        public override bool Equals(object obj) => obj is MetadataToken other && other.token == token;

        public static bool operator ==(MetadataToken one, MetadataToken other) => one.token == other.token;

        public static bool operator !=(MetadataToken one, MetadataToken other) => one.token != other.token;

        public override string ToString() => $"[{TokenType}:0x{RID:x4}]";
    }
}
