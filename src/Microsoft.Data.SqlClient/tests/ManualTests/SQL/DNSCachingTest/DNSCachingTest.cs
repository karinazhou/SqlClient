// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Xunit;

namespace Microsoft.Data.SqlClient.ManualTesting.Tests
{

    public class DNSCachingTest
    {
        public static Assembly systemData = Assembly.GetAssembly(typeof(SqlConnection));
        public static Type AzureSQLDNSCacheType = systemData.GetType("Microsoft.Data.SqlClient.AzureSQLDNSCache");
        public static Type AzureSQLDNSInfoType = systemData.GetType("Microsoft.Data.SqlClient.AzureSQLDNSInfo");
        public static MethodInfo AzureSQLDNSCacheGetDNSInfo = AzureSQLDNSCacheType.GetMethod("GetDNSInfo", BindingFlags.Instance | BindingFlags.NonPublic);
        
        
        [ConditionalFact(typeof(DataTestUtility), nameof(DataTestUtility.IsDNSCachingSetup))]
        public void DNSCachingIsSupportedFlag()
        {
            string expectedDNSCachingSupportedCR = DataTestUtility.IsDNSCachingSupportedCR ? "true" : "false";
            string expectedDNSCachingSupportedTR = DataTestUtility.IsDNSCachingSupportedTR ? "true" : "false";

            using(SqlConnection connection = new SqlConnection(DataTestUtility.DNSCachingConnString))
            {
                connection.Open();

                string isSupportedStateTR = (string)typeof(SqlConnection).GetProperty("AzureSQLDNSCachingSupportedState", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(connection);
                string isSupportedStateCR = (string)typeof(SqlConnection).GetProperty("RedirectAzureSQLDNSCachingSupportedState", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(connection);
                Assert.Equal(expectedDNSCachingSupportedCR, isSupportedStateCR);
                Assert.Equal(expectedDNSCachingSupportedTR, isSupportedStateTR);
            }
        }

        
        [ConditionalFact(typeof(DataTestUtility), nameof(DataTestUtility.IsDNSCachingSetup))]
        public void DNSCachingGetDNSInfo()
        {            
            using(SqlConnection connection = new SqlConnection(DataTestUtility.DNSCachingConnString))
            {
                connection.Open();
            }

            var azureSQLDNSCacheInstance = AzureSQLDNSCacheType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public).GetValue(null);
            
            var serverList = new List<KeyValuePair<string, bool>>();
            serverList.Add(new KeyValuePair<string, bool>(DataTestUtility.DNSCachingServerCR, DataTestUtility.IsDNSCachingSupportedCR));
            serverList.Add(new KeyValuePair<string, bool>(DataTestUtility.DNSCachingServerTR, DataTestUtility.IsDNSCachingSupportedTR));
            
            foreach(var server in serverList)
            {
                object[] parameters;
                bool ret;

                if (!string.IsNullOrEmpty(server.Key))
                {
                    parameters = new object[] { server.Key, null };
                    ret = (bool)AzureSQLDNSCacheGetDNSInfo.Invoke(azureSQLDNSCacheInstance, parameters);

                    if (server.Value)
                    {
                        Assert.NotNull(parameters[1]);
                        Assert.Equal(server.Key, (string)AzureSQLDNSInfoType.GetProperty("FQDN").GetValue(parameters[1]));
                    }
                    else
                    {
                        Assert.Null(parameters[1]);
                    }
                }
            }
        }

    }

}