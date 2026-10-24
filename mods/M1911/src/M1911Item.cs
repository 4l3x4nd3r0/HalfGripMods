using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace HalfGrip1911
{
	internal static class M1911Item
	{
		public const string ItemId = "m1911";

		private const string IconFile = "m1911_icon.png";

		// The SMG's sprites, and the file that stands in for each one. The new art keeps the
		// original's pixel size, so the slide and magazine still line up with the body.
		private static readonly Dictionary<string, string> Reskin = new Dictionary<string, string>
		{
			{ "baza smg_0", "m1911_body.png" },
			{ "smg2_0", "m1911_slide.png" },
			{ "smg3 backside_0", "m1911_back.png" },
			{ "smg4_0", "m1911_round.png" },
			{ "fullbullet_0", "m1911_feed.png" },
			{ "amunitie smg_0", "m1911_mag_empty.png" },
			{ "amunitie smg full_0", "m1911_mag_full.png" }
		};

		// The 3D gun you hold is made of three extruded meshes, one per texture. Where the sprite's top left corner
		// and its pixels sit in each mesh was read from the game's own meshes.
		private static readonly Dictionary<string, HeldPart> HeldParts = new Dictionary<string, HeldPart>
		{
			{ "baza smg", new HeldPart("m1911_body.png", 150, 79, new SpriteMesh.Frame { x0 = 506.25f, xDir = -1f, dx = 6.25f, z0 = -100f, dz = 6.25f, yMin = -37.5f, yMax = 37.5f }) },
			{ "smg2", new HeldPart("m1911_slide.png", 83, 17, new SpriteMesh.Frame { x0 = 12.5f, xDir = -1f, dx = 6.25f, z0 = -31.25f, dz = 6.25f, yMin = -6.25f, yMax = 37.5f }) },
			{ "amunitie smg", new HeldPart("m1911_mag_empty.png", 16, 54, new SpriteMesh.Frame { x0 = -668.75f, xDir = -1f, dx = 11.72f, z0 = -319f, dz = 6.25f, yMin = -174.6f, yMax = -124.6f }) }
		};

		private sealed class HeldPart
		{
			public readonly string File;
			public readonly int Width;
			public readonly int Height;
			public readonly SpriteMesh.Frame Frame;

			public HeldPart(string file, int width, int height, SpriteMesh.Frame frame)
			{
				File = file;
				Width = width;
				Height = height;
				Frame = frame;
			}
		}

		private static readonly Dictionary<string, Mesh> meshCache = new Dictionary<string, Mesh>();

		private static readonly Dictionary<string, Material> materialCache = new Dictionary<string, Material>();

		private static GunUpgrade item;

		private static bool dumped;

		public static GunUpgrade Item => item;

		// The game's own SMG and revolver items, which the other custom guns are copied from.
		internal static GunUpgrade BaseSmg;

		internal static GunUpgrade BaseRevolver;

		public static bool IsFitted(Gun gun)
		{
			return item != null && gun != null && gun.Fitted == item;
		}

		/// <summary>Hands the gun to HalfGripTools, which puts it in the item catalog before the game builds its id lookup.</summary>
		public static void Register(HalfGripTools.ModContext ctx)
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
					Mod.Log.LogInfo($"[gun item] name='{g.name}' id='{g.id}' type={g.gunType} tier={g.tier} damage={g.damage} fireRate={g.fireRate} recoil={g.recoil} inaccuracy={g.inaccuracy} capacity={g.capacity} magazines={g.magazines} pellets={g.projectilesPerShot}");
				}
			}

			GunUpgrade smg = guns.FirstOrDefault(g => g.gunType == GunType.SMG_RIFLE);
			if (smg == null)
			{
				Mod.Log.LogError("No SMG gun item found in the catalog, so there is nothing to base the M1911 on.");
				return null;
			}
			GunUpgrade revolver = guns.FirstOrDefault(g => g.gunType == GunType.REVOLVER) ?? smg;
			BaseSmg = smg;
			BaseRevolver = revolver;

			GunUpgrade made = Mod.Ctx.Items.Clone(smg, ItemId);
			// First line is the name; the grey line under it is how the game styles every item's flavor text.
			made.description = "A Light In Shinning Darkness\n<color=#7e7e7e>\"We can't expect God to do all the work.\"</color>";
			made.secondaryDescription = "<color=#7e7e7e>\"I survived because the fire inside me burned brighter than the fire around me.\"\nJoshua Graham</color>";
			made.tier = ItemTier.Legendary;
			// No "rounds per minute" line on the card.
			if (made.forceHide == null)
			{
				made.forceHide = new List<string>();
			}
			made.forceHide.Add("fire rate");
			made.gunType = GunType.SMG_RIFLE;
			// Enabled and unlocked from the start, so shops and race rewards can offer it like the RPG and the Sniper Rifle.
			made.disabled = false;
			made.startsLocked = false;
			made.cost = Mathf.Max(smg.cost, revolver.cost) * 3;

			made.damage = Pick(Mod.Damage.Value, smg.damage, revolver.damage);
			made.recoil = Pick(Mod.Recoil.Value, smg.recoil, revolver.recoil);
			made.inaccuracy = Pick(Mod.Inaccuracy.Value, smg.inaccuracy, revolver.inaccuracy);
			made.fireRate = Mathf.Max(0.5f, Mod.FireRate.Value);
			made.capacity = Mathf.Max(1, Mod.Capacity.Value);
			made.projectilesPerShot = 1;
			made.projectileSpread = 0f;
			made.ammoWord = "";

			// It fires like the revolver: same muzzle flash and screen shake.
			made.muzzleFlash = revolver.muzzleFlash;
			made.shakeIntensity = revolver.shakeIntensity;
			made.shakeLength = revolver.shakeLength;
			made.shakeSharpness = revolver.shakeSharpness;

			Sprite icon = Mod.Ctx.Sprites.LoadSprite(IconFile, smg.image);
			if (icon != null)
			{
				made.image = icon;
			}

			item = made;
			Mod.Log.LogInfo($"Created M1911 from '{smg.name}': damage={made.damage} (SMG {smg.damage}, revolver {revolver.damage}) recoil={made.recoil} inaccuracy={made.inaccuracy} fireRate={made.fireRate} capacity={made.capacity} magazines={made.magazines}");
			return made;
		}

		private static float Pick(float configured, float a, float b)
		{
			return configured > 0f ? configured : (a + b) * 0.5f;
		}

		/// <summary>Called when a race loads with the M1911 equipped.</summary>
		public static void Equip(NAutomaticGun gun)
		{
			gun.fullAuto = false;
			CopyRevolverShot(gun);
			CenterMagazine(gun);
			ReskinSprites();
			ReskinHeldModel(gun);
		}

		// Sounds, tracer and hit effects come from the revolver's gun object.
		internal static void CopyRevolverShot(Gun gun)
		{
			Gun revolver = UnityEngine.Object.FindObjectsByType<Gun>(FindObjectsInactive.Include, FindObjectsSortMode.None)
				.FirstOrDefault(g => g != gun && g.RequiredGunType == GunType.REVOLVER);
			if (revolver == null)
			{
				Mod.Log.LogWarning("No revolver gun in this scene, so the M1911 keeps the SMG's sounds and tracer");
				return;
			}
			gun.shootSounds = revolver.shootSounds;
			gun.shoootSoundVolume = revolver.shoootSoundVolume;
			gun.tracerMaterial = revolver.tracerMaterial;
			gun.tracerWidthMultiplier = revolver.tracerWidthMultiplier;
			gun.tracerLifetime = revolver.tracerLifetime;
			gun.bulletHitEffect = revolver.bulletHitEffect;
			gun.bulletCollisionSounds = revolver.bulletCollisionSounds;
			Mod.Log.LogInfo("M1911 shoots with the revolver's sound and tracer");
		}

		// The SMG's magazine well is left of the middle of the 1911's grip, so move where the magazine lines up and seats
		// 8 pixels (0.8 units) toward the back. The 3D magazine mesh is already built centered on the grip.
		private static void CenterMagazine(NAutomaticGun gun)
		{
			var moved = new HashSet<Transform>();
			foreach (NMagazine magazine in UnityEngine.Object.FindObjectsByType<NMagazine>(FindObjectsInactive.Include, FindObjectsSortMode.None))
			{
				if (magazine.gun != gun)
				{
					continue;
				}
				foreach (Transform point in new[] { magazine.insertPoint, magazine.alignPoint })
				{
					if (point != null && moved.Add(point))
					{
						Vector3 local = point.localPosition;
						local.x += 0.8f;
						point.localPosition = local;
					}
				}
			}
			Mod.Log.LogInfo("Moved " + moved.Count + " magazine seat points toward the grip's center");
		}

		// The flat sprites you see while the gun sits in the car.
		private static void ReskinSprites()
		{
			Mod.Log.LogInfo("Gun is fitted, swapped " + Mod.Ctx.Sprites.Reskin(Reskin) + " sprites");
		}

		// The 3D gun you hold while aiming.
		private static void ReskinHeldModel(Gun gun)
		{
			if (gun.aimModel == null)
			{
				Mod.Log.LogWarning("The SMG has no aim model, so the held M1911 keeps the SMG's shape");
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
				if (!HeldParts.TryGetValue(textureName, out HeldPart part))
				{
					continue;
				}
				// The slide texture is shared with the SMG's small sight blocks; those would float over the new slide.
				if (textureName == "smg2" && filter.sharedMesh.bounds.size.x < 100f)
				{
					renderer.enabled = false;
					continue;
				}
				Texture2D texture = Mod.Ctx.Sprites.LoadTexture(part.File);
				if (texture == null || texture.width != part.Width || texture.height != part.Height)
				{
					Mod.Log.LogWarning("Held part " + textureName + ": art is missing or the wrong size");
					continue;
				}
				if (!meshCache.TryGetValue(part.File, out Mesh mesh) || mesh == null)
				{
					mesh = SpriteMesh.Build(texture, part.Frame, "M1911 " + part.File);
					meshCache[part.File] = mesh;
				}
				if (!materialCache.TryGetValue(part.File, out Material material) || material == null)
				{
					material = new Material(original) { name = "M1911 " + part.File, hideFlags = HideFlags.HideAndDontSave };
					material.SetTexture("_BaseMap", texture);
					material.SetTexture("_MainTex", texture);
					materialCache[part.File] = material;
				}
				filter.sharedMesh = mesh;
				renderer.sharedMaterial = material;
				swapped++;
			}
			Mod.Log.LogInfo("Held M1911 model: replaced " + swapped + " meshes");
		}
	}
}
