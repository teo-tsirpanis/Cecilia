//
// Author:
//   Jb Evain (jbevain@gmail.com)
//
// Copyright (c) 2008 - 2015 Jb Evain
// Copyright (c) 2008 - 2011 Novell, Inc.
//
// Licensed under the MIT/X11 license.
//

namespace Cecilia
{
    public enum TargetRuntime
    {
        Net_1_0,
        Net_1_1,
        Net_2_0,
        Net_4_0,
    }

    internal static class TargetRuntimeExtensions
    {
        public static TargetRuntime ParseRuntime(this string self)
        {
            if (string.IsNullOrEmpty(self))
                return TargetRuntime.Net_4_0;

            switch (self[1])
            {
                case '1':
                    return self[3] == '0'
                        ? TargetRuntime.Net_1_0
                        : TargetRuntime.Net_1_1;
                case '2':
                    return TargetRuntime.Net_2_0;
                case '4':
                default:
                    return TargetRuntime.Net_4_0;
            }
        }

        public static string RuntimeVersionString(this TargetRuntime runtime)
        {
            switch (runtime)
            {
                case TargetRuntime.Net_1_0:
                    return "v1.0.3705";
                case TargetRuntime.Net_1_1:
                    return "v1.1.4322";
                case TargetRuntime.Net_2_0:
                    return "v2.0.50727";
                case TargetRuntime.Net_4_0:
                default:
                    return "v4.0.30319";
            }
        }
    }
}
