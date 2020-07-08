﻿using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.SQL;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Security;

namespace SQLBindingExtension
{
    internal class SQLConverters
    {
        internal  class SQLConverter : IConverter<SQLBindingAttribute, SqlCommand>
        {
            private SqlConnectionWrapper _connection;

            /// <summary>
            /// Used for testing, so we can pass a dummy SqlConnection to the converter and avoid it trying to build 
            /// an actual one
            /// </summary>
            /// <param name="connection"> The dummy SqlConnection </param>
            public SQLConverter(SqlConnectionWrapper connection)
            {
                _connection = connection;
            }

            public SQLConverter() { }

            public SqlCommand Convert(SQLBindingAttribute attribute)
            {
                _connection = BuildConnection(_connection, attribute);
                SqlCommand command = new SqlCommand(attribute.SQLQuery + " FOR JSON AUTO", _connection.GetConnection());
                return command;
            }

        }

        internal class SQLGenericsConverter<T> : IConverter<SQLBindingAttribute, IEnumerable<T>>
        {
            private SqlConnectionWrapper _connection;

            /// <summary>
            /// Used for testing, so we can pass a dummy SqlConnection to the converter and avoid it trying to build 
            /// an actual one
            /// </summary>
            /// <param name="connection"> The dummy SqlConnection </param>
            public SQLGenericsConverter(SqlConnectionWrapper connection)
            {
                _connection = connection;
            }

            public SQLGenericsConverter() { }

            /// <summary>
            /// Opens a SqlConnection, reads in the data from the user's database, and returns it as a list of POCOs. Throws an exception if 
            /// the SqlConnection cannot be established or the data cannot be read from the database <see cref="BuildItemFromAttribute(SQLBindingAttribute)"/>
            /// </summary>
            /// <param name="attribute">
            /// Contains the information necessary to establish a SqlConnection, and the query to be executed on the database
            /// </param>
            /// <returns>An IEnumerable containing the rows read from the user's database in the form of the user-defined POCO</returns>
            public IEnumerable<T> Convert(SQLBindingAttribute attribute)
            {
                string json = BuildItemFromAttribute(attribute);
                return JsonConvert.DeserializeObject<IEnumerable<T>>(json);
            }

            /// <summary>
            /// Extracts the ConnectionString in attribute and uses it (in combination with the Authentication string, if provided) to establish a connection
            /// to the SQL database. (Must be virtual for mocking the method in unit tests)
            /// </summary>
            /// <param name="attribute">
            /// The binding attribute that contains the connection string, authentication, and query.
            /// </param>
            /// <exception cref="ArgumentNullException">
            /// Thrown when the ConnectionString in attribute is null.
            /// </exception>
            /// <exception cref="InvalidOperationException">
            /// Thrown if an exception occurs when opening the SQL connection or when running the query.
            /// </exception>
            /// <returns></returns>
            public virtual string BuildItemFromAttribute(SQLBindingAttribute attribute)
            {
                _connection = BuildConnection(_connection, attribute);
                string result = string.Empty;
                using (SqlConnection connection = _connection.GetConnection())
                {
                    try
                    {
                        string query = attribute.SQLQuery + " FOR JSON AUTO";
                        SqlCommand command = new SqlCommand(query, connection);
                        command.Connection.Open();
                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                result += reader[0];
                            }
                        }
                        command.Connection.Close();
                    }
                    catch (Exception e)
                    {
                        throw new InvalidOperationException("Exception in executing query: " + e.Message);
                    }

                }

                return result;
            }
        }

        internal static SqlConnectionWrapper BuildConnection(SqlConnectionWrapper connection, SQLBindingAttribute attribute)
        {
            if (attribute.ConnectionString == null)
            {
                throw new ArgumentNullException("Must specify a connection string to connect to your SQL server instance.");
            }

            if (connection == null)
            {
                connection = new SqlConnectionWrapper(attribute.ConnectionString);
            }
            connection.SetCredential(GetCredential(attribute.Authentication));
            return connection;
        }

        /// <summary>
        /// Extracts the User ID and password from the authentication string if authentication string is not null and returns a SqlCredential object
        /// with the authentication information. Returns null if authentication is null.
        /// </summary>
        /// <param name="authentication">
        /// The authentication string, must follow the format "User ID=<userid>;Password=<password>"
        /// </param>
        /// <returns>
        /// SqlCredential object with User ID and password in the authentication string. Null if authentication string is null.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Thrown if the authentication string is malformed.
        /// </exception>
        private static SqlCredential GetCredential(string authentication)
        {
            if (authentication == null)
            {
                return null;
            }

            string[] credentials = authentication.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            Dictionary<string, string> dict = new Dictionary<string, string>();
            foreach (var pair in credentials)
            {
                string[] items = pair.Split('=');
                if (items.Length != 2)
                {
                    throw new ArgumentException("Keys must be separated by \";\" and key and value must be separated by \"=\", i.e. " +
                        "\"User ID =<userid>;Password =<password>;\"");
                }
                dict.Add(items[0], items[1]);
            }

            string passwordStr;
            string username;
            if (!dict.TryGetValue("User ID", out username))
            {
                throw new ArgumentException("User ID must be specified in the Authentication string as \"User ID =<userid>;\"");
            }

            if (!dict.TryGetValue("Password", out passwordStr))
            {
                throw new ArgumentException("Password must be specified in the Authentication string as \"Password =<password>;\"");
            }

            SecureString password = new SecureString();
            for (int i = 0; i < passwordStr.Length; i++)
            {
                password.AppendChar(passwordStr[i]);
            }
            password.MakeReadOnly();
            return new SqlCredential(username, password);
        }
    }
}