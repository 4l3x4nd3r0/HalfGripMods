using System.Collections.Generic;
using System.Linq;
using HalfGripTools;
using UnityEngine;

namespace HalfGripMinigun
{
	/// <summary>
	/// The Minigun as an item, cloned from the game's SMG item. It rides on the SMG's gun object like the M1911 does: the remote
	/// takes the place of the SMG's sprites and held model, and <see cref="MiniHeat"/> and the patches change how it fires.
	/// </summary>
	internal static class MinigunItem
	{
		public const string ItemId = "minigun.gun";

		private const string IconFile = "minigun_icon.png";

		// The SMG's sprites, and the art that stands in for each. The remote keeps the SMG body's pixel size so the held model lines up;
		// every other part is left empty (the magazine is gone: heat takes its place).
		private static readonly Dictionary<string, string> Reskin = new Dictionary<string, string>
		{
			{ "baza smg_0", "minigun_body.png" },
			{ "smg2_0", "minigun_clear_slide.png" },
			{ "smg3 backside_0", "minigun_clear_back.png" },
			{ "smg4_0", "minigun_clear_round.png" },
			{ "fullbullet_0", "minigun_clear_feed.png" },
			{ "amunitie smg_0", "minigun_clear_round.png" },
			{ "amunitie smg full_0", "minigun_clear_round.png" }
		};

		// Where the remote's pixels sit in the held model's body mesh (the SMG body is 150x79; frame read off the game's own mesh), made
		// smaller about the middle of that box by HeldScale so the remote in your hand does not reach into the car's pillar.
		private static SpriteMesh.Frame BodyFrame()
		{
			float s = Mathf.Clamp(Mod.HeldScale.Value, 0.2f, 1.5f);
			const float dx = 6.25f;
			const float centreX = 506.25f - 150f * dx * 0.5f;
			const float centreZ = -100f + 79f * dx * 0.5f;
			return new SpriteMesh.Frame { x0 = centreX + 150f * dx * s * 0.5f, xDir = -1f, dx = dx * s, z0 = centreZ - 79f * dx * s * 0.5f, dz = dx * s, yMin = -37.5f * s, yMax = 37.5f * s };
		}

		private static GunUpgrade item;

		private static bool dumped;

		private static Mesh bodyMesh;

		private static Material bodyMaterial;

		public static GunUpgrade Item => item;

		public static bool IsFitted(Gun gun)
		{
			return item != null && gun != null && gun.Fitted == item;
		}

		public static void Register(ModContext ctx)
		{
			ctx.Items.Add(ItemId, Create);
		}

		private static GunUpgrade Create(ItemCatalog catalog)
		{
			List<GunUpgrade> guns = catalog.items.OfType<GunUpgrade>().Where(g => g != null).ToList();
			if (Mod.DumpInfo.Value && !dumped)
			{
				dumped = true;
				foreach (GunUpgrade g in guns)
				{
					Mod.Log.LogInfo($"[gun item] name='{g.name}' id='{g.id}' type={g.gunType} tier={g.tier} cost={g.cost} damage={g.damage} fireRate={g.fireRate} recoil={g.recoil} capacity={g.capacity} magazines={g.magazines}");
				}
			}
			GunUpgrade smg = guns.FirstOrDefault(g => g.gunType == GunType.SMG_RIFLE);
			if (smg == null)
			{
				Mod.Log.LogError("No SMG gun item found in the catalog, so there is nothing to base the Minigun on.");
				return null;
			}

			GunUpgrade made = Mod.Ctx.Items.Clone(smg, ItemId);
			made.name = "Minigun";
			string[] rows =
			{
				"Unlimited ammo, limited by heat",
				"Overheat: locked for " + Mathf.RoundToInt(Mod.OverheatPenalty.Value) + " s. Ammo boxes cool it"
			};
			made.description = "Minigun\n<color=#7e7e7e>It costs four hundred thousand dollars to fire this weapon for twelve seconds.</color>";
			foreach (string row in rows)
			{
				made.description += "\n<color=#6fb7b4>" + row + "</color>";
			}
			made.secondaryDescription = "";
			made.gunType = GunType.SMG_RIFLE;
			made.tier = ItemTier.Legendary;
			made.disabled = false;
			made.startsLocked = false;
			made.cost = Mathf.Max(1, Mod.Cost.Value);

			made.damage = Mathf.Max(0.1f, Mod.Damage.Value);
			made.projectilesPerShot = 1;
			made.projectileSpread = 0f;
			made.inaccuracy = smg.inaccuracy;
			made.recoil = smg.recoil * 0.4f;
			made.fireRate = Mathf.Max(1f, Mod.RoundsPerSecond.Value);
			made.capacity = 1;
			// The item's magazine count is the size of the heat meter.
			made.magazines = Mathf.Max(1, Mod.Magazines.Value);
			made.ammoWord = "";
			made.shakeIntensity = smg.shakeIntensity * 0.4f;
			made.shakeLength = smg.shakeLength;
			made.shakeSharpness = smg.shakeSharpness;
			if (made.forceHide == null)
			{
				made.forceHide = new List<string>();
			}
			made.forceHide.Add("fire rate");

			Sprite icon = Mod.Ctx.Sprites.LoadSprite(IconFile, smg.image);
			if (icon != null)
			{
				made.image = icon;
			}
			Mod.Log.LogInfo($"Created Minigun from '{smg.name}': damage={made.damage} rate={made.fireRate}/s heat meter={made.magazines} magazines, cost={made.cost}");
			item = made;
			return made;
		}

		/// <summary>Called as the SMG's gun starts with the Minigun fitted.</summary>
		public static void Equip(NAutomaticGun gun)
		{
			gun.fullAuto = true;
			gun.shoootSoundVolume *= 0.55f;
			Mod.Log.LogInfo("Minigun is fitted, swapped " + Mod.Ctx.Sprites.Reskin(Reskin) + " sprites");
			ReskinHeldModel(gun);
			MoveRemote(gun);
			if (gun.GetComponent<MiniHeat>() == null)
			{
				gun.gameObject.AddComponent<MiniHeat>().Setup(gun);
			}
		}

		// The SMG hangs on the roof lining right by the rear-view mirror. The remote is moved so it sits between the mirror and the
		// leaderboard: a RemotePlacer nudges it, a frame at a time, with the live interior camera until its centre is in the middle of the
		// gap. (A one-shot placement at start measured with the camera before its field of view and the window's shape had settled, and
		// the remote ended up off the screen.) The gun's own parent object is moved, which takes the body, the slide and the sprites with
		// it, and the two grabbable parts are told their new rest pose when it is done.
		private static void MoveRemote(NAutomaticGun gun)
		{
			if (gun.body == null || gun.body.transform.parent == null)
			{
				return;
			}
			if (Mod.RemoteAuto.Value)
			{
				gun.gameObject.AddComponent<RemotePlacer>().Begin(gun);
				return;
			}
			Shift(gun);
		}

		// The fallback: along the interior camera's right by RemoteShift.
		internal static void Shift(NAutomaticGun gun)
		{
			float shift = Mod.RemoteShift.Value;
			Transform holder = gun.body.transform.parent;
			if (Mathf.Abs(shift) >= 0.001f)
			{
				GameObject eye = GameObject.Find("InteriorCamera");
				Vector3 right = eye != null ? eye.transform.right : Vector3.right;
				Vector3 before = holder.position;
				holder.position = before + right * shift;
				Mod.Log.LogInfo($"Remote placed by RemoteShift {shift}: {before} to {holder.position}");
			}
			Rest(gun);
		}

		// Tells the grabbable parts where the remote now rests.
		internal static void Rest(NAutomaticGun gun)
		{
			gun.body.ResetRestPose(gun.body.transform.localPosition, gun.body.transform.localRotation);
			if (gun.slide != null)
			{
				gun.slide.ResetRestPose(gun.slide.transform.localPosition, gun.slide.transform.localRotation);
			}
		}

		internal const float RemoteArtShare = 96f / 150f;

		// The mirror's right edge, the leaderboard's left edge and the remote's span, all as 0..1 of the interior camera's width.
		internal static bool Measure(Camera cam, NAutomaticGun gun, out float mirrorRight, out float boardLeft, out float bodyLeft, out float bodyRight)
		{
			mirrorRight = boardLeft = bodyLeft = bodyRight = 0f;
			var mirrorRenderers = new List<Renderer>();
			foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
			{
				if (t.name == "RearViewMirror")
				{
					mirrorRenderers.AddRange(t.GetComponentsInChildren<Renderer>(true));
				}
			}
			// The board is the text the leaderboard writes into (StatusInfo only holds a reference to it; the component itself may sit
			// anywhere), else whatever renderers StatusInfo has under it.
			StatusInfo board = Object.FindFirstObjectByType<StatusInfo>(FindObjectsInactive.Include);
			var boardRenderers = new List<Renderer>();
			if (board != null)
			{
				var text = HarmonyLib.Traverse.Create(board).Field("statusText").GetValue<Component>();
				if (text != null)
				{
					boardRenderers.AddRange(text.GetComponentsInChildren<Renderer>(true));
				}
				if (boardRenderers.Count == 0)
				{
					boardRenderers.AddRange(board.GetComponentsInChildren<Renderer>(true));
				}
			}
			if (!Span(cam, mirrorRenderers, out float mirrorLeft, out mirrorRight) || !Span(cam, boardRenderers, out boardLeft, out float boardRight)
				|| !Span(cam, gun.body.GetComponentsInChildren<Renderer>(true), out bodyLeft, out bodyRight))
			{
				return false;
			}
			return mirrorRight > 0f && boardLeft < 1f && boardLeft > mirrorRight;
		}

		// The horizontal extent of some renderers on the camera's screen, as 0..1 of its width.
		internal static bool Span(Camera cam, IEnumerable<Renderer> renderers, out float lo, out float hi)
		{
			lo = float.MaxValue;
			hi = float.MinValue;
			bool any = false;
			foreach (Renderer renderer in renderers)
			{
				if (renderer == null)
				{
					continue;
				}
				Bounds b = renderer.bounds;
				for (int i = 0; i < 8; i++)
				{
					Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
					Vector3 v = cam.WorldToViewportPoint(corner);
					if (v.z <= 0f)
					{
						continue;
					}
					lo = Mathf.Min(lo, v.x);
					hi = Mathf.Max(hi, v.x);
					any = true;
				}
			}
			return any;
		}

		// The remote you hold while aiming: the body mesh is rebuilt from the remote's pixels; the slide and the magazine are hidden.
		private static void ReskinHeldModel(Gun gun)
		{
			if (gun.aimModel == null)
			{
				Mod.Log.LogWarning("The SMG has no aim model, so the held Minigun keeps the SMG's shape");
				return;
			}
			int swapped = 0;
			foreach (MeshRenderer renderer in gun.aimModel.GetComponentsInChildren<MeshRenderer>(true))
			{
				MeshFilter filter = renderer.GetComponent<MeshFilter>();
				Material original = renderer.sharedMaterial;
				if (filter == null || filter.sharedMesh == null || original == null || original.mainTexture == null)
				{
					continue;
				}
				string textureName = original.mainTexture.name;
				if (Mod.DumpInfo.Value)
				{
					Mod.Log.LogInfo($"[held] '{renderer.name}' mesh='{filter.sharedMesh.name}' size={filter.sharedMesh.bounds.size} texture='{textureName}'");
				}
				if (textureName == "smg2" || textureName == "amunitie smg")
				{
					renderer.enabled = false;
					continue;
				}
				if (textureName != "baza smg")
				{
					continue;
				}
				Texture2D texture = Mod.Ctx.Sprites.LoadTexture("minigun_body.png");
				if (texture == null || texture.width != 150 || texture.height != 79)
				{
					Mod.Log.LogWarning("minigun_body.png is missing or not 150x79, so the held Minigun keeps the SMG's body");
					continue;
				}
				if (bodyMesh == null)
				{
					bodyMesh = SpriteMesh.Build(texture, BodyFrame(), "Minigun remote");
				}
				if (bodyMaterial == null)
				{
					bodyMaterial = new Material(original) { name = "Minigun remote", hideFlags = HideFlags.HideAndDontSave };
					bodyMaterial.SetTexture("_BaseMap", texture);
					bodyMaterial.SetTexture("_MainTex", texture);
				}
				filter.sharedMesh = bodyMesh;
				renderer.sharedMaterial = bodyMaterial;
				swapped++;
			}
			Mod.Log.LogInfo("Held Minigun remote: replaced " + swapped + " meshes");
		}
	}
}
