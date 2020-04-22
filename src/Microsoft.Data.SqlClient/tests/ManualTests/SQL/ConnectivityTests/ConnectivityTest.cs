// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Xunit;

namespace Microsoft.Data.SqlClient.ManualTesting.Tests
{
    public static class ConnectivityTest
    {
        private const string COL_SPID = "SPID";
        private const string COL_PROGRAM_NAME = "ProgramName";
        private const string COL_HOSTNAME = "HostName";
        private static readonly string s_databaseName = "d_" + Guid.NewGuid().ToString().Replace('-', '_');
        private static readonly string s_tableName = DataTestUtility.GenerateObjectName();
        private static readonly string s_connectionString = DataTestUtility.TCPConnectionString;
        private static readonly string s_dbConnectionString = new SqlConnectionStringBuilder(s_connectionString) { InitialCatalog = s_databaseName }.ConnectionString;
        private static readonly string s_createDatabaseCmd = $"CREATE DATABASE {s_databaseName}";
        private static readonly string s_createTableCmd = $"CREATE TABLE {s_tableName} (NAME NVARCHAR(40), AGE INT)";
        private static readonly string s_alterDatabaseSingleCmd = $"ALTER DATABASE {s_databaseName} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;";
        private static readonly string s_alterDatabaseMultiCmd = $"ALTER DATABASE {s_databaseName} SET MULTI_USER WITH ROLLBACK IMMEDIATE;";
        private static readonly string s_selectTableCmd = $"SELECT COUNT(*) FROM {s_tableName}";
        private static readonly string s_dropDatabaseCmd = $"DROP DATABASE {s_databaseName}";

        [CheckConnStrSetupFact]
        public static void EnvironmentHostNameTest()
        {
            SqlConnectionStringBuilder builder = (new SqlConnectionStringBuilder(DataTestUtility.TCPConnectionString) { Pooling = true });
            builder.ApplicationName = "HostNameTest";

            using (SqlConnection sqlConnection = new SqlConnection(builder.ConnectionString))
            {
                sqlConnection.Open();
                int sessionSpid;

                using (SqlCommand cmd = new SqlCommand("SELECT @@SPID", sqlConnection))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    reader.Read();
                    sessionSpid = reader.GetInt16(0);
                }

                using (SqlCommand command = new SqlCommand("sp_who2", sqlConnection))
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int programNameOrdinal = reader.GetOrdinal(COL_PROGRAM_NAME);
                        string programName = reader.GetString(programNameOrdinal);

                        int spidOrdinal = reader.GetOrdinal(COL_SPID);
                        string spid = reader.GetString(spidOrdinal);

                        if (programName != null && programName.Trim().Equals(builder.ApplicationName) && Int16.Parse(spid) == sessionSpid)
                        {
                            // Get the hostname
                            int hostnameOrdinal = reader.GetOrdinal(COL_HOSTNAME);
                            string hostnameFromServer = reader.GetString(hostnameOrdinal);
                            string expectedMachineName = Environment.MachineName.ToUpper();
                            string hostNameFromServer = hostnameFromServer.Trim().ToUpper();
                            Assert.Matches(expectedMachineName, hostNameFromServer);
                            return;
                        }
                    }
                }
            }
            Assert.True(false, "No non-empty hostname found for the application");
        }

        [CheckConnStrSetupFact]
        public static void ConnectionTimeoutTestWithThread()
        {
            const int timeoutSec = 5;
            const int numOfTry = 2;
            const int numOfThreads = 5;

            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(DataTestUtility.TCPConnectionString);
            builder.DataSource = "invalidhost";
            builder.ConnectTimeout = timeoutSec;
            string connStrNotAvailable = builder.ConnectionString;

            for (int i = 0; i < numOfThreads; ++i)
            {
                new ConnectionWorker(connStrNotAvailable, numOfTry);
            }

            ConnectionWorker.Start();
            ConnectionWorker.Stop();

            double timeTotal = 0;
            double timeElapsed = 0;

            foreach (ConnectionWorker w in ConnectionWorker.WorkerList)
            {
                timeTotal += w.TimeElapsed;
            }
            timeElapsed = timeTotal / Convert.ToDouble(ConnectionWorker.WorkerList.Count);

            int threshold = timeoutSec * numOfTry * 2 * 1000;

            Assert.True(timeElapsed < threshold);
        }

        [CheckConnStrSetupFact]
        public static void ProcessIdTest()
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(DataTestUtility.TCPConnectionString);
            string sqlProviderName = builder.ApplicationName;
            string sqlProviderProcessID = System.Diagnostics.Process.GetCurrentProcess().Id.ToString();

            using (SqlConnection sqlConnection = new SqlConnection(builder.ConnectionString))
            {
                sqlConnection.Open();
                string strCommand = $"SELECT PROGRAM_NAME,HOSTPROCESS FROM SYS.SYSPROCESSES WHERE PROGRAM_NAME LIKE ('%{sqlProviderName}%')";
                using (SqlCommand command = new SqlCommand(strCommand, sqlConnection))
                {
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        bool processIdFound = false;
                        while (reader.Read())
                        {
                            Assert.Equal(sqlProviderName, reader.GetString(0).Trim());
                            if (sqlProviderProcessID == reader.GetString(1).Trim())
                            {
                                processIdFound = true;
                            }
                        }
                        Assert.True(processIdFound);
                    }
                }
            }
        }
        public class ConnectionWorker
        {
            private static List<ConnectionWorker> workerList = new List<ConnectionWorker>();
            private ManualResetEventSlim _doneEvent = new ManualResetEventSlim(false);
            private double _timeElapsed;
            private Thread _thread;
            private string _connectionString;
            private int _numOfTry;

            public ConnectionWorker(string connectionString, int numOfTry)
            {
                workerList.Add(this);
                _connectionString = connectionString;
                _numOfTry = numOfTry;
                _thread = new Thread(new ThreadStart(SqlConnectionOpen));
            }

            public static List<ConnectionWorker> WorkerList => workerList;

            public double TimeElapsed => _timeElapsed;

            public static void Start()
            {
                foreach (ConnectionWorker w in workerList)
                {
                    w._thread.Start();
                }
            }

            public static void Stop()
            {
                foreach (ConnectionWorker w in workerList)
                {
                    w._doneEvent.Wait();
                }
            }

            public void SqlConnectionOpen()
            {
                Stopwatch sw = new Stopwatch();
                double totalTime = 0;
                for (int i = 0; i < _numOfTry; ++i)
                {
                    using (SqlConnection con = new SqlConnection(_connectionString))
                    {
                        sw.Start();
                        try
                        {
                            con.Open();
                        }
                        catch { }
                        sw.Stop();
                    }
                    totalTime += sw.Elapsed.TotalMilliseconds;
                    sw.Reset();
                }

                _timeElapsed = totalTime / Convert.ToDouble(_numOfTry);

                _doneEvent.Set();
            }
        }

        [ConditionalFact(typeof(DataTestUtility), nameof(DataTestUtility.AreConnStringsSetup), nameof(DataTestUtility.IsNotAzureServer))]
        public static void ConnectionKilledTest()
        {
            try
            {
                // Setup Database and Table.
                DataTestUtility.RunNonQuery(s_connectionString, s_createDatabaseCmd);
                DataTestUtility.RunNonQuery(s_dbConnectionString, s_createTableCmd);

                // Kill all the connections and set Database to SINGLE_USER Mode.
                DataTestUtility.RunNonQuery(s_connectionString, s_alterDatabaseSingleCmd, 4);
                // Set Database back to MULTI_USER Mode
                DataTestUtility.RunNonQuery(s_connectionString, s_alterDatabaseMultiCmd, 4);

                // Execute SELECT statement.
                DataTestUtility.RunNonQuery(s_dbConnectionString, s_selectTableCmd);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Assert.Null(ex);
            }
            finally
            {
                // Kill all the connections, set Database to SINGLE_USER Mode and drop Database
                DataTestUtility.RunNonQuery(s_connectionString, s_alterDatabaseSingleCmd, 4);
                DataTestUtility.RunNonQuery(s_connectionString, s_dropDatabaseCmd, 4);
            }
        }

        [ConditionalFact(typeof(DataTestUtility), nameof(DataTestUtility.AreConnStringsSetup))]
        public static void ConnectionResiliencyTest()
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(DataTestUtility.TCPConnectionString);
            builder.ConnectRetryCount = 0;
            builder.ConnectRetryInterval = 5;

            // No connection resiliency
            using (SqlConnection conn = new SqlConnection(builder.ConnectionString))
            {
                conn.Open();
                InternalConnectionWrapper wrapper = new InternalConnectionWrapper(conn, true, builder.ConnectionString);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT TOP 1 * FROM dbo.Employees";
                    wrapper.KillConnectionByTSql();
                    bool cmdSuccess = false;
                    try
                    {
                        cmd.ExecuteScalar();
                        cmdSuccess = true;
                    }
                    // Windows always throws SqlException. Unix sometimes throws AggregateException against Azure SQL DB.
                    catch (Exception ex) when (ex is SqlException || ex is AggregateException) { }
                    Assert.False(cmdSuccess);
                }
            }

            builder.ConnectRetryCount = 2;
            using (SqlConnection conn = new SqlConnection(builder.ConnectionString))
            {
                conn.Open();
                InternalConnectionWrapper wrapper = new InternalConnectionWrapper(conn, true, builder.ConnectionString);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT TOP 1 * FROM dbo.Employees";
                    using (SqlDataReader reader = cmd.ExecuteReader())
                        while (reader.Read())
                        { }

                    wrapper.KillConnectionByTSql();

                    // Connection resiliency should reconnect transparently
                    using (SqlDataReader reader = cmd.ExecuteReader())
                        while (reader.Read())
                        { }
                }
            }
        }

        [ConditionalFact(typeof(DataTestUtility), nameof(DataTestUtility.IsTCPConnectionStringPasswordIncluded))]
        public static void ConnectionStringPersistentInfoTest()
        {
            SqlConnectionStringBuilder connectionStringBuilder = new SqlConnectionStringBuilder(DataTestUtility.TCPConnectionString);
            connectionStringBuilder.PersistSecurityInfo = false;
            string cnnString = connectionStringBuilder.ConnectionString;

            connectionStringBuilder.Clear();
            using (SqlConnection sqlCnn = new SqlConnection(cnnString))
            {
                sqlCnn.Open();
                connectionStringBuilder.ConnectionString = sqlCnn.ConnectionString;
                Assert.True(connectionStringBuilder.Password == string.Empty, "Password must not persist according to set the PersistSecurityInfo by false!");
            }

            connectionStringBuilder.ConnectionString = DataTestUtility.TCPConnectionString;
            connectionStringBuilder.PersistSecurityInfo = true;
            cnnString = connectionStringBuilder.ConnectionString;

            connectionStringBuilder.Clear();
            using (SqlConnection sqlCnn = new SqlConnection(cnnString))
            {
                sqlCnn.Open();
                connectionStringBuilder.ConnectionString = sqlCnn.ConnectionString;
                Assert.True(connectionStringBuilder.Password != string.Empty, "Password must persist according to set the PersistSecurityInfo by true!");
            }
        }

        [ConditionalFact(typeof(DataTestUtility), nameof(DataTestUtility.AreConnStringsSetup))]
        public static void ConnectionOpenDisableRetry()
        {
            using (SqlConnection sqlConnection = new SqlConnection((new SqlConnectionStringBuilder(DataTestUtility.TCPConnectionString) { InitialCatalog = "DoesNotExist0982532435423", Pooling = false }).ConnectionString))
            {
                TimeSpan duration;
                DateTime start = DateTime.Now;
                try
                {
                    sqlConnection.Open(SqlConnectionOverrides.OpenWithoutRetry);
                    Assert.True(false, "Connection succeeded to database that should not exist.");
                }
                catch (SqlException)
                {
                    duration = DateTime.Now - start;
                    Assert.True(duration.TotalSeconds < 2, $"Connection Open() without retries took longer than expected. Expected < 2 sec. Took {duration.TotalSeconds} sec.");
                }

                start = DateTime.Now;
                try
                {
                    sqlConnection.Open();
                    Assert.True(false, "Connection succeeded to database that should not exist.");
                }
                catch (SqlException)
                {
                    duration = DateTime.Now - start;
                    //Should not fail fast due to transient fault handling when DB does not exist
                    Assert.True(duration.TotalSeconds > 5, $"Connection Open() with retries took less time than expected. Expect > 5 sec with transient fault handling. Took {duration.TotalSeconds} sec.");
                }
            }
        }
    }
}
