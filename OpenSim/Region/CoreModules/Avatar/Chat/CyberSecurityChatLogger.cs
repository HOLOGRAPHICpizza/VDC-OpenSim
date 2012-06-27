/* Add the following project refrences:
 *	- System.Data
 *	- bin/MySql.Data.dll
 */

using System;
using System.Data;
using System.Reflection;
using System.Collections.Generic;
using log4net;
using MySql.Data.MySqlClient;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Data;

namespace OpenSim.Region.CoreModules.Avatar.Chat
{
	static class CyberSecurityChatLogger
	{
		private const string CONNECTION_STRING = "Server=virtualdiscoverycenter.net;Database=opensim;Uid=cybersecurity;Pwd=burrtango;";

		public const string LOG_NAME = "CHAT LOGGER";

		private static readonly ILog m_log =
			LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

		public static void logChat(string from, string to, string message)
		{
			m_log.InfoFormat("[{0}]: <{1}> <{2}>: {3}", LOG_NAME, from, to, message);

			MySqlConnection dbcon = new MySqlConnection(CONNECTION_STRING);

			try
			{
				dbcon.Open();

				String cmdStr = "INSERT INTO `opensim`.`chatLogs` (`time`, `from`, `to`, `message`) VALUES (@time, @from, @to, @message);";

				MySqlCommand cmd = new MySqlCommand(cmdStr, dbcon);
				cmd.Parameters.AddWithValue("@time", Util.UnixTimeSinceEpoch());
				cmd.Parameters.AddWithValue("@from", from);
				cmd.Parameters.AddWithValue("@to", to);
				cmd.Parameters.AddWithValue("@message", message);

				cmd.ExecuteNonQuery();

				dbcon.Close();
			}
			catch(MySqlException e)
			{
				switch (e.Number)
				{
					case (int) MySqlErrorCode.None:
						m_log.ErrorFormat("[{0}]: Cannot connect to chat log server!", LOG_NAME);
						break;

					case (int) MySqlErrorCode.AccessDenied:
						m_log.ErrorFormat("[{0}]: Access denied connecting to chat log server.", LOG_NAME);
						break;

					case (int) MySqlErrorCode.NoSuchTable:
						m_log.ErrorFormat("[{0}]: Table not found on chat log server!", LOG_NAME);
						break;

					case (int) MySqlErrorCode.TableAccessDenied:
						m_log.ErrorFormat("[{0}]: Access denied to table on chat log server.", LOG_NAME);
						break;

					default:
						m_log.ErrorFormat("[{0}]: MySqlErrorCode {1} logging chat message.", LOG_NAME, e.Number);
						break;
				}
			}
		}
	}
}
