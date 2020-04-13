// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Concurrent;

// kz test
namespace Microsoft.Data.SqlClient
{
    internal class AzureSQLDNSCache
    {
        private static readonly AzureSQLDNSCache _AzureSQLDNSCache = new AzureSQLDNSCache();
        private static readonly int initialCapacity = 100;
        private ConcurrentDictionary<string, AzureSQLDNSInfo> DNSInfoCache;

        // singleton instance
        public static AzureSQLDNSCache Instance { get { return _AzureSQLDNSCache; } }

        private AzureSQLDNSCache()
        {
            int level = 4 * Environment.ProcessorCount;
            DNSInfoCache = new ConcurrentDictionary<string, AzureSQLDNSInfo>(concurrencyLevel: level,
                                                                            capacity: initialCapacity,
                                                                            comparer: StringComparer.OrdinalIgnoreCase);
        }

        internal bool AddDNSInfo(AzureSQLDNSInfo item)
        {
            if (null != item)
            {
                if (DNSInfoCache.ContainsKey(item.FQDN))
                {

                    DeleteDNSInfo(item.FQDN);
                }

                return DNSInfoCache.TryAdd(item.FQDN, item);
            }

            return false;
        }

        internal bool DeleteDNSInfo(string FQDN)
        {
            AzureSQLDNSInfo value;
            return DNSInfoCache.TryRemove(FQDN, out value);
        }

        internal bool GetDNSInfo(string FQDN, out AzureSQLDNSInfo result)
        {
            return DNSInfoCache.TryGetValue(FQDN, out result);
        }

        internal bool IsDuplicate(AzureSQLDNSInfo newItem)
        {
            if (null != newItem)
            {
                AzureSQLDNSInfo oldItem;
                if (GetDNSInfo(newItem.FQDN, out oldItem))
                {
                    return (newItem.AddrIPv4 == oldItem.AddrIPv4 &&
                            newItem.AddrIPv6 == oldItem.AddrIPv6 &&
                            newItem.Port == oldItem.Port);
                }
            }

            return false;
        }

    }

    internal class AzureSQLDNSInfo
    {
        public string FQDN { get; set; }
        public string AddrIPv4 { get; set; }
        public string AddrIPv6 { get; set; }
        public string Port { get; set; }

        internal AzureSQLDNSInfo(string FQDN, string ipv4, string ipv6, string port)
        {
            this.FQDN = FQDN;
            AddrIPv4 = ipv4;
            AddrIPv6 = ipv6;
            Port = port;
        }
    }
}
