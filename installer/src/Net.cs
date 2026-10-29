using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace HalfGripModsInstaller
{
	/// <summary>A problem the player can understand. The message is shown as it is, so write it for someone who does not code.</summary>
	internal sealed class PlainException : Exception
	{
		internal PlainException(string message) : base(message)
		{
		}
	}

	/// <summary>Every download goes through here: 3 tries, TLS 1.2, plain messages. Calls block, so run them off the window thread.</summary>
	internal static class Net
	{
		private static readonly HttpClient client = Create();

		private static HttpClient Create()
		{
			ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
			HttpClient created = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
			created.DefaultRequestHeaders.UserAgent.ParseAdd("HalfGrip-Mods-Installer/1.0");
			return created;
		}

		private static HttpResponseMessage Send(string url)
		{
			Exception last = null;
			for (int attempt = 1; attempt <= 3; attempt++)
			{
				try
				{
					HttpResponseMessage response = client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
					if ((response.StatusCode == HttpStatusCode.Forbidden && url.Contains("api.github.com")) || (int)response.StatusCode == 429)
					{
						throw new PlainException("GitHub is limiting how often this PC may ask. Wait an hour and run the installer again.");
					}
					return response;
				}
				catch (PlainException)
				{
					throw;
				}
				catch (Exception e)
				{
					last = e;
					Thread.Sleep(2000);
				}
			}
			throw new PlainException("Could not reach " + new Uri(url).Host + ". Check your internet connection and run the installer again. (" + (last?.InnerException?.Message ?? last?.Message) + ")");
		}

		/// <summary>The page as text, or null if it does not exist (404).</summary>
		internal static string GetString(string url)
		{
			using (HttpResponseMessage response = Send(url))
			{
				if (response.StatusCode == HttpStatusCode.NotFound)
				{
					return null;
				}
				if (!response.IsSuccessStatusCode)
				{
					throw new PlainException("The server answered " + (int)response.StatusCode + " for " + url);
				}
				return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
			}
		}

		/// <summary>Saves the file; false if it does not exist (404).</summary>
		internal static bool Download(string url, string destination)
		{
			using (HttpResponseMessage response = Send(url))
			{
				if (response.StatusCode == HttpStatusCode.NotFound)
				{
					return false;
				}
				if (!response.IsSuccessStatusCode)
				{
					throw new PlainException("The server answered " + (int)response.StatusCode + " for " + url);
				}
				using (Stream input = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
				using (FileStream output = File.Create(destination))
				{
					input.CopyTo(output);
				}
				return true;
			}
		}

		internal static string Sha256(string file)
		{
			using (SHA256 sha = SHA256.Create())
			using (FileStream stream = File.OpenRead(file))
			{
				StringBuilder hex = new StringBuilder();
				foreach (byte b in sha.ComputeHash(stream))
				{
					hex.Append(b.ToString("X2"));
				}
				return hex.ToString();
			}
		}

		/// <summary>Stops the install if the file is not the one that was published.</summary>
		internal static void CheckHash(string file, string expected, string what)
		{
			string actual = Sha256(file);
			if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
			{
				throw new PlainException(what + " did not match its checksum, so nothing was installed from it. Try again later.");
			}
		}
	}
}
