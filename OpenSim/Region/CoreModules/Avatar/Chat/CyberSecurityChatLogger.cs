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
	public static class CyberSecurityChatLogger
	{
		private const string DB_CONNECTION_STRING = "Server=virtualdiscoverycenter.net;Database=opensim;Uid=cybersecurity;Pwd=burrtango;";
		private const string TCP_PROXY_SERVER = "virtualdiscoverycenter.net";
		private const int TCP_PROXY_PORT = 8019;

		public const string LOG_NAME = "CYBERSECURITY LOGGER";

		private static readonly ILog m_log =
			LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

		public static void logLogin(string name, UUID uuid, bool successful, string ip, string viewer)
		{
			// Log to database
			MySqlConnection dbcon = new MySqlConnection(DB_CONNECTION_STRING);
			try
			{
				dbcon.Open();

				string cmdStr = "INSERT INTO `opensim`.`loginHistory` (`time`, `name`, `uuid`, `successful`, `ip`, `viewer`) VALUES (@time, @name, @uuid, @successful, @viewer);";

				MySqlCommand cmd = new MySqlCommand(cmdStr, dbcon);
				cmd.Parameters.AddWithValue("@time", getJavaTimestamp().ToString());
				cmd.Parameters.AddWithValue("@name", name);
				cmd.Parameters.AddWithValue("@uuid", uuid.ToString());
				if(successful)
					cmd.Parameters.AddWithValue("@successful", 1);
				else
					cmd.Parameters.AddWithValue("@successful", 0);
				cmd.Parameters.AddWithValue("@ip", ip);
				cmd.Parameters.AddWithValue("@viewer", viewer);

				cmd.ExecuteNonQuery();

				dbcon.Close();
			}
			catch (MySqlException e)
			{
				handleMySQLError(e);
			}
		}

		public static void logChat(int? channel, string contents, Vector3 location, string sender, string receiver, string region, UUID fromUuid, UUID? toUuid)
		{
			long timestamp = getJavaTimestamp();

            // Log to console
			string rcvStr = receiver;
			if (rcvStr == null)
			{
				rcvStr = "NULL";
			}
			m_log.InfoFormat("[{0}]: <{1}> <{2}>: {3}", LOG_NAME, sender, rcvStr, contents);

			// Log to TCP proxy
			sendTCP(TCP_PROXY_SERVER, TCP_PROXY_PORT,
				SerializeChat(channel, contents, location, sender, receiver, timestamp, region, fromUuid, toUuid) + '\n');

			// Log to database
			MySqlConnection dbcon = new MySqlConnection(DB_CONNECTION_STRING);
			try
			{
				dbcon.Open();

				string cmdStr = null;
				// This is a local chat
				if (receiver == null)
				{
					cmdStr = "INSERT INTO `opensim`.`chatLogs` (`time`, `from`, `to`, `message`, `channel`, `location`, `region`, `fromUuid`, `toUuid`) VALUES (@time, @from, NULL, @message, @channel, @location, @region, @fromUuid, NULL);";
				}
				// This is a private chat
				else if (channel == null)
				{
					cmdStr = "INSERT INTO `opensim`.`chatLogs` (`time`, `from`, `to`, `message`, `channel`, `location`, `region`, `fromUuid`, `toUuid`) VALUES (@time, @from, @to, @message, NULL, @location, @region, @fromUuid, @toUuid);";
				}
				else
				{
					m_log.ErrorFormat("[{0}]: What kind of message IS this???", LOG_NAME);
					return;
				}


				MySqlCommand cmd = new MySqlCommand(cmdStr, dbcon);
				cmd.Parameters.AddWithValue("@time", timestamp.ToString());
				cmd.Parameters.AddWithValue("@from", sender);
				if (receiver != null)
					cmd.Parameters.AddWithValue("@to", receiver);
				cmd.Parameters.AddWithValue("@message", contents);
				if (channel != null)
					cmd.Parameters.AddWithValue("@channel", channel);
				cmd.Parameters.AddWithValue("@location", location.ToString());
				cmd.Parameters.AddWithValue("@region", region);
				cmd.Parameters.AddWithValue("@fromUuid", fromUuid.ToString());
				if (toUuid != null)
					cmd.Parameters.AddWithValue("@toUuid", toUuid.ToString());

				cmd.ExecuteNonQuery();

				dbcon.Close();
			}
			catch (MySqlException e)
			{
				handleMySQLError(e);
			}
		}

        public static void sendTCP(string server, int port, string message)
        {
			try
			{
				Byte[] data = System.Text.Encoding.UTF8.GetBytes(message);

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

		public static long getJavaTimestamp()
		{
			// This *ought* to give time since unix epoch in milliseconds which is what Java needs
			TimeSpan t = (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0).ToUniversalTime());
			return (long)t.TotalMilliseconds;
		}

		private static void handleMySQLError(MySqlException e)
		{
			switch (e.Number)
			{
				case (int)MySqlErrorCode.None:
					m_log.ErrorFormat("[{0}]: Cannot connect to database!", LOG_NAME);
					break;

				case (int)MySqlErrorCode.AccessDenied:
					m_log.ErrorFormat("[{0}]: Access denied connecting to database.", LOG_NAME);
					break;

				case (int)MySqlErrorCode.NoSuchTable:
					m_log.ErrorFormat("[{0}]: Table not found in database!", LOG_NAME);
					break;

				case (int)MySqlErrorCode.TableAccessDenied:
					m_log.ErrorFormat("[{0}]: Access denied to database table.", LOG_NAME);
					break;

				case (int)MySqlErrorCode.BadFieldError:
					m_log.ErrorFormat("[{0}]: Bad feild: Database table is incorrectly set up.", LOG_NAME);
					break;

				default:
					m_log.ErrorFormat("[{0}]: MySqlErrorCode {1} while logging event.", LOG_NAME, e.Number);
					break;
			}
		}

		private static string SerializeChat(int? channel, string contents, Vector3 location, string sender, string receiver, long timestamp, string region, UUID fromUuid, UUID? toUuid)
		{
			StringWriter stringWriter = new Utf8StringWriter();
			XmlWriterSettings settings = new XmlWriterSettings();
			settings.Encoding = System.Text.Encoding.UTF8;
			using (XmlWriter writer = XmlWriter.Create(stringWriter, settings))
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

				writer.WriteStartElement("sender");
				writer.WriteElementString("name", sender);
				writer.WriteElementString("uuid", fromUuid.ToString());
				writer.WriteEndElement();

				if (receiver != null)
				{
					writer.WriteStartElement("receiver");
					writer.WriteElementString("name", receiver);
					writer.WriteElementString("uuid", toUuid.ToString());
					writer.WriteEndElement();
				}

				writer.WriteElementString("time", timestamp.ToString());

				writer.WriteEndElement();
				writer.WriteEndDocument();
			}

			return stringWriter.ToString();
		}

		private class Utf8StringWriter : StringWriter
		{
			public override System.Text.Encoding Encoding
			{
				get
				{
					return System.Text.Encoding.UTF8;
				}
			}
		}
	}
}
