using System.Collections.Generic;
using UnityEngine;

namespace TempleOfDrift
{
	/// <summary>One piece of steel guardrail, about 8 units long. (The same rail as Highway's, ported from there.)</summary>
	internal struct RailPiece
	{
		public Vector3 Pos;     // middle of the piece, at the road surface
		public Vector3 Along;   // unit vector along the rail
		public Vector3 Out;     // unit vector away from the road
		public float Len;
		public int Chunk;
	}

	/// <summary>The loose boxes a broken guardrail turns into. Capped, and each one vanishes after a few seconds.</summary>
	internal static class Debris
	{
		private const int Max = 140;

		private static readonly Queue<GameObject> live = new Queue<GameObject>();
		private static Mesh cube;
		private static Transform parent;

		internal static void Reset(Transform root)
		{
			live.Clear();
			parent = root;
			if (cube == null)
			{
				Batch unit = new Batch("cube", Tex.White, Color.white, false);
				unit.Box(Vector3.zero, Vector3.one, Quaternion.identity, 1f);
				cube = unit.ToMesh();
			}
		}

		internal static void Spawn(Vector3 pos, Quaternion rot, Vector3 size, Material material, Vector3 carVelocity, float mass, int layer)
		{
			while (live.Count >= Max)
			{
				GameObject oldest = live.Dequeue();
				if (oldest != null)
				{
					Object.Destroy(oldest);
				}
			}
			GameObject piece = new GameObject("shard") { layer = layer };
			if (parent != null)
			{
				piece.transform.SetParent(parent, true);
			}
			piece.transform.SetPositionAndRotation(pos, rot);
			piece.transform.localScale = size;
			piece.AddComponent<MeshFilter>().sharedMesh = cube;
			piece.AddComponent<MeshRenderer>().sharedMaterial = material;
			piece.AddComponent<BoxCollider>();
			Rigidbody body = piece.AddComponent<Rigidbody>();
			body.mass = mass;
			body.linearVelocity = carVelocity * 0.6f + Random.insideUnitSphere * 3f + Vector3.up * 2f;
			body.angularVelocity = Random.insideUnitSphere * 8f;
			live.Enqueue(piece);
			Object.Destroy(piece, 3f);
		}
	}

	/// <summary>
	/// The guardrail, without a single physics body (as on Highway): merged meshes, and every physics step each car (the player and the
	/// rivals) is tested against the rail pieces in its own grid cells. A piece the car's body really touches breaks, is thrown away as
	/// loose boxes, and takes a share of the car's speed.
	/// </summary>
	internal sealed class BreakField : MonoBehaviour
	{
		private const float Cell = 24f;
		private const float Body = 0.96f;   // the measured footprint includes mirrors and bumpers, so the test uses a hair less

		private static readonly Color SteelC = new Color(0.66f, 0.68f, 0.72f);
		private static readonly Color PoleC = new Color(0.36f, 0.37f, 0.40f);

		private sealed class Chunk
		{
			public readonly List<int> Pieces = new List<int>();
			public MeshFilter Steel;
			public MeshFilter Posts;
			public bool Dirty;
		}

		private readonly List<RailPiece> pieces = new List<RailPiece>();
		private bool[] broken;
		private readonly List<Chunk> chunks = new List<Chunk>();
		private readonly List<Chunk> dirty = new List<Chunk>();
		private readonly Dictionary<long, List<int>> railGrid = new Dictionary<long, List<int>>();
		private readonly Dictionary<EnemyController, CarControllerNew> cars = new Dictionary<EnemyController, CarControllerNew>();

		private Material steelMaterial;
		private Material postMaterial;
		private TrackManager track;
		private int layer;
		private Vector3 carCentre;
		private float carHalfWidth = 2.65f;
		private float carHalfLength = 7.1f;

		private static long Key(int cx, int cz)
		{
			return ((long)cx << 32) ^ (uint)cz;
		}

		internal void Setup(List<RailPiece> rails, TrackManager tm, Vector3 centreInCar, float halfWidth, float halfLength, int gameLayer)
		{
			steelMaterial = Mats.Get(Tex.White, SteelC);
			postMaterial = Mats.Get(Tex.White, PoleC);
			track = tm;
			carCentre = centreInCar;
			carHalfWidth = halfWidth * Body;
			carHalfLength = halfLength * Body;
			layer = gameLayer;
			Debris.Reset(transform);

			pieces.AddRange(rails);
			broken = new bool[pieces.Count];
			for (int i = 0; i < pieces.Count; i++)
			{
				int c = pieces[i].Chunk;
				while (chunks.Count <= c)
				{
					chunks.Add(new Chunk());
				}
				chunks[c].Pieces.Add(i);
				float half = pieces[i].Len * 0.5f;
				Register(i, pieces[i].Pos.x - half, pieces[i].Pos.x + half, pieces[i].Pos.z - half, pieces[i].Pos.z + half);
			}
			foreach (Chunk chunk in chunks)
			{
				chunk.Steel = MakeFilter("steel", steelMaterial);
				chunk.Posts = MakeFilter("posts", postMaterial);
				Rebuild(chunk);
			}
		}

		private void Register(int index, float x0, float x1, float z0, float z1)
		{
			for (int cx = Mathf.FloorToInt(x0 / Cell); cx <= Mathf.FloorToInt(x1 / Cell); cx++)
			{
				for (int cz = Mathf.FloorToInt(z0 / Cell); cz <= Mathf.FloorToInt(z1 / Cell); cz++)
				{
					List<int> list;
					if (!railGrid.TryGetValue(Key(cx, cz), out list))
					{
						list = new List<int>();
						railGrid[Key(cx, cz)] = list;
					}
					if (!list.Contains(index))
					{
						list.Add(index);
					}
				}
			}
		}

		private MeshFilter MakeFilter(string name, Material material)
		{
			GameObject go = new GameObject(name) { layer = layer };
			go.transform.SetParent(transform, false);
			MeshFilter filter = go.AddComponent<MeshFilter>();
			go.AddComponent<MeshRenderer>().sharedMaterial = material;
			return filter;
		}

		private static Quaternion Facing(RailPiece p)
		{
			return Quaternion.LookRotation(Vector3.Cross(p.Along, Vector3.up), Vector3.up);
		}

		private void Rebuild(Chunk chunk)
		{
			Batch steel = new Batch("steel", Tex.White, SteelC, false);
			Batch posts = new Batch("posts", Tex.White, PoleC, false);
			foreach (int i in chunk.Pieces)
			{
				if (broken[i])
				{
					continue;
				}
				RailPiece p = pieces[i];
				Quaternion rot = Facing(p);
				float beam = p.Len - 0.06f;
				steel.Box(p.Pos + rot * new Vector3(0f, 0.85f, 0.1f), new Vector3(beam, 0.5f, 0.08f), rot, 4f);
				steel.Box(p.Pos + rot * new Vector3(0f, 1.6f, 0.1f), new Vector3(beam, 0.5f, 0.08f), rot, 4f);
				for (int k = -1; k <= 1; k++)
				{
					posts.Box(p.Pos + rot * new Vector3(k * (p.Len * 0.5f - 0.5f), -0.45f, 0f), new Vector3(0.18f, 5.1f, 0.18f), rot, 4f);
				}
			}
			Replace(chunk.Steel, steel);
			Replace(chunk.Posts, posts);
			chunk.Dirty = false;
		}

		private static void Replace(MeshFilter filter, Batch batch)
		{
			Mesh old = filter.sharedMesh;
			filter.sharedMesh = batch.Count > 0 ? batch.ToMesh() : null;
			if (old != null)
			{
				Destroy(old);
			}
		}

		private void FixedUpdate()
		{
			if (track == null)
			{
				return;
			}
			if (track.playerCarController != null)
			{
				Test(track.playerCarController);
			}
			IReadOnlyList<EnemyController> enemies = track.Enemies;
			for (int i = 0; i < enemies.Count; i++)
			{
				EnemyController enemy = enemies[i];
				if (enemy == null)
				{
					continue;
				}
				CarControllerNew car;
				if (!cars.TryGetValue(enemy, out car))
				{
					car = enemy.GetComponent<CarControllerNew>();
					cars[enemy] = car;
				}
				Test(car);
			}
			if (dirty.Count > 0)
			{
				foreach (Chunk chunk in dirty)
				{
					Rebuild(chunk);
				}
				dirty.Clear();
			}
		}

		private void Test(CarControllerNew car)
		{
			if (car == null)
			{
				return;
			}
			Rigidbody body = car.rb != null ? car.rb : car.GetComponent<Rigidbody>();
			if (body == null)
			{
				return;
			}
			Transform t = car.transform;
			Vector3 centre = t.TransformPoint(carCentre);
			Vector3 forward = t.forward;
			forward.y = 0f;
			if (forward.sqrMagnitude < 1e-4f)
			{
				return;
			}
			forward.Normalize();
			Vector3 right = new Vector3(forward.z, 0f, -forward.x);

			float reach = carHalfLength + 2f;
			int x0 = Mathf.FloorToInt((centre.x - reach) / Cell);
			int x1 = Mathf.FloorToInt((centre.x + reach) / Cell);
			int z0 = Mathf.FloorToInt((centre.z - reach) / Cell);
			int z1 = Mathf.FloorToInt((centre.z + reach) / Cell);

			int hits = 0;
			Vector3 velocity = body.linearVelocity;
			for (int cx = x0; cx <= x1; cx++)
			{
				for (int cz = z0; cz <= z1; cz++)
				{
					List<int> rails;
					if (!railGrid.TryGetValue(Key(cx, cz), out rails))
					{
						continue;
					}
					for (int n = 0; n < rails.Count; n++)
					{
						int i = rails[n];
						if (broken[i] || !SegmentTouchesCar(pieces[i], centre, forward, right))
						{
							continue;
						}
						broken[i] = true;
						Chunk chunk = chunks[pieces[i].Chunk];
						if (!chunk.Dirty)
						{
							chunk.Dirty = true;
							dirty.Add(chunk);
						}
						Shatter(pieces[i], velocity);
						hits++;
					}
				}
			}
			// Every piece takes the same share of the car's speed, so three hits are heavy and five or six all but stop it; pieces broken
			// in the same step count in full for the first two and a third as much after that (a broadside crossing is one barrier).
			if (hits > 0)
			{
				float keep = 1f - Mathf.Clamp01(Mod.SpeedLossPerPiece.Value);
				float counted = hits <= 2 ? hits : 2f + 0.35f * (hits - 2);
				body.linearVelocity = velocity * Mathf.Pow(keep, counted);
			}
		}

		private bool SegmentTouchesCar(RailPiece p, Vector3 centre, Vector3 forward, Vector3 right)
		{
			Vector3 d = p.Pos - centre;
			if (Mathf.Abs(d.y) > 4f)
			{
				return false;
			}
			float half = p.Len * 0.5f;
			float px = Vector3.Dot(d, right);
			float pz = Vector3.Dot(d, forward);
			float dx = Vector3.Dot(p.Along, right) * half;
			float dz = Vector3.Dot(p.Along, forward) * half;
			if (Mathf.Abs(px) > carHalfWidth + Mathf.Abs(dx) || Mathf.Abs(pz) > carHalfLength + Mathf.Abs(dz))
			{
				return false;
			}
			float t0 = 0f, t1 = 1f;
			float ax = px - dx, az = pz - dz, vx = 2f * dx, vz = 2f * dz;
			return Clip(-vx, ax + carHalfWidth, ref t0, ref t1) && Clip(vx, carHalfWidth - ax, ref t0, ref t1)
				&& Clip(-vz, az + carHalfLength, ref t0, ref t1) && Clip(vz, carHalfLength - az, ref t0, ref t1);
		}

		private static bool Clip(float p, float q, ref float t0, ref float t1)
		{
			if (Mathf.Abs(p) < 1e-6f)
			{
				return q >= 0f;
			}
			float r = q / p;
			if (p < 0f)
			{
				if (r > t1)
				{
					return false;
				}
				if (r > t0)
				{
					t0 = r;
				}
			}
			else
			{
				if (r < t0)
				{
					return false;
				}
				if (r < t1)
				{
					t1 = r;
				}
			}
			return true;
		}

		private void Shatter(RailPiece p, Vector3 carVelocity)
		{
			Quaternion rot = Facing(p);
			float beam = p.Len - 0.06f;
			Debris.Spawn(p.Pos + rot * new Vector3(0f, 0.85f, 0.1f), rot, new Vector3(beam, 0.5f, 0.08f), steelMaterial, carVelocity, 14f, layer);
			Debris.Spawn(p.Pos + rot * new Vector3(0f, 1.6f, 0.1f), rot, new Vector3(beam, 0.5f, 0.08f), steelMaterial, carVelocity, 14f, layer);
			Debris.Spawn(p.Pos + rot * new Vector3(-p.Len * 0.5f + 0.5f, 1.05f, 0f), rot, new Vector3(0.18f, 2.1f, 0.18f), postMaterial, carVelocity, 4f, layer);
			Debris.Spawn(p.Pos + rot * new Vector3(p.Len * 0.5f - 0.5f, 1.05f, 0f), rot, new Vector3(0.18f, 2.1f, 0.18f), postMaterial, carVelocity, 4f, layer);
		}
	}
}
