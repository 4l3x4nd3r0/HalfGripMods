using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BepInEx.Logging;
using HalfGripTools;
using UnityEngine;

namespace Multiplayer
{
	/// <summary>
	/// One checksum per loaded mod, and one combined checksum for the whole set. A room only accepts players
	/// whose combined checksum is the same as the host's, so nobody races with a different set of mods, items or art.
	/// </summary>
	internal static class Checksums
	{
		internal const int Protocol = 6;

		internal sealed class ModSum
		{
			internal string Id;
			internal string Version;
			internal string Hash;
		}

		private static readonly string[] TextExtensions = { ".cs", ".json", ".txt", ".md" };

		private static List<ModSum> mods;
		private static string combined;

		/// <summary>Every loaded mod (this one included), sorted by id.</summary>
		internal static IReadOnlyList<ModSum> Mods
		{
			get
			{
				Ensure();
				return mods;
			}
		}

		/// <summary>The checksum rooms compare: all mods plus game version, loader version and protocol.</summary>
		internal static string Combined
		{
			get
			{
				Ensure();
				return combined;
			}
		}

		/// <summary>The mod list in one line, <c>id@version:hash8;...</c>, so a mismatch can say which mod differs.</summary>
		internal static string ModLine
		{
			get
			{
				Ensure();
				return string.Join(";", mods.Select(m => m.Id + "@" + m.Version + ":" + m.Hash.Substring(0, 8)).ToArray());
			}
		}

		/// <summary>Computes lazily: by the time a menu exists every mod has loaded, which is not true while this mod loads.</summary>
		private static void Ensure()
		{
			if (combined != null)
			{
				return;
			}
			ManualLogSource log = Mod.Log;
			mods = new List<ModSum>();
			try
			{
				// HalfGripTools keeps the loaded mods in an internal list; there is no public way to read it.
				Type loader = typeof(HalfGripMod).Assembly.GetType("HalfGripTools.ModLoader");
				PropertyInfo property = loader == null ? null : loader.GetProperty("Mods", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
				IEnumerable contexts = property == null ? null : property.GetValue(null, null) as IEnumerable;
				if (contexts == null)
				{
					log.LogError("Could not read the loaded mod list from HalfGripTools (ModLoader.Mods). Rooms will refuse everyone.");
				}
				else
				{
					foreach (object item in contexts)
					{
						ModContext context = item as ModContext;
						if (context != null)
						{
							mods.Add(new ModSum { Id = context.Manifest.id, Version = context.Manifest.version, Hash = HashFolder(context.Folder) });
						}
					}
				}
			}
			catch (Exception e)
			{
				log.LogError("Mod checksums failed: " + e);
			}
			mods.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

			StringBuilder text = new StringBuilder();
			text.Append("protocol|").Append(Protocol).Append('\n');
			text.Append("game|").Append(Application.version).Append('\n');
			text.Append("tools|").Append(typeof(HalfGripMod).Assembly.GetName().Version).Append('\n');
			foreach (ModSum mod in mods)
			{
				text.Append("mod|").Append(mod.Id).Append('|').Append(mod.Version).Append('|').Append(mod.Hash).Append('\n');
			}
			combined = Hex(Sha(Encoding.UTF8.GetBytes(text.ToString()))).Substring(0, 32);

			log.LogInfo("Mod checksum " + combined.Substring(0, 8) + " over " + mods.Count + " mod(s): " + ModLine);
		}

		/// <summary>SHA256 over every file in a mod folder: relative path and bytes, in a fixed order.</summary>
		private static string HashFolder(string folder)
		{
			using (SHA256 sha = SHA256.Create())
			{
				string root = Path.GetFullPath(folder);
				string[] files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
				List<string> relative = files.Select(f => f.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/')).ToList();
				relative.Sort(string.CompareOrdinal);
				foreach (string path in relative)
				{
					byte[] name = Encoding.UTF8.GetBytes(path + "\n");
					sha.TransformBlock(name, 0, name.Length, null, 0);
					byte[] data = File.ReadAllBytes(Path.Combine(root, path));
					if (TextExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
					{
						data = StripCarriageReturns(data);
					}
					sha.TransformBlock(data, 0, data.Length, null, 0);
				}
				sha.TransformFinalBlock(new byte[0], 0, 0);
				return Hex(sha.Hash);
			}
		}

		/// <summary>A text file checked out with CRLF on one PC and LF on another is still the same mod.</summary>
		private static byte[] StripCarriageReturns(byte[] data)
		{
			List<byte> kept = new List<byte>(data.Length);
			foreach (byte b in data)
			{
				if (b != 13)
				{
					kept.Add(b);
				}
			}
			return kept.ToArray();
		}

		private static byte[] Sha(byte[] data)
		{
			using (SHA256 sha = SHA256.Create())
			{
				return sha.ComputeHash(data);
			}
		}

		private static string Hex(byte[] bytes)
		{
			StringBuilder text = new StringBuilder(bytes.Length * 2);
			foreach (byte b in bytes)
			{
				text.Append(b.ToString("x2"));
			}
			return text.ToString();
		}
	}
}
