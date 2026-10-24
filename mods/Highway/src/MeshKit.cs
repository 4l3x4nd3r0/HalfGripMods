using System;
using System.Collections.Generic;
using UnityEngine;

namespace HalfGripHighway
{
	/// <summary>
	/// All the triangles of one colour. Every face has its own vertices, so Unity's normals come out flat, and each face is
	/// wound to look toward a hint direction, so no call has to get the winding right by hand.
	/// </summary>
	internal sealed class Batch
	{
		public readonly string Name;
		public readonly Color Color;
		public readonly bool Collide;
		public readonly bool Wall;

		private readonly List<Vector3> verts = new List<Vector3>();
		private readonly List<int> tris = new List<int>();

		public Batch(string name, Color color, bool collide, bool wall = false)
		{
			Name = name;
			Color = color;
			Collide = collide;
			Wall = wall;
		}

		public int Count => verts.Count;

		public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 hint)
		{
			if (Vector3.Dot(Vector3.Cross(b - a, c - a), hint) < 0f)
			{
				Vector3 swap = b;
				b = d;
				d = swap;
			}
			int i = verts.Count;
			verts.Add(a);
			verts.Add(b);
			verts.Add(c);
			verts.Add(d);
			tris.Add(i);
			tris.Add(i + 1);
			tris.Add(i + 2);
			tris.Add(i);
			tris.Add(i + 2);
			tris.Add(i + 3);
		}

		public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 hint)
		{
			if (Vector3.Dot(Vector3.Cross(b - a, c - a), hint) < 0f)
			{
				Vector3 swap = b;
				b = c;
				c = swap;
			}
			int i = verts.Count;
			verts.Add(a);
			verts.Add(b);
			verts.Add(c);
			tris.Add(i);
			tris.Add(i + 1);
			tris.Add(i + 2);
		}

		public void Box(Vector3 center, Vector3 size, Quaternion rotation)
		{
			Vector3 h = size * 0.5f;
			Vector3 x = rotation * new Vector3(h.x, 0f, 0f);
			Vector3 y = rotation * new Vector3(0f, h.y, 0f);
			Vector3 z = rotation * new Vector3(0f, 0f, h.z);
			Face(center, x, y, z);
			Face(center, -x, y, z);
			Face(center, y, x, z);
			Face(center, -y, x, z);
			Face(center, z, x, y);
			Face(center, -z, x, y);
		}

		private void Face(Vector3 center, Vector3 normal, Vector3 u, Vector3 v)
		{
			Vector3 c = center + normal;
			Quad(c - u - v, c + u - v, c + u + v, c - u + v, normal);
		}

		/// <summary>A flat round disc (a sign face): <paramref name="thickness"/> along the local Z axis of <paramref name="rot"/>.</summary>
		public void Disc(Vector3 center, float radius, float thickness, Quaternion rot, int sides = 28)
		{
			Vector3 forward = rot * Vector3.forward;
			Vector3 front = center + forward * (thickness * 0.5f);
			Vector3 back = center - forward * (thickness * 0.5f);
			for (int i = 0; i < sides; i++)
			{
				float a0 = i * Mathf.PI * 2f / sides;
				float a1 = (i + 1) * Mathf.PI * 2f / sides;
				Vector3 r0 = rot * new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f) * radius;
				Vector3 r1 = rot * new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f) * radius;
				Tri(front, front + r0, front + r1, forward);
				Tri(back, back + r0, back + r1, -forward);
				Quad(back + r0, back + r1, front + r1, front + r0, r0 + r1);
			}
		}

		public void Cylinder(Vector3 baseCenter, float radius, float height, int sides)
		{
			Vector3 top = baseCenter + Vector3.up * height;
			for (int i = 0; i < sides; i++)
			{
				float a0 = i * Mathf.PI * 2f / sides;
				float a1 = (i + 1) * Mathf.PI * 2f / sides;
				Vector3 r0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius;
				Vector3 r1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius;
				Quad(baseCenter + r0, baseCenter + r1, top + r1, top + r0, r0 + r1);
				Tri(top, top + r0, top + r1, Vector3.up);
			}
		}

		public Mesh ToMesh()
		{
			Mesh mesh = new Mesh { name = "Highway_" + Name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
			mesh.SetVertices(verts);
			mesh.SetTriangles(tris, 0);
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			return mesh;
		}
	}

	/// <summary>Helpers for polylines (lists of points) that the roads are built from.</summary>
	internal static class Geo
	{
		/// <summary>Right of the direction of travel, flat. For a polyline heading east that is south.</summary>
		public static Vector3 Right(IList<Vector3> p, int i)
		{
			Vector3 prev = p[Mathf.Max(i - 1, 0)];
			Vector3 next = p[Mathf.Min(i + 1, p.Count - 1)];
			Vector3 f = next - prev;
			f.y = 0f;
			if (f.sqrMagnitude < 1e-6f)
			{
				return Vector3.right;
			}
			f.Normalize();
			return new Vector3(f.z, 0f, -f.x);
		}

		public static Vector3 Forward(IList<Vector3> p, int i)
		{
			Vector3 r = Right(p, i);
			return new Vector3(-r.z, 0f, r.x);
		}

		public static Vector3 At(IList<Vector3> p, int i, float lateral, float lift = 0f)
		{
			return p[i] + Right(p, i) * lateral + Vector3.up * lift;
		}

		public static List<Vector3> Straight(Vector3 origin, Vector3 dir, float s0, float s1, float step)
		{
			List<Vector3> pts = new List<Vector3>();
			int n = Mathf.Max(1, Mathf.CeilToInt((s1 - s0) / step));
			for (int i = 0; i <= n; i++)
			{
				pts.Add(origin + dir * Mathf.Lerp(s0, s1, i / (float)n));
			}
			return pts;
		}

		public static float[] Cumulative(IList<Vector3> p)
		{
			float[] cum = new float[p.Count];
			for (int i = 1; i < p.Count; i++)
			{
				Vector3 d = p[i] - p[i - 1];
				d.y = 0f;
				cum[i] = cum[i - 1] + d.magnitude;
			}
			return cum;
		}

		/// <summary>Position and right vector at distance s along the polyline.</summary>
		public static void PointAt(IList<Vector3> p, float[] cum, float s, out Vector3 pos, out Vector3 right)
		{
			s = Mathf.Clamp(s, 0f, cum[cum.Length - 1]);
			int i = Array.BinarySearch(cum, s);
			if (i < 0)
			{
				i = ~i - 1;
			}
			i = Mathf.Clamp(i, 0, p.Count - 2);
			float span = Mathf.Max(1e-4f, cum[i + 1] - cum[i]);
			float t = (s - cum[i]) / span;
			pos = Vector3.Lerp(p[i], p[i + 1], t);
			right = Vector3.Slerp(Right(p, i), Right(p, i + 1), t);
		}

		/// <summary>Flat road ribbon between two lateral offsets.</summary>
		public static void Strip(Batch b, IList<Vector3> p, float latA, float latB, float lift, Func<Vector3, bool> skip = null, bool facesDown = false)
		{
			Vector3 hint = facesDown ? Vector3.down : Vector3.up;
			for (int i = 0; i + 1 < p.Count; i++)
			{
				if (skip != null && skip((p[i] + p[i + 1]) * 0.5f))
				{
					continue;
				}
				b.Quad(At(p, i, latA, lift), At(p, i, latB, lift), At(p, i + 1, latB, lift), At(p, i + 1, latA, lift), hint);
			}
		}

		/// <summary>A painted line, solid when off is 0, otherwise dashes.</summary>
		public static void Line(Batch b, IList<Vector3> p, float lateral, float width, float lift, float on, float off, Func<Vector3, bool> skip = null)
		{
			float[] cum = Cumulative(p);
			float total = cum[cum.Length - 1];
			float s = 0f;
			while (s < total)
			{
				float e = off <= 0f ? total : Mathf.Min(total, s + on);
				List<Vector3> left = new List<Vector3>();
				List<Vector3> rightSide = new List<Vector3>();
				bool hidden = false;
				for (float t = s; ; t = Mathf.Min(e, t + 4f))
				{
					PointAt(p, cum, t, out Vector3 pos, out Vector3 right);
					if (skip != null && skip(pos))
					{
						hidden = true;
					}
					left.Add(pos + right * (lateral - width * 0.5f) + Vector3.up * lift);
					rightSide.Add(pos + right * (lateral + width * 0.5f) + Vector3.up * lift);
					if (t >= e)
					{
						break;
					}
				}
				if (!hidden)
				{
					for (int i = 0; i + 1 < left.Count; i++)
					{
						b.Quad(left[i], rightSide[i], rightSide[i + 1], left[i + 1], Vector3.up);
					}
				}
				if (off <= 0f)
				{
					break;
				}
				s = e + off;
			}
		}

		/// <summary>Sweeps a closed cross-section (lateral, height) along a polyline.</summary>
		public static void Extrude(Batch b, IList<Vector3> p, Vector2[] profile, bool caps = true)
		{
			if (p.Count < 2)
			{
				return;
			}
			float mid = 0f;
			foreach (Vector2 v in profile)
			{
				mid += v.y;
			}
			mid /= profile.Length;
			int m = profile.Length;
			for (int i = 0; i + 1 < p.Count; i++)
			{
				Vector3 r0 = Right(p, i);
				Vector3 r1 = Right(p, i + 1);
				for (int k = 0; k < m; k++)
				{
					Vector2 a = profile[k];
					Vector2 c = profile[(k + 1) % m];
					Vector3 hint = r0 * ((a.x + c.x) * 0.5f) + Vector3.up * ((a.y + c.y) * 0.5f - mid);
					b.Quad(p[i] + r0 * a.x + Vector3.up * a.y, p[i] + r0 * c.x + Vector3.up * c.y,
						p[i + 1] + r1 * c.x + Vector3.up * c.y, p[i + 1] + r1 * a.x + Vector3.up * a.y, hint);
				}
			}
			if (!caps)
			{
				return;
			}
			for (int end = 0; end < 2; end++)
			{
				int i = end == 0 ? 0 : p.Count - 1;
				Vector3 r = Right(p, i);
				Vector3 center = p[i] + Vector3.up * mid;
				Vector3 outward = Forward(p, i) * (end == 0 ? -1f : 1f);
				for (int k = 0; k < m; k++)
				{
					Vector2 a = profile[k];
					Vector2 c = profile[(k + 1) % m];
					b.Tri(center, p[i] + r * a.x + Vector3.up * a.y, p[i] + r * c.x + Vector3.up * c.y, outward);
				}
			}
		}
	}
}
