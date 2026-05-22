using Oracle.ManagedDataAccess.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DBObjectEditor.Application
{
    class Oracle
    {
        /// <summary>
        /// parametre olarak verilen spyi devden getirir
        /// </summary>
        /// <param name="spAd"></param>
        /// <returns></returns>
        public static string OracleMevcutSpGetir(string dbConnectionString, string spAd)
        {
            StringBuilder spIcerik = new StringBuilder();

            string owner = "VITPROD";
            string objectName = spAd;

            string query = @"
            SELECT TEXT 
            FROM ALL_SOURCE 
            WHERE NAME = UPPER(:objectName)  AND OWNER = UPPER(:owner) 
            ORDER BY TYPE, LINE";

            using (OracleConnection connection = new OracleConnection(dbConnectionString))
            {
                using (OracleCommand command = new OracleCommand(query, connection))
                {
                    command.Parameters.Add(new OracleParameter("objectName", objectName));
                    command.Parameters.Add(new OracleParameter("owner", owner));

                    try
                    {
                        connection.Open();
                        using (OracleDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                spIcerik.Append(reader["TEXT"].ToString());
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"ERROR OracleMevcutSpGetir: {ex.Message}");
                        throw;
                    }
                }
            }

            return spIcerik.ToString();
        }


        /// <summary>
        /// ai'dan donen ve temizlenen sp oracle'a commit atilir
        /// </summary>
        /// <param name="sqlScript"></param>
        public static void OracleSpGuncelle(string dbConnectionString, string sqlScript)
        {
            using (OracleConnection connection = new OracleConnection(dbConnectionString))
            {
                using (OracleCommand command = new OracleCommand(sqlScript, connection))
                {
                    try
                    {
                        connection.Open();
                        command.ExecuteNonQuery();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"\nERROR Oracle Commit: {ex.Message}");
                        throw;
                    }
                }
            }
        }
    }
}
