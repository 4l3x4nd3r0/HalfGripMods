using System.Collections.Generic;
using UnityEngine;

namespace HalfGripHighway
{
	/// <summary>
	/// A road crew digging up the lower highway just before it goes under the bridge: the outer lane of the racing carriageway (the side the
	/// right-hand exit is on) is closed, with a diagonal chain of concrete median blocks, an arrow board and keep-left signs sending traffic
	/// into the inner lane, more blocks along the closed lane, and an excavator, a dump truck, a roller and a site office made of Factory
	/// containers at work. The machines, the containers and the blocks are solid and never break; the few cones and the signs break.
	/// The same machines work on the unfinished dirt ramps (see <c>DirtRamps.cs</c>).
	///
	/// Everything here is drawn in real metres and multiplied by <see cref="Plan.PerMetre"/>: a car in this game is 14.2 units long, a car is
	/// about 4.3 m, so one metre is 3.3 units. Lanes (11 units) are about 3.3 m wide, which is also why an excavator is 10 units across.
	/// </summary>
	internal sealed partial class RoadBuilder
	{
		public readonly List<ContainerSpot> Containers = new List<ContainerSpot>();

		public readonly Batch MachYellow = new Batch("machine yellow", new Color(0.95f, 0.74f, 0.06f), true, wall: true);
		public readonly Batch MachDark = new Batch("machine dark", new Color(0.10f, 0.10f, 0.11f), true, wall: true);
		public readonly Batch MachGrey = new Batch("machine grey", new Color(0.45f, 0.47f, 0.50f), true, wall: true);
		public readonly Batch MachGlass = new Batch("machine glass", new Color(0.22f, 0.34f, 0.42f), true, wall: true);
		public readonly Batch MachOrange = new Batch("machine orange", new Color(0.95f, 0.40f, 0.05f), true, wall: true);
		public readonly Batch Spoil = new Batch("spoil", new Color(0.38f, 0.27f, 0.17f), true);
		public readonly Batch Trench = new Batch("trench", new Color(0.26f, 0.18f, 0.11f), false);

		private static readonly Color BlueC = new Color(0.07f, 0.28f, 0.66f);
		private const float S = Plan.PerMetre;

		/// <summary>The concrete median block at worksite size: the 1.1 m profile of the median, scaled up to a block as tall as a car is high.</summary>
		private static readonly Vector2[] WorkBlockProfile = ScaleProfile(new[]
		{
			new Vector2(-0.6f, -0.05f), new Vector2(-0.6f, 0.25f), new Vector2(-0.28f, 0.85f), new Vector2(-0.2f, 1.1f),
			new Vector2(0.2f, 1.1f), new Vector2(0.28f, 0.85f), new Vector2(0.6f, 0.25f), new Vector2(0.6f, -0.05f)
		}, 2.4f);

		private static Vector2[] ScaleProfile(Vector2[] p, float k)
		{
			Vector2[] q = new Vector2[p.Length];
			for (int i = 0; i < p.Length; i++)
			{
				q[i] = p[i] * k;
			}
			return q;
		}

		/// <summary>A box in a machine's own space (x right, y up, z forward, in metres), turned to face <paramref name="yaw"/> degrees.</summary>
		private static void MBox(Batch b, Vector3 origin, float yaw, Vector3 local, Vector3 size, float pitch = 0f)
		{
			Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
			b.Box(origin + turn * (local * S), size * S, turn * Quaternion.Euler(pitch, 0f, 0f));
		}

		private void Excavator(Vector3 o, float yaw)
		{
			foreach (float x in new[] { -1.1f, 1.1f })
			{
				MBox(MachDark, o, yaw, new Vector3(x, 0.45f, 0f), new Vector3(0.9f, 0.9f, 4.2f));
			}
			MBox(MachYellow, o, yaw, new Vector3(0f, 1.6f, -0.2f), new Vector3(2.6f, 1.4f, 3.0f));
			MBox(MachYellow, o, yaw, new Vector3(0f, 1.8f, -1.9f), new Vector3(2.4f, 1.0f, 0.9f));
			MBox(MachGlass, o, yaw, new Vector3(-0.7f, 3.0f, 0.5f), new Vector3(1.3f, 1.4f, 1.4f));
			MBox(MachOrange, o, yaw, new Vector3(-0.7f, 3.85f, 0.2f), new Vector3(0.3f, 0.25f, 0.3f));
			MBox(MachYellow, o, yaw, new Vector3(0f, 3.05f, 2.8f), new Vector3(0.5f, 0.5f, 3.7f), -31f);
			MBox(MachYellow, o, yaw, new Vector3(0f, 2.95f, 5.2f), new Vector3(0.4f, 0.4f, 2.7f), 53f);
			MBox(MachDark, o, yaw, new Vector3(0f, 1.6f, 6.1f), new Vector3(1.1f, 0.8f, 0.9f));
		}

		private void DumpTruck(Vector3 o, float yaw)
		{
			TruckChassis(o, yaw, 8f);
			MBox(MachYellow, o, yaw, new Vector3(0f, 2.2f, -1.3f), new Vector3(2.4f, 1.3f, 4.6f), -7f);
		}

		/// <summary>The truck without a body: frame, wheels and cab, in metres. The cab is at the front (+z).</summary>
		private void TruckChassis(Vector3 o, float yaw, float length)
		{
			MBox(MachGrey, o, yaw, new Vector3(0f, 1.0f, 0f), new Vector3(2.3f, 0.4f, length));
			float front = length * 0.5f - 1.1f;
			foreach (float z in new[] { front, -0.6f, -2.2f })
			{
				foreach (float x in new[] { -1.1f, 1.1f })
				{
					MBox(MachDark, o, yaw, new Vector3(x, 0.5f, z), new Vector3(0.45f, 1.0f, 1.0f));
				}
			}
			MBox(MachYellow, o, yaw, new Vector3(0f, 2.3f, front), new Vector3(2.3f, 1.9f, 2.0f));
			MBox(MachGlass, o, yaw, new Vector3(0f, 2.6f, front + 1.05f), new Vector3(2.1f, 0.75f, 0.08f));
			MBox(MachOrange, o, yaw, new Vector3(0f, 3.4f, front), new Vector3(0.35f, 0.25f, 0.35f));
		}

		/// <summary>A flatbed truck carrying a Factory container.</summary>
		private void ContainerTruck(Vector3 o, float yaw, int kind)
		{
			TruckChassis(o, yaw, 10f);
			Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
			Containers.Add(new ContainerSpot { Pos = o + turn * (new Vector3(0f, 1.2f, -1.0f) * S), Yaw = yaw, Kind = kind });
		}

		private void Roller(Vector3 o, float yaw)
		{
			foreach (float z in new[] { -1.6f, 1.6f })
			{
				MBox(MachGrey, o, yaw, new Vector3(0f, 0.7f, z), new Vector3(1.8f, 1.4f, 1.4f));
			}
			MBox(MachYellow, o, yaw, new Vector3(0f, 1.4f, 0f), new Vector3(1.5f, 0.8f, 3.6f));
			foreach (float x in new[] { -0.7f, 0.7f })
			{
				foreach (float z in new[] { -1.2f, 0.1f })
				{
					MBox(MachDark, o, yaw, new Vector3(x, 2.4f, z), new Vector3(0.1f, 1.7f, 0.1f));
				}
			}
			MBox(MachDark, o, yaw, new Vector3(0f, 3.3f, -0.55f), new Vector3(1.6f, 0.1f, 1.6f));
			MBox(MachDark, o, yaw, new Vector3(0f, 2.0f, -0.55f), new Vector3(0.7f, 0.5f, 0.7f));
			MBox(MachOrange, o, yaw, new Vector3(0f, 3.55f, -0.55f), new Vector3(0.35f, 0.25f, 0.35f));
		}

		/// <summary>A trailer with a black board of amber lamps in the shape of an arrow. It faces <paramref name="yaw"/>, toward the traffic that is coming.</summary>
		private void ArrowBoard(Vector3 o, float yaw, bool pointsLeft)
		{
			MBox(MachGrey, o, yaw, new Vector3(0f, 0.7f, 0f), new Vector3(1.7f, 0.4f, 3.4f));
			foreach (float x in new[] { -1.0f, 1.0f })
			{
				MBox(MachDark, o, yaw, new Vector3(x, 0.45f, -0.3f), new Vector3(0.3f, 0.9f, 0.9f));
			}
			MBox(MachGrey, o, yaw, new Vector3(0f, 1.9f, 0f), new Vector3(0.15f, 2.0f, 0.15f));
			MBox(MachDark, o, yaw, new Vector3(0f, 3.6f, 0.1f), new Vector3(2.2f, 1.3f, 0.12f));
			// A shaft of five lamps and a head of four; the board's own +x is the driver's left. Built pointing right, mirrored for left.
			Vector2[] lamps =
			{
				new Vector2(0.8f, 0f), new Vector2(0.4f, 0f), new Vector2(0f, 0f), new Vector2(-0.4f, 0f), new Vector2(-0.8f, 0f),
				new Vector2(-0.4f, 0.4f), new Vector2(-0.4f, -0.4f), new Vector2(0f, 0.7f), new Vector2(0f, -0.7f)
			};
			foreach (Vector2 l in lamps)
			{
				float x = pointsLeft ? -l.x : l.x;
				MBox(MachOrange, o, yaw, new Vector3(x, 3.6f + l.y * 0.8f, 0.2f), new Vector3(0.2f, 0.2f, 0.07f));
			}
		}

		/// <summary>A heap of spoil, size in metres.</summary>
		private void Heap(Vector3 pos, Vector3 metres, float yaw)
		{
			Spoil.Box(pos + Vector3.up * (metres.y * S * 0.5f), metres * S, Quaternion.Euler(0f, yaw, 0f));
		}

		/// <summary>A cone, a cone's height of 0.7 m at the cars' scale. It breaks when a car touches it and does not slow the car.</summary>
		private void Cone(Vector3 pos)
		{
			const float k = 2.4f;
			Prop p = NewProp("cone", pos, Vector3.forward, 3f);
			p.Box(DarkC, new Vector3(0f, 0.03f, 0f) * k, new Vector3(0.55f, 0.06f, 0.55f) * k);
			p.Box(OrangeC, new Vector3(0f, 0.31f, 0f) * k, new Vector3(0.38f, 0.5f, 0.38f) * k);
			p.Box(WhiteC, new Vector3(0f, 0.52f, 0f) * k, new Vector3(0.34f, 0.12f, 0.34f) * k);
			p.Box(OrangeC, new Vector3(0f, 0.78f, 0f) * k, new Vector3(0.22f, 0.4f, 0.22f) * k);
			p.Hit(new Vector3(0f, 0.45f, 0f) * k, new Vector3(0.5f, 0.9f, 0.5f) * k);
		}

		/// <summary>A round blue "keep left / right" sign: a white arrow on blue. <paramref name="arrow"/> is "&lt;" or "&gt;".</summary>
		private void DivertSign(Vector3 basePos, Vector3 facing, string arrow)
		{
			Prop p = NewProp("divert", basePos, facing, 25f);
			p.Box(PoleC, new Vector3(0f, 1.7f, 0f), new Vector3(0.3f, 3.4f, 0.3f));
			p.Disc(WhiteC, new Vector3(0f, 4.8f, 0f), 1.5f, 0.16f);
			p.Disc(BlueC, new Vector3(0f, 4.8f, 0.09f), 1.3f, 0.1f);
			p.Hit(new Vector3(0f, 1.7f, 0f), new Vector3(0.3f, 3.4f, 0.3f));
			p.Hit(new Vector3(0f, 4.8f, 0f), new Vector3(2.9f, 2.9f, 0.16f));
			Foot(p, 0f, 1.2f);
			p.Text(arrow, new Vector3(0f, 4.8f, 0.16f), new Vector2(2.0f, 2.0f), WhiteC, 1);
		}

		/// <summary>A chain of concrete median blocks from <paramref name="a"/> to <paramref name="b"/>: blocks about 12 m long with a gap between them. Solid.</summary>
		private void BlockChain(Vector3 a, Vector3 b)
		{
			float length = Vector3.Distance(a, b);
			int n = Mathf.Max(1, Mathf.RoundToInt(length / 12.5f));
			Vector3 unit = (b - a) / length;
			for (int k = 0; k < n; k++)
			{
				Vector3 p0 = Vector3.Lerp(a, b, k / (float)n) + unit * 0.5f;
				Vector3 p1 = Vector3.Lerp(a, b, (k + 1) / (float)n) - unit * 0.5f;
				Geo.Extrude(Barrier, new List<Vector3> { p0, p1 }, WorkBlockProfile);
			}
		}

		private void WorkZone(Vector3 origin, Vector3 dir)
		{
			Vector3 across = new Vector3(dir.z, 0f, -dir.x);
			float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
			Vector3 At(float s, float lat)
			{
				return origin + dir * s + across * lat;
			}

			// Warnings and the diversion signs (the lane on the right is closed: keep left), on both sides of the road.
			foreach (float s in new[] { -560f, -450f })
			{
				string text = "LUCRARI\n" + (s < -500f ? "250 m" : "150 m");
				WarningBoard(At(s, Plan.HalfWidth + 4.5f), -dir, text);
				WarningBoard(At(s, 0f), -dir, text);
			}
			SpeedSign(At(-380f, Plan.HalfWidth + 3.0f), -dir, "60");
			SpeedSign(At(-380f, 1.8f), -dir, "60");
			DivertSign(At(-362f, 1.8f), -dir, "<");
			DivertSign(At(-362f, Plan.HalfWidth + 3.0f), -dir, "<");

			// An arrow board on the shoulder at the start of the taper, a diagonal chain of median blocks across the closed lane from the
			// road edge to the lane divider, blocks along the divider, and a short taper back at the end.
			ArrowBoard(At(-342f, 31.5f), yaw + 180f, pointsLeft: true);
			BlockChain(At(-335f, 32f), At(-260f, 18f));
			BlockChain(At(-260f, 18f), At(-92f, 18f));
			BlockChain(At(-92f, 18f), At(-66f, 31f));

			// The crew, closed lane (18 to 33 m from the median): the excavator digs the trench, then the dump truck and the roller, and a
			// site office of Factory containers on the outside edge.
			Excavator(At(-252f, 25f), yaw);
			Vector3 lift = Vector3.up * 0.06f;
			Trench.Quad(At(-224f, 20.5f) + lift, At(-178f, 20.5f) + lift, At(-178f, 26.5f) + lift, At(-224f, 26.5f) + lift, Vector3.up);
			Heap(At(-238f, 30.5f), new Vector3(3.2f, 1.0f, 2.0f), yaw + 12f);
			Heap(At(-172f, 29.5f), new Vector3(3.6f, 1.1f, 2.4f), yaw + 20f);
			DumpTruck(At(-150f, 25f), yaw);
			Roller(At(-118f, 24.5f), yaw);
			float outside = Plan.HalfWidth - 3.6f;
			Containers.Add(new ContainerSpot { Pos = At(-205f, outside) + Vector3.up * 0.03f, Yaw = yaw, Kind = 0 });
			Containers.Add(new ContainerSpot { Pos = At(-205f, outside) + Vector3.up * (HalfGripHighway.Containers.Height + 0.03f), Yaw = yaw, Kind = 2 });
			Containers.Add(new ContainerSpot { Pos = At(-178f, outside) + Vector3.up * 0.03f, Yaw = yaw, Kind = 1 });

			// A few cones round the dig (these break, and do not slow the car).
			foreach (float s in new[] { -222f, -206f, -190f })
			{
				Cone(At(s, 19.3f));
			}
		}
	}
}
