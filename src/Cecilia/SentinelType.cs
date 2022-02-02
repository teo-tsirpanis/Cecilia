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

using MD = Cecilia.Metadata;

namespace Cecilia {

	public sealed class SentinelType : TypeSpecification {

		public override bool IsValueType {
			get { return false; }
			set { throw new InvalidOperationException (); }
		}

		public override bool IsSentinel {
			get { return true; }
		}

		public SentinelType (TypeReference type)
			: base (type)
		{
			Mixin.CheckNotNull (type);
			this.etype = MD.ElementType.Sentinel;
		}
	}
}
