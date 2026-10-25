using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Multiplayer
{
	/// <summary>Message types. The first byte of every packet.</summary>
	internal enum Msg : byte
	{
		Hello = 1,
		Welcome = 2,
		Roster = 3,
		SetReady = 4,
		Start = 6,
		Loaded = 7,
		Go = 8,
		Car = 9,
		Hit = 10,
		Wrecked = 11,
		Respawned = 12,
		Finished = 13,
		Results = 14,
		Bye = 16,
		AiState = 17,
		AiHit = 18,
		AiDead = 19,
		Kick = 20,
		Ping = 21,
		Stats = 22,
		Despawn = 23,
		EndRace = 24
	}

	/// <summary>Writes one packet.</summary>
	internal sealed class W
	{
		private readonly MemoryStream stream = new MemoryStream(64);
		private readonly BinaryWriter writer;

		internal W(Msg type)
		{
			writer = new BinaryWriter(stream, Encoding.UTF8);
			writer.Write((byte)type);
		}

		internal W U8(int v) { writer.Write((byte)v); return this; }
		internal W Bool(bool v) { writer.Write(v); return this; }
		internal W I32(int v) { writer.Write(v); return this; }
		internal W U64(ulong v) { writer.Write(v); return this; }
		internal W F32(float v) { writer.Write(v); return this; }
		internal W Str(string v)
		{
			string s = v ?? "";
			if (s.Length > 400)
			{
				s = s.Substring(0, 400);
			}
			writer.Write(s);
			return this;
		}
		internal W Vec(Vector3 v) { writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); return this; }
		internal W Rot(Quaternion q) { writer.Write(q.x); writer.Write(q.y); writer.Write(q.z); writer.Write(q.w); return this; }
		internal byte[] Bytes() { writer.Flush(); return stream.ToArray(); }
	}

	/// <summary>Reads one packet. A malformed packet throws; callers drop it.</summary>
	internal sealed class R
	{
		private readonly BinaryReader reader;

		internal Msg Type { get; private set; }

		internal R(byte[] data)
		{
			if (data == null || data.Length < 1 || data.Length > 4096)
			{
				throw new InvalidDataException("bad packet size");
			}
			reader = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
			Type = (Msg)reader.ReadByte();
		}

		internal int U8() { return reader.ReadByte(); }
		internal bool Bool() { return reader.ReadBoolean(); }
		internal int I32() { return reader.ReadInt32(); }
		internal ulong U64() { return reader.ReadUInt64(); }
		internal float F32() { return reader.ReadSingle(); }
		internal string Str() { return reader.ReadString(); }
		internal Vector3 Vec() { return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()); }
		internal Quaternion Rot() { return new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()); }
	}
}
