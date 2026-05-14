using Microsoft.Data.SqlClient;

namespace StateMobile.API.Function
{
    public class SQLActions : IDisposable
    {
        public string strError;
        private string strSPname;
        List<clsParameters> lstParameter = new List<clsParameters>();
        List<clsOutput> lstOutput = new List<clsOutput>();
        List<clsByteParameter> lstByteParameter = new List<clsByteParameter>();
        List<testParamters> lstTestParamter = new List<testParamters>();

        private SqlConnection connData;
        private SqlTransaction tranSQL;
        private bool blnTransaction = false;
        public string ErrorMessage { get; set; }


        public class clsParameters
        {
            public string prmName;
            public string prmValue;
            public vbDataType prmType;
            public bool prmOutPut;
        }

        public class clsByteParameter
        {
            public string prmName;
            public byte[] prmValue;
        }

        struct testParamters
        {
            public string prmName;
            public string prmValue;
            public bool prmOutPut;
        }

        public class clsOutput
        {
            public string oName;
            public string oValue;
        }

        //public void Dispose()
        //{
        //    connData.Close();
        //    connData.Dispose();
        //    GC.SuppressFinalize(this);
        //}

        public enum vbDataType
        {
            dtString = 0,
            dtInteger = 1,
            dtDouble = 2,
            dtDate = 3,
            dtDecimal = 4,
            dtNull = 5
        }

        public enum Database
        {
            Default,
            MISD,
            HO_State
        }

        public string StoredProcedureName
        {
            get
            {
                return strSPname;
            }

            set
            {
                strSPname = value;
            }
        }


        public void AddParametersWithValue(string Name, string Value, bool Output = false)
        {
            lstParameter.Add(new clsParameters() { prmName = Name, prmValue = Value, prmOutPut = Output });
        }

        public void AddParametersWithValue(string Name, bool Value, bool Output = false, vbDataType DataType = vbDataType.dtString)
        {
            lstParameter.Add(new clsParameters() { prmName = Name, prmValue = Value ? "1" : "0", prmOutPut = Output, prmType = DataType });
        }

        public void AddParametersWithValue(string Name, DateTime Value, bool Output = false, vbDataType DataType = vbDataType.dtDate)
        {
            lstParameter.Add(new clsParameters() { prmName = Name, prmValue = Value.ToString("yyyy-MM-dd HH:mm:ss.fff"), prmOutPut = Output, prmType = DataType });
        }

        public void AddParametersWithValue(string Name, decimal Value, bool Output = false, vbDataType DataType = vbDataType.dtDecimal)
        {
            lstParameter.Add(new clsParameters() { prmName = Name, prmValue = Value.ToString(), prmOutPut = Output, prmType = DataType });
        }

        public void AddParametersWithValue(string Name, byte[] Value)
        {
            lstByteParameter.Add(new clsByteParameter { prmName = Name, prmValue = Value });
        }


        //public void AddByteParametersWithValue(string Name, byte[] Value)
        //{
        //    lstByteParameter.Add(new clsByteParameter { prmName = Name, prmValue = Value });
        //}

        public void BeginTransaction(Database db = Database.Default, string username = "", string password = "")
        {
            connData = NewConnection(db, username, password);
            tranSQL = connData.BeginTransaction();
            blnTransaction = true;
        }

        public void CommitTransaction()
        {
            tranSQL.Commit();
            tranSQL.Dispose();
            connData.Close();
            connData.Dispose();
            blnTransaction = false;
        }

        public void RollBack()
        {
            tranSQL.Rollback();
            blnTransaction = false;
        }
        public void ClearParameters()
        {
            lstParameter.Clear();
            lstByteParameter.Clear();
            lstOutput.Clear();
            lstTestParamter.Clear();
        }
        public SqlConnection NewConnection(Database db, string username = "", string password = "")
        {
            // Ensure appRoot is never null before calling SetBasePath to fix CS8604.
            var location = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string appRoot;
            if (string.IsNullOrEmpty(location))
            {
                appRoot = AppContext.BaseDirectory;
            }
            else
            {
                appRoot = Path.GetDirectoryName(location) ?? AppContext.BaseDirectory;
            }

            var builder = new ConfigurationBuilder()
                .SetBasePath(appRoot)
                .AddJsonFile("appsettings.json").Build();

            var sqlConn = new SqlConnection();
            string? connTemplate = null; // make nullable to avoid CS8600 when assigning possible null

            switch (db)
            {
                case Database.Default:
                    if (string.IsNullOrEmpty(username))
                    {
                        connTemplate = builder.GetConnectionString("DefaultConnection");
                        if (string.IsNullOrEmpty(connTemplate))
                        {
                            strError = "Connection string 'DefaultConnection' not found.";
                            return null;
                        }
                    }
                    else
                    {
                        var tpl = builder.GetConnectionString("DefaultConnectionWithPW");
                        if (string.IsNullOrEmpty(tpl))
                        {
                            strError = "Connection string 'DefaultConnectionWithPW' not found.";
                            return null;
                        }
                        connTemplate = tpl.Replace("<userid>", username).Replace("<pw>", password);
                    }
                    break;

                case Database.MISD:
                    if (string.IsNullOrEmpty(username))
                    {
                        connTemplate = builder.GetConnectionString("MISD");
                        if (string.IsNullOrEmpty(connTemplate))
                        {
                            strError = "Connection string 'MISD' not found.";
                            return null;
                        }
                    }
                    else
                    {
                        var tpl = builder.GetConnectionString("MISDWithPW");
                        if (string.IsNullOrEmpty(tpl))
                        {
                            strError = "Connection string 'MISDWithPW' not found.";
                            return null;
                        }
                        connTemplate = tpl.Replace("<userid>", username).Replace("<pw>", password);
                    }
                    break;

                // Add other `Database` cases similarly if needed.
                default:
                    strError = $"No connection configuration for database enum value '{db}'.";
                    return null;
            }

            // SqlConnection.ConnectionString expects a non-nullable string.
            // Use null-coalescing to ensure a non-null value is assigned (shouldn't be needed due to checks above,
            // but satisfies the compiler and avoids CS8600/CS8604).
            sqlConn.ConnectionString = connTemplate ?? string.Empty;

            try
            {
                sqlConn.Open();
                return sqlConn;
            }
            catch (Exception ex)
            {
                strError = ex.Message;
                Console.WriteLine("Connection failed!\n" + ex.Message, "Log-in failed");
                return null;
            }
        }

        public bool DoAction(Database db = Database.Default, string username = "", string password = "")
        {
            SqlConnection tmpConn = new SqlConnection();
            if (blnTransaction)
            {
                tmpConn = connData;
            }
            else
            {
                tmpConn = NewConnection(db, username, password);
            }
            SqlCommand sqlComm = new SqlCommand();
            using (sqlComm)
            {
                if (blnTransaction)
                {
                    sqlComm.Transaction = tranSQL;
                    sqlComm.Connection = connData;
                }
                else
                {
                    sqlComm.Connection = tmpConn;
                }
                sqlComm.CommandType = System.Data.CommandType.StoredProcedure;
                sqlComm.CommandText = strSPname;
                sqlComm.CommandTimeout = 0;
                lstOutput.Clear();
                foreach (clsParameters item in lstParameter)
                {
                    switch (item.prmType)
                    {
                        case vbDataType.dtDate:
                            sqlComm.Parameters.AddWithValue(item.prmName, Convert.ToDateTime(item.prmValue));
                            break;
                        case vbDataType.dtDecimal:
                            sqlComm.Parameters.AddWithValue(item.prmName, Convert.ToDecimal(item.prmValue));
                            break;
                        case vbDataType.dtDouble:
                            sqlComm.Parameters.AddWithValue(item.prmName, Convert.ToDouble(item.prmValue));
                            break;
                        case vbDataType.dtInteger:
                            sqlComm.Parameters.AddWithValue(item.prmName, Convert.ToInt32(item.prmValue));
                            break;
                        case vbDataType.dtNull:
                            sqlComm.Parameters.AddWithValue(item.prmName, DBNull.Value);
                            break;
                        default:
                            sqlComm.Parameters.AddWithValue(item.prmName, item.prmValue);
                            break;

                    }
                    //sqlComm.Parameters.AddWithValue(item.prmName, item.prmValue);
                    if (item.prmOutPut)
                    {
                        sqlComm.Parameters[item.prmName].Direction = System.Data.ParameterDirection.Output;
                        sqlComm.Parameters[item.prmName].Size = 1000;
                        lstOutput.Add(new clsOutput() { oName = item.prmName, oValue = item.prmValue });
                    }
                }

                foreach (clsByteParameter item in lstByteParameter)
                {
                    sqlComm.Parameters.AddWithValue(item.prmName, item.prmValue);
                }
                lstByteParameter.Clear();
                lstParameter.Clear();
                try
                {
                    sqlComm.ExecuteNonQuery();
                    foreach (clsOutput item in lstOutput)
                    {
                        if (Convert.IsDBNull(sqlComm.Parameters[item.oName].Value))
                        {
                            item.oValue = "";
                        }
                        else
                        {
                            item.oValue = sqlComm.Parameters[item.oName].Value.ToString();
                        }
                    }
                    sqlComm.Dispose();
                    if (blnTransaction == false)
                    {
                        tmpConn.Close();
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    ErrorMessage = ex.Message;
                    Console.WriteLine(ex.Message);
                    //MessageBox.Show(ex.Message, "Warning", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    return false;
                }
            }
        }


        public SqlDataReader GetData(Database db = Database.Default, string username = "", string password = "")
        {
            connData = NewConnection(db, username, password);
            SqlCommand sqlComm = new SqlCommand();
            using (sqlComm)
            {
                sqlComm.Connection = connData;
                sqlComm.CommandType = System.Data.CommandType.StoredProcedure;
                sqlComm.CommandText = strSPname;
                sqlComm.CommandTimeout = 0;

                foreach (clsParameters item in lstParameter)
                {
                    sqlComm.Parameters.AddWithValue(item.prmName, item.prmValue);
                }
                foreach (clsByteParameter item in lstByteParameter)
                {
                    sqlComm.Parameters.AddWithValue(item.prmName, item.prmValue);
                }
                lstByteParameter.Clear();
                lstParameter.Clear();
                try
                {
                    return sqlComm.ExecuteReader();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    ErrorMessage = ex.Message;
                    //MessageBox.Show(ex.Message, "Warning", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    return null /* TODO Change to default(_) if this is not a reference type */;
                }
            }
        }

        public string Output(string ParameterName)
        {
            var tmpOutput = lstOutput.Find(o => o.oName == ParameterName);
            if (tmpOutput == null)
            {
                return "";
            }
            else
            {
                return tmpOutput.oValue;
            }

        }

        public async Task<bool> ConnectedAsync(string User, string Password)
        {
            var location = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string appRoot;
            if (string.IsNullOrEmpty(location))
            {
                appRoot = AppContext.BaseDirectory;
            }
            else
            {
                appRoot = Path.GetDirectoryName(location) ?? AppContext.BaseDirectory;
            }

            var builder = new ConfigurationBuilder()
                .SetBasePath(appRoot)
                .AddJsonFile("appsettings.json").Build();

            var sqlConn = new SqlConnection();

            var tpl = builder.GetConnectionString("Login");
            if (string.IsNullOrEmpty(tpl))
            {
                ErrorMessage = "Connection string 'Login' not found.";
                return false;
            }

            sqlConn.ConnectionString = tpl.Replace("<userid>", User).Replace("<pw>", Password);

            try
            {
                await sqlConn.OpenAsync();
                return true;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Connection attempt failed: {ex.Message}";
                Console.WriteLine($"❌ ConnectedAsync error: {ex.Message}");
                return false;
            }
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    if (connData != null)
                    {
                        connData.Close();
                        connData.Dispose();
                    }

                    // TODO: dispose managed state (managed objects).
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.

                disposedValue = true;
            }
        }

        // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        // ~SQLActions() {
        //   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        //   Dispose(false);
        // }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // TODO: uncomment the following line if the finalizer is overridden above.
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
