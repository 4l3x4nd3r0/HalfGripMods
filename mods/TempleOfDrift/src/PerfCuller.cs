using System.Collections.Generic;
using UnityEngine;

namespace TempleOfDrift
{
	/// <summary>
	/// Keeps the work per frame small. A mountain hides its cave: unless the player is in the cave or near a mouth, none of the cave's meshes
	/// or lights are drawn; and deep inside the cave nothing outside is (terrain, roads, buildings, trees). Parts of the cave are also dropped
	/// when they are more than about 330 units away. The map's meshes cast no shadows (except the buildings) and ignore light and reflection
	/// probes.
	/// </summary>
	internal sealed class PerfCuller : MonoBehaviour
	{
		private struct Entry
		{
			public Renderer R;
			public Vector3 Centre;
			public bool Cave;
			public bool Chunked;
		}

		private static readonly List<Entry> entries = new List<Entry>();
		private static readonly List<Light> lights = new List<Light>();
		private const float CaveRange = 330f;

		private TrackManager track;
		private float next;
		private Vector3[] portals;
		internal static int CaveOn, OutdoorOn;
		/// <summary>Debug: draw only the cave (the see-through check renders it against a flat colour).</summary>
		internal static bool ForceHideOutdoor;

		internal static void Reset()
		{
			entries.Clear();
			lights.Clear();
		}

		internal static bool IsCaveName(string n)
		{
			return n.StartsWith("cave ") || n.StartsWith("portal ") || n.StartsWith("mine ") || n.StartsWith("sign ") || n == "short way floor";
		}

		internal static void Register(Renderer r, bool cave, bool chunked)
		{
			entries.Add(new Entry { R = r, Centre = r.bounds.center, Cave = cave, Chunked = chunked });
		}

		internal static void AddLight(Light l)
		{
			lights.Add(l);
		}

		internal void Setup(TrackManager tm)
		{
			track = tm;
			Vector3 a, b;
			a = Data.Main[Data.Index("cave_in")].P;
			b = Data.Main[Data.Index("cave_out")].P;
			portals = new[] { a, b };
		}

		private void Update()
		{
			if (Time.unscaledTime < next || track == null)
			{
				return;
			}
			next = Time.unscaledTime + 0.2f;
			Vector3 p;
			if (!CaveAtmosphere.Observer(track, out p))
			{
				p = new Vector3(1e6f, 0f, 1e6f);   // nobody is looking from anywhere: the outside, as the game has it
			}
			bool all = CaveAtmosphere.Override.HasValue;
			float depth = CaveAtmosphere.PlayerDepth;
			bool nearMouth = false;
			for (int i = 0; i < portals.Length; i++)
			{
				float dx = portals[i].x - p.x, dz = portals[i].z - p.z;
				if (dx * dx + dz * dz < 280f * 280f)
				{
					nearMouth = true;
				}
			}
			bool caveOn = all || ForceHideOutdoor || depth > 0.01f || nearMouth;
			bool outdoorOn = (all || depth < 0.7f || nearMouth) && !ForceHideOutdoor;
			int c = 0, o = 0;
			for (int i = 0; i < entries.Count; i++)
			{
				Entry e = entries[i];
				if (e.R == null)
				{
					continue;
				}
				bool on;
				if (e.Cave)
				{
					on = caveOn;
					if (on && e.Chunked && !all && !ForceHideOutdoor)
					{
						float dx = e.Centre.x - p.x, dz = e.Centre.z - p.z;
						on = dx * dx + dz * dz < CaveRange * CaveRange;
					}
					if (on)
					{
						c++;
					}
				}
				else
				{
					on = outdoorOn;
					if (on)
					{
						o++;
					}
				}
				if (e.R.enabled != on)
				{
					e.R.enabled = on;
				}
			}
			for (int i = 0; i < lights.Count; i++)
			{
				if (lights[i] != null && lights[i].enabled != caveOn)
				{
					lights[i].enabled = caveOn;
				}
			}
			TreeInstancer.Hidden = !outdoorOn;
			CaveOn = c;
			OutdoorOn = o;
		}

		private void OnDestroy()
		{
			TreeInstancer.Hidden = false;
		}
	}
}
