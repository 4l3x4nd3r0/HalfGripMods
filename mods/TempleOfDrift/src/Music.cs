using System;
using System.IO;
using UnityEngine;

namespace TempleOfDrift
{
	/// <summary>
	/// The map's own track, <c>audio\temple_of_drift.wav</c> (16-bit PCM, written by <c>art\make_music.py</c>). It becomes the map's
	/// <c>musicTrack</c>, so the game plays it through its Music mixer group and the player's music slider applies.
	/// </summary>
	internal static class Music
	{
		private static AudioClip clip;

		internal static AudioClip Load(string folder)
		{
			if (clip != null)
			{
				return clip;
			}
			try
			{
				string path = Path.Combine(Path.Combine(folder, "audio"), "temple_of_drift.wav");
				if (!File.Exists(path))
				{
					Mod.Log.LogInfo("No audio\\temple_of_drift.wav, the map plays the game's own race music.");
					return null;
				}
				byte[] bytes = File.ReadAllBytes(path);
				int channels = BitConverter.ToInt16(bytes, 22);
				int rate = BitConverter.ToInt32(bytes, 24);
				int bits = BitConverter.ToInt16(bytes, 34);
				if (bits != 16 || channels < 1 || channels > 2)
				{
					Mod.Log.LogWarning("temple_of_drift.wav must be 16-bit PCM, mono or stereo; it is " + bits + " bit, " + channels + " channels.");
					return null;
				}
				// Find the data chunk.
				int pos = 12;
				int dataAt = -1;
				int dataLength = 0;
				while (pos + 8 <= bytes.Length)
				{
					string id = System.Text.Encoding.ASCII.GetString(bytes, pos, 4);
					int size = BitConverter.ToInt32(bytes, pos + 4);
					if (id == "data")
					{
						dataAt = pos + 8;
						dataLength = Math.Min(size, bytes.Length - dataAt);
						break;
					}
					pos += 8 + size + (size & 1);
				}
				if (dataAt < 0)
				{
					Mod.Log.LogWarning("temple_of_drift.wav has no data chunk.");
					return null;
				}
				int samples = dataLength / 2;
				float[] data = new float[samples];
				for (int i = 0; i < samples; i++)
				{
					data[i] = BitConverter.ToInt16(bytes, dataAt + i * 2) / 32768f;
				}
				AudioClip made = AudioClip.Create("Temple of Drift OST", samples / channels, channels, rate, false);
				made.SetData(data, 0);
				made.hideFlags = HideFlags.HideAndDontSave;
				clip = made;
				Mod.Log.LogInfo("Temple of Drift music loaded: " + made.length.ToString("0.0") + " s, " + rate + " Hz, " + channels + " ch");
				return clip;
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Could not load the map's music: " + e.Message);
				return null;
			}
		}
	}
}
