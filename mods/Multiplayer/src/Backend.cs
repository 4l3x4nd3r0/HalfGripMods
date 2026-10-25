using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Steamworks;
using Steamworks.Data;
using UnityEngine;

namespace Multiplayer
{
	/// <summary>A friend who could be invited.</summary>
	internal sealed class FriendInfo
	{
		internal ulong Id;
		internal string Name;
		internal bool Playing;
	}

	/// <summary>A room as seen from outside, before joining it.</summary>
	internal sealed class RoomRef
	{
		internal ulong Handle;
		internal ulong Owner;
		internal int Members;
		internal int Max;
		internal Dictionary<string, string> Data = new Dictionary<string, string>();

		internal string Get(string key)
		{
			string value;
			return Data.TryGetValue(key, out value) ? value : "";
		}
	}

	/// <summary>
	/// Everything the mod needs from the network, in one place: rooms (create, find by code, join, leave, who is in it) and
	/// a message pipe between members. <see cref="SteamBackend"/> is the real one; <see cref="LocalBackend"/> lets two copies of the
	/// game on one PC play each other without Steam, which is how the mod is tested.
	/// </summary>
	internal abstract class Backend
	{
		internal Action<ulong> MemberJoined;
		internal Action<ulong> MemberLeft;
		internal Action<RoomRef> InviteRequested;

		internal abstract string Name { get; }
		internal abstract bool Ready { get; }
		internal abstract ulong LocalId { get; }
		internal abstract string LocalName { get; }
		internal abstract bool InRoom { get; }
		internal abstract ulong OwnerId { get; }
		internal abstract List<ulong> Members();
		internal abstract void Create(int max, Action<bool, string> done);
		internal abstract void Find(string code, Action<RoomRef> done);
		internal abstract void Join(RoomRef room, Action<bool, string> done);
		internal abstract void Leave();
		internal abstract void SetRoomData(string key, string value);
		internal abstract void SetJoinable(bool joinable);
		internal abstract string GetRoomData(string key);
		internal virtual List<FriendInfo> Friends() { return new List<FriendInfo>(); }
		internal virtual void InviteFriend(ulong id) { }
		internal abstract void Send(ulong to, byte[] data, bool reliable);
		internal abstract bool Receive(out ulong from, out byte[] data);
		internal abstract void Tick();
		internal virtual void Dispose() { }
	}

	/// <summary>Steam lobbies for rooms and Steam P2P for messages. Facepunch.Steamworks is the library the game itself ships.</summary>
	internal sealed class SteamBackend : Backend
	{
		private struct Packet
		{
			internal ulong From;
			internal byte[] Data;
		}

		private readonly List<Func<bool>> pending = new List<Func<bool>>();
		private readonly Queue<Packet> inbox = new Queue<Packet>();
		private Lobby? lobby;
		private ulong lastOwner;
		private readonly HashSet<ulong> known = new HashSet<ulong>();

		internal override string Name { get { return "Steam"; } }
		internal override bool Ready { get { return SteamIdentity.Ready; } }
		internal override ulong LocalId { get { return Ready ? SteamClient.SteamId.Value : 0UL; } }
		internal override string LocalName { get { return Ready ? SteamClient.Name : "Player"; } }
		internal override bool InRoom { get { return lobby.HasValue; } }
		internal override ulong OwnerId { get { return lobby.HasValue ? lobby.Value.Owner.Id.Value : 0UL; } }

		internal SteamBackend()
		{
			if (!Ready)
			{
				return;
			}
			SteamNetworking.AllowP2PPacketRelay(true);
			SteamNetworking.OnP2PSessionRequest = OnSessionRequest;
			SteamMatchmaking.OnLobbyMemberJoined += OnJoined;
			SteamMatchmaking.OnLobbyMemberLeave += OnLeft;
			SteamMatchmaking.OnLobbyMemberDisconnected += OnLeft;
			SteamFriends.OnGameLobbyJoinRequested += OnInvite;
		}

		internal override void Dispose()
		{
			if (!Ready)
			{
				return;
			}
			SteamMatchmaking.OnLobbyMemberJoined -= OnJoined;
			SteamMatchmaking.OnLobbyMemberLeave -= OnLeft;
			SteamMatchmaking.OnLobbyMemberDisconnected -= OnLeft;
			SteamFriends.OnGameLobbyJoinRequested -= OnInvite;
			if (SteamNetworking.OnP2PSessionRequest == (Action<SteamId>)OnSessionRequest)
			{
				SteamNetworking.OnP2PSessionRequest = null;
			}
		}

		private void OnSessionRequest(SteamId id)
		{
			// Only people in the room we are in may talk to us.
			if (lobby.HasValue && Members().Contains(id.Value))
			{
				SteamNetworking.AcceptP2PSessionWithUser(id);
			}
		}

		private void OnJoined(Lobby l, Friend f)
		{
			if (lobby.HasValue && l.Id.Value == lobby.Value.Id.Value && known.Add(f.Id.Value) && MemberJoined != null)
			{
				MemberJoined(f.Id.Value);
			}
		}

		private void OnLeft(Lobby l, Friend f)
		{
			if (lobby.HasValue && l.Id.Value == lobby.Value.Id.Value)
			{
				known.Remove(f.Id.Value);
				if (MemberLeft != null)
				{
					MemberLeft(f.Id.Value);
				}
			}
		}

		private void OnInvite(Lobby l, SteamId friend)
		{
			if (InviteRequested != null)
			{
				InviteRequested(new RoomRef { Handle = l.Id.Value });
			}
		}

		internal override List<ulong> Members()
		{
			return lobby.HasValue ? lobby.Value.Members.Select(m => m.Id.Value).ToList() : new List<ulong>();
		}

		private void Track(Func<bool> poll)
		{
			pending.Add(poll);
		}

		internal override void Create(int max, Action<bool, string> done)
		{
			Task<Lobby?> task = SteamMatchmaking.CreateLobbyAsync(max);
			Track(() =>
			{
				if (!task.IsCompleted)
				{
					return false;
				}
				if (task.IsFaulted || !task.Result.HasValue)
				{
					done(false, "Steam would not create a room.");
					return true;
				}
				lobby = task.Result.Value;
				lobby.Value.SetPublic();
				lobby.Value.SetJoinable(true);
				known.Clear();
				known.Add(LocalId);
				lastOwner = LocalId;
				done(true, "");
				return true;
			});
		}

		internal override void Find(string code, Action<RoomRef> done)
		{
			Task<Lobby[]> task = SteamMatchmaking.LobbyList.FilterDistanceWorldwide().WithKeyValue("hg_code", code).WithMaxResults(8).RequestAsync();
			Track(() =>
			{
				if (!task.IsCompleted)
				{
					return false;
				}
				Lobby[] found = task.IsFaulted ? null : task.Result;
				if (found == null || found.Length == 0)
				{
					done(null);
					return true;
				}
				Lobby first = found[0];
				RoomRef room = new RoomRef { Handle = first.Id.Value, Owner = first.Owner.Id.Value, Members = first.MemberCount, Max = first.MaxMembers };
				foreach (KeyValuePair<string, string> pair in first.Data)
				{
					room.Data[pair.Key] = pair.Value;
				}
				done(room);
				return true;
			});
		}

		internal override void Join(RoomRef room, Action<bool, string> done)
		{
			Task<Lobby?> task = SteamMatchmaking.JoinLobbyAsync(new SteamId { Value = room.Handle });
			Track(() =>
			{
				if (!task.IsCompleted)
				{
					return false;
				}
				if (task.IsFaulted || !task.Result.HasValue)
				{
					done(false, "Could not join the room. It may be full or closed.");
					return true;
				}
				lobby = task.Result.Value;
				known.Clear();
				foreach (ulong id in Members())
				{
					known.Add(id);
				}
				lastOwner = lobby.Value.Owner.Id.Value;
				done(true, "");
				return true;
			});
		}

		internal override void Leave()
		{
			if (lobby.HasValue)
			{
				foreach (ulong id in Members())
				{
					if (id != LocalId)
					{
						SteamNetworking.CloseP2PSessionWithUser(new SteamId { Value = id });
					}
				}
				lobby.Value.Leave();
			}
			lobby = null;
			known.Clear();
		}

		internal override void SetRoomData(string key, string value)
		{
			if (lobby.HasValue)
			{
				lobby.Value.SetData(key, value);
			}
		}

		internal override void SetJoinable(bool joinable)
		{
			if (lobby.HasValue)
			{
				lobby.Value.SetJoinable(joinable);
			}
		}

		internal override string GetRoomData(string key)
		{
			return lobby.HasValue ? lobby.Value.GetData(key) ?? "" : "";
		}

		internal override List<FriendInfo> Friends()
		{
			List<FriendInfo> list = new List<FriendInfo>();
			if (!Ready)
			{
				return list;
			}
			foreach (Friend f in SteamFriends.GetFriends())
			{
				if (f.IsOnline && !f.IsMe)
				{
					list.Add(new FriendInfo { Id = f.Id.Value, Name = f.Name, Playing = f.IsPlayingThisGame });
				}
			}
			return list.OrderByDescending(f => f.Playing).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
		}

		internal override void InviteFriend(ulong id)
		{
			if (lobby.HasValue)
			{
				lobby.Value.InviteFriend(new SteamId { Value = id });
			}
		}

		internal override void Send(ulong to, byte[] data, bool reliable)
		{
			if (!Ready || to == 0UL)
			{
				return;
			}
			SteamNetworking.SendP2PPacket(new SteamId { Value = to }, data, data.Length, 0, reliable ? P2PSend.Reliable : P2PSend.UnreliableNoDelay);
		}

		internal override bool Receive(out ulong from, out byte[] data)
		{
			if (inbox.Count > 0)
			{
				Packet packet = inbox.Dequeue();
				from = packet.From;
				data = packet.Data;
				return true;
			}
			from = 0UL;
			data = null;
			return false;
		}

		internal override void Tick()
		{
			if (!Ready)
			{
				return;
			}
			SteamIdentity.Pump();
			for (int i = pending.Count - 1; i >= 0; i--)
			{
				if (pending[i]())
				{
					pending.RemoveAt(i);
				}
			}
			int guard = 0;
			while (guard++ < 256 && SteamNetworking.IsP2PPacketAvailable(0))
			{
				P2Packet? packet = SteamNetworking.ReadP2PPacket(0);
				if (packet.HasValue)
				{
					inbox.Enqueue(new Packet { From = packet.Value.SteamId.Value, Data = packet.Value.Data });
				}
			}
			if (lobby.HasValue)
			{
				ulong owner = lobby.Value.Owner.Id.Value;
				if (owner != lastOwner)
				{
					// Steam hands a room to somebody else when its owner leaves.
					ulong old = lastOwner;
					lastOwner = owner;
					if (MemberLeft != null && !Members().Contains(old))
					{
						MemberLeft(old);
					}
				}
			}
		}
	}

	/// <summary>
	/// Rooms in a shared folder and messages over UDP on this PC. Used only by the test rigs (two copies of the game started with
	/// <c>-mprig</c>); it does what the Steam backend does, so the code above both is the same.
	/// </summary>
	internal sealed class LocalBackend : Backend
	{
		private readonly string dir;
		private readonly ulong id;
		private readonly string name;
		private readonly UdpClient udp;
		private readonly int port;
		private ulong handle;
		private bool owner;
		private int max;
		private readonly Dictionary<ulong, int> ports = new Dictionary<ulong, int>();
		private readonly Dictionary<ulong, string> names = new Dictionary<ulong, string>();
		private readonly HashSet<ulong> known = new HashSet<ulong>();
		private readonly Dictionary<string, string> data = new Dictionary<string, string>();
		private float nextScan;
		private ulong ownerId;

		internal override string Name { get { return "Local"; } }
		internal override bool Ready { get { return true; } }
		internal override ulong LocalId { get { return id; } }
		internal override string LocalName { get { return name; } }
		internal override bool InRoom { get { return handle != 0UL; } }
		internal override ulong OwnerId { get { return ownerId; } }

		internal LocalBackend(string folder, ulong localId, string localName)
		{
			dir = folder;
			id = localId;
			name = localName;
			Directory.CreateDirectory(dir);
			udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
			udp.Client.Blocking = false;
			port = ((IPEndPoint)udp.Client.LocalEndPoint).Port;
		}

		private string RoomFile(ulong h) { return Path.Combine(dir, "room_" + h + ".txt"); }
		private string MemberFile(ulong h, ulong who) { return Path.Combine(dir, "mem_" + h + "_" + who + ".txt"); }

		private static void Write(string path, IEnumerable<string> lines)
		{
			string tmp = path + "." + Guid.NewGuid().ToString("N").Substring(0, 6) + ".tmp";
			File.WriteAllLines(tmp, lines.ToArray());
			if (File.Exists(path))
			{
				File.Delete(path);
			}
			File.Move(tmp, path);
		}

		private static Dictionary<string, string> Read(string path)
		{
			Dictionary<string, string> map = new Dictionary<string, string>();
			for (int attempt = 0; attempt < 5; attempt++)
			{
				try
				{
					foreach (string line in File.ReadAllLines(path))
					{
						int eq = line.IndexOf('=');
						if (eq > 0)
						{
							map[line.Substring(0, eq)] = Uri.UnescapeDataString(line.Substring(eq + 1));
						}
					}
					return map;
				}
				catch (IOException)
				{
					map.Clear();
				}
			}
			return map;
		}

		private void WriteRoom()
		{
			List<string> lines = new List<string> { "owner=" + id, "max=" + max };
			foreach (KeyValuePair<string, string> pair in data)
			{
				lines.Add("d." + pair.Key + "=" + Uri.EscapeDataString(pair.Value));
			}
			Write(RoomFile(handle), lines);
		}

		private void WriteMember()
		{
			Write(MemberFile(handle, id), new[] { "port=" + port, "name=" + Uri.EscapeDataString(name) });
		}

		internal override List<ulong> Members()
		{
			List<ulong> list = new List<ulong>();
			if (handle == 0UL)
			{
				return list;
			}
			foreach (string file in Directory.GetFiles(dir, "mem_" + handle + "_*.txt"))
			{
				string[] parts = Path.GetFileNameWithoutExtension(file).Split('_');
				ulong who;
				if (parts.Length == 3 && ulong.TryParse(parts[2], out who))
				{
					list.Add(who);
				}
			}
			return list;
		}

		internal override void Create(int maxMembers, Action<bool, string> done)
		{
			handle = (ulong)new System.Random().Next(100000, int.MaxValue);
			max = maxMembers;
			owner = true;
			ownerId = id;
			data.Clear();
			WriteRoom();
			WriteMember();
			known.Clear();
			known.Add(id);
			done(true, "");
		}

		private RoomRef Load(string path)
		{
			Dictionary<string, string> map = Read(path);
			ulong h;
			if (!ulong.TryParse(Path.GetFileNameWithoutExtension(path).Substring(5), out h) || !map.ContainsKey("owner"))
			{
				return null;
			}
			RoomRef room = new RoomRef { Handle = h, Owner = ulong.Parse(map["owner"]), Max = map.ContainsKey("max") ? int.Parse(map["max"]) : 6 };
			foreach (KeyValuePair<string, string> pair in map)
			{
				if (pair.Key.StartsWith("d."))
				{
					room.Data[pair.Key.Substring(2)] = pair.Value;
				}
			}
			room.Members = Directory.GetFiles(dir, "mem_" + h + "_*.txt").Length;
			return room;
		}

		internal override void Find(string code, Action<RoomRef> done)
		{
			foreach (string file in Directory.GetFiles(dir, "room_*.txt"))
			{
				RoomRef room = Load(file);
				if (room != null && room.Get("hg_code") == code)
				{
					done(room);
					return;
				}
			}
			done(null);
		}

		internal override void Join(RoomRef room, Action<bool, string> done)
		{
			if (!File.Exists(RoomFile(room.Handle)))
			{
				done(false, "That room is gone.");
				return;
			}
			RoomRef fresh = Load(RoomFile(room.Handle));
			if (fresh == null || fresh.Members >= fresh.Max)
			{
				done(false, "That room is full.");
				return;
			}
			handle = room.Handle;
			owner = false;
			ownerId = fresh.Owner;
			WriteMember();
			known.Clear();
			List<ulong> inside = Members();
			foreach (ulong who in inside)
			{
				known.Add(who);
			}
			ScanPorts(inside);
			done(true, "");
		}

		internal override void Leave()
		{
			if (handle != 0UL)
			{
				try
				{
					File.Delete(MemberFile(handle, id));
					if (owner)
					{
						File.Delete(RoomFile(handle));
					}
				}
				catch (IOException)
				{
				}
			}
			handle = 0UL;
			owner = false;
			known.Clear();
			ports.Clear();
		}

		internal override void SetRoomData(string key, string value)
		{
			data[key] = value;
			if (owner && handle != 0UL)
			{
				WriteRoom();
			}
		}

		internal override void SetJoinable(bool joinable)
		{
			SetRoomData("joinable", joinable ? "1" : "0");
		}

		internal override string GetRoomData(string key)
		{
			if (owner)
			{
				string mine;
				return data.TryGetValue(key, out mine) ? mine : "";
			}
			if (handle == 0UL || !File.Exists(RoomFile(handle)))
			{
				return "";
			}
			string value;
			return Read(RoomFile(handle)).TryGetValue("d." + key, out value) ? value : "";
		}

		internal override List<FriendInfo> Friends()
		{
			return new List<FriendInfo> { new FriendInfo { Id = 9001, Name = "Test friend", Playing = true }, new FriendInfo { Id = 9002, Name = "Another friend" } };
		}

		internal override void InviteFriend(ulong friend)
		{
			Mod.Log.LogInfo("Local backend: invite of " + friend + " is a no-op (room " + handle + ").");
		}

		internal override void Send(ulong to, byte[] payload, bool reliable)
		{
			int target;
			if (!ports.TryGetValue(to, out target))
			{
				ScanPorts(new[] { to }); // a message can arrive before the next scan has noticed the sender
				if (!ports.TryGetValue(to, out target))
				{
					return;
				}
			}
			byte[] framed = new byte[payload.Length + 8];
			Array.Copy(BitConverter.GetBytes(id), 0, framed, 0, 8);
			Array.Copy(payload, 0, framed, 8, payload.Length);
			try
			{
				udp.Send(framed, framed.Length, new IPEndPoint(IPAddress.Loopback, target));
			}
			catch (SocketException)
			{
			}
		}

		internal override bool Receive(out ulong from, out byte[] payload)
		{
			from = 0UL;
			payload = null;
			try
			{
				if (udp.Available <= 0)
				{
					return false;
				}
				IPEndPoint endpoint = new IPEndPoint(IPAddress.Any, 0);
				byte[] framed = udp.Receive(ref endpoint);
				if (framed.Length < 8)
				{
					return false;
				}
				from = BitConverter.ToUInt64(framed, 0);
				payload = new byte[framed.Length - 8];
				Array.Copy(framed, 8, payload, 0, payload.Length);
				return true;
			}
			catch (SocketException)
			{
				return false;
			}
		}

		internal override void Tick()
		{
			if (handle == 0UL || Time.realtimeSinceStartup < nextScan)
			{
				return;
			}
			nextScan = Time.realtimeSinceStartup + 0.15f;
			if (!File.Exists(RoomFile(handle)))
			{
				ulong gone = ownerId;
				handle = 0UL;
				known.Clear();
				if (MemberLeft != null)
				{
					MemberLeft(gone);
				}
				return;
			}
			HashSet<ulong> now = new HashSet<ulong>(Members());
			foreach (ulong who in now)
			{
				if (known.Add(who))
				{
					if (MemberJoined != null)
					{
						MemberJoined(who);
					}
				}
			}
			foreach (ulong who in known.ToList())
			{
				if (!now.Contains(who))
				{
					known.Remove(who);
					ports.Remove(who);
					if (MemberLeft != null)
					{
						MemberLeft(who);
					}
				}
			}
			ScanPorts(now);
		}

		private void ScanPorts(IEnumerable<ulong> members)
		{
			foreach (ulong who in members)
			{
				if (who != id && !ports.ContainsKey(who))
				{
					Dictionary<string, string> map = Read(MemberFile(handle, who));
					int p;
					if (map.ContainsKey("port") && int.TryParse(map["port"], out p))
					{
						ports[who] = p;
					}
					if (map.ContainsKey("name"))
					{
						names[who] = map["name"];
					}
				}
			}
		}

		internal override void Dispose()
		{
			Leave();
			udp.Close();
		}
	}
}
