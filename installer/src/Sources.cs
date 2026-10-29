using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace HalfGripModsInstaller
{
	/// <summary>Where to download a zip from, and how to describe it to the player.</summary>
	internal sealed class Resolved
	{
		internal string ZipUrl;

		internal string Describe;
	}

	/// <summary>
	/// A place mods come from: a GitHub repository (or one folder of it) or a direct .zip link. What the player pasted is
	/// turned into a download address here, nothing else is accepted.
	/// </summary>
	internal sealed class Source
	{
		internal string Text = "";

		internal string Owner = "";

		internal string Repo = "";

		/// <summary>Branch or tag from a .../tree/&lt;ref&gt; link. Empty means the latest release.</summary>
		internal string Ref = "";

		/// <summary>Folder inside the repository from a .../tree/&lt;ref&gt;/&lt;folder&gt; link. Empty means all of it.</summary>
		internal string SubPath = "";

		internal string DirectZip = "";

		/// <summary>True for a source from the remote recommended list: shown as a suggestion, never installed without a click.</summary>
		internal bool Recommended;

		/// <summary>Name, author and note the recommended list gives this source.</summary>
		internal string Label = "";

		internal string By = "";

		internal string Note = "";

		/// <summary>Same repository, branch and folder give the same identity, whatever the spelling of the link.</summary>
		internal string Identity => Key + "|" + Ref + "|" + SubPath;

		/// <summary>Short name shown in the window.</summary>
		internal string Name
		{
			get
			{
				if (DirectZip != "")
				{
					return Path.GetFileName(new Uri(DirectZip).AbsolutePath);
				}
				return Owner + "/" + Repo + (SubPath != "" ? "/" + SubPath : "");
			}
		}

		/// <summary>Same repository gives the same key, so the installer can tell which source an installed mod came from.</summary>
		internal string Key => (DirectZip != "" ? DirectZip : Owner + "/" + Repo).ToLowerInvariant();

		/// <summary>The folder name used for a mod.json that sits at the very top of the source.</summary>
		internal string TopName => DirectZip != "" ? Path.GetFileNameWithoutExtension(new Uri(DirectZip).AbsolutePath) : (SubPath != "" ? SubPath.Substring(SubPath.LastIndexOf('/') + 1) : Repo);

		/// <summary>
		/// True for the official mods: they always come from the main branch. The repository also holds the installer's own releases,
		/// and "latest release" would otherwise freeze the mods at whatever commit the last installer was tagged on.
		/// </summary>
		internal bool UseMainCode;

		/// <summary>For trying things out (--mods-dir): a folder on this PC to read the mods from instead of downloading them.</summary>
		internal string LocalDir = "";

		internal static string OfficialLocalDir = "";

		internal static Source Official
		{
			get
			{
				Source source = Parse(Config.Current.ModsRepo);
				source.UseMainCode = true;
				source.LocalDir = OfficialLocalDir;
				return source;
			}
		}

		/// <summary>Accepts https://github.com/owner/repo, owner/repo, a .git ending, .../tree/branch[/folder] and a direct .zip link.</summary>
		internal static Source Parse(string input)
		{
			string text = (input ?? "").Trim().Trim('"').Trim();
			if (text.Length == 0)
			{
				throw new PlainException("Paste a link first.");
			}
			Source source = new Source { Text = text };
			string path = text;
			if (Regex.IsMatch(text, "^[A-Za-z][A-Za-z0-9+.-]*://"))
			{
				if (!Uri.TryCreate(text, UriKind.Absolute, out Uri uri) || (uri.Scheme != "https" && uri.Scheme != "http"))
				{
					throw new PlainException("That is not a web link. It should look like https://github.com/owner/repo");
				}
				if (uri.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
				{
					source.DirectZip = uri.AbsoluteUri;
					return source;
				}
				if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) && !uri.Host.Equals("www.github.com", StringComparison.OrdinalIgnoreCase))
				{
					throw new PlainException("Only GitHub links and direct .zip links work. This one is from " + uri.Host + ".");
				}
				path = uri.AbsolutePath;
			}
			else if (Regex.IsMatch(text, "^(www\\.)?github\\.com/", RegexOptions.IgnoreCase))
			{
				path = text.Substring(text.IndexOf('/') + 1);
			}
			path = path.Trim('/');
			Match m = Regex.Match(path, "^(?<o>[A-Za-z0-9-]+)/(?<r>[A-Za-z0-9_.-]+?)(?:\\.git)?(?:/tree/(?<ref>[^/]+)(?:/(?<sub>.+))?)?$");
			if (!m.Success)
			{
				throw new PlainException("That does not look like a GitHub link. It should look like https://github.com/owner/repo");
			}
			source.Owner = m.Groups["o"].Value;
			source.Repo = m.Groups["r"].Value;
			source.Ref = m.Groups["ref"].Value;
			source.SubPath = m.Groups["sub"].Value.Trim('/');
			foreach (string part in source.SubPath.Split('/'))
			{
				if (part == "..")
				{
					throw new PlainException("That link has a '..' in it, which is not allowed.");
				}
			}
			return source;
		}

		/// <summary>
		/// Picks what to download: the exact branch or tag of the link, else the repository's latest release, else its main code.
		/// Releases come first so that a published release of someone's mods is what people get, not unfinished work.
		/// </summary>
		internal Resolved Resolve()
		{
			if (DirectZip != "")
			{
				return new Resolved { ZipUrl = DirectZip, Describe = "zip file" };
			}
			string baseUrl = "https://github.com/" + Owner + "/" + Repo;
			if (UseMainCode && Ref == "")
			{
				return new Resolved { ZipUrl = baseUrl + "/archive/HEAD.zip", Describe = "main code" };
			}
			if (Ref != "")
			{
				return new Resolved { ZipUrl = baseUrl + "/archive/" + Uri.EscapeDataString(Ref) + ".zip", Describe = Ref };
			}
			string json = Net.GetString("https://api.github.com/repos/" + Owner + "/" + Repo + "/releases/latest");
			if (json != null && MiniJson.Parse(json) is Dictionary<string, object> release)
			{
				string tag = MiniJson.String(release, "tag_name");
				if (tag != "")
				{
					return new Resolved { ZipUrl = baseUrl + "/archive/refs/tags/" + Uri.EscapeDataString(tag) + ".zip", Describe = "release " + tag };
				}
			}
			return new Resolved { ZipUrl = baseUrl + "/archive/HEAD.zip", Describe = "latest code" };
		}
	}
}
