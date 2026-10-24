using HalfGripTools;
using UnityEngine;

namespace HalfGripRpg
{
	/// <summary>The RPG's 3D parts, extruded from its art: the launcher and rocket you hold, and the rocket in flight.</summary>
	internal static class RpgModels
	{
		// rpg_body.png is the launcher (258x62) with its tube on pixel row 12.5; rpg_rocket_side.png is the rocket (97x25), warhead first.
		private const int LauncherWidth = 258;

		private const int LauncherHeight = 62;

		private const float LauncherAxisRow = 12.5f;

		private const int RocketWidth = 97;

		private const int RocketHeight = 25;

		// The fat part of the rocket, the warhead, is its first 58 columns; after that it is the thin shaft.
		private const int WarheadColumns = 58;

		private const float RocketAxisRow = 12.5f;

		// How long the rocket in flight is, in metres.
		public const float FlyingLength = 4.2f;

		/// <summary>A copy of this is what the 3D parts are drawn with: the game's own held-gun material, which is known to render.</summary>
		public static Material GunMaterial;

		private static Mesh flyingMesh;

		public static void AttachFlyingRocket(Transform parent)
		{
			Texture2D texture = Mod.Ctx.Sprites.LoadTexture("rpg_rocket_side.png");
			if (texture == null)
			{
				return;
			}
			if (flyingMesh == null)
			{
				float pixel = FlyingLength / RocketWidth;
				flyingMesh = VoxelMesh.Build(texture, pixel, RocketHeight * pixel * 0.7f, RocketAxisRow, "RPG rocket");
			}
			// Drawn unlit, so it is as bright in the air as it is on the art.
			GameObject model = VoxelMesh.MakeObject("model", flyingMesh, texture, null, parent, 0);
			if (model.GetComponent<MeshRenderer>().sharedMaterial == null)
			{
				model.GetComponent<MeshRenderer>().sharedMaterial = GunMaterial;
			}
		}

		/// <summary>
		/// The launcher you hold, as a child of the aim model. <paramref name="muzzle"/> is where its tube ends, in the aim model's
		/// space (forward is +z); <paramref name="length"/> is how long it is. Also makes the rocket that stands out of the tube
		/// while it is loaded. Returns the warhead object so it can be shown and hidden.
		/// </summary>
		public static GameObject BuildHeld(Transform aim, Vector3 muzzle, float length, int layer, out GameObject warhead)
		{
			warhead = null;
			Texture2D body = Mod.Ctx.Sprites.LoadTexture("rpg_body.png");
			Texture2D rocket = Mod.Ctx.Sprites.LoadTexture("rpg_rocket_side.png");
			if (body == null)
			{
				return null;
			}

			float pixel = length / LauncherWidth;
			Mesh launcherMesh = VoxelMesh.Build(body, pixel, 14f * pixel, LauncherAxisRow, "RPG launcher");
			GameObject launcher = VoxelMesh.MakeObject("RpgLauncherModel", launcherMesh, body, GunMaterial, aim, layer);
			launcher.transform.localPosition = muzzle;

			if (rocket != null)
			{
				// The rocket is half as long as the launcher. Only the warhead, and a stub of neck, is built: the rest of it is inside the
				// tube, and a long thin neck out in front would only read as more tube.
				float rocketPixel = length * 0.5f / RocketWidth;
				Texture2D warheadOnly = new Texture2D(WarheadColumns, RocketHeight, TextureFormat.RGBA32, false)
				{
					filterMode = FilterMode.Point,
					wrapMode = TextureWrapMode.Clamp,
					hideFlags = HideFlags.HideAndDontSave
				};
				warheadOnly.SetPixels(rocket.GetPixels(0, 0, WarheadColumns, RocketHeight));
				warheadOnly.Apply();
				Mesh rocketMesh = VoxelMesh.Build(warheadOnly, rocketPixel, RocketHeight * rocketPixel * 0.55f, RocketAxisRow, "RPG held warhead");
				warhead = VoxelMesh.MakeObject("RpgHeldRocket", rocketMesh, warheadOnly, GunMaterial, aim, layer);
				// Its back end, the cut one, sits just inside the muzzle.
				warhead.transform.localPosition = muzzle + Vector3.forward * (WarheadColumns * rocketPixel * 0.85f);
				warhead.SetActive(false);
			}
			return launcher;
		}
	}
}
