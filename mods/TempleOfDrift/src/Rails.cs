using HalfGripTools;
using HalfGripTools.Meshes;
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
		public int Prev;        // index of the piece before this one in the same run, or -1
		public int Next;        // index of the piece after it, or -1
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
		private float[] bend;                // 0 straight .. 1 badly bent
		private int[] stamp;                 // the step a piece was last collected in, so a piece in two grid cells is handled once
		private int stampNow;
		private readonly List<int> touched = new List<int>();
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
			bend = new float[pieces.Count];
			stamp = new int[pieces.Count];
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
				if (bend[i] > 0.02f)
				{
					BentPiece(steel, posts, p, rot, beam, bend[i]);
					continue;
				}
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

		/// <summary>
		/// A damaged piece: each beam is two halves that meet in a point pushed away from the road (a bow), the middle post leans out with it
		/// and the end posts lean a little. The ends stay where they were, so the pieces either side still meet it.
		/// </summary>
		private static void BentPiece(Batch steel, Batch posts, RailPiece p, Quaternion rot, float beam, float b)
		{
			float bow = b * 3.4f;
			BentBeam(steel, p, 0.85f, beam, bow);
			BentBeam(steel, p, 1.6f, beam, bow);
			Vector3 axis = Vector3.Cross(Vector3.up, p.Out);
			for (int k = -1; k <= 1; k++)
			{
				Vector3 foot = p.Pos + p.Along * (k * (p.Len * 0.5f - 0.5f));
				if (k == 0)
				{
					foot += p.Out * bow;
				}
				Quaternion lean = Quaternion.AngleAxis(b * (k == 0 ? 32f : 14f), axis);
				Vector3 centre = foot + lean * (rot * new Vector3(0f, -0.45f, 0f));
				posts.Box(centre, new Vector3(0.18f, 5.1f, 0.18f), lean * rot, 4f);
			}
		}

		private static void BentBeam(Batch steel, RailPiece p, float height, float beam, float bow)
		{
			Vector3 lift = Vector3.up * height;
			Vector3 e0 = p.Pos + lift - p.Along * (beam * 0.5f);
			Vector3 e1 = p.Pos + lift + p.Along * (beam * 0.5f);
			Vector3 mid = p.Pos + lift + p.Out * bow;
			BeamBetween(steel, e0, mid);
			BeamBetween(steel, mid, e1);
		}

		private static void BeamBetween(Batch steel, Vector3 a, Vector3 b)
		{
			Vector3 d = b - a;
			float len = d.magnitude;
			if (len < 0.01f)
			{
				return;
			}
			Quaternion r = Quaternion.LookRotation(Vector3.Cross(d / len, Vector3.up), Vector3.up);
			steel.Box((a + b) * 0.5f, new Vector3(len + 0.04f, 0.5f, 0.08f), r, 4f);
		}

		private void MarkDirty(int piece)
		{
			Chunk chunk = chunks[pieces[piece].Chunk];
			if (!chunk.Dirty)
			{
				chunk.Dirty = true;
				dirty.Add(chunk);
			}
		}

		private void AddBend(int piece, float amount, bool atLeast)
		{
			float now = bend[piece];
			float next = atLeast ? Mathf.Max(now, amount) : Mathf.Min(1f, now + amount);
			if (next > now + 0.001f)
			{
				bend[piece] = next;
				MarkDirty(piece);
			}
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
			stampNow++;
			touched.Clear();
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
						if (broken[i] || stamp[i] == stampNow || !SegmentTouchesCar(pieces[i], centre, forward, right))
						{
							continue;
						}
						stamp[i] = stampNow;
						touched.Add(i);
					}
				}
			}
			if (touched.Count == 0)
			{
				return;
			}
			Vector3 v = velocity;
			hits = ResolveRails(car, body, centre, forward, right, ref v);
			if (v != velocity)
			{
				body.linearVelocity = v;
			}
		}

		private static GuardrailTuning tuning;

		/// <summary>The guardrail rule's numbers from this mod's settings (see <c>GuardrailRules</c> in HalfGripTools).</summary>
		private static GuardrailTuning Tuning()
		{
			if (tuning == null)
			{
				tuning = new GuardrailTuning();
			}
			tuning.GlanceAngle = Mod.GlanceAngle.Value;
			tuning.BendMinInto = Mod.BendMinSpeed.Value;
			tuning.BreakInto = Mod.BreakSpeed.Value;
			tuning.BendLossDash = Mod.BendSpeedLoss.Value;
			tuning.Bounce = Mod.RailBounce.Value;
			tuning.BreakKeep = Mod.BreakSpeedKept.Value;
			return tuning;
		}

		/// <summary>The number on the dash is the car's speed in game units a second times this (<c>SpeedDashboard.SpeedToDisplay</c>).</summary>
		private const float Dash = 1.8f;

		/// <summary>The direction from the car to the rail piece, across the road: the piece's outward normal, turned round for a car that is already outside it.</summary>
		private static Vector3 Toward(RailPiece p, Vector3 centre)
		{
			return Vector3.Dot(centre - p.Pos, p.Out) > 0f ? -p.Out : p.Out;
		}

		/// <summary>
		/// What the touched pieces do with a hit, from the car's speed and the angle it meets the rail at (<c>GuardrailRules</c> in HalfGripTools):
		/// under GlanceAngle the car slides along the rail untouched; above it, and into the rail faster than BendMinSpeed, the rail bends and the
		/// car loses a few dash units; from BreakSpeed (speed times the sine of the angle) the pieces break, with the two either side bent, and the
		/// car loses speed and takes crash damage like it does for a tree. Returns how many pieces broke.
		/// </summary>
		private int ResolveRails(CarControllerNew car, Rigidbody body, Vector3 centre, Vector3 forward, Vector3 right, ref Vector3 v)
		{
			GuardrailTuning tuning = Tuning();
			Vector3 v0 = v;
			float into = 0f;
			for (int n = 0; n < touched.Count; n++)
			{
				into = Mathf.Max(into, Vector3.Dot(v0, Toward(pieces[touched[n]], centre)) * Dash);
			}
			GuardrailVerdict verdict = GuardrailRules.Judge(v0, into, tuning);

			if (verdict.Outcome == GuardrailOutcome.Break)
			{
				for (int n = 0; n < touched.Count; n++)
				{
					int i = touched[n];
					broken[i] = true;
					MarkDirty(i);
					Shatter(pieces[i], v0);
				}
				for (int n = 0; n < touched.Count; n++)
				{
					BendAround(touched[n]);
				}
				v = GuardrailRules.AfterBreak(car, v0, into, tuning, out float damage);
				LogHit(car, into, "break at " + verdict.Angle.ToString("0") + " deg, " + damage.ToString("0") + " damage", touched.Count, v0, v);
				return touched.Count;
			}

			// The rail holds the car in: the speed into it is taken away, all of it for a scrape, all but a soft push back for a bend.
			float bounce = verdict.Outcome == GuardrailOutcome.Bend ? tuning.Bounce : 0f;
			Vector3 shift = Vector3.zero;
			for (int n = 0; n < touched.Count; n++)
			{
				RailPiece p = pieces[touched[n]];
				Vector3 toward = Toward(p, centre);
				float closing = Vector3.Dot(v, toward);
				if (closing > 0f)
				{
					v -= toward * closing * (1f + bounce);
				}
				float pen = Penetration(p, centre + shift, forward, right, toward);
				if (pen > 0f)
				{
					shift -= toward * (pen + 0.05f);
				}
			}
			if (verdict.Outcome == GuardrailOutcome.Bend)
			{
				for (int n = 0; n < touched.Count; n++)
				{
					AddBend(touched[n], verdict.BendAmount, false);
				}
				for (int n = 0; n < touched.Count; n++)
				{
					BendBeside(touched[n], verdict.BendAmount);
				}
				v = GuardrailRules.AfterBend(car, v, tuning);
			}
			if (shift.sqrMagnitude > 1e-8f)
			{
				body.position = body.position + Vector3.ClampMagnitude(shift, 2.5f);
			}
			if (into > 5f)
			{
				LogHit(car, into, (verdict.Outcome == GuardrailOutcome.Bend ? "bend" : "slide") + " at " + verdict.Angle.ToString("0") + " deg", touched.Count, v0, v);
			}
			return 0;
		}

		/// <summary>How far the car's footprint reaches past the rail line, measured from its corners that are along the piece.</summary>
		private float Penetration(RailPiece p, Vector3 centre, Vector3 forward, Vector3 right, Vector3 toward)
		{
			float best = 0f;
			float reach = p.Len * 0.5f + 0.3f;
			for (int sx = -1; sx <= 1; sx += 2)
			{
				for (int sz = -1; sz <= 1; sz += 2)
				{
					Vector3 d = centre + right * (sx * carHalfWidth) + forward * (sz * carHalfLength) - p.Pos;
					if (Mathf.Abs(Vector3.Dot(d, p.Along)) <= reach)
					{
						best = Mathf.Max(best, Vector3.Dot(d, toward));
					}
				}
			}
			return best;
		}

		/// <summary>A bend spreads along the rail: the two pieces either side bow half as much and then a quarter as much, so the dent has a shape.</summary>
		private void BendBeside(int piece, float amount)
		{
			for (int dir = 0; dir < 2; dir++)
			{
				int n = dir == 0 ? pieces[piece].Prev : pieces[piece].Next;
				for (int depth = 0; depth < 2 && n >= 0; depth++)
				{
					if (!broken[n])
					{
						AddBend(n, amount * (depth == 0 ? 0.55f : 0.28f), true);
					}
					n = dir == 0 ? pieces[n].Prev : pieces[n].Next;
				}
			}
		}

		/// <summary>The two pieces either side of a gap are bent, the nearer one more.</summary>
		private void BendAround(int piece)
		{
			for (int dir = 0; dir < 2; dir++)
			{
				int n = dir == 0 ? pieces[piece].Prev : pieces[piece].Next;
				int depth = 0;
				while (n >= 0 && depth < 2)
				{
					if (!broken[n])
					{
						AddBend(n, depth == 0 ? 0.8f : 0.45f, true);
						depth++;
					}
					n = dir == 0 ? pieces[n].Prev : pieces[n].Next;
				}
			}
		}

		private static void LogHit(CarControllerNew car, float impact, string what, int count, Vector3 before, Vector3 after)
		{
			if (Mod.LogRailHits != null && Mod.LogRailHits.Value)
			{
				Mod.Log.LogInfo("Rail " + what + ": " + car.name + " into the rail at " + impact.ToString("0") + " on the dash, " + count + " piece(s), speed "
					+ (before.magnitude * Dash).ToString("0") + " -> " + (after.magnitude * Dash).ToString("0"));
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
