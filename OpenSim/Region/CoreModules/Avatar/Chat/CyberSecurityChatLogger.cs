/* Add the following project refrences:
 *	- System.Data
 *	- bin/MySql.Data.dll
 */

using System;
using System.IO;
using System.Xml;
using System.Data;
using System.Reflection;
using System.Collections.Generic;
using log4net;
using MySql.Data.MySqlClient;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Data;
using System.Net.Sockets;

namespace OpenSim.Region.CoreModules.Avatar.Chat
{
	static class CyberSecurityChatLogger
	{
		private const string DB_CONNECTION_STRING = "Server=virtualdiscoverycenter.net;Database=opensim;Uid=cybersecurity;Pwd=burrtango;";
		private const string TCP_PROXY_SERVER = "virtualdiscoverycenter.net";
		private const int TCP_PROXY_PORT = 8019;

		public const string LOG_NAME = "CHAT LOGGER";

		private static readonly ILog m_log =
			LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

		public static void logChat(int? channel, string contents, Vector3 location, string sender, string receiver, string region)
		{
			// This *ought* to give time since unix epoch in milliseconds which is what Java needs
			TimeSpan t = (DateTime.UtcNow - new DateTime(1970, 1, 1).ToLocalTime());
			long timestamp = (long) t.TotalMilliseconds;

            // Log to console
			string rcvStr = receiver;
			if (rcvStr == null)
			{
				rcvStr = "NULL";
			}
			m_log.InfoFormat("[{0}]: <{1}> <{2}>: {3}", LOG_NAME, sender, rcvStr, contents);

			// Log to TCP proxy
			sendTCP(TCP_PROXY_SERVER, TCP_PROXY_PORT,
				SerializeChat(channel, contents, location, sender, receiver, timestamp, region) + '\n');

			// Log to database
			MySqlConnection dbcon = new MySqlConnection(DB_CONNECTION_STRING);
			try
			{
				dbcon.Open();

				string cmdStr = null;
				// This is a local chat
				if (receiver == null)
				{
					cmdStr = "INSERT INTO `opensim`.`chatLogs` (`time`, `from`, `to`, `message`, `channel`, `location`, `region`) VALUES (@time, @from, NULL, @message, @channel, @location, @region);";
				}
				// This is a private chat
				else if (channel == null)
				{
					cmdStr = "INSERT INTO `opensim`.`chatLogs` (`time`, `from`, `to`, `message`, `channel`, `location`, `region`) VALUES (@time, @from, @to, @message, NULL, @location, @region);";
				}
				else
				{
					m_log.ErrorFormat("[{0}]: What kind of message IS this???", LOG_NAME);
					return;
				}


				MySqlCommand cmd = new MySqlCommand(cmdStr, dbcon);
				cmd.Parameters.AddWithValue("@time", timestamp);
				cmd.Parameters.AddWithValue("@from", sender);
				if (receiver != null)
					cmd.Parameters.AddWithValue("@to", receiver);
				cmd.Parameters.AddWithValue("@message", contents);
				if (channel != null)
					cmd.Parameters.AddWithValue("@channel", channel);
				cmd.Parameters.AddWithValue("@location", location.ToString());
				cmd.Parameters.AddWithValue("@region", region);

				cmd.ExecuteNonQuery();

				dbcon.Close();
			}
			catch (MySqlException e)
			{
				switch (e.Number)
				{
					case (int)MySqlErrorCode.None:
						m_log.ErrorFormat("[{0}]: Cannot connect to chat log server!", LOG_NAME);
						break;

					case (int)MySqlErrorCode.AccessDenied:
						m_log.ErrorFormat("[{0}]: Access denied connecting to chat log server.", LOG_NAME);
						break;

					case (int)MySqlErrorCode.NoSuchTable:
						m_log.ErrorFormat("[{0}]: Table not found on chat log server!", LOG_NAME);
						break;

					case (int)MySqlErrorCode.TableAccessDenied:
						m_log.ErrorFormat("[{0}]: Access denied to table on chat log server.", LOG_NAME);
						break;

					case (int)MySqlErrorCode.BadFieldError:
						m_log.ErrorFormat("[{0}]: Bad feild: Chat log database table is incorrectly set up.", LOG_NAME);
						break;

					default:
						m_log.ErrorFormat("[{0}]: MySqlErrorCode {1} logging chat message.", LOG_NAME, e.Number);
						break;
				}
			}
		}

        public static void sendTCP(string server, int port, string message)
        {
			try
			{
				Byte[] data = System.Text.Encoding.ASCII.GetBytes(message);

				TcpClient client = new TcpClient(server, port);
				NetworkStream stream = client.GetStream();

				stream.Write(data, 0, data.Length);

				client.Close();
			}
			catch (ArgumentNullException)
			{
				m_log.ErrorFormat("[{0}]: ArgumentNullException sending to TCP server.", LOG_NAME);
			}
			catch (SocketException)
			{
				m_log.ErrorFormat("[{0}]: Could not connect to TCP server!", LOG_NAME);
			}
        }

		public static string SerializeChat(int? channel, string contents, Vector3 location, string sender, string receiver, long timestamp, string region)
		{
			StringWriter stringWriter = new StringWriter();
			using (XmlWriter writer = XmlWriter.Create(stringWriter))
			{
				writer.WriteStartDocument();
				writer.WriteStartElement("message");

				if(channel != null)
					writer.WriteElementString("channel", channel.ToString());

				writer.WriteElementString("contents", contents);

				writer.WriteStartElement("location");
				writer.WriteElementString("region", region);
				writer.WriteElementString("x", location.X.ToString());
				writer.WriteElementString("y", location.Y.ToString());
				writer.WriteElementString("z", location.Z.ToString());
				writer.WriteEndElement();

				writer.WriteElementString("sender", sender);
				if(receiver != null)
					writer.WriteElementString("receiver", receiver);

				writer.WriteElementString("time", timestamp.ToString());

				writer.WriteEndElement();
				writer.WriteEndDocument();
			}

			return stringWriter.ToString();
		}
	}
}
